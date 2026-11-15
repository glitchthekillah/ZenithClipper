using System.Drawing.Drawing2D;

namespace ZenithClipper.Windows.Ui;
using Control = System.Windows.Forms.Control;

/// <summary>Big replay readout at the top of the rail: buffered time, a disk-use ring and the capture setup as pills.</summary>
sealed class ReplayHero : Control, IMeasured
{
    double buffered, target = 120; long bytes, capBytes; string state = "Connecting"; Color stateColor = Theme.Faint;
    readonly List<string> pills = [];
    public ReplayHero() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
    public int MeasureHeight(int width) => Theme.S(112);
    public void Update(RecorderStatus? s, Settings settings, bool offline)
    {
        target = Math.Max(1, settings.BufferSeconds); capBytes = settings.MaxBufferMiB * 1048576;
        buffered = s?.BufferedSeconds ?? 0; bytes = s?.BufferBytes ?? 0;
        (state, stateColor) = offline || s == null ? (offline ? "Offline" : "Connecting", Theme.Faint)
            : s.Error != null ? ("Needs attention", Theme.Warn) : s.Capturing ? ("Recording", Theme.Live) : s.State == "Starting" ? ("Starting", Theme.Muted) : ("Paused", Theme.Muted);
        pills.Clear();
        if (s?.Encoder is { Length: > 0 } enc) pills.Add(enc.ToUpperInvariant());
        var screen = Screen.AllScreens.ElementAtOrDefault(settings.Monitor) ?? Screen.PrimaryScreen;
        pills.Add(settings.OutputWidth > 0 ? $"{settings.OutputWidth}px wide" : screen is null ? "Native" : $"{screen.Bounds.Width}×{screen.Bounds.Height}");
        pills.Add(settings.Fps > 0 ? $"{settings.Fps} fps" : "Display fps");
        pills.Add(settings.CaptureAudio ? "Audio on" : "Audio off");
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Theme.Solid(Parent)); Theme.Prep(g);
        // Status line.
        float r = Theme.S(4f); int y0 = Theme.S(2);
        using (var dot = new SolidBrush(stateColor)) g.FillEllipse(dot, 0, y0 + Theme.S(8) - r, r * 2, r * 2);
        Theme.Label(g, state.ToUpperInvariant(), Theme.Caption, stateColor == Theme.Faint ? Theme.Faint : Theme.Mix(stateColor, Theme.Text, .35f), new Rectangle(Theme.S(14), y0, Theme.S(160), Theme.S(16)));
        // Big buffered time.
        int ringD = Theme.S(76), ringX = Width - ringD;
        string big = Theme.Duration(buffered);
        Theme.Label(g, big, Theme.Display, Theme.Text, new Rectangle(0, Theme.S(18), ringX - Theme.S(8), Theme.S(42)));
        Theme.Label(g, $"buffered of {Theme.Duration(target)} history", Theme.Small, Theme.Muted, new Rectangle(0, Theme.S(58), ringX - Theme.S(8), Theme.S(18)));
        // Disk ring.
        var ring = new RectangleF(ringX + Theme.S(4f), Theme.S(4f), ringD - Theme.S(8f), ringD - Theme.S(8f));
        float frac = capBytes > 0 ? (float)Math.Clamp(bytes / (double)capBytes, 0, 1) : 0;
        using (var track = new Pen(Theme.Raised, Theme.S(7f))) g.DrawEllipse(track, ring);
        if (frac > 0) using (var arc = new Pen(frac > .97 ? Theme.Warn : Theme.Accent, Theme.S(7f)) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(arc, ring, -90, Math.Max(2, 360 * frac));
        var inner = Rectangle.Round(ring);
        Theme.Label(g, Theme.Bytes(bytes), Theme.Strong, Theme.Text, new Rectangle(inner.X, inner.Y + inner.Height / 2 - Theme.S(14), inner.Width, Theme.S(18)), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        Theme.Label(g, "of " + Theme.Bytes(capBytes), Theme.Caption, Theme.Faint, new Rectangle(inner.X, inner.Y + inner.Height / 2 + Theme.S(2), inner.Width, Theme.S(14)), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        // Setup pills.
        int x = 0, py = Theme.S(86), ph = Theme.S(22);
        foreach (var p in pills)
        {
            int w = Theme.Measure(p, Theme.Small).Width + Theme.S(18); if (x + w > Width) break;
            var box = new RectangleF(x, py, w, ph);
            Theme.Fill(g, box, ph / 2f, Theme.Raised); Theme.Stroke(g, box, ph / 2f, Theme.Line);
            Theme.Label(g, p, Theme.Small, Theme.Muted, Rectangle.Round(box), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            x += w + Theme.S(6);
        }
    }
}

/// <summary>Three small tiles: clips saved today, what the recorder costs in CPU (with a sparkline) and memory.</summary>
sealed class StatTiles : Control, IMeasured
{
    int todayCount; long todayBytes; float cpu; long memory; readonly Queue<float> history = new();
    public StatTiles() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
    public int MeasureHeight(int width) => Theme.S(70);
    public void SetToday(int count, long size) { todayCount = count; todayBytes = size; Invalidate(); }
    public void SetPerf(float cpuPercent, long bytes) { cpu = cpuPercent; memory = bytes; history.Enqueue(cpuPercent); while (history.Count > 30) history.Dequeue(); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Theme.Solid(Parent)); Theme.Prep(g);
        int gap = Theme.S(8), w = (Width - gap * 2) / 3, h = Height - 1;
        void Tile(int i, string label, string value, string sub, Action<Rectangle>? extra = null)
        {
            var box = new Rectangle(i * (w + gap), 0, w, h);
            Theme.Fill(g, box, Theme.S(8f), Theme.Raised); Theme.Stroke(g, box, Theme.S(8f), Theme.Divider);
            extra?.Invoke(box);
            Theme.Label(g, label, Theme.Caption, Theme.Faint, new Rectangle(box.X + Theme.S(10), box.Y + Theme.S(8), box.Width - Theme.S(16), Theme.S(14)));
            Theme.Label(g, value, Theme.Title, Theme.Text, new Rectangle(box.X + Theme.S(10), box.Y + Theme.S(24), box.Width - Theme.S(16), Theme.S(22)));
            Theme.Label(g, sub, Theme.Caption, Theme.Muted, new Rectangle(box.X + Theme.S(10), box.Y + Theme.S(47), box.Width - Theme.S(16), Theme.S(14)));
        }
        Tile(0, "SAVED TODAY", todayCount.ToString(System.Globalization.CultureInfo.CurrentCulture), todayCount == 0 ? "No clips yet" : Theme.Bytes(todayBytes));
        Tile(1, "RECORDER CPU", $"{cpu:0.0}%", "of all cores", box =>
        {
            if (history.Count < 2) return;
            float max = Math.Max(5, history.Max()), sx = (box.Width - Theme.S(20)) / 29f, baseY = box.Bottom - Theme.S(8);
            var pts = history.Select((v, k) => new PointF(box.X + Theme.S(10) + (30 - history.Count + k) * sx, baseY - v / max * Theme.S(30))).ToArray();
            using var pen = new Pen(Theme.Alpha(Theme.Accent, 110), Theme.S(1.5f)); g.DrawLines(pen, pts);
        });
        Tile(2, "MEMORY", Theme.Bytes(memory), "engine + encoder");
    }
}
