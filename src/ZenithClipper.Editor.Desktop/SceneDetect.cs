using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;

/// <summary>Premiere's Scene Edit Detection: cuts the selected clip wherever ffmpeg's scene score jumps.</summary>
public sealed partial class EditorWindow
{
    internal static async Task<List<double>> DetectScenes(string path, double start, double length, double threshold, CancellationToken ct)
    {
        var info = new ProcessStartInfo(FindFfmpeg()) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "-hide_banner", "-nostdin", "-ss", start.ToString("0.###", CultureInfo.InvariantCulture), "-t", length.ToString("0.###", CultureInfo.InvariantCulture), "-i", path, "-an", "-sn", "-dn",
                     "-vf", $"scale=320:-2,select='gt(scene\\,{threshold.ToString("0.###", CultureInfo.InvariantCulture)})',showinfo", "-f", "null", "-" })
            info.ArgumentList.Add(a);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Couldn't start ffmpeg.");
        using var reg = ct.Register(() => { try { process.Kill(true); } catch (InvalidOperationException) { } });
        var times = new List<double>(); var pattern = new Regex(@"pts_time:([0-9.]+)", RegexOptions.CultureInvariant);
        _ = process.StandardOutput.ReadToEndAsync(ct);
        while (await process.StandardError.ReadLineAsync(ct) is { } line)
            if (line.Contains("Parsed_showinfo", StringComparison.Ordinal) && pattern.Match(line) is { Success: true } m) times.Add(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
        await process.WaitForExitAsync(ct);
        return times;
    }

    async Task SceneEditDetection()
    {
        if (selectedClip is not { } id || selectedTrack is not { } trackId) { status.Text = "Select a video clip first."; return; }
        var seq = Sequence; var clip = seq.Tracks.SelectMany(t => t.Clips).First(c => c.Id == id);
        var media = Project.Media.First(m => m.Id == clip.MediaId);
        if (media.Kind != MediaKind.Video || !File.Exists(media.Path)) { status.Text = "Scene detection works on video clips."; return; }
        if (TimeRemap.Has(clip) || clip.Reverse) { status.Text = "Turn off time remapping and reverse before detecting scenes."; return; }
        status.Text = "Detecting scene changes…";
        double fps = seq.Fps.Value, start = clip.SourceIn / fps, length = clip.Duration * clip.Speed / fps;
        var times = await DetectScenes(media.Path, start, length, .35, CancellationToken.None);
        var frames = times.Select(t => clip.Start + (long)Math.Round(t * fps / clip.Speed)).Where(f => f > clip.Start + 2 && f < clip.End - 2).Distinct().OrderBy(f => f).ToList();
        if (frames.Count == 0) { status.Text = "No scene changes found in this clip."; return; }
        long origin = clip.Start; var track = trackId;
        Change("Scene edit detection", p =>
        {
            var s = p.Sequences.First(x => x.Id == p.ActiveSequenceId);
            foreach (var f in frames)
            {
                var piece = s.Tracks.First(t => t.Id == track).Clips.FirstOrDefault(c => c.Start < f && c.End > f);
                if (piece is not null) EditCommands.Split(p, p.ActiveSequenceId, track, piece.Id, f);
            }
        });
        status.Text = $"Added {frames.Count} cut{(frames.Count == 1 ? "" : "s")} at scene changes.";
    }
}
