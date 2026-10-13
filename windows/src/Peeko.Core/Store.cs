using System.Text.Json;
using System.Text.Json.Serialization;

namespace Peeko.Core;

/// <summary>
/// JSON files in %APPDATA%\Peeko (tasks.json, settings.json, days\yyyy-MM-dd.json), same shapes
/// as the macOS app: schemaVersion everywhere, additive changes only, atomic writes, and an
/// undecodable file is moved aside, never overwritten.
/// </summary>
public sealed class Store
{
    public static readonly JsonSerializerOptions Json = Build();

    private static JsonSerializerOptions Build()
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
        };
        o.Converters.Add(new IsoDateConverter());
        return o;
    }

    public string Root { get; }
    private string DaysDir => Path.Combine(Root, "days");

    public Store(string? root = null)
    {
        Root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Peeko");
    }

    public TaskList LoadTaskList() => Load<TaskList>(Path.Combine(Root, "tasks.json")) ?? new TaskList();
    public void Save(TaskList list) => Write(list, Path.Combine(Root, "tasks.json"));

    public AppSettings LoadSettings() => Load<AppSettings>(Path.Combine(Root, "settings.json")) ?? new AppSettings();
    public void Save(AppSettings settings) => Write(settings, Path.Combine(Root, "settings.json"));

    public DayRecord LoadDay(string key) =>
        Load<DayRecord>(Path.Combine(DaysDir, key + ".json")) ?? new DayRecord(key);

    public void Save(DayRecord record) => Write(record, Path.Combine(DaysDir, record.Day + ".json"));

    public WordBaselines LoadWordBaselines(string day) =>
        Load<WordBaselines>(Path.Combine(Root, "words", day + ".json")) ?? new WordBaselines(day);

    public void Save(WordBaselines baselines) =>
        Write(baselines, Path.Combine(Root, "words", baselines.Day + ".json"));

    public IReadOnlyList<string> AllDayKeys() =>
        !Directory.Exists(DaysDir)
            ? Array.Empty<string>()
            : Directory.GetFiles(DaysDir, "*.json")
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .Where(n => System.Text.RegularExpressions.Regex.IsMatch(n, @"^\d{4}-\d{2}-\d{2}$"))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

    private static T? Load<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json);
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            var stamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            try { File.Move(path, $"{path}.corrupt-{stamp}"); } catch (IOException) { }
            return null;
        }
    }

    private static void Write<T>(T value, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json));
        File.Move(tmp, path, overwrite: true);
    }

    public static Catalog LoadCatalog(string path) =>
        JsonSerializer.Deserialize<Catalog>(File.ReadAllText(path), Json) ?? new Catalog();
}
