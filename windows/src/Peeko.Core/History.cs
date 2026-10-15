namespace Peeko.Core;

/// <summary>Past days loaded from the day files, plus streak helpers. Port of the macOS History.</summary>
public sealed class History
{
    private readonly Dictionary<string, DayRecord> _days;

    public History(Dictionary<string, DayRecord> days) => _days = days;

    /// <summary>The <paramref name="count"/> days ending at <paramref name="date"/>. Missing days are empty records.</summary>
    public static History Load(Store store, int count, DateTime date)
    {
        var days = new Dictionary<string, DayRecord>();
        for (var i = 0; i < count; i++)
        {
            var key = Key(date.AddDays(-i));
            days[key] = store.LoadDay(key);
        }
        return new History(days);
    }

    /// <summary>A copy with the live record swapped in for today.</summary>
    public History With(DayRecord live)
    {
        var copy = new Dictionary<string, DayRecord>(_days) { [live.Day] = live };
        return new History(copy);
    }

    public static string Key(DateTime localDate) =>
        localDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public int DoneCount(DateTime date) =>
        _days.TryGetValue(Key(date), out var d) ? d.Statuses.Values.Count(s => s.IsDone) : 0;

    public bool IsDone(string taskId, DateTime date) =>
        _days.TryGetValue(Key(date), out var d) && d.Statuses.TryGetValue(taskId, out var s) && s.IsDone;

    /// <summary>Days in a row with at least one task done.</summary>
    public int OverallStreak(DateTime today) => Streak(today, d => DoneCount(d) > 0);

    public int StreakFor(string taskId, DateTime today) => Streak(today, d => IsDone(taskId, d));

    public int TimesDone(string taskId, int inLast, DateTime today) =>
        Enumerable.Range(0, inLast).Count(i => IsDone(taskId, today.AddDays(-i)));

    /// <summary>Today only extends a streak; it never breaks one, because the day isn't over yet.</summary>
    private int Streak(DateTime today, Func<DateTime, bool> isGoodDay)
    {
        var date = isGoodDay(today) ? today : today.AddDays(-1);
        var count = 0;
        while (_days.ContainsKey(Key(date)) && isGoodDay(date))
        {
            count++;
            date = date.AddDays(-1);
        }
        return count;
    }
}
