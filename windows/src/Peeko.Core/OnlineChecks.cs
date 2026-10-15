using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Peeko.Core;

/// <summary>
/// Fetches URLs at most once per interval, with ETag / Last-Modified, and remembers the last body
/// and the last problem. Port of the macOS CachedFetcher.
/// </summary>
public sealed class CachedFetcher
{
    private sealed class Entry
    {
        public DateTimeOffset FetchedAt = DateTimeOffset.MinValue;
        public byte[]? Body;
        public string? ETag;
        public string? LastModified;
        public string? Problem;
    }

    private readonly IHttp _http;
    private readonly Dictionary<string, Entry> _entries = new();
    private readonly object _gate = new();

    public CachedFetcher(IHttp http) => _http = http;

    public async Task<byte[]?> BodyAsync(HttpRequest original, TimeSpan every, DateTimeOffset now)
    {
        var key = original.CacheKey;
        Entry entry;
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out entry!)) _entries[key] = entry = new Entry();
            if (now >= entry.FetchedAt && now - entry.FetchedAt < every) return entry.Body;
            // Claim the slot before awaiting so concurrent callers reuse the cache.
            entry.FetchedAt = now;
        }

        var headers = new Dictionary<string, string>(original.Headers ?? new Dictionary<string, string>())
        {
            ["User-Agent"] = "Peeko-App (+https://peeko.stack42.tech)",
        };
        string? etag, modified;
        lock (_gate) { etag = entry.ETag; modified = entry.LastModified; }
        if (etag is not null) headers["If-None-Match"] = etag;
        if (modified is not null) headers["If-Modified-Since"] = modified;

        try
        {
            var result = await _http.SendAsync(original with { Headers = headers }).ConfigureAwait(false);
            lock (_gate)
            {
                switch (result.Status)
                {
                    case 200:
                        entry.Body = result.Body;
                        entry.ETag = result.Header("ETag");
                        entry.LastModified = result.Header("Last-Modified");
                        entry.Problem = null;
                        break;
                    case 304:
                        entry.Problem = null;
                        break;
                    default:
                        entry.Problem = $"{original.Host} answered {result.Status}";
                        break;
                }
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            lock (_gate) entry.Problem = $"can't reach {original.Host} · offline or a firewall?";
        }
        lock (_gate) return entry.Body;
    }

    public string? ProblemFor(HttpRequest request)
    {
        lock (_gate) return _entries.TryGetValue(request.CacheKey, out var e) ? e.Problem : null;
    }
}

public sealed record GitHubEvent(string Type, DateTimeOffset CreatedAt);

/// <summary>
/// A user's public events at most once per poll interval, reusing the ETag so unchanged replies are
/// body-less 304s. Unauthenticated 304s still count against GitHub's 60/hour limit; a 10-minute
/// poll keeps us at 6/hour.
/// </summary>
public sealed class GitHubEventsClient
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(600);
    private static readonly Regex UserName = new("^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})$");

    private sealed class Entry
    {
        public string? ETag;
        public List<GitHubEvent>? Events;
        public DateTimeOffset FetchedAt = DateTimeOffset.MinValue;
    }

    private readonly IHttp _http;
    private readonly Dictionary<string, Entry> _entries = new();
    private readonly Dictionary<string, string?> _problems = new();
    private readonly object _gate = new();
    public int RequestCount { get; private set; }

    public GitHubEventsClient(IHttp http) => _http = http;

    public static bool IsValidUsername(string name) => UserName.IsMatch(name);

    /// <summary>null until the first successful fetch for this user.</summary>
    public async Task<List<GitHubEvent>?> EventsAsync(string username, DateTimeOffset now)
    {
        var key = username.ToLowerInvariant();
        Entry entry;
        string? etag;
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out entry!)) _entries[key] = entry = new Entry();
            if (now - entry.FetchedAt < PollInterval) return entry.Events;
            if (!IsValidUsername(username)) return null;
            entry.FetchedAt = now;
            RequestCount++;
            etag = entry.ETag;
        }

        var headers = new Dictionary<string, string>
        {
            ["User-Agent"] = "Peeko-App",
            ["Accept"] = "application/vnd.github+json",
            ["X-GitHub-Api-Version"] = "2022-11-28",
        };
        if (etag is not null) headers["If-None-Match"] = etag;
        var request = new HttpRequest($"https://api.github.com/users/{username}/events/public?per_page=100", Headers: headers);

        try
        {
            var result = await _http.SendAsync(request).ConfigureAwait(false);
            lock (_gate)
            {
                if (result.Status == 200)
                {
                    entry.Events = ParseEvents(result.Body);
                    entry.ETag = result.Header("ETag");
                    _problems[key] = null;
                }
                else
                {
                    // 304 keeps the cached events; anything else is retried at the next poll.
                    _problems[key] = ProblemForStatus(result.Status);
                }
            }
        }
        catch (JsonException)
        {
            lock (_gate) _problems[key] = "GitHub sent something unexpected";
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            lock (_gate) _problems[key] = "can't reach GitHub · offline or a firewall?";
        }
        lock (_gate) return entry.Events;
    }

    /// <summary>What went wrong with the last request for this user, in words for the UI.</summary>
    public string? ProblemFor(string username)
    {
        lock (_gate) return _problems.GetValueOrDefault(username.ToLowerInvariant());
    }

    public static string? ProblemForStatus(int status) => status switch
    {
        200 or 304 => null,
        404 => "GitHub doesn't know that username",
        403 or 429 => "GitHub's rate limit · trying again soon",
        _ => $"GitHub answered {status} · trying again soon",
    };

    public static List<GitHubEvent> ParseEvents(byte[] body)
    {
        using var doc = JsonDocument.Parse(body);
        var list = new List<GitHubEvent>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var type = e.GetProperty("type").GetString() ?? "";
            var at = IsoDate.Parse(e.GetProperty("created_at").GetString()!);
            list.Add(new GitHubEvent(type, at));
        }
        return list;
    }
}

/// <summary>Request builders and response parsers for the fetch-based checks.</summary>
public static class OnlineChecks
{
    public static readonly TimeSpan CodeforcesInterval = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan LeetCodeInterval = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan FeedInterval = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan DuolingoInterval = TimeSpan.FromMinutes(15);

    private static readonly Regex CodeforcesHandle = new(@"^[A-Za-z0-9_.-]{1,24}$");
    private static readonly Regex LeetCodeName = new(@"^[A-Za-z0-9_-]{1,50}$");
    private static readonly Regex DuolingoName = new(@"^[A-Za-z0-9_.-]{1,64}$");

    public static HttpRequest? CodeforcesRequest(string handle)
    {
        var name = handle.Trim();
        return CodeforcesHandle.IsMatch(name)
            ? new HttpRequest($"https://codeforces.com/api/user.status?handle={name}&from=1&count=50")
            : null;
    }

    public static HttpRequest? LeetCodeRequest(string username)
    {
        var name = username.Trim();
        if (!LeetCodeName.IsMatch(name)) return null;
        const string query = "query r($username: String!, $limit: Int!) { recentAcSubmissionList(username: $username, limit: $limit) { title timestamp } }";
        var body = JsonSerializer.Serialize(new { query, variables = new { username = name, limit = 20 } });
        return new HttpRequest("https://leetcode.com/graphql", "POST", body,
            new Dictionary<string, string> { ["Referer"] = "https://leetcode.com" });
    }

    public static HttpRequest? DuolingoRequest(string username)
    {
        var name = username.Trim();
        return DuolingoName.IsMatch(name)
            ? new HttpRequest($"https://www.duolingo.com/2017-06-30/users?username={name}")
            : null;
    }

    public static HttpRequest? FeedRequest(string? url)
    {
        if (url is null || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u)) return null;
        return u.Scheme is "https" or "http" && u.Host.Length > 0 ? new HttpRequest(u.ToString()) : null;
    }

    /// <summary>The request a fetch-based task makes, so the UI can ask what went wrong.</summary>
    public static HttpRequest? RequestFor(TaskDefinition t) => t.Params.Source switch
    {
        "feed" => FeedRequest(t.Params.Url),
        "duolingo" => DuolingoRequest(t.Params.Username ?? ""),
        "codeforces" => CodeforcesRequest(t.Params.Username ?? ""),
        "leetcode" => LeetCodeRequest(t.Params.Username ?? ""),
        _ => null,
    };

    /// <summary>Times of Codeforces submissions with verdict OK.</summary>
    public static List<DateTimeOffset> CodeforcesAccepted(byte[] data)
    {
        var times = new List<DateTimeOffset>();
        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            if (root.GetProperty("status").GetString() != "OK") return times;
            foreach (var s in root.GetProperty("result").EnumerateArray())
            {
                if (s.TryGetProperty("verdict", out var v) && v.GetString() == "OK"
                    && s.TryGetProperty("creationTimeSeconds", out var t))
                    times.Add(DateTimeOffset.FromUnixTimeSeconds(t.GetInt64()));
            }
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { }
        return times;
    }

    /// <summary>data.recentAcSubmissionList[].timestamp, seconds as strings.</summary>
    public static List<DateTimeOffset> LeetCodeAccepted(byte[] data)
    {
        var times = new List<DateTimeOffset>();
        try
        {
            using var doc = JsonDocument.Parse(data);
            var list = doc.RootElement.GetProperty("data").GetProperty("recentAcSubmissionList");
            foreach (var item in list.EnumerateArray())
            {
                var raw = item.GetProperty("timestamp");
                var seconds = raw.ValueKind == JsonValueKind.String
                    ? long.TryParse(raw.GetString(), out var n) ? n : (long?)null
                    : raw.GetInt64();
                if (seconds is { } s) times.Add(DateTimeOffset.FromUnixTimeSeconds(s));
            }
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { }
        return times;
    }

    /// <summary>users[0].streakData.currentStreak.endDate, a "yyyy-MM-dd" string.</summary>
    public static string? DuolingoStreakEnd(byte[] data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            return doc.RootElement.GetProperty("users")[0].GetProperty("streakData")
                .GetProperty("currentStreak").GetProperty("endDate").GetString();
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    private static readonly HashSet<string> DateTags = new(StringComparer.Ordinal) { "pubDate", "date", "published", "updated" };

    /// <summary>Item dates from RSS (pubDate, dc:date) and Atom (published, updated) feeds.</summary>
    public static List<DateTimeOffset> FeedDates(byte[] data)
    {
        var dates = new List<DateTimeOffset>();
        try
        {
            var doc = XDocument.Parse(System.Text.Encoding.UTF8.GetString(data));
            foreach (var item in doc.Descendants().Where(e => e.Name.LocalName is "item" or "entry"))
            foreach (var child in item.Elements().Where(e => DateTags.Contains(e.Name.LocalName)))
                if (ParseFeedDate(child.Value) is { } d) dates.Add(d);
        }
        catch (System.Xml.XmlException) { }
        return dates;
    }

    public static DateTimeOffset? ParseFeedDate(string raw)
    {
        var s = raw.Trim();
        // RFC 822 offsets like "+0000" → "+00:00" so .NET accepts them.
        s = Regex.Replace(s, @"([+-])(\d{2})(\d{2})$", "$1$2:$3");
        s = Regex.Replace(s, @"\s(GMT|UT|UTC|Z)$", " +00:00");
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out var d) ? d : null;
    }

    public static bool SameLocalDay(DateTimeOffset a, DateTimeOffset b) =>
        a.ToLocalTime().Date == b.ToLocalTime().Date;
}
