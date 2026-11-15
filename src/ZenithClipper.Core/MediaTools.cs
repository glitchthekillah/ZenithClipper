using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace ZenithClipper;

public static class MediaTools
{
    public static string FindFfmpeg(Settings s)
    {
        if (!string.IsNullOrWhiteSpace(s.FfmpegPath)) return s.FfmpegPath;
        var local = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
        return File.Exists(local) ? local : "ffmpeg";
    }
    public static Process Start(string executable, IEnumerable<string> args, bool stdin = false)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true, RedirectStandardInput = stdin };
        foreach (string arg in args) info.ArgumentList.Add(arg);
        var process = Process.Start(info) ?? throw new IOException($"Could not start {executable}");
        LowerPriority(process); return process;
    }
    public static void LowerPriority(Process process)
    {
        if(!OperatingSystem.IsWindows())return;
        try { process.PriorityClass=ProcessPriorityClass.BelowNormal; }
        catch(Exception ex) when(ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }
    public static async Task<string> RunAsync(string executable, IEnumerable<string> args, CancellationToken ct = default)
    {
        using var process = Start(executable, args);
        var error = process.StandardError.ReadToEndAsync(ct); var output = process.StandardOutput.ReadToEndAsync(ct);
        try { await process.WaitForExitAsync(ct); }
        catch { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); } throw; }
        var stderr = await error; string stdout = await output;
        if (process.ExitCode != 0) throw new IOException($"{Path.GetFileName(executable)} failed ({process.ExitCode}): {stderr[^Math.Min(stderr.Length, 4000)..]}");
        return stdout;
    }
    public static async Task<string> ResolveEncoder(Settings s, CancellationToken ct)
    {
        if (s.Encoder != "auto") return s.Encoder;
        var candidates = OperatingSystem.IsWindows() ? EncoderSelection.WindowsCandidates(WindowsDisplay.PreferredEncoder(s.Monitor)) : new[] { "vaapi", "nvenc", "qsv" };
        foreach (string encoder in candidates)
        {
            var trial = s with { Encoder = encoder };
            var args = new List<string> { "-v", "error" };
            if (encoder == "vaapi") args.AddRange(["-vaapi_device", s.LinuxRenderNode]);
            args.AddRange(["-f", "lavfi", "-i", "color=s=320x180:r=30", "-frames:v", "1"]);
            if (encoder == "vaapi") args.AddRange(["-vf", "format=nv12,hwupload"]);
            args.AddRange(CaptureCommand.EncoderArgs(trial)); args.AddRange(["-f", "null", "-"]);
            try { using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(12)); await RunAsync(FindFfmpeg(s), args, timeout.Token); return encoder; }
            catch (Exception ex) when (ex is IOException or OperationCanceledException) { ct.ThrowIfCancellationRequested(); }
        }
        throw new IOException("No usable hardware encoder found. Update GPU drivers or explicitly select software encoding (higher CPU use).");
    }
    public static async Task<string> ProbeAsync(string ffmpeg, string path, CancellationToken ct = default)
    {
        string ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg) ?? "", OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        return await RunAsync(ffprobe, ["-v", "error", "-show_format", "-show_streams", "-of", "json", path], ct);
    }
    public static string Number(double n) => n.ToString("0.########", CultureInfo.InvariantCulture);
}

public sealed record EditRequest(string Input, string Output)
{
    public double StartSeconds { get; init; }
    public double? DurationSeconds { get; init; }
    public bool FastTrim { get; init; } = true;
    public bool Mute { get; init; }
    public double Volume { get; init; } = 1;
    public int CropWidth { get; init; }
    public int CropHeight { get; init; }
    public int CropX { get; init; }
    public int CropY { get; init; }
    public int OutputWidth { get; init; }
    public double Speed { get; init; } = 1;
    public int Quality { get; init; } = 20;
    public string Encoder { get; init; } = "auto";
    public void Validate()
    {
        if (Path.GetFullPath(Input).Equals(Path.GetFullPath(Output), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new ArgumentException("Export must use a different file from the original.");
        if (!double.IsFinite(StartSeconds) || StartSeconds < 0 || DurationSeconds is { } d && (!double.IsFinite(d) || d <= 0)) throw new ArgumentException("Invalid trim times.");
        if (!double.IsFinite(Volume) || Volume < 0 || Volume > 4 || !double.IsFinite(Speed) || Speed < .5 || Speed > 2) throw new ArgumentException("Volume must be 0–4 and speed 0.5–2.");
        if (CropWidth < 0 || CropHeight < 0 || CropX < 0 || CropY < 0 || (CropWidth == 0) != (CropHeight == 0) || CropWidth % 2 != 0 || CropHeight % 2 != 0 || OutputWidth < 0 || OutputWidth % 2 != 0) throw new ArgumentException("Crop needs positive even width and height, or both zero.");
        if (Quality is < 0 or > 51) throw new ArgumentException("Quality must be 0–51.");
    }
}

public static class Editor
{
    public static async Task ExportAsync(Settings settings, EditRequest edit, CancellationToken ct = default)
    {
        edit.Validate(); if (!File.Exists(edit.Input)) throw new FileNotFoundException("Clip does not exist.", edit.Input);
        if (File.Exists(edit.Output)) throw new IOException("Output already exists; choose a new filename.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(edit.Output))!);
        string temp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(edit.Output))!, $".export-{Guid.NewGuid():N}.mp4");
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y" };
        bool copy = edit.FastTrim && edit.CropWidth == 0 && edit.OutputWidth == 0 && edit.Volume == 1 && edit.Speed == 1;
        args.AddRange(["-ss", MediaTools.Number(edit.StartSeconds)]);
        if(!copy && edit.DurationSeconds is {} inputDuration) args.AddRange(["-t",MediaTools.Number(inputDuration)]);
        args.AddRange(["-i", edit.Input]);
        if (copy && edit.DurationSeconds is { } duration) args.AddRange(["-t", MediaTools.Number(duration)]);
        args.AddRange(["-map", "0:v:0"]); if (!edit.Mute) args.AddRange(["-map", "0:a:0?"]);
        if (copy) args.AddRange(["-c", "copy"]);
        else
        {
            var s = settings with { Encoder = edit.Encoder, Quality = edit.Quality };
            s = s with { Encoder = await MediaTools.ResolveEncoder(s, ct) };
            var filters = new List<string>();
            if (edit.CropWidth > 0) filters.Add($"crop={edit.CropWidth}:{edit.CropHeight}:{edit.CropX}:{edit.CropY}");
            if (edit.OutputWidth > 0) filters.Add($"scale={edit.OutputWidth}:-2");
            if (edit.Speed != 1) filters.Add($"setpts=PTS/{MediaTools.Number(edit.Speed)}");
            if (s.Encoder == "vaapi") { args.InsertRange(0, ["-vaapi_device", s.LinuxRenderNode]); filters.Add("format=nv12,hwupload"); }
            if (filters.Count > 0) args.AddRange(["-vf", string.Join(',', filters)]);
            args.AddRange(CaptureCommand.EncoderArgs(s));
            if (!edit.Mute) args.AddRange(["-af", $"volume={MediaTools.Number(edit.Volume)},atempo={MediaTools.Number(edit.Speed)}", "-c:a", "aac", "-b:a", "192k"]);
        }
        args.AddRange(["-movflags", "+faststart", "-avoid_negative_ts", "make_zero", temp]);
        try { await MediaTools.RunAsync(MediaTools.FindFfmpeg(settings), args, ct); File.Move(temp, edit.Output, false); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
