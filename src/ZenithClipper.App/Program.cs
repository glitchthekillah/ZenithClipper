using ZenithClipper;
using System.Diagnostics;
using System.Text.Json;

try
{
    string command = args.FirstOrDefault() ?? "panel";
    if(command=="daemon"&&!args.Contains("--legacy-daemon",StringComparer.Ordinal)&&DaemonLauncher.CanUseNative(AppPaths.Load(),AppContext.BaseDirectory))
    {
        Process.Start(DaemonLauncher.Create(AppContext.BaseDirectory,Environment.ProcessPath!,true));return 0;
    }
    if (command == "daemon") return await RunDaemon(!args.Contains("--no-compatibility-fallback",StringComparer.Ordinal));
    if (command == "editor" && EditorLauncher.Launch(args.Skip(1).FirstOrDefault())) return 0;
    if (command is "panel" or "editor")
    {
        if (OperatingSystem.IsWindows())
        {
            string ui = Path.Combine(AppContext.BaseDirectory, "ZenithClipper.Windows.exe");
            if (!File.Exists(ui)) throw new FileNotFoundException("Build/package the Windows panel first.", ui);
            var uiStart = new ProcessStartInfo(ui) { UseShellExecute = false, CreateNoWindow = true }; uiStart.ArgumentList.Add(command); foreach(var argument in args.Skip(1)) uiStart.ArgumentList.Add(argument); Process.Start(uiStart);
        }
        else await LinuxUi.ShowAsync(command, args.Skip(1).ToArray());
        return 0;
    }
    if (command == "check-update")
    {
        var u = await Updates.CheckAsync();
        Console.WriteLine(u is null ? $"Up to date ({Updates.Current.ToString(3)})" : $"Update {u.Version.ToString(3)} available ({Updates.Flavor}): {u.DownloadName ?? "open " + u.PageUrl}");
        if (u is not null && args.Contains("--download")) Console.WriteLine("Verified download: " + await Updates.DownloadAsync(u));
        return 0;
    }
    if (command == "config-path") { Console.WriteLine(AppPaths.Config); return 0; }
    if (command == "pipe-name") { Console.WriteLine(Control.PipeName); return 0; }
    if (command == "init") { if (!File.Exists(AppPaths.Config)) AppPaths.Save(new Settings()); Console.WriteLine(AppPaths.Config); return 0; }
    if (command == "doctor")
    {
        var s = AppPaths.Load(); Console.WriteLine($"Platform: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}\nConfig: {AppPaths.Config}\nFFmpeg: {MediaTools.FindFfmpeg(s)}");
        Console.WriteLine((await MediaTools.RunAsync(MediaTools.FindFfmpeg(s), ["-version"])).Split('\n')[0]);
        Console.WriteLine("Encoder: " + await MediaTools.ResolveEncoder(s, CancellationToken.None)); return 0;
    }
    if (command == "export")
    {
        if (args.Length != 2) throw new ArgumentException("Usage: zenithclipper export edit-request.json");
        var edit = JsonSerializer.Deserialize<EditRequest>(File.ReadAllText(args[1]), AppPaths.Json) ?? throw new ArgumentException("Empty edit request.");
        await Editor.ExportAsync(AppPaths.Load(), edit); Console.WriteLine(edit.Output); return 0;
    }
    var request = command == "configure" ? new ControlRequest(command, Settings: AppPaths.Load()) : new ControlRequest(command, args.Length > 1 ? int.Parse(args[1]) : null);
    ControlRequest.Parse(JsonSerializer.Serialize(request));
    var response = await Control.SendAsync(request); Console.WriteLine(JsonSerializer.Serialize(response, AppPaths.Json));
    return response.Ok ? 0 : 1;
}
catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }

static async Task<int> RunDaemon(bool allowCompatibilityFallback)
{
    Directory.CreateDirectory(AppPaths.Root);
    if (OperatingSystem.IsLinux()) LinuxUi.NotifyIfUpdateAvailable();
    FileStream instance;
    try { instance = new FileStream(Path.Combine(AppPaths.Root, "engine.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
    catch (IOException) { Console.Error.WriteLine("ZenithClipper engine is already running."); return 2; }
    using (instance)
    using (var shutdown = new CancellationTokenSource())
    {
        ExportRecovery.Clean(Path.Combine(AppPaths.Root,"exports"));
        var settings = AppPaths.Load(); if (!File.Exists(AppPaths.Config)) AppPaths.Save(settings);
        await using var recorder = new Recorder(settings,allowCompatibilityFallback);
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };
        void Log(string message)
        {
            Console.Error.WriteLine(message);
            string log = Path.Combine(AppPaths.Root, "engine.log");
            try { if (File.Exists(log) && new FileInfo(log).Length > 1024 * 1024) File.Move(log, log + ".old", true); File.AppendAllText(log, $"{DateTimeOffset.Now:O} {message}\n"); } catch (IOException) { }
        }
        void Save() => _ = Task.Run(async () => { try { Log("Saved: " + await recorder.SaveAsync(ct: shutdown.Token)); } catch (Exception ex) { Log("Save failed: " + ex.Message); } });
        void Panel() { try { var info = PanelLauncher.Create(AppContext.BaseDirectory, Environment.ProcessPath!, OperatingSystem.IsWindows()); Process.Start(info); } catch (Exception ex) { Log(ex.Message); } }
        GlobalHotkeys? hotkeys = new(settings, Save, Panel);
        if (hotkeys.Error != null) Log(hotkeys.Error);
        var server = new ControlServer(recorder, shutdown);
        server.SettingsChanged += value => { hotkeys?.Dispose(); hotkeys = new(value, Save, Panel); if (hotkeys.Error != null) Log(hotkeys.Error); };
        var serverTask = server.RunAsync(shutdown.Token);
        Recorder.Log = Log;
        Log($"Engine started. Data={AppPaths.Root}; Pipe={Control.PipeName}");
        try
        {
            if (settings.AutoStartCapture) { try { await recorder.StartAsync(shutdown.Token); } catch (Exception ex) { Log("Capture failed: " + ex.Message); } }
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            string? lastError = null, lastNotice = null;
            while (await timer.WaitForNextTickAsync(shutdown.Token))
            {
                if(serverTask.IsCompleted) await serverTask; await recorder.TickAsync(shutdown.Token);
                var status = recorder.Status();
                if (status.Error != lastError) { if (status.Error is { Length: > 0 }) Log("Capture error: " + status.Error.Replace('\n', ' ')); lastError = status.Error; }
                if (status.Notice != lastNotice) { if (status.Notice is { Length: > 0 }) Log(status.Notice.Replace('\n', ' ')); lastNotice = status.Notice; }
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        finally { shutdown.Cancel(); hotkeys.Dispose(); await serverTask; }
    }
    return 0;
}
