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
            var choice = await Dialog("--list", "--title=ZenithClipper controls", $"--text={status?.State} — {status?.BufferedSeconds:F1}s buffered\n{status?.Error}", "--column=Action", "Save clip", "Pause capture", "Resume capture", "Settings", "Startup", "Keyboard shortcuts", "Check for updates", "Open editor", "Open clips", "Quit engine", "--height=420", "--width=600");
            if (choice == null) return;
            try
            {
                switch (choice)
                {
                    case "Save clip": Check(await Control.SendAsync(new("save"))); break;
                    case "Pause capture": Check(await Control.SendAsync(new("pause"))); break;
                    case "Resume capture": Check(await Control.SendAsync(new("resume"))); break;
                    case "Settings": await ConfigureAsync(); break;
                    case "Startup": await ConfigureStartupAsync(); break;
                    case "Keyboard shortcuts": await ConfigureShortcutsAsync(); break;
                    case "Check for updates": await UpdateAsync(); break;
                    case "Open editor": LaunchEditor(); break;
                    case "Open clips": using (var p = Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { AppPaths.Load().ClipDirectory } })) { } break;
                    case "Quit engine": Check(await Control.SendAsync(new("shutdown"))); return;
                }
            }
            catch (Exception ex) { await Dialog("--error", "--title=ZenithClipper", "--text=" + ex.Message); }
        }
    }
    static void Check(ControlResponse response) { if (!response.Ok) throw new IOException(response.Error); }
    static async Task ConfigureStartupAsync()
    {
        var response = await Control.SendAsync(new("settings")); Check(response); var settings = response.Settings!;
        const string login = "Start ZenithClipper when I log in", record = "Start recording as soon as it starts";
        var picked = await Dialog("--list", "--checklist", "--title=Startup", "--text=Choose what happens when you turn on your PC.", "--column=On", "--column=Option",
            LinuxStartup.Enabled ? "TRUE" : "FALSE", login, settings.AutoStartCapture ? "TRUE" : "FALSE", record, "--separator=|", "--width=520", "--height=260");
        if (picked == null) return;
        var on = picked.Split('|');
        LinuxStartup.Set(on.Contains(login));
        if (on.Contains(record) != settings.AutoStartCapture) Check(await Control.SendAsync(new("configure", Settings: settings with { AutoStartCapture = on.Contains(record) })));
    }
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

    /// <summary>AppImage: downloads the new AppImage, checks it against SHA256SUMS and swaps it in place, then reopens.
    /// .deb and tar.gz installs open the release page (their package manager or the user installs those).</summary>
    static async Task UpdateAsync()
    {
        var update = await Updates.CheckAsync();
        if (update is null) { await Dialog("--info", "--title=ZenithClipper", $"--text=You're up to date (version {Updates.Current.ToString(3)})."); return; }
        bool self = Updates.CanSelfUpdate && update.DownloadUrl is not null;
        if (await Dialog("--question", "--title=ZenithClipper update", $"--text=ZenithClipper {update.Version.ToString(3)} is available.", $"--ok-label={(self ? "Update now" : "Open download page")}", "--cancel-label=Later") == null) return;
        if (!self) { using var _ = Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { update.PageUrl } }); return; }
        string downloaded = await Updates.DownloadAsync(update);
        string relaunch;
        switch (Updates.Flavor)
        {
            case Updates.Kind.AppImage:
            {
                string current = Environment.GetEnvironmentVariable("APPIMAGE")!;
                // Same folder, then rename over the running file: Linux keeps the old inode alive until it exits.
                string staged = current + ".new";
                File.Copy(downloaded, staged, true);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(staged, File.GetUnixFileMode(current) | UnixFileMode.UserExecute);
                File.Move(staged, current, true);
                relaunch = current; break;
            }
            case Updates.Kind.LinuxDeb:
                // System package: pkexec shows the desktop's password prompt, then apt replaces /opt/zenithclipper.
                await MediaTools.RunAsync("pkexec", new[] { "apt-get", "install", "-y", "--allow-downgrades", downloaded });
                relaunch = "/usr/bin/zenithclipper"; break;
            default:
            {
                // Unpacked tarball: extract beside the current folder, then swap the files in (running binaries keep their old inodes).
                string root = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), stage = root + ".update";
                if (Directory.Exists(stage)) Directory.Delete(stage, true);
                Directory.CreateDirectory(stage);
                await MediaTools.RunAsync("tar", new[] { "-xzf", downloaded, "-C", stage, "--strip-components=1" });
                await MediaTools.RunAsync("cp", new[] { "-a", stage + "/.", root + "/" });
                Directory.Delete(stage, true);
                relaunch = Environment.ProcessPath!; break;
            }
        }
        File.Delete(downloaded);
        try { await Control.SendAsync(new("shutdown")); } catch (Exception ex) when (ex is IOException or TimeoutException) { }
        using (Process.Start(new ProcessStartInfo(relaunch) { UseShellExecute = false, ArgumentList = { "panel" } })) { }
        Environment.Exit(0);
    }

    /// <summary>On engine start (at most every 20 hours): a desktop notification when a new version is out, once per version.</summary>
    public static void NotifyIfUpdateAvailable() => _ = Task.Run(async () =>
    {
        if (!Updates.Due(20)) return;
        var update = await Updates.CheckAsync();
        if (update is null || Updates.State.Notified == update.Tag) return;
        Updates.SaveState(Updates.State with { Notified = update.Tag });
        try { using var _ = Process.Start(new ProcessStartInfo("notify-send") { ArgumentList = { "--app-name=ZenithClipper", $"ZenithClipper {update.Version.ToString(3)} is available", "Open the ZenithClipper controls and choose Check for updates." } }); }
        catch (System.ComponentModel.Win32Exception) { }
    });
}

/// <summary>Start at login through an XDG autostart entry. The file is the setting; the first run turns it on once.</summary>
static class LinuxStartup
{
    static string Entry => Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x ? x : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "autostart", "zenithclipper.desktop");
    static string Marker => Path.Combine(AppPaths.Root, "startup-default-applied");
    public static bool Enabled => File.Exists(Entry);

    static string? Launcher()
    {
        if (Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } image) return image;
        string? self = Environment.ProcessPath;
        return self == null || Path.GetFileNameWithoutExtension(self) == "dotnet" ? null : self;
    }

    public static void Set(bool on)
    {
        if (!on) { File.Delete(Entry); return; }
        string launcher = Launcher() ?? throw new IOException("Start at login needs the installed app, not a development build.");
        Directory.CreateDirectory(Path.GetDirectoryName(Entry)!);
        File.WriteAllText(Entry, "[Desktop Entry]\nType=Application\nName=ZenithClipper\nComment=Instant replay recorder\nExec=\"" + launcher.Replace("\"", "\\\"") + "\" daemon\nTerminal=false\nX-GNOME-Autostart-enabled=true\n");
    }

    public static void EnsureDefault()
    {
        try
        {
            if (!File.Exists(Marker)) { if (Launcher() != null) Set(true); Directory.CreateDirectory(AppPaths.Root); File.WriteAllText(Marker, "1"); }
            else if (Enabled && Launcher() != null) Set(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
