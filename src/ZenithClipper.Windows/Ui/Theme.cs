using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace ZenithClipper.Windows.Ui;
using Control = System.Windows.Forms.Control;

/// <summary>ZenithClipper neutral grey and orange-red palette, type scale and drawing helpers shared by the controls panel and the editor.</summary>
static class Theme
{
    // Graphite is the base palette; Appearance (Settings > Appearance) re-tints the neutrals and swaps the accent.
    // Fields, not constants, so a change repaints live (see ApplyAppearance).
    public static Color Bg = Color.FromArgb(19, 20, 22);
    public static Color Surface = Color.FromArgb(22, 23, 26);
    public static Color Raised = Color.FromArgb(29, 31, 35);
    public static Color Hover = Color.FromArgb(37, 40, 45);
    public static Color Line = Color.FromArgb(42, 45, 51);
    public static Color Divider = Color.FromArgb(36, 38, 43);
    public static readonly Color Text = Color.FromArgb(232, 233, 236);
    public static readonly Color Muted = Color.FromArgb(154, 158, 168);
    public static readonly Color Faint = Color.FromArgb(107, 111, 120);
    public static Color Accent = Color.FromArgb(255, 107, 74);
    public static Color AccentEnd = Color.FromArgb(255, 107, 74);
    public static readonly Color Live = Color.FromArgb(240, 72, 72);
    public static readonly Color Ok = Color.FromArgb(62, 207, 142);
    public static readonly Color Warn = Color.FromArgb(245, 165, 36);

    public static readonly Color Violet = Color.FromArgb(201, 204, 211);
    public static readonly Color Blue = Color.FromArgb(201, 204, 211);
    /// <summary>Selected segment fill in segmented controls.</summary>
    public static Color Selected = Color.FromArgb(46, 49, 55);
    /// <summary>Text on accent-filled buttons: dark ink on light accents, white on dark ones.</summary>
    public static Color Ink = Color.FromArgb(26, 13, 8);
    public static Appearance Look { get; private set; } = new();

    /// <summary>Points the palette at an appearance. Returns old→new colour pairs so already-built controls can be re-tinted.</summary>
    public static Dictionary<Color, Color> ApplyAppearance(Appearance look)
    {
        Look = look;
        var before = new[] { Bg, Surface, Raised, Hover, Line, Divider, Selected, Accent };
        Color N(byte r, byte g, byte b) { var (nr, ng, nb) = look.Neutral(r, g, b); return Color.FromArgb(nr, ng, nb); }
        Bg = N(19, 20, 22); Surface = N(22, 23, 26); Raised = N(29, 31, 35); Hover = N(37, 40, 45); Line = N(42, 45, 51); Divider = N(36, 38, 43); Selected = N(46, 49, 55);
        var (ar, ag, ab) = look.AccentRgb; Accent = AccentEnd = Color.FromArgb(ar, ag, ab);
        var (ir, ig, ib) = look.InkRgb; Ink = Color.FromArgb(ir, ig, ib);
        var after = new[] { Bg, Surface, Raised, Hover, Line, Divider, Selected, Accent };
        var map = new Dictionary<Color, Color>();
        for (int i = 0; i < before.Length; i++) if (before[i].ToArgb() != after[i].ToArgb()) map[Color.FromArgb(before[i].ToArgb())] = after[i];
        return map;
    }
    /// <summary>Re-tints BackColor/ForeColor on a built control tree and repaints it.</summary>
    public static void Retint(Control root, Dictionary<Color, Color> map)
    {
        void Walk(Control c)
        {
            if (map.TryGetValue(Color.FromArgb(c.BackColor.ToArgb()), out var b)) c.BackColor = b;
            if (map.TryGetValue(Color.FromArgb(c.ForeColor.ToArgb()), out var f)) c.ForeColor = f;
            foreach (Control child in c.Controls) Walk(child);
        }
        Walk(root); root.Invalidate(true);
        if (root is Form form && form.IsHandleCreated) Native.StyleWindow(form);
    }

    public static readonly float Scale;
    static readonly string TextFamily, DisplayFamily, IconFamily, MonoFamily;
    public static readonly Font Body, Small, Strong, Title, Hero, Mono, Caption, Display, Heading;

    static Theme()
    {
        PanelStartupProbe.Mark("theme_start");
        ApplyAppearance(Appearance.Load());
        using (var g = Graphics.FromHwnd(0)) Scale = g.DpiX / 96f;
        using var fonts = new InstalledFontCollection();
        var names = fonts.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        TextFamily = names.Contains("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
        DisplayFamily = names.Contains("Segoe UI Variable Display") ? "Segoe UI Variable Display" : "Segoe UI";
        IconFamily = names.Contains("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
        MonoFamily = names.Contains("Cascadia Mono") ? "Cascadia Mono" : "Consolas";
        PanelStartupProbe.Mark("font_families_ready");
        Body = new(TextFamily, 9.75f);
        Small = new(TextFamily, 8.5f);
        Caption = new(TextFamily, 8f);
        Strong = new(TextFamily, 9.75f, FontStyle.Bold);
        Title = new(DisplayFamily, 10.5f, FontStyle.Bold);
        Hero = new(DisplayFamily, 10.5f, FontStyle.Bold);
        Mono = new(MonoFamily, 8.5f);
        Display = new(DisplayFamily, 24f, FontStyle.Bold);
        Heading = new(DisplayFamily, 15f, FontStyle.Bold);
        PanelStartupProbe.Mark("theme_ready");
    }

    public static int S(int value) => (int)Math.Round(value * Scale);
    public static float S(float value) => value * Scale;
    static readonly Dictionary<float, Font> icons = [];
    public static Font Icon(float size)
    {
        if (!icons.TryGetValue(size, out var font)) icons[size] = font = new Font(IconFamily, size);
        return font;
    }

    public static Color Mix(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }
    public static Color Alpha(Color c, int alpha) => Color.FromArgb(alpha, c);
    /// <summary>First opaque background up the parent chain, for clearing custom-painted controls.</summary>
    public static Color Solid(Control? c)
    {
        for (; c != null; c = c.Parent) if (c.BackColor.A == 255) return c.BackColor;
        return Bg;
    }

    public static void Prep(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }
    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
    public static void Fill(Graphics g, RectangleF r, float radius, Color color)
    {
        using var path = Round(r, radius); using var brush = new SolidBrush(color); g.FillPath(brush, path);
    }
    public static void Fill(Graphics g, RectangleF r, float radius, Brush brush)
    {
        using var path = Round(r, radius); g.FillPath(brush, path);
    }
    public static void Stroke(Graphics g, RectangleF r, float radius, Color color, float width = 1)
    {
        r.Inflate(-width / 2, -width / 2);
        using var path = Round(r, radius); using var pen = new Pen(color, width); g.DrawPath(pen, path);
    }
    /// <summary>ZenithClipper's flat orange-red primary fill (kept as a brush so existing callers stay unchanged).</summary>
    public static LinearGradientBrush AccentBrush(RectangleF r, int alpha = 255) =>
        new(new RectangleF(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), Alpha(Accent, alpha), Alpha(AccentEnd, alpha), 35f);
    /// <summary>One-pixel horizontal divider used between rail sections and setting rows.</summary>
    public static void HLine(Graphics g, int x1, int x2, int y) { using var pen = new Pen(Divider); g.DrawLine(pen, x1, y, x2, y); }

    /// <summary>The ZenithClipper orb: an orange-red sphere with a thin grey rim.</summary>
    public static void Orb(Graphics g, RectangleF r)
    {
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(r);
            using var b = new PathGradientBrush(path) { CenterPoint = new PointF(r.X + r.Width * 0.34f, r.Y + r.Height * 0.28f) };
            b.InterpolationColors = new ColorBlend
            {
                Colors = [Mix(Accent, Color.Black, .65f), Mix(Accent, Color.Black, .12f), Mix(Accent, Color.White, .16f), Mix(Accent, Color.White, .75f)],
                Positions = [0f, 0.3f, 0.68f, 1f],
            };
            g.FillEllipse(b, r);
        }
        using var rim = new Pen(Color.FromArgb(58, 61, 68), Math.Max(1f, r.Width / 22f));
        g.DrawEllipse(rim, RectangleF.Inflate(r, 0.5f, 0.5f));
    }

    const TextFormatFlags Base = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
    public static void Label(Graphics g, string text, Font font, Color color, Rectangle r, TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left) =>
        TextRenderer.DrawText(g, text, font, r, color, Base | flags);
    public static void Wrap(Graphics g, string text, Font font, Color color, Rectangle r) =>
        TextRenderer.DrawText(g, text, font, r, color, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
    public static Size Measure(string text, Font font) => TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    public static int WrapHeight(string text, Font font, int width) =>
        TextRenderer.MeasureText(text, font, new Size(Math.Max(1, width), int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak).Height;
    public static void Glyph(Graphics g, string glyph, float size, Color color, Rectangle r) =>
        TextRenderer.DrawText(g, glyph, Icon(size), r, color, TextFormatFlags.NoPadding | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

    public static string Duration(double seconds, bool fraction = false)
    {
        if (!double.IsFinite(seconds) || seconds < 0) seconds = 0;
        var t = TimeSpan.FromSeconds(seconds);
        string main = t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
        return fraction ? main + "." + (t.Milliseconds / 10).ToString("00", System.Globalization.CultureInfo.InvariantCulture) : main;
    }
    public static string Span(int seconds) => seconds switch
    {
        < 60 => $"{seconds}s",
        < 3600 when seconds % 60 == 0 => $"{seconds / 60}m",
        < 3600 => $"{seconds / 60}m {seconds % 60}s",
        _ when seconds % 3600 == 0 => $"{seconds / 3600}h",
        _ => $"{seconds / 3600}h {seconds % 3600 / 60}m",
    };
    public static string Bytes(double bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (1L << 20):0} MB",
        _ => $"{bytes / 1024:0} KB",
    };
}

/// <summary>Segoe Fluent / MDL2 glyph code points.</summary>
static class Glyphs
{
    public const string Record = "", Video = "", Settings = "", Close = "", Folder = "",
        Cut = "", Play = "", Pause = "", Volume = "", Mute = "", Back = "",
        Add = "", Delete = "", Crop = "", Speed = "", Export = "", Monitor = "",
        Keyboard = "", Storage = "", Info = "", Warning = "", Check = "", Refresh = "",
        Search = "", Speakers = "", Rename = "", Open = "", Library = "", Previous = "",
        Next = "", Resize = "", Music = "", Power = "", Clock = "", Palette = "", Bracket = "";
}

static class Native
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(nint hwnd, string? app, string? idList);
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern nint SendMessageW(nint hwnd, int message, nint wParam, nint lParam);

    static int ColorRef(Color c) => c.R | c.G << 8 | c.B << 16;
    /// <summary>Dark title bar, Zenith caption colour and rounded Windows 11 corners. Older systems ignore unsupported attributes.</summary>
    public static void StyleWindow(Form form, bool rounded = true)
    {
        int on = 1; DwmSetWindowAttribute(form.Handle, 20, ref on, 4);
        int caption = ColorRef(Theme.Bg); DwmSetWindowAttribute(form.Handle, 35, ref caption, 4);
        int text = ColorRef(Theme.Text); DwmSetWindowAttribute(form.Handle, 36, ref text, 4);
        int border = ColorRef(Theme.Line); DwmSetWindowAttribute(form.Handle, 34, ref border, 4);
        int corner = rounded ? 2 : 1; DwmSetWindowAttribute(form.Handle, 33, ref corner, 4);
    }
    public static void DarkScrollbars(Control control)
    {
        if (control.IsHandleCreated) SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
        else control.HandleCreated += (_, _) => SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
    }
    public static void DragWindow(Form form)
    {
        ReleaseCapture(); SendMessageW(form.Handle, 0xA1, 2, 0);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct FileOperation
    {
        public nint Window; public uint Function; public string From; public string? To; public ushort Flags; public int Aborted; public nint Mappings; public string? Title;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHFileOperationW(ref FileOperation operation);
    /// <summary>Moves a file to the Recycle Bin so a deleted clip can still be restored.</summary>
    public static bool Recycle(string path, nint owner)
    {
        var op = new FileOperation { Window = owner, Function = 3, From = path + "\0\0", Flags = 0x0040 | 0x0010 | 0x0400 };
        return SHFileOperationW(ref op) == 0 && op.Aborted == 0;
    }
}
