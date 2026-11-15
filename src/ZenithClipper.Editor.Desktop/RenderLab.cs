using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;

/// <summary>
/// Developer harness: writes small MLT projects (one per feature) so each engine feature can be rendered with melt and
/// checked frame by frame. Run with --mlt-lab &lt;output dir&gt; &lt;media dir containing ow1.mp4, ow2.mp4, voice.wav&gt;.
/// </summary>
internal static class RenderLab
{
    internal static readonly List<(string Name, Func<string, EditProject> Build)> Scenarios = [];
    public static void Run(string output, string media)
    {
        Directory.CreateDirectory(output);
        foreach (var (name, build) in Scenarios)
        {
            var project = build(media);
            File.WriteAllText(Path.Combine(output, name + ".mlt"), ZenithClipper.Mlt.MltXml.Compile(project).ToString());
            Console.WriteLine("wrote " + name);
        }
        // Render-preview round trip: render 0.5 s to 1.5 s of a two-track sequence picture-only with melt, then compile
        // the playback graph that uses it.
        if (Environment.GetEnvironmentVariable("ZENITH_LAB_PREVIEW") is not "1") return;
        {
            var (p, s, a, b, _) = Base(media);
            s.Tracks.Insert(1, new EditTrack { Name = "Video 2", Kind = TrackKind.Video });
            s.Tracks[0].Clips.Add(new TimelineClip { MediaId = a.Id, Duration = 60 });
            s.Tracks[1].Clips.Add(new TimelineClip { MediaId = b.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly, Transform = new ClipTransform { ScaleX = .5, ScaleY = .5 }, Effects = [new ClipEffect { Id = "drop_shadow" }] });
            string plain = Path.Combine(output, "preview_source.mlt"); File.WriteAllText(plain, ZenithClipper.Mlt.MltXml.Compile(p).ToString());
            string file = Path.Combine(output, "preview_render.mp4"); File.Delete(file);
            var melt = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("melt", [plain, "in=15", "out=44", "-consumer", "avformat:" + file, "vcodec=libx264", "crf=16", "preset=ultrafast", "g=1", "an=1", "pix_fmt=yuv420p"]) { RedirectStandardError = true })!;
            melt.StandardError.ReadToEnd(); melt.WaitForExit();
            PreviewRenders.Add(s, new PreviewRender { From = 15, To = 45, Key = PreviewRenders.Key(p, s, 15, 45), Path = file });
            var play = ZenithClipper.Editing.ProjectValidation.Clone(p);
            Console.WriteLine("preview substitutions: " + PreviewRenders.Substitute(play, play.ActiveSequenceId));
            File.WriteAllText(Path.Combine(output, "preview_play.mlt"), ZenithClipper.Mlt.MltXml.Compile(play).ToString());
        }
    }
    /// <summary>A 640×360 PNG, transparent except an orange ring, for alpha-aware checks without a font stack.</summary>
    static string AlphaPng()
    {
        string path = Path.Combine(Path.GetTempPath(), "zc-lab-alpha.png"); if (File.Exists(path)) return path;
        const int w = 640, h = 360; var raw = new byte[h * (w * 4 + 1)];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            double r = Math.Sqrt((x - 320.0) * (x - 320.0) + (y - 180.0) * (y - 180.0)); if (r < 70 || r > 110) continue;
            int i = y * (w * 4 + 1) + 1 + x * 4; raw[i] = 255; raw[i + 1] = 107; raw[i + 2] = 74; raw[i + 3] = 255;
        }
        static uint Crc(byte[] d) { uint c = 0xFFFFFFFF; foreach (byte b in d) { c ^= b; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1; } return ~c; }
        using var file = File.Create(path);
        void Chunk(string type, byte[] data)
        {
            var len = BitConverter.GetBytes(data.Length); Array.Reverse(len); file.Write(len);
            var body = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray(); file.Write(body);
            var crc = BitConverter.GetBytes(Crc(body)); Array.Reverse(crc); file.Write(crc);
        }
        file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        Chunk("IHDR", new byte[] { 0, 0, 2, 128, 0, 0, 1, 104, 8, 6, 0, 0, 0 });
        using (var ms = new MemoryStream()) { using (var z = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.Fastest, true)) z.Write(raw); Chunk("IDAT", ms.ToArray()); }
        Chunk("IEND", []);
        return path;
    }
    internal static (EditProject Project, EditSequence Sequence, MediaAsset A, MediaAsset B, MediaAsset Voice) Base(string media)
    {
        var fps = new FrameRate(30, 1);
        var a = new MediaAsset { Name = "a", Path = Path.Combine(media, "ow1.mp4"), Kind = MediaKind.Video, DurationSeconds = 20, Width = 1280, Height = 720, Fps = new FrameRate(60, 1), HasAudio = true };
        var b = new MediaAsset { Name = "b", Path = Path.Combine(media, "ow2.mp4"), Kind = MediaKind.Video, DurationSeconds = 8, Width = 1280, Height = 720, Fps = new FrameRate(60, 1), HasAudio = true };
        var v = new MediaAsset { Name = "voice", Path = Path.Combine(media, "voice.wav"), Kind = MediaKind.Audio, DurationSeconds = 30, HasAudio = true };
        var seq = new EditSequence { Fps = fps, Width = 640, Height = 360, Tracks = [new EditTrack { Name = "Video 1", Kind = TrackKind.Video }, new EditTrack { Name = "Audio 1", Kind = TrackKind.Audio }] };
        return (new EditProject { ActiveSequenceId = seq.Id, Sequences = [seq], Media = [a, b, v] }, seq, a, b, v);
    }
    static RenderLab()
    {
        foreach (var def in ZenithClipper.Mlt.EffectCatalog.All)
        {
            var d = def;
            Scenarios.Add(("fx_" + d.Id, m =>
            {
                var (p, s, a, _, voice) = Base(m);
                var effect = new ClipEffect { Id = d.Id };
                // Push numbers away from their neutral default so the effect visibly/audibly does something.
                foreach (var param in d.Params)
                    effect.Parameters[param.Key] = param.Type switch
                    {
                        ZenithClipper.Mlt.ParamType.Number => (param.Default + (param.Max - param.Default) * .6).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                        ZenithClipper.Mlt.ParamType.File => Path.Combine(m, "test.cube"),
                        _ => param.DefaultText
                    };
                if (d.Kind == ZenithClipper.Mlt.EffectKind.Video)
                    s.Tracks[0].Clips.Add(new TimelineClip { MediaId = a.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly, Effects = [effect] });
                else
                    s.Tracks[1].Clips.Add(new TimelineClip { MediaId = voice.Id, Duration = 90, StreamIntent = ClipStreamIntent.AudioOnly, Effects = [effect] });
                return p;
            }));
        }
        foreach (var def in TransitionCatalog.All)
        {
            var t = def;
            Scenarios.Add(("tr_" + t.Id, m =>
            {
                var (p, s, a, b, voice) = Base(m);
                // Two clips cut at 1 s, each with plenty of handle on both sides, so the transition centers on the cut.
                var track = s.Tracks[t.Audio ? 1 : 0];
                var mediaA = t.Audio ? voice : a; var mediaB = t.Audio ? voice : b;
                track.Clips.Add(new TimelineClip { MediaId = mediaA.Id, Start = 0, SourceIn = 60, Duration = 30, StreamIntent = t.Audio ? ClipStreamIntent.AudioOnly : ClipStreamIntent.VideoOnly });
                track.Clips.Add(new TimelineClip { MediaId = mediaB.Id, Start = 30, SourceIn = t.Audio ? 300 : 60, Duration = 60, StreamIntent = t.Audio ? ClipStreamIntent.AudioOnly : ClipStreamIntent.VideoOnly, TransitionIn = new ClipTransition { Kind = t.Id, Duration = 20 } });
                return p;
            }));
        }
        Scenarios.Add(("kf_motion", m =>
        {
            var (p, s, a, b, _) = Base(m);
            s.Tracks[0].Clips.Add(new TimelineClip { MediaId = a.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly });
            var v2 = new EditTrack { Name = "Video 2", Kind = TrackKind.Video }; s.Tracks.Add(v2);
            v2.Clips.Add(new TimelineClip { MediaId = b.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly, Motion =
            [
                new() { Parameter = "x", Frame = 0, Value = -.3 }, new() { Parameter = "x", Frame = 59, Value = .3 },
                new() { Parameter = "scale_x", Frame = 0, Value = .4 }, new() { Parameter = "scale_y", Frame = 0, Value = .4 },
                new() { Parameter = "rotation", Frame = 0, Value = 0 }, new() { Parameter = "rotation", Frame = 59, Value = 90 },
                new() { Parameter = "opacity", Frame = 0, Value = 1 }, new() { Parameter = "opacity", Frame = 59, Value = .3 },
            ] });
            return p;
        }));
        foreach (var mode in new[] { "multiply", "screen", "difference", "overlay" })
        {
            var blend = mode;
            Scenarios.Add(("blend_" + blend, m =>
            {
                var (p, s, a, b, _) = Base(m);
                s.Tracks[0].Clips.Add(new TimelineClip { MediaId = a.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly });
                var v2 = new EditTrack { Name = "Video 2", Kind = TrackKind.Video }; s.Tracks.Add(v2);
                v2.Clips.Add(new TimelineClip { MediaId = b.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly, BlendMode = blend });
                return p;
            }));
        }
        Scenarios.Add(("mask_ellipse", m =>
        {
            var (p, s, a, b, _) = Base(m);
            s.Tracks[0].Clips.Add(new TimelineClip { MediaId = a.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly });
            var v2 = new EditTrack { Name = "Video 2", Kind = TrackKind.Video }; s.Tracks.Add(v2);
            v2.Clips.Add(new TimelineClip { MediaId = b.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly, Mask = new ClipMask { Shape = "ellipse", X = .35, Y = .5, Width = .5, Height = .7, Feather = .15 } });
            return p;
        }));
        Scenarios.Add(("mask_rect_inverted", m =>
        {
            var (p, s, a, b, _) = Base(m);
            s.Tracks[0].Clips.Add(new TimelineClip { MediaId = a.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly });
            var v2 = new EditTrack { Name = "Video 2", Kind = TrackKind.Video }; s.Tracks.Add(v2);
            v2.Clips.Add(new TimelineClip { MediaId = b.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly, Mask = new ClipMask { Shape = "rectangle", X = .5, Y = .5, Width = .4, Height = .4, Feather = .05, Invert = true } });
            return p;
        }));
        Scenarios.Add(("kf_volume_pan", m =>
        {
            var (p, s, _, _, voice) = Base(m);
            s.Tracks[1].Pan = -1;
            s.Tracks[1].Clips.Add(new TimelineClip { MediaId = voice.Id, Duration = 90, StreamIntent = ClipStreamIntent.AudioOnly, Motion = [new() { Parameter = "volume", Frame = 0, Value = -40 }, new() { Parameter = "volume", Frame = 89, Value = 0 }] });
            return p;
        }));
        Scenarios.Add(("hold", m =>
        {
            var (p, s, a, _, _) = Base(m);
            s.Tracks[0].Clips.Add(new TimelineClip { MediaId = a.Id, Duration = 60, HoldFrame = 300, StreamIntent = ClipStreamIntent.VideoOnly });
            return p;
        }));
        Scenarios.Add(("fade_edges", m =>
        {
            var (p, s, a, _, _) = Base(m);
            s.Tracks[0].Clips.Add(new TimelineClip { MediaId = a.Id, Start = 0, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly, TransitionIn = new ClipTransition { Kind = "cross_dissolve", Duration = 20 }, TransitionOut = new ClipTransition { Kind = "dip_white", Duration = 20 } });
            return p;
        }));
        Scenarios.Add(("crop", m =>
        {
            var (p, s, a, _, _) = Base(m);
            s.Tracks[0].Clips.Add(new TimelineClip { MediaId = a.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly, Transform = new ClipTransform { CropLeft = .25, CropRight = .1, CropTop = .2 } });
            return p;
        }));
        Scenarios.Add(("rot_static", m =>
        {
            var (p, s, a, _, _) = Base(m);
            s.Tracks[0].Clips.Add(new TimelineClip { MediaId = a.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly, Transform = new ClipTransform { ScaleX = .5, ScaleY = .5, X = .2, Rotation = 30 } });
            return p;
        }));
        Scenarios.Add(("shadow_title", m =>
        {
            var (p, s, a, _, _) = Base(m);
            var bg = new MediaAsset { Name = "bg", Kind = MediaKind.Color, Color = "#E0E0E0", DurationSeconds = 5, Path = "" }; p.Media.Add(bg);
            var title = new MediaAsset { Name = "shape", Kind = MediaKind.Image, Path = AlphaPng(), Width = 640, Height = 360, DurationSeconds = 5 }; p.Media.Add(title);
            s.Tracks.Insert(1, new EditTrack { Name = "Video 2", Kind = TrackKind.Video });
            s.Tracks[0].Clips.Add(new TimelineClip { MediaId = bg.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly });
            var c = new TimelineClip { MediaId = title.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly, Transform = new ClipTransform { ScaleX = (double)title.Width / s.Width, ScaleY = (double)title.Height / s.Height } };
            c.Effects.Add(new ClipEffect { Id = "drop_shadow", Parameters = new() { ["distance"] = "12", ["softness"] = "8", ["opacity"] = "80" } });
            s.Tracks[1].Clips.Add(c);
            return p;
        }));
        Scenarios.Add(("shadow_pip", m =>
        {
            var (p, s, a, b, _) = Base(m);
            s.Tracks.Insert(1, new EditTrack { Name = "Video 2", Kind = TrackKind.Video });
            var bg = new MediaAsset { Name = "bg", Kind = MediaKind.Color, Color = "#E0E0E0", DurationSeconds = 5, Path = "" }; p.Media.Add(bg);
            s.Tracks[0].Clips.Add(new TimelineClip { MediaId = bg.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly });
            var c = new TimelineClip { MediaId = a.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly, Transform = new ClipTransform { ScaleX = .5, ScaleY = .5 } };
            c.Effects.Add(new ClipEffect { Id = "drop_shadow", Parameters = new() { ["distance"] = "20", ["softness"] = "0", ["opacity"] = "100", ["color"] = "#000000" } });
            c.Effects.Add(new ClipEffect { Id = "bevel_edges" });
            s.Tracks[1].Clips.Add(c);
            return p;
        }));
        Scenarios.Add(("mask_pen", m =>
        {
            var (p, s, a, b, _) = Base(m);
            s.Tracks.Insert(1, new EditTrack { Name = "Video 2", Kind = TrackKind.Video });
            s.Tracks[0].Clips.Add(new TimelineClip { MediaId = a.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly });
            s.Tracks[1].Clips.Add(new TimelineClip { MediaId = b.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly, Mask = new ClipMask { Shape = "path", Points = [.5, .1, .9, .9, .1, .9], Feather = .02 } });
            return p;
        }));
        Scenarios.Add(("mask_pen_smooth", m =>
        {
            var (p, s, a, b, _) = Base(m);
            s.Tracks.Insert(1, new EditTrack { Name = "Video 2", Kind = TrackKind.Video });
            s.Tracks[0].Clips.Add(new TimelineClip { MediaId = a.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly });
            s.Tracks[1].Clips.Add(new TimelineClip { MediaId = b.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly, Mask = new ClipMask { Shape = "path", Points = [.5, .1, .9, .5, .5, .9, .1, .5], Smooth = true, Feather = .1 } });
            return p;
        }));
        Scenarios.Add(("alpha_ignore", m =>
        {
            var (p, s, a, _, _) = Base(m);
            var bg = new MediaAsset { Name = "bg", Kind = MediaKind.Color, Color = "#3060A0", DurationSeconds = 5, Path = "" }; p.Media.Add(bg);
            var img = new MediaAsset { Name = "ring", Kind = MediaKind.Image, Path = AlphaPng(), Width = 640, Height = 360, DurationSeconds = 5, AlphaMode = "ignore" }; p.Media.Add(img);
            s.Tracks.Insert(1, new EditTrack { Name = "Video 2", Kind = TrackKind.Video });
            s.Tracks[0].Clips.Add(new TimelineClip { MediaId = bg.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly });
            s.Tracks[1].Clips.Add(new TimelineClip { MediaId = img.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly });
            return p;
        }));
        Scenarios.Add(("alpha_invert", m =>
        {
            var (p, s, a, _, _) = Base(m);
            var bg = new MediaAsset { Name = "bg", Kind = MediaKind.Color, Color = "#3060A0", DurationSeconds = 5, Path = "" }; p.Media.Add(bg);
            var img = new MediaAsset { Name = "ring", Kind = MediaKind.Image, Path = AlphaPng(), Width = 640, Height = 360, DurationSeconds = 5, AlphaMode = "invert" }; p.Media.Add(img);
            s.Tracks.Insert(1, new EditTrack { Name = "Video 2", Kind = TrackKind.Video });
            s.Tracks[0].Clips.Add(new TimelineClip { MediaId = bg.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly });
            s.Tracks[1].Clips.Add(new TimelineClip { MediaId = img.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly });
            return p;
        }));
        Scenarios.Add(("alpha_premultiplied", m =>
        {
            var (p, s, a, _, _) = Base(m);
            var bg = new MediaAsset { Name = "bg", Kind = MediaKind.Color, Color = "#3060A0", DurationSeconds = 5, Path = "" }; p.Media.Add(bg);
            var img = new MediaAsset { Name = "ring", Kind = MediaKind.Image, Path = AlphaPng(), Width = 640, Height = 360, DurationSeconds = 5, AlphaMode = "premultiplied" }; p.Media.Add(img);
            s.Tracks.Insert(1, new EditTrack { Name = "Video 2", Kind = TrackKind.Video });
            s.Tracks[0].Clips.Add(new TimelineClip { MediaId = bg.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly });
            s.Tracks[1].Clips.Add(new TimelineClip { MediaId = img.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly });
            return p;
        }));
        Scenarios.Add(("keys_eased", m =>
        {
            var (p, s, a, _, _) = Base(m);
            var c = new TimelineClip { MediaId = a.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly, Transform = new ClipTransform { ScaleX = .5, ScaleY = .5 } };
            c.Motion.AddRange([new Keyframe { Parameter = "x", Frame = 0, Value = -.25, Interpolation = "ease_in_out" }, new Keyframe { Parameter = "x", Frame = 40, Value = .25 },
                new Keyframe { Parameter = "opacity", Frame = 0, Value = .2, Interpolation = "ease_out" }, new Keyframe { Parameter = "opacity", Frame = 30, Value = 1, Interpolation = "discrete" }, new Keyframe { Parameter = "opacity", Frame = 50, Value = .5 }]);
            s.Tracks[0].Clips.Add(c);
            return p;
        }));
        Scenarios.Add(("mask_tracked", m =>
        {
            var (p, s, a, b, _) = Base(m);
            s.Tracks.Insert(1, new EditTrack { Name = "Video 2", Kind = TrackKind.Video });
            var bg = new MediaAsset { Name = "bg", Kind = MediaKind.Color, Color = "#3060A0", DurationSeconds = 5, Path = "" }; p.Media.Add(bg);
            s.Tracks[0].Clips.Add(new TimelineClip { MediaId = bg.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly });
            s.Tracks[1].Clips.Add(new TimelineClip { MediaId = b.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly, Mask = new ClipMask { Shape = "ellipse", X = .3, Y = .5, Width = .3, Height = .4, Feather = .02, Track = [0, .3, .5, 59, .7, .5] } });
            return p;
        }));
        Scenarios.Add(("anchor_static", m =>
        {
            var (p, s, a, _, _) = Base(m);
            s.Tracks[0].Clips.Add(new TimelineClip { MediaId = a.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly, Transform = new ClipTransform { ScaleX = .5, ScaleY = .5, AnchorX = 0, AnchorY = 0, Rotation = 90 } });
            return p;
        }));
        Scenarios.Add(("anchor_keyed", m =>
        {
            var (p, s, a, _, _) = Base(m);
            var c = new TimelineClip { MediaId = a.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly, Transform = new ClipTransform { ScaleX = .5, ScaleY = .5, AnchorX = 0, AnchorY = 0, Rotation = 90 } };
            c.Motion.Add(new Keyframe { Parameter = "rotation", Frame = 0, Value = 90 }); c.Motion.Add(new Keyframe { Parameter = "rotation", Frame = 59, Value = 90 });
            s.Tracks[0].Clips.Add(c);
            return p;
        }));
        Scenarios.Add(("rot_keyed", m =>
        {
            var (p, s, a, _, _) = Base(m);
            var c = new TimelineClip { MediaId = a.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly, Transform = new ClipTransform { ScaleX = .5, ScaleY = .5, X = .2, Rotation = 30 } };
            c.Motion.Add(new Keyframe { Parameter = "rotation", Frame = 0, Value = 30 }); c.Motion.Add(new Keyframe { Parameter = "rotation", Frame = 59, Value = 30 });
            s.Tracks[0].Clips.Add(c);
            return p;
        }));
        Scenarios.Add(("adjustment", m =>
        {
            // b full frame, an inverting adjustment layer above it, then a small a on top that must stay un-inverted.
            var (p, s, a, b, _) = Base(m);
            var adj = new MediaAsset { Name = "Adjustment Layer", Kind = MediaKind.Adjustment, DurationSeconds = 10, Path = "" }; p.Media.Add(adj);
            s.Tracks.Insert(1, new EditTrack { Name = "Video 2", Kind = TrackKind.Video }); s.Tracks.Insert(2, new EditTrack { Name = "Video 3", Kind = TrackKind.Video });
            s.Tracks[0].Clips.Add(new TimelineClip { MediaId = b.Id, Duration = 90, StreamIntent = ClipStreamIntent.VideoOnly });
            s.Tracks[1].Clips.Add(new TimelineClip { MediaId = adj.Id, Start = 15, Duration = 45, StreamIntent = ClipStreamIntent.VideoOnly, Effects = [new ClipEffect { Id = "invert" }] });
            s.Tracks[2].Clips.Add(new TimelineClip { MediaId = a.Id, Duration = 90, StreamIntent = ClipStreamIntent.VideoOnly, Transform = new ClipTransform { ScaleX = .4, ScaleY = .4, X = .25 } });
            return p;
        }));
        Scenarios.Add(("remap", m =>
        {
            // 100% for 20 frames, ramp to 400% by frame 40, hold 400%, then down to 50% at the end.
            var (p, s, a, _, _) = Base(m);
            var c = new TimelineClip { MediaId = a.Id, Duration = 90, SourceIn = 30, StreamIntent = ClipStreamIntent.VideoOnly };
            foreach (var (f, v) in new (long, double)[] { (20, 1), (40, 4), (60, 4), (89, .5) }) c.Motion.Add(new Keyframe { Parameter = "speed", Frame = f, Value = v });
            s.Tracks[0].Clips.Add(c);
            Console.WriteLine("remap source at 10/30/50/80: " + string.Join(", ", new long[] { 10, 30, 50, 80 }.Select(k => (30 + TimeRemap.Consumed(c, k)).ToString("0.###"))));
            return p;
        }));
        Scenarios.Add(("plain_a", m =>
        {
            var (p, s, a, _, _) = Base(m);
            s.Tracks[0].Clips.Add(new TimelineClip { MediaId = a.Id, Duration = 590, StreamIntent = ClipStreamIntent.VideoOnly });
            return p;
        }));
        Scenarios.Add(("pitch_keep", m =>
        {
            var (p, s, _, _, voice) = Base(m);
            s.Tracks.Last(t => t.Kind == TrackKind.Audio).Clips.Add(new TimelineClip { MediaId = voice.Id, Duration = 60, Speed = 2, MaintainPitch = true, StreamIntent = ClipStreamIntent.AudioOnly });
            return p;
        }));
        Scenarios.Add(("pitch_shift", m =>
        {
            var (p, s, _, _, voice) = Base(m);
            s.Tracks.Last(t => t.Kind == TrackKind.Audio).Clips.Add(new TimelineClip { MediaId = voice.Id, Duration = 60, Speed = 2, StreamIntent = ClipStreamIntent.AudioOnly });
            return p;
        }));
        foreach (var (name, spec) in new[] { ("gen_bars", "bars"), ("gen_gradient", "gradient:#1E2A78:#FF6B4A"), ("gen_transparent", "transparent"), ("gen_matte", "#2B5CFF") })
            Scenarios.Add((name, m =>
            {
                var (p, s, _, b, _) = Base(m);
                var gen = new MediaAsset { Name = name, Kind = MediaKind.Color, Color = spec, DurationSeconds = 5, Path = "" }; p.Media.Add(gen);
                s.Tracks.Insert(1, new EditTrack { Name = "Video 2", Kind = TrackKind.Video });
                s.Tracks[0].Clips.Add(new TimelineClip { MediaId = b.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly });
                s.Tracks[1].Clips.Add(new TimelineClip { MediaId = gen.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly, Transform = new ClipTransform { ScaleX = .5, ScaleY = .5 } });
                return p;
            }));
        foreach (var type in EditorWindow.SoundTypeNames)
        {
            var t = type;
            Scenarios.Add(("essential_" + t.ToLowerInvariant(), m =>
            {
                var (p, s, _, _, voice) = Base(m);
                var clip = new TimelineClip { MediaId = voice.Id, Duration = 60, StreamIntent = ClipStreamIntent.AudioOnly };
                foreach (var (fx, ps) in EditorWindow.SoundChain(t)) clip.Effects.Add(new ClipEffect { Id = fx, Parameters = ps.ToDictionary(x => x.Item1, x => x.Item2) });
                s.Tracks.Last(x => x.Kind == TrackKind.Audio).Clips.Add(clip);
                return p;
            }));
        }
        foreach (var cam in new[] { 1, 2 })
            Scenarios.Add(("multicam_" + cam, m =>
            {
                // Camera 1 = a, camera 2 = b; the multicam clip switches to camera 2 at frame 30 when cam == 2.
                var (p, s, a, b, _) = Base(m);
                s.Tracks.Insert(1, new EditTrack { Name = "Video 2", Kind = TrackKind.Video });
                var c1 = new TimelineClip { MediaId = a.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly }; s.Tracks[0].Clips.Add(c1);
                var c2 = new TimelineClip { MediaId = b.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly }; s.Tracks[1].Clips.Add(c2);
                var id = EditCommands.CreateMulticam(p, s.Id, [c1.Id, c2.Id], "Multicam");
                if (cam == 2) EditCommands.SwitchAngle(p, s.Id, s.Tracks[0].Id, id, 30, 2);
                return p;
            }));
        Scenarios.Add(("interpret", m =>
        {
            var (p, s, a, _, _) = Base(m);
            a.InterpretFps = a.Fps.Value / 2; a.DurationSeconds *= 2;
            s.Tracks[0].Clips.Add(new TimelineClip { MediaId = a.Id, Duration = 90, StreamIntent = ClipStreamIntent.VideoOnly });
            return p;
        }));
        Scenarios.Add(("nested", m =>
        {
            // b fills the frame; a nested sequence holding a half-size a (with its audio) sits on top, scaled down again.
            var (p, s, a, b, _) = Base(m);
            s.Tracks.Insert(0, new EditTrack { Name = "Video 0", Kind = TrackKind.Video });
            s.Tracks[0].Clips.Add(new TimelineClip { MediaId = b.Id, Duration = 90, StreamIntent = ClipStreamIntent.VideoOnly });
            s.Tracks[1].Clips.Add(new TimelineClip { MediaId = a.Id, Start = 15, Duration = 60, Transform = new ClipTransform { ScaleX = .5, ScaleY = .5 } });
            var id = EditCommands.Nest(p, s.Id, [s.Tracks[1].Clips[0].Id], "Nested");
            var nested = s.Tracks.SelectMany(t => t.Clips).Single(c => c.Id == id);
            nested.Transform = new ClipTransform { X = .2, ScaleX = .8, ScaleY = .8 };
            nested.Effects.Add(new ClipEffect { Id = "invert" });
            return p;
        }));
    }
}
