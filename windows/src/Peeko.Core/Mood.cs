namespace Peeko.Core;

public enum Mood { Dozing, Looking, Beaming }

public static class Moods
{
    /// <summary>pending/failed → dozing, inProgress → looking, done → beaming.</summary>
    public static Mood Of(TaskState s) => s switch
    {
        TaskState.Done => Mood.Beaming,
        TaskState.InProgress => Mood.Looking,
        _ => Mood.Dozing,
    };

    /// <summary>The day's overall mood: everything settled → beaming, some progress → looking.</summary>
    public static Mood Day(IReadOnlyCollection<TaskState> statuses)
    {
        var done = statuses.Count(s => s.IsDone);
        var open = statuses.Count(s => s is TaskState.Pending or TaskState.InProgress);
        if (done > 0 && open == 0) return Mood.Beaming;
        var anyProgress = statuses.Any(s => s is TaskState.Done or TaskState.InProgress);
        return anyProgress ? Mood.Looking : Mood.Dozing;
    }
}
