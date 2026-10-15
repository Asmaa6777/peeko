namespace Peeko.Core;

/// <summary>
/// Owns today's DayRecord. Tick is the 10-second heartbeat: roll the day, sample, credit time, log
/// activity, evaluate every automatic task, save. All time goes through the 'now' argument so
/// checks can drive it. Network and disk checks run outside the lock, so a slow request never
/// blocks the window.
/// </summary>
public sealed class Scheduler : IDisposable
{
    public const int TickSeconds = 10;

    private readonly Store _store;
    private readonly IForegroundAppProvider _front;
    private readonly IIdleTimeProvider _idle;
    private readonly ActivityTracker _tracker = new();
    private readonly VerifierEnvironment _env;
    private readonly CachedFetcher _fetcher;
    private readonly GitHubEventsClient _github;
    private readonly GitScanner _git;
    private readonly object _gate = new();
    private System.Threading.Timer? _timer;
    private int _ticking;
    private DateTimeOffset _lastSave = DateTimeOffset.MinValue;
    private List<TaskDefinition> _tasks = new();

    public DayRecord Today { get; private set; }
    public string GithubUsername { get; set; } = "";

    /// <summary>Fires after any change to today's record (tick credit, manual tap, rollover).</summary>
    public event Action? Changed;
    /// <summary>Fires once when an automatic task flips to done. Never for manual taps.</summary>
    public event Action<TaskDefinition>? AutoCompleted;
    /// <summary>Fires after every tick with that tick's activity sample (reminders hang off this).</summary>
    public event Action<ActivitySample>? Ticked;

    public Scheduler(Store store, IForegroundAppProvider front, IIdleTimeProvider idle, DateTimeOffset now,
        IHttp? http = null, GitScanner? git = null)
    {
        _store = store;
        _front = front;
        _idle = idle;
        var net = http ?? new HttpClientAdapter();
        _fetcher = new CachedFetcher(net);
        _github = new GitHubEventsClient(net);
        _git = git ?? new GitScanner();
        _env = new VerifierEnvironment
        {
            GithubUsername = () => GithubUsername,
            Github = _github,
            Fetcher = _fetcher,
            Folders = new FolderScanner(store),
            Git = _git,
        };
        Today = store.LoadDay(DayKey.For(now));
    }

    public IReadOnlyList<TaskDefinition> Tasks
    {
        get { lock (_gate) return _tasks.ToList(); }
    }

    public void SetTasks(IEnumerable<TaskDefinition> tasks)
    {
        lock (_gate) _tasks = tasks.ToList();
        Changed?.Invoke();
    }

    public bool IsEnabled(string taskId)
    {
        lock (_gate) return _tasks.Any(t => t.Id == taskId);
    }

    /// <summary>The enabled copy of a preset carries the user's choices (folder, username).</summary>
    public TaskDefinition? EnabledCopy(string taskId)
    {
        lock (_gate) return _tasks.FirstOrDefault(t => t.Id == taskId);
    }

    /// <summary>Turns a task on (a copy is saved to tasks.json) or off, then re-checks right away.</summary>
    public void SetEnabled(TaskDefinition task, bool on)
    {
        lock (_gate)
        {
            if (on && !_tasks.Any(t => t.Id == task.Id)) _tasks.Add(Clone(task));
            else if (!on) _tasks.RemoveAll(t => t.Id == task.Id);
            _store.Save(new TaskList { Tasks = _tasks.ToList() });
        }
        Changed?.Invoke();
        if (on) _ = Task.Run(() => RefreshAsync(DateTimeOffset.Now));
    }

    public TaskDefinition AddCustomManual(string title)
    {
        var task = new TaskDefinition
        {
            Id = TaskDefinition.CustomIdPrefix + Guid.NewGuid().ToString("N")[..8],
            Title = title.Trim(),
            Type = VerifierType.Manual,
            Icon = "checkmark.circle",
        };
        lock (_gate)
        {
            _tasks.Add(task);
            _store.Save(new TaskList { Tasks = _tasks.ToList() });
        }
        Changed?.Invoke();
        return task;
    }

    /// <summary>Your git email, so "Committed code today" only counts your own commits.</summary>
    public string GitEmail() => _git.Email();

    public int AccumulatedMinutes(string taskId)
    {
        lock (_gate)
            return Today.AccumulatedSeconds.TryGetValue(taskId, out var s) ? (int)(s / 60) : 0;
    }

    /// <summary>A copy of today's record that is safe to read from another thread.</summary>
    public DayRecord Snapshot()
    {
        lock (_gate) return CopyOfToday();
    }

    private DayRecord CopyOfToday() => new(Today.Day)
    {
        Statuses = new(Today.Statuses),
        AccumulatedSeconds = new(Today.AccumulatedSeconds),
        Activity = Today.Activity?.Select(s => new ActivitySpan { Start = s.Start, End = s.End }).ToList(),
    };

    /// <summary>What went wrong with an online check's last request, in words for the UI.</summary>
    public string? OnlineProblem(TaskDefinition task)
    {
        if (task.Type != VerifierType.RemoteCheck) return null;
        if (task.Params.Source == "githubPush")
        {
            var user = string.IsNullOrEmpty(task.Params.Username) ? GithubUsername : task.Params.Username!;
            return user.Length == 0 ? null : _github.ProblemFor(user);
        }
        var request = OnlineChecks.RequestFor(task);
        return request is null ? null : _fetcher.ProblemFor(request);
    }

    /// <summary>Tasks that can still happen today. Limits never need doing.</summary>
    public int OpenCount(DateTimeOffset now)
    {
        List<TaskDefinition> tasks;
        lock (_gate) tasks = _tasks.ToList();
        return tasks.Count(t =>
            t.Type != VerifierType.AppLimit && Verifiers.IsVerifiable(t)
            && StatusOf(t, now) is TaskState.Pending or TaskState.InProgress);
    }

    /// <summary>Pure verifiers are computed live; the rest show what the last tick stored.</summary>
    public TaskState StatusOf(TaskDefinition task, DateTimeOffset now)
    {
        lock (_gate) return Verifiers.Evaluate(task, Today, now);
    }

    public void SetManual(string taskId, bool done, DateTimeOffset now)
    {
        lock (_gate)
        {
            RollOver(now);
            Today.Statuses[taskId] = done ? new TaskState.Done(now) : new TaskState.Pending();
            Save(now);
        }
        Changed?.Invoke();
    }

    public void Start()
    {
        _timer ??= new System.Threading.Timer(_ =>
        {
            // Skip a beat rather than overlap if a network check is slow.
            if (Interlocked.Exchange(ref _ticking, 1) == 1) return;
            try { Tick(DateTimeOffset.Now); }
            catch (Exception) { /* a failed beat is retried at the next one */ }
            finally { Interlocked.Exchange(ref _ticking, 0); }
        }, null, TimeSpan.FromSeconds(TickSeconds), TimeSpan.FromSeconds(TickSeconds));
    }

    /// <summary>Sleep, lock or shutdown: the next tick only restarts the clock.</summary>
    public void Pause()
    {
        lock (_gate) _tracker.Pause();
    }

    public void Tick(DateTimeOffset now) => TickAsync(now).GetAwaiter().GetResult();

    public async Task TickAsync(DateTimeOffset now)
    {
        ActivitySample sample;
        bool creditedOrMoved;
        lock (_gate)
        {
            var rolled = RollOver(now);
            var title = _tasks.Any(t => t.NeedsWindowTitle) ? _front.FrontWindowTitle() : null;
            sample = new ActivitySample(now, _front.FrontProcessName(), _idle.SecondsSinceInput(), title);
            var before = Today.AccumulatedSeconds.Sum(kv => kv.Value);
            _tracker.Record(sample, _tasks, Today);
            var credited = Today.AccumulatedSeconds.Sum(kv => kv.Value) != before;
            var (started, moved) = NoteActivity(sample);
            creditedOrMoved = rolled || credited || started || (moved && now - _lastSave >= TimeSpan.FromSeconds(60));
        }

        var statusesChanged = await EvaluateAutomaticAsync(now).ConfigureAwait(false);
        if (creditedOrMoved || statusesChanged)
            lock (_gate) Save(now);

        Changed?.Invoke();
        Ticked?.Invoke(sample);
    }

    /// <summary>Re-evaluates without crediting time, e.g. right after a task is enabled.</summary>
    public async Task RefreshAsync(DateTimeOffset now)
    {
        if (await EvaluateAutomaticAsync(now).ConfigureAwait(false))
        {
            lock (_gate) Save(now);
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Manual tasks are skipped: the record is their source of truth, and writing back an evaluated
    /// copy after an await could undo a tap made in between.
    /// </summary>
    private async Task<bool> EvaluateAutomaticAsync(DateTimeOffset now)
    {
        List<TaskDefinition> tasks;
        DayRecord snapshot;
        lock (_gate)
        {
            tasks = _tasks.Where(t => t.IsAutomatic && Verifiers.IsVerifiable(t)).ToList();
            snapshot = CopyOfToday();
        }

        var results = new Dictionary<string, TaskState>();
        foreach (var task in tasks)
        {
            try { results[task.Id] = await Verifiers.EvaluateAsync(task, snapshot, now, _env).ConfigureAwait(false); }
            catch (Exception) { /* a broken check must not take the tick down */ }
        }

        var completed = new List<TaskDefinition>();
        var changed = false;
        lock (_gate)
        {
            if (Today.Day != snapshot.Day) return false;   // rolled over while we were away
            foreach (var task in tasks)
            {
                if (!results.TryGetValue(task.Id, out var status)) continue;
                Today.Statuses.TryGetValue(task.Id, out var stored);
                if (Equals(stored, status)) continue;
                if (stored is null && status is TaskState.Pending) continue;
                Today.Statuses[task.Id] = status;
                changed = true;
                if (status.IsDone && stored?.IsDone != true) completed.Add(task);
            }
        }
        foreach (var task in completed) AutoCompleted?.Invoke(task);
        return changed;
    }

    private (bool Started, bool Moved) NoteActivity(ActivitySample s)
    {
        if (s.SecondsSinceInput > BreakTracker.ActiveWindow) return (false, false);
        var spans = Today.Activity ?? new List<ActivitySpan>();
        var lastEnd = spans.Count > 0 ? spans[^1].End : (DateTimeOffset?)null;
        var count = spans.Count;
        var dayStart = DayKey.StartOfDay(DayKey.For(s.Now))!.Value;
        var started = ActivityLog.Note(s.Now.AddSeconds(-s.SecondsSinceInput), spans, dayStart);
        var moved = started || spans.Count != count || (spans.Count > 0 && lastEnd != spans[^1].End);
        if (moved) Today.Activity = spans;
        return (started, moved);
    }

    /// <summary>Moves to a new day file. Returns true if the day changed.</summary>
    private bool RollOver(DateTimeOffset now)
    {
        var key = DayKey.For(now);
        if (key == Today.Day) return false;
        FinalizeDay();
        _store.Save(Today);
        Today = _store.LoadDay(key);
        return true;
    }

    /// <summary>
    /// Settles the day that's ending, even if the PC slept through midnight: time-window tasks get
    /// their verdict, and limits still under their cap count as done.
    /// </summary>
    private void FinalizeDay()
    {
        if (DayKey.StartOfDay(Today.Day) is not { } start) return;
        var end = start.AddDays(1).AddSeconds(-1);
        foreach (var task in _tasks)
        {
            TaskState? settled = null;
            if (task.Type == VerifierType.Activity)
            {
                var s = ActivityWindow.Status(task, Today, end.AddSeconds(1));
                if (s is not TaskState.Pending) settled = s;
            }
            else if (task.Type == VerifierType.AppLimit)
            {
                var s = Verifiers.Evaluate(task, Today, end);
                settled = s is TaskState.Failed ? s : new TaskState.Done(end);
            }
            if (settled is not null && !Equals(Today.Statuses.GetValueOrDefault(task.Id), settled))
                Today.Statuses[task.Id] = settled;
        }
    }

    private void Save(DateTimeOffset now)
    {
        _lastSave = now;
        try { _store.Save(Today); } catch (IOException) { /* retried on the next change */ }
    }

    private static TaskDefinition Clone(TaskDefinition t) =>
        System.Text.Json.JsonSerializer.Deserialize<TaskDefinition>(
            System.Text.Json.JsonSerializer.Serialize(t, Store.Json), Store.Json)!;

    public void Dispose() => _timer?.Dispose();
}
