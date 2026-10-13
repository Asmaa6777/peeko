using System.Diagnostics;
using System.Globalization;

namespace Peeko.Core;

public sealed record FolderScanResult(DateTimeOffset? FirstEditToday, int WordsAdded, DateTimeOffset? DailyNote);

/// <summary>
/// Watches a notes folder by rescanning it at most once a minute. Words added today = for each file
/// changed today, its count now minus its count at the start of the day ("start of the day" is the
/// first scan after launch or midnight; once a file changes, that starting count is saved so a
/// restart keeps it). Files created today start at 0.
/// </summary>
public sealed class FolderScanner
{
    public static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(60);
    public static readonly string[] DefaultExtensions = { "md", "markdown", "txt" };
    private const int MaxFiles = 20_000;
    private const long MaxFileSize = 5_000_000;

    private sealed class State
    {
        public string Day = "";
        public DateTimeOffset LastScan;
        public FolderScanResult? Result;
        public Dictionary<string, int> Counts = new();
    }

    private sealed record Entry(string Path, string FullPath, DateTimeOffset Modified, DateTimeOffset Created);

    private readonly Store _store;
    private readonly Dictionary<string, State> _states = new();
    private WordBaselines? _baselines;
    private readonly object _gate = new();

    public FolderScanner(Store store) => _store = store;

    public FolderScanResult? Scan(string taskId, string folder, IEnumerable<string>? extensions, DateTimeOffset now)
    {
        lock (_gate)
        {
            var day = DayKey.For(now);
            if (_states.TryGetValue(taskId, out var prior) && prior.Day == day
                && now >= prior.LastScan && now - prior.LastScan < ScanInterval)
                return prior.Result;

            var exts = new HashSet<string>((extensions ?? DefaultExtensions).Select(e => e.TrimStart('.').ToLowerInvariant()));
            var files = List(folder, exts);
            if (files is null)
            {
                _states[taskId] = new State { Day = day, LastScan = now, Result = null, Counts = prior?.Counts ?? new() };
                return null;
            }

            var firstScan = prior is null;
            var counts = prior?.Counts ?? new Dictionary<string, int>();
            if (firstScan)
                foreach (var f in files) counts[f.Path] = Words(f.FullPath);

            if (_baselines?.Day != day) _baselines = _store.LoadWordBaselines(day);
            _baselines!.Tasks.TryGetValue(taskId, out var saved);
            saved ??= new Dictionary<string, int>();
            var savedChanged = false;

            var dayStart = DayKey.StartOfDay(day)!.Value;
            var dailyNote = files.Where(f => System.IO.Path.GetFileName(f.Path).Contains(day))
                .Select(f => (DateTimeOffset?)f.Modified).Max();
            var added = 0;
            DateTimeOffset? firstEdit = null;
            foreach (var file in files.Where(f => f.Modified >= dayStart))
            {
                firstEdit = firstEdit is null || file.Modified < firstEdit ? file.Modified : firstEdit;
                var current = firstScan ? counts.GetValueOrDefault(file.Path) : Words(file.FullPath);
                int baseline;
                if (saved.TryGetValue(file.Path, out var known))
                {
                    baseline = known;
                }
                else if (firstScan && file.Created >= dayStart)
                {
                    // Created today before our first look: all of it was written today.
                    baseline = 0;
                    saved[file.Path] = 0;
                    savedChanged = true;
                }
                else if (counts.TryGetValue(file.Path, out var previous) && previous == current)
                {
                    baseline = current;
                }
                else
                {
                    baseline = counts.GetValueOrDefault(file.Path);
                    saved[file.Path] = baseline;
                    savedChanged = true;
                }
                added += Math.Max(0, current - baseline);
                counts[file.Path] = current;
            }

            if (savedChanged)
            {
                _baselines.Tasks[taskId] = saved;
                try { _store.Save(_baselines); } catch (IOException) { }
            }

            var result = new FolderScanResult(firstEdit, added, dailyNote);
            _states[taskId] = new State { Day = day, LastScan = now, Result = result, Counts = counts };
            return result;
        }
    }

    /// <summary>null if the folder can't be read. Hidden files and folders are skipped.</summary>
    private static List<Entry>? List(string root, HashSet<string> extensions)
    {
        if (!Directory.Exists(root)) return null;
        var files = new List<Entry>();
        var rootFull = System.IO.Path.GetFullPath(root);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };
        try
        {
            foreach (var path in Directory.EnumerateFiles(rootFull, "*", options))
            {
                var rel = System.IO.Path.GetRelativePath(rootFull, path).Replace('\\', '/');
                if (rel.Split('/').Any(part => part.StartsWith('.'))) continue;
                if (!extensions.Contains(System.IO.Path.GetExtension(path).TrimStart('.').ToLowerInvariant())) continue;
                var info = new FileInfo(path);
                if (info.Length > MaxFileSize) continue;
                files.Add(new Entry(rel, path, info.LastWriteTimeUtc, info.CreationTimeUtc));
                if (files.Count >= MaxFiles) break;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
        return files;
    }

    /// <summary>Whitespace-separated tokens, which works for any script.</summary>
    public static int Words(string path)
    {
        try { return File.ReadAllText(path).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }
    }
}

public static class FolderTidy
{
    /// <summary>"~" and "%VAR%" expanded; defaults to the Desktop.</summary>
    public static string ExpandPath(string? path)
    {
        var p = string.IsNullOrWhiteSpace(path) ? "~/Desktop" : path.Trim();
        if (p == "~" || p.StartsWith("~/") || p.StartsWith("~\\"))
        {
            var rest = p.Length > 2 ? p[2..] : "";
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            // Known folders (the Desktop may be redirected to OneDrive).
            if (rest.Equals("Desktop", StringComparison.OrdinalIgnoreCase))
            {
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (!string.IsNullOrEmpty(desktop)) return desktop;
            }
            p = System.IO.Path.Combine(home, rest);
        }
        return Environment.ExpandEnvironmentVariables(p);
    }

    /// <summary>Visible items in the folder. null if it can't be read.</summary>
    public static int? ItemCount(string? path)
    {
        try
        {
            var dir = new DirectoryInfo(ExpandPath(path));
            return dir.EnumerateFileSystemInfos()
                .Count(i => (i.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0 && !i.Name.StartsWith('.'));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }
}

/// <summary>Finds git repos under a folder (up to two levels deep) and asks each for today's commits.</summary>
public sealed class GitScanner
{
    public static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(5);

    private readonly Func<string, string[], string> _run;
    private readonly Func<string?> _gitPath;
    private readonly Dictionary<string, (DateTimeOffset Scanned, DateTimeOffset? First)> _cache = new();
    private readonly object _gate = new();

    public GitScanner(Func<string, string[], string>? run = null, Func<string?>? gitPath = null)
    {
        _run = run ?? Run;
        _gitPath = gitPath ?? FindGit;
    }

    public static string? FindGit()
    {
        var names = new List<string>();
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(System.IO.Path.PathSeparator))
        {
            if (dir.Length == 0) continue;
            names.Add(System.IO.Path.Combine(dir, "git.exe"));
            names.Add(System.IO.Path.Combine(dir, "git"));
        }
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        names.Add(System.IO.Path.Combine(pf, "Git", "cmd", "git.exe"));
        names.Add(System.IO.Path.Combine(pf86, "Git", "cmd", "git.exe"));
        names.Add(System.IO.Path.Combine(local, "Programs", "Git", "cmd", "git.exe"));
        return names.FirstOrDefault(File.Exists);
    }

    /// <summary>Your git email, so "Committed code today" only counts your own commits.</summary>
    public string Email()
    {
        var git = _gitPath();
        return git is null ? "" : _run(git, new[] { "config", "--global", "user.email" }).Trim();
    }

    /// <summary>The earliest commit today by <paramref name="author"/> (anyone if empty) across repos under root.</summary>
    public DateTimeOffset? FirstCommitToday(string root, string author, DateTimeOffset now)
    {
        var key = $"{root}|{author}|{DayKey.For(now)}";
        lock (_gate)
            if (_cache.TryGetValue(key, out var hit) && now >= hit.Scanned && now - hit.Scanned < ScanInterval)
                return hit.First;

        var git = _gitPath();
        if (git is null) return null;
        var dayStart = DayKey.StartOfDay(DayKey.For(now))!.Value;
        var since = dayStart.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        DateTimeOffset? first = null;
        foreach (var repo in Repos(root))
        {
            var args = new List<string> { "-C", repo, "log", "--all", $"--since={since}", "--format=%ct" };
            if (author.Length > 0) args.Add($"--author={author}");
            foreach (var line in _run(git, args.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!long.TryParse(line.Trim(), out var seconds)) continue;
                var date = DateTimeOffset.FromUnixTimeSeconds(seconds);
                if (OnlineChecks.SameLocalDay(date, now) && (first is null || date < first)) first = date;
            }
        }
        lock (_gate) _cache[key] = (now, first);
        return first;
    }

    public static List<string> Repos(string root)
    {
        static bool IsRepo(string p) => Directory.Exists(System.IO.Path.Combine(p, ".git")) || File.Exists(System.IO.Path.Combine(p, ".git"));
        static IEnumerable<string> Children(string p)
        {
            try { return Directory.EnumerateDirectories(p).Where(d => !System.IO.Path.GetFileName(d).StartsWith('.')).ToList(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
        }
        if (IsRepo(root)) return new() { root };
        var found = new List<string>();
        foreach (var child in Children(root))
        {
            if (IsRepo(child)) found.Add(child);
            else found.AddRange(Children(child).Where(IsRepo));
        }
        return found;
    }

    private static string Run(string exe, string[] args)
    {
        var info = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) info.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(info);
            if (p is null) return "";
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(15_000);
            return output;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return ""; }
    }
}
