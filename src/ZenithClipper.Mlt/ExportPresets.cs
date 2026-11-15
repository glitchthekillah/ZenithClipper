using System.Globalization;

namespace ZenithClipper.Mlt;

/// <summary>An export recipe: container, codecs and their settings, optional output size and target file size.</summary>
public sealed record ExportPreset(string Name, string Group, string Extension, string Description, (string Key, string Value)[] Properties, int Width = 0, int Height = 0, double? TargetMegabytes = null, int AudioKbps = 192, bool AudioOnly = false);

/// <summary>What to export: a preset plus the frame range (Premiere's "Entire sequence" or "In to Out").</summary>
public sealed record ExportOptions(ExportPreset Preset, long? In = null, long? Out = null);

/// <summary>Premiere-style export presets, tuned for where clips actually go. All encoders are CPU encoders unless named NVIDIA.</summary>
public static class ExportPresets
{
    static (string, string)[] H264(int crf, string preset = "medium") => [("f", "mp4"), ("vcodec", "libx264"), ("crf", crf.ToString(CultureInfo.InvariantCulture)), ("preset", preset), ("pix_fmt", "yuv420p"), ("acodec", "aac"), ("movflags", "+faststart")];
    public static readonly ExportPreset[] All =
    [
        new("Match sequence · H.264 high quality", "General", "mp4", "Same size as the sequence. Visually lossless for most footage.", H264(18, "slow")),
        new("Match sequence · H.264 fast", "General", "mp4", "Quick render, larger file.", H264(20, "veryfast")),
        new("YouTube 1080p · H.264", "Social", "mp4", "1920×1080, YouTube's recommended H.264 settings.", H264(18, "slow"), 1920, 1080, AudioKbps: 384),
        new("YouTube 1440p · H.265", "Social", "mp4", "2560×1440; YouTube re-encodes 1440p+ with a better codec.", [("f", "mp4"), ("vcodec", "libx265"), ("crf", "20"), ("preset", "medium"), ("pix_fmt", "yuv420p"), ("tag", "hvc1"), ("acodec", "aac"), ("movflags", "+faststart")], 2560, 1440, AudioKbps: 384),
        new("YouTube 4K · H.265", "Social", "mp4", "3840×2160 H.265.", [("f", "mp4"), ("vcodec", "libx265"), ("crf", "20"), ("preset", "medium"), ("pix_fmt", "yuv420p"), ("tag", "hvc1"), ("acodec", "aac"), ("movflags", "+faststart")], 3840, 2160, AudioKbps: 384),
        new("Twitter / X · 1080p", "Social", "mp4", "1920×1080 H.264 under X's bitrate cap.", [.. H264(21), ("maxrate", "20M"), ("bufsize", "40M")], 1920, 1080),
        new("Discord · 10 MB", "Discord", "mp4", "Fits Discord's free upload limit.", H264(0, "medium"), TargetMegabytes: 10, AudioKbps: 96),
        new("Discord · 50 MB (Nitro Basic)", "Discord", "mp4", "Fits a 50 MB upload.", H264(0, "medium"), TargetMegabytes: 50, AudioKbps: 128),
        new("Discord · 500 MB (Nitro)", "Discord", "mp4", "Fits a 500 MB upload.", H264(0, "medium"), TargetMegabytes: 500, AudioKbps: 192),
        new("Small file · H.265", "Archive", "mp4", "About half the size of H.264 at the same quality.", [("f", "mp4"), ("vcodec", "libx265"), ("crf", "24"), ("preset", "medium"), ("pix_fmt", "yuv420p"), ("tag", "hvc1"), ("acodec", "aac"), ("movflags", "+faststart")]),
        new("Smallest file · AV1", "Archive", "mp4", "AV1 (SVT). Smallest files; slower to encode, newer players only.", [("f", "mp4"), ("vcodec", "libsvtav1"), ("crf", "32"), ("preset", "8"), ("pix_fmt", "yuv420p"), ("acodec", "aac"), ("movflags", "+faststart")]),
        new("NVIDIA · H.264 (NVENC)", "Hardware", "mp4", "GPU encoding on an NVIDIA card. Fails clearly if NVENC isn't available.", [("f", "mp4"), ("vcodec", "h264_nvenc"), ("rc", "vbr"), ("cq", "19"), ("preset", "p5"), ("pix_fmt", "yuv420p"), ("acodec", "aac"), ("movflags", "+faststart")]),
        new("NVIDIA · HEVC (NVENC)", "Hardware", "mp4", "GPU HEVC on an NVIDIA card.", [("f", "mp4"), ("vcodec", "hevc_nvenc"), ("rc", "vbr"), ("cq", "22"), ("preset", "p5"), ("pix_fmt", "yuv420p"), ("tag", "hvc1"), ("acodec", "aac"), ("movflags", "+faststart")]),
        new("Master · ProRes 422 HQ", "Master", "mov", "Edit-friendly master for other editors (large).", [("f", "mov"), ("vcodec", "prores_ks"), ("profile", "3"), ("pix_fmt", "yuv422p10le"), ("acodec", "pcm_s16le")]),
        new("MOV · H.264", "Formats", "mov", "Same quality as the MP4 preset in a QuickTime file (Final Cut, iMovie).", [("f", "mov"), ("vcodec", "libx264"), ("crf", "18"), ("preset", "medium"), ("pix_fmt", "yuv420p"), ("acodec", "aac"), ("movflags", "+faststart")]),
        new("MKV · H.264", "Formats", "mkv", "Matroska container; plays in VLC, OBS and most players.", [("f", "matroska"), ("vcodec", "libx264"), ("crf", "18"), ("preset", "medium"), ("pix_fmt", "yuv420p"), ("acodec", "aac")]),
        new("WebM · VP9", "Formats", "webm", "For web pages and browsers. VP9 video, Opus audio.", [("f", "webm"), ("vcodec", "libvpx-vp9"), ("crf", "32"), ("vb", "0"), ("deadline", "good"), ("cpu-used", "4"), ("pix_fmt", "yuv420p"), ("acodec", "libopus")], AudioKbps: 160),
        new("Animated GIF · 480p", "Formats", "gif", "No sound, 480p. Best for short loops; long GIFs get big fast.", [("f", "gif"), ("vcodec", "gif"), ("pix_fmt", "rgb8"), ("an", "1")], 854, 480),
        new("Audio only · WAV", "Audio", "wav", "Uncompressed 16-bit stereo.", [("f", "wav"), ("vn", "1"), ("acodec", "pcm_s16le")], AudioOnly: true),
        new("Audio only · MP3 320", "Audio", "mp3", "320 kbps MP3.", [("f", "mp3"), ("vn", "1"), ("acodec", "libmp3lame")], AudioKbps: 320, AudioOnly: true),
        new("Audio only · M4A (AAC)", "Audio", "m4a", "AAC in an MP4 audio file; what phones and iTunes expect.", [("f", "ipod"), ("vn", "1"), ("acodec", "aac")], AudioKbps: 256, AudioOnly: true),
        new("Audio only · FLAC", "Audio", "flac", "Lossless and about half the size of WAV.", [("f", "flac"), ("vn", "1"), ("acodec", "flac")], AudioOnly: true),
    ];

    /// <summary>
    /// A preset's size names its short edge (1080p, 1440p, 4K); the sequence's shape is kept, so vertical and square
    /// edits are scaled, never stretched. Sizes stay even for the encoder.
    /// </summary>
    public static (int Width, int Height) OutputSize(ExportPreset preset, int sequenceWidth, int sequenceHeight)
    {
        if (preset.Width <= 0 || sequenceWidth <= 0 || sequenceHeight <= 0) return (preset.Width > 0 ? preset.Width : sequenceWidth, preset.Height > 0 ? preset.Height : sequenceHeight);
        double scale = Math.Min(preset.Width, preset.Height) / (double)Math.Min(sequenceWidth, sequenceHeight);
        int Even(double v) => Math.Max(2, (int)Math.Round(v / 2) * 2);
        return (Even(sequenceWidth * scale), Even(sequenceHeight * scale));
    }
    /// <summary>Encoder properties for a preset over a given duration, including the bitrate that fits a target size.</summary>
    public static List<(string Key, string Value)> Resolve(ExportPreset preset, double seconds, int sequenceWidth = 0, int sequenceHeight = 0)
    {
        var props = preset.Properties.Where(p => !(preset.TargetMegabytes is not null && p.Key == "crf")).ToList();
        props.Add(("ab", $"{preset.AudioKbps}k"));
        if (preset.Width > 0)
        {
            var (w, h) = OutputSize(preset, sequenceWidth, sequenceHeight);
            props.Add(("width", w.ToString(CultureInfo.InvariantCulture))); props.Add(("height", h.ToString(CultureInfo.InvariantCulture)));
        }
        if (preset.TargetMegabytes is { } mb)
        {
            // 10% headroom: the MP4 container plus x264 overshoot on short clips (measured ~6% at 3 s).
            double totalKbps = mb * 1000 * 1000 * 8 * .90 / Math.Max(.1, seconds) / 1000, videoKbps = Math.Max(100, totalKbps - preset.AudioKbps);
            string vb = $"{Math.Floor(videoKbps)}k";
            props.Add(("vb", vb)); props.Add(("maxrate", vb)); props.Add(("bufsize", vb));
        }
        return props;
    }
}
