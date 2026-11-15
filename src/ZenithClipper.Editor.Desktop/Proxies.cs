using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;

/// <summary>Premiere's proxy workflow: make 540p H.264 copies for smooth editing, toggle them, export from originals.</summary>
public sealed partial class EditorWindow
{
    async Task CreateProxies()
    {
        var targets = Project.Media.Where(m => m.Kind == MediaKind.Video && File.Exists(m.Path) && (m.ProxyPath is null || !File.Exists(m.ProxyPath)) && m.Height > 720).GroupBy(m => m.Path).Select(g => g.First()).ToList();
        if (targets.Count == 0) { status.Text = "Every video above 720p already has a proxy (smaller videos don't need one)."; return; }
        var folder = Path.Combine(AssetDirectory, "proxies"); Directory.CreateDirectory(folder);
        int n = 0;
        foreach (var media in targets)
        {
            n++; status.Text = $"Creating proxy {n} of {targets.Count}: {media.Name}…";
            var output = Path.Combine(folder, Path.GetFileNameWithoutExtension(media.Path) + $"-{Guid.NewGuid():N}"[..9] + "-proxy.mp4");
            await MediaTools.RunAsync(FindFfmpeg(), ["-v", "error", "-nostdin", "-y", "-i", media.Path, "-map", "0:v:0", "-map", "0:a?", "-vf", "scale=-2:540", "-c:v", "libx264", "-preset", "veryfast", "-crf", "24", "-g", "15", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "160k", output]);
            var probed = await Probe(output); string source = media.Path;
            Change("Create proxy", p => { foreach (var m in p.Media.Where(m => m.Path == source)) { m.ProxyPath = output; m.ProxyWidth = probed.Width; m.ProxyHeight = probed.Height; } });
        }
        useProxies = true; graphRevision++; Refresh(true);
        status.Text = $"Created {targets.Count} prox{(targets.Count == 1 ? "y" : "ies")}. Preview uses them; export always uses the originals.";
    }

    void ToggleProxies()
    {
        useProxies = !useProxies; graphRevision++; Refresh(true);
        status.Text = useProxies ? "Proxies on for preview." : "Proxies off: previewing the original files.";
    }
}
