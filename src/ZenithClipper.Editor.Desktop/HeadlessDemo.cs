using Avalonia.Controls;
using Avalonia.VisualTree;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;

public sealed partial class EditorWindow
{
    internal void PopulateHeadlessDemo()
    {
        var project = NewProject();
        project.Name = "Overwatch highlights";
        var sequence = project.Sequences[0];
        sequence.Fps = new FrameRate(60000, 1001);
        var interview = new MediaAsset { Name = "Overwatch 21:14.mp4", Kind = MediaKind.Video, DurationSeconds = 20, Width = 2560, Height = 1600, Fps = sequence.Fps, HasAudio = true };
        var broll = new MediaAsset { Name = "Overwatch 21:02.mp4", Kind = MediaKind.Video, DurationSeconds = 8, Width = 2560, Height = 1600, Fps = sequence.Fps, HasAudio = true };
        var music = new MediaAsset { Name = "Discord voice.wav", Kind = MediaKind.Audio, DurationSeconds = 30, HasAudio = true };
        var title = new MediaAsset { Name = "TRIPLE KILL", Text = "TRIPLE KILL", Kind = MediaKind.Text, DurationSeconds = 5, Width = 400, Height = 100 };
        project.Media = [interview, broll, music, title];
        // Optional real files so the snapshot shows true filmstrips and waveforms.
        if (Environment.GetEnvironmentVariable("ZENITH_DEMO_MEDIA") is { Length: > 0 } demo)
        { interview.Path = Path.Combine(demo, "ow1.mp4"); broll.Path = Path.Combine(demo, "ow2.mp4"); music.Path = Path.Combine(demo, "voice.wav"); }
        var first = new TimelineClip
        {
            Name = interview.Name, MediaId = interview.Id, Duration = sequence.Fps.Frames(7), GainDb = -1.5,
            Effects = [new ClipEffect { Id = "brightness", Parameters = new() { ["level"] = "1", ["alpha"] = "1" }, Keyframes = [new() { Parameter = "level", Frame = 0, Value = .7 }, new() { Parameter = "level", Frame = sequence.Fps.Frames(7) - 1, Value = 1 }] }]
        };
        sequence.Tracks[0].Clips = [first, new TimelineClip { Name = broll.Name, MediaId = broll.Id, Start = sequence.Fps.Frames(8), SourceIn = sequence.Fps.Frames(1), Duration = sequence.Fps.Frames(5) }];
        var link = Guid.NewGuid(); first.LinkId = first.SourcePairId = link; first.StreamIntent = ClipStreamIntent.VideoOnly;
        sequence.Tracks[1].Clips = [new TimelineClip { Name = interview.Name + " · audio", MediaId = interview.Id, Duration = first.Duration, GainDb = -1.5, LinkId = link, SourcePairId = link, StreamIntent = ClipStreamIntent.AudioOnly }];
        sequence.Tracks.Add(new EditTrack { Name = "Audio 2", Kind = TrackKind.Audio, Clips = [new TimelineClip { Name = music.Name, MediaId = music.Id, Start = sequence.Fps.Frames(2), Duration = sequence.Fps.Frames(18), GainDb = -4 }] });
        sequence.Tracks.Add(new EditTrack { Name = "Titles", Kind = TrackKind.Video, Clips = [TextClip(title, sequence, sequence.Fps.Frames(1), sequence.Fps.Frames(4), false)] });
        var captionTrack = new EditTrack { Name = AutoCaptionTrack, Kind = TrackKind.Video };
        foreach (var (text, from, to) in new[] { ("ok ok ok", 0.4, 1.6), ("he is one shot", 1.8, 3.2), ("wait for it", 3.4, 4.6), ("push now, they're split", 4.8, 6.6), ("LETS GO", 6.8, 7.6) })
        {
            var line = new MediaAsset { Name = text, Text = text, Kind = MediaKind.Text, DurationSeconds = to - from, Width = 600, Height = 90 };
            project.Media.Add(line);
            captionTrack.Clips.Add(TextClip(line, sequence, sequence.Fps.Frames(from), sequence.Fps.Frames(to - from), true));
        }
        sequence.Tracks.Add(captionTrack);
        // Premiere layer on show: a dissolve into the second shot, a crossfade on the music, a graded first clip, In/Out, a label.
        sequence.Tracks[0].Clips[1].Start = first.End; sequence.Tracks[0].Clips[1].TransitionIn = new ClipTransition { Kind = "cross_dissolve", Duration = sequence.Fps.Frames(1) };
        sequence.Tracks[0].Clips[1].Label = "#3C9AD6";
        first.Effects.Add(new ClipEffect { Id = "lumetri_basic", Parameters = new() { ["temperature"] = "18", ["contrast"] = "22", ["saturation"] = "115" } });
        first.Effects.Add(new ClipEffect { Id = "vignette", Parameters = new() { ["amount"] = "35" } });
        sequence.Tracks[2].Clips[0].TransitionIn = new ClipTransition { Kind = "constant_power", Duration = sequence.Fps.Frames(1) };
        sequence.Markers.Add(new SequenceMarker { Frame = sequence.Fps.Frames(6), Name = "Ace" });
        markIn = sequence.Fps.Frames(2); markOut = sequence.Fps.Frames(5);
        first.Motion.Add(new Keyframe { Parameter = "scale_x", Frame = 0, Value = 1 }); first.Motion.Add(new Keyframe { Parameter = "scale_x", Frame = sequence.Fps.Frames(4), Value = 1.2 });
        first.Motion.Add(new Keyframe { Parameter = "scale_y", Frame = 0, Value = 1 }); first.Motion.Add(new Keyframe { Parameter = "scale_y", Frame = sequence.Fps.Frames(4), Value = 1.2 });
        sequence.Tracks[0].Clips[1].BlendMode = "screen";
        sequence.Tracks[2].Pan = -.3;
        if (Environment.GetEnvironmentVariable("ZENITH_DEMO_TRANSFORM") is not null) inspectorSections["Transform"] = true;
        if (Environment.GetEnvironmentVariable("ZENITH_DEMO_TAB") is { } tab) { libraryFilter = tab; inspectorTab = tab == "Effects" ? "Effects" : inspectorTab; }
        if (Environment.GetEnvironmentVariable("ZENITH_DEMO_CROP") is not null) { first.Transform.CropLeft = .12; first.Transform.CropRight = .2; first.Transform.CropTop = .08; inspectorSections["Crop"] = true; cropOverlay.Active = true; }
        session = new EditorSession(project);
        timelineFps = sequence.Fps;
        selectedClip = first.Id;
        selectedTrack = sequence.Tracks[0].Id;
        playhead = sequence.Fps.Frames(4);
        Title = project.Name;
        
        sequence.Width = 2560; sequence.Height = 1600; sequence.Name = "Highlights";
        session = new EditorSession(project);
        UpdateChrome();
        BuildTrackHeaders();
        timeline.Rebuild();
        BuildInspector();
        UpdateTime();
        status.Text = "Layout preview: sample clips only. No media playback or audio loaded.";
        if (!new[]{"property-Timeline-start","property-Source-in","property-Duration","property-Speed","property-Scale-width","property-Opacity"}.All(name=>inspector.GetVisualDescendants().OfType<TextBox>().Any(box=>box.Name==name)) || timeline.Children.Count < 5)
            throw new InvalidOperationException("The populated headless fixture did not build clip controls.");
        if (Environment.GetEnvironmentVariable("ZENITH_DEMO_OPEN") is { } openSection) { inspectorSections[openSection] = true; inspectorSections["Timing"] = false; BuildInspector(); }
        if (Environment.GetEnvironmentVariable("ZENITH_DEMO_FIND") is { } find && propertiesScroller is not null)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (inspector.GetVisualDescendants().OfType<Avalonia.Controls.Control>().FirstOrDefault(c => c.Name == find) is { } target) target.BringIntoView(); }, Avalonia.Threading.DispatcherPriority.Background);
        if (Environment.GetEnvironmentVariable("ZENITH_DEMO_DOCK_LEFT") is { } dockLeft) MovePanel(dockLeft, "left");
        if (Environment.GetEnvironmentVariable("ZENITH_DEMO_RIGHT") is { } right)
        {
            rightPanel = right; rebuildRightTabs?.Invoke();
            if (right == "Scopes" && Environment.GetEnvironmentVariable("ZENITH_DEMO_FRAME") is { } png)
            {
                // Raw test frame: "W H\n" followed by RGBA bytes.
                var raw = File.ReadAllBytes(png); int nl = Array.IndexOf(raw, (byte)'\n'); var dims = System.Text.Encoding.ASCII.GetString(raw, 0, nl).Split(' ');
                var size = new Avalonia.PixelSize(int.Parse(dims[0]), int.Parse(dims[1])); var rgba = raw[(nl + 1)..];
                scopes.Update(rgba, size.Width, size.Height);
            }
        }
    }
}
