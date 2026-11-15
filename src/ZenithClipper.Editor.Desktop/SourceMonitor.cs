using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;

/// <summary>
/// Premiere's Source Monitor, sharing the preview with the Program Monitor: double-click media to load it, mark In/Out,
/// then Insert (,) or Overwrite (.) it into the sequence at the program playhead on the targeted tracks.
/// </summary>
public sealed partial class EditorWindow
{
    bool sourceMode, sourceEditing, effectsBypassed, useProxies = true;
    /// <summary>Source patching: target tracks for Insert/Overwrite (null = automatic, Guid.Empty = off).</summary>
    internal Guid? targetVideo, targetAudio;
    Guid? sourceMedia;
    long sourcePlayhead, programPlayhead;
    internal long? sourceIn, sourceOut;
    readonly StackPanel monitorTabs = new() { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>The program playhead, even while the shared monitor shows the source.</summary>
    internal long ProgramPlayhead => sourceMode ? programPlayhead : playhead;
    long MonitorDuration => sourceMode ? SourceLength : Sequence.Duration;
    long SourceLength => Project.Media.FirstOrDefault(m => m.Id == sourceMedia) is { } m ? Math.Max(1, Sequence.Fps.Frames(m.SubclipOut - m.SubclipIn ?? m.DurationSeconds)) : 0;
    /// <summary>Frames before the source monitor's 0 (a subclip starts part-way into its file).</summary>
    long SourceOffset => Project.Media.FirstOrDefault(m => m.Id == sourceMedia)?.SubclipIn is { } a ? Sequence.Fps.Frames(a) : 0;

    /// <summary>Ctrl+U: a new bin item covering the Source Monitor's In/Out.</summary>
    void MakeSubclip()
    {
        if (sourceMedia is not { } id || Project.Media.FirstOrDefault(m => m.Id == id) is not { } media) { status.Text = "Open a clip in the Source Monitor and mark In/Out first."; return; }
        if (media.Kind is not (MediaKind.Video or MediaKind.Audio)) { status.Text = "Subclips work on video and audio files."; return; }
        long length = SourceLength, a = Math.Clamp(sourceIn ?? 0, 0, length - 1), b = Math.Clamp(sourceOut ?? length, a + 1, length);
        double fps = Sequence.Fps.Value, offset = media.SubclipIn ?? 0;
        var sub = ProjectValidation.Clone(media); sub.Id = Guid.NewGuid(); sub.SubclipIn = offset + a / fps; sub.SubclipOut = offset + b / fps;
        sub.Name = $"{media.Name} · {Clock(a)}-{Clock(b)}";
        Change("Make subclip", p => p.Media.Add(sub));
        status.Text = $"Subclip \"{sub.Name}\" added to the media bin.";
    }

    /// <summary>What the preview engine plays: the sequence, or a one-clip scratch sequence for the source media.</summary>
    EditProject PreviewProject()
    {
        var project = ProjectCopy;
        // Rendered preview files stand in for their ranges (checked before proxies swap any paths).
        if (!sourceMode && !effectsBypassed) PreviewRenders.Substitute(project, project.ActiveSequenceId);
        // Program monitor "Global FX mute": preview without clip effects to compare before/after (export unaffected).
        if (useProxies)
            foreach (var m in project.Media.Where(m => m.ProxyPath is { } x && File.Exists(x)))
            { m.Path = m.ProxyPath!; if (m.ProxyWidth > 0) { m.Width = m.ProxyWidth; m.Height = m.ProxyHeight; } }
        if (effectsBypassed) foreach (var c in project.Sequences.SelectMany(s => s.Tracks).SelectMany(t => t.Clips)) foreach (var e in c.Effects) e.Enabled = false;
        if (!sourceMode || sourceMedia is not { } id || project.Media.All(m => m.Id != id)) return project;
        var active = project.Sequences.First(s => s.Id == project.ActiveSequenceId);
        var scratch = new EditSequence { Name = "Source", Width = active.Width, Height = active.Height, Fps = active.Fps, Tracks = [new EditTrack { Name = "V", Kind = TrackKind.Video }, new EditTrack { Name = "A", Kind = TrackKind.Audio }] };
        project.Sequences.Add(scratch); project.ActiveSequenceId = scratch.Id;
        EditCommands.AppendMedia(project, scratch.Id, id, 0);
        return project;
    }

    void BuildMonitorTabs()
    {
        monitorTabs.Children.Clear();
        var media = Project.Media.FirstOrDefault(m => m.Id == sourceMedia);
        foreach (var (name, source) in new[] { ("Source", true), ("Program", false) })
        {
            bool on = source == sourceMode;
            var tab = Button(source && media is not null ? "Source: " + media.Name : name, () => { if (source) ShowSource(); else ShowProgram(); });
            tab.Height = 24; tab.Padding = new Thickness(9, 0); tab.FontSize = 12; tab.MaxWidth = 260; tab.HorizontalAlignment = HorizontalAlignment.Left;
            tab.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal; tab.Foreground = on ? TextBrush : MutedBrush;
            tab.Classes.Add("subtle"); tab.Tag = on ? SelectedBrush : null; tab.Background = on ? SelectedBrush : Brushes.Transparent;
            tab.IsEnabled = !source || media is not null;
            ToolTip.SetTip(tab, source ? "Source Monitor · double-click media to load it. I/O mark, , insert, . overwrite" : "Program Monitor · the open sequence");
            monitorTabs.Children.Add(tab);
        }
    }

    internal void OpenInSource(Guid mediaId)
    {
        if (sourceMedia != mediaId) { sourceMedia = mediaId; sourcePlayhead = 0; sourceIn = sourceOut = null; }
        sourceMode = false; ShowSource();
    }

    void ShowSource()
    {
        if (sourceMode || sourceMedia is null || Project.Media.All(m => m.Id != sourceMedia)) { BuildMonitorTabs(); return; }
        programPlayhead = playhead; sourceMode = true; playhead = Math.Min(sourcePlayhead, Math.Max(0, SourceLength - 1));
        MonitorChanged();
    }

    internal void ShowProgram()
    {
        if (!sourceMode) return;
        sourcePlayhead = playhead; sourceMode = false; playhead = programPlayhead;
        MonitorChanged();
    }

    void MonitorChanged()
    {
        BuildMonitorTabs();
        status.Text = sourceMode ? SourceStatus() : "Program Monitor";
        UpdateChrome();
        Refresh(true);
    }

    string SourceStatus() => $"Source · In {(sourceIn is { } i ? Clock(i) : "—")} · Out {(sourceOut is { } o ? Clock(o) : "—")} · , insert · . overwrite";

    /// <summary>Keys while the Source Monitor is showing. Transport keys drive the source; anything else returns to Program.</summary>
    bool HandleSourceKey(KeyEventArgs e)
    {
        if (!sourceMode) return false;
        var mods = e.KeyModifiers; bool ctrl = mods.HasFlag(KeyModifiers.Control) || mods.HasFlag(KeyModifiers.Meta), shift = mods.HasFlag(KeyModifiers.Shift), alt = mods.HasFlag(KeyModifiers.Alt);
        switch (e.Key)
        {
            case Key.I when !ctrl && !alt && !shift: sourceIn = playhead; if (sourceOut <= sourceIn) sourceOut = null; status.Text = SourceStatus(); return true;
            case Key.O when !ctrl && !alt && !shift: sourceOut = playhead + 1; if (sourceIn >= sourceOut) sourceIn = null; status.Text = SourceStatus(); return true;
            case Key.I when alt: sourceIn = null; status.Text = SourceStatus(); return true;
            case Key.O when alt: sourceOut = null; status.Text = SourceStatus(); return true;
            case Key.X when alt: sourceIn = sourceOut = null; status.Text = SourceStatus(); return true;
            case Key.I when shift: if (sourceIn is { } a) Seek(a); return true;
            case Key.O when shift: if (sourceOut is { } b) Seek(b - 1); return true;
            case Key.U when ctrl && !alt && !shift: MakeSubclip(); return true;
            case Key.OemComma when !ctrl && !alt: SourceEdit(insert: true); return true;
            case Key.OemPeriod when !ctrl && !alt: SourceEdit(insert: false); return true;
            case Key.Space or Key.J or Key.K or Key.L or Key.Left or Key.Right or Key.Home or Key.End: return false;
        }
        if (e.Key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt) return false;
        ShowProgram();
        return false;
    }

    /// <summary>Three-point edit: source In/Out (or the whole clip) placed at the program In (or playhead).</summary>
    void SourceEdit(bool insert)
    {
        if (sourceMedia is not { } id) { status.Text = "Double-click media to open it in the Source Monitor first."; return; }
        long length = SourceLength, a = Math.Clamp(sourceIn ?? 0, 0, length - 1), b = Math.Clamp(sourceOut ?? length, a + 1, length);
        long at = markIn ?? ProgramPlayhead;
        var sequence = Sequence;
        // Targeted tracks win (Guid.Empty = that kind is switched off); otherwise the selected or first unlocked track.
        EditTrack? Pick(TrackKind kind, Guid? target) => target == Guid.Empty ? null
            : sequence.Tracks.FirstOrDefault(t => t.Id == target && !t.Locked) ?? sequence.Tracks.FirstOrDefault(t => t.Id == selectedTrack && t.Kind == kind && !t.Locked) ?? sequence.Tracks.FirstOrDefault(t => t.Kind == kind && !t.Locked);
        var video = Pick(TrackKind.Video, targetVideo); var audio = Pick(TrackKind.Audio, targetAudio);
        // Build the clips in a scratch sequence exactly as an append would, then trim them to the marks.
        var scratchProject = ProjectCopy; var scratch = new EditSequence { Fps = sequence.Fps, Width = sequence.Width, Height = sequence.Height, Tracks = [new EditTrack { Kind = TrackKind.Video }, new EditTrack { Kind = TrackKind.Audio }] };
        scratchProject.Sequences.Add(scratch);
        EditCommands.AppendMedia(scratchProject, scratch.Id, id, 0);
        var clipboard = new List<(Guid, TimelineClip)>();
        foreach (var track in scratch.Tracks)
        foreach (var clip in track.Clips)
        {
            var target = track.Kind == TrackKind.Video ? video : audio;
            if (target is null) continue;
            clip.SourceIn = SourceOffset + a; clip.Duration = b - a; clip.Start = 0;
            clipboard.Add((target.Id, clip));
        }
        if (clipboard.Count == 0) { status.Text = "Unlock a video or audio track to edit into."; return; }
        if (clipboard.Count == 1 && clipboard[0].Item2.LinkId is not null) clipboard[0].Item2.LinkId = null;
        List<Guid> created = [];
        sourceEditing = true;
        try { Change(insert ? "Insert" : "Overwrite", p => created = EditCommands.Paste(p, p.ActiveSequenceId, clipboard, at, insert)); }
        finally { sourceEditing = false; }
        if (created.Count == 0) return;
        // Like Premiere, the program playhead moves to the end of the new clip.
        long end = at + (b - a);
        if (sourceMode) programPlayhead = end; else Seek(end);
        selectedClip = created[0]; timeline.Rebuild();
        status.Text = $"{(insert ? "Inserted" : "Overwrote")} {Clock(b - a)} at {Clock(at)}.";
    }
}

public sealed partial class EditorWindow
{
    /// <summary>Source Monitor three-point edits and sequence tabs, run headlessly against a scratch project.</summary>
    internal void VerifySourceMonitor()
    {
        var fps = new FrameRate(30, 1);
        var media = new MediaAsset { Name = "src", Kind = MediaKind.Video, DurationSeconds = 20, Width = 1920, Height = 1080, Fps = fps, HasAudio = true, Path = "/nonexistent.mp4" };
        var seq = new EditSequence { Fps = fps, Name = "Main", Tracks = [new EditTrack { Name = "Video 1", Kind = TrackKind.Video }, new EditTrack { Name = "Audio 1", Kind = TrackKind.Audio }] };
        var project = new EditProject { ActiveSequenceId = seq.Id, Sequences = [seq], Media = [media] };
        EditCommands.AppendMedia(project, seq.Id, media.Id, 0); foreach (var c in seq.Tracks.SelectMany(t => t.Clips)) c.Duration = 300;
        var keep = session; var keepFps = timelineFps; var keepPlayhead = playhead; var keepIn = markIn; var keepOut = markOut; var keepSel = selectedClip; var keepTrack = selectedTrack;
        try
        {
            session = new EditorSession(project); timelineFps = fps; markIn = markOut = null; selectedClip = null; selectedTrack = null;
            playhead = 100;
            OpenInSource(media.Id);
            if (!sourceMode || ProgramPlayhead != 100 || playhead != 0) throw new InvalidOperationException("Source Monitor did not take over the shared preview.");
            playhead = 30; HandleTestKey(Key.I); playhead = 89; HandleTestKey(Key.O);
            if (sourceIn != 30 || sourceOut != 90) throw new InvalidOperationException("Source In/Out were not marked.");
            HandleTestKey(Key.OemComma);
            var s = Sequence; var v = s.Tracks[0].Clips; var a = s.Tracks[1].Clips;
            if (!sourceMode || v.Count != 3 || a.Count != 3 || v[1].Start != 100 || v[1].Duration != 60 || v[1].SourceIn != 30 || v[2].Start != 160 || a[1].LinkId != v[1].LinkId || v[1].LinkId is null || ProgramPlayhead != 160)
                throw new InvalidOperationException("Insert from Source did not split, ripple and link correctly.");
            HandleTestKey(Key.OemPeriod);
            s = Sequence; v = s.Tracks[0].Clips;
            if (v.Count != 4 || v.Sum(c => c.Duration) != 360 || v[2].Start != 160 || v[2].SourceIn != 30 || v.Max(c => c.End) != 360)
                throw new InvalidOperationException("Overwrite from Source changed the sequence length.");
            HandleTestKey(Key.K); HandleTestKey(Key.Delete);
            if (sourceMode) throw new InvalidOperationException("A timeline key did not return to the Program Monitor.");
            // Sequence tabs: new sequence opens empty; switching back keeps the original edit.
            NewSequence();
            if (Project.Sequences.Count != 2 || Sequence.Tracks.Any(t => t.Clips.Count > 0)) throw new InvalidOperationException("New sequence did not open empty.");
            UpdateSequenceTabs(); if (sequenceTabs.Children.Count != 3) throw new InvalidOperationException("Sequence tabs did not list both sequences.");
            OpenSequence(seq.Id); if (Sequence.Tracks[0].Clips.Count != 4) throw new InvalidOperationException("Switching sequences lost edits.");
        }
        finally
        {
            sourceMode = false; sourceMedia = null; sourceIn = sourceOut = null; session = keep; timelineFps = keepFps; playhead = keepPlayhead; markIn = keepIn; markOut = keepOut; selectedClip = keepSel; selectedTrack = keepTrack;
            sequenceTabsKey = ""; BuildMonitorTabs(); Refresh();
        }
        Console.WriteLine("Source Monitor smoke passed: In/Out, insert, overwrite, return to Program, sequence tabs.");
    }
    void HandleTestKey(Key key) => HandlePremiereKey(new KeyEventArgs { Key = key, KeyModifiers = KeyModifiers.None, RoutedEvent = InputElement.KeyDownEvent });
}
