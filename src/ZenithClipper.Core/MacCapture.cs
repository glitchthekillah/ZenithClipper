using System.Diagnostics;
using System.Globalization;

namespace ZenithClipper;

/// <summary>On macOS the app's own executable doubles as the ScreenCaptureKit helper: it writes paced NV12 frames to stdout
/// and system audio (minus excluded apps) to a FIFO, and FFmpeg encodes both with VideoToolbox.</summary>
public static class MacCapture
{
    public static string Helper => Environment.GetEnvironmentVariable("ZENITHCLIPPER_MAC_HELPER") is { Length: > 0 } configured
        ? configured : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "MacOS", "ZenithClipper"));

    /// <summary>Frame size after scaling (even numbers) and the display's refresh rate.</summary>
    public static async Task<(string Size, int Hz)> ProbeAsync(Settings s, CancellationToken ct)
    {
        string output = await MediaTools.RunAsync(Helper, ["--size", $"{s.Monitor}", $"{s.OutputWidth}"], ct);
        var parts = output.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int hz))
            throw new IOException("Screen capture is not available. Allow ZenithClipper in System Settings > Privacy & Security > Screen Recording, then restart it.");
        return (parts[0], Math.Clamp(hz, 2, 240));
    }

    public static async Task<string> CreateAudioFifoAsync(string directory, CancellationToken ct)
    {
        string path = Path.Combine(directory, "system-audio.pcm");
        await MediaTools.RunAsync("/usr/bin/mkfifo", [path], ct);
        return path;
    }

    public static Process Start(Settings s, string size, int fps, string? audioFifo)
        => MediaTools.Start(Helper, ["--capture", $"{s.Monitor}", size, $"{fps}", audioFifo ?? "-", string.Join(',', s.ExcludedApps)]);
}
