using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using ZenithClipper.Editing;
using ZenithClipper.Mlt;

namespace ZenithClipper.Editor.Desktop;
using Control = Avalonia.Controls.Control;

/// <summary>Loudness (LUFS) meter in the mixer, and Premiere's export loudness normalization as "Match loudness".</summary>
public sealed partial class EditorWindow
{
    readonly TextBlock lufsMomentary = new(), lufsShort = new(), lufsIntegrated = new(), lufsPeak = new();

    static string Lu(double v) => double.IsFinite(v) ? $"{v:0.0}" : "–∞";

    void UpdateLoudnessReadout()
    {
        if (rightPanel != "Mixer") return;
        var r = engine.Loudness.Read();
        lufsMomentary.Text = Lu(r.Momentary); lufsShort.Text = Lu(r.ShortTerm); lufsIntegrated.Text = Lu(r.Integrated); lufsPeak.Text = Lu(r.PeakDb);
    }

    Control LoudnessCard()
    {
        var card = new Border { Background = ControlBrush, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8), Margin = new Thickness(0, 6, 0, 0) };
        var stack = new StackPanel { Spacing = 6 };
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        head.Children.Add(Copy("LOUDNESS (LUFS)", 10.5, FaintBrush, Avalonia.Media.FontWeight.SemiBold));
        var reset = Button("Reset", () => { engine.Loudness.Reset(); UpdateLoudnessReadout(); }); reset.Height = 22; reset.Padding = new Thickness(8, 0); reset.FontSize = 11; reset.HorizontalAlignment = HorizontalAlignment.Right;
        ToolTip.SetTip(reset, "Start measuring again from the next playback."); Grid.SetColumn(reset, 1); head.Children.Add(reset); stack.Children.Add(head);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };
        foreach (var (i, label, value, tip) in new[] { (0, "Momentary", lufsMomentary, "Last 400 ms"), (1, "Short-term", lufsShort, "Last 3 s"), (2, "Integrated", lufsIntegrated, "Everything played since reset (gated, EBU R128)"), (3, "Peak dB", lufsPeak, "Highest sample since reset") })
        {
            var cell = new StackPanel { Spacing = 1 }; value.FontSize = 15; value.Foreground = TextBrush; value.FontWeight = Avalonia.Media.FontWeight.SemiBold; value.FontFamily = new Avalonia.Media.FontFamily("Consolas, Menlo, monospace");
            (value.Parent as Panel)?.Children.Remove(value);
            cell.Children.Add(value); cell.Children.Add(Copy(label, 10.5, FaintBrush)); ToolTip.SetTip(cell, tip); Grid.SetColumn(cell, i); grid.Children.Add(cell);
        }
        stack.Children.Add(grid);
        var targets = new ComboBox { ItemsSource = new[] { "-14 LUFS · YouTube, Spotify", "-16 LUFS · Podcasts, Apple", "-23 LUFS · Broadcast (EBU R128)", "-24 LUFS · Broadcast (ATSC A/85)" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12 };
        var match = Button("Match loudness", () => Run(() => MatchLoudness(new[] { -14.0, -16, -23, -24 }[Math.Max(0, targets.SelectedIndex)])));
        ToolTip.SetTip(match, "Measures the whole sequence offline and shifts every track's volume so it hits the target. Peaks are kept under -1 dB.");
        stack.Children.Add(targets); stack.Children.Add(match);
        card.Child = stack; UpdateLoudnessReadout(); return card;
    }

    /// <summary>Renders the sequence's audio to a temporary WAV, measures integrated loudness and offsets every track.</summary>
    async Task MatchLoudness(double target)
    {
        if (Sequence.Duration <= 0) { status.Text = "Add clips before matching loudness."; return; }
        if (exportFinished is not null) { status.Text = "Finish the export first."; return; }
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); exportFinished = finished;
        string temp = Path.Combine(Path.GetTempPath(), $"zenith-loudness-{Guid.NewGuid():N}.wav");
        try
        {
            InvalidatePreview();
            if (playing) { playing = false; UpdateTransportState(); await engine.PauseAsync(); }
            var wav = ExportPresets.All.First(p => p.Extension == "wav");
            var preset = wav with { Properties = [.. wav.Properties, ("frequency", "48000"), ("channels", "2")] };
            status.Text = "Measuring loudness…"; progress.IsVisible = true;
            await renderGate.WaitAsync();
            try { await engine.ExportAsync(ProjectCopy, temp, new ExportOptions(preset), new Progress<double>(v => { if (!editorClosed) progress.Value = v * 100; })); }
            finally { renderGate.Release(); loadedRevision = -1; }
            var reading = await Task.Run(() => LoudnessMeter.MeasureWav(temp));
            if (!double.IsFinite(reading.Integrated)) { status.Text = "The sequence is silent, nothing to match."; return; }
            double delta = target - reading.Integrated, headroom = -1 - reading.PeakDb;
            bool limited = delta > headroom; if (limited) delta = headroom;
            delta = Math.Round(delta, 1);
            if (Math.Abs(delta) < .05) { status.Text = $"Already at {reading.Integrated:0.0} LUFS."; return; }
            finished.TrySetResult(); exportFinished = null;
            Change("Match loudness", p => { foreach (var t in p.Sequences.First(s => s.Id == p.ActiveSequenceId).Tracks) t.GainDb = Math.Clamp(Math.Round(t.GainDb + delta, 1), -60, 12); });
            status.Text = $"Measured {reading.Integrated:0.0} LUFS, peak {reading.PeakDb:0.0} dB. Tracks {(delta > 0 ? "+" : "")}{delta:0.0} dB" + (limited ? $" (limited by peaks; lands near {reading.Integrated + delta:0.0} LUFS)." : $" → {target:0} LUFS.");
            UpdateMixer();
        }
        finally
        {
            progress.IsVisible = false; exportFinished = null; finished.TrySetResult();
            try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    Control DuckingCard()
    {
        var audio = Sequence.Tracks.Where(t => t.Kind == TrackKind.Audio).ToList();
        var card = new Border { Background = ControlBrush, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8), Margin = new Thickness(0, 6, 0, 0) };
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(Copy("AUTO-DUCK MUSIC", 10.5, FaintBrush, Avalonia.Media.FontWeight.SemiBold));
        var music = new ComboBox { ItemsSource = audio.Select(t => t.Name).ToList(), SelectedIndex = audio.Count > 1 ? audio.Count - 1 : 0, HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12 };
        var amount = new ComboBox { ItemsSource = new[] { "Duck by 6 dB", "Duck by 12 dB", "Duck by 18 dB", "Duck by 24 dB" }, SelectedIndex = 1, HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12 };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,6,*") }; Grid.SetColumn(amount, 2); row.Children.Add(music); row.Children.Add(amount); stack.Children.Add(row);
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,6,Auto") };
        var duck = Button("Duck under dialogue", () => { if (music.SelectedIndex >= 0 && music.SelectedIndex < audio.Count) DuckMusic(audio[music.SelectedIndex].Id, 6 * (amount.SelectedIndex + 1)); });
        var clear = Button("Clear", () => { if (music.SelectedIndex >= 0 && music.SelectedIndex < audio.Count) { var id = audio[music.SelectedIndex].Id; Change("Clear ducking", p => { foreach (var c in p.Sequences.First(s => s.Id == p.ActiveSequenceId).Tracks.First(t => t.Id == id).Clips) c.Motion.RemoveAll(k => k.Parameter == "volume"); }); } });
        ToolTip.SetTip(duck, "Adds volume keyframes to the music track's clips that dip whenever any other track has sound. Re-run after editing.");
        ToolTip.SetTip(clear, "Removes all volume keyframes from the music track.");
        Grid.SetColumn(clear, 2); buttons.Children.Add(duck); buttons.Children.Add(clear); stack.Children.Add(buttons);
        if (audio.Count == 0) stack.IsEnabled = false;
        card.Child = stack; return card;
    }

    /// <summary>Dialogue = every other audible clip (audio tracks and video clips that carry their own sound).</summary>
    void DuckMusic(Guid musicTrack, double db)
    {
        var project = ProjectCopy; var seq = project.Sequences.First(s => s.Id == project.ActiveSequenceId);
        var sources = new List<(TimelineClip Clip, byte[] Peaks)>(); bool loading = false;
        foreach (var track in seq.Tracks.Where(t => t.Id != musicTrack && !t.Muted))
        foreach (var clip in track.Clips.Where(c => c.Enabled))
        {
            var media = project.Media.FirstOrDefault(m => m.Id == clip.MediaId);
            if (media is null || media.Kind is not (MediaKind.Video or MediaKind.Audio) || !media.HasAudio || clip.StreamIntent == ClipStreamIntent.VideoOnly) continue;
            if (track.Kind == TrackKind.Video && clip.LinkId is { } l && seq.Tracks.Any(t => t.Kind == TrackKind.Audio && t.Clips.Any(c => c.LinkId == l))) continue;
            var entry = MediaPreviews.Get(media);
            if (entry is null) continue;
            if (!entry.PeaksDone || entry.Peaks is null) { loading = true; continue; }
            sources.Add((clip, entry.Peaks));
        }
        if (loading) { status.Text = "Waveforms are still loading. Try again in a moment."; return; }
        if (sources.Count == 0) { status.Text = "No dialogue found on the other tracks."; return; }
        var fps = seq.Fps; const double threshold = -38; // dBFS: speech is well above, room tone below
        bool Active(long f)
        {
            foreach (var (clip, peaks) in sources)
            {
                if (f < clip.Start || f >= clip.End) continue;
                double seconds = (clip.Reverse ? clip.SourceIn + (clip.End - 1 - f) * clip.Speed : clip.SourceIn + (f - clip.Start) * clip.Speed) / fps.Value;
                int i = (int)(seconds * MediaPreviews.PeaksPerSecond); if (i < 0 || i >= peaks.Length) continue;
                double level = peaks[i] / 255.0; level *= level; // peaks are stored as sqrt(level)
                double gain = clip.GainDb + seq.Tracks.First(t => t.Clips.Contains(clip)).GainDb;
                if (level > 0 && 20 * Math.Log10(level) + gain > threshold) return true;
            }
            return false;
        }
        int changed = 0;
        // Activity is read from the snapshot above; the edit itself runs on the live project.
        var activity = new Dictionary<long, bool>();
        Change("Auto-duck music", p => changed = Ducking.Apply(p.Sequences.First(s => s.Id == p.ActiveSequenceId), musicTrack, f => activity.TryGetValue(f, out var a) ? a : activity[f] = Active(f), db));
        status.Text = changed == 0 ? "No music clips overlap dialogue." : $"Ducked {changed} music clip{(changed == 1 ? "" : "s")} by {db:0} dB under dialogue.";
    }

    /// <summary>Audio Clip Mixer: volume of each audible clip under the playhead (keyframed clips get a keyframe).</summary>
    void ClipMixer(StackPanel list)
    {
        var seq = Sequence; long at = ProgramPlayhead; var media = Project.Media.ToDictionary(m => m.Id);
        var clips = seq.Tracks.Where(t => t.Kind == TrackKind.Audio || t.Clips.Any()).SelectMany(t => t.Clips.Select(c => (Track: t, Clip: c)))
            .Where(x => x.Clip.Start <= at && x.Clip.End > at && media.TryGetValue(x.Clip.MediaId, out var m) && m.HasAudio && x.Clip.StreamIntent != ClipStreamIntent.VideoOnly && (x.Track.Kind == TrackKind.Audio || x.Clip.LinkId is null)).ToList();
        list.Children.Add(Copy("CLIPS AT PLAYHEAD", 10.5, FaintBrush, Avalonia.Media.FontWeight.SemiBold));
        if (clips.Count == 0) { list.Children.Add(Copy("No audio under the playhead.", 11, FaintBrush)); return; }
        foreach (var (track, clip) in clips)
        {
            var id = clip.Id; long rel = at - clip.Start; bool keyed = clip.Motion.Any(k => k.Parameter == "volume");
            double now = keyed ? ClipBlend.At(clip, "volume", rel) ?? clip.GainDb : clip.GainDb;
            list.Children.Add(Copy($"{track.Name} · {clip.Name}", 11.5, SoftTextBrush));
            list.Children.Add(MixerSlider("Volume", now, -60, 12, "dB", v => Change("Clip volume", p => EditCommands.ChangeLinkedClip(p, p.ActiveSequenceId, id, c =>
            {
                if (c.Motion.Any(k => k.Parameter == "volume")) SetKeyframe(c, "volume", rel, Math.Round(v, 1)); else c.GainDb = Math.Round(v, 1);
            }))));
        }
    }
}
