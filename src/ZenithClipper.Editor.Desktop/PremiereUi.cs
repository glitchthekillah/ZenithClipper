using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ZenithClipper.Editing;
using ZenithClipper.Mlt;

namespace ZenithClipper.Editor.Desktop;
using Control = Avalonia.Controls.Control;

/// <summary>
/// The Premiere Pro layer of the editor: the Tools panel (V A B N C Y U R H Z), Premiere's default keyboard shortcuts,
/// In/Out points, J/K/L shuttle, clipboard (copy, cut, paste, paste insert, paste attributes), the Effects panel with
/// video effects, audio effects and transitions, and the Effect Controls for whatever is applied to the selected clip.
/// </summary>
public sealed partial class EditorWindow
{
    internal enum EditTool { Selection, TrackForward, Ripple, Rolling, Razor, Slip, Slide, RateStretch, Hand, Zoom }
    internal EditTool tool = EditTool.Selection;
    internal bool trackForwardSingle;
    readonly StackPanel toolPalette = new() { Orientation = Orientation.Horizontal, Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
    static readonly (EditTool Tool, string Icon, string Name, string Key)[] ToolList =
    [
        (EditTool.Selection, "pointer", "Selection", "V"), (EditTool.TrackForward, "forward", "Track Select Forward", "A"),
        (EditTool.Ripple, "rippleedit", "Ripple Edit", "B"), (EditTool.Rolling, "rolling", "Rolling Edit", "N"),
        (EditTool.Razor, "cut", "Razor", "C"), (EditTool.Slip, "slip", "Slip", "Y"), (EditTool.Slide, "slide", "Slide", "U"),
        (EditTool.RateStretch, "stretch", "Rate Stretch", "R"), (EditTool.Hand, "hand", "Hand", "H"), (EditTool.Zoom, "zoom", "Zoom", "Z"),
    ];

    internal long? markIn, markOut;
    // Shared by every editor window so clips copy between open projects, like Premiere.
    static readonly List<(Guid TrackId, TimelineClip Clip)> clipboard = [];
    DispatcherTimer? shuttle; int shuttleRate;
    static double DefaultTransitionSeconds => Prefs.TransitionSeconds;

    void BuildToolPalette()
    {
        toolPalette.Children.Clear();
        foreach (var (t, icon, name, key) in ToolList)
        {
            var button = ActionButton(icon, $"{name} tool ({key})", () => SetTool(t), iconOnly: true);
            if (t == tool) { button.Background = SelectedBrush; button.Tag = SelectedBrush; }
            toolPalette.Children.Add(button);
        }
    }
    internal void SetTool(EditTool next, bool single = false)
    {
        tool = next; trackForwardSingle = single; BuildToolPalette();
        timeline.Cursor = next switch
        {
            EditTool.Razor => new Cursor(StandardCursorType.Cross), EditTool.Hand => new Cursor(StandardCursorType.Hand),
            EditTool.Zoom => new Cursor(StandardCursorType.Cross), EditTool.TrackForward => new Cursor(StandardCursorType.RightSide),
            _ => null
        };
        status.Text = next switch
        {
            EditTool.Selection => "Selection: drag clips, drag edges to trim.",
            EditTool.TrackForward => single ? "Track Select Forward (one track): drag to move everything after the click on that track." : "Track Select Forward: drag to move everything after the click, on all tracks.",
            EditTool.Ripple => "Ripple Edit: drag a clip edge; later clips move to close or open the gap.",
            EditTool.Rolling => "Rolling Edit: drag a cut between two clips; both change length.",
            EditTool.Razor => "Razor: click a clip to cut it. Shift+click cuts every track.",
            EditTool.Slip => "Slip: drag a clip to change which part of the source plays.",
            EditTool.Slide => "Slide: drag a clip between its neighbours; they trim to make room.",
            EditTool.RateStretch => "Rate Stretch: drag a clip edge to change its speed to fit.",
            EditTool.Hand => "Hand: drag to scroll the timeline.",
            _ => "Zoom: click to zoom in, Alt+click to zoom out."
        };
    }

    // ---------------- Keyboard (Premiere Pro defaults) ----------------
    bool HandlePremiereKey(KeyEventArgs e)
    {
        var mods = e.KeyModifiers; bool ctrl = mods.HasFlag(KeyModifiers.Control) || mods.HasFlag(KeyModifiers.Meta), shift = mods.HasFlag(KeyModifiers.Shift), alt = mods.HasFlag(KeyModifiers.Alt);
        bool plain = !ctrl && !alt;
        if (HandleSourceKey(e)) return true;
        if (HandleMulticamKey(e)) return true;
        switch (e.Key)
        {
            case Key.V when !ctrl && !alt && !shift: SetTool(EditTool.Selection); return true;
            case Key.V when shift && !ctrl && !alt: Run(ToggleVoiceOver); return true;
            case Key.A when plain: SetTool(EditTool.TrackForward, shift); return true;
            case Key.OemTilde when ctrl: ToggleFullScreen(); return true;
            case Key.G when ctrl && !shift && !alt: GroupSelected(); return true;
            case Key.G when ctrl && shift && !alt: UngroupSelected(); return true;
            case Key.B when plain && !shift: SetTool(EditTool.Ripple); return true;
            case Key.N when plain && !shift: SetTool(EditTool.Rolling); return true;
            case Key.C when plain && !shift: SetTool(EditTool.Razor); return true;
            case Key.C when plain && shift: ToggleCaptions(); return true;
            case Key.Y when plain && !shift: SetTool(EditTool.Slip); return true;
            case Key.U when plain && !shift: SetTool(EditTool.Slide); return true;
            case Key.R when plain && !shift: SetTool(EditTool.RateStretch); return true;
            case Key.H when plain && !shift: SetTool(EditTool.Hand); return true;
            case Key.Z when plain && !shift: SetTool(EditTool.Zoom); return true;
            case Key.Enter when plain && !shift: Run(RenderInToOut); return true;
            case Key.I when plain && !shift: markIn = playhead; MarksChanged(); return true;
            case Key.O when plain && !shift: markOut = playhead + 1; MarksChanged(); return true;
            case Key.I when alt: markIn = null; MarksChanged(); return true;
            case Key.O when alt: markOut = null; MarksChanged(); return true;
            case Key.X when ctrl && shift: markIn = markOut = null; MarksChanged(); return true;
            case Key.I when plain && shift: if (markIn is { } i) Seek(i); return true;
            case Key.O when plain && shift: if (markOut is { } o) Seek(o - 1); return true;
            case Key.X when plain && !shift: MarkClip(); return true;
            case Key.OemComma when plain && !shift: SourceEdit(insert: true); return true;
            case Key.OemPeriod when plain && !shift: SourceEdit(insert: false); return true;
            case Key.J when plain: Shuttle(-1); return true;
            case Key.K when plain && shift: StopShuttle(); Run(PlayAround); return true;
            case Key.K when plain && !shift: StopShuttle(); if (playing) Run(TogglePlay); return true;
            case Key.L when plain: Shuttle(1); return true;
            case Key.Q when plain && !shift: RippleToPlayhead(true); return true;
            case Key.W when plain && !shift: RippleToPlayhead(false); return true;
            case Key.K when ctrl && shift: { long at = playhead; Change("Add edit to all tracks", p => EditCommands.AddEditAll(p, p.ActiveSequenceId, at)); return true; }
            case Key.OemSemicolon when plain: LiftExtract(false); return true;
            case Key.OemQuotes when plain: LiftExtract(true); return true;
            case Key.Up when plain: Seek(EditPoints().LastOrDefault(f => f < playhead)); return true;
            case Key.Down when plain: { var next = EditPoints().FirstOrDefault(f => f > playhead); if (next > 0) Seek(next); return true; }
            case Key.Up when alt && !ctrl: MoveClipTrack(-1); return true;
            case Key.Down when alt && !ctrl: MoveClipTrack(1); return true;
            case Key.Left when alt && !ctrl: Nudge(shift ? -Prefs.LargeTrimOffset : -1); return true;
            case Key.Right when alt && !ctrl: Nudge(shift ? Prefs.LargeTrimOffset : 1); return true;
            case Key.M when ctrl && !alt && !shift: Run(Export); return true;
            case Key.M when plain && !shift: AddMarker(); return true;
            case Key.M when plain && shift: { var m = Sequence.Markers.Select(x => x.Frame).Where(f => f > playhead).DefaultIfEmpty(-1).Min(); if (m >= 0) Seek(m); return true; }
            case Key.M when ctrl && shift: { var m = Sequence.Markers.Select(x => x.Frame).Where(f => f < playhead).DefaultIfEmpty(-1).Max(); if (m >= 0) Seek(m); return true; }
            case Key.E when plain && shift: ToggleEnabled(); return true;
            case Key.C when ctrl && !shift && !alt: CopySelection(false); return true;
            case Key.X when ctrl && !shift: CopySelection(true); return true;
            case Key.V when ctrl && alt: PasteAttributes(); return true;
            case Key.V when ctrl: PasteClipboard(shift); return true;
            case Key.D when ctrl && shift: ApplyDefaultTransition(audio: true, video: false); return true;
            case Key.D when ctrl: ApplyDefaultTransition(audio: false, video: true); return true;
            case Key.D when plain && shift: ApplyDefaultTransition(audio: true, video: true); return true;
            case Key.L when ctrl && !shift: if (selectedClip is { } lc && Sequence.Tracks.SelectMany(t => t.Clips).First(c => c.Id == lc).LinkId is not null) UnlinkSelected(); else RelinkSelected(); return true;
            case Key.E when ctrl && shift: Run(ExportFrame); return true;
            case Key.OemBackslash or Key.Oem5 when plain: FitTimeline(); return true;
            case Key.OemPlus when ctrl && !alt: ResizeTracks(12); return true;
            case Key.OemMinus when ctrl && !alt: ResizeTracks(-12); return true;
            case Key.OemPlus when plain: zoom.Value = Math.Min(zoom.Maximum, zoom.Value * 1.4); return true;
            case Key.OemMinus when plain: zoom.Value = Math.Max(zoom.Minimum, zoom.Value / 1.4); return true;
            case Key.Delete when selectedClip is null && selectedTrack is { } gt: { long at = playhead; Change("Ripple delete gap", p => EditCommands.CloseGap(p, p.ActiveSequenceId, gt, at)); return true; }
            case Key.Left when shift && !alt: Seek(Math.Max(0, playhead - Prefs.LargeTrimOffset)); return true;
            case Key.Right when shift && !alt: Seek(playhead + Prefs.LargeTrimOffset); return true;
        }
        return false;
    }

    List<long> EditPoints() => Sequence.Tracks.Where(t => !t.Locked).SelectMany(t => t.Clips).SelectMany(c => new[] { c.Start, c.End }).Append(0L).Distinct().Order().ToList();
    void MarksChanged() { timeline.Rebuild(); status.Text = markIn is null && markOut is null ? "In and Out cleared." : $"In {Clock(markIn ?? 0)} · Out {(markOut is { } o ? Clock(o) : "—")}"; }
    void MarkClip()
    {
        var clip = Sequence.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == selectedClip) ?? Sequence.Tracks.FirstOrDefault(t => t.Id == selectedTrack)?.Clips.FirstOrDefault(c => c.Start <= playhead && c.End > playhead);
        if (clip is null) return; markIn = clip.Start; markOut = clip.End; MarksChanged();
    }
    void RippleToPlayhead(bool head)
    {
        if (selectedTrack is not { } t) { status.Text = "Select a track first."; return; }
        long at = playhead; Change(head ? "Ripple trim previous edit" : "Ripple trim next edit", p => EditCommands.RippleTrimToPlayhead(p, p.ActiveSequenceId, t, at, head));
    }
    void LiftExtract(bool extract)
    {
        if (markIn is not { } i || markOut is not { } o) { status.Text = "Mark In (I) and Out (O) first."; return; }
        Change(extract ? "Extract" : "Lift", p => EditCommands.LiftExtract(p, p.ActiveSequenceId, i, o, extract));
        if (extract) { markOut = null; Seek(i); }
    }
    void ResizeTracks(int by)
    {
        int next = Math.Clamp(TrackHeight + by, 36, 124); if (next == TrackHeight) return;
        TrackHeight = next; BuildTrackHeaders(); timeline.Rebuild(); status.Text = $"Track height {next}px · Ctrl+= taller, Ctrl+- shorter.";
    }
    void Nudge(long frames)
    {
        if (selectedClip is not { } c) return;
        var all = Sequence.Tracks.SelectMany(t => t.Clips).ToList(); var clip = all.First(x => x.Id == c);
        var moving = EditCommands.Closure(Project, Project.ActiveSequenceId, SelectedClips);
        frames = Math.Max(frames, -all.Where(x => moving.Contains(x.Id)).Min(x => x.Start)); if (frames == 0) return;
        var extra = SelectedClips.ToList();
        Change(moving.Count > 1 ? "Nudge clips" : "Nudge clip", p => EditCommands.MoveOverwrite(p, p.ActiveSequenceId, c, clip.Start + frames, alsoMove: extra));
    }
    void MoveClipTrack(int rows)
    {
        if (selectedClip is not { } c) return;
        var track = Sequence.Tracks.First(t => t.Clips.Any(x => x.Id == c)); var display = DisplayTracks(); int row = display.IndexOf(track) + rows;
        if (row < 0 || row >= display.Count || display[row].Kind != track.Kind || display[row].Locked) return;
        var target = display[row].Id; long start = track.Clips.First(x => x.Id == c).Start;
        Change("Move clip to track", p => EditCommands.MoveOverwrite(p, p.ActiveSequenceId, c, start, new Dictionary<Guid, Guid> { [c] = target }));
        selectedTrack = target;
    }
    void ToggleEnabled()
    {
        if (selectedClip is not { } c) return;
        Change("Enable/disable clip", p => EditCommands.ChangeLinkedClip(p, p.ActiveSequenceId, c, x => x.Enabled = !x.Enabled));
    }

    // ---------------- J / K / L ----------------
    void Shuttle(int direction)
    {
        // Premiere: L plays, pressing again doubles speed (up to 8x); J does the same in reverse; K stops.
        if (direction > 0 && shuttleRate <= 0 && !playing) { shuttleRate = 1; Run(TogglePlay); return; }
        if (direction > 0 && shuttleRate == 1 && playing) { Run(TogglePlay); shuttleRate = 2; }
        else if (Math.Sign(shuttleRate) == direction) shuttleRate = Math.Clamp(shuttleRate * 2, -8, 8);
        else shuttleRate = direction * (direction < 0 ? 1 : 2);
        if (playing) Run(TogglePlay);
        shuttle ??= new DispatcherTimer(TimeSpan.FromMilliseconds(66), DispatcherPriority.Input, (_, _) =>
        {
            long step = (long)Math.Round(timelineFps.Value * shuttleRate / 15.0);
            long next = playhead + step;
            if (next < 0 || next >= MonitorDuration) { StopShuttle(); Seek(Math.Clamp(next, 0, Math.Max(0, MonitorDuration - 1))); return; }
            Seek(next);
        });
        shuttle.Start(); status.Text = $"Shuttle {(shuttleRate < 0 ? "◀ " : "▶ ")}{Math.Abs(shuttleRate)}×";
    }
    void StopShuttle() { shuttle?.Stop(); shuttleRate = 0; }

    // ---------------- Clipboard ----------------
    void CopySelection(bool cut)
    {
        if (selectedClip is not { } c) return;
        var picked = SelectionClosure();
        clipboard.Clear();
        foreach (var track in Sequence.Tracks)
            foreach (var x in track.Clips.Where(x => picked.Contains(x.Id)))
                clipboard.Add((track.Id, ProjectValidation.Clone(x)));
        RememberClipboardSource();
        if (cut) Delete(false);
        status.Text = $"{(cut ? "Cut" : "Copied")} {clipboard.Count} clip{(clipboard.Count == 1 ? "" : "s")}. Ctrl+V overwrites at the playhead, Ctrl+Shift+V inserts.";
    }
    void PasteClipboard(bool insert)
    {
        if (clipboard.Count == 0) { status.Text = "Nothing copied yet."; return; }
        long at = playhead; List<Guid> made = []; string? note = null;
        Change(insert ? "Paste insert" : "Paste", p => { var board = ClipboardFor(p, out note); made = board.Count == 0 ? [] : EditCommands.Paste(p, p.ActiveSequenceId, board, at, insert); });
        if (note is not null) status.Text = note;
        if (made.Count > 0) { selectedClip = made[0]; BuildInspector(); timeline.Rebuild(); }
    }
    void PasteAttributes()
    {
        if (clipboard.Count == 0 || selectedClip is not { } c) { status.Text = "Copy a clip, select another, then Ctrl+Alt+V."; return; }
        var source = clipboard.Select(x => x.Clip).FirstOrDefault(x => Sequence.Tracks.First(t => t.Id == clipboard.First(b => b.Clip.Id == x.Id).TrackId).Kind == Sequence.Tracks.First(t => t.Clips.Any(y => y.Id == c)).Kind) ?? clipboard[0].Clip;
        Change("Paste attributes", p => EditCommands.ChangeLinkedClip(p, p.ActiveSequenceId, c, x =>
        {
            x.Transform = ProjectValidation.Clone(source.Transform); x.Opacity = source.Opacity; x.GainDb = source.GainDb;
            x.Effects = ProjectValidation.Clone(source.Effects); x.Speed = x.Speed; x.Label = source.Label;
        }));
        status.Text = "Pasted motion, opacity, volume and effects.";
    }

    // ---------------- Transitions ----------------
    internal void ApplyDefaultTransition(bool audio, bool video)
    {
        long frames = timelineFps.Frames(DefaultTransitionSeconds);
        int applied = 0;
        foreach (var track in Sequence.Tracks.Where(t => !t.Locked && (t.Kind == TrackKind.Audio ? audio : video)))
        {
            // The selected clip (and its linked partner) wins; otherwise the edit nearest the playhead on the selected track.
            var selected = Sequence.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == selectedClip);
            TimelineClip? target = selected is null ? null : track.Clips.FirstOrDefault(c => c.Id == selected.Id || (selected.LinkId is not null && c.LinkId == selected.LinkId));
            bool head;
            if (target is not null) head = Math.Abs(playhead - target.Start) <= Math.Abs(playhead - target.End);
            else
            {
                if (selectedTrack != track.Id && selected is null) continue;
                var cut = track.Clips.SelectMany(c => new[] { (Clip: c, Head: true, At: c.Start), (Clip: c, Head: false, At: c.End) }).OrderBy(x => Math.Abs(x.At - playhead)).FirstOrDefault();
                if (cut.Clip is null) continue; target = cut.Clip; head = cut.Head;
                var neighbour = track.Clips.FirstOrDefault(c => c.Start == target.End);
                if (!head && neighbour is not null) { target = neighbour; head = true; }
            }
            var t = track.Id; var id = target.Id; string kind = track.Kind == TrackKind.Audio ? "constant_power" : "cross_dissolve";
            Change("Apply default transition", p => EditCommands.SetTransition(p, p.ActiveSequenceId, t, id, head, kind, frames));
            applied++;
        }
        status.Text = applied == 0 ? "Select a clip (or a track) to add a transition." : $"Applied {(audio && !video ? "Constant Power" : "Cross Dissolve")} ({DefaultTransitionSeconds} s).";
    }
    internal void ApplyTransitionTo(Guid trackId, Guid clipId, string kind, bool head)
    {
        long frames = timelineFps.Frames(DefaultTransitionSeconds);
        Change("Apply transition", p => EditCommands.SetTransition(p, p.ActiveSequenceId, trackId, clipId, head, kind, frames));
        status.Text = $"{TransitionCatalog.Find(kind)?.Name} applied to the {(head ? "start" : "end")} of the clip.";
    }
    internal void ApplyEffectTo(Guid clipId, string effectId)
    {
        var def = EffectCatalog.Find(effectId)!;
        var track = Sequence.Tracks.First(t => t.Clips.Any(c => c.Id == clipId));
        var clip = track.Clips.First(c => c.Id == clipId);
        // Audio effects go on the audio half of a linked pair, video effects on the picture.
        if ((def.Kind == EffectKind.Audio) != (track.Kind == TrackKind.Audio) && clip.LinkId is { } link)
        {
            var partner = Sequence.Tracks.Where(t => (t.Kind == TrackKind.Audio) == (def.Kind == EffectKind.Audio)).SelectMany(t => t.Clips).FirstOrDefault(c => c.LinkId == link);
            if (partner is not null) { clipId = partner.Id; }
        }
        else if ((def.Kind == EffectKind.Audio) != (track.Kind == TrackKind.Audio)) { status.Text = def.Kind == EffectKind.Audio ? "That's an audio effect. Drop it on a clip with sound." : "That's a video effect. Drop it on a video clip."; return; }
        var target = clipId;
        Change("Apply " + def.Name, p => EditCommands.ChangeLinkedClip(p, p.ActiveSequenceId, target, c => c.Effects.Add(new ClipEffect { Id = def.Id })));
        selectedClip = target; inspectorSections["Effects"] = true; BuildInspector(); timeline.Rebuild();
        status.Text = def.Name + " applied. Adjust it in the inspector.";
    }

    // ---------------- Effects panel ----------------
    readonly ScrollViewer effectsBrowser = new() { IsVisible = false, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    void UpdateEffectsBrowser()
    {
        effectsBrowser.IsVisible = libraryFilter == "Effects";
        if (!effectsBrowser.IsVisible) return;
        var search = mediaSearch.Text?.Trim() ?? "";
        bool Match(string name, string category) => search.Length == 0 || name.Contains(search, StringComparison.OrdinalIgnoreCase) || category.Contains(search, StringComparison.OrdinalIgnoreCase);
        var list = new StackPanel { Spacing = 1, Margin = new Thickness(8, 8, 8, 8) };
        void Group(string title, IEnumerable<(string Id, string Name, string Category, string Icon, bool Transition)> items)
        {
            var rows = items.Where(i => Match(i.Name, i.Category)).ToList(); if (rows.Count == 0) return;
            list.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), FontSize = 10.5, FontWeight = FontWeight.SemiBold, Foreground = FaintBrush, Margin = new Thickness(4, 10, 0, 4) });
            foreach (var category in rows.GroupBy(r => r.Category))
            {
                list.Children.Add(new TextBlock { Text = category.Key, FontSize = 11, Foreground = MutedBrush, Margin = new Thickness(4, 6, 0, 2) });
                foreach (var item in category) list.Children.Add(EffectRow(item.Id, item.Name, item.Icon, item.Transition));
            }
        }
        AddPresetRows(list, Match);
        Group("Video transitions", TransitionCatalog.All.Where(t => !t.Audio).Select(t => (t.Id, t.Name, t.Category, "transition", true)));
        Group("Audio transitions", TransitionCatalog.All.Where(t => t.Audio).Select(t => (t.Id, t.Name, t.Category, "transition", true)));
        Group("Video effects", EffectCatalog.All.Where(e => e.Kind == EffectKind.Video).Select(e => (e.Id, e.Name, e.Category, "fx", false)));
        Group("Audio effects", EffectCatalog.All.Where(e => e.Kind == EffectKind.Audio).Select(e => (e.Id, e.Name, e.Category, "audio", false)));
        if (list.Children.Count == 0) list.Children.Add(new TextBlock { Text = "No effects match.", Foreground = MutedBrush, FontSize = 12, Margin = new Thickness(4, 12) });
        effectsBrowser.Content = list;
        mediaCount.Text = $"{EffectCatalog.All.Length} effects · {TransitionCatalog.All.Length} transitions · drag onto a clip";
    }
    Control EffectRow(string id, string name, string icon, bool transition)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*"), Height = 28 };
        row.Children.Add(Glyph(icon, transition ? AccentSoftBrush : MutedBrush, 14));
        var label = Copy(name, 12, SoftTextBrush); Grid.SetColumn(label, 1); row.Children.Add(label);
        var host = new Border { Child = row, Padding = new Thickness(6, 0), CornerRadius = new CornerRadius(5), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
        ToolTip.SetTip(host, transition ? "Drag onto the start or end of a clip, or click to apply to the selected clip's nearest edge." : "Drag onto a clip, or click to apply to the selected clip.");
        host.PointerEntered += (_, _) => host.Background = HoverBrush; host.PointerExited += (_, _) => host.Background = Brushes.Transparent;
        Point? press = null; bool dragging = false;
        host.PointerPressed += (_, e) => { if (e.GetCurrentPoint(host).Properties.IsLeftButtonPressed) { press = e.GetPosition(this); dragging = false; e.Handled = true; } };
        host.PointerMoved += (_, e) =>
        {
            if (press is not { } start) return; var now = e.GetPosition(this);
            if (!dragging && Math.Abs(now.X - start.X) + Math.Abs(now.Y - start.Y) < 6) return;
            if (!dragging) { dragging = true; e.Pointer.Capture(host); }
            if (OverTimeline(e)) timeline.DragEffectOver(id, name, transition, e.GetPosition(timeline)); else timeline.DropEffect(null);
        };
        host.PointerReleased += (_, e) =>
        {
            if (dragging) { timeline.DropEffect(OverTimeline(e) ? e.GetPosition(timeline) : null); e.Pointer.Capture(null); }
            else if (press is not null)
            {
                if (selectedClip is not { } c) status.Text = "Select a clip first, or drag the effect onto one.";
                else if (transition) { var track = Sequence.Tracks.First(t => t.Clips.Any(x => x.Id == c)); var clip = track.Clips.First(x => x.Id == c); ApplyTransitionTo(track.Id, c, id, Math.Abs(playhead - clip.Start) <= Math.Abs(playhead - clip.End)); }
                else ApplyEffectTo(c, id);
            }
            press = null; dragging = false;
        };
        return host;
    }

    // ---------------- Effect Controls (inspector) ----------------
    void InspectorEffectControls(TimelineClip clip, EditTrack track)
    {
        bool audioClip = track.Kind == TrackKind.Audio;
        // Transitions on this clip's edges.
        var transitions = InspectorSection("Transitions", "transition", clip.TransitionIn is not null || clip.TransitionOut is not null);
        foreach (bool head in new[] { true, false })
        {
            var current = head ? clip.TransitionIn : clip.TransitionOut;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("70,*,6,64,4,28"), MinHeight = 32 };
            row.Children.Add(Copy(head ? "Start" : "End", 12, SoftTextBrush));
            var kinds = TransitionCatalog.All.Where(t => t.Audio == audioClip).ToList();
            var combo = new ComboBox { ItemsSource = new[] { "None" }.Concat(kinds.Select(k => k.Name)).ToList(), SelectedIndex = current is null ? 0 : kinds.FindIndex(k => k.Id == current.Kind) + 1, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 28 };
            Grid.SetColumn(combo, 1); row.Children.Add(combo);
            var seconds = new TextBox { Text = current is null ? "1" : timelineFps.Seconds(current.Duration).ToString("0.##", CultureInfo.InvariantCulture), FontSize = 12, MinHeight = 28, Height = 28, HorizontalContentAlignment = HorizontalAlignment.Right, IsEnabled = current is not null };
            Grid.SetColumn(seconds, 3); row.Children.Add(seconds);
            var unit = Copy("s", 11, FaintBrush); unit.Margin = new Thickness(4, 0, 0, 0); Grid.SetColumn(unit, 5); row.Children.Add(unit);
            var t = track.Id; var id = clip.Id; bool h = head;
            combo.SelectionChanged += (_, _) =>
            {
                string? kind = combo.SelectedIndex <= 0 ? null : kinds[combo.SelectedIndex - 1].Id;
                if (kind == current?.Kind) return;
                long frames = current?.Duration ?? timelineFps.Frames(DefaultTransitionSeconds);
                Change(kind is null ? "Remove transition" : "Change transition", p => EditCommands.SetTransition(p, p.ActiveSequenceId, t, id, h, kind, frames));
            };
            seconds.LostFocus += (_, _) =>
            {
                if (current is null || !double.TryParse(seconds.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v <= 0) return;
                long frames = timelineFps.Frames(v); if (frames == current.Duration) return; string kind = current.Kind;
                Change("Transition duration", p => EditCommands.SetTransition(p, p.ActiveSequenceId, t, id, h, kind, frames));
            };
            transitions.Children.Add(row);
        }
        transitions.Children.Add(Copy("Ctrl+D video · Ctrl+Shift+D audio · Shift+D both", 11, FaintBrush));

        // Applied catalog effects, in order, with their parameters.
        var applied = clip.Effects.Select((e, i) => (Effect: e, Index: i, Def: EffectCatalog.Find(e.Id))).Where(x => x.Def is not null).ToList();
        var section = InspectorSection("Effects", "fx", applied.Count > 0);
        if (applied.Count == 0) section.Children.Add(new TextBlock { Text = "Open the Effects tab on the left and drag an effect onto this clip.", FontSize = 11, Foreground = MutedBrush, TextWrapping = TextWrapping.Wrap });
        else { var savePreset = Button("Save as preset…", () => Run(() => SaveEffectPreset(clip, audioClip))); savePreset.Margin = new Thickness(0, 2, 0, 4); section.Children.Add(savePreset); }
        foreach (var (effect, index, def) in applied)
        {
            var card = new StackPanel { Spacing = 4, Margin = new Thickness(0, 4, 0, 6) };
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("28,*,Auto,Auto,Auto"), Height = 28 };
            var toggle = new CheckBox { IsChecked = effect.Enabled, MinWidth = 0 }; ToolTip.SetTip(toggle, "Effect on/off");
            int at = index; var cid = clip.Id;
            toggle.IsCheckedChanged += (_, _) => Change("Toggle effect", p => EditCommands.ChangeLinkedClip(p, p.ActiveSequenceId, cid, c => c.Effects[at].Enabled = toggle.IsChecked == true));
            header.Children.Add(toggle);
            var title = Copy(def!.Name, 12, effect.Enabled ? TextBrush : FaintBrush, FontWeight.SemiBold); Grid.SetColumn(title, 1); header.Children.Add(title);
            var up = ActionButton("up", "Move up", () => Change("Reorder effects", p => EditCommands.ChangeLinkedClip(p, p.ActiveSequenceId, cid, c => { if (at > 0) (c.Effects[at - 1], c.Effects[at]) = (c.Effects[at], c.Effects[at - 1]); })), iconOnly: true);
            up.IsEnabled = at > 0; Grid.SetColumn(up, 2); header.Children.Add(up);
            var reset = ActionButton("undo", "Reset to defaults", () => Change("Reset effect", p => EditCommands.ChangeLinkedClip(p, p.ActiveSequenceId, cid, c => c.Effects[at].Parameters.Clear())), iconOnly: true);
            Grid.SetColumn(reset, 3); header.Children.Add(reset);
            var remove = ActionButton("delete", "Remove effect", () => Change("Remove effect", p => EditCommands.ChangeLinkedClip(p, p.ActiveSequenceId, cid, c => c.Effects.RemoveAt(at))), iconOnly: true);
            Grid.SetColumn(remove, 4); header.Children.Add(remove);
            card.Children.Add(header);
            foreach (var param in def.Params) card.Children.Add(ParamControl(cid, at, param, effect.Parameters.GetValueOrDefault(param.Key) ?? param.DefaultText));
            section.Children.Add(card);
            section.Children.Add(new Border { Height = 1, Background = LineBrush });
        }
    }

    Control ParamControl(Guid clipId, int effectIndex, EffectParam param, string value)
    {
        void Set(string v) => Change("Adjust " + param.Label, p => EditCommands.ChangeLinkedClip(p, p.ActiveSequenceId, clipId, c => c.Effects[effectIndex].Parameters[param.Key] = v));
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*,64"), MinHeight = 28 };
        row.Children.Add(Copy(param.Label, 11.5, SoftTextBrush));
        switch (param.Type)
        {
            case ParamType.Number:
            {
                double current = double.Parse(value, CultureInfo.InvariantCulture);
                var slider = new Slider { Minimum = param.Min, Maximum = param.Max, Value = current, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0) }; StyleSlider(slider);
                var box = new TextBox { Text = current.ToString("0.##", CultureInfo.InvariantCulture), FontSize = 11.5, MinHeight = 26, Height = 26, HorizontalContentAlignment = HorizontalAlignment.Right, Padding = new Thickness(6, 0) };
                ToolTip.SetTip(box, param.Unit.Length > 0 ? $"{param.Label} ({param.Unit})" : param.Label);
                Grid.SetColumn(slider, 1); Grid.SetColumn(box, 2); row.Children.Add(slider); row.Children.Add(box);
                bool commitPending = false;
                slider.ValueChanged += (_, _) => { box.Text = slider.Value.ToString("0.##", CultureInfo.InvariantCulture); commitPending = true; };
                slider.AddHandler(PointerReleasedEvent, (_, _) => { if (commitPending) { commitPending = false; Set(slider.Value.ToString("0.###", CultureInfo.InvariantCulture)); } }, Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, true);
                slider.LostFocus += (_, _) => { if (commitPending) { commitPending = false; Set(slider.Value.ToString("0.###", CultureInfo.InvariantCulture)); } };
                box.LostFocus += (_, _) => { if (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v != current) Set(Math.Clamp(v, param.Min, param.Max).ToString("0.###", CultureInfo.InvariantCulture)); };
                box.KeyDown += (_, k) => { if (k.Key == Key.Enter) Focus(); };
                break;
            }
            case ParamType.Choice:
            {
                var combo = new ComboBox { ItemsSource = param.Choices, SelectedItem = value, FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 26, Margin = new Thickness(6, 0, 0, 0) };
                combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string s && s != value) Set(s); };
                Grid.SetColumn(combo, 1); Grid.SetColumnSpan(combo, 2); row.Children.Add(combo);
                break;
            }
            case ParamType.Color:
            {
                var swatch = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(4), BorderBrush = LineBrush, BorderThickness = new Thickness(1), Background = Brush(value), Margin = new Thickness(6, 0), HorizontalAlignment = HorizontalAlignment.Left };
                var box = new TextBox { Text = value, FontSize = 11.5, MinHeight = 26, Height = 26, Padding = new Thickness(6, 0) };
                box.LostFocus += (_, _) => { var t = (box.Text ?? "").Trim().ToUpperInvariant(); if (!t.StartsWith('#')) t = "#" + t; if (t.Length == 7 && t.Skip(1).All(Uri.IsHexDigit) && t != value) Set(t); };
                Grid.SetColumn(swatch, 1); Grid.SetColumn(box, 2); row.Children.Add(swatch); row.Children.Add(box);
                break;
            }
            case ParamType.File:
            {
                var pick = Button(value.Length == 0 ? "Choose LUT…" : Path.GetFileName(value), () => Run(async () =>
                {
                    var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "Choose a LUT", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("LUT") { Patterns = ["*.cube", "*.3dl"] }] });
                    if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) Set(path);
                }));
                Grid.SetColumn(pick, 1); Grid.SetColumnSpan(pick, 2); pick.Margin = new Thickness(6, 0, 0, 0); row.Children.Add(pick);
                break;
            }
        }
        return row;
    }

    // ---------------- Export frame ----------------
    async Task ExportFrame()
    {
        var sequence = Sequence; if (sequence.Duration == 0) return;
        var file = await StorageProvider.SaveFilePickerAsync(new() { Title = "Export frame", SuggestedFileName = $"{Project.Name} {Clock(playhead).Replace(':', '-')}.png", DefaultExtension = "png", FileTypeChoices = [new FilePickerFileType("PNG") { Patterns = ["*.png"] }] });
        if (file?.TryGetLocalPath() is not { } path) return;
        status.Text = "Exporting frame…";
        await using var still = new MltSession();
        await still.LoadAsync(ProjectCopy, sequence.Width);
        var frame = await still.GetFrameAsync(playhead);
        using var bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using (var buffer = bitmap.Lock()) System.Runtime.InteropServices.Marshal.Copy(frame.Rgba, 0, buffer.Address, Math.Min(frame.Rgba.Length, buffer.RowBytes * frame.Height));
#pragma warning disable CS0618
        bitmap.Save(path);
#pragma warning restore CS0618
        status.Text = $"Frame saved: {path} ({frame.Width}×{frame.Height})";
    }
}

public sealed partial class EditorWindow
{
    static string lastPreset = ExportPresets.All[0].Name;
    bool queueChosen;
    /// <summary>Export dialog caption choice: 0 burn in, 1 .srt sidecar, 2 .vtt sidecar, 3 none.</summary>
    int exportCaptions;
    readonly List<(EditProject Project, string Path, ExportOptions Options)> exportQueue = [];
    /// <summary>Premiere's Export panel, condensed: preset, range (entire sequence or In to Out), and what you'll get.</summary>
    async Task<ExportOptions?> ChooseExport()
    {
        var presets = ExportPresets.All;
        var dialog = new Window { Title = "Export", Width = 520, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false, Background = PanelBrush, Foreground = TextBrush };
        var panel = new StackPanel { Margin = new Thickness(22), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Export", FontSize = 16, FontWeight = FontWeight.SemiBold });
        var presetBox = new ComboBox { ItemsSource = presets.Select(p => $"{p.Group}  ·  {p.Name}").ToList(), SelectedIndex = Math.Max(0, Array.FindIndex(presets, p => p.Name == lastPreset)), HorizontalAlignment = HorizontalAlignment.Stretch };
        bool hasRange = markIn is not null || markOut is not null;
        var rangeBox = new ComboBox { ItemsSource = hasRange ? new[] { "Entire sequence", "In to Out" } : new[] { "Entire sequence" }, SelectedIndex = hasRange ? 1 : 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var info = new TextBlock { FontSize = 12, Foreground = MutedBrush, TextWrapping = TextWrapping.Wrap, LineHeight = 18 };
        void Row(string label, Control control) { var g = new Grid { ColumnDefinitions = new ColumnDefinitions("90,*") }; g.Children.Add(Copy(label, 12, SoftTextBrush)); Grid.SetColumn(control, 1); g.Children.Add(control); panel.Children.Add(g); }
        var kbps = new[] { 0, 96, 128, 192, 256, 320 };
        var audioBox = new ComboBox { ItemsSource = kbps.Select(k => k == 0 ? "Preset default" : $"{k} kbps").ToList(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var rateBox = new ComboBox { ItemsSource = new[] { "48 kHz", "44.1 kHz" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var captionBox = new ComboBox { ItemsSource = new[] { "Burn into video", "Sidecar file (.srt)", "Sidecar file (.vtt)", "None" }, SelectedIndex = exportCaptions, HorizontalAlignment = HorizontalAlignment.Stretch, Name = "export-captions" };
        Row("Preset", presetBox); Row("Range", rangeBox); Row("Captions", captionBox); Row("Audio bitrate", audioBox); Row("Sample rate", rateBox); panel.Children.Add(info);
        (long In, long Out) Range() => rangeBox.SelectedIndex == 1 ? (markIn ?? 0, markOut ?? Sequence.Duration) : (0, Sequence.Duration);
        void Describe()
        {
            var preset = presets[Math.Max(0, presetBox.SelectedIndex)]; var (a, b) = Range(); double seconds = timelineFps.Seconds(Math.Max(1, b - a));
            var (ow, oh) = ExportPresets.OutputSize(preset, Sequence.Width, Sequence.Height);
            string size = preset.Width > 0 ? $"{ow}×{oh}" : preset.AudioOnly ? "audio only" : $"{Sequence.Width}×{Sequence.Height}";
            string target = preset.TargetMegabytes is { } mb ? $" · fits under {mb:0} MB (about {ExportPresets.Resolve(preset, seconds).First(p => p.Key == "vb").Value}bps video)" : "";
            string smart = SmartRender.Check(Project, a, b, out _) is not null ? "\nThis range is one untouched clip: File → Smart export saves it instantly without re-encoding." : "";
            info.Text = $"{preset.Description}\n{size} · {Clock(b - a)} long{target}{smart}";
        }
        presetBox.SelectionChanged += (_, _) => Describe(); rangeBox.SelectionChanged += (_, _) => Describe(); Describe();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        ExportOptions? result = null;
        void Accept()
        {
            var p = presets[Math.Max(0, presetBox.SelectedIndex)]; var (a, b) = Range(); lastPreset = p.Name; exportCaptions = Math.Max(0, captionBox.SelectedIndex);
            // Audio settings override the preset (Premiere's Audio tab): bitrate for lossy codecs, and the sample rate.
            if (audioBox.SelectedIndex > 0) p = p with { AudioKbps = kbps[audioBox.SelectedIndex] };
            p = p with { Properties = [.. p.Properties.Where(x => x.Key != "frequency"), ("frequency", rateBox.SelectedIndex == 1 ? "44100" : "48000")] };
            result = new ExportOptions(p, a, b); dialog.Close();
        }
        var go = Button("Export…", Accept);
        go.Background = AccentBrush; go.Foreground = InkBrush; go.Classes.Add("primary"); go.HorizontalAlignment = HorizontalAlignment.Left;
        var cancel = Button("Cancel", () => dialog.Close()); cancel.HorizontalAlignment = HorizontalAlignment.Left;
        queueChosen = false;
        var queue = Button("Add to queue", () => { queueChosen = true; Accept(); });
        ToolTip.SetTip(queue, "Queue this export and keep editing. Start the queue from File → Export queue.");
        buttons.Children.Add(cancel); buttons.Children.Add(queue); buttons.Children.Add(go); panel.Children.Add(buttons);
        dialog.Content = panel;
        await dialog.ShowDialog(this);
        return result;
    }
}
