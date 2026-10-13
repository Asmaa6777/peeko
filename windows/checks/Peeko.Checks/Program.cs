using System.Net.Http;
using System.Text.Json;
using Peeko.Core;

// Stand-in for unit tests, like Checks/main.swift on macOS. Exits non-zero on failure.
var failures = 0;
void Check(string name, bool ok)
{
    Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name}");
    if (!ok) failures++;
}

var t0 = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
var read = new TaskDefinition
{
    Id = "read", Title = "Read for 1 minute", Type = VerifierType.AppTime,
    Params = new TaskParams { ProcessNames = new() { "AcroRd32" }, Minutes = 1, IdleCutoffSeconds = 120 },
};

// --- tracker
{
    var tracker = new ActivityTracker();
    var rec = new DayRecord("2026-03-10");
    tracker.Record(new(t0, "AcroRd32", 0), new[] { read }, rec);
    Check("first tick only starts the clock", !rec.AccumulatedSeconds.ContainsKey("read"));
    tracker.Record(new(t0.AddSeconds(10), "acrord32", 0), new[] { read }, rec);
    Check("matching app credits 10s, case-insensitive", rec.AccumulatedSeconds["read"] == 10);
    tracker.Record(new(t0.AddSeconds(20), "notepad", 0), new[] { read }, rec);
    Check("other app credits nothing", rec.AccumulatedSeconds["read"] == 10);
    tracker.Record(new(t0.AddSeconds(30), "AcroRd32", 500), new[] { read }, rec);
    Check("idle past cutoff credits nothing", rec.AccumulatedSeconds["read"] == 10);
    tracker.Record(new(t0.AddHours(8), "AcroRd32", 0), new[] { read }, rec);
    Check("a long gap (sleep) credits at most 10s", rec.AccumulatedSeconds["read"] == 20);
    tracker.Pause();
    tracker.Record(new(t0.AddHours(8).AddSeconds(10), "AcroRd32", 0), new[] { read }, rec);
    Check("first tick after pause credits nothing", rec.AccumulatedSeconds["read"] == 20);
}

// --- verifier
{
    var rec = new DayRecord("2026-03-10");
    Check("no time -> pending", Verifiers.Evaluate(read, rec, t0) is TaskState.Pending);
    rec.AccumulatedSeconds["read"] = 30;
    Check("half -> inProgress 0.5",
        Verifiers.Evaluate(read, rec, t0) is TaskState.InProgress { Progress: 0.5 });
    rec.AccumulatedSeconds["read"] = 60;
    Check("target reached -> done", Verifiers.Evaluate(read, rec, t0) is TaskState.Done);
    rec.Statuses["read"] = new TaskState.Done(t0);
    Check("done keeps its original time",
        Verifiers.Evaluate(read, rec, t0.AddHours(1)) is TaskState.Done { At: var at } && at == t0);
}

// --- JSON compatibility with the macOS files
{
    const string mac = """
    {"schemaVersion":1,"day":"2026-03-10",
     "statuses":{"a":{"pending":{}},"b":{"inProgress":{"progress":0.25}},
                 "c":{"done":{"at":"2026-03-10T09:30:00Z"}},"d":{"failed":{}}},
     "accumulatedSeconds":{"b":450},
     "activity":[{"start":"2026-03-10T08:00:00Z","end":"2026-03-10T08:40:00Z"}]}
    """;
    var day = JsonSerializer.Deserialize<DayRecord>(mac, Store.Json)!;
    Check("reads a macOS day file", day.Statuses["c"] is TaskState.Done && day.AccumulatedSeconds["b"] == 450
        && day.Activity!.Count == 1);
    var back = JsonSerializer.Serialize(day, Store.Json);
    Check("writes the Swift enum shape", back.Contains("\"done\": {") && back.Contains("2026-03-10T09:30:00Z")
        && back.Contains("\"pending\": {}"));

    const string macTask = """
    {"id":"x","title":"T","type":"appTime","icon":"book",
     "params":{"bundleIDs":["com.apple.Preview"],"minutes":30}}
    """;
    var task = JsonSerializer.Deserialize<TaskDefinition>(macTask, Store.Json)!;
    var out2 = JsonSerializer.Serialize(task, Store.Json);
    Check("keeps unknown Mac params on save", out2.Contains("com.apple.Preview"));
}

// --- store + scheduler end to end
{
    var dir = Path.Combine(Path.GetTempPath(), "peeko-checks-" + Guid.NewGuid().ToString("N"));
    try
    {
        var store = new Store(dir);
        var front = new Fake { Process = "AcroRd32" };
        var sch = new Scheduler(store, front, front, t0);
        sch.SetTasks(new[] { read });
        var completed = new List<string>();
        sch.AutoCompleted += t => completed.Add(t.Id);

        for (var i = 0; i <= 6; i++) sch.Tick(t0.AddSeconds(10 * i));
        Check("scheduler completes the task after a minute", sch.Today.Statuses["read"].IsDone);
        Check("auto-complete notifies exactly once", completed.SequenceEqual(new[] { "read" }));
        sch.Tick(t0.AddSeconds(70));
        Check("no second notification", completed.Count == 1);

        sch.SetManual("m", true, t0);
        Check("manual tap is saved",
            new Store(dir).LoadDay(DayKey.For(t0)).Statuses["m"] is TaskState.Done);
        Check("persisted day reloads", new Store(dir).LoadDay(DayKey.For(t0)).AccumulatedSeconds["read"] >= 60);

        sch.Tick(t0.AddDays(1));
        Check("rolls over to a new day file", sch.Today.Day == DayKey.For(t0.AddDays(1)));
        Check("both days on disk", store.AllDayKeys().Count == 2);

        File.WriteAllText(Path.Combine(dir, "tasks.json"), "{ not json");
        Check("corrupt file loads empty and is moved aside",
            store.LoadTaskList().Tasks.Count == 0 && Directory.GetFiles(dir, "tasks.json.corrupt-*").Length == 1);
    }
    finally { try { Directory.Delete(dir, true); } catch { } }
}

// --- history, streaks, mood
{
    var dir = Path.Combine(Path.GetTempPath(), "peeko-checks-" + Guid.NewGuid().ToString("N"));
    try
    {
        var store = new Store(dir);
        var today = new DateTime(2026, 3, 10);
        void Day(int back, params string[] doneIds)
        {
            var r = new DayRecord(History.Key(today.AddDays(-back)));
            foreach (var id in doneIds) r.Statuses[id] = new TaskState.Done(DateTimeOffset.UtcNow);
            store.Save(r);
        }
        Day(1, "a", "b"); Day(2, "a"); Day(3, "b"); /* day 4 missing → breaks */ Day(5, "a");
        var h = History.Load(store, 35, today);
        Check("streak counts from yesterday when today is empty", h.OverallStreak(today) == 3);
        var live = new DayRecord(History.Key(today));
        live.Statuses["a"] = new TaskState.Done(DateTimeOffset.UtcNow);
        var h2 = h.With(live);
        Check("today extends the streak", h2.OverallStreak(today) == 4);
        Check("per-task streak", h2.StreakFor("a", today) == 3 && h2.StreakFor("b", today) == 1);
        Check("times done in the last 30 days", h2.TimesDone("a", 30, today) == 4);
        Check("a missing day breaks the streak", h.StreakFor("a", today.AddDays(-5)) == 1);
    }
    finally { try { Directory.Delete(dir, true); } catch { } }

    Check("mood: nothing -> dozing", Moods.Day(new TaskState[] { new TaskState.Pending() }) == Mood.Dozing);
    Check("mood: progress -> looking",
        Moods.Day(new TaskState[] { new TaskState.InProgress(0.3), new TaskState.Pending() }) == Mood.Looking);
    Check("mood: all settled -> beaming",
        Moods.Day(new TaskState[] { new TaskState.Done(t0), new TaskState.Failed() }) == Mood.Beaming);
    Check("mood: done but some left -> looking",
        Moods.Day(new TaskState[] { new TaskState.Done(t0), new TaskState.Pending() }) == Mood.Looking);
}

// --- wording and enabling presets
{
    Check("describe appTime", TaskDetail.Describe(read).StartsWith("1 min in "));
    Check("duration formatting", TaskDetail.Duration(90) == "1 h 30 min" && TaskDetail.Duration(45) == "45 min");
    Check("done note for manual", TaskDetail.Done(new TaskDefinition { Type = VerifierType.Manual }, t0).StartsWith("done · "));
    Check("done note for auto", TaskDetail.Done(read, t0).StartsWith("Peeko saw that! · "));

    var dir = Path.Combine(Path.GetTempPath(), "peeko-checks-" + Guid.NewGuid().ToString("N"));
    try
    {
        var store = new Store(dir);
        var f = new Fake();
        var sch = new Scheduler(store, f, f, t0);
        sch.SetEnabled(read, true);
        Check("preset enabled and saved", sch.IsEnabled("read") && store.LoadTaskList().Tasks.Count == 1);
        sch.SetEnabled(read, false);
        Check("preset disabled and saved", !sch.IsEnabled("read") && store.LoadTaskList().Tasks.Count == 0);
        var custom = sch.AddCustomManual("  Stretch  ");
        Check("custom task is manual, trimmed, saved",
            custom.Title == "Stretch" && custom.Id.StartsWith("custom-")
            && store.LoadTaskList().Tasks.Single().Type == VerifierType.Manual);
        store.Save(new AppSettings { NotificationsEnabled = false });
        Check("settings round-trip", !new Store(dir).LoadSettings().NotificationsEnabled);
        Check("export is valid JSON with days",
            System.Text.Json.JsonDocument.Parse(Exporter.Make(store, t0)).RootElement.TryGetProperty("days", out _));
    }
    finally { try { Directory.Delete(dir, true); } catch { } }
}


// ───────────── local day helpers: robust in any time zone (everything is "today, local")
var day0 = new DateTimeOffset(DateTime.Today);
DateTimeOffset At(int h, int m = 0) => day0.AddHours(h).AddMinutes(m);
var dayKey = DayKey.For(day0);
TaskDefinition Def(string id, string type, Action<TaskParams> configure)
{
    var t = new TaskDefinition { Id = id, Title = id, Type = type };
    configure(t.Params);
    return t;
}
DayRecord Spans(params (DateTimeOffset s, DateTimeOffset e)[] spans)
{
    var r = new DayRecord(dayKey) { Activity = spans.Select(x => new ActivitySpan { Start = x.s, End = x.e }).ToList() };
    return r;
}

// --- activity windows
{
    var quiet = Def("quiet", VerifierType.Activity, p => { p.Rule = "quietBetween"; p.From = "00:30"; p.Until = "05:00"; });
    Check("quiet: nothing yet -> pending", ActivityWindow.Status(quiet, Spans(), At(2)) is TaskState.Pending);
    Check("quiet: a quiet night -> done at the end of the window",
        ActivityWindow.Status(quiet, Spans(), At(6)) is TaskState.Done { At: var d } && d == At(5));
    Check("quiet: activity inside the window -> failed",
        ActivityWindow.Status(quiet, Spans((At(1), At(1, 20))), At(2)) is TaskState.Failed);

    var early = Def("early", VerifierType.Activity, p => { p.Rule = "startBetween"; p.From = "04:00"; p.Until = "08:00"; });
    Check("early: started 07:10 -> done at 07:10",
        ActivityWindow.Status(early, Spans((At(7, 10), At(9))), At(9)) is TaskState.Done { At: var e } && e == At(7, 10));
    Check("early: an all-nighter that began at 03:00 doesn't count",
        ActivityWindow.Status(early, Spans((At(3), At(9))), At(9)) is TaskState.Failed);
    Check("early: nothing yet, still before the end -> pending",
        ActivityWindow.Status(early, Spans(), At(6)) is TaskState.Pending);

    var lunch = Def("lunch", VerifierType.Activity, p => { p.Rule = "gapBetween"; p.From = "12:00"; p.Until = "15:00"; p.GapMinutes = 30; });
    Check("gap: a 40-minute break -> done",
        ActivityWindow.Status(lunch, Spans((At(9), At(12, 10)), (At(12, 50), At(14))), At(14)) is TaskState.Done { At: var g } && g == At(12, 40));
    Check("gap: still away right now counts",
        ActivityWindow.Status(lunch, Spans((At(9), At(12, 10))), At(12, 45)) is TaskState.Done);
    Check("gap: time before the first activity isn't a break",
        ActivityWindow.Status(lunch, Spans((At(14, 50), At(14, 55))), At(15, 1)) is TaskState.Failed);
    var breaks = Def("breaks", VerifierType.Activity, p => { p.Rule = "gapBetween"; p.From = "09:00"; p.Until = "18:00"; p.GapMinutes = 5; p.Count = 4; });
    Check("gap: three breaks of four -> pending",
        ActivityWindow.Status(breaks, Spans((At(9), At(10)), (At(10, 10), At(11)), (At(11, 10), At(12)), (At(12, 2), At(13))), At(13)) is TaskState.Pending);
    Check("gap: four breaks -> done",
        ActivityWindow.Status(breaks, Spans((At(9), At(10)), (At(10, 10), At(11)), (At(11, 10), At(12)), (At(12, 10), At(13)), (At(13, 10), At(14))), At(14)) is TaskState.Done);

    Check("HH:mm parsing", ActivityWindow.Minutes("24:00") == 1440 && ActivityWindow.Minutes("09:30") == 570
        && ActivityWindow.Minutes("25:00") is null && ActivityWindow.Minutes("9") is null);

    var spans = new List<ActivitySpan>();
    Check("activity log: first input starts a span", ActivityLog.Note(At(9), spans, At(0)));
    Check("activity log: input within 5 min extends it", !ActivityLog.Note(At(9, 4), spans, At(0)) && spans.Count == 1 && spans[0].End == At(9, 4));
    Check("activity log: a longer gap starts a new span", ActivityLog.Note(At(10), spans, At(0)) && spans.Count == 2);
    var clamp = new List<ActivitySpan>();
    ActivityLog.Note(At(0).AddHours(-3), clamp, At(0));
    Check("activity log: input from before midnight is clamped", clamp[0].Start == At(0));
}

// --- app limits, title filters, time windows
{
    var limit = Def("limit", VerifierType.AppLimit, p => { p.Minutes = 1; p.ProcessNames = new() { "chrome" }; });
    var rec = new DayRecord(dayKey);
    rec.AccumulatedSeconds["limit"] = 30;
    Check("limit: half used -> inProgress 0.5", Verifiers.Evaluate(limit, rec, At(12)) is TaskState.InProgress { Progress: 0.5 });
    rec.AccumulatedSeconds["limit"] = 61;
    Check("limit: over -> failed", Verifiers.Evaluate(limit, rec, At(12)) is TaskState.Failed);
    rec.Statuses["limit"] = new TaskState.Failed();
    rec.AccumulatedSeconds["limit"] = 0;
    Check("limit: once failed it stays failed", Verifiers.Evaluate(limit, rec, At(12)) is TaskState.Failed);
    var none = Def("none", VerifierType.AppLimit, p => { p.Minutes = 0; });
    Check("limit: a zero limit shows an empty ring",
        Verifiers.Evaluate(none, new DayRecord(dayKey), At(12)) is TaskState.InProgress { Progress: 0 });

    var yt = Def("yt", VerifierType.AppLimit, p => { p.Minutes = 45; p.ProcessNames = new() { "chrome" }; p.TitleContains = new() { "YouTube" }; });
    var tracker = new ActivityTracker();
    var r2 = new DayRecord(dayKey);
    tracker.Record(new(At(12), "chrome", 0, "Cats - YouTube - Google Chrome"), new[] { yt }, r2);
    tracker.Record(new(At(12).AddSeconds(10), "chrome", 0, "Cats - YouTube - Google Chrome"), new[] { yt }, r2);
    Check("title filter: a matching title is counted", r2.AccumulatedSeconds.GetValueOrDefault("yt") == 10);
    tracker.Record(new(At(12).AddSeconds(20), "chrome", 0, "Inbox - Gmail"), new[] { yt }, r2);
    Check("title filter: another title is not", r2.AccumulatedSeconds.GetValueOrDefault("yt") == 10);
    tracker.Record(new(At(12).AddSeconds(30), "chrome", 0, null), new[] { yt }, r2);
    Check("title filter: no title is not", r2.AccumulatedSeconds.GetValueOrDefault("yt") == 10);

    var social = Def("social", VerifierType.AppLimit, p => { p.Minutes = 0; p.From = "00:00"; p.Until = "12:00"; p.TitleContains = new() { "Reddit" }; });
    var t3 = new ActivityTracker();
    var r3 = new DayRecord(dayKey);
    t3.Record(new(At(9), "chrome", 0, "Reddit"), new[] { social }, r3);
    t3.Record(new(At(9).AddSeconds(10), "chrome", 0, "Reddit"), new[] { social }, r3);
    Check("time window: counted before noon", r3.AccumulatedSeconds.GetValueOrDefault("social") == 10);
    t3.Record(new(At(13), "chrome", 0, "Reddit"), new[] { social }, r3);
    t3.Record(new(At(13).AddSeconds(10), "chrome", 0, "Reddit"), new[] { social }, r3);
    Check("time window: not counted after noon", r3.AccumulatedSeconds.GetValueOrDefault("social") == 10);
    Check("needsWindowTitle only with a title filter", yt.NeedsWindowTitle && !limit.NeedsWindowTitle);

    // Rollover settles yesterday: a limit still under its cap is done, an over-limit one stays failed.
    var dir = Path.Combine(Path.GetTempPath(), "peeko-checks-" + Guid.NewGuid().ToString("N"));
    try
    {
        var store = new Store(dir);
        var f = new Fake { Process = "chrome", Title = "x" };
        var sch = new Scheduler(store, f, f, At(12), new FakeHttp());
        var over = Def("over", VerifierType.AppLimit, p => { p.Minutes = 1; });
        var under = Def("under", VerifierType.AppLimit, p => { p.Minutes = 60; });
        sch.SetTasks(new[] { over, under });
        sch.Today.AccumulatedSeconds["over"] = 100;
        sch.Today.AccumulatedSeconds["under"] = 100;
        sch.Tick(At(12).AddDays(1));
        var yesterday = store.LoadDay(dayKey);
        Check("rollover: limit under its cap is done for yesterday", yesterday.Statuses["under"] is TaskState.Done);
        Check("rollover: limit over its cap is failed for yesterday", yesterday.Statuses["over"] is TaskState.Failed);
        Check("window title is only asked for when a task needs it", f.TitleCalls == 0);
        sch.SetTasks(new[] { yt });
        sch.Tick(At(12).AddDays(1).AddSeconds(10));
        Check("window title is asked for when a task filters on it", f.TitleCalls == 1);
    }
    finally { try { Directory.Delete(dir, true); } catch { } }
}

// --- online checks, with canned responses
{
    var noon = At(12);
    string Today(DateTimeOffset t) => IsoDate.Format(t);

    var http = new FakeHttp();
    var dir = Path.Combine(Path.GetTempPath(), "peeko-checks-" + Guid.NewGuid().ToString("N"));
    try
    {
        var store = new Store(dir);
        var f = new Fake { Process = "notepad" };
        var sch = new Scheduler(store, f, f, noon, http) { GithubUsername = "octocat" };
        var push = Def("github-push", VerifierType.RemoteCheck, p => p.Source = "githubPush");
        var completed = new List<string>();
        sch.AutoCompleted += t => completed.Add(t.Id);
        sch.SetTasks(new[] { push });

        // yesterday's push only -> not done
        http.Respond = _ => FakeHttp.Ok($"[{{\"type\":\"PushEvent\",\"created_at\":\"{Today(noon.AddDays(-1))}\"}}]", "\"v1\"");
        await sch.TickAsync(noon);
        Check("github: yesterday's push is not today's", !sch.StatusOf(push, noon).IsDone && http.Requests.Count == 1);
        Check("github: sends the API headers",
            http.Requests[0].Url == "https://api.github.com/users/octocat/events/public?per_page=100"
            && http.Requests[0].Headers!["User-Agent"] == "Peeko-App");

        await sch.TickAsync(noon.AddSeconds(10));
        Check("github: polls at most every 10 minutes", http.Requests.Count == 1);

        // after the interval: ETag is sent, 304 keeps the cache
        http.Respond = r => r.Headers!.ContainsKey("If-None-Match")
            ? new HttpResult(304, Array.Empty<byte>(), new Dictionary<string, string>())
            : FakeHttp.Ok("[]");
        await sch.TickAsync(noon.AddMinutes(11));
        Check("github: revalidates with If-None-Match", http.Requests.Count == 2 && http.Requests[1].Headers!["If-None-Match"] == "\"v1\"");
        Check("github: a 304 isn't a problem", sch.OnlineProblem(push) is null);

        // a push today (PushEvent only; other event types are ignored)
        http.Respond = _ => FakeHttp.Ok(
            $"[{{\"type\":\"WatchEvent\",\"created_at\":\"{Today(noon.AddHours(-3))}\"}},{{\"type\":\"PushEvent\",\"created_at\":\"{Today(noon.AddHours(-1))}\"}}]");
        await sch.TickAsync(noon.AddMinutes(30));
        Check("github: a push today -> done at that push",
            sch.StatusOf(push, noon.AddMinutes(30)) is TaskState.Done { At: var at } && at.ToLocalTime().Hour == 11);
        Check("github: notifies exactly once", completed.SequenceEqual(new[] { "github-push" }));
        await sch.TickAsync(noon.AddMinutes(50));
        Check("github: no second notification", completed.Count == 1);
    }
    finally { try { Directory.Delete(dir, true); } catch { } }

    // problems in words
    {
        var h = new FakeHttp { Respond = _ => new HttpResult(404, Array.Empty<byte>(), new Dictionary<string, string>()) };
        var client = new GitHubEventsClient(h);
        await client.EventsAsync("nobody-here", noon);
        Check("github: 404 -> 'doesn't know that username'", client.ProblemFor("nobody-here") == "GitHub doesn't know that username");
        h.Respond = _ => throw new HttpRequestException("offline");
        await client.EventsAsync("octocat", noon);
        Check("github: offline -> 'can't reach GitHub'", client.ProblemFor("octocat")!.StartsWith("can't reach GitHub"));
        var before = h.Requests.Count;
        await client.EventsAsync("../etc/passwd", noon);
        Check("github: invalid usernames never build a URL", h.Requests.Count == before);
        Check("github: username rules", GitHubEventsClient.IsValidUsername("a-b") && !GitHubEventsClient.IsValidUsername("-ab")
            && !GitHubEventsClient.IsValidUsername("a b"));
    }

    // Duolingo, LeetCode, Codeforces, feeds
    async Task<TaskState> Run(TaskDefinition task, Func<HttpRequest, HttpResult> respond)
    {
        var h = new FakeHttp { Respond = respond };
        var env = new VerifierEnvironment
        {
            GithubUsername = () => "",
            Github = new GitHubEventsClient(h),
            Fetcher = new CachedFetcher(h),
            Folders = new FolderScanner(new Store(Path.GetTempPath())),
            Git = new GitScanner(),
        };
        return await Verifiers.EvaluateAsync(task, new DayRecord(dayKey), noon, env);
    }

    var duo = Def("duo", VerifierType.RemoteCheck, p => { p.Source = "duolingo"; p.Username = "peeko_fan"; });
    Check("duolingo: streak ended today -> done",
        await Run(duo, _ => FakeHttp.Ok($"{{\"users\":[{{\"streakData\":{{\"currentStreak\":{{\"endDate\":\"{dayKey}\"}}}}}}]}}")) is TaskState.Done);
    Check("duolingo: streak ended yesterday -> pending",
        await Run(duo, _ => FakeHttp.Ok($"{{\"users\":[{{\"streakData\":{{\"currentStreak\":{{\"endDate\":\"{DayKey.For(day0.AddDays(-1))}\"}}}}}}]}}")) is TaskState.Pending);
    Check("duolingo: no username -> no request", OnlineChecks.DuolingoRequest("") is null);

    var lc = Def("lc", VerifierType.RemoteCheck, p => { p.Source = "leetcode"; p.Username = "someone"; });
    HttpRequest? sent = null;
    var lcState = await Run(lc, r =>
    {
        sent = r;
        return FakeHttp.Ok($"{{\"data\":{{\"recentAcSubmissionList\":[{{\"title\":\"Two Sum\",\"timestamp\":\"{noon.AddHours(-2).ToUnixTimeSeconds()}\"}}]}}}}");
    });
    Check("leetcode: accepted today -> done", lcState is TaskState.Done);
    Check("leetcode: POSTs the GraphQL query", sent!.Method == "POST" && sent.Body!.Contains("recentAcSubmissionList") && sent.Body.Contains("someone"));
    Check("leetcode: yesterday's solution -> pending",
        await Run(lc, _ => FakeHttp.Ok($"{{\"data\":{{\"recentAcSubmissionList\":[{{\"timestamp\":\"{noon.AddDays(-1).ToUnixTimeSeconds()}\"}}]}}}}")) is TaskState.Pending);

    var cf = Def("cf", VerifierType.RemoteCheck, p => { p.Source = "codeforces"; p.Username = "tourist"; });
    Check("codeforces: an OK verdict today -> done",
        await Run(cf, _ => FakeHttp.Ok($"{{\"status\":\"OK\",\"result\":[{{\"verdict\":\"WRONG_ANSWER\",\"creationTimeSeconds\":{noon.ToUnixTimeSeconds()}}},{{\"verdict\":\"OK\",\"creationTimeSeconds\":{noon.AddHours(-1).ToUnixTimeSeconds()}}}]}}")) is TaskState.Done);
    Check("codeforces: only wrong answers -> pending",
        await Run(cf, _ => FakeHttp.Ok($"{{\"status\":\"OK\",\"result\":[{{\"verdict\":\"WRONG_ANSWER\",\"creationTimeSeconds\":{noon.ToUnixTimeSeconds()}}}]}}")) is TaskState.Pending);
    Check("codeforces: handle rules", OnlineChecks.CodeforcesRequest("a b") is null && OnlineChecks.CodeforcesRequest("tourist") is not null);

    var feed = Def("feed", VerifierType.RemoteCheck, p => { p.Source = "feed"; p.Url = "https://example.com/feed.xml"; });
    var rfc = noon.UtcDateTime.ToString("ddd, dd MMM yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
    Check("feed: an RSS item dated today (+0000) -> done",
        await Run(feed, _ => FakeHttp.Ok($"<rss><channel><item><title>x</title><pubDate>{rfc} +0000</pubDate></item></channel></rss>")) is TaskState.Done);
    Check("feed: an RSS item dated today (GMT) -> done",
        await Run(feed, _ => FakeHttp.Ok($"<rss><channel><item><pubDate>{rfc} GMT</pubDate></item></channel></rss>")) is TaskState.Done);
    Check("feed: an Atom entry dated today -> done",
        await Run(feed, _ => FakeHttp.Ok($"<feed xmlns=\"http://www.w3.org/2005/Atom\"><entry><published>{IsoDate.Format(noon)}</published></entry></feed>")) is TaskState.Done);
    Check("feed: only old items -> pending",
        await Run(feed, _ => FakeHttp.Ok("<rss><channel><item><pubDate>Mon, 01 Jan 2024 10:00:00 +0000</pubDate></item></channel></rss>")) is TaskState.Pending);
    Check("feed: a channel date outside items is ignored",
        await Run(feed, _ => FakeHttp.Ok($"<rss><channel><pubDate>{rfc} +0000</pubDate></channel></rss>")) is TaskState.Pending);
    Check("feed: malformed XML -> pending", await Run(feed, _ => FakeHttp.Ok("<rss><oops")) is TaskState.Pending);
    Check("feed: only http(s) links", OnlineChecks.FeedRequest("ftp://x.com/a") is null && OnlineChecks.FeedRequest("https://x.com/a") is not null);

    // the fetcher reports problems and caches by URL + body
    var fh = new FakeHttp { Respond = _ => new HttpResult(500, Array.Empty<byte>(), new Dictionary<string, string>()) };
    var fetcher = new CachedFetcher(fh);
    var req = OnlineChecks.FeedRequest("https://example.com/feed.xml")!;
    await fetcher.BodyAsync(req, TimeSpan.FromMinutes(30), noon);
    Check("fetcher: a 500 is a problem in words", fetcher.ProblemFor(req) == "example.com answered 500");
    await fetcher.BodyAsync(req, TimeSpan.FromMinutes(30), noon.AddMinutes(1));
    Check("fetcher: respects the interval", fh.Requests.Count == 1);
    var a = new HttpRequest("https://x.test/g", "POST", "{\"u\":\"a\"}");
    var b = new HttpRequest("https://x.test/g", "POST", "{\"u\":\"b\"}");
    Check("fetcher: POSTs with different bodies cache separately", a.CacheKey != b.CacheKey);
}

// --- folders, notes and words
{
    var now = DateTimeOffset.Now;
    var dir = Path.Combine(Path.GetTempPath(), "peeko-checks-" + Guid.NewGuid().ToString("N"));
    var notes = Path.Combine(dir, "notes");
    Directory.CreateDirectory(Path.Combine(notes, ".obsidian"));
    Directory.CreateDirectory(Path.Combine(notes, "sub"));
    try
    {
        File.WriteAllText(Path.Combine(notes, "a.md"), "one two three four five six seven eight nine ten");
        File.WriteAllText(Path.Combine(notes, ".obsidian", "hidden.md"), "should not count at all");
        File.WriteAllText(Path.Combine(notes, "image.png"), "not a note");
        File.WriteAllText(Path.Combine(notes, "sub", "b.txt"), "alpha beta");
        var store = new Store(Path.Combine(dir, "data"));
        var scanner = new FolderScanner(store);

        var r1 = scanner.Scan("t", notes, null, now);
        Check("notes: finds today's edits, skipping hidden folders and other extensions",
            r1 is { FirstEditToday: not null } && r1.WordsAdded == 12);

        var any = Def("any", VerifierType.FolderActivity, p => { p.Path = notes; p.MinWords = 0; });
        var write = Def("w", VerifierType.FolderActivity, p => { p.Path = notes; p.MinWords = 300; });
        var daily = Def("d", VerifierType.FolderActivity, p => { p.Path = notes; p.DailyNote = true; });
        var env = new VerifierEnvironment
        {
            GithubUsername = () => "", Github = new GitHubEventsClient(new FakeHttp()),
            Fetcher = new CachedFetcher(new FakeHttp()), Folders = new FolderScanner(store), Git = new GitScanner(),
        };
        var rec = new DayRecord(DayKey.For(now));
        Check("notes: any edit today -> done", await Verifiers.EvaluateAsync(any, rec, now, env) is TaskState.Done);
        Check("notes: 12 of 300 words -> inProgress",
            await Verifiers.EvaluateAsync(write, rec, now, env) is TaskState.InProgress { Progress: > 0.03 and < 0.05 });
        Check("notes: no dated note -> pending", await Verifiers.EvaluateAsync(daily, rec, now, env) is TaskState.Pending);

        File.WriteAllText(Path.Combine(notes, DayKey.For(now) + ".md"), "today");
        var later = now.AddMinutes(2);
        Check("notes: today's dated note -> done", await Verifiers.EvaluateAsync(daily, rec, later, env) is TaskState.Done);

        File.AppendAllText(Path.Combine(notes, "a.md"), " " + string.Join(' ', Enumerable.Repeat("word", 320)));
        Check("notes: 300+ words added -> done", await Verifiers.EvaluateAsync(write, rec, later.AddMinutes(2), env) is TaskState.Done);
        Check("notes: starting counts are saved for restarts", File.Exists(Path.Combine(dir, "data", "words", DayKey.For(now) + ".json")));

        var missing = Def("m", VerifierType.FolderActivity, p => { p.Path = Path.Combine(dir, "nope"); p.MinWords = 0; });
        Check("notes: a missing folder -> pending", await Verifiers.EvaluateAsync(missing, rec, now, env) is TaskState.Pending);

        // tidy
        var tidy = Path.Combine(dir, "desk");
        Directory.CreateDirectory(tidy);
        foreach (var n in new[] { "1.txt", "2.txt", "3.txt", ".hidden" }) File.WriteAllText(Path.Combine(tidy, n), "x");
        Check("tidy: counts visible items only", FolderTidy.ItemCount(tidy) == 3);
        var tidyOk = Def("tidy", VerifierType.FolderTidy, p => { p.Path = tidy; p.MaxItems = 5; p.From = "00:00"; });
        Check("tidy: under the limit after the start time -> done", await Verifiers.EvaluateAsync(tidyOk, rec, now, env) is TaskState.Done);
        var tidyBusy = Def("tidy2", VerifierType.FolderTidy, p => { p.Path = tidy; p.MaxItems = 2; });
        Check("tidy: over the limit -> pending", await Verifiers.EvaluateAsync(tidyBusy, rec, now, env) is TaskState.Pending);
        var tidyLate = Def("tidy3", VerifierType.FolderTidy, p => { p.Path = tidy; p.MaxItems = 5; p.From = "23:59"; });
        var earlyNow = new DateTimeOffset(DateTime.Today.AddHours(8));
        Check("tidy: before the start time -> pending", await Verifiers.EvaluateAsync(tidyLate, rec, earlyNow, env) is TaskState.Pending);
        Check("tidy: the Desktop default expands", FolderTidy.ExpandPath("~/Desktop").Length > 0 && !FolderTidy.ExpandPath("~/Desktop").StartsWith("~"));

        // git, with a fake runner
        var code = Path.Combine(dir, "code");
        Directory.CreateDirectory(Path.Combine(code, "app", ".git"));
        Directory.CreateDirectory(Path.Combine(code, "work", "api", ".git"));
        Directory.CreateDirectory(Path.Combine(code, "a", "b", "c", ".git"));
        var repos = GitScanner.Repos(code).Select(r => Path.GetRelativePath(code, r).Replace('\\', '/')).OrderBy(x => x).ToList();
        Check("git: finds repos up to two levels down", repos.SequenceEqual(new[] { "app", "work/api" }));

        var calls = new List<string[]>();
        var earlier = now.AddHours(-1).ToUnixTimeSeconds();
        var git = new GitScanner((exe, args) =>
        {
            calls.Add(args);
            return args.Contains("config") ? "me@example.com\n" : $"{now.ToUnixTimeSeconds()}\n{earlier}\n";
        }, () => "git");
        var first = git.FirstCommitToday(code, "me@example.com", now);
        Check("git: the earliest commit today", first?.ToUnixTimeSeconds() == earlier);
        Check("git: filters by author and since the start of the day",
            calls.Any(c => c.Contains("--author=me@example.com")) && calls.Any(c => c.Any(a => a.StartsWith("--since="))));
        var count = calls.Count;
        git.FirstCommitToday(code, "me@example.com", now.AddMinutes(1));
        Check("git: caches for five minutes", calls.Count == count);
        Check("git: reads your email", git.Email() == "me@example.com");
        var noGit = new GitScanner((_, _) => "", () => null);
        Check("git: no git installed -> nothing", noGit.FirstCommitToday(code, "", now) is null);
    }
    finally { try { Directory.Delete(dir, true); } catch { } }
}

// --- reminders
{
    var noon = At(12);
    var bt = new BreakTracker();
    var due = false;
    for (var i = 0; i <= 7 && !due; i++)
        due = bt.Record(new(noon.AddSeconds(10 * i), "code", 0), 60);
    Check("breaks: a minute of activity reaches a 60s interval", due);
    Check("breaks: the count restarts after a reminder", bt.ActiveSeconds == 0);

    var bt2 = new BreakTracker();
    for (var i = 0; i < 5; i++) bt2.Record(new(noon.AddSeconds(10 * i), "code", 0), 600);
    bt2.Record(new(noon.AddSeconds(50), "code", 400), 600);
    Check("breaks: five idle minutes count as the break", bt2.ActiveSeconds == 0);

    var bt3 = new BreakTracker();
    for (var i = 0; i < 5; i++) bt3.Record(new(noon.AddSeconds(10 * i), "code", 0), 600);
    bt3.Record(new(noon.AddMinutes(30), "code", 0), 600);
    Check("breaks: a long gap between ticks (asleep) is a break", bt3.ActiveSeconds == 0);

    var s = new AppSettings();
    Check("evening: not before 21:00", !EveningNudge.IsDue(At(20, 59), s, null, 2));
    Check("evening: due at 21:00 with tasks left", EveningNudge.IsDue(At(21), s, null, 2));
    Check("evening: not when everything's done", !EveningNudge.IsDue(At(21), s, null, 0));
    Check("evening: once a day", !EveningNudge.IsDue(At(22), s, DayKey.For(At(21)), 2));
    Check("evening: can be switched off", !EveningNudge.IsDue(At(21), new AppSettings { EveningNudgeEnabled = false }, null, 2));

    var engine = new ReminderEngine();
    var n1 = engine.OnTick(new(At(21, 5), "code", 0), s, 1, null, out var nudged).ToList();
    Check("engine: nudges with the singular wording", n1.Any(n => n.Body == "1 task left today.") && nudged == DayKey.For(At(21, 5)));
    var n2 = engine.OnTick(new(At(21, 5).AddSeconds(10), "code", 0), s, 3, nudged, out var again).ToList();
    Check("engine: doesn't nudge twice", n2.All(n => !n.Id.StartsWith("nudge")) && again is null);

    var engine2 = new ReminderEngine();
    var titles = new List<string>();
    var settings = new AppSettings { BreakIntervalMinutes = 30, EveningNudgeEnabled = false };
    var t = noon;
    for (var round = 0; round < 3; round++)
    {
        for (var i = 0; i < 400; i++)
        {
            t = t.AddSeconds(10);
            titles.AddRange(engine2.OnTick(new(t, "code", 0), settings, 0, null, out _).Where(n => n.Id.StartsWith("break")).Select(n => n.Title));
        }
        t = t.AddMinutes(10);   // a real break between rounds
    }
    Check("engine: break reminders rotate their wording", titles.Distinct().Count() == 3 && titles.Count >= 3);
    Check("engine: break reminders can be switched off", !new ReminderEngine().OnTick(new(noon, "code", 0),
        new AppSettings { BreakRemindersEnabled = false }, 0, null, out _).Any());
    Check("break wording", BreakMessages.Duration(60) == "an hour" && BreakMessages.Duration(45) == "45 minutes");
}

// --- wording for the new types
{
    Check("describe: limit with a title filter",
        TaskDetail.Describe(Def("y", VerifierType.AppLimit, p => { p.Minutes = 45; p.TitleContains = new() { "YouTube" }; }))
        == "under 45 min of YouTube");
    Check("describe: none before noon",
        TaskDetail.Describe(Def("s", VerifierType.AppLimit, p => { p.Minutes = 0; p.Until = "12:00"; p.TitleContains = new() { "Reddit" }; }))
        == "none of Reddit before 12:00");
    Check("describe: whole screen time",
        TaskDetail.Describe(Def("st", VerifierType.AppLimit, p => p.Minutes = 480)) == "under 8 h of screen time");
    Check("describe: github", TaskDetail.Describe(Def("g", VerifierType.RemoteCheck, p => p.Source = "githubPush")) == "a public push today");
    Check("describe: feed host", TaskDetail.Describe(Def("f", VerifierType.RemoteCheck,
        p => { p.Source = "feed"; p.Url = "https://www.example.com/feed.xml"; })) == "a new post on example.com");
    Check("describe: asks for a username",
        TaskDetail.Describe(Def("l", VerifierType.RemoteCheck, p => p.Source = "leetcode")) == "add your LeetCode username in Explore");
    Check("describe: sleep window", TaskDetail.Describe(Def("q", VerifierType.Activity,
        p => { p.Rule = "quietBetween"; p.From = "00:30"; p.Until = "05:00"; })) == "no screen time 00:30–05:00");
    Check("describe: folder", TaskDetail.Describe(Def("w", VerifierType.FolderActivity,
        p => { p.Path = @"C:\Users\me\Notes"; p.MinWords = 300; })) == "300 words in Notes");
    Check("describe: folder not picked yet",
        TaskDetail.Describe(Def("w2", VerifierType.FolderActivity, p => p.MinWords = 0)) == "pick a folder in Explore");
    Check("describe: macOS-only types say so", TaskDetail.Describe(Def("b", VerifierType.Backup, _ => { })) == "macOS only");
    Check("done wording: sleep", TaskDetail.Done(Def("q", VerifierType.Activity, p => p.Rule = "quietBetween"), At(5)) == "looks like you slept on time");
    Check("done wording: custom note with {time}",
        TaskDetail.Done(Def("u", VerifierType.Activity, p => { p.Rule = "startBetween"; p.DoneNote = "up and about at {time}"; }), At(7))
            .StartsWith("up and about at "));
    Check("missed wording is gentle", TaskDetail.Missed(Def("q", VerifierType.Activity, p => p.Rule = "quietBetween")) == "up late last night · no worries");
    Check("verifiable: needs its params",
        !Verifiers.IsVerifiable(Def("x", VerifierType.RemoteCheck, p => p.Source = "unknown"))
        && !Verifiers.IsVerifiable(Def("y", VerifierType.Backup, _ => { }))
        && Verifiers.IsVerifiable(Def("z", VerifierType.Activity, p => p.Rule = "gapBetween")));
}

// --- open count (what the evening nudge counts)
{
    var dir = Path.Combine(Path.GetTempPath(), "peeko-checks-" + Guid.NewGuid().ToString("N"));
    try
    {
        var f = new Fake();
        var sch = new Scheduler(new Store(dir), f, f, At(12), new FakeHttp());
        sch.SetTasks(new[]
        {
            Def("m1", VerifierType.Manual, _ => { }),
            Def("m2", VerifierType.Manual, _ => { }),
            Def("limit", VerifierType.AppLimit, p => p.Minutes = 30),
            Def("backup", VerifierType.Backup, _ => { }),
        });
        Check("open count ignores limits and macOS-only tasks", sch.OpenCount(At(12)) == 2);
        sch.SetManual("m1", true, At(12));
        Check("open count drops finished tasks", sch.OpenCount(At(12)) == 1);
    }
    finally { try { Directory.Delete(dir, true); } catch { } }
}

// --- catalog
{
    var path = Path.Combine(AppContext.BaseDirectory, "catalog.windows.json");
    var cat = Store.LoadCatalog(path);
    Check("windows catalog loads with presets", cat.Presets.Count > 0);
    Check("every preset has a verifier on Windows", cat.Presets.All(Verifiers.IsVerifiable));
    Check("no macOS-only presets", cat.Presets.All(p => !VerifierType.IsMacOnly(p.Type)));
    Check("presets that need setup ship empty",
        cat.Presets.Where(p => p.Params.Source is "githubPush" or "leetcode" or "codeforces" or "duolingo")
            .All(p => string.IsNullOrEmpty(p.Params.Username))
        && cat.Presets.Where(p => p.Type is VerifierType.FolderActivity or VerifierType.GitCommits)
            .All(p => string.IsNullOrEmpty(p.Params.Path)));
    Check("preset ids are unique", cat.Presets.Select(p => p.Id).Distinct().Count() == cat.Presets.Count);
}

Console.WriteLine(failures == 0 ? "\nAll checks passed." : $"\n{failures} check(s) failed.");
return failures == 0 ? 0 : 1;

sealed class Fake : IForegroundAppProvider, IIdleTimeProvider
{
    public string? Process;
    public string? Title;
    public int TitleCalls;
    public double Idle = 0;
    public string? FrontProcessName() => Process;
    public string? FrontWindowTitle() { TitleCalls++; return Title; }
    public double SecondsSinceInput() => Idle;
}

sealed class FakeHttp : IHttp
{
    public List<HttpRequest> Requests = new();
    public Func<HttpRequest, HttpResult> Respond = _ => new HttpResult(404, Array.Empty<byte>(), new Dictionary<string, string>());

    public Task<HttpResult> SendAsync(HttpRequest request, CancellationToken ct = default)
    {
        Requests.Add(request);
        return Task.FromResult(Respond(request));
    }

    public static HttpResult Ok(string body, string? etag = null) =>
        new(200, System.Text.Encoding.UTF8.GetBytes(body),
            etag is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["ETag"] = etag });
}
