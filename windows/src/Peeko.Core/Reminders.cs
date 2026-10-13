namespace Peeko.Core;

/// <summary>
/// Counts active screen time (any app) and says when it's time for a short break. Stepping away for
/// five minutes or more counts as the break and restarts the count.
/// </summary>
public sealed class BreakTracker
{
    public static readonly TimeSpan BreakLength = TimeSpan.FromMinutes(5);
    /// <summary>Input within this window counts as being at the computer.</summary>
    public const double ActiveWindow = 120;

    private DateTimeOffset? _lastTick;
    public double ActiveSeconds { get; private set; }

    /// <summary>Returns true when a reminder is due. The count restarts after each reminder.</summary>
    public bool Record(ActivitySample sample, double intervalSeconds)
    {
        var last = _lastTick;
        _lastTick = sample.Now;

        // Idle that long, or no ticks that long (asleep, app not running): that was a break.
        var gap = last is { } l ? (sample.Now - l).TotalSeconds : 0;
        if (sample.SecondsSinceInput >= BreakLength.TotalSeconds || gap >= BreakLength.TotalSeconds)
        {
            ActiveSeconds = 0;
            return false;
        }
        if (last is null || sample.SecondsSinceInput > ActiveWindow) return false;

        ActiveSeconds += Math.Min(Math.Max(gap, 0), ActivityTracker.MaxDelta);
        if (ActiveSeconds < intervalSeconds) return false;
        ActiveSeconds = 0;
        return true;
    }
}

public static class BreakMessages
{
    public sealed record Message(string Title, string Body);

    /// <summary>Rotated so the reminder doesn't read the same every time.</summary>
    public static IReadOnlyList<Message> All(int minutes)
    {
        var time = Duration(minutes);
        return new[]
        {
            new Message("Time for a little walk",
                $"That's {time} at the screen. Stand up and walk around for 5 minutes. I'll keep an eye on things."),
            new Message("Stretch break?",
                $"You've been going for {time}. Give your legs and eyes 5 minutes off. Stand, stretch, maybe grab some water."),
            new Message("Peeko says: up you get",
                $"{char.ToUpperInvariant(time[0])}{time[1..]} in. Walk around for 5 minutes and you'll come back sharper."),
        };
    }

    public static string Duration(int minutes) => minutes == 60 ? "an hour" : $"{minutes} minutes";
}

public static class EveningNudge
{
    public static bool IsDue(DateTimeOffset now, AppSettings settings, string? lastNudgeDay, int remaining)
    {
        if (!settings.EveningNudgeEnabled || remaining <= 0) return false;
        var local = now.ToLocalTime();
        if (local.Hour * 60 + local.Minute < settings.EveningNudgeMinutes) return false;
        return lastNudgeDay != DayKey.For(now);
    }
}

/// <summary>A notification Peeko wants to show.</summary>
public sealed record Notice(string Id, string Title, string Body);

/// <summary>
/// Turns each tick into zero or more reminders: the evening "still peeking" nudge (once a day) and
/// the rotating break reminder. Pure apart from its own counters, so checks can drive it.
/// </summary>
public sealed class ReminderEngine
{
    private readonly BreakTracker _breaks = new();
    private int _messageIndex;

    public IEnumerable<Notice> OnTick(ActivitySample sample, AppSettings settings, int remaining, string? lastNudgeDay,
        out string? nudgedDay)
    {
        var notices = new List<Notice>();
        nudgedDay = null;

        if (EveningNudge.IsDue(sample.Now, settings, lastNudgeDay, remaining))
        {
            nudgedDay = DayKey.For(sample.Now);
            notices.Add(new Notice($"nudge-{nudgedDay}", "Peeko's still peeking",
                remaining == 1 ? "1 task left today." : $"{remaining} tasks left today."));
        }

        var interval = Math.Clamp(settings.BreakIntervalMinutes, 30, 120) * 60.0;
        if (_breaks.Record(sample, interval) && settings.BreakRemindersEnabled)
        {
            var messages = BreakMessages.All(settings.BreakIntervalMinutes);
            var message = messages[_messageIndex % messages.Count];
            _messageIndex++;
            notices.Add(new Notice($"break-{sample.Now.ToUnixTimeSeconds()}", message.Title, message.Body));
        }
        return notices;
    }
}
