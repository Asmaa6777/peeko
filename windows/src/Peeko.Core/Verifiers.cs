namespace Peeko.Core;

/// <summary>What the async verifiers need beyond the task itself.</summary>
public sealed class VerifierEnvironment
{
    public required Func<string> GithubUsername { get; init; }
    public required GitHubEventsClient Github { get; init; }
    public required CachedFetcher Fetcher { get; init; }
    public required FolderScanner Folders { get; init; }
    public required GitScanner Git { get; init; }
}

public static class Verifiers
{
    private static readonly HashSet<string> RemoteSources = new() { "githubPush", "feed", "duolingo", "leetcode", "codeforces" };

    /// <summary>Whether this build has a verifier for the task, without building one.</summary>
    public static bool IsVerifiable(TaskDefinition t) => t.Type switch
    {
        VerifierType.Manual or VerifierType.AppTime or VerifierType.FolderActivity
            or VerifierType.GitCommits or VerifierType.FolderTidy => true,
        VerifierType.RemoteCheck => RemoteSources.Contains(t.Params.Source ?? ""),
        VerifierType.Activity => ActivityWindow.Rules.Contains(t.Params.Rule ?? ""),
        VerifierType.AppLimit => t.Params.Minutes is not null,
        _ => false,
    };

    /// <summary>Verifiers that are a pure function of the day record and the clock (no I/O).</summary>
    public static bool IsPure(TaskDefinition t) =>
        t.Type is VerifierType.Manual or VerifierType.AppTime or VerifierType.AppLimit or VerifierType.Activity;

    /// <summary>
    /// The pure verifiers. Manual returns what the record says. appTime turns accumulated seconds
    /// into pending / inProgress / done and keeps the original completion time. appLimit is
    /// progress toward the cap, failed once over. Anything else returns what was last stored.
    /// </summary>
    public static TaskState Evaluate(TaskDefinition task, DayRecord record, DateTimeOffset now)
    {
        record.Statuses.TryGetValue(task.Id, out var stored);
        record.AccumulatedSeconds.TryGetValue(task.Id, out var seconds);
        switch (task.Type)
        {
            case VerifierType.AppTime:
                if (stored is TaskState.Done) return stored;
                var target = Math.Max(task.Params.Minutes ?? 0, 1) * 60.0;
                if (seconds >= target) return new TaskState.Done(now);
                return seconds > 0 ? new TaskState.InProgress(seconds / target) : new TaskState.Pending();

            case VerifierType.AppLimit:
                if (stored is TaskState.Done or TaskState.Failed) return stored;
                if (task.Params.Minutes is not { } minutes || minutes < 0) return new TaskState.Pending();
                var limit = minutes * 60.0;
                if (seconds > limit) return new TaskState.Failed();
                // A zero limit ("none before noon") has no meaningful ratio; show an empty ring.
                return new TaskState.InProgress(limit == 0 ? 0 : seconds / limit);

            case VerifierType.Activity:
                return ActivityWindow.Status(task, record, now);

            default:
                return stored ?? new TaskState.Pending();
        }
    }

    /// <summary>Evaluates any automatic task. Never throws for network or disk problems.</summary>
    public static async Task<TaskState> EvaluateAsync(
        TaskDefinition task, DayRecord record, DateTimeOffset now, VerifierEnvironment env)
    {
        if (IsPure(task)) return Evaluate(task, record, now);
        if (record.Statuses.TryGetValue(task.Id, out var existing) && existing.IsDone) return existing;
        var p = task.Params;

        switch (task.Type)
        {
            case VerifierType.RemoteCheck:
                return await RemoteAsync(task, now, env).ConfigureAwait(false);

            case VerifierType.FolderActivity:
            {
                if (string.IsNullOrEmpty(p.Path)) return new TaskState.Pending();
                var result = env.Folders.Scan(task.Id, FolderTidy.ExpandPath(p.Path), p.Extensions, now);
                if (result is null) return new TaskState.Pending();
                if (p.DailyNote == true)
                    return result.DailyNote is { } at ? new TaskState.Done(at) : new TaskState.Pending();
                var target = p.MinWords ?? 0;
                if (target <= 0)
                    return result.FirstEditToday is { } first ? new TaskState.Done(first) : new TaskState.Pending();
                if (result.WordsAdded >= target) return new TaskState.Done(now);
                return result.WordsAdded > 0
                    ? new TaskState.InProgress((double)result.WordsAdded / target)
                    : new TaskState.Pending();
            }

            case VerifierType.GitCommits:
            {
                if (string.IsNullOrEmpty(p.Path)) return new TaskState.Pending();
                var first = env.Git.FirstCommitToday(FolderTidy.ExpandPath(p.Path), p.Author ?? "", now);
                return first is { } at ? new TaskState.Done(at) : new TaskState.Pending();
            }

            case VerifierType.FolderTidy:
            {
                if (FolderTidy.ItemCount(p.Path) is not { } count) return new TaskState.Pending();
                if (ActivityWindow.Minutes(p.From) is { } from)
                {
                    var start = DayKey.StartOfDay(DayKey.For(now))!.Value.AddMinutes(from);
                    if (now < start) return new TaskState.Pending();
                }
                return count <= (p.MaxItems ?? 5) ? new TaskState.Done(now) : new TaskState.Pending();
            }

            default:
                return new TaskState.Pending();
        }
    }

    private static async Task<TaskState> RemoteAsync(TaskDefinition task, DateTimeOffset now, VerifierEnvironment env)
    {
        var p = task.Params;
        TaskState FirstToday(IEnumerable<DateTimeOffset> times)
        {
            var today = times.Where(t => OnlineChecks.SameLocalDay(t, now)).ToList();
            return today.Count > 0 ? new TaskState.Done(today.Min()) : new TaskState.Pending();
        }

        switch (p.Source)
        {
            case "githubPush":
            {
                var own = p.Username ?? "";
                var user = own.Length > 0 ? own : env.GithubUsername();
                if (user.Length == 0) return new TaskState.Pending();
                var events = await env.Github.EventsAsync(user, now).ConfigureAwait(false);
                if (events is null) return new TaskState.Pending();
                // created_at is UTC; compare against today in the user's calendar, never by string.
                return FirstToday(events.Where(e => e.Type == "PushEvent").Select(e => e.CreatedAt));
            }
            case "feed":
            {
                if (OnlineChecks.FeedRequest(p.Url) is not { } request) return new TaskState.Pending();
                var body = await env.Fetcher.BodyAsync(request, OnlineChecks.FeedInterval, now).ConfigureAwait(false);
                return body is null ? new TaskState.Pending() : FirstToday(OnlineChecks.FeedDates(body));
            }
            case "codeforces":
            {
                if (OnlineChecks.CodeforcesRequest(p.Username ?? "") is not { } request) return new TaskState.Pending();
                var body = await env.Fetcher.BodyAsync(request, OnlineChecks.CodeforcesInterval, now).ConfigureAwait(false);
                return body is null ? new TaskState.Pending() : FirstToday(OnlineChecks.CodeforcesAccepted(body));
            }
            case "leetcode":
            {
                if (OnlineChecks.LeetCodeRequest(p.Username ?? "") is not { } request) return new TaskState.Pending();
                var body = await env.Fetcher.BodyAsync(request, OnlineChecks.LeetCodeInterval, now).ConfigureAwait(false);
                return body is null ? new TaskState.Pending() : FirstToday(OnlineChecks.LeetCodeAccepted(body));
            }
            case "duolingo":
            {
                if (OnlineChecks.DuolingoRequest(p.Username ?? "") is not { } request) return new TaskState.Pending();
                var body = await env.Fetcher.BodyAsync(request, OnlineChecks.DuolingoInterval, now).ConfigureAwait(false);
                if (body is null || OnlineChecks.DuolingoStreakEnd(body) is not { } end) return new TaskState.Pending();
                return end == DayKey.For(now) ? new TaskState.Done(now) : new TaskState.Pending();
            }
            default:
                return new TaskState.Pending();
        }
    }
}
