using System.Drawing;

namespace Peeko.Core;

public enum TaskbarEdge { Bottom, Top, Left, Right }

/// <summary>
/// Where the tray popover opens: just past the taskbar, never on top of it, on whichever edge it
/// sits. Pure geometry (screen pixels) so the checks can cover it; the Win32 lookups stay in
/// Peeko.Windows.
/// </summary>
public static class PopoverPlacement
{
    /// <summary>
    /// The taskbar on this screen, or null when it can't be seen from the working area
    /// (auto-hidden, or on another screen).
    /// </summary>
    public static (TaskbarEdge Edge, Rectangle Rect)? FromWorkingArea(Rectangle screen, Rectangle work)
    {
        if (work.Bottom < screen.Bottom) return (TaskbarEdge.Bottom, Rectangle.FromLTRB(screen.Left, work.Bottom, screen.Right, screen.Bottom));
        if (work.Top > screen.Top) return (TaskbarEdge.Top, Rectangle.FromLTRB(screen.Left, screen.Top, screen.Right, work.Top));
        if (work.Left > screen.Left) return (TaskbarEdge.Left, Rectangle.FromLTRB(screen.Left, screen.Top, work.Left, screen.Bottom));
        if (work.Right < screen.Right) return (TaskbarEdge.Right, Rectangle.FromLTRB(work.Right, screen.Top, screen.Right, screen.Bottom));
        return null;
    }

    /// <summary>
    /// Clips the taskbar to its screen and gives it at least <paramref name="minThickness"/>, so an
    /// auto-hidden taskbar (a 2 px sliver, or reported off-screen) still pushes the popover clear
    /// of where it slides in.
    /// </summary>
    public static Rectangle Normalize(TaskbarEdge edge, Rectangle taskbar, Rectangle screen, int minThickness)
    {
        var r = Rectangle.Intersect(taskbar, screen);
        if (r.IsEmpty) r = taskbar;
        return edge switch
        {
            TaskbarEdge.Bottom => Rectangle.FromLTRB(r.Left, Math.Min(r.Top, screen.Bottom - minThickness), r.Right, screen.Bottom),
            TaskbarEdge.Top => Rectangle.FromLTRB(r.Left, screen.Top, r.Right, Math.Max(r.Bottom, screen.Top + minThickness)),
            TaskbarEdge.Left => Rectangle.FromLTRB(screen.Left, r.Top, Math.Max(r.Right, screen.Left + minThickness), r.Bottom),
            _ => Rectangle.FromLTRB(Math.Min(r.Left, screen.Right - minThickness), r.Top, screen.Right, r.Bottom),
        };
    }

    /// <summary>
    /// Top-left corner for the popover. Centred on the click when it came from the taskbar,
    /// otherwise (keyboard, Win+B) at the tray end of the taskbar.
    /// </summary>
    public static Point Place(Size popover, Rectangle screen, TaskbarEdge edge, Rectangle taskbar, Point? click, int gap)
    {
        var onTaskbar = click is { } c && Rectangle.Inflate(taskbar, gap, gap).Contains(c);
        int w = popover.Width, h = popover.Height, x, y;
        switch (edge)
        {
            case TaskbarEdge.Bottom:
                x = onTaskbar ? click!.Value.X - w / 2 : taskbar.Right - w - gap;
                y = taskbar.Top - h - gap;
                break;
            case TaskbarEdge.Top:
                x = onTaskbar ? click!.Value.X - w / 2 : taskbar.Right - w - gap;
                y = taskbar.Bottom + gap;
                break;
            case TaskbarEdge.Left:
                x = taskbar.Right + gap;
                y = onTaskbar ? click!.Value.Y - h / 2 : taskbar.Bottom - h - gap;
                break;
            default:
                x = taskbar.Left - w - gap;
                y = onTaskbar ? click!.Value.Y - h / 2 : taskbar.Bottom - h - gap;
                break;
        }

        // Keep it on the screen and off the taskbar.
        var free = edge switch
        {
            TaskbarEdge.Bottom => Rectangle.FromLTRB(screen.Left, screen.Top, screen.Right, taskbar.Top),
            TaskbarEdge.Top => Rectangle.FromLTRB(screen.Left, taskbar.Bottom, screen.Right, screen.Bottom),
            TaskbarEdge.Left => Rectangle.FromLTRB(taskbar.Right, screen.Top, screen.Right, screen.Bottom),
            _ => Rectangle.FromLTRB(screen.Left, screen.Top, taskbar.Left, screen.Bottom),
        };
        x = Math.Clamp(x, free.Left + gap, Math.Max(free.Left + gap, free.Right - w - gap));
        y = Math.Clamp(y, free.Top + gap, Math.Max(free.Top + gap, free.Bottom - h - gap));
        return new Point(x, y);
    }
}
