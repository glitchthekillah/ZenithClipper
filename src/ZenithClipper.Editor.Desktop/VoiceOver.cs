using System.Diagnostics;
using System.Text.RegularExpressions;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;

/// <summary>
/// Premiere's Voice-over Record: records the default (or chosen) microphone with ffmpeg while the program plays, then
/// drops the take on an audio track at the frame where recording started.
/// </summary>
public sealed partial class EditorWindow
{
    Process? voiceOver; string? voiceOverPath; long voiceOverStart; DateTime voiceOverBegan;

    /// <summary>Lists DirectShow microphones on Windows (empty elsewhere).</summary>
    internal static async Task<List<string>> Microphones()
    {
        if (!OperatingSystem.IsWindows()) return [];
        var info = new ProcessStartInfo(FindFfmpeg()) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy" }) info.ArgumentList.Add(a);
        using var p = Process.Start(info)!; var text = await p.StandardError.ReadToEndAsync(); await p.WaitForExitAsync();
        return [.. Regex.Matches(text, "\"([^\"]+)\"\\s*\\(audio\\)").Select(m => m.Groups[1].Value).Distinct()];
    }

    async Task ToggleVoiceOver()
    {
        if (voiceOver is not null) { await StopVoiceOver(); return; }
        var mics = await Microphones();
        if (mics.Count == 0) { status.Text = OperatingSystem.IsWindows() ? "No microphone found." : "Voice-over recording needs Windows."; return; }
        string mic = mics[0];
        if (mics.Count > 1 && await Ask("Record voice-over", "Which microphone?", [.. mics.Take(4)]) is { } picked) mic = picked;
        var folder = Path.Combine(AssetDirectory, "voiceover"); Directory.CreateDirectory(folder);
        voiceOverPath = Path.Combine(folder, $"voiceover-{DateTime.Now:yyyyMMdd-HHmmss}.wav");
        var info = new ProcessStartInfo(FindFfmpeg()) { RedirectStandardInput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "-hide_banner", "-nostats", "-f", "dshow", "-audio_buffer_size", "50", "-i", "audio=" + mic, "-ac", "2", "-ar", "48000", "-c:a", "pcm_s16le", "-y", voiceOverPath }) info.ArgumentList.Add(a);
        voiceOver = Process.Start(info) ?? throw new InvalidOperationException("Couldn't start ffmpeg to record.");
        _ = voiceOver.StandardError.ReadToEndAsync();
        voiceOverStart = ProgramPlayhead; voiceOverBegan = DateTime.UtcNow;
        if (!playing) await TogglePlay();
        status.Text = $"● Recording voice-over from {mic}. Press Shift+V (or the menu item) again to stop.";
    }

    async Task StopVoiceOver()
    {
        var process = voiceOver; var path = voiceOverPath; voiceOver = null; voiceOverPath = null;
        if (playing) await TogglePlay();
        if (process is null || path is null) return;
        try { await process.StandardInput.WriteAsync('q'); await process.StandardInput.FlushAsync(); } catch (IOException) { }
        if (!process.WaitForExit(5000)) { try { process.Kill(true); } catch (InvalidOperationException) { } }
        process.Dispose();
        if (!File.Exists(path) || new FileInfo(path).Length < 1000) { status.Text = "The recording was empty."; return; }
        var media = await Probe(path); media.Name = "Voice-over " + DateTime.Now.ToString("HH:mm:ss");
        long at = voiceOverStart;
        Change("Record voice-over", p =>
        {
            var s = p.Sequences.First(x => x.Id == p.ActiveSequenceId); p.Media.Add(media);
            long length = Math.Max(1, s.Fps.Frames(media.DurationSeconds));
            var track = s.Tracks.FirstOrDefault(t => t.Kind == TrackKind.Audio && !t.Locked && t.Name.StartsWith("Voice-over", StringComparison.Ordinal) && !t.Clips.Any(c => c.Start < at + length && c.End > at));
            if (track is null) { track = new EditTrack { Kind = TrackKind.Audio, Name = $"Voice-over {s.Tracks.Count(t => t.Name.StartsWith("Voice-over", StringComparison.Ordinal)) + 1}" }; s.Tracks.Add(track); }
            track.Clips.Add(new TimelineClip { MediaId = media.Id, Name = media.Name, Start = at, Duration = length, StreamIntent = ClipStreamIntent.AudioOnly });
            track.Clips.Sort((a, b) => a.Start.CompareTo(b.Start));
        });
        status.Text = $"Voice-over added ({(DateTime.UtcNow - voiceOverBegan).TotalSeconds:0.0} s).";
    }
}
