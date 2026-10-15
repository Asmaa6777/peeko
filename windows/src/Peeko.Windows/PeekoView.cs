using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Peeko.Core;

namespace Peeko.Windows;

/// <summary>
/// Peeko's screens, drawn by hand so they match the SwiftUI look. One view, two layouts, like macOS:
/// <list type="bullet">
/// <item><b>Popover</b> (tray): header with face, wordmark, done / total and streak; Today / Explore /
/// History / Settings tabs; a fixed 400px scrolling content area; a footer. 320 logical px wide.</item>
/// <item><b>Window</b>: a 230px sidebar (face, the four sections with Ctrl+1–4, today's count and
/// streak) and a wide, centred content column. Explore becomes a searchable card grid with
/// category chips, and History shows 20 weeks.</item>
/// </list>
/// Layout is in logical px and scaled by DPI.
/// </summary>
internal sealed class PeekoView : Control
{
    public enum ViewMode { Popover, Window }
    private enum Tab { Today, Explore, History, Settings }

    public const float PopW = 320, HeaderH = 64, TabsH = 28, PopContentH = 400, FooterH = 36;
    public const float PopTotalH = HeaderH + TabsH + PopContentH + FooterH;
    private const float SidebarW = 230;
    private const string Version = "0.1.0";
    private const string Build = "build 10";

    private readonly ViewMode _mode;
    private readonly Scheduler _scheduler;
    private readonly Store _store;
    private readonly IReadOnlyList<TaskDefinition> _presets;
    private readonly Action _quit;
    private readonly Action _openWindow;
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 1000 };
    private readonly List<(RectangleF Rect, Action Click, bool Content)> _hits = new();
    private readonly TextBox _search;

    private readonly AppSettings _settings;
    private Tab _tab = Tab.Today;
    private float _scroll;
    private float _contentHeight;
    private float W = PopW;                       // width of the area the tab screens draw in
    private RectangleF _content;                  // the scrolling viewport, in window space
    private float _originX, _originY;             // content-space → window-space offset for this paint
    private PointF _mouse = new(-1, -1);
    private History? _past;
    private string? _pastFor;
    private bool _loggedPaintError;
    private bool _searchWanted;
    private RectangleF _searchRect;
    private TaskCategory? _category;

    /// <summary>True while a dialog this view opened is up, so the popover doesn't hide underneath it.</summary>
    public bool ShowingDialog { get; private set; }

    public PeekoView(ViewMode mode, Scheduler scheduler, Store store, AppSettings settings,
        IReadOnlyList<TaskDefinition> presets, Action quit, Action openWindow)
    {
        _mode = mode;
        _scheduler = scheduler;
        _store = store;
        _presets = presets;
        _quit = quit;
        _openWindow = openWindow;
        _settings = settings;

        BackColor = Theme.Night;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);

        _search = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.CardSolid,
            ForeColor = Theme.Chalk,
            PlaceholderText = "Search tasks: reading, sleep, YouTube…",
            Visible = false,
        };
        _search.TextChanged += (_, _) => { _scroll = 0; Invalidate(); };
        _search.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Escape) return;
            _search.Text = "";
            e.SuppressKeyPress = true;
        };
        Controls.Add(_search);

        _scheduler.Changed += () => { if (IsHandleCreated && !IsDisposed) BeginInvoke(Invalidate); };
        _refresh.Tick += (_, _) => { if (Visible && (Parent?.Visible ?? true)) Invalidate(); };
        _refresh.Start();
    }

    private float UiScale => DeviceDpi / 96f;

    // ── dialogs: flag them so the popover stays put while one is open

    private T Dialog<T>(Func<T> show)
    {
        ShowingDialog = true;
        try { return show(); }
        finally { ShowingDialog = false; }
    }

    private string? Ask(string title, string label, string initial = "", string okText = "Add", bool allowEmpty = false) =>
        Dialog(() => AskText.Ask(this, title, label, initial, okText, allowEmpty));

    private void Say(string message) => Dialog(() => MessageBox.Show(this, message, "Peeko"));

    // ───────────────────────── data

    private DateTime Today => DateTime.Now.Date;

    private History HistoryNow()
    {
        var key = History.Key(Today);
        if (_past is null || _pastFor != key)
        {
            _past = History.Load(_store, 20 * 7 + 7, Today);
            _pastFor = key;
        }
        return _past.With(_scheduler.Snapshot());
    }

    private (IReadOnlyList<TaskDefinition> Tasks, List<TaskState> States) Day()
    {
        var now = DateTimeOffset.Now;
        var tasks = _scheduler.Tasks;
        return (tasks, tasks.Select(t => _scheduler.StatusOf(t, now)).ToList());
    }

    // ───────────────────────── painting

    protected override void OnPaint(PaintEventArgs e)
    {
        try
        {
            Draw(e.Graphics);
        }
        catch (Exception ex)
        {
            if (_loggedPaintError) return;
            _loggedPaintError = true;
            try
            {
                Directory.CreateDirectory(_store.Root);
                File.WriteAllText(Path.Combine(_store.Root, "error.log"), ex.ToString());
            }
            catch { }
        }
    }

    private void Draw(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Theme.Night);
        g.ScaleTransform(UiScale, UiScale);
        _hits.Clear();
        _searchWanted = false;

        var (tasks, states) = Day();
        var history = HistoryNow();
        var today = Today;
        var streak = history.OverallStreak(today);
        var width = ClientSize.Width / UiScale;
        var height = ClientSize.Height / UiScale;

        float titleH = 0;
        if (_mode == ViewMode.Popover)
        {
            W = PopW;
            _content = new RectangleF(0, HeaderH + TabsH, PopW, PopContentH);
            _originX = 0;
            _originY = _content.Y + 10 - _scroll;
            DrawHeader(g, tasks, states, streak);
            DrawTabs(g);
        }
        else
        {
            DrawSidebar(g, tasks, states, streak, height);
            // A centred column: wide for the Explore grid and the history calendar, readable for lists.
            var detailX = SidebarW + 1;
            var available = Math.Max(width - detailX - 72, 260);
            var maxW = _tab is Tab.Explore or Tab.History ? 960 : 640;
            var columnW = Math.Min(maxW, available);
            var columnX = detailX + 36 + (available - columnW) / 2;
            W = columnW + 24;                       // the screens pad rows by 12 on each side
            _content = new RectangleF(detailX, 0, Math.Max(width - detailX, 0), height);
            _originX = columnX - 12;
            _originY = 48 - _scroll;
            titleH = 72;
        }

        var saved = g.Save();
        g.IntersectClip(_content);
        g.TranslateTransform(_originX, _originY);
        if (_mode == ViewMode.Window)
        {
            DrawPageTitle(g, tasks, states);
            g.TranslateTransform(0, titleH);
            _originY += titleH;
        }
        var end = _tab switch
        {
            Tab.Today => DrawToday(g, tasks, states),
            Tab.Explore => _mode == ViewMode.Window ? DrawExploreGrid(g) : DrawCatalog(g),
            Tab.History => DrawHistory(g, tasks, history, today, streak, _mode == ViewMode.Window ? 20 : 5),
            _ => DrawSettings(g),
        };
        g.Restore(saved);

        var bottomPad = _mode == ViewMode.Window ? 36 + 48 : 20;
        _contentHeight = end + titleH + bottomPad;
        var maxScroll = Math.Max(0, _contentHeight - _content.Height);
        _scroll = Math.Clamp(_scroll, 0, maxScroll);
        if (maxScroll > 0)
        {
            var h = _content.Height * _content.Height / _contentHeight;
            var y = _content.Y + (_content.Height - h) * (_scroll / maxScroll);
            Gfx.FillRound(g, Color.FromArgb(60, Theme.Muted), new RectangleF(_content.Right - 5, y, 3, h), 1.5f);
        }

        if (_mode == ViewMode.Popover) DrawFooter(g);
        PlaceSearch();
    }

    // ── window layout: sidebar and page title (MainWindowView on macOS)

    private void DrawSidebar(Graphics g, IReadOnlyList<TaskDefinition> tasks, List<TaskState> states, int streak, float height)
    {
        g.FillRectangle(Gfx.Brush(Theme.Sidebar), 0, 0, SidebarW, height);
        g.FillRectangle(Gfx.Brush(Theme.Separator), SidebarW, 0, 1, height);

        Gfx.Face(g, Moods.Day(states), 20, 0, 52);
        Txt(g, "peeko", Theme.Display28, Theme.Chalk, 84, 14);
        Txt(g, "peeks in, ticks off", Theme.Mono10, Theme.Muted, 85, 50);

        var y = 96f;
        var index = 0;
        foreach (var tab in Enum.GetValues<Tab>())
        {
            index++;
            var selected = tab == _tab;
            var item = new RectangleF(12, y, SidebarW - 24, 34);
            if (selected) Gfx.FillRound(g, Theme.Card, item, 8);
            else if (item.Contains(_mouse)) Gfx.FillRound(g, Color.FromArgb(8, 255, 255, 255), item, 8);
            Txt(g, NavGlyph(tab), Theme.Emoji(13), selected ? Theme.Green : Theme.Muted, item.X + 12, item.Y + 9);
            Txt(g, tab.ToString(), selected ? Theme.RowTitle : Theme.Body14, Theme.Chalk, item.X + 40, item.Y + 8);
            var shortcut = $"Ctrl+{index}";
            var sw = Measure(g, shortcut, Theme.Mono10);
            Txt(g, shortcut, Theme.Mono10, Color.FromArgb(153, Theme.Muted), item.Right - 12 - sw, item.Y + 11);
            var t = tab;
            _hits.Add((item, () => SelectTab(t), false));
            y += 34 + 4;
        }

        var bottom = height - 20;
        Txt(g, $"v{Version} · {Build}", Theme.Mono10, Color.FromArgb(179, Theme.Muted), 20, bottom - 14);
        bottom -= 22;
        if (streak > 0)
        {
            Txt(g, "🔥", Theme.Emoji(11), Theme.Green, 20, bottom - 15);
            Txt(g, streak == 1 ? "1 day in a row" : $"{streak} days in a row", Theme.Mono11, Theme.Muted, 38, bottom - 14);
            bottom -= 22;
        }
        if (tasks.Count > 0)
            Txt(g, $"{states.Count(s => s.IsDone)} / {tasks.Count} today", Theme.Mono13Bold, Theme.Chalk, 20, bottom - 16);
    }

    private static string NavGlyph(Tab tab) => tab switch
    {
        Tab.Today => "☑",
        Tab.Explore => "▦",
        Tab.History => "📅",
        _ => "⚙",
    };

    private void DrawPageTitle(Graphics g, IReadOnlyList<TaskDefinition> tasks, List<TaskState> states)
    {
        Txt(g, _tab.ToString(), Theme.Display34, Theme.Chalk, 12, 0);
        var subtitle = _tab switch
        {
            Tab.Today => tasks.Count == 0 ? "nothing to watch yet" : $"{states.Count(s => s.IsDone)} of {tasks.Count} done",
            Tab.Explore => "pick what Peeko keeps an eye on",
            Tab.History => "the last 20 weeks",
            _ => "how Peeko behaves",
        };
        Txt(g, subtitle, Theme.Mono12, Theme.Muted, 13, 46);
    }

    private void SelectTab(Tab tab)
    {
        _tab = tab;
        _scroll = 0;
        Invalidate();
    }

    private void DrawHeader(Graphics g, IReadOnlyList<TaskDefinition> tasks, List<TaskState> states, int streak)
    {
        Gfx.Face(g, Moods.Day(states), 16, 0, 48);
        Txt(g, "peeko", Theme.Display26, Theme.Chalk, 76, 6);
        Txt(g, "peeks in, ticks off", Theme.Mono11, Theme.Muted, 77, 40);

        if (tasks.Count == 0) return;
        var done = states.Count(s => s.IsDone);
        Txt(g, $"{done} / {tasks.Count}", Theme.Mono15Bold, Theme.Chalk, 16, 9, W - 32, StringAlignment.Far);

        // "🔥 N · today", right-aligned and muted, with the flame in green.
        var tail = streak > 0 ? $"{streak} · today" : "today";
        var tailW = Measure(g, tail, Theme.Mono11);
        var x = W - 16 - tailW;
        Txt(g, tail, Theme.Mono11, Theme.Muted, x, 31);
        if (streak > 0) Txt(g, "🔥", Theme.Emoji(11), Theme.Green, x - 16, 30);
    }

    private void DrawTabs(Graphics g)
    {
        var x = 16f;
        foreach (var tab in Enum.GetValues<Tab>())
        {
            var label = tab.ToString().ToUpperInvariant();
            var w = Measure(g, label, Theme.Mono11Bold) + 6;
            var active = tab == _tab;
            Txt(g, label, Theme.Mono11Bold, active ? Theme.Chalk : Theme.Muted, x, HeaderH + 3);
            if (active) Gfx.FillRound(g, Theme.Green, new RectangleF(x, HeaderH + 19, w - 6, 2), 1);
            var t = tab;
            _hits.Add((new RectangleF(x - 4, HeaderH, w + 8, TabsH), () => SelectTab(t), false));
            x += w + 14;
        }
    }

    private void DrawFooter(Graphics g)
    {
        var top = HeaderH + TabsH + PopContentH;
        g.FillRectangle(Gfx.Brush(Theme.FooterBg), 0, top, PopW, FooterH);
        Txt(g, $"v{Version} · {Build}", Theme.Mono11, Theme.Muted, 16, top + 11);
        var quitW = Measure(g, "Quit", Theme.Mono11);
        Txt(g, "Quit", Theme.Mono11, Theme.Muted, PopW - 16 - quitW, top + 11);
        _hits.Add((new RectangleF(PopW - 24 - quitW, top, quitW + 16, FooterH), _quit, false));
        const string open = "⤢ Open window";
        var openW = Measure(g, open, Theme.Mono11);
        var ox = PopW - 16 - quitW - 14 - openW;
        Txt(g, open, Theme.Mono11, Theme.Muted, ox, top + 11);
        _hits.Add((new RectangleF(ox - 6, top, openW + 12, FooterH), _openWindow, false));
    }

    // ───────────────────────── tabs

    private float DrawToday(Graphics g, IReadOnlyList<TaskDefinition> tasks, List<TaskState> states)
    {
        if (tasks.Count == 0)
        {
            var cy = PopContentH / 2 - 60;
            Gfx.Face(g, Mood.Dozing, W / 2 - 28, cy, 56);
            Txt(g, "Nothing to watch yet.", Theme.RowTitle, Theme.Chalk, 0, cy + 64, W, StringAlignment.Center);
            var pill = new RectangleF(W / 2 - 62, cy + 94, 124, 28);
            Gfx.FillRound(g, Theme.Green, pill, 14);
            Txt(g, "Pick some tasks", Theme.Mono12Bold, Theme.Night, pill.X, pill.Y + 7, pill.Width, StringAlignment.Center);
            AddHit(pill, () => SelectTab(Tab.Explore));
            return PopContentH - 20;
        }

        var y = 0f;
        for (var i = 0; i < tasks.Count; i++)
        {
            var task = tasks[i];
            var state = states[i];
            var row = new RectangleF(12, y, W - 24, 52);
            var manual = task.Type == VerifierType.Manual;
            var hover = manual && IsHover(row);
            Gfx.FillRound(g, hover ? Theme.CardHover : Theme.Card, row, 10);

            Gfx.Face(g, RowMood(task, state), row.X + 12, row.Y + (row.Height - 25) / 2, 30);
            var textX = row.X + 12 + 30 + 12;
            var textW = row.Right - 12 - 26 - textX - 8;
            Txt(g, task.Title, Theme.RowTitle, state.IsDone ? Theme.Green : Theme.Chalk, textX, row.Y + 8, textW);
            Txt(g, Subtitle(task, state), Theme.Mono11, Theme.Muted, textX, row.Y + 29, textW);

            var tr = new RectangleF(row.Right - 12 - 18, row.Y + (row.Height - 18) / 2, 18, 18);
            if (manual)
            {
                Gfx.CheckCircle(g, tr, state.IsDone);
                var id = task.Id;
                AddHit(row, () => { _scheduler.SetManual(id, !_scheduler.StatusOf(task, DateTimeOffset.Now).IsDone, DateTimeOffset.Now); Invalidate(); });
            }
            else if (state.IsDone)
            {
                Gfx.CheckCircle(g, tr, true);
            }
            else if (!Verifiers.IsVerifiable(task))
            {
                var label = VerifierType.IsMacOnly(task.Type) ? "Mac only" : "auto · soon";
                var w = Measure(g, label, Theme.Mono9) + 12;
                Gfx.StrokeRound(g, Color.FromArgb(102, Theme.Muted), new RectangleF(row.Right - 12 - w, row.Y + 17, w, 18), 9);
                Txt(g, label, Theme.Mono9, Theme.Muted, row.Right - 12 - w + 6, row.Y + 21);
            }
            else if (task.Type is VerifierType.AppTime or VerifierType.AppLimit
                     || (task.Type == VerifierType.FolderActivity && (task.Params.MinWords ?? 0) > 0))
            {
                var p = state is TaskState.InProgress ip ? ip.Progress : 0;
                // For limits the ring is time used, so it warms up as you near the limit.
                var color = task.Type == VerifierType.AppLimit && p >= 0.8 ? Theme.Blush : Theme.Green;
                Gfx.Ring(g, new RectangleF(tr.X + 1, tr.Y + 1, 16, 16), p, color);
            }
            y += 52 + 6;
        }
        return y - 6;
    }

    /// <summary>A GitHub task that's still waiting is "checking", so Peeko looks rather than dozes.</summary>
    private Mood RowMood(TaskDefinition task, TaskState state)
    {
        if (task.Type == VerifierType.RemoteCheck && task.Params.Source == "githubPush" && !state.IsDone
            && GithubUser(task).Length > 0)
            return Mood.Looking;
        return Moods.Of(state);
    }

    private string GithubUser(TaskDefinition task) =>
        string.IsNullOrEmpty(task.Params.Username) ? _settings.GithubUsername : task.Params.Username!;

    private string Subtitle(TaskDefinition task, TaskState state)
    {
        var p = task.Params;
        if (state is TaskState.Done d) return TaskDetail.Done(task, d.At);
        if (state is TaskState.Failed) return TaskDetail.Missed(task);
        if (VerifierType.IsMacOnly(task.Type)) return "only works in the macOS app";

        if (task.Type == VerifierType.AppLimit && p.Minutes == 0)
            return $"so far so good · until {p.Until ?? "noon"}";
        if (task.Type == VerifierType.AppLimit && state is TaskState.InProgress lp && p.Minutes is { } limit)
        {
            var used = (int)Math.Floor(lp.Progress * limit);
            var target = TaskDetail.LimitTarget(task);
            return $"{TaskDetail.Duration(used)} of {TaskDetail.Duration(limit)} used · {(target.Length > 3 ? target[3..] : target)}";
        }
        if (task.Type == VerifierType.AppTime && p.Continuous == true && state is TaskState.InProgress && p.Minutes is { } best)
            return $"best run {_scheduler.AccumulatedMinutes(task.Id)} of {best} min";
        if (task.Type == VerifierType.FolderTidy)
        {
            var count = FolderTidy.ItemCount(p.Path);
            return count is { } c ? $"{c} things on the Desktop now · aim for {p.MaxItems ?? 5}" : TaskDetail.Describe(task);
        }
        if (task.Type == VerifierType.FolderActivity && state is TaskState.InProgress fp && p.MinWords is { } words and > 0)
            return $"{(int)Math.Round(fp.Progress * words)} of {words} words · {TaskDetail.FolderName(task) ?? "notes"}";
        if (task.Type == VerifierType.RemoteCheck && _scheduler.OnlineProblem(task) is { } problem)
            return problem;
        if (task.Type == VerifierType.RemoteCheck && p.Source == "githubPush")
        {
            var user = GithubUser(task);
            return user.Length == 0 ? "add your GitHub username in Settings" : $"checking… · @{user}";
        }
        if (state is TaskState.InProgress && p.Minutes is { } target2 && task.Type == VerifierType.AppTime)
            return $"{_scheduler.AccumulatedMinutes(task.Id)} of {target2} min · {TaskDetail.AppNames(task)}";
        return TaskDetail.Describe(task);
    }

    // ── Explore: presets grouped by how Peeko checks them; switching one on asks for what it needs

    private float DrawCatalog(Graphics g)
    {
        var y = 0f;
        if (_presets.Count == 0)
            Txt(g, "No presets found (catalog.windows.json is missing).", Theme.Mono11, Theme.Muted, 16, y);

        foreach (var category in TaskCategory.All)
        {
            var inGroup = _presets.Where(p => TaskCategory.Of(p) == category).ToList();
            if (inGroup.Count == 0) continue;
            y = Section(g, category.Name, y, category.Color);
            foreach (var preset in inGroup)
            {
                var shown = _scheduler.EnabledCopy(preset.Id) ?? preset;
                var on = _scheduler.IsEnabled(preset.Id);
                var row = new RectangleF(12, y, W - 24, 52);
                Gfx.FillRound(g, Theme.Card, row, 10);
                IconAndText(g, row, shown, TaskDetail.Describe(shown));
                Gfx.Toggle(g, new RectangleF(row.Right - 12 - 30, row.Y + 18, 30, 16), on);
                var captured = preset;
                AddHit(row, () => TogglePreset(captured));
                y += 52 + 6;
            }
            y += 8;
        }

        y = Section(g, "Your tasks", y);
        var presetIds = _presets.Select(p => p.Id).ToHashSet();
        foreach (var task in _scheduler.Tasks.Where(t => !presetIds.Contains(t.Id)))
        {
            var row = new RectangleF(12, y, W - 24, 52);
            Gfx.FillRound(g, Theme.Card, row, 10);
            IconAndText(g, row, task, TaskDetail.Describe(task));
            Txt(g, "✕", Theme.Emoji(13), Theme.Muted, row.Right - 12 - 14, row.Y + 17);
            var def = task;
            AddHit(row, () => { _scheduler.SetEnabled(def, false); Invalidate(); });
            y += 52 + 6;
        }

        return DrawAddRow(g, y);
    }

    /// <summary>Off → on asks for what the preset needs (a folder, a username, a link); on → off just removes it.</summary>
    private void TogglePreset(TaskDefinition preset)
    {
        if (_scheduler.IsEnabled(preset.Id))
        {
            _scheduler.SetEnabled(preset, false);
            Invalidate();
            return;
        }

        var task = JsonClone(preset);
        var p = task.Params;
        switch (task.Type)
        {
            case VerifierType.FolderActivity when string.IsNullOrEmpty(p.Path):
            case VerifierType.GitCommits when string.IsNullOrEmpty(p.Path):
            {
                var message = task.Type == VerifierType.GitCommits
                    ? "Pick the folder that holds your code. Peeko looks for git repos up to two levels down."
                    : "Pick your notes folder or Obsidian vault. Peeko only reads .md and .txt files in it.";
                using var dialog = new FolderBrowserDialog { Description = message, UseDescriptionForTitle = true };
                if (Dialog(() => dialog.ShowDialog(this)) != DialogResult.OK) return;
                p.Path = dialog.SelectedPath;
                if (task.Type == VerifierType.GitCommits && string.IsNullOrEmpty(p.Author))
                    p.Author = _scheduler.GitEmail();
                break;
            }
            case VerifierType.RemoteCheck when p.Source == "feed" && string.IsNullOrEmpty(p.Url):
            {
                var link = Ask("Which feed?", "Paste the RSS or Atom link of your blog, newsletter or channel.", okText: "Switch on");
                if (link is null) return;
                if (OnlineChecks.FeedRequest(link) is null)
                {
                    Say("That doesn't look like a feed link (it should start with https://).");
                    return;
                }
                p.Url = link.Trim();
                break;
            }
            case VerifierType.RemoteCheck when p.Source == "githubPush":
            {
                if (_settings.GithubUsername.Length == 0 && string.IsNullOrEmpty(p.Username))
                {
                    var name = Ask("Your GitHub username",
                        "Public pushes only. Peeko checks for a push today.", okText: "Switch on");
                    if (name is null) return;
                    name = name.Trim().TrimStart('@');
                    if (!GitHubEventsClient.IsValidUsername(name))
                    {
                        Say("That doesn't look like a GitHub username.");
                        return;
                    }
                    SetGithubUsername(name);
                }
                break;
            }
            case VerifierType.RemoteCheck when string.IsNullOrEmpty(p.Username):
            {
                var (title, message) = p.Source switch
                {
                    "leetcode" => ("Your LeetCode username", "Your profile needs to be public. Peeko checks for an accepted solution today."),
                    "codeforces" => ("Your Codeforces handle", "Peeko checks for an accepted (OK) submission today."),
                    _ => ("Your Duolingo username", "Your profile needs to be public. Peeko checks whether your streak was extended today."),
                };
                var name = Ask(title, message, okText: "Switch on");
                if (name is null) return;
                p.Username = name.Trim().TrimStart('@');
                // A handle the site can't have would sit at "pending" forever; say so now instead.
                if (OnlineChecks.RequestFor(task) is null)
                {
                    Say($"That doesn't look like a {(p.Source == "codeforces" ? "Codeforces handle" : "username")}.");
                    return;
                }
                break;
            }
        }
        _scheduler.SetEnabled(task, true);
        Invalidate();
    }

    private static TaskDefinition JsonClone(TaskDefinition t) =>
        System.Text.Json.JsonSerializer.Deserialize<TaskDefinition>(
            System.Text.Json.JsonSerializer.Serialize(t, Store.Json), Store.Json)!;

    private void SetGithubUsername(string name)
    {
        _settings.GithubUsername = name;
        _store.Save(_settings);
        _scheduler.GithubUsername = name;
        _ = Task.Run(() => _scheduler.RefreshAsync(DateTimeOffset.Now));
    }

    // ── window Explore: search, category chips and a card grid (CatalogGridView on macOS)

    private bool Matches(TaskDefinition task)
    {
        var kind = TaskCategory.Of(task);
        if (_category is not null && kind != _category) return false;
        var q = _search.Text.Trim();
        if (q.Length == 0) return true;
        return new[] { task.Title, TaskDetail.Describe(task), kind.Name }
            .Any(text => text.Contains(q, StringComparison.CurrentCultureIgnoreCase));
    }

    private float DrawExploreGrid(Graphics g)
    {
        var y = 0f;

        // Search bar; the real TextBox is placed over it after painting.
        var bar = new RectangleF(12, y, W - 24, 40);
        Gfx.FillRound(g, Theme.Card, bar, 12);
        Gfx.StrokeRound(g, _search.Focused ? Color.FromArgb(153, Theme.Green) : Color.FromArgb(20, 255, 255, 255), bar, 12);
        Txt(g, "🔍", Theme.Emoji(13), Theme.Muted, bar.X + 14, bar.Y + 11);
        var hint = "Ctrl+F";
        var hintW = Measure(g, hint, Theme.Mono10);
        Txt(g, hint, Theme.Mono10, Color.FromArgb(153, Theme.Muted), bar.Right - 14 - hintW, bar.Y + 14);
        var textRight = bar.Right - 14 - hintW - 12;
        if (_search.Text.Length > 0)
        {
            var clear = new RectangleF(textRight - 18, bar.Y + 10, 18, 20);
            Txt(g, "✕", Theme.Emoji(12), Theme.Muted, clear.X + 2, clear.Y + 2);
            AddHit(clear, () => _search.Text = "");
            textRight -= 26;
        }
        _searchRect = new RectangleF(bar.X + 40, bar.Y + 11, Math.Max(textRight - bar.X - 40, 40), 20);
        _searchWanted = true;
        AddHit(bar, () => _search.Focus());
        y += 40 + 18;

        // Category chips; they wrap onto a second line when the window is narrow.
        var x = 12f;
        void Chip(string title, Color color, bool selected, Action click)
        {
            var textW = Measure(g, title, Theme.Mono11Bold);
            var w = textW + 24 + (selected ? 0 : 12);
            if (x + w > W - 12 && x > 12) { x = 12; y += 26 + 8; }
            var r = new RectangleF(x, y, w, 26);
            Gfx.FillRound(g, selected ? color : (IsHover(r) ? Theme.CardHover : Theme.Card), r, 13);
            var tx = r.X + 12;
            if (!selected)
            {
                g.FillEllipse(Gfx.Brush(color), tx, r.Y + 10, 6, 6);
                tx += 12;
            }
            Txt(g, title, Theme.Mono11Bold, selected ? Theme.Night : Color.FromArgb(217, Theme.Chalk), tx, r.Y + 6);
            AddHit(r, click);
            x += w + 8;
        }
        Chip("All", Theme.Chalk, _category is null, () => { _category = null; Invalidate(); });
        foreach (var category in TaskCategory.All)
        {
            var c = category;
            Chip(c.Name, c.Color, _category == c, () => { _category = _category == c ? null : c; _scroll = 0; Invalidate(); });
        }
        y += 26 + 18;

        var presetIds = _presets.Select(p => p.Id).ToHashSet();
        var presets = _presets.Where(p => Matches(_scheduler.EnabledCopy(p.Id) ?? p)).ToList();
        var custom = _scheduler.Tasks.Where(t => !presetIds.Contains(t.Id) && Matches(t)).ToList();

        if (presets.Count == 0 && custom.Count == 0)
        {
            Gfx.Face(g, Mood.Looking, W / 2 - 26, y + 16, 52);
            var q = _search.Text.Trim();
            Txt(g, q.Length == 0 ? "Nothing in this category yet." : $"Nothing matches “{q}”.", Theme.RowTitle, Theme.Chalk,
                0, y + 70, W, StringAlignment.Center);
            Txt(g, "Try another word, or add your own below.", Theme.Mono11, Theme.Muted, 0, y + 92, W, StringAlignment.Center);
            y += 130;
        }

        float Grid(IReadOnlyList<TaskDefinition> items, Func<TaskDefinition, bool> isOn, Action<TaskDefinition> click, bool deletable)
        {
            const float minW = 196, gap = 12, cardH = 168;
            var avail = W - 24;
            var columns = Math.Max(1, (int)((avail + gap) / (minW + gap)));
            var cardW = (avail - gap * (columns - 1)) / columns;
            for (var i = 0; i < items.Count; i++)
            {
                var col = i % columns;
                var row = i / columns;
                var r = new RectangleF(12 + col * (cardW + gap), y + row * (cardH + gap), cardW, cardH);
                DrawCard(g, r, items[i], isOn(items[i]), deletable);
                var item = items[i];
                AddHit(r, () => click(item));
            }
            var rows = (items.Count + columns - 1) / columns;
            return y + rows * (cardH + gap) - (rows > 0 ? gap : 0);
        }

        if (presets.Count > 0)
        {
            y = Section(g, "Presets", y);
            y = Grid(presets.Select(p => _scheduler.EnabledCopy(p.Id) ?? p).ToList(),
                t => _scheduler.IsEnabled(t.Id),
                t => TogglePreset(_presets.First(p => p.Id == t.Id)), false) + 18;
        }
        if (custom.Count > 0)
        {
            y = Section(g, "Your tasks", y);
            y = Grid(custom, _ => true, t => { _scheduler.SetEnabled(t, false); Invalidate(); }, true) + 18;
        }

        y = Section(g, "Add your own", y);
        return DrawAddRow(g, y);
    }

    private void DrawCard(Graphics g, RectangleF r, TaskDefinition task, bool on, bool deletable)
    {
        var kind = TaskCategory.Of(task);
        Gfx.FillRound(g, IsHover(r) ? Theme.CardHover : Theme.Card, r, 14);
        Gfx.StrokeRound(g, on ? Color.FromArgb(115, Theme.Green) : Color.FromArgb(15, 255, 255, 255), r, 14);

        var icon = new RectangleF(r.X + 16, r.Y + 16, 36, 36);
        Gfx.FillRound(g, Color.FromArgb(31, kind.Color), icon, 10);
        Txt(g, Glyph(task.Icon), Theme.Emoji(17), kind.Color, icon.X + 8, icon.Y + 7);

        if (deletable) Txt(g, "✕", Theme.Emoji(13), Theme.Muted, r.Right - 16 - 14, r.Y + 20);
        else Gfx.Toggle(g, new RectangleF(r.Right - 16 - 30, r.Y + 26, 30, 16), on);

        var textW = r.Width - 32;
        var titleH = TxtWrap(g, task.Title, Theme.Title15, Theme.Chalk, r.X + 16, r.Y + 62, textW, 2);
        TxtWrap(g, TaskDetail.Describe(task), Theme.Mono11, Theme.Muted, r.X + 16, r.Y + 62 + titleH + 6, textW, 2);
        Txt(g, $"· {kind.Name}", Theme.Mono10, Color.FromArgb(204, kind.Color), r.X + 16, r.Bottom - 28);
    }

    /// <summary>The "Custom manual task … Add" row, shared by both layouts.</summary>
    private float DrawAddRow(Graphics g, float y)
    {
        var add = new RectangleF(12, y, W - 24, 38);
        Gfx.StrokeRound(g, Theme.Outline, add, 10);
        Txt(g, "Custom manual task", Theme.Body13, Theme.Muted, 24, y + 10);
        var addW = Measure(g, "Add", Theme.Mono11Bold);
        Txt(g, "Add", Theme.Mono11Bold, Theme.Green, add.Right - 12 - addW, y + 12);
        AddHit(add, () =>
        {
            var title = Ask("New task", "What do you want to tick off each day?");
            if (title is not null) _scheduler.AddCustomManual(title);
            Invalidate();
        });
        return y + 38;
    }

    private Font? _searchFont;
    private float _searchFontScale;

    /// <summary>Puts the real search TextBox over the painted search bar, or hides it.</summary>
    private void PlaceSearch()
    {
        var r = ToWindow(_searchRect);
        var show = _searchWanted && _mode == ViewMode.Window && _tab == Tab.Explore
                   && r.Top >= _content.Top && r.Bottom <= _content.Bottom;
        if (show)
        {
            if (_searchFont is null || _searchFontScale != UiScale)
            {
                _searchFont?.Dispose();
                _searchFont = new Font(Theme.SearchFont.FontFamily, 14f * UiScale, GraphicsUnit.Pixel);
                _searchFontScale = UiScale;
                _search.Font = _searchFont;
            }
            var bounds = Rectangle.Round(new RectangleF(r.X * UiScale, r.Y * UiScale, r.Width * UiScale, r.Height * UiScale));
            if (_search.Bounds != bounds) _search.Bounds = bounds;
        }
        if (_search.Visible != show)
        {
            if (!show && _search.Focused) Focus();
            _search.Visible = show;
        }
    }

    private float DrawHistory(Graphics g, IReadOnlyList<TaskDefinition> tasks, History history, DateTime today, int streak, int weeks)
    {
        var y = 0f;
        // Streak card
        var card = new RectangleF(12, y, W - 24, 54);
        Gfx.FillRound(g, Theme.Card, card, 10);
        Gfx.Face(g, streak > 0 ? Mood.Beaming : Mood.Dozing, card.X + 12, card.Y + (54 - 33) / 2, 40);
        var head = streak == 0 ? "Start a streak today" : streak == 1 ? "1 day in a row" : $"{streak} days in a row";
        var sub = streak == 0 ? "Do one thing and Peeko will count it." : "with at least one thing done";
        Txt(g, head, Theme.Title16, Theme.Chalk, card.X + 12 + 40 + 12, card.Y + 9);
        Txt(g, sub, Theme.Mono11, Theme.Muted, card.X + 12 + 40 + 12, card.Y + 30, card.Width - 76);
        y += 54 + 14;

        // Heatmap: columns are weeks, rows are weekdays, starting on the culture's first weekday.
        y = Section(g, $"Last {weeks} weeks", y);
        var first = (int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var offset = ((int)today.DayOfWeek - first + 7) % 7;
        var lastWeekStart = today.AddDays(-offset);
        var firstDay = lastWeekStart.AddDays(-7 * (weeks - 1));
        var counts = Enumerable.Range(0, weeks * 7).Select(i => history.DoneCount(firstDay.AddDays(i))).ToArray();
        var peak = Math.Max(counts.Max(), 1);

        var panel = new RectangleF(12, y, W - 24, 7 * 18 + 6 * 4 + 18);
        Gfx.FillRound(g, Theme.Card, panel, 10);
        var names = CultureInfo.CurrentCulture.DateTimeFormat.ShortestDayNames;
        var cellW = (panel.Width - 24 - 12 - 4 * weeks) / weeks;
        for (var row = 0; row < 7; row++)
        {
            var cy = panel.Y + 9 + row * 22;
            var label = names[(first + row) % 7];
            Txt(g, label.Length > 0 ? label[..1] : "", Theme.Mono9, Theme.Muted, panel.X + 12, cy + 3, 12);
            for (var week = 0; week < weeks; week++)
            {
                var index = week * 7 + row;
                var date = firstDay.AddDays(index);
                var cx = panel.X + 12 + 12 + 4 + week * (cellW + 4);
                var cell = new RectangleF(cx, cy, cellW, 18);
                if (date > today) continue;
                var c = counts[index];
                var fill = c == 0 ? Color.FromArgb(15, 255, 255, 255)
                    : Color.FromArgb((int)(255 * (0.3 + 0.7 * c / peak)), Theme.Green);
                Gfx.FillRound(g, fill, cell, 4);
                if (date == today) Gfx.StrokeRound(g, Color.FromArgb(204, Theme.Chalk), cell, 4, 1.5f);
            }
        }
        y += panel.Height + 14;

        if (tasks.Count > 0)
        {
            y = Section(g, "By task", y);
            foreach (var task in tasks)
            {
                var row = new RectangleF(12, y, W - 24, 48);
                Gfx.FillRound(g, Theme.Card, row, 10);
                Txt(g, Glyph(task.Icon), Theme.Emoji(14), TaskCategory.Of(task).Color, row.X + 12, row.Y + 13);
                Txt(g, task.Title, Theme.Title13, Theme.Chalk, row.X + 12 + 20 + 12, row.Y + 8, row.Width - 120);
                Txt(g, $"{history.TimesDone(task.Id, 30, today)} of the last 30 days", Theme.Mono11, Theme.Muted,
                    row.X + 12 + 20 + 12, row.Y + 27, row.Width - 120);
                var s = history.StreakFor(task.Id, today);
                if (s > 0)
                {
                    var label = s.ToString();
                    var lw = Measure(g, label, Theme.Mono12Bold);
                    Txt(g, label, Theme.Mono12Bold, Theme.Chalk, row.Right - 12 - lw, row.Y + 15);
                    Txt(g, "🔥", Theme.Emoji(12), Theme.Green, row.Right - 12 - lw - 17, row.Y + 14);
                }
                y += 48 + 6;
            }
            y -= 6;
        }
        return y;
    }

    private float DrawSettings(Graphics g)
    {
        var y = 0f;
        var save = () => { _store.Save(_settings); Invalidate(); };

        // GitHub
        y = Section(g, "GitHub", y);
        var gh = new RectangleF(12, y, W - 24, 58);
        Gfx.FillRound(g, Theme.Card, gh, 10);
        var hasName = _settings.GithubUsername.Length > 0;
        Txt(g, hasName ? "@" + _settings.GithubUsername : "Add your username", Theme.Body13,
            hasName ? Theme.Chalk : Theme.Muted, 24, y + 10);
        var ghAction = hasName ? "Edit" : "Add";
        var ghW = Measure(g, ghAction, Theme.Mono11Bold);
        Txt(g, ghAction, Theme.Mono11Bold, Theme.Green, gh.Right - 12 - ghW, y + 12);
        Txt(g, "Public pushes only. Checked every 10 minutes.", Theme.Mono10Bold, Theme.Muted, 24, y + 34, W - 60);
        AddHit(gh, () =>
        {
            var name = Ask("GitHub username", "Public pushes only. Leave it empty to clear.",
                _settings.GithubUsername, "Save", allowEmpty: true);
            if (name is null) return;
            name = name.Trim().TrimStart('@');
            if (name.Length > 0 && !GitHubEventsClient.IsValidUsername(name))
            {
                Say("That doesn't look like a GitHub username.");
                return;
            }
            SetGithubUsername(name);
            Invalidate();
        });
        y += 58 + 14;

        // Notifications
        y = Section(g, "Notifications", y);
        var rows = 3 + (_settings.EveningNudgeEnabled ? 1 : 0) + (_settings.BreakRemindersEnabled ? 1 : 0);
        var card = new RectangleF(12, y, W - 24, rows * 38 + 6);
        Gfx.FillRound(g, Theme.Card, card, 10);
        var ry = y + 3;

        ToggleRow(g, card, ref ry, "Balloon when Peeko sees something", _settings.NotificationsEnabled, false,
            () => { _settings.NotificationsEnabled = !_settings.NotificationsEnabled; save(); });
        ToggleRow(g, card, ref ry, "Evening reminder", _settings.EveningNudgeEnabled, true,
            () => { _settings.EveningNudgeEnabled = !_settings.EveningNudgeEnabled; save(); });
        if (_settings.EveningNudgeEnabled)
        {
            var m = _settings.EveningNudgeMinutes;
            StepperRow(g, card, ref ry, "if anything's left at", $"{m / 60:00}:{m % 60:00}",
                () => { _settings.EveningNudgeMinutes = Math.Max(0, m - 30); save(); },
                () => { _settings.EveningNudgeMinutes = Math.Min(23 * 60 + 30, m + 30); save(); });
        }
        ToggleRow(g, card, ref ry, "Break reminders", _settings.BreakRemindersEnabled, true,
            () => { _settings.BreakRemindersEnabled = !_settings.BreakRemindersEnabled; save(); });
        if (_settings.BreakRemindersEnabled)
        {
            var n = _settings.BreakIntervalMinutes;
            StepperRow(g, card, ref ry, "a 5-min walk after every", $"{n} min",
                () => { _settings.BreakIntervalMinutes = Math.Max(30, n - 15); save(); },
                () => { _settings.BreakIntervalMinutes = Math.Min(120, n + 15); save(); });
        }
        y += card.Height + 14;

        // General
        y = Section(g, "General", y);
        var general = new RectangleF(12, y, W - 24, 4 * 38 + 6);
        Gfx.FillRound(g, Theme.Card, general, 10);
        ry = y + 3;
        ToggleRow(g, general, ref ry, "Launch at login", LaunchAtLogin, false, () => { LaunchAtLogin = !LaunchAtLogin; Invalidate(); });
        LinkRow(g, general, ref ry, "Your data", "Open in Explorer", () =>
        {
            Directory.CreateDirectory(_store.Root);
            Launch(new ProcessStartInfo("explorer.exe", $"\"{_store.Root}\""));
        });
        LinkRow(g, general, ref ry, "Everything in one file", "Export JSON…", ExportJson);
        LinkRow(g, general, ref ry, "Updates", "Releases on GitHub", () =>
            Launch(new ProcessStartInfo("https://github.com/STACK-42/peeko/releases") { UseShellExecute = true }));
        return y + general.Height;
    }

    private void ToggleRow(Graphics g, RectangleF card, ref float ry, string label, bool on, bool divider, Action click)
    {
        if (divider) g.FillRectangle(Gfx.Brush(Theme.Divider), card.X + 12, ry, card.Width - 24, 1);
        Txt(g, label, Theme.Body13, Theme.Chalk, 24, ry + 10, card.Width - 70);
        Gfx.Toggle(g, new RectangleF(card.Right - 12 - 30, ry + 11, 30, 16), on);
        AddHit(new RectangleF(card.X, ry, card.Width, 38), click);
        ry += 38;
    }

    /// <summary>"label        ‹ value ›": the hand-drawn stepper.</summary>
    private void StepperRow(Graphics g, RectangleF card, ref float ry, string label, string value, Action minus, Action plus)
    {
        Txt(g, label, Theme.Mono11, Theme.Muted, 24, ry + 12);
        var right = card.Right - 12;
        var plusRect = new RectangleF(right - 22, ry + 6, 22, 26);
        Txt(g, "›", Theme.Title16, Theme.Green, plusRect.X + 6, ry + 8);
        var valueW = Math.Max(Measure(g, value, Theme.Mono11Bold), 52);
        var valueX = plusRect.X - 4 - valueW;
        Txt(g, value, Theme.Mono11Bold, Theme.Chalk, valueX, ry + 12, valueW, StringAlignment.Center);
        var minusRect = new RectangleF(valueX - 26, ry + 6, 22, 26);
        Txt(g, "‹", Theme.Title16, Theme.Green, minusRect.X + 6, ry + 8);
        AddHit(plusRect, plus);
        AddHit(minusRect, minus);
        ry += 38;
    }

    /// <summary>Starts Explorer or the browser; a failure is a message, never a crash.</summary>
    private void Launch(ProcessStartInfo info)
    {
        try { Process.Start(info); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Say($"Couldn't open that: {e.Message}");
        }
    }

    private void LinkRow(Graphics g, RectangleF card, ref float ry, string label, string action, Action perform)
    {
        g.FillRectangle(Gfx.Brush(Theme.Divider), card.X + 12, ry, card.Width - 24, 1);
        Txt(g, label, Theme.Body13, Theme.Chalk, 24, ry + 10);
        var w = Measure(g, action, Theme.Mono11Bold);
        Txt(g, action, Theme.Mono11Bold, Theme.Green, card.Right - 12 - w, ry + 12);
        AddHit(new RectangleF(card.X, ry, card.Width, 38), perform);
        ry += 38;
    }

    // ───────────────────────── helpers

    private static string Glyph(string icon) => icon switch
    {
        "book" => "📖", "laptopcomputer" => "💻", "scope" => "🎯", "music.note" => "🎵",
        "paintpalette" => "🎨", "drop" => "💧", "checkmark.circle" => "✅", "github" => "🐙",
        "pencil" => "✏️", "figure.walk" => "🚶", "moon" => "🌙", "sun.max" => "☀️",
        _ => "●",
    };

    private static void IconAndText(Graphics g, RectangleF row, TaskDefinition task, string detail)
    {
        string icon = task.Icon, title = task.Title;
        Txt(g, Glyph(icon), Theme.Emoji(14), TaskCategory.Of(task).Color, row.X + 12, row.Y + 16);
        var x = row.X + 12 + 22 + 12;
        var w = row.Width - (x - row.X) - 12 - 30 - 8;
        Txt(g, title, Theme.RowTitle, Theme.Chalk, x, row.Y + 8, w);
        Txt(g, detail, Theme.Mono11, Theme.Muted, x, row.Y + 29, w);
    }

    private static float Section(Graphics g, string title, float y, Color? dot = null)
    {
        var x = 16f;
        if (dot is { } c)
        {
            g.FillEllipse(Gfx.Brush(c), x, y + 3, 6, 6);
            x += 11;
        }
        Txt(g, title.ToUpperInvariant(), Theme.Mono10Bold, Theme.Muted, x, y);
        return y + 18;
    }

    private static readonly StringFormat Near = new(StringFormat.GenericTypographic)
    {
        FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces,
        Trimming = StringTrimming.EllipsisCharacter,
    };

    private static void Txt(Graphics g, string s, Font font, Color color, float x, float y,
        float width = 0, StringAlignment align = StringAlignment.Near)
    {
        var w = width > 0 ? width : 1000;
        using var f = new StringFormat(Near) { Alignment = align };
        g.DrawString(s, font, Gfx.Brush(color), new RectangleF(x, y, w, font.Size * 1.6f), f);
    }

    private static readonly StringFormat Wrapping = new(StringFormat.GenericTypographic)
    {
        FormatFlags = StringFormatFlags.LineLimit,
        Trimming = StringTrimming.EllipsisWord,
    };

    /// <summary>Draws up to <paramref name="maxLines"/> wrapped lines and returns the height used.</summary>
    private static float TxtWrap(Graphics g, string s, Font font, Color color, float x, float y, float width, int maxLines)
    {
        var lineH = font.Size * 1.3f;
        var measured = g.MeasureString(s, font, new SizeF(width, lineH * maxLines), Wrapping).Height;
        var h = Math.Min(Math.Max(measured, lineH), lineH * maxLines);
        g.DrawString(s, font, Gfx.Brush(color), new RectangleF(x, y, width, h + 1), Wrapping);
        return h;
    }

    private static float Measure(Graphics g, string s, Font font) =>
        g.MeasureString(s, font, 1000, Near).Width;

    private RectangleF ToWindow(RectangleF r) => new(r.X + _originX, r.Y + _originY, r.Width, r.Height);

    private void AddHit(RectangleF contentSpace, Action click) => _hits.Add((ToWindow(contentSpace), click, true));

    private bool IsHover(RectangleF contentSpace) => ToWindow(contentSpace).Contains(_mouse) && _content.Contains(_mouse);

    // ───────────────────────── input

    private PointF ToLogical(Point p) => new(p.X / UiScale, p.Y / UiScale);

    private Action? HitAt(PointF p)
    {
        var inContent = _content.Contains(p);
        for (var i = _hits.Count - 1; i >= 0; i--)
        {
            var h = _hits[i];
            if (h.Rect.Contains(p) && (!h.Content || inContent)) return h.Click;
        }
        return null;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        // Wheel events go to the focused control.
        if (!_search.Focused) Focus();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _mouse = ToLogical(e.Location);
        Cursor = HitAt(_mouse) is null ? Cursors.Default : Cursors.Hand;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _mouse = new PointF(-1, -1);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        HitAt(ToLogical(e.Location))?.Invoke();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _scroll = Math.Clamp(_scroll - e.Delta / 120f * 40f, 0, Math.Max(0, _contentHeight - _content.Height));
        Invalidate();
    }

    /// <summary>Ctrl+1–4 switch sections, Ctrl+F searches Explore (window). The host forms forward keys here.</summary>
    public bool HandleKey(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        if ((keyData & Keys.Control) != 0 && key is >= Keys.D1 and <= Keys.D4)
        {
            SelectTab((Tab)(key - Keys.D1));
            return true;
        }
        if (keyData == (Keys.Control | Keys.F) && _mode == ViewMode.Window)
        {
            SelectTab(Tab.Explore);
            BeginInvoke(() => _search.Focus());
            return true;
        }
        return false;
    }

    protected override bool IsInputKey(Keys keyData) => base.IsInputKey(keyData);

    // ───────────────────────── settings actions

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static bool LaunchAtLogin
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("Peeko") is string;
        }
        set
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null) return;
            if (value) key.SetValue("Peeko", $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue("Peeko", throwOnMissingValue: false);
        }
    }

    private void ExportJson()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "JSON|*.json",
            FileName = $"peeko-export-{DateTime.Now:yyyy-MM-dd}.json",
        };
        if (Dialog(() => dialog.ShowDialog(this)) == DialogResult.OK)
            File.WriteAllText(dialog.FileName, Exporter.Make(_store, DateTimeOffset.Now));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refresh.Dispose();
            _search.Dispose();
        }
        base.Dispose(disposing);
    }
}
