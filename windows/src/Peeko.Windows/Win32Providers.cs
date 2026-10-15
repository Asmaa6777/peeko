using System.Diagnostics;
using System.Runtime.InteropServices;
using Peeko.Core;

namespace Peeko.Windows;

/// <summary>Front window's process via GetForegroundWindow. Needs no permission.</summary>
internal sealed class Win32ForegroundApp : IForegroundAppProvider
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

    /// <summary>The front window's title, read only when a task filters on it. Needs no permission.</summary>
    public string? FrontWindowTitle()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        var length = GetWindowTextLength(hwnd);
        if (length <= 0) return null;
        var text = new System.Text.StringBuilder(length + 1);
        return GetWindowText(hwnd, text, text.Capacity) > 0 ? text.ToString() : null;
    }

    public string? FrontProcessName()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return null;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch (ArgumentException) { return null; }   // process already gone
        catch (InvalidOperationException) { return null; }
    }
}

/// <summary>Seconds since the last keyboard or mouse input, via GetLastInputInfo.</summary>
internal sealed class Win32IdleTime : IIdleTimeProvider
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    public double SecondsSinceInput()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return double.MaxValue;
        // Both are 32-bit tick counts that wrap together, so unsigned subtraction is safe.
        var ms = unchecked((uint)Environment.TickCount - info.dwTime);
        return ms / 1000.0;
    }
}
