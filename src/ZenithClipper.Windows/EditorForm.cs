using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using ZenithClipper.Windows.Media;
using ZenithClipper.Windows.Ui;

namespace ZenithClipper.Windows;
using Control = System.Windows.Forms.Control;

/// <summary>
/// Clip editor, launched on demand as its own process. Library on the left, preview + timeline in the middle,
/// inspector on the right. Exports always write a new file; originals are never modified.
/// </summary>
public sealed class EditorForm : Form
{
    public const string WindowTitle = "ZenithClipper Editor";
    static readonly (string Label, double? Aspect)[] Aspects = [("Original", null), ("16:9", 16 / 9.0), ("9:16", 9 / 16.0), ("1:1", 1), ("4:5", 4 / 5.0), ("21:9", 21 / 9.0)];
    static readonly (string Label, int Height)[] Sizes = [("Original", 0), ("1440p", 1440), ("1080p", 1080), ("720p", 720), ("480p", 480)];
    static readonly double[] Speeds = [0.5, 0.75, 1, 1.25, 1.5, 2];

    readonly LibraryList library = new();
    readonly ZField search = new("Search clips", Glyphs.Search);
    readonly PreviewCanvas preview = new();
    readonly Timeline timeline = new();
    readonly ZButton play = new("", ButtonKind.Primary, Glyphs.Play) { IconOnly = true, AutoWidth = false };
    readonly ZLabel clock = new("0:00.00 / 0:00.00", Theme.Mono, Theme.Text);
    readonly ZLabel titleLabel = new("No clip selected", Theme.Strong, Theme.Text);
    readonly ZLabel metaLabel = new("", Theme.Small, Theme.Muted);
    readonly ZButton exportTop = new("Export", ButtonKind.Primary, Glyphs.Export);

    readonly ZField inField = new(), outField = new();
    readonly ZLabel lengthLabel = new("", Theme.Small, Theme.Muted);
    readonly ZToggle fast = new();
    readonly SettingRow fastRow;
    readonly ZChips aspect = new(), size = new(), speed = new();
    readonly ZToggle mute = new();
    readonly ZSlider volume = new(0, 400, 100) { Format = v => $"{Math.Round(v)}%" };
    readonly ZLabel summary = new("", Theme.Small, Theme.Muted) { Wraps = true };
    readonly ZButton export = new("Export clip", ButtonKind.Primary, Glyphs.Export) { Height = Theme.S(44) };
    readonly ZButton cancel = new("Cancel export", ButtonKind.Ghost) { Visible = false };
    readonly Shimmer progress = new() { Visible = false };
    readonly Banner result = new();
    readonly PreviewPlayer player = new();
    readonly System.Windows.Forms.Timer scrubDelay = new() { Interval = 40 };

    Settings settings = new();
    string? path;
    ClipInfo? info;
    double playhead, inPoint, outPoint;
    double? cropAspect;
    float cropX = 0.5f, cropY = 0.5f;
    int targetHeight;
    double speedValue = 1;
    CancellationTokenSource? clipLoad, scrub, exportCts;
    bool exporting, syncing;
    string? lastExport;
    readonly string? initial;

    public EditorForm(string? path)
    {
        initial = path;
        Text = WindowTitle; BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.Body; KeyPreview = true; AllowDrop = true; DoubleBuffered = true;
        ClientSize = new Size(Theme.S(1380), Theme.S(860)); MinimumSize = new Size(Theme.S(1040), Theme.S(680)); StartPosition = FormStartPosition.CenterScreen;
        var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
        if (Width > screen.Width || Height > screen.Height) { Size = new Size(Math.Min(Width, screen.Width), Math.Min(Height, screen.Height)); WindowState = FormWindowState.Maximized; }

        fastRow = new SettingRow("Keyframe-fast", "No re-encode. The start snaps to the nearest earlier keyframe.", fast, 44);
        var layout = new EditorLayout { Dock = DockStyle.Fill, Top = BuildTopBar(), Left = BuildLibrary(), Right = BuildInspector(), Center = BuildCenter(), Timeline = timeline };
        Controls.Add(layout);

        scrubDelay.Tick += async (_, _) => { scrubDelay.Stop(); await GrabFrameAsync(); };
        player.Ended += () => { SetPlaying(false); };
        Shown += async (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            try { settings = AppPaths.Load(); } catch (Exception ex) when (ex is IOException or ArgumentException or System.Text.Json.JsonException) { result.Show("Settings could not be read; using defaults. " + ex.Message, Banner.Tone.Warning); }
            await library.LoadAsync(settings);
            if (initial != null && File.Exists(initial)) await OpenAsync(initial);
            else if (library.First is { } first) await OpenAsync(first);
        };
        FormClosing += (_, e) =>
        {
            if (!exporting) return;
            e.Cancel = true; exportCts?.Cancel();
            result.Show("Cancelling export. Close again once it stops.", Banner.Tone.Warning);
        };
        FormClosed += (_, _) => { player.Dispose(); scrubDelay.Dispose(); clipLoad?.Cancel(); scrub?.Cancel(); };
        DragEnter += (_, e) => { if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
        DragDrop += async (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) await OpenAsync(files[0]); };
        UpdateEditState();
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Native.StyleWindow(this); }

    /// <summary>A second "editor &lt;file&gt;" launch forwards its path here instead of opening another window.</summary>
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x4A && m.LParam != 0)
        {
            var data = Marshal.PtrToStructure<CopyData>(m.LParam);
            if (data.Data != 0 && data.Length > 0)
            {
                string file = Marshal.PtrToStringUni(data.Data, data.Length / 2).TrimEnd('\0');
                if (File.Exists(file)) BeginInvoke(async () => { await library.LoadAsync(settings); await OpenAsync(file); });
            }
            m.Result = 1; return;
        }
        base.WndProc(ref m);
    }
    [StructLayout(LayoutKind.Sequential)] internal struct CopyData { public nint Id; public int Length; public nint Data; }

    // ─────────────────────────── layout ───────────────────────────
    Control BuildTopBar()
    {
        var bar = new TopBar { Height = Theme.S(60) };
        var row = new HRow(40) { Dock = DockStyle.Fill };
        var names = new VStack { BackColor = Theme.Bg, Gap = 0, Width = Theme.S(10), Height = Theme.S(42) };
        names.Add(titleLabel); names.Add(metaLabel);
        row.Add(names, fill: true);
        var folder = row.Add(new ZButton("Show in folder", ButtonKind.Ghost, Glyphs.Folder));
        folder.Click += (_, _) => { if (path != null) Reveal(path); };
        var external = row.Add(new ZButton("Play with sound", ButtonKind.Ghost, Glyphs.Speakers));
        external.Click += (_, _) => { if (path != null) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); };
        row.Add(exportTop);
        exportTop.Click += async (_, _) => await ExportAsync();
        bar.Padding = new Padding(Theme.S(70), Theme.S(10), Theme.S(18), Theme.S(10));
        bar.Controls.Add(row);
        return bar;
    }

    Control BuildLibrary()
    {
        var side = new SidePanel { Padding = new Padding(Theme.S(14)) };
        var stack = new LibraryHost(library) { Dock = DockStyle.Fill };
        var top = new VStack { Dock = DockStyle.Top, BackColor = Theme.Surface, Gap = Theme.S(10), Height = Theme.S(92) };
        var head = top.Add(new HRow(32));
        head.Add(new ZLabel("Library", Theme.Title, Theme.Text), fill: true);
        var import = head.Add(new ZButton("", ButtonKind.Ghost, Glyphs.Add) { IconOnly = true, AutoWidth = false, Width = Theme.S(32), Height = Theme.S(32) });
        var refresh = head.Add(new ZButton("", ButtonKind.Ghost, Glyphs.Refresh) { IconOnly = true, AutoWidth = false, Width = Theme.S(32), Height = Theme.S(32) });
        top.Add(search);
        import.Click += async (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = "Video|*.mp4;*.mkv;*.mov;*.ts;*.webm|All files|*.*", InitialDirectory = settings.ClipDirectory };
            if (dialog.ShowDialog(this) == DialogResult.OK) await OpenAsync(dialog.FileName);
        };
        refresh.Click += async (_, _) => await library.LoadAsync(settings);
        search.Box.TextChanged += (_, _) => library.Filter(search.Text);
        library.Picked += async file => await OpenAsync(file);
        library.Menu = BuildLibraryMenu();
        side.Controls.Add(stack); side.Controls.Add(top);
        return side;
    }

    ContextMenuStrip BuildLibraryMenu()
    {
        var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), BackColor = Theme.Raised, ForeColor = Theme.Text, Font = Theme.Body, ShowImageMargin = false };
        menu.Items.Add("Open", null, async (_, _) => { if (library.MenuTarget is { } p) await OpenAsync(p); });
        menu.Items.Add("Show in folder", null, (_, _) => { if (library.MenuTarget is { } p) Reveal(p); });
        menu.Items.Add("Rename…", null, async (_, _) => await RenameAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Move to Recycle Bin", null, async (_, _) => await DeleteAsync());
        foreach (ToolStripItem item in menu.Items) { item.ForeColor = Theme.Text; item.Padding = new Padding(Theme.S(6), Theme.S(4), Theme.S(6), Theme.S(4)); }
        return menu;
    }

    Control BuildCenter()
    {
        var center = new CenterColumn { Preview = preview };
        var transport = new HRow(44);
        var toStart = transport.Add(new ZButton("", ButtonKind.Ghost, Glyphs.Previous) { IconOnly = true, AutoWidth = false, Width = Theme.S(40), Height = Theme.S(40) });
        play.Width = play.Height = Theme.S(44); transport.Add(play);
        var toEnd = transport.Add(new ZButton("", ButtonKind.Ghost, Glyphs.Next) { IconOnly = true, AutoWidth = false, Width = Theme.S(40), Height = Theme.S(40) });
        clock.Width = Theme.S(200); transport.Add(clock);
        transport.Add(new ZLabel("Space play  ·  I / O set in and out  ·  ← → frame  ·  Shift 1s", Theme.Small, Theme.Faint) { Align = TextFormatFlags.Right | TextFormatFlags.VerticalCenter }, fill: true);
        var setIn = transport.Add(new ZButton("Set in", ButtonKind.Secondary, Glyphs.Bracket) { Hint = "I" });
        var setOut = transport.Add(new ZButton("Set out", ButtonKind.Secondary, Glyphs.Bracket) { Hint = "O" });
        center.Transport = transport;
        play.Click += (_, _) => TogglePlay();
        toStart.Click += (_, _) => Seek(inPoint);
        toEnd.Click += (_, _) => Seek(Math.Max(inPoint, outPoint - 0.05));
        setIn.Click += (_, _) => SetIn(playhead);
        setOut.Click += (_, _) => SetOut(playhead);
        timeline.Seek += (t, dragging) => { if (player.Playing) SetPlaying(false); Seek(t, dragging); };
        timeline.RangeChanged += (a, b) => { inPoint = a; outPoint = b; SyncTrimFields(); UpdateEditState(); };
        preview.CropMoved += (x, y) => { cropX = x; cropY = y; UpdateCrop(); };
        return center;
    }

    Control BuildInspector()
    {
        var side = new SidePanel { Padding = new Padding(Theme.S(6)) };
        var stack = new VStack { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Surface, Padding = new Padding(Theme.S(14), Theme.S(14), Theme.S(14), Theme.S(14)), Gap = Theme.S(12) };
        Native.DarkScrollbars(stack);

        var trim = stack.Add(new AutoCard("Trim", Glyphs.Cut) { BackColor = Theme.Raised });
        var inRow = trim.Add(new HRow(36));
        inRow.Add(new ZLabel("In", Theme.Body, Theme.Muted) { Width = Theme.S(36) });
        inRow.Add(inField, fill: true);
        inRow.Add(new ZButton("", ButtonKind.Secondary, Glyphs.Bracket) { IconOnly = true, AutoWidth = false, Width = Theme.S(36) }).Click += (_, _) => SetIn(playhead);
        var outRow = trim.Add(new HRow(36));
        outRow.Add(new ZLabel("Out", Theme.Body, Theme.Muted) { Width = Theme.S(36) });
        outRow.Add(outField, fill: true);
        outRow.Add(new ZButton("", ButtonKind.Secondary, Glyphs.Bracket) { IconOnly = true, AutoWidth = false, Width = Theme.S(36) }).Click += (_, _) => SetOut(playhead);
        trim.Add(lengthLabel);
        trim.Add(fastRow);
        fast.SetSilently(true);
        fast.CheckedChanged += (_, _) => UpdateEditState();
        foreach (var (field, isIn) in new[] { (inField, true), (outField, false) })
        {
            field.Box.Font = Theme.Mono;
            field.Box.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; CommitTime(field, isIn); preview.Focus(); } };
            field.Box.LostFocus += (_, _) => CommitTime(field, isIn);
        }

        var frame = stack.Add(new AutoCard("Frame", Glyphs.Crop) { BackColor = Theme.Raised });
        frame.Add(new ZLabel("ASPECT", Theme.Caption, Theme.Faint));
        foreach (var (label, value) in Aspects) aspect.Add(label, value ?? 0.0);
        aspect.SelectedIndex = 0; frame.Add(aspect);
        frame.Add(new ZLabel("Drag inside the preview to reposition the crop.", Theme.Small, Theme.Muted) { Wraps = true });
        frame.Add(new ZLabel("SIZE", Theme.Caption, Theme.Faint) { Margin = new Padding(0, Theme.S(6), 0, 0) });
        foreach (var (label, value) in Sizes) size.Add(label, value);
        size.SelectedIndex = 0; frame.Add(size);
        aspect.SelectionChanged += (_, _) => { cropAspect = aspect.SelectedValue is double a && a > 0 ? a : null; cropX = cropY = 0.5f; UpdateCrop(); };
        size.SelectionChanged += (_, _) => { targetHeight = size.SelectedValue is int h ? h : 0; UpdateEditState(); };

        var speedCard = stack.Add(new AutoCard("Speed", Glyphs.Speed) { BackColor = Theme.Raised });
        foreach (double s in Speeds) speed.Add(s.ToString("0.##", CultureInfo.InvariantCulture) + "×", s);
        speed.Select(1.0); speedCard.Add(speed);
        speed.SelectionChanged += (_, _) => { speedValue = speed.SelectedValue is double s ? s : 1; UpdateEditState(); };

        var audioCard = stack.Add(new AutoCard("Audio", Glyphs.Volume) { BackColor = Theme.Raised });
        audioCard.Add(new SettingRow("Mute", null, mute, 44));
        audioCard.Add(new ZLabel("VOLUME", Theme.Caption, Theme.Faint));
        audioCard.Add(volume);
        mute.CheckedChanged += (_, _) => { volume.Enabled = !mute.Checked; timeline.Muted = mute.Checked; timeline.Invalidate(); UpdateEditState(); };
        volume.ValueChanged += (_, _) => UpdateEditState();

        var exportCard = stack.Add(new AutoCard("Export", Glyphs.Export) { BackColor = Theme.Raised });
        exportCard.Add(summary);
        exportCard.Add(export);
        exportCard.Add(progress);
        exportCard.Add(cancel);
        exportCard.Add(result);
        export.Click += async (_, _) => await ExportAsync();
        cancel.Click += (_, _) => exportCts?.Cancel();
        result.ActionClicked += (_, _) => { if (lastExport != null) Reveal(lastExport); };

        side.Controls.Add(stack);
        return side;
    }

    // ─────────────────────────── clip loading ───────────────────────────
    async Task OpenAsync(string file)
    {
        if (exporting) { result.Show("Finish or cancel the export before switching clips.", Banner.Tone.Warning); return; }
        SetPlaying(false);
        clipLoad?.Cancel(); var cts = clipLoad = new CancellationTokenSource();
        try
        {
            var clip = await Frames.InfoAsync(settings, file, cts.Token);
            if (cts.IsCancellationRequested) return;
            path = file; info = clip; playhead = inPoint = 0; outPoint = clip.Duration;
            cropAspect = null; cropX = cropY = 0.5f; targetHeight = 0; speedValue = 1;
            aspect.SelectedIndex = 0; size.SelectedIndex = 0; speed.Select(1.0); mute.SetSilently(false); volume.Value = 100; volume.Enabled = true;
            titleLabel.Text = Path.GetFileNameWithoutExtension(file);
            metaLabel.Text = $"{clip.Width}×{clip.Height}  ·  {clip.Fps:0.##} fps  ·  {Theme.Duration(clip.Duration)}  ·  {Theme.Bytes(clip.Bytes)}{(clip.HasAudio ? "" : "  ·  no audio")}";
            library.Highlight(file);
            timeline.Load(clip.Duration);
            preview.SetSource(clip.Width, clip.Height); preview.SetFrame(null);
            result.Dismiss();
            SyncTrimFields(); UpdateCrop(); UpdateEditState();
            await GrabFrameAsync();
            _ = LoadTimelineAsync(file, clip, cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Text.Json.JsonException or KeyNotFoundException or System.ComponentModel.Win32Exception)
        {
            result.Show($"Couldn't open {Path.GetFileName(file)}: {ex.Message}", Banner.Tone.Error);
        }
    }

    async Task LoadTimelineAsync(string file, ClipInfo clip, CancellationToken ct)
    {
        try
        {
            int tiles = 16;
            await Frames.FilmstripAsync(settings, file, clip, tiles, Theme.S(64), (i, bmp) => BeginInvoke(() => { if (path == file) timeline.SetTile(i, tiles, bmp); else bmp.Dispose(); }), ct);
            var peaks = await Frames.PeaksAsync(settings, file, clip, 1200, ct);
            if (path == file) timeline.SetPeaks(peaks);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidOperationException or System.ComponentModel.Win32Exception or ObjectDisposedException) { }
    }

    async Task GrabFrameAsync()
    {
        if (path == null || info == null) return;
        scrub?.Cancel(); var cts = scrub = new CancellationTokenSource();
        var (w, h) = preview.DecodeSize();
        try
        {
            var bmp = await Frames.GrabAsync(settings, path, Math.Min(playhead, Math.Max(0, info.Duration - 0.05)), w, h, cts.Token);
            if (cts.IsCancellationRequested || player.Playing) { bmp?.Dispose(); return; }
            if (bmp != null) preview.SetFrame(bmp);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or System.ComponentModel.Win32Exception) { }
    }

    // ─────────────────────────── playback ───────────────────────────
    void TogglePlay() => SetPlaying(!player.Playing);
    void SetPlaying(bool on)
    {
        if (!on) { if (player.Playing) { player.Stop(); _ = GrabFrameAsync(); } play.Glyph = Glyphs.Play; return; }
        if (path == null || info == null) return;
        if (playhead >= outPoint - 0.05 || playhead < inPoint) playhead = inPoint;
        var (w, h) = preview.DecodeSize();
        try
        {
            player.Start(settings, path, playhead, outPoint, w, h, info.Fps, SynchronizationContext.Current!, (bgra, t) =>
            {
                preview.ShowRaw(bgra, w, h); playhead = Math.Min(t, outPoint); timeline.Playhead = playhead; UpdateClock();
            });
            play.Glyph = Glyphs.Pause;
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception) { result.Show(ex.Message, Banner.Tone.Error); }
    }

    void Seek(double t, bool dragging = false)
    {
        if (info == null) return;
        playhead = Math.Clamp(t, 0, info.Duration); timeline.Playhead = playhead; UpdateClock();
        if (player.Playing) { SetPlaying(false); }
        scrubDelay.Stop(); if (dragging) scrubDelay.Start(); else _ = GrabFrameAsync();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (ActiveControl is ZField or TextBox || ActiveControl?.Parent is ZField) return base.ProcessCmdKey(ref msg, keyData);
        if (info == null) return base.ProcessCmdKey(ref msg, keyData);
        double frame = 1 / Math.Max(1, info.Fps);
        switch (keyData)
        {
            case Keys.Space: TogglePlay(); return true;
            case Keys.I: SetIn(playhead); return true;
            case Keys.O: SetOut(playhead); return true;
            case Keys.Left: Seek(playhead - frame); return true;
            case Keys.Right: Seek(playhead + frame); return true;
            case Keys.Shift | Keys.Left: Seek(playhead - 1); return true;
            case Keys.Shift | Keys.Right: Seek(playhead + 1); return true;
            case Keys.Home: Seek(inPoint); return true;
            case Keys.End: Seek(outPoint); return true;
            case Keys.Control | Keys.E: _ = ExportAsync(); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ─────────────────────────── edits ───────────────────────────
    void SetIn(double t) { if (info == null) return; inPoint = Math.Clamp(t, 0, Math.Max(0, outPoint - 0.1)); timeline.SetRange(inPoint, outPoint); SyncTrimFields(); UpdateEditState(); }
    void SetOut(double t) { if (info == null) return; outPoint = Math.Clamp(t, inPoint + 0.1, info.Duration); timeline.SetRange(inPoint, outPoint); SyncTrimFields(); UpdateEditState(); }
    void SyncTrimFields()
    {
        syncing = true;
        try { inField.Text = Theme.Duration(inPoint, true); outField.Text = Theme.Duration(outPoint, true); }
        finally { syncing = false; }
        lengthLabel.Text = info == null ? "" : $"Selection {Theme.Duration(outPoint - inPoint, true)} of {Theme.Duration(info.Duration, true)}";
        UpdateClock();
    }
    void CommitTime(ZField field, bool isIn)
    {
        if (syncing || info == null) return;
        if (ParseTime(field.Text) is double t) { if (isIn) SetIn(t); else SetOut(t); }
        else SyncTrimFields();
    }
    static double? ParseTime(string text)
    {
        text = text.Trim(); if (text.Length == 0) return null;
        var parts = text.Split(':'); double total = 0;
        foreach (var part in parts)
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || v < 0) return null;
            total = total * 60 + v;
        }
        return total;
    }
    void UpdateClock() => clock.Text = $"{Theme.Duration(playhead, true)} / {Theme.Duration(info?.Duration ?? 0, true)}";

    Rectangle? CropRect()
    {
        if (info == null || cropAspect is not double a) return null;
        int w = info.Width, h = info.Height, cw, ch;
        if (w / (double)h > a) { ch = h & ~1; cw = Math.Min(w, (int)Math.Round(h * a)) & ~1; }
        else { cw = w & ~1; ch = Math.Min(h, (int)Math.Round(w / a)) & ~1; }
        int x = (int)Math.Round((w - cw) * cropX) & ~1, y = (int)Math.Round((h - ch) * cropY) & ~1;
        return new Rectangle(Math.Clamp(x, 0, w - cw), Math.Clamp(y, 0, h - ch), cw, ch);
    }
    void UpdateCrop() { preview.Crop = CropRect(); UpdateEditState(); }

    (int Width, int Height) OutputSize()
    {
        if (info == null) return (0, 0);
        var crop = CropRect(); int w = crop?.Width ?? info.Width, h = crop?.Height ?? info.Height;
        if (targetHeight > 0 && targetHeight < h) { w = (int)Math.Round(targetHeight * w / (double)h) & ~1; h = targetHeight; }
        return (w, h);
    }
    bool NeedsEncode => cropAspect != null || OutputSize().Height != (CropRect()?.Height ?? info?.Height) || speedValue != 1 || (!mute.Checked && Math.Abs(volume.Value - 100) > 0.5);

    void UpdateEditState()
    {
        bool has = info != null;
        export.Enabled = exportTop.Enabled = has && !exporting;
        if (info == null) { summary.Text = "Pick a clip from the library, or drop a video file onto this window."; return; }
        bool encode = NeedsEncode;
        fast.Enabled = !encode;
        fastRow.Detail = encode ? "Turned off: crop, size, speed or volume changes need a re-encode." : fast.Checked ? "No re-encode, near-instant. The start snaps to the nearest earlier keyframe." : "Frame-accurate start. Re-encodes using your GPU.";
        var (w, h) = OutputSize();
        double length = (outPoint - inPoint) / speedValue;
        string method = encode || !fast.Checked ? $"re-encode with {(settings.Encoder == "auto" ? "your GPU" : settings.Encoder.ToUpperInvariant())}" : "fast copy, no quality loss";
        summary.Text = $"{w}×{h}  ·  {Theme.Duration(length, true)}  ·  {(mute.Checked || !info.HasAudio ? "no audio" : $"audio {Math.Round(volume.Value)}%")}\n{char.ToUpperInvariant(method[0]) + method[1..]}. The original clip is kept.";
        summary.Parent?.PerformLayout();
    }

    // ─────────────────────────── export ───────────────────────────
    async Task ExportAsync()
    {
        if (path == null || info == null || exporting) return;
        string dir = Path.GetDirectoryName(path) ?? settings.ClipDirectory;
        string name = Path.GetFileNameWithoutExtension(path) + " edit";
        string suggestion = Path.Combine(dir, name + ".mp4");
        for (int i = 2; File.Exists(suggestion); i++) suggestion = Path.Combine(dir, $"{name} {i}.mp4");
        using var dialog = new SaveFileDialog { Filter = "MP4 video|*.mp4", DefaultExt = "mp4", FileName = Path.GetFileName(suggestion), InitialDirectory = dir, OverwritePrompt = false, Title = "Export clip" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (File.Exists(dialog.FileName)) { result.Show("That file already exists. Pick a new name so nothing is overwritten.", Banner.Tone.Warning); return; }

        SetPlaying(false);
        var crop = CropRect(); var (outW, outH) = OutputSize();
        bool trimmed = inPoint > 0.01 || outPoint < info.Duration - 0.01;
        var request = new EditRequest(path, dialog.FileName)
        {
            StartSeconds = inPoint, DurationSeconds = trimmed ? outPoint - inPoint : null, FastTrim = fast.Checked && !NeedsEncode,
            Mute = mute.Checked, Volume = volume.Value / 100, Speed = speedValue,
            CropWidth = crop?.Width ?? 0, CropHeight = crop?.Height ?? 0, CropX = crop?.X ?? 0, CropY = crop?.Y ?? 0,
            OutputWidth = outH != (crop?.Height ?? info.Height) ? outW : 0, Encoder = settings.Encoder, Quality = settings.Quality,
        };
        exporting = true; exportCts = new CancellationTokenSource();
        progress.Visible = cancel.Visible = true; result.Dismiss(); UpdateEditState(); export.Text = "Exporting…";
        var watch = Stopwatch.StartNew();
        try
        {
            await Task.Run(() => Editor.ExportAsync(settings, request, exportCts.Token));
            lastExport = dialog.FileName;
            result.Show($"Exported {Path.GetFileName(dialog.FileName)} in {watch.Elapsed.TotalSeconds:0.0}s.", Banner.Tone.Success, "Show");
            await library.LoadAsync(settings); library.Highlight(path);
        }
        catch (OperationCanceledException) { result.Show("Export cancelled. Nothing was written and the original is untouched.", Banner.Tone.Info); }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { result.Show("Export failed: " + ex.Message, Banner.Tone.Error); }
        finally
        {
            exportCts.Dispose(); exportCts = null; exporting = false;
            progress.Visible = cancel.Visible = false; export.Text = "Export clip"; UpdateEditState();
            progress.Parent?.PerformLayout();
        }
    }

    async Task RenameAsync()
    {
        if (library.MenuTarget is not { } target) return;
        string? name = PromptForm.Ask(this, "Rename clip", "New name:", Path.GetFileNameWithoutExtension(target));
        if (string.IsNullOrWhiteSpace(name)) return;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { result.Show("That name has characters Windows doesn't allow in file names.", Banner.Tone.Warning); return; }
        string next = Path.Combine(Path.GetDirectoryName(target)!, name.Trim() + Path.GetExtension(target));
        if (File.Exists(next)) { result.Show("A clip with that name already exists.", Banner.Tone.Warning); return; }
        if (target == path) { SetPlaying(false); }
        try { File.Move(target, next); } catch (IOException ex) { result.Show(ex.Message, Banner.Tone.Error); return; }
        await library.LoadAsync(settings);
        if (target == path) await OpenAsync(next);
    }

    async Task DeleteAsync()
    {
        if (library.MenuTarget is not { } target || exporting) return;
        if (target == path) { SetPlaying(false); path = null; info = null; preview.SetFrame(null); titleLabel.Text = "No clip selected"; metaLabel.Text = ""; timeline.Load(0); UpdateEditState(); }
        if (!Native.Recycle(target, Handle)) { result.Show("Couldn't move the clip to the Recycle Bin.", Banner.Tone.Error); return; }
        await library.LoadAsync(settings);
        if (path == null && library.First is { } first) await OpenAsync(first);
    }

    static void Reveal(string file) => Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", file }, UseShellExecute = false });

    // ─────────────────────────── layout containers ───────────────────────────
    sealed class EditorLayout : Panel
    {
        Control? top, left, right, center;
        public new Control? Top { get => top; set { top = value; if (value != null) Controls.Add(value); } }
        public new Control? Left { get => left; set { left = value; if (value != null) Controls.Add(value); } }
        public new Control? Right { get => right; set { right = value; if (value != null) Controls.Add(value); } }
        public Control? Center { get => center; set { center = value; if (value != null) Controls.Add(value); } }
        public EditorLayout() { BackColor = Theme.Bg; DoubleBuffered = true; }
        public Control? Timeline { get; set; }
        protected override void OnLayout(LayoutEventArgs levent)
        {
            // Premiere-like: media bin | program monitor | inspector on top, full-width tracks underneath.
            if (Timeline != null && !Controls.Contains(Timeline)) Controls.Add(Timeline);
            int w = ClientSize.Width, h = ClientSize.Height, gap = Theme.S(12), topH = Theme.S(60), tl = Math.Clamp(h / 4, Theme.S(190), Theme.S(260));
            int lw = Math.Clamp(w / 5, Theme.S(250), Theme.S(320)), rw = Math.Clamp(w / 4, Theme.S(300), Theme.S(360));
            int upper = h - topH - tl - gap * 2;
            top?.SetBounds(0, 0, w, topH);
            left?.SetBounds(gap, topH, lw, upper);
            right?.SetBounds(w - rw - gap, topH, rw, upper);
            center?.SetBounds(gap * 2 + lw, topH, w - lw - rw - gap * 4, upper);
            Timeline?.SetBounds(gap, topH + upper + gap, w - gap * 2, tl);
        }
    }

    sealed class CenterColumn : Panel
    {
        Control? preview, timeline, transport;
        public Control? Preview { get => preview; set { preview = value; if (value != null) Controls.Add(value); } }
        public Control? Timeline { get => timeline; set { timeline = value; if (value != null) Controls.Add(value); } }
        public Control? Transport { get => transport; set { transport = value; if (value != null) Controls.Add(value); } }
        public CenterColumn() { BackColor = Theme.Bg; DoubleBuffered = true; }
        protected override void OnLayout(LayoutEventArgs levent)
        {
            int w = ClientSize.Width, h = ClientSize.Height, tr = Theme.S(52);
            transport?.SetBounds(0, h - tr, w, tr);
            preview?.SetBounds(0, 0, w, h - tr - Theme.S(4));
        }
    }

    sealed class SidePanel : Panel
    {
        public SidePanel() { BackColor = Theme.Surface; DoubleBuffered = true; ResizeRedraw = true; }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Theme.Bg); Theme.Prep(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Theme.Fill(g, r, Theme.S(16f), Theme.Surface); Theme.Stroke(g, r, Theme.S(16f), Theme.Alpha(Theme.Line, 160));
        }
    }

    sealed class TopBar : Panel
    {
        public TopBar() { BackColor = Theme.Bg; DoubleBuffered = true; ResizeRedraw = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Prep(g);
            Theme.Orb(g, new RectangleF(Theme.S(24), (Height - Theme.S(30)) / 2f, Theme.S(30), Theme.S(30)));
        }
    }

    sealed class LibraryHost : Panel
    {
        public LibraryHost(Control list) { BackColor = Theme.Surface; Padding = new Padding(0, Theme.S(6), 0, 0); list.Dock = DockStyle.Fill; Controls.Add(list); }
    }

    internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = e.Item?.Text?.StartsWith("Move", StringComparison.Ordinal) == true ? Theme.Mix(Theme.Live, Color.White, 0.3f) : Theme.Text; base.OnRenderItemText(e); }
        sealed class DarkColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Theme.Raised;
            public override Color MenuBorder => Theme.Line;
            public override Color MenuItemSelected => Theme.Hover;
            public override Color MenuItemBorder => Theme.Hover;
            public override Color SeparatorDark => Theme.Line;
            public override Color SeparatorLight => Theme.Raised;
            public override Color ImageMarginGradientBegin => Theme.Raised;
            public override Color ImageMarginGradientMiddle => Theme.Raised;
            public override Color ImageMarginGradientEnd => Theme.Raised;
        }
    }
}

/// <summary>Owner-drawn clip list with cached thumbnails, search and a context menu.</summary>
sealed class LibraryList : ListBox
{
    sealed class Item(string path, DateTime when, long bytes)
    {
        public string Path { get; } = path; public DateTime When { get; } = when; public long Bytes { get; } = bytes;
        public Bitmap? Thumb { get; set; } public ClipInfo? Info { get; set; }
        public override string ToString() => System.IO.Path.GetFileNameWithoutExtension(Path);
    }
    readonly List<Item> all = [];
    string filter = "", highlighted = "";
    CancellationTokenSource? loading;
    int hover = -1;
    public event Func<string, Task>? Picked;
    public ContextMenuStrip? Menu { get; set; }
    public string? MenuTarget { get; private set; }
    public string? First => all.FirstOrDefault()?.Path;
    public LibraryList()
    {
        DrawMode = DrawMode.OwnerDrawFixed; ItemHeight = Math.Min(255, Theme.S(76)); BorderStyle = BorderStyle.None; BackColor = Theme.Surface; ForeColor = Theme.Text;
        IntegralHeight = false; SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        Native.DarkScrollbars(this);
    }
    public async Task LoadAsync(Settings settings)
    {
        loading?.Cancel(); var cts = loading = new CancellationTokenSource();
        foreach (var i in all) i.Thumb?.Dispose();
        all.Clear();
        if (Directory.Exists(settings.ClipDirectory))
            foreach (var f in new DirectoryInfo(settings.ClipDirectory).EnumerateFiles().Where(f => !f.Name.StartsWith('.') && f.Extension.ToLowerInvariant() is ".mp4" or ".mkv" or ".mov" or ".webm").OrderByDescending(f => f.LastWriteTimeUtc).Take(500))
                all.Add(new Item(f.FullName, f.LastWriteTime, f.Length));
        Apply();
        foreach (var item in all.ToList())
        {
            if (cts.IsCancellationRequested) return;
            try
            {
                item.Info = await Frames.InfoAsync(settings, item.Path, cts.Token);
                item.Thumb = await Frames.ThumbnailAsync(settings, item.Path, cts.Token);
                if (!cts.IsCancellationRequested) Invalidate();
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Text.Json.JsonException or KeyNotFoundException or System.ComponentModel.Win32Exception) { }
        }
    }
    public void Filter(string text) { filter = text.Trim(); Apply(); }
    public void Highlight(string path) { highlighted = path; Invalidate(); }
    void Apply()
    {
        BeginUpdate();
        try { Items.Clear(); foreach (var i in all.Where(i => filter.Length == 0 || i.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase))) Items.Add(i); }
        finally { EndUpdate(); }
    }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int h = IndexFromPoint(e.Location); if (h != hover) { hover = h; Invalidate(); } }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }
    protected override async void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        int i = IndexFromPoint(e.Location); if (i < 0 || Items[i] is not Item item) return;
        if (e.Button == MouseButtons.Left && Picked != null) await Picked(item.Path);
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Right) return;
        int i = IndexFromPoint(e.Location); if (i < 0 || Items[i] is not Item item) return;
        MenuTarget = item.Path; Menu?.Show(this, e.Location);
    }
    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Enter && SelectedItem is Item item && Picked != null) { e.Handled = true; await Picked(item.Path); }
    }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        var g = e.Graphics; var bounds = e.Bounds;
        using (var bg = new SolidBrush(Theme.Surface)) g.FillRectangle(bg, bounds);
        if (e.Index < 0 || e.Index >= Items.Count || Items[e.Index] is not Item item) return;
        Theme.Prep(g);
        bool current = item.Path == highlighted, hot = e.Index == hover;
        var card = new RectangleF(bounds.X + 1, bounds.Y + Theme.S(3), bounds.Width - 2, bounds.Height - Theme.S(6));
        if (current || hot) Theme.Fill(g, card, Theme.S(10f), current ? Theme.Alpha(Theme.Accent, 46) : Theme.Raised);
        if (current) Theme.Stroke(g, card, Theme.S(10f), Theme.Alpha(Theme.Accent, 160));
        int th = (int)card.Height - Theme.S(12), tw = th * 16 / 9;
        var thumb = new Rectangle((int)card.X + Theme.S(6), (int)card.Y + Theme.S(6), tw, th);
        using (var path = Theme.Round(thumb, Theme.S(7f)))
        {
            Theme.Fill(g, thumb, Theme.S(7f), Theme.Bg);
            if (item.Thumb != null)
            {
                var state = g.Save(); g.SetClip(path);
                float scale = Math.Max(thumb.Width / (float)item.Thumb.Width, thumb.Height / (float)item.Thumb.Height);
                g.DrawImage(item.Thumb, thumb.X + (thumb.Width - item.Thumb.Width * scale) / 2, thumb.Y + (thumb.Height - item.Thumb.Height * scale) / 2, item.Thumb.Width * scale, item.Thumb.Height * scale);
                g.Restore(state);
            }
            else Theme.Glyph(g, Glyphs.Video, 11, Theme.Faint, thumb);
        }
        if (item.Info != null)
        {
            string d = Theme.Duration(item.Info.Duration); var ds = Theme.Measure(d, Theme.Caption);
            var tag = new Rectangle(thumb.Right - ds.Width - Theme.S(10), thumb.Bottom - ds.Height - Theme.S(6), ds.Width + Theme.S(6), ds.Height + Theme.S(2));
            Theme.Fill(g, tag, Theme.S(4f), Theme.Alpha(Color.Black, 170));
            Theme.Label(g, d, Theme.Caption, Color.White, tag, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        int x = thumb.Right + Theme.S(10), w = (int)card.Right - x - Theme.S(6);
        Theme.Label(g, item.ToString(), Theme.Body, Theme.Text, new Rectangle(x, (int)card.Y + Theme.S(10), w, Theme.S(20)));
        string meta = item.When.ToString("MMM d  HH:mm", CultureInfo.CurrentCulture) + "  ·  " + Theme.Bytes(item.Bytes);
        Theme.Label(g, meta, Theme.Small, Theme.Muted, new Rectangle(x, (int)card.Y + Theme.S(32), w, Theme.S(18)));
        if (item.Info != null) Theme.Label(g, $"{item.Info.Width}×{item.Info.Height}  ·  {item.Info.Fps:0} fps", Theme.Small, Theme.Faint, new Rectangle(x, (int)card.Y + Theme.S(48), w, Theme.S(16)));
    }
    protected override void Dispose(bool disposing) { if (disposing) { loading?.Cancel(); foreach (var i in all) i.Thumb?.Dispose(); } base.Dispose(disposing); }
}

/// <summary>Letterboxed frame view with a draggable crop overlay.</summary>
sealed class PreviewCanvas : Control
{
    Bitmap? frame;
    int srcW = 16, srcH = 9;
    Rectangle? crop;
    Point? dragFrom; PointF dragStart;
    public event Action<float, float>? CropMoved;
    public PreviewCanvas()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
    }
    public Rectangle? Crop { get => crop; set { crop = value; Cursor = value != null ? Cursors.SizeAll : Cursors.Default; Invalidate(); } }
    public void SetSource(int w, int h) { srcW = Math.Max(1, w); srcH = Math.Max(1, h); Invalidate(); }
    public void SetFrame(Bitmap? bmp) { var old = frame; frame = bmp; if (!ReferenceEquals(old, bmp)) old?.Dispose(); Invalidate(); }
    public void ShowRaw(byte[] bgra, int w, int h)
    {
        if (frame == null || frame.Width != w || frame.Height != h) SetFrame(new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppRgb));
        Frames.Copy(bgra, frame!); Invalidate();
    }
    /// <summary>Decode size that fills the current view sharply without decoding more pixels than shown.</summary>
    public (int, int) DecodeSize()
    {
        var r = Fit(); int w = Math.Clamp(r.Width, 160, 1600) & ~1;
        return (w, Math.Max(2, (int)Math.Round(w * srcH / (double)srcW) & ~1));
    }
    Rectangle Fit()
    {
        var area = Rectangle.Inflate(ClientRectangle, -Theme.S(8), -Theme.S(8));
        double s = Math.Min(area.Width / (double)srcW, area.Height / (double)srcH);
        int w = Math.Max(1, (int)(srcW * s)), h = Math.Max(1, (int)(srcH * s));
        return new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
    }
    RectangleF CropOnScreen(Rectangle view, Rectangle c) => new(view.X + c.X * view.Width / (float)srcW, view.Y + c.Y * view.Height / (float)srcH, c.Width * view.Width / (float)srcW, c.Height * view.Height / (float)srcH);
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e); Focus();
        if (crop is { } c && e.Button == MouseButtons.Left)
        {
            dragFrom = e.Location;
            dragStart = new PointF(srcW == c.Width ? 0.5f : c.X / (float)(srcW - c.Width), srcH == c.Height ? 0.5f : c.Y / (float)(srcH - c.Height));
        }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragFrom is not { } from || crop is not { } c) return;
        var view = Fit(); var sc = CropOnScreen(view, c);
        float freeX = view.Width - sc.Width, freeY = view.Height - sc.Height;
        float x = freeX < 1 ? 0.5f : Math.Clamp(dragStart.X + (e.X - from.X) / freeX, 0, 1);
        float y = freeY < 1 ? 0.5f : Math.Clamp(dragStart.Y + (e.Y - from.Y) / freeY, 0, 1);
        CropMoved?.Invoke(x, y);
    }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragFrom = null; }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Theme.Bg); Theme.Prep(g);
        var stage = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Theme.Fill(g, stage, Theme.S(16f), Color.FromArgb(6, 7, 9));
        var view = Fit();
        if (frame == null)
        {
            Theme.Glyph(g, Glyphs.Video, 26, Theme.Faint, new Rectangle(0, Height / 2 - Theme.S(48), Width, Theme.S(40)));
            Theme.Label(g, "Pick a clip from the library, or drop a video here", Theme.Body, Theme.Muted, new Rectangle(0, Height / 2, Width, Theme.S(24)), TextFormatFlags.HorizontalCenter);
            return;
        }
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
        g.DrawImage(frame, view);
        if (crop is not { } c) return;
        var sc = CropOnScreen(view, c);
        using (var dim = new Region(view)) { dim.Exclude(sc); using var shade = new SolidBrush(Theme.Alpha(Color.Black, 165)); g.FillRegion(shade, dim); }
        using (var thirds = new Pen(Theme.Alpha(Color.White, 50)))
            for (int i = 1; i < 3; i++)
            {
                g.DrawLine(thirds, sc.X + sc.Width * i / 3, sc.Y, sc.X + sc.Width * i / 3, sc.Bottom);
                g.DrawLine(thirds, sc.X, sc.Y + sc.Height * i / 3, sc.Right, sc.Y + sc.Height * i / 3);
            }
        using (var border = new Pen(Color.White, Theme.S(2f))) g.DrawRectangle(border, sc.X, sc.Y, sc.Width, sc.Height);
        float k = Theme.S(14f);
        using var corner = new Pen(Theme.AccentEnd, Theme.S(4f));
        foreach (var (px, py, dx, dy) in new[] { (sc.X, sc.Y, 1, 1), (sc.Right, sc.Y, -1, 1), (sc.X, sc.Bottom, 1, -1), (sc.Right, sc.Bottom, -1, -1) })
        { g.DrawLine(corner, px, py, px + dx * k, py); g.DrawLine(corner, px, py, px, py + dy * k); }
        string label = $"{c.Width}×{c.Height}"; var ls = Theme.Measure(label, Theme.Small);
        var tag = new Rectangle((int)sc.X + Theme.S(8), (int)sc.Y + Theme.S(8), ls.Width + Theme.S(12), ls.Height + Theme.S(6));
        Theme.Fill(g, tag, Theme.S(5f), Theme.Alpha(Color.Black, 170));
        Theme.Label(g, label, Theme.Small, Color.White, tag, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
    protected override void Dispose(bool disposing) { if (disposing) frame?.Dispose(); base.Dispose(disposing); }
}

/// <summary>Ruler, filmstrip and waveform with draggable in/out handles and a playhead.</summary>
sealed class Timeline : Control
{
    double duration, inPoint, outPoint, playhead;
    Bitmap?[] tiles = [];
    float[] peaks = [];
    enum Drag { None, Seek, In, Out }
    Drag drag;
    public event Action<double, bool>? Seek;
    public event Action<double, double>? RangeChanged;
    public Timeline()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
    }
    public double Playhead { get => playhead; set { playhead = value; Invalidate(); } }
    public void Load(double seconds)
    {
        duration = seconds; inPoint = 0; outPoint = seconds; playhead = 0;
        foreach (var t in tiles) t?.Dispose(); tiles = []; peaks = []; Invalidate();
    }
    public void SetRange(double a, double b) { inPoint = a; outPoint = b; Invalidate(); }
    public void SetTile(int index, int count, Bitmap bmp)
    {
        if (tiles.Length != count) { foreach (var t in tiles) t?.Dispose(); tiles = new Bitmap?[count]; }
        tiles[index]?.Dispose(); tiles[index] = bmp; Invalidate();
    }
    public void SetPeaks(float[] values) { peaks = values; Invalidate(); }

    int Pad => Theme.S(16);
    int HeaderW => Theme.S(64);
    public bool Muted { get; set; }
    Rectangle Track => new(Pad + HeaderW, Theme.S(34), Width - Pad * 2 - HeaderW, Height - Theme.S(34) - Theme.S(14));
    float X(double t) => (float)(Track.X + Track.Width * (duration <= 0 ? 0 : t / duration));
    double T(int x) => duration <= 0 ? 0 : Math.Clamp((x - Track.X) / (double)Track.Width, 0, 1) * duration;
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e); if (duration <= 0 || e.Button != MouseButtons.Left) return;
        int grab = Theme.S(10);
        if (Math.Abs(e.X - X(inPoint)) <= grab && e.Y > Track.Y) drag = Drag.In;
        else if (Math.Abs(e.X - X(outPoint)) <= grab && e.Y > Track.Y) drag = Drag.Out;
        else { drag = Drag.Seek; Seek?.Invoke(T(e.X), true); }
        Capture = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (drag == Drag.None)
        {
            int grab = Theme.S(10);
            Cursor = duration > 0 && e.Y > Track.Y && (Math.Abs(e.X - X(inPoint)) <= grab || Math.Abs(e.X - X(outPoint)) <= grab) ? Cursors.SizeWE : Cursors.Hand;
            return;
        }
        double t = T(e.X), min = Math.Min(0.1, duration / 2);
        switch (drag)
        {
            case Drag.Seek: Seek?.Invoke(t, true); break;
            case Drag.In: inPoint = Math.Min(t, outPoint - min); RangeChanged?.Invoke(inPoint, outPoint); Seek?.Invoke(inPoint, true); break;
            case Drag.Out: outPoint = Math.Max(t, inPoint + min); RangeChanged?.Invoke(inPoint, outPoint); Seek?.Invoke(outPoint, true); break;
        }
        Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (drag != Drag.None) Seek?.Invoke(playhead, false); drag = Drag.None; Capture = false; }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Theme.Bg); Theme.Prep(g);
        var panel = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Theme.Fill(g, panel, Theme.S(16f), Theme.Surface); Theme.Stroke(g, panel, Theme.S(16f), Theme.Alpha(Theme.Line, 160));
        var track = Track;
        if (duration <= 0) { Theme.Label(g, "Timeline", Theme.Small, Theme.Faint, track, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); return; }

        // Ruler with ~8 readable labels whatever the clip length.
        double[] steps = [0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600];
        double step = steps.FirstOrDefault(s => duration / s <= 8, 7200);
        using (var tick = new Pen(Theme.Line))
            for (double t = 0; t <= duration + 1e-6; t += step)
            {
                float x = X(t); g.DrawLine(tick, x, Theme.S(20), x, Theme.S(26));
                Theme.Label(g, Theme.Duration(t, step < 1), Theme.Caption, Theme.Faint, new Rectangle((int)x - Theme.S(40), Theme.S(4), Theme.S(80), Theme.S(14)), TextFormatFlags.HorizontalCenter);
            }

        int filmH = (int)(track.Height * 0.58), waveY = track.Y + filmH + Theme.S(6), waveH = track.Bottom - waveY;
        // Track headers, Premiere style.
        foreach (var (name, glyph, ty, th, tint) in new[] { ("V1", Glyphs.Video, track.Y, filmH, Theme.Violet), ("A1", Muted ? Glyphs.Mute : Glyphs.Volume, waveY, waveH, Theme.AccentEnd) })
        {
            var head = new Rectangle(Pad, ty, HeaderW - Theme.S(8), th);
            Theme.Fill(g, head, Theme.S(8f), Theme.Raised);
            Theme.Fill(g, new RectangleF(head.X, head.Y, Theme.S(3), head.Height), Theme.S(1.5f), tint);
            Theme.Label(g, name, Theme.Strong, Theme.Text, new Rectangle(head.X + Theme.S(10), head.Y, Theme.S(26), head.Height));
            Theme.Glyph(g, glyph, 9, Theme.Muted, new Rectangle(head.Right - Theme.S(24), head.Y, Theme.S(20), head.Height));
        }
        var film = new Rectangle(track.X, track.Y, track.Width, filmH);
        using (var clip = Theme.Round(film, Theme.S(8f)))
        {
            Theme.Fill(g, film, Theme.S(8f), Theme.Alpha(Theme.Violet, 60));
            var state = g.Save(); g.SetClip(clip);
            if (tiles.Length > 0)
            {
                float tw = film.Width / (float)tiles.Length;
                for (int i = 0; i < tiles.Length; i++)
                {
                    if (tiles[i] is not { } bmp) continue;
                    float scale = Math.Max(tw / bmp.Width, film.Height / (float)bmp.Height), dw = bmp.Width * scale, dh = bmp.Height * scale;
                    var cell = new RectangleF(film.X + i * tw, film.Y, tw + 1, film.Height);
                    var s2 = g.Save(); g.IntersectClip(cell);
                    g.DrawImage(bmp, cell.X + (tw - dw) / 2, film.Y + (film.Height - dh) / 2, dw, dh);
                    g.Restore(s2);
                }
            }
            using (var bandB = new SolidBrush(Theme.Alpha(Theme.Violet, 200))) g.FillRectangle(bandB, film.X, film.Y, film.Width, Theme.S(4));
            g.Restore(state);
        }
        Theme.Stroke(g, film, Theme.S(8f), Theme.Alpha(Theme.Violet, 170));
        var wave = new Rectangle(track.X, waveY, track.Width, waveH);
        Theme.Fill(g, wave, Theme.S(8f), Theme.Alpha(Theme.AccentEnd, Muted ? 14 : 34));
        Theme.Stroke(g, wave, Theme.S(8f), Theme.Alpha(Theme.AccentEnd, Muted ? 60 : 150));
        if (peaks.Length > 0)
        {
            using var pen = new Pen(Theme.Alpha(Muted ? Theme.Faint : Theme.AccentEnd, 220), 1);
            float mid = wave.Y + wave.Height / 2f;
            for (int x = 0; x < wave.Width; x++)
            {
                int a = x * peaks.Length / wave.Width, b = Math.Max(a + 1, (x + 1) * peaks.Length / wave.Width);
                float v = 0; for (int i = a; i < b && i < peaks.Length; i++) v = Math.Max(v, peaks[i]);
                float hh = Math.Max(1, v * (wave.Height / 2f - Theme.S(3)));
                g.DrawLine(pen, wave.X + x, mid - hh, wave.X + x, mid + hh);
            }
        }

        // Selection: dim outside, accent frame + handles inside.
        float xi = X(inPoint), xo = X(outPoint);
        using (var shade = new SolidBrush(Theme.Alpha(Theme.Bg, 175)))
        {
            g.FillRectangle(shade, track.X, track.Y, xi - track.X, track.Height);
            g.FillRectangle(shade, xo, track.Y, track.Right - xo, track.Height);
        }
        var sel = new RectangleF(xi, track.Y - Theme.S(2), xo - xi, track.Height + Theme.S(4));
        Theme.Stroke(g, sel, Theme.S(8f), Theme.Accent, Theme.S(2.5f));
        foreach (float hx in new[] { xi, xo })
        {
            var handle = new RectangleF(hx - Theme.S(5), track.Y - Theme.S(2), Theme.S(10), track.Height + Theme.S(4));
            using (var b = Theme.AccentBrush(handle)) Theme.Fill(g, handle, Theme.S(4f), b);
            using var grip = new Pen(Theme.Alpha(Color.White, 200), Theme.S(1.5f));
            g.DrawLine(grip, hx, track.Y + track.Height / 2f - Theme.S(8), hx, track.Y + track.Height / 2f + Theme.S(8));
        }

        float px = X(playhead);
        using (var line = new Pen(Color.White, Theme.S(2f))) g.DrawLine(line, px, Theme.S(18), px, track.Bottom + Theme.S(2));
        using (var knob = new SolidBrush(Color.White))
            g.FillPolygon(knob, new[] { new PointF(px - Theme.S(6), Theme.S(14)), new PointF(px + Theme.S(6), Theme.S(14)), new PointF(px, Theme.S(22)) });
    }
    protected override void Dispose(bool disposing) { if (disposing) foreach (var t in tiles) t?.Dispose(); base.Dispose(disposing); }
}
