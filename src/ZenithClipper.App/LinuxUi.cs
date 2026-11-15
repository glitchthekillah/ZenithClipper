using ZenithClipper;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

static class LinuxUi
{
    static async Task<string?> Dialog(params string[] args)
    {
        try { return (await MediaTools.RunAsync("zenity", args)).TrimEnd('\r', '\n'); }
        catch (IOException) { return null; } // Cancel and WM close terminate the on-demand client.
    }
    public static async Task ShowAsync(string command, string[] args)
    {
        if (command == "editor") { if(!EditorLauncher.Launch(args.FirstOrDefault()))await EditAsync(args.FirstOrDefault()); return; }
        try { await Control.SendAsync(new("status")); }
        catch (Exception ex) when (ex is IOException or TimeoutException)
        {
            var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "zenithclipper.dll"));
            info.ArgumentList.Add("daemon"); Process.Start(info);
            await Task.Delay(500);
        }
        while (true)
        {
            var response = await Control.SendAsync(new("status")); var status = response.Status;
            var choice = await Dialog("--list", "--title=ZenithClipper controls", $"--text={status?.State} — {status?.BufferedSeconds:F1}s buffered\n{status?.Error}", "--column=Action", "Save clip", "Pause capture", "Resume capture", "Settings", "Keyboard shortcuts", "Open editor", "Open clips", "Quit engine", "--height=420", "--width=600");
            if (choice == null) return;
            try
            {
                switch (choice)
                {
                    case "Save clip": Check(await Control.SendAsync(new("save"))); break;
                    case "Pause capture": Check(await Control.SendAsync(new("pause"))); break;
                    case "Resume capture": Check(await Control.SendAsync(new("resume"))); break;
                    case "Settings": await ConfigureAsync(); break;
                    case "Keyboard shortcuts": await ConfigureShortcutsAsync(); break;
                    case "Open editor": LaunchEditor(); break;
                    case "Open clips": using (var p = Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { AppPaths.Load().ClipDirectory } })) { } break;
                    case "Quit engine": Check(await Control.SendAsync(new("shutdown"))); return;
                }
            }
            catch (Exception ex) { await Dialog("--error", "--title=ZenithClipper", "--text=" + ex.Message); }
        }
    }
    static void Check(ControlResponse response) { if (!response.Ok) throw new IOException(response.Error); }
    static void LaunchEditor()
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "zenithclipper.dll"));
        info.ArgumentList.Add("editor"); Process.Start(info);
    }
    static async Task ConfigureShortcutsAsync()
    {
        var response = await Control.SendAsync(new("settings")); Check(response); var settings = response.Settings!;
        var clip = await Dialog("--entry", "--title=Save clip shortcut", "--text=Pressing this shortcut saves the replay. Type a combination such as Alt+C.", "--entry-text=" + settings.ClipHotkey, "--width=560");
        if (clip == null) return;
        var panel = await Dialog("--entry", "--title=Clipping menu shortcut", "--text=Type a combination such as Shift+~. Shift+backtick uses the same key.", "--entry-text=" + settings.PanelHotkey, "--width=560");
        if (panel == null) return;
        var updated = settings with { ClipHotkey = clip.Trim(), PanelHotkey = panel.Trim() };
        updated.Validate();
        Check(await Control.SendAsync(new("configure", Settings: updated)));
    }
    static async Task ConfigureAsync()
    {
        var r = await Control.SendAsync(new("settings")); Check(r); var s = r.Settings!;
        var text = await Dialog("--entry", "--title=Capture settings", "--text=Clip seconds | Buffer seconds | Disk MiB | FPS (0=auto) | Encoder | Excluded apps (comma-separated)", $"--entry-text={s.ClipSeconds}|{s.BufferSeconds}|{s.MaxBufferMiB}|{s.Fps}|{s.Encoder}|{string.Join(',', s.ExcludedApps)}", "--width=800");
        if (text == null) return;
        var values = text.Split('|'); if (values.Length != 6) throw new ArgumentException("Enter six fields separated by |.");
        var value = s with { ClipSeconds = int.Parse(values[0]), BufferSeconds = int.Parse(values[1]), MaxBufferMiB = long.Parse(values[2]), Fps = int.Parse(values[3]), Encoder = values[4], ExcludedApps = values[5].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) };
        Check(await Control.SendAsync(new("configure", Settings: value)));
    }
    static async Task EditAsync(string? path)
    {
        path ??= await Dialog("--file-selection", "--title=Choose clip", "--file-filter=Video | *.mp4 *.mkv *.ts *.mov");
        if (path == null) return;
        var action = await Dialog("--list", "--title=Clip editor", "--column=Action", "Preview", "Export trim / crop / audio / speed", "--height=240");
        if (action == null) return;
        if (action == "Preview") { Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { path } }); return; }
        var parameters = await Dialog("--entry", "--title=Export settings", "--text=Start seconds | Duration (0=to end) | Volume (0-4) | Speed (0.5-2) | Width (0=native) | Crop W,H,X,Y (0,0,0,0=none) | Fast trim (true/false)", "--entry-text=0|0|1|1|0|0,0,0,0|true", "--width=900");
        if (parameters == null) return;
        string? output = await Dialog("--file-selection", "--save", "--title=Export MP4", "--filename=" + Path.ChangeExtension(path, null) + "-edited.mp4");
        if (output == null) return;
        try
        {
            var p = parameters.Split('|'); var c = p[5].Split(',').Select(int.Parse).ToArray(); var culture = CultureInfo.InvariantCulture;
            var settings = AppPaths.Load(); double duration = double.Parse(p[1], culture);
            var edit = new EditRequest(path, output) { StartSeconds = double.Parse(p[0], culture), DurationSeconds = duration == 0 ? null : duration, Volume = double.Parse(p[2], culture), Speed = double.Parse(p[3], culture), OutputWidth = int.Parse(p[4]), CropWidth = c[0], CropHeight = c[1], CropX = c[2], CropY = c[3], FastTrim = bool.Parse(p[6]), Encoder = settings.Encoder, Quality = settings.Quality };
            await Editor.ExportAsync(settings, edit); await Dialog("--info", "--text=Saved: " + output);
        }
        catch (Exception ex) { await Dialog("--error", "--text=" + ex.Message); }
    }
}
