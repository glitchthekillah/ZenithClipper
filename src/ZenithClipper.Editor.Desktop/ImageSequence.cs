using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Platform.Storage;

namespace ZenithClipper.Editor.Desktop;

/// <summary>Import numbered stills (frame_0001.png …) as one clip, like Premiere's "Image Sequence" import option.</summary>
public sealed partial class EditorWindow
{
    /// <summary>For "shot_0042.png" returns ("shot_%04d.png", 42); null when the name has no trailing number.</summary>
    internal static (string Pattern, int Start)? SequencePattern(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path); var m = Regex.Match(name, @"^(.*?)(\d+)$");
        if (!m.Success) return null;
        string digits = m.Groups[2].Value;
        return (Path.Combine(Path.GetDirectoryName(path)!, m.Groups[1].Value.Replace("%", "%%") + "%0" + digits.Length + "d" + Path.GetExtension(path)), int.Parse(digits, CultureInfo.InvariantCulture));
    }

    async Task ImportImageSequence()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "Pick the first image of the sequence", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.tif", "*.tiff", "*.bmp", "*.exr", "*.dpx", "*.webp"] }] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } first) return;
        if (SequencePattern(first) is not { } seq) { status.Text = "The file name needs a frame number at the end, like shot_0001.png."; return; }
        var rateText = await Prompt("Image sequence frame rate", "Frames per second to play the stills at.", Sequence.Fps.Value.ToString("0.###", CultureInfo.InvariantCulture));
        if (!double.TryParse(rateText, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate) || rate <= 0 || rate > 240) return;
        var folder = Path.Combine(AssetDirectory, "image-sequences"); Directory.CreateDirectory(folder);
        var output = Path.Combine(folder, Path.GetFileNameWithoutExtension(first).TrimEnd("0123456789".ToCharArray()).TrimEnd('_', '-', '.', ' ') + $"-{Guid.NewGuid():N}"[..9] + ".mp4");
        status.Text = "Building the image sequence clip…";
        // Near-lossless H.264 keeps it light to edit; even dimensions are required for 4:2:0.
        await ZenithClipper.MediaTools.RunAsync(FindFfmpeg(), ["-v", "error", "-nostdin", "-y", "-framerate", rate.ToString("0.###", CultureInfo.InvariantCulture), "-start_number", seq.Start.ToString(CultureInfo.InvariantCulture), "-i", seq.Pattern,
            "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2", "-c:v", "libx264", "-crf", "12", "-preset", "medium", "-pix_fmt", "yuv420p", output]);
        await ImportPathsAsync([output]);
        status.Text = "Image sequence imported as one clip.";
    }
}
