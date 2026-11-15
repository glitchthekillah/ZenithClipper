using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;
using Control = Avalonia.Controls.Control;

/// <summary>
/// Track layout modelled on Premiere Pro: video tracks stack upward from the middle (V1 sits just above the divider and
/// higher numbers composite on top), audio tracks stack downward (A1 just below the divider). The edit model keeps its own
/// order; only the display is mirrored.
/// </summary>
public sealed partial class EditorWindow
{
    const double DividerHeight = 6;
    List<EditTrack> DisplayTracks() => [.. Sequence.Tracks.Where(t => t.Kind != TrackKind.Audio).Reverse(), .. Sequence.Tracks.Where(t => t.Kind == TrackKind.Audio)];
    int VideoRows => Sequence.Tracks.Count(t => t.Kind != TrackKind.Audio);
    bool HasDivider => VideoRows > 0 && VideoRows < Sequence.Tracks.Count;
    double RowTop(int row) => RulerHeight + row * TrackHeight + (HasDivider && row >= VideoRows ? DividerHeight : 0);
    double TracksHeight => RowTop(Sequence.Tracks.Count);
    /// <summary>Display row under a y position, or -1 for the ruler, the divider or empty space.</summary>
    int RowAt(double y)
    {
        for (int row = 0; row < Sequence.Tracks.Count; row++) { double top = RowTop(row); if (y >= top && y < top + TrackHeight) return row; }
        return -1;
    }
    /// <summary>Position of a track among tracks of its kind, counted outward from the divider (V1 = 0, A1 = 0).</summary>
    int Ordinal(EditTrack track) => Sequence.Tracks.Where(t => t.Kind == track.Kind).ToList().IndexOf(track);
    static bool Compatible(EditTrack from, EditTrack to) => from.Kind == to.Kind && IsCaptionTrack(from) == IsCaptionTrack(to);

    /// <summary>Headless checks for the Premiere-style layout, linked overwrite moves and typed timecodes.</summary>
    internal void VerifyTimelineEditing()
    {
        var fps = new FrameRate(30, 1);
        var media = new MediaAsset { Name = "clip.mp4", Kind = MediaKind.Video, DurationSeconds = 10, Width = 1920, Height = 1080, Fps = fps, HasAudio = true };
        var other = new MediaAsset { Name = "other.mp4", Kind = MediaKind.Video, DurationSeconds = 10, Width = 1920, Height = 1080, Fps = fps, HasAudio = false };
        var seq = new EditSequence { Fps = fps };
        EditTrack v1 = new() { Name = "Video 1", Kind = TrackKind.Video }, a1 = new() { Name = "Audio 1", Kind = TrackKind.Audio }, v2 = new() { Name = "Video 2", Kind = TrackKind.Video }, a2 = new() { Name = "Audio 2", Kind = TrackKind.Audio };
        seq.Tracks = [v1, a1, v2, a2];
        var project = new EditProject { ActiveSequenceId = seq.Id, Sequences = [seq], Media = [media, other] };
        var placed = EditCommands.AppendMedia(project, seq.Id, media.Id, 0);
        var blocker = new TimelineClip { MediaId = other.Id, Name = "blocker", Start = 60, Duration = 120 }; v2.Clips.Add(blocker);
        // Drag the linked video up one lane and 3 s right: video lands on V2, audio on A2, and the blocker is trimmed, not overlapped.
        EditCommands.MoveOverwrite(project, seq.Id, placed.VideoClipId!.Value, 90, new Dictionary<Guid, Guid> { [placed.VideoClipId!.Value] = v2.Id, [placed.AudioClipId!.Value] = a2.Id });
        var video = v2.Clips.Single(c => c.Id == placed.VideoClipId); var audio = a2.Clips.Single(c => c.Id == placed.AudioClipId);
        if (video.Start != 90 || audio.Start != 90 || v1.Clips.Count != 0 || a1.Clips.Count != 0 || video.LinkId != audio.LinkId || video.LinkId is null)
            throw new InvalidOperationException("Linked clips did not move together to mirrored lanes.");
        if (v2.Clips.Count != 2 || blocker.Duration != 30 || v2.Clips.Any(c => c.Id != video.Id && c.End > video.Start && c.Start < video.End))
            throw new InvalidOperationException("Dropped clip did not overwrite the clip underneath.");
        // Dropping into the middle of a longer clip splits it around the drop.
        var longClip = new TimelineClip { MediaId = other.Id, Name = "long", Start = 0, Duration = 300 }; v1.Clips.Add(longClip);
        var small = new TimelineClip { MediaId = other.Id, Name = "small", Start = 600, Duration = 30 }; v1.Clips.Add(small);
        EditCommands.MoveOverwrite(project, seq.Id, small.Id, 100);
        if (v1.Clips.Count != 3 || v1.Clips[0].End != 100 || v1.Clips[2].Start != 130 || v1.Clips[2].SourceIn != 130)
            throw new InvalidOperationException("Overwrite inside a clip did not split it with the right source offsets.");
        var keep = session; var keepFps = timelineFps;
        try
        {
            session = new EditorSession(project); timelineFps = fps;
            var rows = DisplayTracks();
            if (rows[0].Id != v2.Id || rows[1].Id != v1.Id || rows[2].Id != a1.Id || rows[3].Id != a2.Id || !HasDivider || RowTop(2) != RulerHeight + 2 * TrackHeight + DividerHeight)
                throw new InvalidOperationException("Tracks are not mirrored around the video/audio divider.");
            foreach (var (text, from, expected) in new (string, long, long)[] { ("1:23", 0, 83 * 30), ("00:01:23.500", 0, 83 * 30 + 15), ("1:00:00:12", 0, 3600 * 30 + 12), ("12.5", 0, 375) })
                if (ParseTimecode(text, from) != expected) throw new InvalidOperationException($"Timecode '{text}' parsed as {ParseTimecode(text, from)}, expected {expected}.");
            if (ParseTimecode("abc", 0) is not null || ParseTimecode("1::2", 0) is not null || ParseTimecode("+2s", 0) is not null) throw new InvalidOperationException("Invalid timecode was accepted.");
        }
        finally { session = keep; timelineFps = keepFps; }
        // Crop compiles to a source-pixel crop filter plus a placement rect shrunk by the same fractions.
        if (Environment.GetEnvironmentVariable("ZENITH_DEMO_MEDIA") is { Length: > 0 } demo)
        {
            var cropMedia = new MediaAsset { Name = "c.mp4", Path = Path.Combine(demo, "ow1.mp4"), Kind = MediaKind.Video, DurationSeconds = 20, Width = 1280, Height = 720, Fps = fps };
            var cs = new EditSequence { Fps = fps, Width = 1920, Height = 1080, Tracks = [new EditTrack { Kind = TrackKind.Video, Clips = [new TimelineClip { MediaId = cropMedia.Id, Duration = 30, StreamIntent = ClipStreamIntent.VideoOnly, Transform = new ClipTransform { CropLeft = .25, CropTop = .1 } }] }] };
            var xml = ZenithClipper.Mlt.MltXml.Compile(new EditProject { ActiveSequenceId = cs.Id, Sequences = [cs], Media = [cropMedia] }).ToString();
            if (!xml.Contains("<property name=\"mlt_service\">crop</property>") || !xml.Contains("<property name=\"left\">320</property>") || !xml.Contains("<property name=\"top\">72</property>") || !xml.Contains("480 108 1440 972 1"))
                throw new InvalidOperationException("Crop did not compile to the expected filter and placement.");
        }
        Console.WriteLine("Timeline editing smoke passed: mirrored V/A lanes, linked lane moves, overwrite trim/split, typed timecode jumps.");
    }

    sealed partial class TimelineView : Canvas
    {
        readonly EditorWindow owner;
        double PixelsPerFrame => owner.zoom.Value / owner.timelineFps.Value;
        public double ViewportWidth { get; set; }

        // Drag state. Every clip in the dragged clip's link group follows the pointer together.
        sealed record Member(Guid Clip, EditTrack Track, long Start, long Duration, Border Card);
        List<Member>? dragGroup; Guid dragClip; Point origin; bool moved, scrubbing; long dragDelta; int laneDelta;
        readonly Border snapLine = new() { Width = 1, Background = Brush("#FFD24A"), IsHitTestVisible = false, IsVisible = false };
        readonly Border dragBadge = new() { Background = Brush("#E6101113"), BorderBrush = Brush("#3A3D44"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2), IsHitTestVisible = false, IsVisible = false };
        // Media dragged in from the bin.
        Border? incoming; MediaAsset? incomingMedia;

        public TimelineView(EditorWindow owner)
        {
            this.owner = owner;
            Background = ChromeBrush; ClipToBounds = true;
            PointerPressed += Pressed; PointerMoved += Moved; PointerReleased += Released;
            PointerCaptureLost += (_, _) => { if (dragGroup is not null || scrubbing) { dragGroup = null; scrubbing = false; Rebuild(); } };
            MediaPreviews.Updated += Rebuild;
        }

        public void Rebuild()
        {
            Children.Clear();
            var sequence = owner.Sequence; var media = owner.Project.Media.ToDictionary(m => m.Id); var rows = owner.DisplayTracks();
            var selectedSet = owner.SelectionClosure();
            double fps = owner.timelineFps.Value, ppf = PixelsPerFrame;
            // Always room to keep working past the end, and never shorter than the visible area.
            double contentSeconds = sequence.Duration / fps, spanSeconds = Math.Max(contentSeconds + Math.Max(30, contentSeconds), (ViewportWidth + 1) / owner.zoom.Value);
            Width = Math.Ceiling(spanSeconds * owner.zoom.Value); Height = Math.Max(owner.TracksHeight + TrackHeight, 1);

            Children.Add(new Border { Width = Width, Height = RulerHeight, Background = PanelBrush, BorderBrush = LineBrush, BorderThickness = new Thickness(0, 0, 0, 1), IsHitTestVisible = false });
            double step = RulerStep(owner.zoom.Value, Width / owner.zoom.Value / 400), stepPixels = step * owner.zoom.Value;
            // One element per labelled tick (minor ticks live inside it) keeps the ruler bounded on very long timelines.
            for (double seconds = 0; seconds * owner.zoom.Value < Width; seconds += step)
            {
                var marks = new Canvas { Width = stepPixels, Height = RulerHeight };
                var text = Copy(RulerLabel(seconds, step, fps), 10, FaintBrush); Canvas.SetLeft(text, 4); Canvas.SetTop(text, 6); marks.Children.Add(text);
                if (stepPixels >= 40) for (int minor = 1; minor < 4; minor++) { var m = new Border { Width = 1, Height = 5, Background = LineBrush }; Canvas.SetLeft(m, stepPixels * minor / 4); Canvas.SetTop(m, RulerHeight - 6); marks.Children.Add(m); }
                var tick = new Border { Height = RulerHeight, BorderBrush = BorderLineBrush, BorderThickness = new Thickness(1, 0, 0, 0), IsHitTestVisible = false, Child = marks, ClipToBounds = true };
                SetLeft(tick, seconds * owner.zoom.Value); Children.Add(tick);
            }
            if (owner.HasDivider) { var divider = new Border { Width = Width, Height = DividerHeight, Background = DeepBrush, BorderBrush = BorderLineBrush, BorderThickness = new Thickness(0, 1), IsHitTestVisible = false }; SetTop(divider, owner.RowTop(owner.VideoRows) - DividerHeight); Children.Add(divider); }

            for (int row = 0; row < rows.Count; row++)
            {
                var track = rows[row]; double top = owner.RowTop(row);
                var lane = new Border { Width = Width, Height = TrackHeight, Background = track.Id == owner.selectedTrack ? Brush("#17181B") : ChromeBrush, BorderBrush = ControlBrush, BorderThickness = new Thickness(0, 0, 0, 1), IsHitTestVisible = false, Tag = "lane:" + track.Id };
                SetTop(lane, top); Children.Add(lane);
                foreach (var clip in track.Clips)
                {
                    bool selected = selectedSet.Contains(clip.Id);
                    var card = Card(track, clip, media.GetValueOrDefault(clip.MediaId), selected, ppf, fps);
                    SetLeft(card, clip.Start * ppf); SetTop(card, top + 3); Children.Add(card);
                }
            }
            // Render bar: green where a preview file still matches the sequence.
            foreach (var r in owner.ValidPreviews()) { var done = new Border { Width = Math.Max(2, (r.To - r.From) * ppf), Height = 3, Background = Brush("#3FBF6A"), IsHitTestVisible = false }; SetLeft(done, r.From * ppf); SetTop(done, RulerHeight - 3); Children.Add(done); }
            foreach (var marker in sequence.Markers) { var mark = new Border { Width = 2, Height = RulerHeight, Background = Brush(marker.Color), IsHitTestVisible = false }; SetLeft(mark, marker.Frame * ppf); Children.Add(mark); }
            if (owner.markIn is not null || owner.markOut is not null)
            {
                long a = owner.markIn ?? 0, b = owner.markOut ?? Math.Max(a + 1, sequence.Duration);
                var range = new Border { Width = Math.Max(2, (b - a) * ppf), Height = Height - RulerHeight, Background = Brush("#14FFFFFF"), IsHitTestVisible = false };
                SetLeft(range, a * ppf); SetTop(range, RulerHeight); Children.Add(range);
                var bar = new Border { Width = Math.Max(2, (b - a) * ppf), Height = 5, Background = Brush("#99FFFFFF"), IsHitTestVisible = false, CornerRadius = new CornerRadius(2) };
                SetLeft(bar, a * ppf); SetTop(bar, RulerHeight - 6); Children.Add(bar);
            }
            foreach (var marker in sequence.Markers.Where(m => m.Name.Length > 0))
            {
                var name = new TextBlock { Text = marker.Name, FontSize = 9.5, Foreground = Brush(marker.Color), IsHitTestVisible = false };
                SetLeft(name, marker.Frame * ppf + 4); SetTop(name, 1); Children.Add(name);
            }
            Children.Add(snapLine); Children.Add(dragBadge);
            UpdatePlayhead(); InvalidateVisual();
        }

        /// <summary>Smallest readable tick spacing (at least 70 px) that also keeps the ruler under ~500 labels.</summary>
        static double RulerStep(double pixelsPerSecond, double minimumSeconds)
        {
            foreach (double s in new[] { 1 / 30.0, 1 / 15.0, .2, .5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200, 14400, 28800, 86400 })
                if (s * pixelsPerSecond >= 70 && s >= minimumSeconds) return s;
            return Math.Max(86400, minimumSeconds);
        }
        static string RulerLabel(double seconds, double step, double fps)
        {
            int whole = (int)Math.Floor(seconds + 1e-6);
            string clock = whole >= 3600 ? $"{whole / 3600}:{whole / 60 % 60:00}:{whole % 60:00}" : $"{whole / 60}:{whole % 60:00}";
            if (step >= 1) return clock;
            int frame = (int)Math.Round((seconds - whole) * fps);
            return frame == 0 ? clock : $"{clock}:{frame:00}";
        }

        Border Card(EditTrack track, TimelineClip clip, MediaAsset? media, bool selected, double ppf, double fps)
        {
            bool caption = IsCaptionTrack(track), text = media?.Kind == MediaKind.Text || track.Kind == TrackKind.Video && track.Name.StartsWith("Titles", StringComparison.Ordinal);
            bool audio = track.Kind == TrackKind.Audio;
            double width = Math.Max(4, clip.Duration * ppf), height = TrackHeight - 6;
            string fill = caption ? "#23252A" : text ? "#2A2418" : audio ? "#1C2321" : "#22252B", edge = caption ? "#33363D" : text ? "#6B5320" : audio ? "#2D3A36" : "#3A3D44";
            var body = new Grid();
            double sourceSeconds = clip.SourceIn / fps, secondsPerPixel = clip.Speed / ppf / fps, span = clip.Duration * clip.Speed / fps;
            var preview = media is null ? null : MediaPreviews.Get(media);
            if (!audio && !caption && !text && media is not null)
                body.Children.Add(new Filmstrip { Entry = preview, SourceSeconds = sourceSeconds, SecondsPerPixel = secondsPerPixel, SpanSeconds = span, Reverse = clip.Reverse, Aspect = media.Width > 0 && media.Height > 0 ? (double)media.Width / media.Height : 16 / 9.0, Margin = new Thickness(0, 17, 0, 0) });
            if (audio && media is not null)
            {
                double gain = Math.Pow(10, (clip.GainDb + track.GainDb) / 20);
                body.Children.Add(new Waveform { Entry = preview, SourceSeconds = sourceSeconds, SecondsPerPixel = secondsPerPixel, SpanSeconds = span, Reverse = clip.Reverse, Gain = gain, Fill = selected ? AccentSoftBrush : Brush("#7FC9AE"), Margin = new Thickness(0, 17, 0, 2) });
            }
            // Name bar across the top, like Premiere's clip label.
            var label = new DockPanel { Height = 17, VerticalAlignment = VerticalAlignment.Top, Background = clip.Label is { } tag ? Brush(tag) : Brush(audio ? "#26302D" : text ? "#3A3020" : "#2C3038"), LastChildFill = true };
            if (clip.LinkId is not null && !caption) { var link = Glyph("link", MutedBrush, 11); link.Margin = new Thickness(5, 0, 0, 0); DockPanel.SetDock(link, Dock.Left); label.Children.Add(link); }
            if (clip.Speed != 1 || clip.Reverse) { var speed = Copy((clip.Reverse ? "−" : "") + $"{clip.Speed * 100:0.#}%", 10, AmberBrush); speed.Margin = new Thickness(0, 0, 5, 0); DockPanel.SetDock(speed, Dock.Right); label.Children.Add(speed); }
            var name = Copy(clip.Name.Replace(" · audio", ""), 10.5, caption ? SoftTextBrush : text ? Brush("#F5C56A") : TextBrush, FontWeight.Medium);
            name.Margin = new Thickness(5, 0, 4, 0); name.TextTrimming = TextTrimming.CharacterEllipsis; label.Children.Add(name);
            body.Children.Add(label);
            if (preview is { ThumbsDone: false } && !audio && !text && !caption) body.Children.Add(new Border { Margin = new Thickness(0, 17, 0, 0), Background = Brush("#14FFFFFF") });
            if (clip.HoldFrame is not null) { var hold = Copy("❚❚ Frame hold", 9.5, AmberBrush, FontWeight.SemiBold); hold.Margin = new Thickness(5, 19, 0, 0); hold.VerticalAlignment = VerticalAlignment.Top; hold.HorizontalAlignment = HorizontalAlignment.Left; body.Children.Add(new Border { Background = Brush("#99000000"), CornerRadius = new CornerRadius(3), Margin = new Thickness(3, 19, 0, 0), Padding = new Thickness(4, 1), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = hold }); }
            // Transitions sit on the clip's edges as a hatched block, like Premiere's transition rectangles.
            foreach (var (tr, atHead) in new[] { (clip.TransitionIn, true), (clip.TransitionOut, false) })
            {
                if (tr is null) continue;
                double tw = Math.Min(width, Math.Max(10, tr.Duration * ppf));
                var block = new Border
                {
                    Width = tw, HorizontalAlignment = atHead ? HorizontalAlignment.Left : HorizontalAlignment.Right, Margin = new Thickness(0, 17, 0, 0),
                    Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative), GradientStops = { new GradientStop(Color.Parse(atHead ? "#B04A3BA8" : "#E66E5BD6"), 0), new GradientStop(Color.Parse(atHead ? "#E66E5BD6" : "#B04A3BA8"), 1) } },
                    BorderBrush = Brush("#B9A7FF"), BorderThickness = new Thickness(atHead ? 0 : 1, 0, atHead ? 1 : 0, 0)
                };
                ToolTip.SetTip(block, $"{TransitionCatalog.Find(tr.Kind)?.Name} · {owner.timelineFps.Seconds(tr.Duration):0.##} s");
                if (tw > 46) block.Child = new TextBlock { Text = TransitionCatalog.Find(tr.Kind)?.Name ?? tr.Kind, FontSize = 9, Foreground = Brushes.White, Margin = new Thickness(3, 2), TextTrimming = TextTrimming.CharacterEllipsis };
                body.Children.Add(block);
            }
            if (clip.Motion.Count > 0)
            {
                // Keyframes show as small diamonds along the bottom of the clip.
                var keys = new Canvas { IsHitTestVisible = false, Height = 8, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 2) };
                foreach (long f in clip.Motion.Select(k => k.Frame).Distinct())
                {
                    var d = new Border { Width = 6, Height = 6, Background = Brush("#FFE7D2"), BorderBrush = Brush("#7A3020"), BorderThickness = new Thickness(1), RenderTransform = new RotateTransform(45) };
                    Canvas.SetLeft(d, Math.Clamp(f * ppf - 3, 0, width - 7)); Canvas.SetTop(d, 1); keys.Children.Add(d);
                }
                body.Children.Add(keys);
            }
            if (clip.Effects.Any(x => x.Enabled))
            {
                var fx = new Border { Background = Brush("#CC1A1B1F"), CornerRadius = new CornerRadius(3), Padding = new Thickness(3, 0), Margin = new Thickness(0, 2, 3, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Child = Copy("fx", 9.5, AccentSoftBrush, FontWeight.Bold) };
                ToolTip.SetTip(fx, string.Join(", ", clip.Effects.Where(x => x.Enabled).Select(x => ZenithClipper.Mlt.EffectCatalog.Find(x.Id)?.Name ?? x.Id)));
                if (width > 60) body.Children.Add(fx);
            }
            return new Border
            {
                Width = width, Height = height, Background = Brush(fill), BorderBrush = selected ? AccentBrush : Brush(edge), BorderThickness = new Thickness(selected ? 2 : 1),
                CornerRadius = new CornerRadius(3), Child = body, ClipToBounds = true, Opacity = track.Hidden || track.Muted || !clip.Enabled ? .4 : 1, Tag = clip.Id, Cursor = new Cursor(StandardCursorType.Hand)
            };
        }

        readonly Border playheadLine = new() { Width = 1, Background = AccentBrush, IsHitTestVisible = false };
        readonly Border playheadHead = new() { Width = 11, Height = 12, Background = AccentBrush, CornerRadius = new CornerRadius(2, 2, 6, 6), IsHitTestVisible = false };
        public void UpdatePlayhead()
        {
            if (!Children.Contains(playheadLine)) Children.Add(playheadLine);
            if (!Children.Contains(playheadHead)) Children.Add(playheadHead);
            playheadLine.Height = Height;
            SetLeft(playheadLine, owner.ProgramPlayhead * PixelsPerFrame); SetTop(playheadLine, 0);
            SetLeft(playheadHead, owner.ProgramPlayhead * PixelsPerFrame - 5); SetTop(playheadHead, 0);
        }

        (EditTrack Track, TimelineClip? Clip)? HitTest(Point p)
        {
            int row = owner.RowAt(p.Y); if (row < 0) return null;
            var track = owner.DisplayTracks()[row]; long frame = (long)(p.X / PixelsPerFrame);
            return (track, track.Clips.LastOrDefault(c => frame >= c.Start && frame < c.End));
        }

        void Pressed(object? sender, PointerPressedEventArgs e)
        {
            var p = e.GetPosition(this); var point = e.GetCurrentPoint(this);
            owner.ShowProgram(); // Clicking the timeline makes the Program Monitor active again.
            if (point.Properties.IsRightButtonPressed)
            {
                if (HitTest(p) is not { } hit) return;
                owner.selectedTrack = hit.Track.Id; owner.selectedClip = hit.Clip?.Id; owner.BuildInspector(); Rebuild();
                owner.ShowMenu(this, e, hit.Clip is null ? owner.TrackMenu(hit.Track) : owner.ClipMenu(hit.Track, hit.Clip, owner.Project.Media.FirstOrDefault(m => m.Id == hit.Clip.MediaId)));
                return;
            }
            if (!point.Properties.IsLeftButtonPressed) return;
            // Double-clicking a nested sequence opens it, as in Premiere.
            if (e.ClickCount == 2 && p.Y >= RulerHeight && HitTest(p) is { Clip: { } opened } && owner.Project.Media.FirstOrDefault(m => m.Id == opened.MediaId) is { Kind: MediaKind.Sequence, SequenceId: { } nestedId })
                { owner.OpenSequence(nestedId); e.Handled = true; return; }
            origin = p; moved = false; dragDelta = 0; laneDelta = 0;
            if (ToolPressed(e, p)) return;
            if (p.Y >= RulerHeight && HitTest(p) is { Clip: { } clip } target)
            {
                // Shift/Ctrl+click adds or removes clips; a plain click on an unselected clip selects just it (and its group).
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.KeyModifiers.HasFlag(KeyModifiers.Control)) owner.ToggleSelection(clip.Id);
                else { if (!owner.SelectionClosure().Contains(clip.Id)) owner.ClearExtraSelection(); else if (owner.selectedClip is { } prev && prev != clip.Id) owner.extraSelection.Add(prev); owner.selectedClip = clip.Id; owner.extraSelection.Remove(clip.Id); }
                owner.selectedTrack = target.Track.Id; owner.BuildInspector(); Rebuild();
                if (target.Track.Locked || owner.selectedClip != clip.Id) return;
                var cards = Children.OfType<Border>().Where(b => b.Tag is Guid).ToDictionary(b => (Guid)b.Tag!);
                dragClip = clip.Id;
                var moving = owner.SelectionClosure();
                dragGroup = [.. owner.Sequence.Tracks.SelectMany(t => t.Clips.Select(c => (t, c)))
                    .Where(x => moving.Contains(x.c.Id) && cards.ContainsKey(x.c.Id))
                    .Select(x => new Member(x.c.Id, x.t, x.c.Start, x.c.Duration, cards[x.c.Id]))];
                foreach (var m in dragGroup) m.Card.ZIndex = 10;
                e.Pointer.Capture(this); e.Handled = true;
                return;
            }
            // Ruler or empty lane: scrub. The playhead follows the pointer for as long as the button is held.
            if (HitTest(p) is { } lane) { owner.selectedTrack = lane.Track.Id; owner.selectedClip = null; owner.ClearExtraSelection(); owner.BuildInspector(); Rebuild(); }
            scrubbing = true; e.Pointer.Capture(this); owner.Seek(Math.Max(0, (long)Math.Round(p.X / PixelsPerFrame))); e.Handled = true;
        }

        void Moved(object? sender, PointerEventArgs e)
        {
            var p = e.GetPosition(this);
            if (scrubbing) { owner.Seek(Math.Max(0, (long)Math.Round(p.X / PixelsPerFrame))); return; }
            if (ToolMoved(e, p)) return;
            if (dragGroup is null) return;
            moved |= Math.Abs(p.X - origin.X) > 3 || Math.Abs(p.Y - origin.Y) > 3;
            if (!moved) return;
            double ppf = PixelsPerFrame; var lead = dragGroup.First(m => m.Clip == dragClip);
            long delta = (long)Math.Round((p.X - origin.X) / ppf);
            delta = Math.Max(delta, -dragGroup.Min(m => m.Start));
            snapLine.IsVisible = false;
            if (owner.snap.IsChecked == true)
            {
                var ids = dragGroup.Select(m => m.Clip).ToHashSet();
                var points = SnapPoints(ids);
                long best = 0; double bestDistance = 10; long? snapAt = null;
                foreach (var m in dragGroup)
                    foreach (long edge in new[] { m.Start + delta, m.Start + m.Duration + delta })
                        foreach (long target in points)
                        { double d = Math.Abs(target - edge) * ppf; if (d < bestDistance) { bestDistance = d; best = target - edge; snapAt = target; } }
                if (snapAt is { } at) { delta += best; snapLine.IsVisible = true; snapLine.Height = Height; SetLeft(snapLine, at * ppf); SetTop(snapLine, 0); }
            }
            dragDelta = delta;
            // Lane change: the grabbed clip goes to the compatible lane under the pointer; linked partners shift the same number
            // of lanes on their side of the divider, so video and audio stay mirrored the way Premiere moves them.
            int row = owner.RowAt(p.Y); var rows = owner.DisplayTracks();
            if (row >= 0 && Compatible(lead.Track, rows[row]) && !rows[row].Locked)
                laneDelta = owner.Ordinal(rows[row]) - owner.Ordinal(lead.Track);
            laneDelta = ClampLaneDelta(laneDelta);
            foreach (var m in dragGroup)
            {
                var lane = LaneFor(m);
                SetLeft(m.Card, (m.Start + delta) * ppf); SetTop(m.Card, owner.RowTop(rows.IndexOf(lane)) + 3);
                m.Card.Opacity = .92; m.Card.BoxShadow = BoxShadows.Parse("0 4 12 0 #80000000");
            }
            double shift = owner.timelineFps.Seconds(Math.Abs(delta));
            dragBadge.Child = Copy((delta < 0 ? "−" : "+") + (shift >= 60 ? $"{(int)shift / 60}:{shift % 60:00.00}" : $"{shift:0.00}s"), 10.5, TextBrush, FontWeight.Medium);
            dragBadge.IsVisible = delta != 0; SetLeft(dragBadge, (lead.Start + delta) * ppf); SetTop(dragBadge, Canvas.GetTop(lead.Card) + TrackHeight - 2);
        }

        EditTrack LaneFor(Member m)
        {
            var same = owner.Sequence.Tracks.Where(t => Compatible(m.Track, t)).ToList(); int index = same.IndexOf(m.Track) + laneDelta;
            return index >= 0 && index < same.Count && !same[index].Locked ? same[index] : m.Track;
        }
        int ClampLaneDelta(int delta)
        {
            // Never split a linked pair: the shift must be possible for every member.
            while (delta != 0 && dragGroup!.Any(m => { var same = owner.Sequence.Tracks.Where(t => Compatible(m.Track, t)).ToList(); int i = same.IndexOf(m.Track) + delta; return i < 0 || i >= same.Count || same[i].Locked; }))
                delta -= Math.Sign(delta);
            return delta;
        }

        void Released(object? sender, PointerReleasedEventArgs e)
        {
            if (ToolReleased(e)) return;
            if (scrubbing) { scrubbing = false; e.Pointer.Capture(null); return; }
            if (dragGroup is { } group && moved && (dragDelta != 0 || laneDelta != 0))
            {
                var lanes = group.ToDictionary(m => m.Clip, m => LaneFor(m).Id);
                var lead = group.First(m => m.Clip == dragClip); long start = lead.Start + dragDelta; var clipId = dragClip; var leadLane = lanes[clipId];
                var others = group.Select(m => m.Clip).ToList(); long delta = dragDelta;
                dragGroup = null;
                if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
                {
                    // Alt+drag duplicates (Premiere): copies land where the drag ended and overwrite; the originals stay.
                    var source = owner.Sequence; var clipboard = group.Select(m => (lanes[m.Clip], source.Tracks.SelectMany(t => t.Clips).First(c => c.Id == m.Clip))).Select(x => (x.Item1, Clone(x.Item2, delta))).ToList();
                    long at = clipboard.Min(x => x.Item2.Start); List<Guid> created = [];
                    owner.Change(group.Count > 1 ? "Duplicate clips" : "Duplicate clip", project => created = EditCommands.Paste(project, project.ActiveSequenceId, clipboard, at, insert: false));
                    if (created.Count > 0) { owner.ClearExtraSelection(); owner.selectedClip = created[0]; foreach (var c in created.Skip(1)) owner.extraSelection.Add(c); owner.selectedTrack = leadLane; }
                }
                else owner.Change(group.Count > 1 ? "Move clips" : "Move clip", project => { EditCommands.MoveOverwrite(project, project.ActiveSequenceId, clipId, start, lanes, others); owner.selectedTrack = leadLane; });
            }
            dragGroup = null; snapLine.IsVisible = false; dragBadge.IsVisible = false;
            e.Pointer.Capture(null); Rebuild();
        }

        static TimelineClip Clone(TimelineClip clip, long delta) { var copy = ProjectValidation.Clone(clip); copy.Start = Math.Max(0, clip.Start + delta); return copy; }

        // ---- Drag from the media bin ----
        internal double? TestGhostTop => incoming is null ? null : GetTop(incoming);
        public void DragMediaOver(MediaAsset media, Point p)
        {
            incomingMedia = media; double ppf = PixelsPerFrame;
            var lane = LaneForMedia(media, p);
            if (incoming is null || !Children.Contains(incoming))
            {
                incoming = new Border { Height = TrackHeight - 6, Background = AccentGhostBrush, BorderBrush = AccentBrush, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(3), IsHitTestVisible = false, ZIndex = 20, Padding = new Thickness(6, 2), Child = Copy(media.Name, 10.5, TextBrush, FontWeight.Medium) };
                Children.Add(incoming);
            }
            incoming.Width = Math.Max(8, owner.Sequence.Fps.Frames(media.DurationSeconds) * ppf);
            SetLeft(incoming, Math.Max(0, p.X - 12)); SetTop(incoming, lane is null ? Math.Max(RulerHeight, p.Y - TrackHeight / 2) : owner.RowTop(owner.DisplayTracks().IndexOf(lane)) + 3);
        }
        public void DropMedia(Point? p)
        {
            if (incoming is not null) Children.Remove(incoming);
            incoming = null; var media = incomingMedia; incomingMedia = null;
            if (media is null || p is not { } at || at.X < 0 || at.Y < 0 || at.X > Bounds.Width || at.Y > Bounds.Height) return;
            long start = Math.Max(0, (long)Math.Round((at.X - 12) / PixelsPerFrame));
            var lane = LaneForMedia(media, at);
            owner.Change("Add " + media.Name, project =>
            {
                var s = project.Sequences.First(x => x.Id == project.ActiveSequenceId);
                var result = EditCommands.AppendMedia(project, project.ActiveSequenceId, media.Id, start, lane?.Id);
                var lanes = new Dictionary<Guid, Guid>();
                // Linked audio lands on the audio track that mirrors the chosen video track (V2 → A2), when there is one.
                if (lane is not null && result.VideoClipId is { } v && result.AudioClipId is { } a)
                {
                    lanes[v] = lane.Id;
                    var video = s.Tracks.Where(t => t.Kind == TrackKind.Video).ToList(); var audio = s.Tracks.Where(t => t.Kind == TrackKind.Audio && !t.Locked).ToList();
                    int i = video.FindIndex(t => t.Id == lane.Id); if (i >= 0 && i < audio.Count) lanes[a] = audio[i].Id;
                }
                EditCommands.MoveOverwrite(project, project.ActiveSequenceId, result.SelectedClipId, start, lanes);
                owner.selectedClip = result.SelectedClipId; owner.selectedTrack = result.SelectedTrackId;
            });
        }
        EditTrack? LaneForMedia(MediaAsset media, Point p)
        {
            var kind = media.Kind == MediaKind.Audio ? TrackKind.Audio : TrackKind.Video; int row = owner.RowAt(p.Y); var rows = owner.DisplayTracks();
            if (row >= 0 && rows[row].Kind == kind && !rows[row].Locked && !IsCaptionTrack(rows[row])) return rows[row];
            return owner.Sequence.Tracks.FirstOrDefault(t => t.Kind == kind && !t.Locked && !IsCaptionTrack(t));
        }
    }
}
