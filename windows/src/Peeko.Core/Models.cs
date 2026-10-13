using System.Text.Json;
using System.Text.Json.Serialization;

namespace Peeko.Core;

/// <summary>Task kinds. Kept as strings so files written by other platforms still load.</summary>
public static class VerifierType
{
    public const string Manual = "manual";
    public const string AppTime = "appTime";
    public const string RemoteCheck = "remoteCheck";
    public const string AppLimit = "appLimit";
    public const string Activity = "activity";
    public const string FolderActivity = "folderActivity";
    public const string GitCommits = "gitCommits";
    public const string FolderTidy = "folderTidy";
    /// <summary>macOS only: Time Machine.</summary>
    public const string Backup = "backup";
    /// <summary>macOS only: Reminders (EventKit).</summary>
    public const string Reminders = "reminders";

    /// <summary>Types that exist on macOS only. They stay in the file untouched and never tick here.</summary>
    public static bool IsMacOnly(string type) => type is Backup or Reminders;
}

public sealed class TaskParams
{
    /// <summary>Windows: executable names without ".exe", matched case-insensitively. Empty = any app.</summary>
    public List<string>? ProcessNames { get; set; }
    public int? Minutes { get; set; }
    public int? IdleCutoffSeconds { get; set; }
    public bool? Continuous { get; set; }
    public string? DoneNote { get; set; }
    public string? MissedNote { get; set; }

    /// <summary>appTime / appLimit: only count while the front window's title contains one of these.</summary>
    public List<string>? TitleContains { get; set; }
    /// <summary>remoteCheck: githubPush, feed, duolingo, leetcode, codeforces.</summary>
    public string? Source { get; set; }
    public string? Username { get; set; }
    public string? Url { get; set; }
    /// <summary>activity: quietBetween, startBetween, gapBetween, with from / until as "HH:mm".</summary>
    public string? Rule { get; set; }
    public string? From { get; set; }
    public string? Until { get; set; }
    public int? GapMinutes { get; set; }
    public int? Count { get; set; }
    /// <summary>folderActivity / gitCommits / folderTidy: a folder path ("~/Desktop" is expanded).</summary>
    public string? Path { get; set; }
    public List<string>? Extensions { get; set; }
    public int? MinWords { get; set; }
    public bool? DailyNote { get; set; }
    public int? MaxItems { get; set; }
    public string? Author { get; set; }

    /// <summary>Everything else (bundleIDs, url, ...) is kept so a save never drops a Mac task's data.</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class TaskDefinition
{
    public const string CustomIdPrefix = "custom-";

    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Type { get; set; } = VerifierType.Manual;
    public TaskParams Params { get; set; } = new();
    public string Icon { get; set; } = "";

    public bool IsAutomatic => Type != VerifierType.Manual;

    /// <summary>Only ask the OS for the window title when a task filters on it.</summary>
    public bool NeedsWindowTitle =>
        Type is VerifierType.AppTime or VerifierType.AppLimit && Params.TitleContains is { Count: > 0 };
}

public sealed class Catalog
{
    public int SchemaVersion { get; set; } = 1;
    public List<TaskDefinition> Presets { get; set; } = new();
}

public sealed class TaskList
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public List<TaskDefinition> Tasks { get; set; } = new();
}

public sealed class ActivitySpan
{
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
}

public sealed class DayRecord
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Day { get; set; } = "";
    public Dictionary<string, TaskState> Statuses { get; set; } = new();
    public Dictionary<string, double> AccumulatedSeconds { get; set; } = new();
    public List<ActivitySpan>? Activity { get; set; }

    public DayRecord() { }
    public DayRecord(string day) { Day = day; }
}

/// <summary>Local calendar day key, never UTC.</summary>
public static class DayKey
{
    public static string For(DateTimeOffset instant) =>
        instant.ToLocalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Local midnight at the start of a "yyyy-MM-dd" day, or null if the key is malformed.</summary>
    public static DateTimeOffset? StartOfDay(string key) =>
        DateTime.TryParseExact(key, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d)
            ? new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Local))
            : null;
}

/// <summary>Per-file word counts at the start of the day, saved once a file changes (words/yyyy-MM-dd.json).</summary>
public sealed class WordBaselines
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Day { get; set; } = "";
    /// <summary>task id → relative file path → word count</summary>
    public Dictionary<string, Dictionary<string, int>> Tasks { get; set; } = new();

    public WordBaselines() { }
    public WordBaselines(string day) { Day = day; }
}
