namespace Peeko.Core;

/// <summary>Stretches of keyboard/mouse activity, merged across gaps under 5 minutes.</summary>
public static class ActivityLog
{
    public static readonly TimeSpan MergeGap = TimeSpan.FromMinutes(5);

    /// <summary>Adds an input moment to the day's spans. Returns true when it starts a new span.</summary>
    public static bool Note(DateTimeOffset instant, List<ActivitySpan> spans, DateTimeOffset notBefore)
    {
        var t = instant > notBefore ? instant : notBefore;
        if (spans.Count > 0 && t - spans[^1].End <= MergeGap)
        {
            if (t > spans[^1].End) spans[^1].End = t;
            return false;
        }
        spans.Add(new ActivitySpan { Start = t, End = t });
        return true;
    }
}

/// <summary>
/// Judges a time window against today's activity spans.
/// quietBetween: no activity in the window. startBetween: a stretch *starts* inside the window.
/// gapBetween: <c>count</c> breaks of at least <c>gapMinutes</c> inside the window.
/// </summary>
public static class ActivityWindow
{
    public static readonly HashSet<string> Rules = new() { "quietBetween", "startBetween", "gapBetween" };

    /// <summary>"HH:mm" → minutes after midnight. "24:00" is allowed as the end of the day.</summary>
    public static int? Minutes(string? hhmm)
    {
        var parts = hhmm?.Split(':');
        if (parts is not { Length: 2 } || !int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m)) return null;
        if (m is < 0 or >= 60) return null;
        if (h == 24 && m == 0) return 24 * 60;
        return h is < 0 or >= 24 ? null : h * 60 + m;
    }

    public static TaskState Status(TaskDefinition task, DayRecord record, DateTimeOffset now)
    {
        if (record.Statuses.TryGetValue(task.Id, out var existing) && existing.IsDone) return existing;
        var p = task.Params;
        if (Minutes(p.From) is not { } from || Minutes(p.Until) is not { } until || from >= until
            || DayKey.StartOfDay(record.Day) is not { } dayStart)
            return new TaskState.Pending();

        var fromDate = dayStart.AddMinutes(from);
        var untilDate = dayStart.AddMinutes(until);
        var spans = (record.Activity ?? new()).OrderBy(s => s.Start).ToList();

        switch (p.Rule)
        {
            case "quietBetween":
                if (spans.Any(s => s.End >= fromDate && s.Start < untilDate)) return new TaskState.Failed();
                return now >= untilDate ? new TaskState.Done(untilDate) : new TaskState.Pending();
            case "startBetween":
                // Only stretches that *start* in the window count, so an all-nighter isn't an early morning.
                var starts = spans.Select(s => s.Start).Where(s => s >= fromDate).ToList();
                if (starts.Count > 0 && starts.Min() < untilDate) return new TaskState.Done(starts.Min());
                return now >= untilDate ? new TaskState.Failed() : new TaskState.Pending();
            case "gapBetween":
                return Gaps(task, spans, fromDate, untilDate, now);
            default:
                return new TaskState.Pending();
        }
    }

    /// <summary>
    /// Breaks are gaps between stretches of activity. Time before the day's first activity isn't a
    /// break, but a gap that's still going on (you're away right now) counts.
    /// </summary>
    private static TaskState Gaps(TaskDefinition task, List<ActivitySpan> spans, DateTimeOffset from, DateTimeOffset until, DateTimeOffset now)
    {
        var need = TimeSpan.FromMinutes(Math.Max(task.Params.GapMinutes ?? 30, 1));
        var wanted = Math.Max(task.Params.Count ?? 1, 1);
        if (spans.Count == 0) return now >= until ? new TaskState.Failed() : new TaskState.Pending();

        var windowEnd = until < now ? until : now;
        var cursor = from > spans[0].Start ? from : spans[0].Start;
        var found = 0;
        foreach (var span in spans.Where(s => s.End > cursor))
        {
            if (span.Start >= windowEnd) break;
            if (span.Start - cursor >= need)
            {
                found++;
                if (found >= wanted) return new TaskState.Done(cursor + need);
            }
            if (span.End > cursor) cursor = span.End;
        }
        if (windowEnd - cursor >= need)
        {
            found++;
            if (found >= wanted) return new TaskState.Done(cursor + need);
        }
        return now >= until ? new TaskState.Failed() : new TaskState.Pending();
    }
}
