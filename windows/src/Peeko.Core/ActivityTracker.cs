namespace Peeko.Core;

/// <summary>
/// Credits active time to appTime tasks, one tick at a time. Same rules as the macOS tracker:
/// credit is min(now - lastTick, 10s), the first tick after a pause only starts the clock,
/// and a tick counts only while the front app matches and the user isn't idle.
/// </summary>
public sealed class ActivityTracker
{
    public const double MaxDelta = 10;
    public const double DefaultIdleCutoff = 120;
    public const double RunGrace = 60;

    private DateTimeOffset? _lastTick;
    private readonly Dictionary<string, Run> _runs = new();

    private sealed class Run
    {
        public double Seconds;
        public DateTimeOffset? AwaySince;
    }

    public void Pause()
    {
        _lastTick = null;
        _runs.Clear();
    }

    public void Record(ActivitySample sample, IEnumerable<TaskDefinition> tasks, DayRecord record)
    {
        var delta = _lastTick is { } last
            ? Math.Min(Math.Max((sample.Now - last).TotalSeconds, 0), MaxDelta)
            : 0;
        _lastTick = sample.Now;
        if (delta <= 0) return;

        foreach (var task in tasks.Where(t => t.Type is VerifierType.AppTime or VerifierType.AppLimit))
        {
            var counts = Matches(task, sample);
            if (task.Params.Continuous == true)
            {
                if (!_runs.TryGetValue(task.Id, out var run)) _runs[task.Id] = run = new Run();
                if (counts)
                {
                    run.AwaySince = null;
                    run.Seconds += delta;
                    record.AccumulatedSeconds.TryGetValue(task.Id, out var best);
                    if (run.Seconds > best) record.AccumulatedSeconds[task.Id] = run.Seconds;
                }
                else if (run.AwaySince is { } away)
                {
                    if ((sample.Now - away).TotalSeconds > RunGrace) run.Seconds = 0;
                }
                else
                {
                    run.AwaySince = sample.Now;
                }
            }
            else if (counts)
            {
                record.AccumulatedSeconds.TryGetValue(task.Id, out var total);
                record.AccumulatedSeconds[task.Id] = total + delta;
            }
        }
    }

    /// <summary>Front app in the task's list (empty = any app), title filter, time window, not idle.</summary>
    private static bool Matches(TaskDefinition task, ActivitySample s)
    {
        if (s.FrontProcess is null) return false;
        var apps = task.Params.ProcessNames ?? new();
        if (apps.Count > 0 && !apps.Any(a => string.Equals(a, s.FrontProcess, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (task.NeedsWindowTitle)
        {
            var title = s.WindowTitle;
            if (title is null || !task.Params.TitleContains!.Any(t => title.Contains(t, StringComparison.OrdinalIgnoreCase)))
                return false;
        }
        if (ActivityWindow.Minutes(task.Params.From) is { } from && ActivityWindow.Minutes(task.Params.Until) is { } until)
        {
            var local = s.Now.ToLocalTime();
            var minute = local.Hour * 60 + local.Minute;
            if (minute < from || minute >= until) return false;
        }
        var cutoff = task.Params.IdleCutoffSeconds ?? DefaultIdleCutoff;
        return s.SecondsSinceInput <= cutoff;
    }
}
