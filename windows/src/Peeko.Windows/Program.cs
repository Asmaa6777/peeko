using Microsoft.Win32;
using Peeko.Core;

namespace Peeko.Windows;

internal static class Program
{
    private const string MutexName = @"Local\com.a7med7x7.peeko";
    private const string ShowEventName = @"Local\com.a7med7x7.peeko.show";

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, MutexName, out var first);
        if (!first)
        {
            // Another copy is running: ask it to bring its window forward, then leave quietly.
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
            return;
        }

        ApplicationConfiguration.Initialize();
        try
        {
            Application.Run(new TrayContext(ShowEventName));
        }
        catch (Exception e)
        {
            // A tray app has no console, so a crash would otherwise look like "nothing happened".
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Peeko");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "error.log"), e.ToString());
            MessageBox.Show($"Peeko couldn't start.\n\n{e.Message}\n\nDetails: {Path.Combine(dir, "error.log")}",
                "Peeko", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

/// <summary>
/// Peeko on Windows, shaped like the macOS app: a tray icon (the face, following the day's mood)
/// that opens a popover with today's list, plus a full window with a sidebar. Right-click the
/// icon for a quick menu.
/// </summary>
internal sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly Scheduler _scheduler;
    private readonly Store _store = new();
    // Marshals scheduler-thread events onto the UI thread.
    private readonly Control _ui = new();
    private readonly MainWindow _window;
    private readonly PopoverForm _popover;
    private readonly AppSettings _settings;
    private readonly ReminderEngine _reminders = new();
    private Mood? _shownMood;
    private readonly EventWaitHandle _showSignal;

    public TrayContext(string showEventName)
    {
        _ = _ui.Handle;
        var front = new Win32ForegroundApp();
        var idle = new Win32IdleTime();
        _settings = _store.LoadSettings();
        _scheduler = new Scheduler(_store, front, idle, DateTimeOffset.Now) { GithubUsername = _settings.GithubUsername };
        _scheduler.SetTasks(LoadTasks());
        _scheduler.Ticked += OnTicked;
        _scheduler.AutoCompleted += task => _ui.BeginInvoke(() => Notify(task));

        SystemEvents.PowerModeChanged += (_, e) =>
        {
            if (e.Mode == PowerModes.Suspend) _scheduler.Pause();
        };
        SystemEvents.SessionSwitch += (_, e) =>
        {
            if (e.Reason is SessionSwitchReason.SessionLock) _scheduler.Pause();
        };

        var presets = LoadPresets();
        _window = new MainWindow(new PeekoView(PeekoView.ViewMode.Window, _scheduler, _store, _settings, presets,
            ExitThread, OpenWindow));
        _popover = new PopoverForm(new PeekoView(PeekoView.ViewMode.Popover, _scheduler, _store, _settings, presets,
            ExitThread, OpenWindow));

        _tray = new NotifyIcon
        {
            Icon = Gfx.FaceIcon(Mood.Dozing),
            Text = "Peeko",
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip(),
        };
        _tray.ContextMenuStrip.Opening += (_, _) => BuildMenu();
        // Left-click: the popover, like clicking the menu bar icon on a Mac. Right-click: quick menu.
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) _popover.Toggle();
        };

        // A second launch signals us to show the window.
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, showEventName);
        var waiter = new Thread(() =>
        {
            while (_showSignal.WaitOne()) _ui.BeginInvoke(() => _window.Bring());
        }) { IsBackground = true };
        waiter.Start();

        _scheduler.Changed += () => _ui.BeginInvoke(UpdateIcon);
        UpdateIcon();

        _scheduler.Start();
        // First launch opens the window so Peeko is easy to find; after that it lives in the tray.
        _window.Bring();
    }

    private void OpenWindow()
    {
        _popover.HideAway();
        _window.Bring();
    }

    /// <summary>The tray face follows the day's mood, like the menu bar icon on macOS.</summary>
    private void UpdateIcon()
    {
        var now = DateTimeOffset.Now;
        var mood = Moods.Day(_scheduler.Tasks.Select(t => _scheduler.StatusOf(t, now)).ToList());
        if (mood == _shownMood) return;
        _shownMood = mood;
        var icon = Gfx.FaceIcon(mood);
        _tray.Icon = icon;
        _window.Icon = icon;
    }

    private static List<TaskDefinition> LoadPresets()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "catalog.windows.json");
        return File.Exists(path)
            ? Store.LoadCatalog(path).Presets.Where(Verifiers.IsVerifiable).ToList()
            : new();
    }

    /// <summary>
    /// tasks.json if there is one. On the very first run: the presets that need no setup (app time
    /// and manual ones), saved so the list isn't empty. Everything else is a switch in Explore.
    /// </summary>
    private List<TaskDefinition> LoadTasks()
    {
        var list = _store.LoadTaskList();
        if (list.Tasks.Count > 0) return list.Tasks;

        var starter = LoadPresets()
            .Where(p => p.Type is VerifierType.Manual or VerifierType.AppTime)
            .ToList();
        _store.Save(new TaskList { Tasks = starter });
        return starter;
    }

    /// <summary>Evening nudge and break reminders. Runs on the scheduler's thread, shows on the UI thread.</summary>
    private void OnTicked(ActivitySample sample)
    {
        var remaining = _scheduler.OpenCount(sample.Now);
        var notices = _reminders.OnTick(sample, _settings, remaining, _settings.LastNudgeDay, out var nudgedDay).ToList();
        if (nudgedDay is not null)
        {
            _settings.LastNudgeDay = nudgedDay;
            try { _store.Save(_settings); } catch (IOException) { }
        }
        foreach (var notice in notices)
            _ui.BeginInvoke(() => _tray.ShowBalloonTip(8000, notice.Title, notice.Body, ToolTipIcon.None));
    }

    private void BuildMenu()
    {
        var menu = _tray.ContextMenuStrip!;
        menu.Items.Clear();
        var now = DateTimeOffset.Now;
        var tasks = _scheduler.Tasks;
        var done = tasks.Count(t => _scheduler.StatusOf(t, now).IsDone);
        menu.Items.Add(new ToolStripLabel($"Peeko  ·  {done} / {tasks.Count} today"));
        menu.Items.Add(new ToolStripSeparator());
        foreach (var task in tasks)
        {
            var status = _scheduler.StatusOf(task, now);
            var item = new ToolStripMenuItem($"{Mark(status)}  {task.Title}{Detail(task, status)}");
            if (task.IsAutomatic) item.Enabled = false;
            else
            {
                var id = task.Id;
                item.Click += (_, _) => _scheduler.SetManual(id, !status.IsDone, DateTimeOffset.Now);
            }
            menu.Items.Add(item);
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open window", null, (_, _) => OpenWindow());
        menu.Items.Add("Quit Peeko", null, (_, _) => ExitThread());
    }

    private static string Mark(TaskState s) => s switch
    {
        TaskState.Done => "✓",
        TaskState.InProgress => "◐",
        TaskState.Failed => "·",
        _ => "○",
    };

    private static string Detail(TaskDefinition t, TaskState s) => s switch
    {
        TaskState.InProgress p => $"  ({p.Progress:P0})",
        TaskState.Done d when t.IsAutomatic => $"  ({d.At.ToLocalTime():t})",
        _ => "",
    };

    // Tone: curious and cheering, never "Verified".
    private void Notify(TaskDefinition task)
    {
        if (!_settings.NotificationsEnabled) return;
        var now = DateTimeOffset.Now;
        var tasks = _scheduler.Tasks;
        var done = tasks.Count(t => _scheduler.StatusOf(t, now).IsDone);
        _tray.ShowBalloonTip(5000, "Peeko saw that!", $"{task.Title} ✓  ·  {done} of {tasks.Count} done today", ToolTipIcon.None);
    }

    protected override void ExitThreadCore()
    {
        _tray.Visible = false;
        _tray.Dispose();
        _window.AllowClose();
        _window.Close();
        _popover.Close();
        _ui.Dispose();
        _scheduler.Dispose();
        base.ExitThreadCore();
    }
}
