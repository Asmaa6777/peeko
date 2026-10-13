using Peeko.Core;

namespace Peeko.Windows;

/// <summary>
/// How Peeko checks a task, mirroring the macOS TaskCategory. Each group has its own color, used
/// only on icons and group headers, never on status: rings, ticks and faces stay green.
/// </summary>
internal sealed record TaskCategory(string Name, Color Color)
{
    public static readonly TaskCategory Time = new("Time in apps", Theme.Sky);
    public static readonly TaskCategory Limits = new("Limits", Theme.Blush);
    public static readonly TaskCategory Writing = new("Writing", Theme.Amber);
    public static readonly TaskCategory Routine = new("Sleep & mornings", Theme.Lavender);
    // "On your Mac" in the macOS app.
    public static readonly TaskCategory Computer = new("On your PC", Theme.Teal);
    public static readonly TaskCategory Online = new("Online", Theme.Peach);
    public static readonly TaskCategory Manual = new("Tap it yourself", Theme.Muted);

    /// <summary>Display order, same as the macOS enum.</summary>
    public static readonly TaskCategory[] All = { Time, Limits, Writing, Routine, Computer, Online, Manual };

    public static TaskCategory Of(TaskDefinition t) => t.Type switch
    {
        VerifierType.AppTime => Time,
        VerifierType.AppLimit => Limits,
        VerifierType.FolderActivity => Writing,
        VerifierType.Activity => Routine,
        VerifierType.RemoteCheck => Online,
        VerifierType.Manual => Manual,
        _ => Computer,   // gitCommits, folderTidy, backup, reminders
    };
}
