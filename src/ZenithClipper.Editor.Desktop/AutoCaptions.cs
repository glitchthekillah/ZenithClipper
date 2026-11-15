using System.Globalization;
using System.Text.Json;
using Avalonia.Controls;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;

/// <summary>
/// Auto captions: mixes the timeline's audible clips with FFmpeg, transcribes the mix locally with whisper.cpp
/// (whisper-cli, run as a separate process like ffprobe), and lays the result on one "Captions (auto)" track.
/// One click on CC hides or shows every caption track; generating again replaces the auto track in one undo step.
/// Nothing is downloaded by the editor and no audio leaves the PC.
/// </summary>
public sealed partial class EditorWindow
{
    internal const string AutoCaptionTrack = "Captions (auto)";
    const int MaxCaptionSources = 64;
    CancellationTokenSource? captionCancellation;
    Button captionsToggle = null!, captionsGenerate = null!;

    static bool IsCaptionTrack(EditTrack track) => track.Kind == TrackKind.Video && track.Name.StartsWith("Captions", StringComparison.Ordinal);
    bool HasCaptions => Sequence.Tracks.Any(IsCaptionTrack);
    bool CaptionsVisible => Sequence.Tracks.Where(IsCaptionTrack).Any(t => !t.Hidden);

    /// <summary>The one-click switch: hides or shows every caption track (auto or imported). Undoable.</summary>
    void ToggleCaptions()
    {
        if (!HasCaptions) { Run(GenerateAutoCaptions); return; }
        bool show = !CaptionsVisible;
        Change(show ? "Show captions" : "Hide captions", p =>
        {
            foreach (var track in p.Sequences.First(s => s.Id == p.ActiveSequenceId).Tracks.Where(IsCaptionTrack)) track.Hidden = !show;
        });
        status.Text = show ? "Captions on." : "Captions off. They stay in the project; click CC to bring them back.";
    }

    void RemoveAutoCaptions()
    {
        if (!Sequence.Tracks.Any(t => t.Name == AutoCaptionTrack)) { status.Text = "There are no auto captions to remove."; return; }
        Change("Remove auto captions", p =>
        {
            var sequence = p.Sequences.First(s => s.Id == p.ActiveSequenceId);
            sequence.Tracks.RemoveAll(t => t.Name == AutoCaptionTrack && !t.Locked);
            DropUnusedText(p);
        });
        status.Text = "Auto captions removed. Undo brings them back.";
    }

    void UpdateCaptionControls()
    {
        if (captionsToggle is null) return;
        bool has = HasCaptions, on = has && CaptionsVisible, busy = captionCancellation is not null;
        captionsToggle.IsEnabled = !busy;
        captionsToggle.Content = CaptionGlyph(on);
        captionsToggle.Background = on ? Brush("#3A2018") : Avalonia.Media.Brushes.Transparent;
        ToolTip.SetTip(captionsToggle, !has ? "Generate auto captions" : on ? "Captions on · click to hide" : "Captions off · click to show");
        Avalonia.Automation.AutomationProperties.SetName(captionsToggle, on ? "Hide captions" : "Show captions");
        captionsGenerate.Content = ToolbarLabel(busy ? "delete" : "captions", busy ? "Cancel captions" : has && Sequence.Tracks.Any(t => t.Name == AutoCaptionTrack) ? "Redo auto captions" : "Auto captions");
        ToolTip.SetTip(captionsGenerate, busy ? "Stop transcribing" : "Transcribe the timeline's audio on this PC and add editable captions");
    }

    async Task GenerateAutoCaptions()
    {
        if (captionCancellation is { } running) { running.Cancel(); return; }
        EnsureEditable();
        var destination = session;
        var project = ProjectCopy;
        var sequence = project.Sequences.First(s => s.Id == project.ActiveSequenceId);
        if (sequence.Duration == 0) throw new InvalidOperationException("Add a clip with sound to the timeline first.");
        if (sequence.Tracks.Any(t => t.Name == AutoCaptionTrack && t.Locked)) throw new InvalidOperationException("Unlock the auto captions track before generating them again.");
        string? whisper = FindWhisper(), model = FindWhisperModel();
        if (whisper is null || model is null)
        {
            await Ask("Auto captions need whisper.cpp",
                "Auto captions run on this PC with whisper.cpp, so your audio never leaves it.\n\n" +
                "Run scripts\\get-whisper.ps1 once from the ZenithClipper folder. It downloads whisper.cpp and the base speech model (about 150 MB) into tools\\whisper. " +
                "You can also point ZENITH_WHISPER at whisper-cli.exe and ZENITH_WHISPER_MODEL at a ggml model file.", ["OK"]);
            status.Text = whisper is null ? "Auto captions: whisper-cli was not found in tools\\whisper." : "Auto captions: no speech model found in tools\\whisper\\models.";
            return;
        }
        var (sources, skipped) = AudibleClips(project, sequence);
        if (sources.Count == 0) throw new InvalidOperationException(skipped > 0 ? "Only sped-up or reversed clips have sound. Auto captions need normal-speed audio." : "Nothing on the timeline has sound to caption.");
        if (sources.Count > MaxCaptionSources) throw new InvalidOperationException($"Auto captions handle up to {MaxCaptionSources} audio clips at once. Split the project or export and re-import it first.");

        using var cts = new CancellationTokenSource();
        captionCancellation = cts; UpdateCaptionControls();
        progress.IsIndeterminate = true; progress.IsVisible = true;
        var work = Path.Combine(Path.GetTempPath(), "zenith-captions-" + Guid.NewGuid().ToString("N"));
        var rendered = new List<(SubtitleCue Cue, MediaAsset Asset)>();
        try
        {
            Directory.CreateDirectory(work);
            status.Text = "Auto captions: mixing timeline audio…";
            var wav = Path.Combine(work, "speech.wav");
            await MediaTools.RunAsync(FindFfmpeg(), MixArguments(sources, sequence, wav), cts.Token);
            status.Text = "Auto captions: transcribing on this PC. Longer projects take a while; click Cancel captions to stop.";
            var output = Path.Combine(work, "speech");
            int threads = Math.Clamp(Environment.ProcessorCount / 2, 2, 8);
            await MediaTools.RunAsync(whisper, ["-m", model, "-f", wav, "-l", "auto", "-oj", "-of", output, "-ml", "42", "-sow", "-np", "-t", threads.ToString(CultureInfo.InvariantCulture)], cts.Token);
            var cues = ParseWhisperJson(await File.ReadAllTextAsync(output + ".json", cts.Token), sequence.Fps.Seconds(sequence.Duration));
            CheckImportDestination(destination);
            if (cues.Count == 0) { status.Text = "Auto captions: no speech was found in the timeline audio."; return; }
            var assetFolder = AssetDirectory;
            foreach (var cue in cues)
            {
                cts.Token.ThrowIfCancellationRequested();
                rendered.Add((cue, TitleRasterizer.Create(cue.Text, 44, "#ffffff", cue.EndSeconds - cue.StartSeconds, assetFolder, "#B0000000")));
                if (rendered.Count % 25 == 0) { status.Text = $"Auto captions: drawing {rendered.Count}/{cues.Count}…"; await Task.Yield(); }
            }
            CheckImportDestination(destination);
            EnsureEditable();
            session.Execute("Auto captions", p =>
            {
                var target = p.Sequences.First(s => s.Id == p.ActiveSequenceId);
                target.Tracks.RemoveAll(t => t.Name == AutoCaptionTrack);
                DropUnusedText(p);
                var track = new EditTrack { Kind = TrackKind.Video, Name = AutoCaptionTrack };
                target.Tracks.Add(track); // last video track draws on top of everything else
                foreach (var (cue, asset) in rendered)
                {
                    long start = target.Fps.Frames(cue.StartSeconds), end = target.Fps.Frames(cue.EndSeconds);
                    p.Media.Add(asset);
                    track.Clips.Add(TextClip(asset, target, start, Math.Max(1, end - start), true));
                }
                selectedTrack = track.Id; selectedClip = null;
            });
            rendered.Clear();
            Refresh(true);
            status.Text = $"Auto captions: {cues.Count} lines added{(skipped > 0 ? $" ({skipped} sped-up or reversed clips skipped)" : "")}. Click CC to turn them off; select one to fix its text.";
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { status.Text = "Auto captions cancelled."; }
        finally
        {
            foreach (var (_, asset) in rendered) try { File.Delete(asset.Path); } catch (IOException) { }
            try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            captionCancellation = null;
            progress.IsIndeterminate = false; progress.IsVisible = exportFinished is not null;
            UpdateCaptionControls();
        }
    }

    /// <summary>Removes Text media no clip refers to any more (captions replaced or removed), so the bin stays clean.</summary>
    static void DropUnusedText(EditProject project)
    {
        var used = project.Sequences.SelectMany(s => s.Tracks).SelectMany(t => t.Clips).Select(c => c.MediaId).ToHashSet();
        project.Media.RemoveAll(m => m.Kind == MediaKind.Text && !used.Contains(m.Id));
    }

    internal sealed record CaptionSource(string Path, double SourceSeconds, double Seconds, double StartSeconds, double GainDb);

    /// <summary>Clips the export would play sound from, using the same rules as the MLT graph. Sped-up and reversed clips are skipped.</summary>
    internal static (List<CaptionSource> Sources, int Skipped) AudibleClips(EditProject project, EditSequence sequence)
    {
        var media = project.Media.ToDictionary(m => m.Id);
        var list = new List<CaptionSource>(); int skipped = 0;
        foreach (var track in sequence.Tracks.Where(t => !t.Muted && !IsCaptionTrack(t)))
            foreach (var clip in track.Clips.Where(c => c.Enabled))
            {
                if (!media.TryGetValue(clip.MediaId, out var asset) || asset.Kind is not (MediaKind.Video or MediaKind.Audio) || !asset.HasAudio) continue;
                if (clip.StreamIntent == ClipStreamIntent.VideoOnly) continue;
                bool linkedAudio = track.Kind == TrackKind.Video && clip.LinkId != null && sequence.Tracks.Where(t => t.Kind == TrackKind.Audio).SelectMany(t => t.Clips).Any(c => c.Enabled && c.LinkId == clip.LinkId && c.MediaId == clip.MediaId);
                if (linkedAudio) continue;
                if (Math.Abs(clip.Speed - 1) > 1e-6 || clip.Reverse) { skipped++; continue; }
                if (!File.Exists(asset.Path)) throw new FileNotFoundException("Relink missing media before making captions.", asset.Path);
                list.Add(new(asset.Path, sequence.Fps.Seconds(clip.SourceIn), sequence.Fps.Seconds(clip.Duration), sequence.Fps.Seconds(clip.Start), clip.GainDb + track.GainDb));
            }
        return (list, skipped);
    }

    /// <summary>One 16 kHz mono WAV of the timeline mix: each clip trimmed, delayed to its timeline position and gain-adjusted.</summary>
    internal static List<string> MixArguments(IReadOnlyList<CaptionSource> sources, EditSequence sequence, string output)
    {
        static string N(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
        var args = new List<string> { "-hide_banner", "-v", "error", "-nostdin", "-y" };
        foreach (var s in sources) args.AddRange(["-ss", N(s.SourceSeconds), "-t", N(s.Seconds), "-i", s.Path]);
        var graph = new System.Text.StringBuilder();
        for (int i = 0; i < sources.Count; i++)
        {
            long delay = (long)Math.Round(sources[i].StartSeconds * 1000);
            graph.Append(CultureInfo.InvariantCulture, $"[{i}:a:0]aresample=16000,aformat=channel_layouts=mono,adelay={delay}:all=1,volume={N(sources[i].GainDb)}dB[a{i}];");
        }
        for (int i = 0; i < sources.Count; i++) graph.Append(CultureInfo.InvariantCulture, $"[a{i}]");
        graph.Append(CultureInfo.InvariantCulture, $"amix=inputs={sources.Count}:normalize=0:duration=longest,alimiter=limit=0.95[mix]");
        args.AddRange(["-filter_complex", graph.ToString(), "-map", "[mix]", "-t", N(sequence.Fps.Seconds(sequence.Duration)), "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", output]);
        return args;
    }

    /// <summary>Reads whisper-cli's -oj output: transcription[].offsets.from/to (ms) and text. Drops empty and bracketed non-speech lines.</summary>
    internal static List<SubtitleCue> ParseWhisperJson(string json, double limitSeconds)
    {
        var cues = new List<SubtitleCue>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("transcription", out var items) || items.ValueKind != JsonValueKind.Array) return cues;
        foreach (var item in items.EnumerateArray())
        {
            var text = (item.TryGetProperty("text", out var t) ? t.GetString() : null)?.Trim() ?? "";
            if (text.Length == 0 || IsNonSpeech(text)) continue;
            if (!item.TryGetProperty("offsets", out var offsets)) continue;
            double from = offsets.GetProperty("from").GetDouble() / 1000, to = offsets.GetProperty("to").GetDouble() / 1000;
            if (!double.IsFinite(from) || !double.IsFinite(to) || from >= limitSeconds) continue;
            to = Math.Min(limitSeconds, Math.Max(to, from + 0.4));
            if (cues.Count > 0 && cues[^1].EndSeconds > from) cues[^1] = cues[^1] with { EndSeconds = from };
            cues.Add(new SubtitleCue(from, to, text));
        }
        cues.RemoveAll(c => c.EndSeconds - c.StartSeconds < 0.05);
        return cues;
    }
    static bool IsNonSpeech(string text) =>
        (text.StartsWith('[') && text.EndsWith(']')) || (text.StartsWith('(') && text.EndsWith(')')) || (text.StartsWith('*') && text.EndsWith('*')) || text is "♪" or "♪♪";

    static string? FindWhisper()
    {
        if (Environment.GetEnvironmentVariable("ZENITH_WHISPER") is { Length: > 0 } configured && File.Exists(configured)) return configured;
        var name = OperatingSystem.IsWindows() ? "whisper-cli.exe" : "whisper-cli";
        return SearchUp(Path.Combine("tools", "whisper", name), Path.Combine("whisper", name), name);
    }
    static string? FindWhisperModel()
    {
        if (Environment.GetEnvironmentVariable("ZENITH_WHISPER_MODEL") is { Length: > 0 } configured && File.Exists(configured)) return configured;
        string[] preferred = ["ggml-base.bin", "ggml-small.bin", "ggml-base.en.bin", "ggml-small.en.bin", "ggml-tiny.bin", "ggml-tiny.en.bin"];
        var roots = new List<string>();
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent) { roots.Add(Path.Combine(dir.FullName, "tools", "whisper", "models")); roots.Add(Path.Combine(dir.FullName, "whisper", "models")); }
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZenithClipper", "whisper", "models"));
        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var file in preferred) if (File.Exists(Path.Combine(root, file))) return Path.Combine(root, file);
            if (Directory.EnumerateFiles(root, "ggml-*.bin").FirstOrDefault() is { } any) return any;
        }
        return null;
    }
    static string FindFfmpeg()
    {
        if (Environment.GetEnvironmentVariable("ZENITH_FFMPEG") is { Length: > 0 } configured) return configured;
        var name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        return SearchUp(name, Path.Combine("build", "windows", name), Path.Combine("tools", "ffmpeg", "bin", name), Path.Combine("tools", "mlt-runtime", "package", "Shotcut", name)) ?? name;
    }
    static string? SearchUp(params string[] relatives)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            foreach (var relative in relatives)
            {
                var candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate)) return candidate;
            }
        return null;
    }

    internal static void VerifyAutoCaptions()
    {
        const string sample = """{"transcription":[{"offsets":{"from":0,"to":1200},"text":" ok ok ok"},{"offsets":{"from":1000,"to":1500},"text":" [BLANK_AUDIO]"},{"offsets":{"from":1100,"to":2600},"text":" push now, they're split"},{"offsets":{"from":9000,"to":9900},"text":" too late"}]}""";
        var cues = ParseWhisperJson(sample, 8);
        if (cues.Count != 2 || cues[0].EndSeconds != 1.1 || cues[1].Text != "push now, they're split")
            throw new InvalidOperationException("Whisper JSON parsing did not trim overlaps, drop non-speech or respect the timeline end.");
        var fps = new FrameRate(60, 1);
        var audio = new MediaAsset { Kind = MediaKind.Audio, HasAudio = true, Path = typeof(EditorWindow).Assembly.Location, DurationSeconds = 30 };
        var video = new MediaAsset { Kind = MediaKind.Video, HasAudio = true, Path = typeof(EditorWindow).Assembly.Location, DurationSeconds = 30 };
        var link = Guid.NewGuid();
        var sequence = new EditSequence { Fps = fps, Tracks = [
            new() { Kind = TrackKind.Video, Name = "Video 1", Clips = [new() { MediaId = video.Id, LinkId = link, Start = 60, Duration = 120 }] },
            new() { Kind = TrackKind.Audio, Name = "Audio 1", Clips = [new() { MediaId = video.Id, LinkId = link, Start = 60, Duration = 120, SourceIn = 30, GainDb = -3 }] },
            new() { Kind = TrackKind.Audio, Name = "Voice", GainDb = 2, Clips = [new() { MediaId = audio.Id, Start = 0, Duration = 600 }, new() { MediaId = audio.Id, Start = 600, Duration = 60, Speed = 2 }] },
            new() { Kind = TrackKind.Audio, Name = "Muted", Muted = true, Clips = [new() { MediaId = audio.Id, Duration = 60 }] }] };
        var project = new EditProject { Media = [audio, video], Sequences = [sequence], ActiveSequenceId = sequence.Id };
        var (sources, skipped) = AudibleClips(project, sequence);
        if (sources.Count != 2 || skipped != 1 || sources[0].SourceSeconds != .5 || sources[0].StartSeconds != 1 || sources[0].GainDb != -3 || sources[1].GainDb != 2)
            throw new InvalidOperationException("Auto caption audio selection does not match what the export plays.");
        var args = MixArguments(sources, sequence, "out.wav");
        var graph = args[args.IndexOf("-filter_complex") + 1];
        if (!graph.Contains("adelay=1000:all=1", StringComparison.Ordinal) || !graph.Contains("amix=inputs=2", StringComparison.Ordinal) || args[^1] != "out.wav")
            throw new InvalidOperationException("Auto caption mix arguments are wrong: " + string.Join(' ', args));
        Console.WriteLine("Auto captions smoke passed: whisper JSON cues, export-matching audio selection and timeline mix arguments.");
    }
    internal static void VerifyIconMotion() => IconMotion.Verify(name => Glyph(name, TextBrush, 16));
}
