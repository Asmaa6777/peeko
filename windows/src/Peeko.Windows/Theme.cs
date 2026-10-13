using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Peeko.Windows;

/// <summary>Mirror of the macOS Theme: Night, Chalk, Peeko green, Blush, muted.</summary>
internal static class Theme
{
    public static readonly Color Night = Color.FromArgb(0x15, 0x15, 0x15);
    public static readonly Color Chalk = Color.FromArgb(0xF2, 0xF2, 0xEF);
    public static readonly Color Green = Color.FromArgb(0x81, 0xDD, 0x86);
    public static readonly Color Blush = Color.FromArgb(0xFA, 0x9F, 0xF2);
    public static readonly Color Muted = Color.FromArgb(0xA9, 0xA9, 0xA5);
    // Task-group colors: the same recipe, oklch(0.82 0.15 hue), so they read as one family.
    public static readonly Color Sky = Color.FromArgb(0x66, 0xCD, 0xFF);       // hue 245
    public static readonly Color Amber = Color.FromArgb(0xF0, 0xBB, 0x3B);     // hue 85
    public static readonly Color Lavender = Color.FromArgb(0xCB, 0xAF, 0xFF);  // hue 295
    public static readonly Color Peach = Color.FromArgb(0xFF, 0xA2, 0x6E);     // hue 45
    // Hue 195 at chroma 0.15 falls outside sRGB, so teal uses 0.11.
    public static readonly Color Teal = Color.FromArgb(0x5E, 0xDA, 0xDA);
    public static readonly Color Card = Color.FromArgb(13, 255, 255, 255);        // white 5%
    public static readonly Color CardHover = Color.FromArgb(23, 255, 255, 255);   // white 9%
    public static readonly Color FooterBg = Color.FromArgb(8, 255, 255, 255);     // white 3%
    public static readonly Color Divider = Color.FromArgb(20, 255, 255, 255);
    public static readonly Color Outline = Color.FromArgb(31, 255, 255, 255);
    public static readonly Color Sidebar = Color.FromArgb(6, 255, 255, 255);       // white 2.5%
    public static readonly Color Separator = Color.FromArgb(15, 255, 255, 255);    // white 6%
    /// <summary>Card (white 5%) composited over Night, for real controls that can't be translucent.</summary>
    public static readonly Color CardSolid = Color.FromArgb(0x20, 0x20, 0x20);

    private static readonly string[] Installed = new InstalledFontCollection().Families.Select(f => f.Name).ToArray();
    private static string Pick(params string[] names) =>
        names.FirstOrDefault(n => Installed.Contains(n, StringComparer.OrdinalIgnoreCase)) ?? "Segoe UI";

    // SF Rounded / SF Mono stand-ins (the brand fonts aren't bundled on macOS either).
    private static readonly string Sans = Pick("Segoe UI Variable Text", "Segoe UI");
    private static readonly string Heavy = Pick("Segoe UI Black", "Segoe UI");
    private static readonly string Mono = Pick("Cascadia Mono", "Consolas", "Courier New");
    public const string EmojiFamily = "Segoe UI Emoji";

    public static readonly Font Display26 = new(Heavy, 26f, FontStyle.Bold, GraphicsUnit.Pixel);
    public static readonly Font Display28 = new(Heavy, 28f, FontStyle.Bold, GraphicsUnit.Pixel);
    public static readonly Font Display34 = new(Heavy, 34f, FontStyle.Bold, GraphicsUnit.Pixel);
    public static readonly Font Title15 = new(Sans, 15f, FontStyle.Bold, GraphicsUnit.Pixel);
    public static readonly Font Body14 = new(Sans, 14f, FontStyle.Regular, GraphicsUnit.Pixel);
    public static readonly Font Mono10 = new(Mono, 10f, FontStyle.Regular, GraphicsUnit.Pixel);
    public static readonly Font Mono12 = new(Mono, 12f, FontStyle.Regular, GraphicsUnit.Pixel);
    public static readonly Font Mono13Bold = new(Mono, 13f, FontStyle.Bold, GraphicsUnit.Pixel);
    public static readonly Font SearchFont = new(Sans, 14f, FontStyle.Regular, GraphicsUnit.Pixel);
    public static readonly Font RowTitle = new(Sans, 14f, FontStyle.Bold, GraphicsUnit.Pixel);
    public static readonly Font Title13 = new(Sans, 13f, FontStyle.Bold, GraphicsUnit.Pixel);
    public static readonly Font Title16 = new(Sans, 16f, FontStyle.Bold, GraphicsUnit.Pixel);
    public static readonly Font Body13 = new(Sans, 13f, FontStyle.Regular, GraphicsUnit.Pixel);
    public static readonly Font Mono11 = new(Mono, 11f, FontStyle.Regular, GraphicsUnit.Pixel);
    public static readonly Font Mono11Bold = new(Mono, 11f, FontStyle.Bold, GraphicsUnit.Pixel);
    public static readonly Font Mono10Bold = new(Mono, 10f, FontStyle.Bold, GraphicsUnit.Pixel);
    public static readonly Font Mono12Bold = new(Mono, 12f, FontStyle.Bold, GraphicsUnit.Pixel);
    public static readonly Font Mono15Bold = new(Mono, 15f, FontStyle.Bold, GraphicsUnit.Pixel);
    public static readonly Font Mono9 = new(Mono, 9f, FontStyle.Regular, GraphicsUnit.Pixel);
    public static Font Emoji(float px) => new(EmojiFamily, px, FontStyle.Regular, GraphicsUnit.Pixel);
}

/// <summary>Small drawing helpers shared by the window and the tray icon.</summary>
internal static class Gfx
{
    private static readonly Dictionary<Color, SolidBrush> Brushes = new();
    public static SolidBrush Brush(Color c)
    {
        if (!Brushes.TryGetValue(c, out var b)) Brushes[c] = b = new SolidBrush(c);
        return b;
    }

    public static GraphicsPath Round(RectangleF r, float radius) => Round(r, radius, radius, radius, radius);

    public static GraphicsPath Round(RectangleF r, float tl, float tr, float br, float bl)
    {
        var p = new GraphicsPath();
        void Arc(float x, float y, float rad, float start)
        {
            if (rad <= 0) return;
            p.AddArc(x, y, rad * 2, rad * 2, start, 90);
        }
        // Each corner is an arc; AddArc continues the figure, and straight lines join them.
        if (tl > 0) Arc(r.Left, r.Top, tl, 180); else p.AddLine(r.Left, r.Top, r.Left, r.Top);
        if (tr > 0) Arc(r.Right - tr * 2, r.Top, tr, 270); else p.AddLine(r.Right, r.Top, r.Right, r.Top);
        if (br > 0) Arc(r.Right - br * 2, r.Bottom - br * 2, br, 0); else p.AddLine(r.Right, r.Bottom, r.Right, r.Bottom);
        if (bl > 0) Arc(r.Left, r.Bottom - bl * 2, bl, 90); else p.AddLine(r.Left, r.Bottom, r.Left, r.Bottom);
        p.CloseFigure();
        return p;
    }

    public static void FillRound(Graphics g, Color c, RectangleF r, float radius)
    {
        using var p = Round(r, radius);
        g.FillPath(Brush(c), p);
    }

    public static void StrokeRound(Graphics g, Color c, RectangleF r, float radius, float width = 1f)
    {
        using var p = Round(r, radius);
        using var pen = new Pen(c, width);
        g.DrawPath(pen, p);
    }

    /// <summary>Peeko's head hanging from the top edge. Geometry is the brand file's 72×60 units.</summary>
    public static void Face(Graphics g, Core.Mood mood, float x, float y, float width)
    {
        var s = width / 72f;
        var state = g.Save();
        g.TranslateTransform(x, y);
        g.ScaleTransform(s, s);
        g.IntersectClip(new RectangleF(0, 0, 72, 60));

        using (var head = Round(new RectangleF(6, -12, 60, 62), 30, 30, 24, 24))
            g.FillPath(Brush(Theme.Green), head);

        var night = Brush(Theme.Night);
        switch (mood)
        {
            case Core.Mood.Dozing:
                FillRound(g, Theme.Night, new RectangleF(18, 24, 12, 3), 1.5f);
                FillRound(g, Theme.Night, new RectangleF(42, 24, 12, 3), 1.5f);
                break;
            case Core.Mood.Looking:
                g.FillEllipse(Brush(Color.White), 16, 16, 16, 16);
                g.FillEllipse(Brush(Color.White), 40, 16, 16, 16);
                g.FillEllipse(night, 23, 19, 7, 7);
                g.FillEllipse(night, 47, 19, 7, 7);
                break;
            case Core.Mood.Beaming:
                Arch(g, 17.5f, 21.5f, 13f, 7.5f);
                Arch(g, 41.5f, 21.5f, 13f, 7.5f);
                using (var mouth = Round(new RectangleF(30, 34, 12, 6), 0, 0, 6, 6))
                    g.FillPath(night, mouth);
                break;
        }
        g.Restore(state);
    }

    /// <summary>An arch: a quadratic curve from bottom-left to bottom-right, peaking above the top.</summary>
    private static void Arch(Graphics g, float x, float y, float w, float h)
    {
        PointF p0 = new(x, y + h), p2 = new(x + w, y + h), c = new(x + w / 2, y - h);
        // Quadratic → cubic control points.
        PointF c1 = new(p0.X + 2f / 3f * (c.X - p0.X), p0.Y + 2f / 3f * (c.Y - p0.Y));
        PointF c2 = new(p2.X + 2f / 3f * (c.X - p2.X), p2.Y + 2f / 3f * (c.Y - p2.Y));
        using var pen = new Pen(Theme.Night, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawBezier(pen, p0, c1, c2, p2);
    }

    public static void Ring(Graphics g, RectangleF r, double progress, Color color)
    {
        using var track = new Pen(Color.FromArgb(77, Theme.Muted), 2.5f);
        using var arc = new Pen(color, 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var rr = RectangleF.Inflate(r, -1.25f, -1.25f);
        g.DrawEllipse(track, rr);
        var sweep = (float)(Math.Clamp(progress, 0, 1) * 360);
        if (sweep > 0.5f) g.DrawArc(arc, rr, -90, sweep);
    }

    public static void CheckCircle(Graphics g, RectangleF r, bool on)
    {
        if (on)
        {
            g.FillEllipse(Brush(Theme.Green), r);
            using var pen = new Pen(Theme.Night, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(pen, new[]
            {
                new PointF(r.X + r.Width * 0.28f, r.Y + r.Height * 0.52f),
                new PointF(r.X + r.Width * 0.44f, r.Y + r.Height * 0.68f),
                new PointF(r.X + r.Width * 0.74f, r.Y + r.Height * 0.34f),
            });
        }
        else
        {
            using var pen = new Pen(Theme.Muted, 1.5f);
            g.DrawEllipse(pen, RectangleF.Inflate(r, -0.75f, -0.75f));
        }
    }

    public static void Toggle(Graphics g, RectangleF r, bool on)
    {
        FillRound(g, on ? Theme.Green : Color.FromArgb(46, 255, 255, 255), r, r.Height / 2);
        var d = r.Height - 4;
        var x = on ? r.Right - d - 2 : r.Left + 2;
        g.FillEllipse(Brush(Color.White), x, r.Top + 2, d, d);
    }

    public static Icon FaceIcon(Core.Mood mood)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            Face(g, mood, 0, 1, 32);
        }
        // The handle is kept for the icon's lifetime. It only changes when the day's mood does.
        return Icon.FromHandle(bmp.GetHicon());
    }
}
