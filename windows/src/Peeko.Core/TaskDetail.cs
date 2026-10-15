namespace Peeko.Core;

/// <summary>The words under each task. Gentle on purpose: hints, not verdicts.</summary>
public static class TaskDetail
{
    private static readonly Dictionary<string, string> Friendly = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Code"] = "VS Code", ["msedge"] = "Edge", ["chrome"] = "Chrome", ["firefox"] = "Firefox",
        ["WindowsTerminal"] = "Terminal", ["devenv"] = "Visual Studio", ["idea64"] = "IntelliJ",
        ["pycharm64"] = "PyCharm", ["AcroRd32"] = "Acrobat Reader", ["Acrobat"] = "Acrobat",
        ["SumatraPDF"] = "SumatraPDF", ["Obsidian"] = "Obsidian", ["Figma"] = "Figma",
        ["Photoshop"] = "Photoshop", ["Illustrator"] = "Illustrator",
    };

    public static string AppNames(TaskDefinition t) =>
        string.Join(" / ", (t.Params.ProcessNames ?? new()).Select(n => Friendly.GetValueOrDefault(n, n)));

    public static string Describe(TaskDefinition t)
    {
        switch (t.Type)
        {
            case VerifierType.Manual:
                return "tick it yourself";
            case VerifierType.AppTime:
                var minutes = t.Params.Minutes is { } m ? $"{m} min" : "time";
                var apps = AppNames(t);
                return apps.Length == 0 ? minutes : $"{minutes} in {apps}";
            case VerifierType.AppLimit when t.Params.Minutes == 0:
                return $"none {LimitTarget(t)} before {t.Params.Until ?? "noon"}";
            case VerifierType.AppLimit:
                return $"under {Duration(t.Params.Minutes ?? 0)} {LimitTarget(t)}";
            case VerifierType.RemoteCheck:
                return t.Params.Source switch
                {
                    "githubPush" => "a public push today",
                    "feed" => FeedHost(t) is { } host ? $"a new post on {host}" : "add a feed link in Explore",
                    "leetcode" or "codeforces" =>
                        string.IsNullOrEmpty(t.Params.Username)
                            ? $"add your {(t.Params.Source == "leetcode" ? "LeetCode" : "Codeforces")} username in Explore"
                            : $"an accepted solution today · @{t.Params.Username}",
                    "duolingo" => string.IsNullOrEmpty(t.Params.Username)
                        ? "add your username in Explore" : $"a lesson today · @{t.Params.Username}",
                    _ => "online check",
                };
            case VerifierType.Activity:
            {
                var p = t.Params;
                var from = p.From ?? "";
                var until = p.Until ?? "";
                return p.Rule switch
                {
                    "startBetween" => $"first on the PC before {until}",
                    "gapBetween" when (p.Count ?? 1) > 1 => $"{p.Count} breaks of {p.GapMinutes ?? 5}+ min, {from}–{until}",
                    "gapBetween" => $"a {p.GapMinutes ?? 30}-min break between {from} and {until}",
                    _ => until == "24:00" ? $"no screen time after {from}" : $"no screen time {from}–{until}",
                };
            }
            case VerifierType.FolderActivity:
            {
                if (FolderName(t) is not { } folder) return "pick a folder in Explore";
                if (t.Params.DailyNote == true) return $"today's dated note in {folder}";
                var words = t.Params.MinWords ?? 0;
                return words > 0 ? $"{words} words in {folder}" : $"any note in {folder}";
            }
            case VerifierType.GitCommits:
                return FolderName(t) is { } repoFolder ? $"a commit in {repoFolder} today" : "pick your code folder in Explore";
            case VerifierType.FolderTidy:
                return $"{t.Params.MaxItems ?? 5} things or fewer on the Desktop after {t.Params.From ?? "17:00"}";
            case VerifierType.Backup:
            case VerifierType.Reminders:
                return "macOS only";
            default:
                return "automatic check";
        }
    }

    /// <summary>"of YouTube" for title filters, "in Discord" for whole apps.</summary>
    public static string LimitTarget(TaskDefinition t)
    {
        if (t.Params.TitleContains is { Count: > 0 } titles)
            return "of " + string.Join(" / ", titles.Select(x => x.Trim(' ', '/')));
        var apps = AppNames(t);
        return apps.Length == 0 ? "of screen time" : "in " + apps;
    }

    public static string? FolderName(TaskDefinition t)
    {
        var path = t.Params.Path;
        if (string.IsNullOrEmpty(path)) return null;
        var trimmed = path.TrimEnd('/', '\\');
        var name = trimmed[(Math.Max(trimmed.LastIndexOf('/'), trimmed.LastIndexOf('\\')) + 1)..];
        return name.Length > 0 ? name : path;
    }

    public static string? FeedHost(TaskDefinition t) =>
        OnlineChecks.FeedRequest(t.Params.Url) is { } r ? r.Host.Replace("www.", "") : null;

    /// <summary>"45 min", "8 h", "1 h 30 min".</summary>
    public static string Duration(int minutes)
    {
        if (minutes < 60) return $"{minutes} min";
        int h = minutes / 60, m = minutes % 60;
        return m == 0 ? $"{h} h" : $"{h} h {m} min";
    }

    public static string Done(TaskDefinition t, DateTimeOffset at)
    {
        var time = at.ToLocalTime().ToString("t");
        if (t.Params.DoneNote is { } note) return note.Replace("{time}", time);
        return t.Type switch
        {
            VerifierType.Manual => $"done · {time}",
            VerifierType.Activity when t.Params.Rule == "quietBetween" => "looks like you slept on time",
            VerifierType.Activity => $"up and about at {time}",
            VerifierType.AppLimit => "stayed under · nice",
            _ => $"Peeko saw that! · {time}",
        };
    }

    /// <summary>Gentle on purpose: these are hints, not verdicts.</summary>
    public static string Missed(TaskDefinition t)
    {
        if (t.Params.MissedNote is { } note) return note;
        return t.Type switch
        {
            VerifierType.Activity when t.Params.Rule == "quietBetween" => "up late last night · no worries",
            VerifierType.Activity => "a slow morning · no worries",
            VerifierType.AppLimit => $"over today's {Duration(t.Params.Minutes ?? 0)} · fresh start tomorrow",
            _ => "not today · no worries",
        };
    }
}
