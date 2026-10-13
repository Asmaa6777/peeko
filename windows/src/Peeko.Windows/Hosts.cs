using System.Runtime.InteropServices;

namespace Peeko.Windows;

internal static class Dwm
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>Dark frame in Night, and optionally Windows 11's rounded corners.</summary>
    public static void Style(IntPtr handle, bool roundCorners)
    {
        int on = 1, caption = 0x151515, text = 0xEFF2F2;   // COLORREF is BGR
        DwmSetWindowAttribute(handle, 20, ref on, 4);        // immersive dark mode
        DwmSetWindowAttribute(handle, 35, ref caption, 4);   // caption colour (Windows 11)
        DwmSetWindowAttribute(handle, 36, ref text, 4);      // caption text colour
        if (roundCorners)
        {
            int round = 2;                                   // DWMWCP_ROUND
            DwmSetWindowAttribute(handle, 33, ref round, 4);
        }
    }
}

/// <summary>
/// The tray popover, like the macOS MenuBarExtra window: borderless, fixed size, opens next to
/// the tray and hides when you click anywhere else. Fixed size on purpose: a panel that resized
/// per tab would jump around next to the taskbar.
/// </summary>
internal sealed class PopoverForm : Form
{
    private readonly PeekoView _view;
    private DateTime _hiddenAt = DateTime.MinValue;

    public PopoverForm(PeekoView view)
    {
        _view = view;
        Text = "Peeko";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Theme.Night;
        KeyPreview = true;
        var s = DeviceDpi / 96f;
        ClientSize = new Size((int)(PeekoView.PopW * s), (int)(PeekoView.PopTotalH * s));
        view.Dock = DockStyle.Fill;
        Controls.Add(view);
        Deactivate += (_, _) => HideAway();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x20000;    // CS_DROPSHADOW
            cp.ExStyle |= 0x80;          // WS_EX_TOOLWINDOW: no Alt+Tab entry
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Dwm.Style(Handle, roundCorners: true);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { HideAway(); return true; }
        return _view.HandleKey(keyData) || base.ProcessCmdKey(ref msg, keyData);
    }

    public void HideAway()
    {
        if (!Visible || _view.ShowingDialog) return;
        Hide();
        _hiddenAt = DateTime.UtcNow;
    }

    /// <summary>Tray click: open next to the tray, or close if it's open.</summary>
    public void Toggle()
    {
        if (Visible) { HideAway(); return; }
        // Clicking the tray icon deactivates (and hides) the popover first; don't reopen it.
        if ((DateTime.UtcNow - _hiddenAt).TotalMilliseconds < 350) return;
        PlaceNearTray();
        Show();
        Activate();
        Dwm.SetForegroundWindow(Handle);
        _view.Focus();
    }

    /// <summary>Next to the tray, on whichever edge the taskbar is, on the screen under the cursor.</summary>
    private void PlaceNearTray()
    {
        var cursor = Cursor.Position;
        var screen = Screen.FromPoint(cursor);
        var bounds = screen.Bounds;
        var work = screen.WorkingArea;
        const int gap = 12;
        int x, y;
        if (work.Bottom < bounds.Bottom)           // taskbar at the bottom (the usual)
        {
            x = cursor.X - Width / 2;
            y = work.Bottom - Height - gap;
        }
        else if (work.Top > bounds.Top)            // top
        {
            x = cursor.X - Width / 2;
            y = work.Top + gap;
        }
        else if (work.Left > bounds.Left)          // left
        {
            x = work.Left + gap;
            y = cursor.Y - Height / 2;
        }
        else                                       // right, or an auto-hidden taskbar
        {
            x = work.Right - Width - gap;
            y = work.Bottom - Height - gap;
        }
        x = Math.Clamp(x, work.Left + gap, work.Right - Width - gap);
        y = Math.Clamp(y, work.Top + gap, work.Bottom - Height - gap);
        Location = new Point(x, y);
    }
}

/// <summary>
/// The full window, like the macOS main window: sidebar plus the same screens with room to
/// breathe. Resizable; closing it hides it and Peeko keeps watching from the tray.
/// </summary>
internal sealed class MainWindow : Form
{
    private readonly PeekoView _view;
    private bool _reallyClose;

    public MainWindow(PeekoView view)
    {
        _view = view;
        Text = "Peeko";
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Night;
        var s = DeviceDpi / 96f;
        ClientSize = new Size((int)(960 * s), (int)(680 * s));
        MinimumSize = SizeFromClientSize(new Size((int)(780 * s), (int)(540 * s)));
        view.Dock = DockStyle.Fill;
        Controls.Add(view);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Dwm.Style(Handle, roundCorners: false);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) =>
        _view.HandleKey(keyData) || base.ProcessCmdKey(ref msg, keyData);

    public void Bring()
    {
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        Dwm.SetForegroundWindow(Handle);
        BringToFront();
    }

    public void AllowClose() => _reallyClose = true;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_reallyClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();   // keep watching from the tray
            return;
        }
        base.OnFormClosing(e);
    }
}
