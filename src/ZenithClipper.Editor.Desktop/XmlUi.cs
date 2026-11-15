using Avalonia.Platform.Storage;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;

/// <summary>File → Import / Export Final Cut Pro XML (what Premiere and Resolve exchange).</summary>
public sealed partial class EditorWindow
{
    async Task ImportXml()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "Import Premiere / Final Cut Pro XML", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("Final Cut Pro XML") { Patterns = ["*.xml"] }] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        var x = XmlInterchange.Read(await File.ReadAllTextAsync(path));
        var media = new Dictionary<string, MediaAsset>(); var missing = new List<string>();
        foreach (var (id, file) in x.Clips.GroupBy(c => c.FileId).Select(g => (g.Key, g.First().Path)))
        {
            // Relative or moved files: also look next to the XML.
            var candidate = File.Exists(file) ? file : Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileName(file));
            if (!File.Exists(candidate)) { missing.Add(Path.GetFileName(file)); continue; }
            status.Text = "Reading " + Path.GetFileName(candidate); media[id] = await Probe(candidate);
        }
        var seq = XmlInterchange.Build(x, media);
        Change("Import XML", p => { foreach (var m in media.Values.DistinctBy(m => m.Id)) p.Media.Add(m); p.Sequences.Add(seq); p.ActiveSequenceId = seq.Id; EditCommands.EnsureSequenceMedia(p, seq); });
        selectedClip = null; extraSelection.Clear(); Refresh(true); FitTimeline();
        int count = seq.Tracks.Sum(t => t.Clips.Count);
        status.Text = $"Imported \"{seq.Name}\": {count} clip{(count == 1 ? "" : "s")}" + (missing.Count > 0 ? $"; missing files: {string.Join(", ", missing.Take(5))}{(missing.Count > 5 ? "…" : "")}" : ".") + " Effects and transitions don't carry over.";
    }

    async Task ExportXml()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new() { Title = "Export Final Cut Pro XML", SuggestedFileName = Sequence.Name + ".xml", DefaultExtension = "xml", FileTypeChoices = [new("Final Cut Pro XML") { Patterns = ["*.xml"] }] });
        if (file?.TryGetLocalPath() is not { } path) return;
        await File.WriteAllTextAsync(path, XmlInterchange.Write(Project, Sequence));
        status.Text = "Exported XML (cuts, tracks and in/out points) to " + path;
    }
}
