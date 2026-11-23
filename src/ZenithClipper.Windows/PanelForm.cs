using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ZenithClipper.Windows.Media;
using ZenithClipper.Windows.Ui;

namespace ZenithClipper.Windows;
using Control = System.Windows.Forms.Control;

/// <summary>
/// The overlay summoned by the panel shortcut. It is a separate short-lived process: closing it (Esc, the shortcut
/// again, the close button or a click on the dimmed screen) ends only this window; the engine keeps its replay buffer.
/// Layout: one rail docked to the right edge of the screen, and a settings window that takes the rail's place.
/// </summary>
public sealed class PanelForm : Form
{
    public const string WindowTitle = "ZenithClipper Controls";
    static readonly int[] ClipPresets = [10, 30, 60, 120, 300, 900, 1800, 3600];
    static readonly int[] RailPresets = [15, 30, 60, 120, 300];
    static readonly int[] HistoryPresets = [60, 120, 300, 600, 1800, 3600, 7200];
    static readonly long[] DiskPresets = [1024, 2048, 5120, 10240, 25600, 51200];

    readonly RailHeader header = new();
    readonly Widget railWidget;
    Widget settingsWidget = null!;
    bool settingsBuilt, homeBuilt, initialConnectionScheduled;
    VStack settingsView = null!;
    SettingsNav nav = null!;
    Panel applyBar = null!;
    readonly Banner toast = new();
    readonly Banner settingsToast = new() { Dock = DockStyle.Top };
    readonly PerfSampler perf = new();
    readonly ZLabel footerStats = new("", Theme.Small, Theme.Faint);
    readonly VStack body = new() { Gap = 0, Padding = Padding.Empty, BackColor = Theme.Surface };
    readonly System.Windows.Forms.Timer poll = new() { Interval = 1000 };
    readonly System.Windows.Forms.Timer toastTimer = new() { Interval = 5000 };
    readonly System.Windows.Forms.Timer fadeIn = new() { Interval = 15 };
    const double Dim = 0.5;

    // Home
    ZButton save = null!;
    ZChips lengths = null!;
    BufferMeter meter = null!;
    ZToggle recording = null!;
    ZLabel recordingDetail = null!;
    AudioList audio = null!;
    ZField addApp = null!;
    Banner audioApply = null!;
    RecentList recent = null!;
    ReplayHero hero = null!;
    StatTiles tiles = null!;

    // Settings
    ZButton clipSoundPicker = null!, inputPicker = null!, outputPicker = null!;
    ZToggle captureMicrophone = null!;
    string selectedSound = ClipSaveSound.DefaultId, selectedInput = "", selectedOutput = "";
    ZChips history = null!, disk = null!, defaultLength = null!;
    ZChips resolution = null!, frameRate = null!, encoder = null!, monitor = null!, backend = null!;
    ZSlider quality = null!;
    ZToggle captureAudio = null!, reduceMotion = null!, startWithWindows = null!, recordOnStart = null!, clipSaveSound = null!;
    AccentSwatches accentPicker = null!;
    ZChips tone = null!;
    ZField excludedField = null!;
    HotkeyBox clipKey = null!, panelKey = null!;
    readonly Banner hotkeyWarning = new();
    List<ZCard> settingsPages = [];
    Action<int> showPage = _ => { };
    SettingRow folderRow = null!, encoderRow = null!, resolutionRow = null!;
    ZLabel applyText = null!;
    ZButton apply = null!;

    Settings settings = new();
    Settings? baseline;
    RecorderStatus? status;
    string clipFolder = "";
    bool busy, polling, loadingSettings, offline;

    /// <summary>Opened from the update notification: start updating straight away.</summary>
    internal static bool UpdateOnOpen;
    public PanelForm()
    {
        PanelStartupProbe.Mark("panel_fields_ready");
        // This window is only the dimmed backdrop; the rail and settings are owned windows on top of it.
        Text = WindowTitle; FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual; ShowInTaskbar = false; TopMost = true;
        KeyPreview = true; BackColor = Color.Black; ForeColor = Theme.Text; Font = Theme.Body; Opacity = 0;
        Bounds = Screen.FromPoint(Cursor.Position).Bounds;

        var railHost = new Panel { BackColor = Theme.Surface };
        header.Dock = DockStyle.Top; header.Height = Theme.S(52);
        var footer = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(56), BackColor = Theme.Surface, Padding = new Padding(Theme.S(16), Theme.S(12), Theme.S(12), Theme.S(12)) };
        footer.Paint += (_, e) => Theme.HLine(e.Graphics, 0, footer.Width, 0);
        var footRow = new HRow(32) { Dock = DockStyle.Fill, BackColor = Theme.Surface };
        footRow.Add(footerStats, fill: true);
        footRow.Add(new ZButton("Open folder", ButtonKind.Secondary)).Click += (_, _) => OpenFolder(clipFolder);
        footRow.Add(new ZButton("Open editor", ButtonKind.Secondary)).Click += (_, _) => EngineClient.Editor();
        footer.Controls.Add(footRow);
        body.Dock = DockStyle.Fill; body.AutoScroll = true; body.FillLast = true; Native.DarkScrollbars(body);
        railHost.Controls.Add(body); railHost.Controls.Add(footer); railHost.Controls.Add(header);
        railWidget = new Widget(this, railHost, Theme.S(400), fit: false);

        header.SettingsClicked += () => ShowSettings(true);
        header.CloseClicked += Close;
        toast.ActionClicked += (_, _) => toastAction?.Invoke();
        toast.Margin = new Padding(Theme.S(16), Theme.S(12), Theme.S(16), 0);
        toastTimer.Tick += (_, _) => { toastTimer.Stop(); toast.Dismiss(); settingsToast.Dismiss(); };

        fadeIn.Tick += (_, _) => { Opacity = Math.Min(Dim, Opacity + 0.1); if (Opacity >= Dim) fadeIn.Stop(); };
        poll.Tick += async (_, _) => await PollAsync();
        header.FirstPaint += () =>
        {
            if (!PanelStartupProbe.Enabled && SnapshotDir is null && !initialConnectionScheduled)
            {
                initialConnectionScheduled = true;
                fadeIn.Start();
                BeginInvoke(async () => { if (IsDisposed) return; EnsureHome(); await ConnectAsync(); });
                // Look for a new version at most every 6 hours (or right away when opened from the update notification).
                if (UpdateOnOpen || Updates.Due(6)) BeginInvoke(async () => await CheckForUpdateAsync(quiet: true, install: UpdateOnOpen));
            }
        };
        Shown += (_, _) =>
        {
            if (PanelStartupProbe.Enabled)
            {
                PanelStartupProbe.Mark("shown");
                if (PanelStartupProbe.EagerBaseline) EnsureSettings();
                EnsureHome();
                railWidget.TopMost = false; railWidget.Location = new Point(-32000, -32000);
                railWidget.Show(this); railWidget.Refresh();
                using (var bitmap = new Bitmap(railWidget.ClientSize.Width, railWidget.ClientSize.Height))
                    railWidget.Controls[0].DrawToBitmap(bitmap, new Rectangle(Point.Empty, railWidget.ClientSize));
                PanelStartupProbe.Mark("widgets_painted");
                BeginInvoke(Close);
                return;
            }
            if (SnapshotDir is not null) { TopMost = false; Bounds = new Rectangle(-32000, -32000, 400, 400); BeginInvoke(TakeSnapshots); return; }
            if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
            Bounds = Screen.FromPoint(Cursor.Position).Bounds;
            PlaceWidgets();
            railWidget.Show(this);
            railWidget.Activate();
            // IPC and library work start after the rail header has actually painted.
        };
        PanelStartupProbe.Mark("panel_constructed");
        if (PanelStartupProbe.Enabled) { TopMost = false; Bounds = new Rectangle(-32000, -32000, Width, Height); Opacity = 1; }
        FormClosed += (_, _) => { volumeDebounce.Dispose(); poll.Dispose(); toastTimer.Dispose(); fadeIn.Dispose(); if (homeBuilt) recent.Dispose(); };
    }

    void EnsureHome()
    {
        if (homeBuilt) return;
        save = new("Save last 30s", ButtonKind.Hero);
        lengths = new() { Segmented = true };
        meter = new();
        recording = new();
        recordingDetail = new("", Theme.Small, Theme.Muted) { Align = TextFormatFlags.Right | TextFormatFlags.VerticalCenter };
        audio = new();
        addApp = new("Keep another app out, e.g. Discord"); // the + button beside it is the action
        audioApply = new();
        recent = new(); hero = new(); tiles = new();
        BuildHome();
        PanelStartupProbe.Mark("home_built");
        homeBuilt = true;
        SetReady(false);
    }
    void SetReady(bool ready)
    {
        bool wasReady = header.Ready;
        header.Ready = ready;
        if (!homeBuilt) return;
        save.Enabled = recording.Enabled = lengths.Enabled = audio.Enabled = addApp.Enabled = ready;
        if (!ready) { save.Text = "Connecting…"; recordingDetail.Text = "Connecting to recorder…"; }
        if (ready) { if (!wasReady) UpdateSaveText(); PanelStartupProbe.Mark("functional_ready"); }
    }

    void EnsureSettings()
    {
        if (settingsBuilt) return;
        // Segmented like the Video rows: one row of equal segments instead of wrapping pills.
        history = new() { Segmented = true }; disk = new() { Segmented = true }; defaultLength = new() { Segmented = true };
        resolution = new() { Segmented = true }; frameRate = new() { Segmented = true }; encoder = new() { Segmented = true }; monitor = new() { Segmented = true }; backend = new() { Segmented = true };
        quality = new(10, 40, 20);
        captureAudio = new(); reduceMotion = new(); startWithWindows = new(); recordOnStart = new(); clipSaveSound = new(); accentPicker = new(); tone = new() { Segmented = true };
        excludedField = new("Spotify, Discord");
        clipKey = new(); panelKey = new();
        applyText = new("", Theme.Small, Theme.Muted);
        apply = new("Apply", ButtonKind.Primary);
        settingsView = BuildSettings(out folderRow, out encoderRow, out resolutionRow);
        PanelStartupProbe.Mark("settings_built");
        applyBar = BuildApplyBar();
        var pages = settingsView.Controls.OfType<ZCard>().ToList();
        nav = new SettingsNav(["Replay", "Video", "Audio", "Shortcuts", "Appearance", "Storage"], [Glyphs.Clock, Glyphs.Video, Glyphs.Speakers, Glyphs.Keyboard, Glyphs.Palette, Glyphs.Storage]) { Dock = DockStyle.Left, Width = Theme.S(260) };
        nav.BackClicked += () => ShowSettings(false);
        // The side nav is the only navigation: one section at a time, each nav item summarising its section.
        void JumpTo(int index)
        {
            if (index < 0 || index >= pages.Count) return;
            settingsView.SuspendLayout();
            for (int i = 0; i < pages.Count; i++) pages[i].Visible = i == index;
            settingsView.AutoScrollPosition = Point.Empty;
            settingsView.ResumeLayout(true);
            nav.SetSelected(index);
        }
        settingsPages = pages; showPage = JumpTo;
        nav.Selected += JumpTo;
        JumpTo(0);
        var content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(Theme.S(24), Theme.S(16), Theme.S(16), Theme.S(16)) };
        settingsView.Dock = DockStyle.Fill; settingsView.BackColor = Theme.Surface; settingsView.Padding = new Padding(0, 0, Theme.S(8), Theme.S(16));
        applyBar.Dock = DockStyle.Bottom; applyBar.Visible = false;
        settingsToast.ActionClicked += (_, _) => toastAction?.Invoke();
        content.Controls.Add(settingsView); content.Controls.Add(applyBar); content.Controls.Add(settingsToast);
        var host = new Panel { BackColor = Theme.Surface };
        host.Controls.Add(content); host.Controls.Add(nav);
        settingsWidget = new Widget(this, host, Theme.S(940), fit: false);
        settingsBuilt = true;
        PlaceWidgets();
        if (baseline is not null) LoadSettings(baseline);
    }

    void PlaceWidgets()
    {
        var r = Bounds; int m = Theme.S(12);
        railWidget.Bounds = new Rectangle(r.Right - m - railWidget.Width, r.Y + m, railWidget.Width, r.Height - m * 2);
        if (settingsBuilt) settingsWidget.Bounds = new Rectangle(r.Right - m - settingsWidget.Width, r.Y + m, settingsWidget.Width, Math.Min(r.Height - m * 2, SettingsHeight()));
    }
    /// <summary>Tall enough for the title plus the longest section, so switching tabs never resizes the window
    /// and short sections don't leave a screen of empty space.</summary>
    int SettingsHeight()
    {
        int w = settingsWidget.Width - Theme.S(260) - Theme.S(24 + 16 + 8);
        // Hidden tabs report their rows as hidden too, so show them all for a moment (layout suspended) to measure.
        settingsView.SuspendLayout();
        var shown = settingsPages.Select(p => p.Visible).ToList();
        foreach (var p in settingsPages) p.Visible = true;
        int tallest = settingsPages.Count == 0 ? 0 : settingsPages.Max(p => p is IMeasured m ? m.MeasureHeight(w) : p.Height);
        for (int i = 0; i < settingsPages.Count; i++) settingsPages[i].Visible = shown[i];
        settingsView.ResumeLayout(false);
        return Math.Max(nav.MinimumHeight, Theme.S(16) + tallest + Theme.S(16 + 16) + Theme.S(64) + Theme.S(24));
    }
    void ShowSettings(bool show)
    {
        if (!show && !settingsBuilt) return;
        EnsureSettings();
        if (show)
        {
            if (baseline != null) LoadSettings(baseline);
            PlaceWidgets(); settingsWidget.Show(this); PlaceWidgets(); settingsWidget.Activate(); railWidget.Hide();
        }
        else
        {
            applyBar.Visible = false; railWidget.Show(this); railWidget.Activate(); settingsWidget.Hide();
        }
    }
    internal void WidgetClosed() { }

    /// <summary>Hidden check mode (--snapshot &lt;folder&gt;): builds the rail and every settings tab offscreen with sample
    /// settings, renders each to a PNG and reports layout problems. Never talks to the recorder.</summary>
    internal static string? SnapshotDir;
    void TakeSnapshots()
    {
        var problems = new List<string>();
        void Save(Form w, string name)
        {
            w.Location = new Point(-32000, -32000); if (!w.Visible) w.Show(this); w.PerformLayout(); Application.DoEvents();
            using var bmp = new Bitmap(w.ClientSize.Width, w.ClientSize.Height);
            w.Controls[0].DrawToBitmap(bmp, new Rectangle(Point.Empty, w.ClientSize));
            bmp.Save(Path.Combine(SnapshotDir!, name + ".png"));
        }
        EnsureHome(); EnsureSettings();
        var sample = new Settings(); baseline = settings = sample; LoadSettings(sample);
        // README shots: show the rail as it looks mid-session (recording, an hour of history) instead of "Connecting".
        if (Environment.GetEnvironmentVariable("ZENITHCLIPPER_SNAPSHOT_DEMO") == "1")
        {
            settings = sample with { BufferSeconds = 3600, MaxBufferMiB = 2048 };
            status = new RecorderStatus(true, "Recording", null, "nvenc native", 1271, 612L * 1048576, null, 0);
            offline = false; SetReady(true); header.SetStatus(status, false); meter.Update(status, settings); hero.Update(status, settings, false);
            loadingSettings = true; recording.SetSilently(true); loadingSettings = false;
            foreach (float cpu in new[] { 1.4f, 1.9f, 1.6f, 2.2f, 1.7f, 1.5f, 2.0f, 1.8f, 1.6f, 1.7f }) tiles.SetPerf(cpu, 86L * 1048576);
            tiles.SetToday(0, 0);
        }
        if (Environment.GetEnvironmentVariable("ZENITHCLIPPER_SNAPSHOT_FILM") is { Length: > 0 } filmDir) FilmRail(filmDir);
        railWidget.Size = new Size(railWidget.Width, Theme.S(1200)); Save(railWidget, "rail");
        settingsWidget.Location = new Point(-32000, -32000); settingsWidget.Show(this); settingsWidget.Height = Math.Min(Theme.S(1400), SettingsHeight());
        for (int i = 0; i < settingsPages.Count; i++) { showPage(i); Save(settingsWidget, $"settings-{i}"); }
        // Every segmented control must fit its labels.
        void Walk(Control c) { if (c is ZChips z && !z.LabelsFit()) problems.Add("clipped labels in " + (z.Tag ?? z.Parent?.GetType().Name)); foreach (Control k in c.Controls) Walk(k); }
        Walk(railWidget); Walk(settingsWidget);
        // The nav must switch the visible section.
        showPage(2); if (!settingsPages[2].Visible || settingsPages[0].Visible) problems.Add("nav did not switch to Audio");
        File.WriteAllText(Path.Combine(SnapshotDir!, "report.txt"), problems.Count == 0 ? "OK" : string.Join(Environment.NewLine, problems));
        Close();
    }
    /// <summary>Website capture (ZENITHCLIPPER_SNAPSHOT_FILM=folder): renders the real rail frame by frame at 30 fps while it
    /// records, picks "2m", saves a clip and shows the real success toast. Uses sample status only; never talks to the recorder.</summary>
    void FilmRail(string dir)
    {
        Directory.CreateDirectory(dir);
        railWidget.Location = new Point(-32000, -32000); if (!railWidget.Visible) railWidget.Show(this);
        railWidget.Size = new Size(railWidget.Width, Theme.S(1200)); railWidget.PerformLayout(); Application.DoEvents();
        int n = 0; double buffered = 1271; var rnd = new Random(7);
        void Frame(int hold = 1)
        {
            status = status! with { BufferedSeconds = buffered, BufferBytes = (long)(buffered * 0.48 * 1048576) };
            header.SetStatus(status, false); meter.Update(status, settings); hero.Update(status, settings, false);
            Application.DoEvents();
            using var bmp = new Bitmap(railWidget.ClientSize.Width, railWidget.ClientSize.Height);
            railWidget.Controls[0].DrawToBitmap(bmp, new Rectangle(Point.Empty, railWidget.ClientSize));
            for (int i = 0; i < hold; i++) { bmp.Save(Path.Combine(dir, $"f{++n:D5}.png")); buffered += 1 / 30.0; }
            if (n % 10 == 0) tiles.SetPerf(1.2f + (float)rnd.NextDouble() * .9f, 86L * 1048576);
        }
        loadingSettings = true; SelectPreset(lengths, 15); loadingSettings = false; UpdateSaveText();
        for (int i = 0; i < 60; i++) Frame();                              // recording, buffer ticking
        foreach (int v in new[] { 30, 60, 120 }) { loadingSettings = true; SelectPreset(lengths, v); loadingSettings = false; UpdateSaveText(); for (int i = 0; i < 14; i++) Frame(); }
        for (int i = 0; i < 20; i++) Frame();
        save.Text = "Saving…"; for (int i = 0; i < 7; i++) Frame();      // ~211 ms median save
        UpdateSaveText(); tiles.SetToday(1, 58L * 1048576);
        ShowToast("Saved 2m clip  ·  Clip-2026-11-17-21-42-08-3f9a1c.mp4", Banner.Tone.Success, "Edit", () => { });
        for (int i = 0; i < 75; i++) Frame();
        File.WriteAllText(Path.Combine(dir, "frames.txt"), n.ToString());
    }
    /// <summary>Dialogs belong to the window you are looking at, so they open on top of it instead of under the dimmed backdrop.</summary>
    IWin32Window DialogOwner => settingsBuilt && settingsWidget.Visible ? settingsWidget : railWidget;

    protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; } } // tool window: no Alt+Tab entry
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Close(); } // click on the dimmed screen closes
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) { HandleEscape(); e.Handled = true; }
    }
    internal void HandleEscape() { if (settingsBuilt && settingsWidget.Visible) ShowSettings(false); else Close(); }

    // ───────────────────────────── Home ─────────────────────────────
    void BuildHome()
    {
        body.Add(toast);

        var replay = body.Add(new Section());
        var top = replay.Add(new HRow(24));
        top.Add(new ZLabel("Instant replay", Theme.Strong), fill: true);
        top.Add(new ZLabel("Record", Theme.Small, Theme.Muted) { Width = Theme.Measure("Record", Theme.Small).Width + Theme.S(4), Align = TextFormatFlags.Right | TextFormatFlags.VerticalCenter });
        top.Add(recording);
        replay.Add(hero);
        recording.CheckedChanged += async (_, _) => { if (!loadingSettings) await Guard(ToggleRecordingAsync); };
        replay.Add(meter);
        foreach (int s in RailPresets) lengths.Add(Theme.Span(s), s);
        lengths.Add("Custom", -1);
        lengths.SelectionChanged += async (_, _) => await Guard(ChangeLengthAsync);
        replay.Add(lengths);
        replay.Add(save);
        save.Click += async (_, _) => await Guard(SaveAsync);

        // At a glance: today's clips and what the recorder costs.
        var glance = body.Add(new Section { Padding = new Padding(Theme.S(16), Theme.S(4), Theme.S(16), Theme.S(16)) });
        glance.Add(tiles);

        var audioSection = body.Add(new Section());
        var audioTop = audioSection.Add(new HRow(24));
        audioTop.Add(new ZLabel("Clip audio", Theme.Strong), fill: true);
        audioTop.Add(new ZLabel("Only changes what clips hear", Theme.Small, Theme.Faint) { Width = Theme.S(190), Align = TextFormatFlags.Right | TextFormatFlags.VerticalCenter });
        audioSection.Add(audio);
        audio.Changed += (_, _) => UpdateAudioApply();
        audio.VolumeChanged += () => { volumeDebounce.Stop(); volumeDebounce.Start(); };
        volumeDebounce.Tick += async (_, _) => { volumeDebounce.Stop(); await Guard(ApplyVolumesAsync); };
        var addRow = audioSection.Add(new HRow(32));
        addRow.Add(addApp, fill: true);
        var add = addRow.Add(new ZButton("", ButtonKind.Secondary, Glyphs.Add) { IconOnly = true, AutoWidth = false, Width = Theme.S(32) });
        add.Click += (_, _) => AddApp();
        addApp.Box.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; AddApp(); } };
        audioSection.Add(audioApply);
        audioApply.ActionClicked += async (_, _) => await Guard(ApplyAudioAsync);

        var recentSection = body.Add(new Section { Divider = false, FillLast = true, Padding = new Padding(Theme.S(8), Theme.S(14), Theme.S(8), Theme.S(8)) });
        var recentTop = recentSection.Add(new HRow(24) { Margin = new Padding(Theme.S(8), 0, Theme.S(8), 0) });
        recentTop.Add(new ZLabel("Recent clips", Theme.Strong), fill: true);
        recent.Counted += (today, todayBytes, total, totalBytes) => { tiles.SetToday(today, todayBytes); footerStats.Text = total == 0 ? "No clips saved yet" : $"{total} clips · {Theme.Bytes(totalBytes)}"; };
        recentTop.Add(new ZButton("View all", ButtonKind.Ghost) { Height = Theme.S(24) }).Click += (_, _) => OpenFolder(clipFolder);
        recentSection.Add(recent);
        recent.Open += path => EngineClient.Editor(path);
        recent.Reveal += path => { try { Process.Start("explorer.exe", $"/select,\"{path}\""); } catch (System.ComponentModel.Win32Exception ex) { ShowToast(ex.Message, Banner.Tone.Error); } };
    }

    async Task SaveAsync()
    {
        if (!header.Ready || baseline is null || status is null) return;
        int seconds = lengths.SelectedValue is int s && s > 0 ? s : settings.ClipSeconds;
        save.Enabled = false; save.Text = "Saving…";
        try
        {
            var response = await EngineClient.SendAsync(new("save", seconds));
            string path = response.Path ?? "";
            ShowToast($"Saved {Theme.Span(seconds)} clip  ·  {Path.GetFileName(path)}", Banner.Tone.Success, "Edit", () => EngineClient.Editor(path));
        }
        finally { save.Enabled = true; UpdateSaveText(); }
    }

    async Task ChangeLengthAsync()
    {
        if (loadingSettings || lengths.SelectedValue is not int seconds) return;
        if (seconds < 0)
        {
            string? text = PromptForm.Ask(DialogOwner, "Custom clip length", "Clip length in seconds (or 5m, 1h):", Theme.Span(settings.ClipSeconds));
            if (text == null || ParseSpan(text) is not int custom) { SelectPreset(lengths, settings.ClipSeconds); return; }
            seconds = custom; SelectPreset(lengths, seconds);
        }
        var next = settings with { ClipSeconds = seconds, BufferSeconds = Math.Max(settings.BufferSeconds, seconds) };
        bool extended = next.BufferSeconds != settings.BufferSeconds;
        await ConfigureAsync(next);
        UpdateSaveText(); meter.Invalidate();
        if (extended) ShowToast($"Replay history extended to {Theme.Span(next.BufferSeconds)} so a {Theme.Span(seconds)} clip fits. Older footage fills in as you play.", Banner.Tone.Info);
    }

    async Task ToggleRecordingAsync()
    {
        recording.Enabled = false;
        try { await EngineClient.SendAsync(new(recording.Checked ? "resume" : "pause")); await PollAsync(); }
        catch { recording.SetSilently(!recording.Checked); throw; }
        finally { recording.Enabled = true; }
    }

    void AddApp()
    {
        string name = addApp.Text.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (name.Length == 0) return;
        audio.AddExcluded(name); addApp.Text = ""; UpdateAudioApply();
    }
    void UpdateAudioApply()
    {
        var pending = audio.Excluded;
        bool changed = !pending.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(settings.ExcludedApps.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        if (changed) audioApply.Show("Applying audio changes starts a fresh replay buffer.", Banner.Tone.Warning, "Apply");
        else audioApply.Dismiss();
    }
    readonly System.Windows.Forms.Timer volumeDebounce = new() { Interval = 250 };
    /// <summary>Per-app levels apply live (no fresh buffer), so they save as soon as the slider settles.</summary>
    async Task ApplyVolumesAsync()
    {
        var volumes = new Dictionary<string, int>(audio.Volumes, StringComparer.OrdinalIgnoreCase);
        if (volumes.Count == settings.AppVolumes.Count && volumes.All(v => settings.AppVolumes.TryGetValue(v.Key, out int old) && old == v.Value)) return;
        var response = await EngineClient.SendAsync(new("configure", Settings: settings with { AppVolumes = volumes }));
        settings = baseline = response.Settings ?? settings with { AppVolumes = volumes };
        // An older recorder ignores levels it does not know about; say so instead of pretending.
        if (response.Settings is { } saved && volumes.Count > 0 && saved.AppVolumes.Count == 0)
            ShowToast("Volume levels need the updated recorder. Run scripts\\package-windows.ps1 once to rebuild it.", Banner.Tone.Warning);
    }
    async Task ApplyAudioAsync()
    {
        await ConfigureAsync(settings with { ExcludedApps = [.. audio.Excluded] });
        audioApply.Dismiss(); ShowToast("Audio rules updated. Excluded apps stay out of new footage.", Banner.Tone.Success);
    }

    // ─────────────────────────── Settings ───────────────────────────
    VStack BuildSettings(out SettingRow folder, out SettingRow encoderInfo, out SettingRow resolutionInfo)
    {
        history.Tag = "history";
        var view = new VStack { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(0, 0, Theme.S(6), Theme.S(70)), Gap = Theme.S(14) };
        Native.DarkScrollbars(view);

        var replayCard = view.Add(new AutoCard("Replay", Glyphs.Clock) { Subtitle = "How far back clips can reach and how much disk that may use" });
        foreach (int s in HistoryPresets) history.Add(Theme.Span(s), s);
        history.Add("Custom", -1);
        replayCard.Add(new SettingRow("Replay history", "How far back a clip can reach. Stored compressed on disk and trimmed automatically.", history, 380));
        foreach (long mb in DiskPresets) disk.Add(Theme.Bytes(mb * 1048576.0), mb);
        replayCard.Add(new SettingRow("Disk limit", "History stops growing past this size, even if the time above is longer.", disk, 380));
        foreach (int s in ClipPresets) defaultLength.Add(Theme.Span(s), s);
        replayCard.Add(new SettingRow("Save shortcut length", "How much the save shortcut keeps.", defaultLength, 380));
        replayCard.Add(new SettingRow("Start with Windows", "Runs quietly in the background when you sign in. Open this panel from the shortcut or the Start menu.", startWithWindows, 44));
        replayCard.Add(new SettingRow("Record on startup", "Starts recording as soon as ZenithClipper starts. Turn off to start it yourself with the Record switch.", recordOnStart, 44));
        history.SelectionChanged += (_, _) => { if (!loadingSettings && history.SelectedValue is -1) PickCustomHistory(); Changed(); };
        disk.SelectionChanged += (_, _) => Changed();
        defaultLength.SelectionChanged += (_, _) => Changed();
        recordOnStart.CheckedChanged += (_, _) => Changed();
        startWithWindows.CheckedChanged += (_, _) =>
        {
            if (loadingSettings) return;
            try { Startup.Set(startWithWindows.Checked); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { ShowToast("Could not change startup: " + ex.Message, Banner.Tone.Error); startWithWindows.SetSilently(Startup.Enabled); }
        };

        var videoCard = view.Add(new AutoCard("Video", Glyphs.Video) { Subtitle = "Picture quality, the display that's recorded and the encoder" });
        resolution.Add("Native", 0).Add("1440p", 2560).Add("1080p", 1920).Add("720p", 1280);
        videoCard.Add(resolutionInfo = new SettingRow("Resolution", "Native keeps every pixel of your display.", resolution, 380));
        frameRate.Add("Native", 0).Add("144", 144).Add("120", 120).Add("60", 60).Add("30", 30);
        videoCard.Add(new SettingRow("Frame rate", "Native follows your display refresh rate.", frameRate, 380));
        quality.Format = q => (q <= 16 ? "Max " : q <= 21 ? "High " : q <= 26 ? "Mid " : q <= 31 ? "Lean " : "Small ") + Math.Round(q).ToString(System.Globalization.CultureInfo.InvariantCulture);
        videoCard.Add(new SettingRow("Quality", "Lower numbers look better and take more space.", quality, 380));
        encoder.Add("Auto", "auto").Add("NVIDIA", "nvenc").Add("AMD", "amf").Add("Intel", "qsv").Add("CPU", "software");
        videoCard.Add(encoderInfo = new SettingRow("Encoder", "Auto picks your GPU's hardware encoder.", encoder, 380));
        int screens = Math.Clamp(Screen.AllScreens.Length, 1, 4);
        for (int i = 0; i < screens; i++) monitor.Add($"Display {i + 1}", i);
        videoCard.Add(new SettingRow("Display", "Which monitor is recorded.", monitor, 380));
        backend.Add("Auto", "auto").Add("Compatibility", "desktop-duplication");
        videoCard.Add(new SettingRow("Capture method", "Try Compatibility if clips come out black or stutter.", backend, 380));
        quality.ValueChanged += (_, _) => Changed();
        foreach (var chips in new[] { resolution, frameRate, encoder, monitor, backend }) chips.SelectionChanged += (_, _) => Changed();

        var audioCard = view.Add(new AutoCard("Audio", Glyphs.Speakers) { Subtitle = "What your clips hear" });
        audioCard.Add(new SettingRow("Record app audio", "One mixed track of everything you hear, minus excluded apps.", captureAudio, 44));
        captureMicrophone = new();
        inputPicker = AudioChoiceButton("Input device");
        outputPicker = AudioChoiceButton("Output device");
        clipSoundPicker = AudioChoiceButton("Default clip save sound");
        audioCard.Add(new SettingRow("Include microphone", "Mix your voice into newly recorded clips.", captureMicrophone, 44));
        audioCard.Add(new SettingRow("Input device", "Choose which microphone is recorded.", inputPicker, 220));
        audioCard.Add(new SettingRow("Output device", "Choose the Windows output used for app audio capture.", outputPicker, 220));
        audioCard.Add(new SettingRow("Clip save sound", "Play a short chime after a successful save.", clipSaveSound, 44));
        audioCard.Add(new SettingRow("Default clip save sound", "Choose the chime that plays after a save.", clipSoundPicker, 220));
        // Mic and device choices apply the moment they change; the recorder swaps its audio mixer without losing the buffer.
        captureMicrophone.CheckedChanged += async (_, _) => { if (!loadingSettings) await Guard(ApplyLiveAudioAsync); };
        clipSaveSound.CheckedChanged += (_, _) => Changed();
        inputPicker.Click += (_, _) => ShowDeviceChoices(inputPicker, true);
        outputPicker.Click += (_, _) => ShowDeviceChoices(outputPicker, false);
        clipSoundPicker.Click += (_, _) => ShowAudioChoices(clipSoundPicker, selectedSound,
            ClipSaveSound.SoundIds.Select(id => new WindowsEndpoints.Device(id, ClipSaveSound.DisplayName(id))),
            option => { selectedSound = option.Id; clipSoundPicker.Text = option.Name; Changed(); });
        audioCard.Add(new SettingRow("Excluded apps", "Process names, comma separated. Easier from the home screen while the app is playing.", excludedField, 300));
        captureAudio.CheckedChanged += (_, _) => Changed();
        excludedField.Box.TextChanged += (_, _) => Changed();

        var keysCard = view.Add(new AutoCard("Shortcuts", Glyphs.Keyboard) { Subtitle = "Keys that work even in fullscreen games" });
        keysCard.Add(new SettingRow("Save clip", "Works in games and fullscreen apps.", clipKey, 260));
        keysCard.Add(new SettingRow("Open or close this panel", null, panelKey, 260));
        keysCard.Add(hotkeyWarning);
        clipKey.ValueChanged += (_, _) => Changed(); panelKey.ValueChanged += (_, _) => Changed();

        var lookCard = view.Add(new AutoCard("Appearance", Glyphs.Palette) { Subtitle = "Colours for this panel and the editor. Changes show right away." });
        lookCard.Add(new SettingRow("Accent colour", "Buttons, highlights, the playhead and the logo.", accentPicker, 380));
        foreach (var t in Appearance.Tones) tone.Add(t, t);
        lookCard.Add(new SettingRow("Background", "Graphite is neutral, Midnight leans blue, Slate is lighter, Black suits OLED screens.", tone, 380));
        lookCard.Add(new SettingRow("Reduce motion", "Turns off button and panel animations in the editor.", reduceMotion, 44));
        accentPicker.Picked += hex => ApplyLook(Theme.Look with { Accent = hex });
        tone.SelectionChanged += (_, _) => { if (!loadingSettings && tone.SelectedValue is string t) ApplyLook(Theme.Look with { Tone = t }); };
        reduceMotion.CheckedChanged += (_, _) => { if (!loadingSettings) ApplyLook(Theme.Look with { ReduceMotion = reduceMotion.Checked }); };
        accentPicker.SetSelected(Theme.Look.Accent); tone.Select(Theme.Look.Tone); reduceMotion.SetSilently(Theme.Look.ReduceMotion);

        var storage = view.Add(new AutoCard("Storage and app", Glyphs.Storage) { Subtitle = "Where clips are saved, diagnostics and quitting" });
        var folderButtons = new HRow(36) { Width = Theme.S(220) };
        folderButtons.Add(new ZButton("Change…", ButtonKind.Secondary, Glyphs.Folder) { Tag = "right" }).Click += (_, _) => PickFolder();
        folderButtons.Add(new ZButton("Open", ButtonKind.Ghost)).Click += (_, _) => OpenFolder(clipFolder);
        storage.Add(folder = new SettingRow("Clip folder", "", folderButtons, 220));
        var logButton = new ZButton("Open log", ButtonKind.Secondary, Glyphs.Open) { AutoWidth = false, Width = Theme.S(140) };
        logButton.Click += (_, _) => { string log = Path.Combine(AppPaths.Root, "engine.log"); if (File.Exists(log)) Shell(log); else ShowToast("No engine log yet. That usually means nothing has gone wrong.", Banner.Tone.Info); };
        storage.Add(new SettingRow("Diagnostics", "Capture, audio and shortcut problems are written to engine.log.", logButton, 140));
        var updateButton = new ZButton("Check for updates", ButtonKind.Secondary, Glyphs.Refresh) { AutoWidth = false, Width = Theme.S(190) };
        updateButton.Click += async (_, _) => await CheckForUpdateAsync(quiet: false, install: false);
        storage.Add(new SettingRow("Updates", $"You have version {Updates.Current.ToString(3)}. New versions come from GitHub releases.", updateButton, 190));
        var helpButton = new ZButton("Join the Discord") { AutoWidth = false, Width = Theme.S(190) };
        helpButton.Click += (_, _) => Shell(HelpUrl);
        storage.Add(new SettingRow("Help", "Questions, bugs or ideas? Ask in the support Discord.", helpButton, 190));
        var quit = new ZButton("Quit", ButtonKind.Danger, Glyphs.Power) { AutoWidth = false, Width = Theme.S(140) };
        quit.Click += async (_, _) => await Guard(async () => { await EngineClient.SendAsync(new("shutdown")); Close(); });
        storage.Add(new SettingRow("Quit ZenithClipper", "Stops background recording until you launch it again.", quit, 140));
        return view;
    }

    Panel BuildApplyBar()
    {
        var bar = new ApplyBar { Dock = DockStyle.Bottom, Height = Theme.S(64) };
        var row = new HRow(40) { Dock = DockStyle.Fill };
        bar.Padding = new Padding(Theme.S(16), Theme.S(12), Theme.S(12), Theme.S(12));
        row.Add(applyText, fill: true);
        var revert = row.Add(new ZButton("Revert", ButtonKind.Ghost));
        row.Add(apply);
        revert.Click += (_, _) => { if (baseline != null) LoadSettings(baseline); };
        apply.Click += async (_, _) => await Guard(ApplySettingsAsync);
        bar.Controls.Add(row);
        return bar;
    }

    static ZButton AudioChoiceButton(string label) => new("System default", ButtonKind.Secondary)
    {
        AccessibleName = label,
        AutoWidth = false
    };

    static string DeviceLabel(bool microphone, string id)
    {
        if (id.Length == 0) return "System default";
        try { return WindowsEndpoints.List(microphone).FirstOrDefault(device => device.Id == id)?.Name ?? "Disconnected device"; }
        catch (COMException) { return "Device unavailable"; }
    }

    void ShowDeviceChoices(ZButton button, bool microphone)
    {
        try
        {
            WindowsEndpoints.Device[] choices = [new("", "System default"), .. WindowsEndpoints.List(microphone)];
            ShowAudioChoices(button, microphone ? selectedInput : selectedOutput, choices, option =>
            {
                if (microphone) selectedInput = option.Id;
                else selectedOutput = option.Id;
                button.Text = option.Name;
                _ = Guard(ApplyLiveAudioAsync);
            });
        }
        catch (COMException ex) { ShowToast("Could not list Windows audio devices. " + ex.Message, Banner.Tone.Error); }
    }

    static void ShowAudioChoices(ZButton button, string current, IEnumerable<WindowsEndpoints.Device> choices, Action<WindowsEndpoints.Device> select)
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new EditorForm.DarkMenuRenderer(),
            BackColor = Theme.Raised,
            ForeColor = Theme.Text,
            Font = Theme.Body,
            ShowCheckMargin = true,
            ShowImageMargin = false
        };
        foreach (var option in choices)
        {
            var item = new ToolStripMenuItem(option.Name) { Checked = option.Id == current };
            item.Click += (_, _) => select(option);
            menu.Items.Add(item);
        }
        menu.Closed += (_, _) => menu.Dispose();
        menu.Show(button, new Point(0, button.Height));
    }

    Settings BuildFromControls()
    {
        EnsureSettings();
        var s = baseline ?? settings;
        return s with
        {
            BufferSeconds = history.SelectedValue is int h && h > 0 ? h : s.BufferSeconds,
            MaxBufferMiB = disk.SelectedValue is long d ? d : s.MaxBufferMiB,
            ClipSeconds = defaultLength.SelectedValue is int c && c > 0 ? c : s.ClipSeconds,
            OutputWidth = resolution.SelectedValue is int w ? w : s.OutputWidth,
            Fps = frameRate.SelectedValue is int f ? f : s.Fps,
            Quality = (int)Math.Round(quality.Value),
            Encoder = encoder.SelectedValue as string ?? s.Encoder,
            Monitor = monitor.SelectedValue is int m ? m : s.Monitor,
            WindowsCaptureBackend = backend.SelectedValue as string ?? s.WindowsCaptureBackend,
            CaptureAudio = captureAudio.Checked,
            AutoStartCapture = recordOnStart.Checked,
            ClipSaveSound = clipSaveSound.Checked,
            ClipSaveSoundId = selectedSound,
            CaptureMicrophone = captureMicrophone.Checked,
            WindowsInputDevice = selectedInput,
            WindowsOutputDevice = selectedOutput,
            ExcludedApps = excludedField.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? x[..^4] : x).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            ClipDirectory = clipFolder.Length > 0 ? clipFolder : s.ClipDirectory,
            ClipHotkey = clipKey.Value, PanelHotkey = panelKey.Value,
        };
    }

    void LoadSettings(Settings s)
    {
        if (!settingsBuilt) return;
        loadingSettings = true;
        try
        {
            selectedSound = ClipSaveSound.NormalizeId(s.ClipSaveSoundId);
            clipSoundPicker.Text = ClipSaveSound.DisplayName(selectedSound);
            captureMicrophone.SetSilently(s.CaptureMicrophone);
            selectedInput = s.WindowsInputDevice;
            selectedOutput = s.WindowsOutputDevice;
            inputPicker.Text = DeviceLabel(true, selectedInput);
            outputPicker.Text = DeviceLabel(false, selectedOutput);
            SelectPreset(history, s.BufferSeconds); SelectPreset(defaultLength, s.ClipSeconds);
            if (!disk.Select(s.MaxBufferMiB)) { disk.Add(Theme.Bytes(s.MaxBufferMiB * 1048576.0), s.MaxBufferMiB); disk.Select(s.MaxBufferMiB); }
            if (!resolution.Select(s.OutputWidth)) { resolution.Add($"{s.OutputWidth}w", s.OutputWidth); resolution.Select(s.OutputWidth); }
            if (!frameRate.Select(s.Fps)) { frameRate.Add($"{s.Fps}", s.Fps); frameRate.Select(s.Fps); }
            quality.Value = Math.Clamp(s.Quality, 10, 40);
            encoder.Select(s.Encoder); monitor.Select(Math.Min(s.Monitor, Math.Max(0, Screen.AllScreens.Length - 1))); backend.Select(s.WindowsCaptureBackend);
            captureAudio.SetSilently(s.CaptureAudio); recordOnStart.SetSilently(s.AutoStartCapture); clipSaveSound.SetSilently(s.ClipSaveSound); startWithWindows.SetSilently(Startup.Enabled); excludedField.Text = string.Join(", ", s.ExcludedApps);
            clipKey.Value = s.ClipHotkey; panelKey.Value = s.PanelHotkey; clipFolder = s.ClipDirectory;
            folderRow.Detail = s.ClipDirectory;
            var primary = Screen.AllScreens.ElementAtOrDefault(s.Monitor) ?? Screen.PrimaryScreen;
            resolutionRow.Detail = primary == null ? "Native keeps every pixel of your display." : $"Native keeps every pixel ({primary.Bounds.Width}×{primary.Bounds.Height}). Lower options downscale on the GPU.";
        }
        finally { loadingSettings = false; }
        UpdateOverview(s);
        Changed();
    }

    static void SelectPreset(ZChips chips, int value)
    {
        if (chips.Select(value)) return;
        chips.Clear();
        int[] presets = ReferenceEquals(chips.Tag, "history") ? HistoryPresets : ClipPresets;
        bool inserted = false;
        foreach (int p in presets)
        {
            if (!inserted && value < p) { chips.Add(Theme.Span(value), value); inserted = true; }
            chips.Add(Theme.Span(p), p);
        }
        if (!inserted) chips.Add(Theme.Span(value), value);
        chips.Add("Custom", -1); chips.Select(value); chips.Parent?.PerformLayout();
    }

    void PickCustomHistory()
    {
        string? text = PromptForm.Ask(DialogOwner, "Custom replay history", "History length (e.g. 45m, 3h, 90s):", Theme.Span((baseline ?? settings).BufferSeconds));
        int current = (baseline ?? settings).BufferSeconds;
        SelectPreset(history, text != null && ParseSpan(text) is int v && v <= 86400 ? v : current);
    }

    static int? ParseSpan(string text)
    {
        text = text.Trim().ToLowerInvariant().Replace(" ", "", StringComparison.Ordinal);
        int total = 0; string number = "";
        foreach (char ch in text)
        {
            if (char.IsAsciiDigit(ch)) { number += ch; continue; }
            if (number.Length == 0 || !int.TryParse(number, out int n)) return null;
            total += ch switch { 'h' => n * 3600, 'm' => n * 60, 's' => n, _ => -1_000_000 }; number = "";
        }
        if (number.Length > 0) { if (!int.TryParse(number, out int n)) return null; total += n; }
        return total is >= 1 and <= 86400 ? total : null;
    }

    void Changed()
    {
        if (!settingsBuilt || loadingSettings || baseline == null) return;
        Settings next;
        try { next = BuildFromControls(); next.Validate(); }
        catch (ArgumentException ex) { applyBar.Visible = true; applyText.Text = ex.Message; applyText.ForeColor = Theme.Warn; apply.Enabled = false; return; }
        UpdateOverview(next);
        bool dirty = JsonSerializer.Serialize(next) != JsonSerializer.Serialize(baseline);
        applyBar.Visible = dirty; apply.Enabled = true; applyText.ForeColor = Theme.Muted;
        bool restart = SettingsChanges.RequiresRestart(baseline, next);
        applyText.Text = restart ? "These changes restart capture with a fresh replay buffer." : "Applies instantly. Your replay buffer is kept.";
        apply.Text = restart ? "Apply and restart" : "Apply";
        encoderRow.Detail = next.Encoder == "software" ? "CPU encoding works everywhere but costs noticeably more CPU and can affect games." :
            status?.Encoder is { } active ? $"Auto picks your GPU's hardware encoder. Currently using {active.ToUpperInvariant()}." : "Auto picks your GPU's hardware encoder.";
    }

    void UpdateOverview(Settings s)
    {
        if (!settingsBuilt) return;
        var screen = Screen.AllScreens.ElementAtOrDefault(s.Monitor) ?? Screen.PrimaryScreen;
        string res = s.OutputWidth > 0 ? (s.OutputWidth switch { 2560 => "1440p", 1920 => "1080p", 1280 => "720p", _ => $"{s.OutputWidth}px" }) : screen is null ? "Native" : $"Native {screen.Bounds.Height}p";
        string enc = s.Encoder switch { "nvenc" => "NVIDIA", "amf" => "AMD", "qsv" => "Intel", "software" => "CPU", _ => status?.Encoder?.ToUpperInvariant() ?? "Auto" };
        nav.SetDetail(0, $"{Theme.Span(s.BufferSeconds)} history, up to {Theme.Bytes(s.MaxBufferMiB * 1048576.0)}");
        nav.SetDetail(1, $"{res} · {(s.Fps > 0 ? s.Fps + " fps" : "display fps")} · {enc}");
        nav.SetDetail(2, !s.CaptureAudio ? "App audio off" : s.ExcludedApps.Length == 0 ? "Everything you hear" : $"{s.ExcludedApps.Length} app{(s.ExcludedApps.Length == 1 ? "" : "s")} kept out");
        nav.SetDetail(3, $"Save {s.ClipHotkey} · Panel {s.PanelHotkey}");
        nav.SetDetail(4, LookSummary(Theme.Look));
        nav.SetDetail(5, Path.GetFileName(s.ClipDirectory.TrimEnd('\\', '/')) is { Length: > 0 } f ? $"Clips in \"{f}\"" : s.ClipDirectory);
    }

    /// <summary>Appearance is not a recorder setting: it saves and repaints immediately, and the editor follows the file.</summary>
    void ApplyLook(Appearance look)
    {
        if (look == Theme.Look) return;
        try { look.Save(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowToast("Couldn't save the colours: " + ex.Message, Banner.Tone.Warning); }
        var map = Theme.ApplyAppearance(look);
        foreach (Form form in Application.OpenForms) Theme.Retint(form, map);
        if (settingsBuilt) nav.SetDetail(4, LookSummary(look));
    }
    static string LookSummary(Appearance a) => $"{Appearance.AccentPresets.FirstOrDefault(p => p.Hex.Equals(a.Accent, StringComparison.OrdinalIgnoreCase)).Name ?? "Custom"} accent · {a.Tone}";

    async Task ApplySettingsAsync()
    {
        var next = BuildFromControls(); next.Validate();
        bool restart = baseline != null && SettingsChanges.RequiresRestart(baseline, next);
        await ConfigureAsync(next);
        LoadSettings(next);
        ShowToast(restart ? "Settings applied. Capture restarted with a fresh buffer." : "Settings applied.", Banner.Tone.Success);
    }

    async Task ApplyLiveAudioAsync()
    {
        bool micWas = settings.CaptureMicrophone;
        var next = settings with { CaptureMicrophone = captureMicrophone.Checked, WindowsInputDevice = selectedInput, WindowsOutputDevice = selectedOutput };
        if (next == settings) return;
        await ConfigureAsync(next);
        ShowToast(next.CaptureMicrophone == micWas ? "Audio device updated." : next.CaptureMicrophone ? "Microphone is now recorded into your clips." : "Microphone is no longer recorded.", Banner.Tone.Success);
        Changed();
    }

    async Task ConfigureAsync(Settings next)
    {
        next.Validate();
        var response = await EngineClient.SendAsync(new("configure", Settings: next));
        settings = baseline = response.Settings ?? next;
        LoadHome();
        await PollAsync();
    }

    void PickFolder()
    {
        using var dialog = new FolderBrowserDialog { SelectedPath = clipFolder, ShowNewFolderButton = true, Description = "Where should clips be saved?", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(DialogOwner) != DialogResult.OK) return;
        clipFolder = dialog.SelectedPath; folderRow.Detail = clipFolder; Changed();
    }

    // ─────────────────────────── Engine link ───────────────────────────
    async Task ConnectAsync()
    {
        try
        {
            await EngineClient.EnsureAsync();
            var response = await EngineClient.SendAsync(new("settings"));
            settings = baseline = response.Settings!;
            LoadHome(); LoadSettings(settings);
            offline = false;
            await PollAsync(); poll.Start();
            _ = RefreshAudioAsync();
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            offline = true; SetReady(false); header.SetStatus(null, true); if (homeBuilt) hero.Update(null, settings, true);
            ShowToast("The recorder isn't running. " + ex.Message, Banner.Tone.Error, "Start", async () => await ConnectAsync());
        }
    }

    void LoadHome()
    {
        EnsureHome();
        loadingSettings = true;
        try { SelectPreset(lengths, settings.ClipSeconds); }
        finally { loadingSettings = false; }
        UpdateSaveText();
        audio.SetRules(settings.ExcludedApps); audio.SetVolumes(settings.AppVolumes);
        UpdateAudioApply();
        clipFolder = settings.ClipDirectory;
        meter.BufferTarget = settings.BufferSeconds;
    }

    void UpdateSaveText()
    {
        int s = lengths.SelectedValue is int v && v > 0 ? v : settings.ClipSeconds;
        save.Text = $"Save last {Theme.Span(s)}"; save.Hint = settings.ClipHotkey; meter.ClipSeconds = s; meter.Invalidate();
        recent.Hotkey = settings.ClipHotkey; recent.ClipSeconds = s; recent.Invalidate();
        lengths.IsDim = value => value is int x && x > 0 && status != null && x > status.BufferedSeconds + 1;
    }

    int audioTick;
    string? lastError;
    /// <summary>FFmpeg errors are long logs; show what it means and keep the raw text in engine.log.</summary>
    static string Friendly(string error)
    {
        if (error.Contains("Could not open encoder", StringComparison.OrdinalIgnoreCase) || error.Contains("SubmitInput", StringComparison.OrdinalIgnoreCase))
            return "Capture couldn't start: the GPU encoder refused the stream. Try Retry, or in Settings switch Capture method to Compatibility or pick another encoder.";
        if (error.Contains("hardware encoder", StringComparison.OrdinalIgnoreCase)) return error;
        string line = error.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault(l => !l.StartsWith('[')) ?? error.Split('\n')[0];
        return "Capture stopped: " + (line.Length > 160 ? line[..160] + "…" : line);
    }
    async Task PollAsync()
    {
        if (polling) return; polling = true;
        try
        {
            var response = await EngineClient.SendAsync(new("status"));
            status = response.Status; offline = false; SetReady(baseline is not null && status is not null);
            header.SetStatus(status, false);
            meter.Update(status!, settings); hero.Update(status, settings, false);
            loadingSettings = true;
            try { recording.SetSilently(status!.Capturing || status.State == "Starting"); }
            finally { loadingSettings = false; }
            recordingDetail.Text = status!.Capturing
                ? $"{status.Encoder?.ToUpperInvariant() ?? "GPU"}  ·  {(settings.OutputWidth == 0 ? "native resolution" : settings.OutputWidth + "px wide")}  ·  {(settings.Fps == 0 ? "display refresh" : settings.Fps + " fps")}"
                : status.State == "Starting" ? "Starting capture…" : "Paused. Nothing is being recorded.";
            string? problem = status.Error ?? status.Notice;
            bool hotkeyProblem = problem != null && problem.Contains("hotkey", StringComparison.OrdinalIgnoreCase);
            if (hotkeyProblem) hotkeyWarning.Show(problem!, Banner.Tone.Warning); else hotkeyWarning.Dismiss();
            if (settingsBuilt) nav.SetWarning("Shortcuts", hotkeyProblem);
            if (status.Error != null && status.Error != lastError)
                ShowToast(Friendly(status.Error), Banner.Tone.Error, "Retry", async () => await Guard(async () => { await EngineClient.SendAsync(new("resume")); await PollAsync(); }));
            lastError = status.Error;
            recent.RefreshClips(settings, status.LastClip);
            lengths.Invalidate();
            if (++audioTick % 3 == 0 && railWidget.Visible) _ = RefreshAudioAsync();
            perf.Sample(status!); tiles.SetPerf(perf.Cpu, perf.Memory);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException)
        {
            if (!offline) { offline = true; SetReady(false); header.SetStatus(null, true); hero.Update(null, settings, true); ShowToast("Lost contact with the recorder.", Banner.Tone.Error, "Reconnect", async () => await ConnectAsync()); }
        }
        finally { polling = false; }
    }

    async Task RefreshAudioAsync()
    {
        try
        {
            string[] names = await Task.Run(() =>
            {
                var found = WindowsSessions.ActiveAppNames().Where(n => !n.StartsWith("ZenithClipper", StringComparison.OrdinalIgnoreCase) && !n.Equals("ffmpeg", StringComparison.OrdinalIgnoreCase)).ToArray();
                AppIcons.Load(found);
                return found;
            });
            audio.SetPlaying(names);
        }
        catch (Exception ex) when (ex is InvalidCastException or System.Runtime.InteropServices.COMException) { }
    }

    // ─────────────────────────── helpers ───────────────────────────
    Action? toastAction;
    void ShowToast(string text, Banner.Tone tone, string? action = null, Action? onAction = null, bool sticky = false)
    {
        toastAction = onAction;
        if (settingsBuilt && settingsWidget.Visible)
        {
            settingsToast.Show(text, tone, action);
            settingsToast.Height = settingsToast.MeasureHeight(Math.Max(Theme.S(200), settingsToast.Parent?.ClientSize.Width ?? Theme.S(600)));
        }
        else { toast.Show(text, tone, action); body.AutoScrollPosition = Point.Empty; }
        toastTimer.Stop(); if (sticky) return; toastTimer.Interval = tone == Banner.Tone.Error ? 9000 : 5000; toastTimer.Start();
    }
    UpdateInfo? update; bool updating;
    /// <summary>Asks GitHub for a newer release. Quiet checks only speak up when there is one.</summary>
    async Task CheckForUpdateAsync(bool quiet, bool install)
    {
        if (updating) return;
        if (!quiet) ShowToast("Checking for updates…", Banner.Tone.Info);
        update = await Updates.CheckAsync();
        if (IsDisposed) return;
        if (update is null) { if (!quiet) ShowToast($"You're up to date (version {Updates.Current.ToString(3)}).", Banner.Tone.Success); return; }
        if (install) { await InstallUpdateAsync(); return; }
        ShowToast($"ZenithClipper {update.Version.ToString(3)} is available.", Banner.Tone.Info, Updates.CanSelfUpdate ? "Update now" : "Download", async () => await InstallUpdateAsync(), sticky: true);
    }
    /// <summary>Downloads the newest release for this kind of copy, verifies it and installs it: the installer for
    /// installed copies, an unpack-and-swap for portable ones. Either way the app closes and reopens on the new version.</summary>
    async Task InstallUpdateAsync()
    {
        if (update is null || updating) return;
        if (!Updates.CanSelfUpdate || update.DownloadUrl is null) { Shell(update.PageUrl); return; }
        updating = true;
        try
        {
            int shown = -1;
            var progress = new Progress<double>(f => { int pct = (int)(f * 100); if (pct != shown) { shown = pct; ShowToast($"Downloading ZenithClipper {update.Version.ToString(3)}… {pct}%", Banner.Tone.Info, sticky: true); } });
            string file = await Updates.DownloadAsync(update, progress);
            ShowToast("Installing the update. ZenithClipper will reopen when it's done.", Banner.Tone.Info, sticky: true);
            if (Updates.Flavor == Updates.Kind.WindowsPortable)
            {
                PortableUpdate.Start(file);
                try { await EngineClient.SendAsync(new("shutdown")); } catch (Exception ex) when (ex is IOException or TimeoutException) { }
            }
            else Process.Start(new ProcessStartInfo(file) { UseShellExecute = true, ArgumentList = { "/SILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/relaunch=1" } });
            await Task.Delay(800);
            Application.Exit();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or InvalidOperationException or TaskCanceledException or System.ComponentModel.Win32Exception)
        {
            ShowToast("Update failed: " + ex.Message, Banner.Tone.Error, "Open download page", () => Shell(update.PageUrl), sticky: true);
        }
        finally { updating = false; }
    }

    async Task Guard(Func<Task> action)
    {
        if (busy) return; busy = true;
        try { await action(); }
        catch (Exception ex) when (ex is IOException or ArgumentException or TimeoutException or InvalidOperationException or System.ComponentModel.Win32Exception) { ShowToast(ex.Message, Banner.Tone.Error); }
        finally { busy = false; }
    }
    void OpenFolder(string folder)
    {
        if (folder.Length == 0) return;
        try { Directory.CreateDirectory(folder); Shell(folder); } catch (IOException ex) { ShowToast(ex.Message, Banner.Tone.Error); }
    }
    const string HelpUrl = "https://discord.gg/4ju5pj5HDW";
    static void Shell(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    sealed class ApplyBar : Panel
    {
        public ApplyBar() { BackColor = Theme.Raised; DoubleBuffered = true; ResizeRedraw = true; }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Theme.Bg); Theme.Prep(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Theme.Fill(g, r, Theme.S(14f), Theme.Raised); Theme.Stroke(g, r, Theme.S(14f), Theme.Alpha(Theme.Accent, 120));
        }
    }
}


/// <summary>Borderless owned window that hosts one widget over the dimmed backdrop.</summary>
sealed class Widget : Form
{
    readonly PanelForm owner;
    readonly Control content;
    readonly bool fit;
    public Widget(PanelForm owner, Control content, int width, bool fit = true)
    {
        this.owner = owner; this.content = content; this.fit = fit;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual; KeyPreview = true;
        BackColor = Theme.Surface; Font = Theme.Body; Width = width; Height = Theme.S(200); Text = PanelForm.WindowTitle + " widget";
        if(content is VStack stack)stack.AutoScroll=true;
        content.Dock = DockStyle.Fill; Controls.Add(content);
        if (content is ZCard card) card.CloseRequested += (_, _) => { Hide(); owner.WidgetClosed(); };
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { e.Handled = true; owner.HandleEscape(); } };
    }
    public void Fit()
    {
        if (!Visible || !fit || content is not IMeasured m) return;
        int h = Math.Min(m.MeasureHeight(Width), Screen.FromControl(owner).Bounds.Height * 4 / 5);
        if (Height != h) Height = h;
    }
    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg == 0xF) PanelStartupProbe.Mark("first_widget_paint", once: true);
    }
    protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x80; cp.ClassStyle |= 0x20000; return cp; } }
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if(Visible && content!=null){content.PerformLayout();Fit();}
    }
    protected override bool ShowWithoutActivation => false;
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e); Native.StyleWindow(this);
    }
}


/// <summary>Rail section: stacked content with 16px padding and a divider under it.</summary>
sealed class Section : VStack, IMeasured
{
    public bool Divider { get; set; } = true;
    public Section() { BackColor = Theme.Surface; Gap = Theme.S(12); Padding = new Padding(Theme.S(16), Theme.S(14), Theme.S(16), Theme.S(16)); }
    public int MeasureHeight(int width) => ContentHeight(width);
    protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); if (Divider) Theme.HLine(e.Graphics, 0, Width, Height - 1); }
}

/// <summary>Rail header: orb, name, recorder state, settings and close. Buttons are hit regions.</summary>
sealed class RailHeader : Control
{
    public event Action? FirstPaint, SettingsClicked, CloseClicked;
    bool firstPaint = true;
    RecorderStatus? status; bool offline; string hover = "";
    Rectangle settingsBox, closeBox;
    readonly ToolTip tips = new() { InitialDelay = 400 };
    public bool Ready { get; set; }
    public RailHeader()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
    }
    public void SetStatus(RecorderStatus? value, bool isOffline) { status = value; offline = isOffline; Invalidate(); }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        string h = settingsBox.Contains(e.Location) ? "settings" : closeBox.Contains(e.Location) ? "close" : "";
        if (h != hover) { hover = h; Invalidate(); tips.SetToolTip(this, h == "settings" ? "Settings" : h == "close" ? "Close (Esc). Recording continues." : ""); }
        Cursor = h.Length > 0 ? Cursors.Hand : Cursors.Default;
    }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = ""; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (settingsBox.Contains(e.Location) && Ready) SettingsClicked?.Invoke();
        else if (closeBox.Contains(e.Location)) CloseClicked?.Invoke();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(BackColor); Theme.Prep(g);
        int h = Height, b = Theme.S(32);
        Theme.Orb(g, new RectangleF(Theme.S(16), h / 2f - Theme.S(11), Theme.S(22), Theme.S(22)));
        Theme.Label(g, "ZenithClipper", Theme.Strong, Theme.Text, new Rectangle(Theme.S(46), 0, Theme.S(140), h));
        closeBox = new Rectangle(Width - Theme.S(8) - b, (h - b) / 2, b, b);
        settingsBox = new Rectangle(closeBox.X - b, closeBox.Y, b, b);
        foreach (var (box, id, glyph) in new[] { (settingsBox, "settings", Glyphs.Settings), (closeBox, "close", Glyphs.Close) })
        {
            if (hover == id) Theme.Fill(g, box, Theme.S(6f), Theme.Hover);
            Theme.Glyph(g, glyph, 10, hover == id ? Theme.Text : Theme.Muted, box);
        }
        int sep = settingsBox.X - Theme.S(8);
        using (var pen = new Pen(Theme.Line)) g.DrawLine(pen, sep, h / 2 - Theme.S(10), sep, h / 2 + Theme.S(10));
        string label; Color c;
        if (offline || status == null) { label = offline ? "Offline" : "Connecting"; c = Theme.Faint; }
        else if (status.Error != null) { label = "Error"; c = Theme.Warn; }
        else if (status.Capturing) { label = "Recording"; c = Theme.Live; }
        else if (status.State == "Starting") { label = "Starting"; c = Theme.Muted; }
        else { label = "Paused"; c = Theme.Muted; }
        var size = Theme.Measure(label, Theme.Small);
        int tx = sep - Theme.S(10) - size.Width;
        Theme.Label(g, label, Theme.Small, Theme.Muted, new Rectangle(tx, 0, size.Width + Theme.S(4), h));
        float r = Theme.S(4f);
        using (var dot = new SolidBrush(c)) g.FillEllipse(dot, tx - Theme.S(8) - r, h / 2f - r, r * 2, r * 2);
        Theme.HLine(g, 0, Width, h - 1);
        if (firstPaint) { firstPaint = false; PanelStartupProbe.Mark("first_content_paint", once: true); FirstPaint?.Invoke(); }
    }
    protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }
}

/// <summary>Left-hand section list in the settings window.</summary>
sealed class SettingsNav : Control
{
    readonly string[] items, glyphs, details;
    readonly HashSet<int> warn = [];
    int selected, hover = -2;
    public event Action<int>? Selected;
    public event Action? BackClicked;
    public SettingsNav(string[] items, string[] glyphs)
    {
        this.items = items; this.glyphs = glyphs; details = new string[items.Length];
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Bg; Cursor = Cursors.Hand;
    }
    public void SetSelected(int index) { if (index == selected || index < 0 || index >= items.Length) return; selected = index; Invalidate(); }
    /// <summary>One muted line under the section name saying how it is set up right now.</summary>
    public void SetDetail(int index, string text) { if (details[index] == text) return; details[index] = text; Invalidate(); }
    public void SetWarning(string item, bool on) { int i = Array.IndexOf(items, item); if (i < 0) return; if (on ? warn.Add(i) : warn.Remove(i)) Invalidate(); }
    Rectangle Back => new(Theme.S(8), Theme.S(12), Width - Theme.S(16), Theme.S(36));
    Rectangle Item(int i) => new(Theme.S(8), Theme.S(60) + i * Theme.S(58), Width - Theme.S(16), Theme.S(54));
    public int MinimumHeight => Item(items.Length - 1).Bottom + Theme.S(56);
    int HitTest(Point p) { if (Back.Contains(p)) return -1; for (int i = 0; i < items.Length; i++) if (Item(i).Contains(p)) return i; return -2; }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int h = HitTest(e.Location); if (h != hover) { hover = h; Invalidate(); } }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -2; Invalidate(); }
    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        int h = HitTest(e.Location);
        if (h == -1) BackClicked?.Invoke();
        else if (h >= 0) { selected = h; Invalidate(); Selected?.Invoke(h); }
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(BackColor); Theme.Prep(g);
        var back = Back;
        Theme.Glyph(g, Glyphs.Back, 8.5f, Theme.Muted, new Rectangle(back.X, back.Y, Theme.S(24), back.Height));
        Theme.Label(g, "Back to replay", Theme.Body, hover == -1 ? Theme.Text : Theme.Muted, new Rectangle(back.X + Theme.S(26), back.Y, back.Width, back.Height));
        for (int i = 0; i < items.Length; i++)
        {
            var r = Item(i); bool sel = i == selected;
            if (sel || hover == i) Theme.Fill(g, r, Theme.S(8f), sel ? Theme.Hover : Theme.Raised);
            if (sel) Theme.Fill(g, new RectangleF(r.X, r.Y + Theme.S(12), Theme.S(3), r.Height - Theme.S(24)), Theme.S(1.5f), Theme.Accent);
            Theme.Glyph(g, glyphs[i], 11f, sel ? Theme.Accent : Theme.Muted, new Rectangle(r.X + Theme.S(12), r.Y, Theme.S(22), r.Height));
            int tx = r.X + Theme.S(44), tw = r.Width - Theme.S(44) - Theme.S(22);
            bool two = !string.IsNullOrEmpty(details[i]);
            Theme.Label(g, items[i], sel ? Theme.Strong : Theme.Body, sel ? Theme.Text : Theme.Muted, new Rectangle(tx, two ? r.Y + Theme.S(8) : r.Y, tw, two ? Theme.S(20) : r.Height));
            if (two) Theme.Label(g, details[i], Theme.Small, sel ? Theme.Muted : Theme.Faint, new Rectangle(tx, r.Y + Theme.S(28), tw, Theme.S(18)));
            if (warn.Contains(i)) using (var dot = new SolidBrush(Theme.Warn)) g.FillEllipse(dot, r.Right - Theme.S(16), r.Y + r.Height / 2f - Theme.S(3), Theme.S(6), Theme.S(6));
        }
        Theme.Label(g, "Recorder keeps running when this closes", Theme.Caption, Theme.Faint, new Rectangle(Theme.S(16), Height - Theme.S(34), Width - Theme.S(24), Theme.S(20)));
        using var pen = new Pen(Theme.Divider); g.DrawLine(pen, Width - 1, 0, Width - 1, Height);
    }
}

/// <summary>What the recorder itself costs: the engine process plus any FFmpeg it started. The panel and editor are not counted. Sampled only while the panel is open.</summary>
sealed class PerfSampler
{
    TimeSpan lastCpu; DateTime lastAt; float cpu; long memory; string encoder = "…";
    public string Summary => $"{encoder} · CPU {cpu:0.0}% · RAM {Theme.Bytes(memory)}";
    public float Cpu => cpu;
    public long Memory => memory;
    public void Sample(RecorderStatus status)
    {
        encoder = status.Encoder?.ToUpperInvariant() ?? "No encoder";
        var procs = new List<Process>();
        try { procs.Add(Process.GetProcessById(status.ProcessId)); } catch (ArgumentException) { }
        foreach (var p in Process.GetProcessesByName("ffmpeg"))
        {
            // Only FFmpeg started by the recorder counts; the editor's preview and thumbnail decoders are UI, not recording.
            try { if (status.ProcessId > 0 && ParentId(p) == status.ProcessId) { procs.Add(p); continue; } }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            p.Dispose();
        }
        TimeSpan total = TimeSpan.Zero; long mem = 0;
        foreach (var p in procs) { try { total += p.TotalProcessorTime; mem += p.WorkingSet64; } catch (InvalidOperationException) { } finally { p.Dispose(); } }
        var now = DateTime.UtcNow;
        if (lastAt != default && total >= lastCpu) cpu = (float)((total - lastCpu).TotalMilliseconds / Math.Max(1, (now - lastAt).TotalMilliseconds) / Environment.ProcessorCount * 100);
        lastCpu = total; lastAt = now; memory = mem;
    }

    static int ParentId(Process process)
    {
        var info = new BasicInfo();
        return NtQueryInformationProcess(process.Handle, 0, ref info, Marshal.SizeOf<BasicInfo>(), out _) == 0 ? (int)info.Parent : -1;
    }

    [StructLayout(LayoutKind.Sequential)] struct BasicInfo { public nint ExitStatus, Peb, Affinity, Priority, Id, Parent; }
    [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(nint process, int infoClass, ref BasicInfo info, int size, out int returned);
}

/// <summary>Replay buffer as a timeline: the whole history, with the part a save keeps highlighted, then time and disk used.</summary>
sealed class BufferMeter : Control, IMeasured
{
    double buffered; long bytes; long capBytes; bool capturing;
    public int BufferTarget { get; set; } = 120;
    public int ClipSeconds { get; set; } = 30;
    public BufferMeter() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
    public int MeasureHeight(int width) => Theme.S(66);
    public void Update(RecorderStatus s, Settings settings)
    {
        buffered = Math.Min(s.BufferedSeconds, settings.BufferSeconds); bytes = s.BufferBytes; capBytes = settings.MaxBufferMiB * 1048576; BufferTarget = settings.BufferSeconds; capturing = s.Capturing; Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Theme.Solid(Parent)); Theme.Prep(g);
        var strip = new RectangleF(0.5f, 0.5f, Width - 1, Theme.S(44));
        Theme.Fill(g, strip, Theme.S(6f), Theme.Raised);
        float target = Math.Max(1, BufferTarget), filled = (float)Math.Clamp(buffered / target, 0, 1);
        // Frames the buffer holds so far, as faint tiles from the right edge (newest) back.
        int tiles = 12; float tw = strip.Width / tiles;
        var state = g.Save();
        using (var clip = Theme.Round(strip, Theme.S(6f))) g.SetClip(clip);
        for (int i = 0; i < tiles; i++)
        {
            float x = strip.X + i * tw;
            if (x + tw < strip.Right - strip.Width * filled) continue;
            using var b = new SolidBrush(Color.FromArgb(i % 2 == 0 ? 43 : 50, i % 2 == 0 ? 47 : 54, i % 2 == 0 ? 54 : 62));
            g.FillRectangle(b, x, strip.Y, tw - 1, strip.Height);
        }
        float keep = (float)Math.Clamp(Math.Min(ClipSeconds, buffered) / target, 0, 1) * strip.Width;
        if (keep > 0)
        {
            var sel = new RectangleF(strip.Right - keep, strip.Y, keep, strip.Height);
            using (var b = new SolidBrush(Theme.Alpha(Theme.Accent, 46))) g.FillRectangle(b, sel);
            using (var pen = new Pen(Theme.Accent, Theme.S(2f))) g.DrawLine(pen, sel.X + 1, sel.Y, sel.X + 1, sel.Bottom);
        }
        if (capturing) using (var pen = new Pen(Theme.Live, Theme.S(2f))) g.DrawLine(pen, strip.Right - 1, strip.Y, strip.Right - 1, strip.Bottom);
        g.Restore(state);
        Theme.Stroke(g, strip, Theme.S(6f), Theme.Line);

        // Tick labels: start of history, middle, the clip mark, now.
        int ly = (int)strip.Bottom + Theme.S(4), lh = Theme.S(16);
        Theme.Label(g, "−" + Theme.Duration(target), Theme.Caption, Theme.Faint, new Rectangle(0, ly, Theme.S(60), lh));
        Theme.Label(g, "Now", Theme.Caption, Theme.Faint, new Rectangle(Width - Theme.S(60), ly, Theme.S(60), lh), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        if (keep > Theme.S(40) && keep < strip.Width - Theme.S(60))
            Theme.Label(g, "−" + Theme.Duration(ClipSeconds), Theme.Caption, Theme.Mix(Theme.Accent, Theme.Text, 0.3f), new Rectangle((int)(strip.Right - keep) - Theme.S(30), ly, Theme.S(60), lh), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

    }
}

/// <summary>
/// Apps playing sound plus muted ones, mixer style: icon, name, level slider (0–200%), percentage and a speaker that
/// keeps the app in or out of clips. Levels apply live; mute changes are applied on demand (they start a fresh buffer).
/// </summary>
sealed class AudioList : Control, IMeasured
{
    readonly List<string> playing = [];
    readonly SortedSet<string> excluded = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, int> volumes = new(StringComparer.OrdinalIgnoreCase);
    readonly List<(Rectangle Row, Rectangle Slider, Rectangle Speaker, string Name)> rows = [];
    int hover = -1, dragging = -1;
    public event EventHandler? Changed;
    public event Action? VolumeChanged;
    public IEnumerable<string> Excluded => excluded;
    /// <summary>Levels that differ from 100%.</summary>
    public IReadOnlyDictionary<string, int> Volumes => volumes;
    public AudioList() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
    public void SetRules(IEnumerable<string> rules) { excluded.Clear(); foreach (var r in rules) excluded.Add(r); Refresh2(); }
    public void SetVolumes(IReadOnlyDictionary<string, int> levels) { if (dragging >= 0) return; volumes.Clear(); foreach (var v in levels) if (v.Value != 100) volumes[v.Key] = v.Value; Invalidate(); }
    public void SetPlaying(IEnumerable<string> names)
    {
        var list = names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (list.SequenceEqual(playing, StringComparer.OrdinalIgnoreCase)) { Invalidate(); return; }
        playing.Clear(); playing.AddRange(list); Refresh2();
    }
    public void AddExcluded(string name) { excluded.Add(name); Refresh2(); Changed?.Invoke(this, EventArgs.Empty); }
    IEnumerable<string> Names => playing.Concat(volumes.Keys.Concat(excluded).Where(e => !playing.Contains(e, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase)).Take(10);
    int RowH => Theme.S(40);
    public int MeasureHeight(int width) => Math.Max(1, Names.Count()) * RowH;
    void Refresh2() { Parent?.PerformLayout(); Invalidate(); }
    int Level(string name) => volumes.TryGetValue(name, out int v) ? v : 100;
    void SetLevel(string name, int x, Rectangle slider)
    {
        int level = (int)Math.Round(Math.Clamp((x - slider.X) / (double)slider.Width, 0, 1) * 200);
        if (Math.Abs(level - 100) <= 3) level = 100; // a soft detent at 100% so "back to normal" is easy to hit
        if (level == Level(name)) return;
        if (level == 100) volumes.Remove(name); else volumes[name] = level;
        Invalidate(); VolumeChanged?.Invoke();
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e); if (!Enabled || e.Button != MouseButtons.Left) return;
        int i = rows.FindIndex(r => r.Row.Contains(e.Location)); if (i < 0) return;
        var row = rows[i];
        if (row.Speaker.Contains(e.Location)) { if (!excluded.Remove(row.Name)) excluded.Add(row.Name); Refresh2(); Changed?.Invoke(this, EventArgs.Empty); return; }
        if (Rectangle.Inflate(row.Slider, Theme.S(6), Theme.S(8)).Contains(e.Location) && !excluded.Contains(row.Name)) { dragging = i; Capture = true; SetLevel(row.Name, e.X, row.Slider); }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragging >= 0 && dragging < rows.Count) { SetLevel(rows[dragging].Name, e.X, rows[dragging].Slider); return; }
        int h = rows.FindIndex(r => r.Row.Contains(e.Location)); if (h != hover) { hover = h; Invalidate(); }
        Cursor = h >= 0 && (rows[h].Speaker.Contains(e.Location) || Rectangle.Inflate(rows[h].Slider, Theme.S(6), Theme.S(8)).Contains(e.Location)) ? Cursors.Hand : Cursors.Default;
    }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (dragging >= 0) { dragging = -1; Capture = false; Invalidate(); } }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (dragging < 0) { hover = -1; Invalidate(); } }
    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        // Double-click a slider to reset it to 100%.
        base.OnMouseDoubleClick(e); int i = rows.FindIndex(r => Rectangle.Inflate(r.Slider, Theme.S(6), Theme.S(8)).Contains(e.Location));
        if (i >= 0 && volumes.Remove(rows[i].Name)) { Invalidate(); VolumeChanged?.Invoke(); }
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Theme.Solid(Parent)); Theme.Prep(g); rows.Clear();
        var names = Names.ToList();
        if (names.Count == 0) { Theme.Label(g, "No apps are playing sound right now.", Theme.Small, Theme.Faint, new Rectangle(0, 0, Width, RowH)); return; }
        int speakerW = Theme.S(28), pctW = Theme.S(40), sliderW = Math.Clamp(Width / 3, Theme.S(90), Theme.S(140));
        for (int i = 0, y = 0; i < names.Count; i++, y += RowH)
        {
            string name = names[i]; bool off = excluded.Contains(name); int level = Level(name);
            var row = new Rectangle(0, y, Width, RowH);
            if (i == hover || i == dragging) Theme.Fill(g, RectangleF.Inflate(row, Theme.S(6f), -Theme.S(2f)), Theme.S(6f), Theme.Hover);
            int mid = y + RowH / 2;
            var icon = new Rectangle(0, mid - Theme.S(10), Theme.S(20), Theme.S(20));
            if (AppIcons.Get(name) is { } bmp)
            {
                using var attrs = new System.Drawing.Imaging.ImageAttributes();
                if (off) attrs.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.4f });
                g.DrawImage(bmp, icon, 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, attrs);
            }
            else
            {
                Theme.Fill(g, icon, Theme.S(4f), Theme.Alpha(Theme.Line, off ? 120 : 255));
                Theme.Label(g, name[..1].ToUpperInvariant(), Theme.Caption, off ? Theme.Faint : Theme.Text, icon, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            var speaker = new Rectangle(Width - speakerW, mid - speakerW / 2, speakerW, speakerW);
            var pct = new Rectangle(speaker.X - Theme.S(4) - pctW, y, pctW, RowH);
            var slider = new Rectangle(pct.X - Theme.S(10) - sliderW, mid - Theme.S(2), sliderW, Theme.S(4));
            int tx = icon.Right + Theme.S(10);
            Theme.Label(g, name, Theme.Body, off ? Theme.Faint : Theme.Text, new Rectangle(tx, y, slider.X - Theme.S(12) - tx, RowH));
            // Track, fill up to the level, a tick at 100%, and the knob.
            Theme.Fill(g, slider, Theme.S(2f), Theme.Line);
            float frac = level / 200f, knobX = slider.X + slider.Width * frac;
            if (!off)
            {
                var filled = new RectangleF(slider.X, slider.Y, Math.Max(slider.Height, knobX - slider.X), slider.Height);
                Theme.Fill(g, filled, Theme.S(2f), level > 100 ? Theme.Warn : Theme.Accent);
            }
            using (var tick = new SolidBrush(Theme.Faint)) g.FillRectangle(tick, slider.X + slider.Width / 2f - .5f, slider.Y - Theme.S(3f), 1, slider.Height + Theme.S(6f));
            float d = Theme.S(i == dragging ? 14f : i == hover ? 13f : 12f);
            using (var knob = new SolidBrush(off ? Theme.Faint : Color.White)) g.FillEllipse(knob, knobX - d / 2, mid - d / 2, d, d);
            Theme.Label(g, off ? "Off" : $"{level}%", Theme.Small, off ? Theme.Faint : level == 100 ? Theme.Muted : Theme.Text, pct, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            Theme.Glyph(g, off ? Glyphs.Mute : Glyphs.Volume, 10.5f, off ? Theme.Live : (i == hover ? Theme.Text : Theme.Muted), speaker);
            rows.Add((row, slider, speaker, name));
        }
    }
}

/// <summary>Icons read from the executable of a running app, cached for the life of the panel.</summary>
static class AppIcons
{
    static readonly Dictionary<string, Bitmap?> cache = new(StringComparer.OrdinalIgnoreCase);
    static readonly object gate = new();
    public static Bitmap? Get(string name) { lock (gate) return cache.GetValueOrDefault(name); }
    /// <summary>Call off the UI thread. Elevated or protected processes simply get no icon.</summary>
    public static void Load(IEnumerable<string> names)
    {
        foreach (string name in names)
        {
            lock (gate) if (cache.ContainsKey(name)) continue;
            Bitmap? bitmap = null;
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    if (bitmap == null && p.MainModule?.FileName is { } exe && System.Drawing.Icon.ExtractAssociatedIcon(exe) is { } icon)
                        using (icon) bitmap = icon.ToBitmap();
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException or IOException) { }
                finally { p.Dispose(); }
            }
            lock (gate) cache[name] = bitmap;
        }
    }
}

/// <summary>Newest clips as a two-column grid of thumbnails with name, age and size. Click opens the editor; the folder
/// button shows it in Explorer. With no clips, a friendly empty state shows the save shortcut.</summary>
sealed class RecentList : Control, IMeasured
{
    readonly List<(string Path, DateTime When, long Size, Bitmap? Thumb)> clips = [];
    readonly List<(Rectangle Box, Rectangle RevealBox, string Path)> rows = [];
    int hover = -1;
    CancellationTokenSource? loading;
    string? loadedFolder, loadedLastClip;
    public event Action<string?>? Open;
    public event Action<string>? Reveal;
    /// <summary>Raised after a load with clips saved today (count, bytes) and the folder total (count, bytes).</summary>
    public event Action<int, long, int, long>? Counted;
    public string Hotkey { get; set; } = "";
    public int ClipSeconds { get; set; } = 30;
    public RecentList() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); Cursor = Cursors.Hand; }
    int Columns => 2;
    int CellW(int width) => (width - Theme.S(10)) / Columns;
    int CellH(int width) => (int)(CellW(width) * 9 / 16f) + Theme.S(48);
    public int MeasureHeight(int width) => clips.Count == 0 ? Theme.S(230) : (clips.Count + Columns - 1) / Columns * (CellH(width) + Theme.S(10));
    DateTime loadedStamp;
    public void RefreshClips(Settings settings, string? lastSavedClip)
    {
        if (IsDisposed) return;
        DateTime stamp = DateTime.MinValue;
        try { if (Directory.Exists(settings.ClipDirectory)) stamp = Directory.GetLastWriteTimeUtc(settings.ClipDirectory); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        if (loadedFolder == settings.ClipDirectory && loadedLastClip == lastSavedClip && loadedStamp == stamp) return;

        (string Path, DateTime When, long Size, Bitmap? Thumb)[] files;
        try
        {
            files = Directory.Exists(settings.ClipDirectory)
                ? new DirectoryInfo(settings.ClipDirectory).EnumerateFiles("*.mp4")
                    .Where(file => !file.Name.StartsWith('.'))
                    .Select(file => (file.FullName, file.LastWriteTime, file.Length, (Bitmap?)null))
                    .OrderByDescending(file => file.LastWriteTime.ToUniversalTime())
                    .ToArray()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A removable folder or file can disappear during the scan. Keep the previous list and try again.
            return;
        }

        loadedFolder = settings.ClipDirectory; loadedStamp = stamp;
        loadedLastClip = lastSavedClip;
        loading?.Cancel();
        var cts = loading = new CancellationTokenSource();
        foreach (var clip in clips) clip.Thumb?.Dispose();
        clips.Clear();
        clips.AddRange(files.Take(12));
        rows.Clear();
        hover = -1;

        var day = DateTime.Today;
        var today = files.Where(file => file.When.Date == day).ToArray();
        Counted?.Invoke(today.Length, today.Sum(file => file.Size), files.Length, files.Sum(file => file.Size));
        Parent?.PerformLayout();
        Invalidate();
        _ = LoadThumbnailsAsync(settings, cts);
    }

    async Task LoadThumbnailsAsync(Settings settings, CancellationTokenSource cts)
    {
        try
        {
            for (int i = 0; i < clips.Count && !cts.IsCancellationRequested; i++)
            {
                try
                {
                    var thumb = await Frames.ThumbnailAsync(settings, clips[i].Path, cts.Token);
                    if (cts.IsCancellationRequested) { thumb?.Dispose(); return; }
                    clips[i] = clips[i] with { Thumb = thumb };
                    Invalidate();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or System.ComponentModel.Win32Exception) { }
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(loading, cts)) loading = null;
            cts.Dispose();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int h = rows.FindIndex(r => r.Box.Contains(e.Location)); if (h != hover) { hover = h; Invalidate(); } Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }
    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        int i = rows.FindIndex(r => r.Box.Contains(e.Location)); if (i < 0) return;
        if (rows[i].RevealBox.Contains(e.Location)) Reveal?.Invoke(rows[i].Path); else Open?.Invoke(rows[i].Path);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Theme.Solid(Parent)); Theme.Prep(g); rows.Clear();
        if (clips.Count == 0) { DrawEmpty(g); return; }
        int cw = CellW(Width), ch = CellH(Width), gap = Theme.S(10), thumbH = (int)(cw * 9 / 16f);
        for (int i = 0; i < clips.Count; i++)
        {
            var clip = clips[i]; bool hot = i == hover;
            var box = new Rectangle(i % Columns * (cw + gap), i / Columns * (ch + gap), cw, ch);
            var img = new Rectangle(box.X, box.Y, cw, thumbH);
            Theme.Fill(g, img, Theme.S(8f), Theme.Raised);
            if (clip.Thumb != null)
            {
                var state = g.Save(); using (var path = Theme.Round(img, Theme.S(8f))) g.SetClip(path);
                float scale = Math.Max(img.Width / (float)clip.Thumb.Width, img.Height / (float)clip.Thumb.Height);
                float dw = clip.Thumb.Width * scale, dh = clip.Thumb.Height * scale;
                g.DrawImage(clip.Thumb, img.X + (img.Width - dw) / 2, img.Y + (img.Height - dh) / 2, dw, dh);
                if (hot) using (var shade = new SolidBrush(Color.FromArgb(90, 0, 0, 0))) g.FillRectangle(shade, img);
                g.Restore(state);
            }
            else Theme.Glyph(g, Glyphs.Video, 14, Theme.Faint, img);
            Theme.Stroke(g, img, Theme.S(8f), hot ? Theme.Alpha(Theme.Accent, 200) : Theme.Divider, hot ? Theme.S(1.5f) : 1);
            // Size badge on the thumbnail, play glyph and Explorer button on hover.
            string size = Theme.Bytes(clip.Size); int bw = Theme.Measure(size, Theme.Caption).Width + Theme.S(10);
            var badge = new RectangleF(img.Right - bw - Theme.S(6), img.Bottom - Theme.S(22), bw, Theme.S(16));
            Theme.Fill(g, badge, Theme.S(4f), Color.FromArgb(180, 10, 10, 12));
            Theme.Label(g, size, Theme.Caption, Theme.Text, Rectangle.Round(badge), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            var reveal = new Rectangle(img.Right - Theme.S(34), img.Y + Theme.S(6), Theme.S(28), Theme.S(26));
            if (hot)
            {
                var play = new RectangleF(img.X + img.Width / 2f - Theme.S(18), img.Y + img.Height / 2f - Theme.S(18), Theme.S(36), Theme.S(36));
                using (var b = new SolidBrush(Theme.Alpha(Theme.Accent, 230))) g.FillEllipse(b, play);
                Theme.Glyph(g, Glyphs.Play, 11, Theme.Ink, Rectangle.Round(play));
                Theme.Fill(g, reveal, Theme.S(6f), Color.FromArgb(190, 10, 10, 12));
                Theme.Glyph(g, Glyphs.Folder, 9.5f, Theme.Text, reveal);
            }
            Theme.Label(g, Path.GetFileNameWithoutExtension(clip.Path), Theme.Body, Theme.Text, new Rectangle(box.X + Theme.S(2), img.Bottom + Theme.S(6), cw - Theme.S(4), Theme.S(20)));
            Theme.Label(g, Ago(clip.When), Theme.Small, Theme.Muted, new Rectangle(box.X + Theme.S(2), img.Bottom + Theme.S(25), cw - Theme.S(4), Theme.S(18)));
            rows.Add((box, reveal, clip.Path));
        }
    }
    void DrawEmpty(Graphics g)
    {
        var area = new Rectangle(0, Theme.S(4), Width, Theme.S(218));
        using (var pen = new Pen(Theme.Line) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash }) using (var path = Theme.Round(area, Theme.S(10f))) g.DrawPath(pen, path);
        int cx = Width / 2;
        var circle = new RectangleF(cx - Theme.S(26), area.Y + Theme.S(26), Theme.S(52), Theme.S(52));
        Theme.Fill(g, circle, Theme.S(26f), Theme.Alpha(Theme.Accent, 40));
        Theme.Glyph(g, Glyphs.Record, 16, Theme.Accent, Rectangle.Round(circle));
        Theme.Label(g, "No clips yet", Theme.Title, Theme.Text, new Rectangle(0, area.Y + Theme.S(90), Width, Theme.S(22)), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        Theme.Label(g, $"Press the shortcut in any game to save the last {Theme.Span(ClipSeconds)}", Theme.Small, Theme.Muted, new Rectangle(0, area.Y + Theme.S(114), Width, Theme.S(18)), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        // The shortcut as keycaps.
        var keys = (Hotkey.Length > 0 ? Hotkey : "Alt+C").Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int kh = Theme.S(30), kgap = Theme.S(6);
        var widths = keys.Select(k => Math.Max(kh, Theme.Measure(k, Theme.Strong).Width + Theme.S(18))).ToList();
        int x = cx - (widths.Sum() + kgap * (keys.Length - 1)) / 2, ky = area.Y + Theme.S(150);
        for (int i = 0; i < keys.Length; i++)
        {
            var cap = new Rectangle(x, ky, widths[i], kh);
            Theme.Fill(g, new RectangleF(cap.X, cap.Y + Theme.S(3), cap.Width, cap.Height), Theme.S(6f), Theme.Line);
            Theme.Fill(g, cap, Theme.S(6f), Theme.Hover); Theme.Stroke(g, cap, Theme.S(6f), Theme.Line);
            Theme.Label(g, keys[i], Theme.Strong, Theme.Text, cap, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            x += widths[i] + kgap;
        }
    }
    static string Ago(DateTime when)
    {
        var span = DateTime.Now - when;
        return span.TotalMinutes < 1 ? "Just now" : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} min ago" : when.Date == DateTime.Today ? "Today, " + when.ToString("t", System.Globalization.CultureInfo.CurrentCulture) : when.ToString("MMM d, t", System.Globalization.CultureInfo.CurrentCulture);
    }
    protected override void Dispose(bool disposing) { if (disposing) { loading?.Cancel(); foreach (var c in clips) c.Thumb?.Dispose(); } base.Dispose(disposing); }
}
