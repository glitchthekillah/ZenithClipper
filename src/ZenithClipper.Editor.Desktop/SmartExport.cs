using System.Diagnostics;
using System.Globalization;
using Avalonia.Platform.Storage;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;

/// <summary>File → Smart export: when the range is one untouched clip, copy the original streams with ffmpeg instead of
/// re-encoding (instant, no quality loss). Stream copy has to start on a keyframe, so the file can begin a moment early.</summary>
public sealed partial class EditorWindow
{
    async Task SmartExport()
    {
        var seq = Sequence; if (seq.Duration < 1) { status.Text = "Add a clip before exporting."; return; }
        long from = Math.Clamp(markIn ?? 0, 0, seq.Duration - 1), to = Math.Clamp(markOut ?? seq.Duration, from + 1, seq.Duration);
        if (SmartRender.Check(Project, from, to, out var reason) is not { } plan) { status.Text = $"Smart export needs an untouched clip: {reason}. Use Export instead."; return; }
        string ext = Path.GetExtension(plan.Media.Path).TrimStart('.'); if (ext.Length == 0) ext = "mp4";
        var file = await StorageProvider.SaveFilePickerAsync(new() { Title = "Smart export (no re-encode)", SuggestedFileName = Project.Name + "." + ext, DefaultExtension = ext, FileTypeChoices = [new(ext.ToUpperInvariant() + " file") { Patterns = ["*." + ext] }] });
        if (file?.TryGetLocalPath() is not { } path) return;
        if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(plan.Media.Path), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) { status.Text = "Choose an output different from the source file."; return; }
        var info = new ProcessStartInfo(FindFfmpeg()) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var a in SmartExportArguments(plan, path)) info.ArgumentList.Add(a);
        status.Text = "Smart exporting…";
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Couldn't start ffmpeg.");
        string log = await process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync();
        if (process.ExitCode != 0 || !File.Exists(path) || new FileInfo(path).Length < 256)
            throw new IOException("Smart export failed: " + log.Split('\n').LastOrDefault(l => l.Trim().Length > 0)?.Trim());
        status.Text = $"Smart exported without re-encoding: {path}. It may start up to one keyframe before your In point.";
    }
    internal static List<string> SmartExportArguments(SmartRender.Plan plan, string output)
    {
        string S(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
        var args = new List<string> { "-hide_banner", "-nostdin", "-y", "-ss", S(plan.StartSeconds), "-to", S(plan.EndSeconds), "-i", plan.Media.Path, "-map", "0:v:0" };
        if (plan.Audio) args.AddRange(["-map", "0:a:0?"]);
        args.AddRange(["-c", "copy", "-avoid_negative_ts", "make_zero"]);
        if (Path.GetExtension(output).ToLowerInvariant() is ".mp4" or ".mov" or ".m4v") args.AddRange(["-movflags", "+faststart"]);
        args.Add(output);
        return args;
    }

    internal void VerifySmartRender()
    {
        string dir = Path.Combine(Path.GetTempPath(), "zc-smart-check"); Directory.CreateDirectory(dir); string src = Path.Combine(dir, "a.mp4"); if (!File.Exists(src)) File.WriteAllText(src, "x");
        var media = new MediaAsset { Name = "a", Path = src, Kind = MediaKind.Video, Width = 1920, Height = 1080, Fps = new FrameRate(30, 1), DurationSeconds = 10, HasAudio = true };
        var seq = new EditSequence { Width = 1920, Height = 1080, Fps = new FrameRate(30, 1), Tracks = [new EditTrack { Kind = TrackKind.Video }, new EditTrack { Kind = TrackKind.Audio }] };
        var link = Guid.NewGuid();
        var v = new TimelineClip { MediaId = media.Id, SourceIn = 60, Duration = 90, LinkId = link, StreamIntent = ClipStreamIntent.VideoOnly };
        var a = new TimelineClip { MediaId = media.Id, SourceIn = 60, Duration = 90, LinkId = link, StreamIntent = ClipStreamIntent.AudioOnly };
        seq.Tracks[0].Clips.Add(v); seq.Tracks[1].Clips.Add(a);
        var p = new EditProject { ActiveSequenceId = seq.Id, Sequences = [seq], Media = [media] };
        var plan = SmartRender.Check(p, 30, 90, out _) ?? throw new InvalidOperationException("An untouched clip was not smart-renderable.");
        if (Math.Abs(plan.StartSeconds - 3) > 1e-9 || Math.Abs(plan.EndSeconds - 5) > 1e-9 || !plan.Audio) throw new InvalidOperationException("Smart render range is wrong.");
        v.Transform.ScaleX = 1.1; if (SmartRender.Check(p, 0, 90, out _) is not null) throw new InvalidOperationException("A scaled clip was smart-rendered.");
        v.Transform.ScaleX = 1; a.GainDb = -3; if (SmartRender.Check(p, 0, 90, out _) is not null) throw new InvalidOperationException("Changed audio was smart-rendered.");
    }
}
