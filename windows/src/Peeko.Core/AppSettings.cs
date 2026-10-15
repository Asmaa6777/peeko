using System.Text.Json;
using System.Text.Json.Serialization;

namespace Peeko.Core;

/// <summary>settings.json. Unknown keys (from other platforms) are kept on save.</summary>
public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string GithubUsername { get; set; } = "";
    public bool EveningNudgeEnabled { get; set; } = true;
    /// <summary>Minutes after local midnight; 21:00 by default.</summary>
    public int EveningNudgeMinutes { get; set; } = 21 * 60;
    public bool HasCompletedOnboarding { get; set; }
    public bool BreakRemindersEnabled { get; set; } = true;
    public int BreakIntervalMinutes { get; set; } = 60;

    // Windows-only, additive: the macOS app ignores keys it doesn't know.
    /// <summary>Show a balloon when Peeko ticks a task off by itself.</summary>
    public bool NotificationsEnabled { get; set; } = true;
    /// <summary>The last day the evening nudge fired (the Mac keeps this in UserDefaults).</summary>
    public string? LastNudgeDay { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public static class Exporter
{
    /// <summary>Everything Peeko knows in one JSON document, the same idea as the macOS export.</summary>
    public static string Make(Store store, DateTimeOffset now) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            exportedAt = now,
            tasks = store.LoadTaskList(),
            settings = store.LoadSettings(),
            days = store.AllDayKeys().Select(store.LoadDay).ToList(),
        }, Store.Json);
}
