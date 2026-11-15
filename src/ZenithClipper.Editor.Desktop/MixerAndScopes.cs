using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ZenithClipper.Editing;
using ZenithClipper.Mlt;

namespace ZenithClipper.Editor.Desktop;
using Control = Avalonia.Controls.Control;

/// <summary>
/// Keyframe helpers, blend modes and masks for the inspector, the Audio Track Mixer (faders, pan, mute/solo, meters)
/// and Lumetri-style scopes (waveform, RGB parade, vectorscope, histogram) computed from the program monitor's frames.
/// </summary>
public sealed partial class EditorWindow
{
    internal static void SetKeyframe(TimelineClip clip, string parameter, long frame, double value)
    {
        clip.Motion.RemoveAll(k => k.Parameter == parameter && k.Frame == frame);
        clip.Motion.Add(new Keyframe { Parameter = parameter, Frame = frame, Value = value, Interpolation = "linear" });
        clip.Motion.Sort((a, b) => a.Parameter == b.Parameter ? a.Frame.CompareTo(b.Frame) : string.CompareOrdinal(a.Parameter, b.Parameter));
    }

    void KeyframeNavigator(StackPanel host, TimelineClip clip)
    {
        var frames = clip.Motion.Where(k => k.Parameter != "volume").Select(k => clip.Start + k.Frame).Distinct().Order().ToList();
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), MinHeight = 28, Margin = new Thickness(0, 0, 0, 2) };
        row.Children.Add(Copy(frames.Count == 0 ? "Click ◇ to animate a property" : $"{frames.Count} keyframe{(frames.Count == 1 ? "" : "s")}", 11, FaintBrush));
        var prev = ActionButton("previous", "Previous keyframe", () => { ShowProgram(); var f = frames.LastOrDefault(x => x < playhead, -1); if (f >= 0) Seek(f); }, iconOnly: true);
        var next = ActionButton("next", "Next keyframe", () => { ShowProgram(); var f = frames.FirstOrDefault(x => x > playhead, -1); if (f >= 0) Seek(f); }, iconOnly: true);
        var clear = ActionButton("delete", "Remove all motion keyframes", () => Change("Clear keyframes", p => EditCommands.ChangeLinkedClip(p, p.ActiveSequenceId, clip.Id, c => c.Motion.RemoveAll(k => k.Parameter != "volume"))), iconOnly: true);
        prev.IsEnabled = next.IsEnabled = clear.IsEnabled = frames.Count > 0;
        Grid.SetColumn(prev, 1); Grid.SetColumn(next, 2); Grid.SetColumn(clear, 3); row.Children.Add(prev); row.Children.Add(next); row.Children.Add(clear);
        host.Children.Add(row);
    }

    void BlendAndMask(StackPanel host, TimelineClip clip)
    {
        void Mutate(Action<TimelineClip> a) => Change("Edit clip", p => EditCommands.ChangeLinkedClip(p, p.ActiveSequenceId, clip.Id, a));
        static string Title(string mode) => mode switch { "colordodge" => "Color Dodge", "colorburn" => "Color Burn", "hardlight" => "Hard Light", "softlight" => "Soft Light", "hslhue" => "Hue", "hslsaturation" => "Saturation", "hslcolor" => "Color", "hslluminosity" => "Luminosity", _ => char.ToUpperInvariant(mode[0]) + mode[1..] };
        var blendRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,150"), MinHeight = 30 };
        blendRow.Children.Add(Copy("Blend mode", 12, SoftTextBrush));
        var blend = new ComboBox { ItemsSource = ClipBlend.Modes.Select(Title).ToList(), SelectedIndex = Math.Max(0, Array.IndexOf(ClipBlend.Modes, clip.BlendMode ?? "normal")), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 28 };
        blend.SelectionChanged += (_, _) => { var mode = ClipBlend.Modes[Math.Max(0, blend.SelectedIndex)]; if (mode != (clip.BlendMode ?? "normal")) Mutate(c => c.BlendMode = mode == "normal" ? null : mode); };
        Grid.SetColumn(blend, 1); blendRow.Children.Add(blend); host.Children.Add(blendRow);

        var maskRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,150"), MinHeight = 30 };
        maskRow.Children.Add(Copy("Mask", 12, SoftTextBrush));
        int shapeIndex = clip.Mask is null ? 0 : clip.Mask.Shape switch { "rectangle" => 2, "path" => 3, _ => 1 };
        var shape = new ComboBox { ItemsSource = new[] { "None", "Ellipse", "Rectangle", "Pen (free shape)" }, SelectedIndex = shapeIndex, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 28 };
        shape.SelectionChanged += (_, _) =>
        {
            if (shape.SelectedIndex == shapeIndex) return;
            if (shape.SelectedIndex == 3) { Mutate(c => c.Mask = new ClipMask { Shape = "path", Feather = .05, Invert = c.Mask?.Invert ?? false }); StartPen(true); return; }
            Mutate(c => c.Mask = shape.SelectedIndex == 0 ? null : new ClipMask { Shape = shape.SelectedIndex == 2 ? "rectangle" : "ellipse", X = c.Mask?.X ?? .5, Y = c.Mask?.Y ?? .5, Width = c.Mask?.Width ?? .5, Height = c.Mask?.Height ?? .5, Feather = c.Mask?.Feather ?? .1, Invert = c.Mask?.Invert ?? false });
        };
        Grid.SetColumn(shape, 1); maskRow.Children.Add(shape); host.Children.Add(maskRow);
        if (clip.Mask is not { } mask) return;
        void MaskField(string name, double value, Action<ClipMask, double> apply)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,84,24"), MinHeight = 30 }; row.Children.Add(Copy(name, 12, SoftTextBrush));
            string original = (value * 100).ToString("0.#", CultureInfo.InvariantCulture);
            var box = new TextBox { Text = original, FontSize = 12, MinHeight = 28, Height = 28, Padding = new Thickness(8, 0), HorizontalContentAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(box, 1); row.Children.Add(box); var unit = Copy("%", 11, FaintBrush); unit.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(unit, 2); row.Children.Add(unit);
            box.LostFocus += (_, _) => { if (box.Text != original && double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) Mutate(c => apply(c.Mask!, Math.Clamp(v / 100, 0, 1))); };
            host.Children.Add(row);
        }
        if (mask.Shape == "path")
        {
            var tools = new Grid { ColumnDefinitions = new ColumnDefinitions("*,6,*"), Margin = new Thickness(0, 2, 0, 4) };
            var edit = Button(mask.Points.Count >= 6 ? "Edit points" : "Draw on monitor", () => StartPen(mask.Points.Count < 6)); tools.Children.Add(edit);
            var redraw = Button("Redraw", () => StartPen(true)); Grid.SetColumn(redraw, 2); tools.Children.Add(redraw); host.Children.Add(tools);
            var smooth = new CheckBox { Content = "Smooth curve", IsChecked = mask.Smooth, FontSize = 11, Foreground = SoftTextBrush };
            smooth.IsCheckedChanged += (_, _) => Mutate(c => c.Mask!.Smooth = smooth.IsChecked == true); host.Children.Add(smooth);
            MaskField("Mask feather", mask.Feather, (m, v) => m.Feather = v);
            var inv = new CheckBox { Content = "Inverted (keep outside)", IsChecked = mask.Invert, FontSize = 11, Foreground = SoftTextBrush };
            inv.IsCheckedChanged += (_, _) => Mutate(c => c.Mask!.Invert = inv.IsChecked == true); host.Children.Add(inv);
            return;
        }
        MaskField("Mask centre X", mask.X, (m, v) => m.X = v); MaskField("Mask centre Y", mask.Y, (m, v) => m.Y = v);
        MaskField("Mask width", mask.Width, (m, v) => m.Width = v); MaskField("Mask height", mask.Height, (m, v) => m.Height = v);
        MaskField("Mask feather", mask.Feather, (m, v) => m.Feather = v);
        var invert = new CheckBox { Content = "Inverted (keep outside)", IsChecked = mask.Invert, FontSize = 11, Foreground = SoftTextBrush };
        invert.IsCheckedChanged += (_, _) => Mutate(c => c.Mask!.Invert = invert.IsChecked == true); host.Children.Add(invert);
        // Mask tracking (Premiere's Track selected mask forward).
        var trackRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,6,*"), Margin = new Thickness(0, 4, 0, 0) };
        var forward = Button(maskTracking ? "Tracking…" : "Track forward", () => Run(() => TrackMask(clip.Id))); forward.IsEnabled = !maskTracking; trackRow.Children.Add(forward);
        ToolTip.SetTip(forward, "Follow what's inside the mask from the playhead to the end of the clip, so the mask moves with it.");
        var clear = Button("Clear tracking", () => Mutate(c => c.Mask!.Track = [])); clear.IsEnabled = mask.Track.Count > 0; Grid.SetColumn(clear, 2); trackRow.Children.Add(clear);
        host.Children.Add(trackRow);
        if (mask.Track.Count > 0) host.Children.Add(Copy($"Tracked across {mask.Track.Count / 3} frames", 11, FaintBrush));
    }

    // ---------------- Audio Track Mixer ----------------
    readonly ScrollViewer mixerPanel = new() { IsVisible = false, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    readonly LevelMeter masterMeter = new() { Height = 10, Margin = new Thickness(0, 4) };
    readonly LevelMeter transportMeter = new() { Width = 46, Height = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
    DispatcherTimer? meterTimer; int loudnessTick;
    void StartMeters()
    {
        meterTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) =>
        {
            var (l, r) = playing ? engine.TakePeak() : (0f, 0f);
            masterMeter.Push(l, r); transportMeter.Push(l, r);
            if (playing && ++loudnessTick % 6 == 0) UpdateLoudnessReadout();
        });
        meterTimer.Start();
    }
    string rightPanel = "Inspector";
    void UpdateMixer()
    {
        mixerPanel.IsVisible = PanelShown("Mixer");
        if (!mixerPanel.IsVisible) return;
        var list = new StackPanel { Spacing = 6, Margin = new Thickness(10, 10, 10, 10) };
        (masterMeter.Parent as Panel)?.Children.Remove(masterMeter);
        list.Children.Add(Copy("MASTER", 10.5, FaintBrush, FontWeight.SemiBold)); list.Children.Add(masterMeter);
        list.Children.Add(Copy("Meters move during playback.", 11, FaintBrush));
        list.Children.Add(LoudnessCard());
        list.Children.Add(DuckingCard());
        ClipMixer(list);
        foreach (var track in Sequence.Tracks.Where(t => t.Kind == TrackKind.Audio))
        {
            var id = track.Id;
            void Edit(string label, Action<EditTrack> change) => Change(label, p => change(p.Sequences.First(s => s.Id == p.ActiveSequenceId).Tracks.First(t => t.Id == id)));
            var card = new Border { Background = ControlBrush, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8), Margin = new Thickness(0, 6, 0, 0) };
            var stack = new StackPanel { Spacing = 4 };
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
            head.Children.Add(Copy(track.Name, 12.5, TextBrush, FontWeight.SemiBold));
            var mute = Button("M", () => Edit(track.Muted ? "Unmute" : "Mute", t => t.Muted = !t.Muted)); mute.Width = 28; mute.Height = 24; mute.Padding = new Thickness(0); mute.HorizontalContentAlignment = HorizontalAlignment.Center;
            if (track.Muted) { mute.Background = Brush("#5A2A2A"); mute.Foreground = Brush("#FF8A8A"); }
            var solo = Button("S", () => Edit(track.Solo ? "Unsolo" : "Solo", t => t.Solo = !t.Solo)); solo.Width = 28; solo.Height = 24; solo.Padding = new Thickness(0); solo.HorizontalContentAlignment = HorizontalAlignment.Center; solo.Margin = new Thickness(4, 0, 0, 0);
            if (track.Solo) { solo.Background = Brush("#5A4A1E"); solo.Foreground = AmberBrush; }
            ToolTip.SetTip(mute, "Mute track"); ToolTip.SetTip(solo, "Solo track");
            Grid.SetColumn(mute, 1); Grid.SetColumn(solo, 2); head.Children.Add(mute); head.Children.Add(solo); stack.Children.Add(head);
            stack.Children.Add(MixerSlider("Volume", track.GainDb, -60, 12, "dB", v => Edit("Track volume", t => t.GainDb = Math.Round(v, 1))));
            stack.Children.Add(MixerSlider("Pan", track.Pan * 100, -100, 100, "", v => Edit("Track pan", t => t.Pan = Math.Round(v) / 100), v => v == 0 ? "C" : v < 0 ? $"L{-v:0}" : $"R{v:0}"));
            card.Child = stack; list.Children.Add(card);
        }
        list.Children.Add(Copy("Track volume and pan apply to every clip on the track.", 11, FaintBrush));
        mixerPanel.Content = list;
    }
    Control MixerSlider(string label, double value, double min, double max, string unit, Action<double> commit, Func<double, string>? format = null)
    {
        format ??= v => $"{v:0.0} {unit}";
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("54,*,54"), MinHeight = 26 };
        row.Children.Add(Copy(label, 11.5, SoftTextBrush));
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0) }; StyleSlider(slider);
        var readout = Copy(format(value), 11, MutedBrush); readout.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(slider, 1); Grid.SetColumn(readout, 2); row.Children.Add(slider); row.Children.Add(readout);
        bool dirty = false;
        slider.ValueChanged += (_, _) => { readout.Text = format(slider.Value); dirty = true; };
        slider.AddHandler(PointerReleasedEvent, (_, _) => { if (dirty) { dirty = false; commit(slider.Value); } }, Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, true);
        slider.DoubleTapped += (_, _) => { slider.Value = Math.Clamp(0, min, max); dirty = false; commit(slider.Value); };
        ToolTip.SetTip(slider, "Double-click to reset");
        return row;
    }

    // ---------------- Lumetri Scopes ----------------
    readonly ScopesView scopes = new() { IsVisible = false };
    long lastScopeTicks;
    PreviewFrame? lastShown;
    static readonly bool demoScopeFrame = Environment.GetEnvironmentVariable("ZENITH_DEMO_FRAME") is not null;
    void FeedScopes(PreviewFrame frame)
    {
        if (demoScopeFrame) return; // headless snapshot shows a fixed test frame
        lastShown = frame;
        if (!scopes.IsVisible) return;
        long now = Environment.TickCount64; if (now - lastScopeTicks < 90) return; lastScopeTicks = now;
        scopes.Update(frame.Rgba, frame.Width, frame.Height);
    }
    void UpdateScopesPanel()
    {
        scopes.IsVisible = PanelShown("Scopes");
        if (scopes.IsVisible && bitmap is not null) Dispatcher.UIThread.Post(() => { if (lastShown is { } f) scopes.Update(f.Rgba, f.Width, f.Height); });
    }
}

/// <summary>Stereo peak meter, -60 dB to 0 dB, with a short peak hold, Premiere's green/yellow/red bands.</summary>
internal sealed class LevelMeter : Control
{
    float left = -60, right = -60, holdL = -60, holdR = -60; long holdUntil;
    static float Db(float peak) => peak <= 0.000001f ? -60 : Math.Max(-60, 20 * MathF.Log10(peak));
    public void Push(float l, float r)
    {
        // Instant attack, ~20 dB/s release, 1 s peak hold.
        float dl = Db(l), dr = Db(r);
        left = Math.Max(dl, left - .7f); right = Math.Max(dr, right - .7f);
        long now = Environment.TickCount64;
        if (dl >= holdL || dr >= holdR || now > holdUntil) { holdL = Math.Max(dl, now > holdUntil ? -60 : holdL); holdR = Math.Max(dr, now > holdUntil ? -60 : holdR); holdUntil = now + 1000; }
        InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    {
        var b = Bounds; double gap = 1, h = (b.Height - gap) / 2;
        var track = new SolidColorBrush(Color.Parse("#22252A"));
        for (int ch = 0; ch < 2; ch++)
        {
            double y = ch * (h + gap); float level = ch == 0 ? left : right, hold = ch == 0 ? holdL : holdR;
            context.FillRectangle(track, new Rect(0, y, b.Width, h));
            double w = b.Width * (level + 60) / 60;
            // Green up to -18 dB, yellow to -6 dB, red above.
            double g = b.Width * 42 / 60, yl = b.Width * 54 / 60;
            if (w > 0) context.FillRectangle(new SolidColorBrush(Color.Parse("#3ECF8E")), new Rect(0, y, Math.Min(w, g), h));
            if (w > g) context.FillRectangle(new SolidColorBrush(Color.Parse("#F5C542")), new Rect(g, y, Math.Min(w, yl) - g, h));
            if (w > yl) context.FillRectangle(new SolidColorBrush(Color.Parse("#F04848")), new Rect(yl, y, w - yl, h));
            if (hold > -59) context.FillRectangle(Brushes.White, new Rect(Math.Max(0, b.Width * (hold + 60) / 60 - 1.5), y, 1.5, h));
        }
    }
}

/// <summary>Lumetri-style scopes drawn from RGBA frames: luma waveform, RGB parade, vectorscope with skin-tone line, histogram.</summary>
internal sealed class ScopesView : Grid
{
    readonly Image image = new() { Stretch = Stretch.Uniform };
    readonly ComboBox mode = new() { ItemsSource = new[] { "Waveform (luma)", "RGB parade", "Vectorscope", "Histogram" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12, Margin = new Thickness(10, 10, 10, 6), Height = 30, VerticalContentAlignment = VerticalAlignment.Center };
    WriteableBitmap? bitmap; byte[]? lastFrame; int lastW, lastH;
    internal byte[]? LastPixels;
    internal int Mode { get => mode.SelectedIndex; set => mode.SelectedIndex = value; }
    /// <summary>Headless check: a left-to-right gray ramp must draw a diagonal trace on the waveform and a lit centre on the vectorscope.</summary>
    internal static void Verify()
    {
        var view = new ScopesView(); int fw = 320, fh = 180; var rgba = new byte[fw * fh * 4];
        for (int y = 0; y < fh; y++) for (int x = 0; x < fw; x++) { int i = (y * fw + x) * 4; byte v = (byte)(x * 255 / (fw - 1)); rgba[i] = rgba[i + 1] = rgba[i + 2] = v; rgba[i + 3] = 255; }
        for (int m = 0; m < 4; m++)
        {
            view.Mode = m; view.Update(rgba, fw, fh);
            int lit = 0; var px = view.LastPixels!; for (int i = 0; i < px.Length; i += 4) if (Math.Max(px[i], Math.Max(px[i + 1], px[i + 2])) > 150) lit++;
            // Gray has no colour, so the vectorscope shows one dot at the centre; the other scopes spread across.
            if (m == 2 ? lit < 1 || px[(H / 2 * W + W / 2) * 4 + 1] < 120 : lit < 20) throw new InvalidOperationException($"Scope mode {m} drew no trace ({lit} bright pixels).");
            if (m == 0)
            {
                // Waveform: dark on the left of the frame is low, bright on the right is high.
                int Row(int col) { for (int y = 0; y < H; y++) { int o = (y * W + col) * 4; if (px[o + 1] > 120 && px[o + 1] > px[o] + 20) return y; } return -1; }
                int left = Row(8), right = Row(W - 8);
                if (left < 0 || right < 0 || right >= left) throw new InvalidOperationException($"Waveform trace is not a rising ramp ({left} → {right}).");
            }
        }
        Console.WriteLine("Scopes smoke passed: waveform, RGB parade, vectorscope and histogram all draw, and a gray ramp reads as a rising waveform.");
    }
    const int W = 512, H = 300;
    public ScopesView()
    {
        RowDefinitions = new RowDefinitions("Auto,*");
        Children.Add(mode);
        var host = new Border { Child = image, Background = new SolidColorBrush(Color.Parse("#0B0C0E")), Margin = new Thickness(10, 0, 10, 10), CornerRadius = new CornerRadius(4), ClipToBounds = true };
        SetRow(host, 1); Children.Add(host);
        mode.SelectionChanged += (_, _) => { if (lastFrame is not null) Update(lastFrame, lastW, lastH); };
    }
    public void Update(byte[] rgba, int fw, int fh)
    {
        lastFrame = rgba; lastW = fw; lastH = fh;
        bitmap ??= new WriteableBitmap(new PixelSize(W, H), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        var counts = new int[W * H * 3];
        int step = Math.Max(1, fw / 480);
        int m = mode.SelectedIndex;
        for (int y = 0; y < fh; y += step)
            for (int x = 0; x < fw; x += step)
            {
                int i = (y * fw + x) * 4; int r = rgba[i], g = rgba[i + 1], b = rgba[i + 2];
                switch (m)
                {
                    case 0: { int col = x * W / fw, luma = (int)(.2126 * r + .7152 * g + .0722 * b), row = H - 1 - luma * (H - 1) / 255; counts[(row * W + col) * 3 + 1]++; break; }
                    case 1:
                    {
                        int third = W / 3, col = x * third / fw;
                        counts[((H - 1 - r * (H - 1) / 255) * W + col) * 3 + 0]++;
                        counts[((H - 1 - g * (H - 1) / 255) * W + third + col) * 3 + 1]++;
                        counts[((H - 1 - b * (H - 1) / 255) * W + 2 * third + col) * 3 + 2]++;
                        break;
                    }
                    case 2:
                    {
                        double cb = -.1146 * r - .3854 * g + .5 * b, cr = .5 * r - .4542 * g - .0458 * b, scale = (H - 20) / 2.0 / 128;
                        int px = (int)(W / 2 + cb * scale), py = (int)(H / 2 - cr * scale);
                        if (px >= 0 && px < W && py >= 0 && py < H) { counts[(py * W + px) * 3]++; counts[(py * W + px) * 3 + 1]++; counts[(py * W + px) * 3 + 2]++; }
                        break;
                    }
                    default:
                    {
                        counts[r * 2 * 3]++; counts[g * 2 * 3 + 1]++; counts[b * 2 * 3 + 2]++; break;
                    }
                }
            }
        var dst = new byte[W * H * 4]; const int stride = W * 4; LastPixels = dst;
        {
            for (int i = 0; i < W * H; i++) { dst[i * 4] = 11; dst[i * 4 + 1] = 12; dst[i * 4 + 2] = 14; dst[i * 4 + 3] = 255; }
            void Line(int x0, int y0, int x1, int y1, byte v) { int n = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)); for (int k = 0; k <= n; k++) { int x = x0 + (x1 - x0) * k / Math.Max(1, n), y = y0 + (y1 - y0) * k / Math.Max(1, n); if (x >= 0 && x < W && y >= 0 && y < H) { int o = (y * stride) + x * 4; dst[o] = dst[o + 1] = dst[o + 2] = v; } } }
            if (m == 3)
            {
                int max = 1; for (int v = 0; v < 256; v++) for (int c = 0; c < 3; c++) max = Math.Max(max, counts[v * 2 * 3 + c]);
                for (int v = 0; v < 256; v++)
                    for (int c = 0; c < 3; c++)
                    {
                        int height = (int)(Math.Sqrt(counts[v * 2 * 3 + c] / (double)max) * (H - 10));
                        for (int y = H - 1; y >= H - height; y--) for (int dx = 0; dx < 2; dx++) { int o = y * stride + (v * 2 + dx) * 4; dst[o + 2 - c] = (byte)Math.Min(255, dst[o + 2 - c] + 150); }
                    }
            }
            else
            {
                // Graticule first so traces draw over it.
                if (m == 2)
                {
                    int cx = W / 2, cy = H / 2, rad = (H - 20) / 2;
                    for (int a = 0; a < 360; a++) { double t = a * Math.PI / 180; int x = cx + (int)(Math.Cos(t) * rad), y = cy + (int)(Math.Sin(t) * rad); if (x >= 0 && x < W && y >= 0 && y < H) { int o = y * stride + x * 4; dst[o] = dst[o + 1] = dst[o + 2] = 60; } }
                    // Skin-tone line (about 123° on a standard vectorscope).
                    double skin = -123 * Math.PI / 180; Line(cx, cy, cx + (int)(Math.Cos(skin) * rad), cy + (int)(Math.Sin(skin) * rad), 90);
                }
                else foreach (int ire in new[] { 0, 25, 50, 75, 100 }) { int y = H - 1 - ire * (H - 1) / 100; Line(0, y, W - 1, y, 45); }
                for (int i = 0; i < W * H; i++)
                {
                    int r = counts[i * 3], g = counts[i * 3 + 1], b = counts[i * 3 + 2]; if (r + g + b == 0) continue;
                    int o = (i / W) * stride + (i % W) * 4;
                    static byte V(int n) => (byte)Math.Min(255, 60 + Math.Log2(1 + n) * 40);
                    if (m == 0) { byte v = V(g); dst[o] = (byte)(v * .7); dst[o + 1] = v; dst[o + 2] = (byte)(v * .75); }
                    else if (m == 2) { byte v = V(r); dst[o] = (byte)(v * .6); dst[o + 1] = v; dst[o + 2] = (byte)(v * .7); }
                    else { if (r > 0) dst[o + 2] = V(r); if (g > 0) dst[o + 1] = V(g); if (b > 0) dst[o] = V(b); }
                }
            }
        }
        using (var buffer = bitmap.Lock())
            for (int y = 0; y < H; y++) System.Runtime.InteropServices.Marshal.Copy(dst, y * stride, buffer.Address + y * buffer.RowBytes, stride);
        image.Source = null; image.Source = bitmap;
    }
}
