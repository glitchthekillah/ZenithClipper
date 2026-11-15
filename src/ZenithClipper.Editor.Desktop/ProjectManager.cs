using Avalonia.Platform.Storage;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;

/// <summary>Premiere's Project Manager (Collect Files and Copy to New Location): copies every used file into one folder and relinks.</summary>
public sealed partial class EditorWindow
{
    async Task CollectFiles()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new() { Title = "Collect project files into…", AllowMultiple = false });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } target) return;
        var project = ProjectCopy;
        var files = project.Media.Where(m => !string.IsNullOrEmpty(m.Path) && File.Exists(m.Path)).Select(m => m.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Count == 0) { status.Text = "No media files to collect."; return; }
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); int n = 0; long bytes = 0;
        foreach (var source in files)
        {
            n++; status.Text = $"Copying {n} of {files.Count}: {Path.GetFileName(source)}…";
            var name = Path.GetFileName(source); var destination = Path.Combine(target, name);
            for (int i = 2; File.Exists(destination) && !SameFile(source, destination); i++) destination = Path.Combine(target, $"{Path.GetFileNameWithoutExtension(name)} ({i}){Path.GetExtension(name)}");
            if (!SameFile(source, destination)) await Task.Run(() => File.Copy(source, destination, overwrite: false));
            map[source] = destination; bytes += new FileInfo(destination).Length;
        }
        Change("Collect files", p => { foreach (var m in p.Media) if (!string.IsNullOrEmpty(m.Path) && map.TryGetValue(m.Path, out var moved)) m.Path = moved; });
        status.Text = $"Collected {files.Count} files ({bytes / 1048576.0:0.#} MB) into {target}. Save the project to keep the new links.";
    }

    static bool SameFile(string a, string b) => File.Exists(b) && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
