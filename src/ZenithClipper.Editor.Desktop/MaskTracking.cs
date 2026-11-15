using System.Diagnostics;
using System.Globalization;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;

/// <summary>Track selected mask forward: OpenCV's KCF tracker runs in a short-lived helper process (this program with
/// --track-rect) so the editor itself never loads OpenCV. The tracked centres are stored on the mask.</summary>
public sealed partial class EditorWindow
{
    bool maskTracking;
    async Task TrackMask(Guid clipId)
    {
        var seq = Sequence; var clip = seq.Tracks.SelectMany(t => t.Clips).First(c => c.Id == clipId);
        if (clip.Mask is not { Shape: "ellipse" or "rectangle" } mask) { status.Text = "Tracking works on ellipse and rectangle masks."; return; }
        var media = Project.Media.First(m => m.Id == clip.MediaId);
        if (media.Kind != MediaKind.Video || !File.Exists(media.Path)) { status.Text = "Mask tracking needs a video clip whose file is available."; return; }
        if (Math.Abs(clip.Speed - 1) > 1e-9 || clip.Reverse || TimeRemap.Has(clip)) { status.Text = "Set the clip to 100% speed, forward, without time remapping before tracking."; return; }
        long from = Math.Clamp(ProgramPlayhead - clip.Start, 0, clip.Duration - 1), frames = clip.Duration - from;
        if (frames < 2) { status.Text = "Move the playhead inside the clip, before its last frame."; return; }
        // Analyse at 640 px wide (fast, and plenty for following an object).
        int aw = 640, ah = Math.Max(2, (int)Math.Round(640.0 * seq.Height / seq.Width / 2) * 2);
        // The mask's position at the start frame (an earlier track, if any, already moved it).
        double cx = mask.X, cy = mask.Y;
        for (int i = 0; i + 2 < mask.Track.Count; i += 3) if (mask.Track[i] <= from) { cx = mask.Track[i + 1]; cy = mask.Track[i + 2]; }
        int rw = Math.Max(8, (int)Math.Round(mask.Width * aw)), rh = Math.Max(8, (int)Math.Round(mask.Height * ah));
        int rx = Math.Clamp((int)Math.Round(cx * aw - rw / 2.0), 0, aw - rw), ry = Math.Clamp((int)Math.Round(cy * ah - rh / 2.0), 0, ah - rh);
        string output = Path.Combine(Path.GetTempPath(), "zc-track-" + Guid.NewGuid().ToString("N") + ".txt");
        var info = SelfProcess();
        foreach (var a in new[] { "--track-rect", media.Path, aw.ToString(CultureInfo.InvariantCulture), ah.ToString(CultureInfo.InvariantCulture), seq.Fps.Numerator.ToString(CultureInfo.InvariantCulture), seq.Fps.Denominator.ToString(CultureInfo.InvariantCulture),
            (clip.SourceIn + from).ToString(CultureInfo.InvariantCulture), frames.ToString(CultureInfo.InvariantCulture), rx.ToString(CultureInfo.InvariantCulture), ry.ToString(CultureInfo.InvariantCulture), rw.ToString(CultureInfo.InvariantCulture), rh.ToString(CultureInfo.InvariantCulture), output })
            info.ArgumentList.Add(a);
        maskTracking = true; BuildInspector();
        try
        {
            using var process = Process.Start(info) ?? throw new InvalidOperationException("Couldn't start the tracker.");
            _ = process.StandardError.ReadToEndAsync();
            while (await process.StandardOutput.ReadLineAsync() is { } line)
                if (line.StartsWith("progress ", StringComparison.Ordinal) && double.TryParse(line[9..], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) status.Text = $"Tracking mask… {v * 100:0}%";
            await process.WaitForExitAsync();
            if (process.ExitCode != 0 || !File.Exists(output)) throw new InvalidOperationException("Tracking failed. The clip may use a format the tracker can't read.");
            var points = new List<double>();
            foreach (var row in File.ReadAllLines(output))
            {
                var v = row.Split(' ').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray(); if (v.Length < 5) continue;
                long local = (long)v[0] - clip.SourceIn; if (local < from || local >= clip.Duration) continue;
                points.AddRange([local, Math.Round((v[1] + v[3] / 2) / aw, 5), Math.Round((v[2] + v[4] / 2) / ah, 5)]);
            }
            if (points.Count == 0) throw new InvalidOperationException("The tracker couldn't follow anything in the mask. Try a larger mask around a detailed area.");
            Change("Track mask", p => EditCommands.ChangeLinkedClip(p, p.ActiveSequenceId, clipId, c =>
            {
                var keep = new List<double>(); for (int i = 0; i + 2 < c.Mask!.Track.Count; i += 3) if (c.Mask.Track[i] < from) keep.AddRange(c.Mask.Track.Skip(i).Take(3));
                c.Mask.Track = [.. keep, .. points];
            }));
            status.Text = $"Mask tracked across {points.Count / 3} frames.";
        }
        finally { maskTracking = false; try { File.Delete(output); } catch (IOException) { } BuildInspector(); }
    }
    /// <summary>Starts this same program (exe, or dotnet + dll when run that way) for a helper task.</summary>
    static ProcessStartInfo SelfProcess()
    {
        string host = Environment.ProcessPath ?? "dotnet"; var info = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) info.ArgumentList.Add(typeof(EditorWindow).Assembly.Location);
        return info;
    }
}
