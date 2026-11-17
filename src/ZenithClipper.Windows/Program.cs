using ZenithClipper;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ZenithClipper.Windows;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        int probeIndex = Array.IndexOf(args, "--startup-probe");
        if (probeIndex >= 0 && probeIndex + 1 < args.Length) PanelStartupProbe.Configure(args[probeIndex + 1], args.Contains("--probe-eager-baseline"));
        PanelStartupProbe.Mark("main");
        ApplicationConfiguration.Initialize();
        PanelStartupProbe.Mark("application_configured");
        if (PanelStartupProbe.Enabled) { PanelStartupProbe.Run(); return; }
        try
        {
            if (args.FirstOrDefault() == "--background") { Startup.EnsureDefault(); EngineClient.StartEngine(); UpdateNotifier.Run(force: false); return; }
            if (args.FirstOrDefault() == "--update-check") { UpdateNotifier.Run(force: args.Contains("--now")); return; }
            if (args.FirstOrDefault() == "update") { PanelForm.UpdateOnOpen = true; args = ["panel"]; }
            if (args.FirstOrDefault() == "--snapshot" && args.Length > 1) { Directory.CreateDirectory(args[1]); PanelForm.SnapshotDir = args[1]; Application.Run(new PanelForm()); return; }
            bool editor = args.FirstOrDefault() == "editor";
            if(editor && EditorLauncher.Launch(args.Skip(1).FirstOrDefault()))return;
            using var single = new Mutex(true, @"Local\ZenithClipper-" + (editor ? "Editor" : "Panel"), out bool first);
            if (!first)
            {
                nint window = FindWindowW(null, editor ? EditorForm.WindowTitle : PanelForm.WindowTitle);
                if (window == 0) return;
                if (!editor) { PostMessageW(window, 0x10, 0, 0); return; } // panel shortcut toggles: a second press closes it
                if (args.Skip(1).FirstOrDefault() is { } file) SendPath(window, Path.GetFullPath(file));
                ShowWindowAsync(window, 9); SetForegroundWindow(window);
                return;
            }
            if (!editor) Startup.EnsureDefault();
            Application.Run(editor ? new EditorForm(args.Skip(1).FirstOrDefault()) : new PanelForm());
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "ZenithClipper", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
    static void SendPath(nint window, string file)
    {
        nint text = Marshal.StringToHGlobalUni(file + "\0");
        try
        {
            var data = new EditorForm.CopyData { Id = 1, Length = (file.Length + 1) * 2, Data = text };
            nint block = Marshal.AllocHGlobal(Marshal.SizeOf<EditorForm.CopyData>());
            try { Marshal.StructureToPtr(data, block, false); SendMessageTimeoutW(window, 0x4A, 0, block, 2, 2000, out _); }
            finally { Marshal.FreeHGlobal(block); }
        }
        finally { Marshal.FreeHGlobal(text); }
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern nint FindWindowW(string? className,string windowName);
    [DllImport("user32.dll")] static extern bool PostMessageW(nint window, int message, nint wParam, nint lParam);
    [DllImport("user32.dll")] static extern nint SendMessageTimeoutW(nint window, int message, nint wParam, nint lParam, int flags, int timeout, out nint result);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] static extern bool ShowWindowAsync(nint window,int command);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] static extern bool SetForegroundWindow(nint window);
}

static class EngineClient
{
    public static void StartEngine()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "zenithclipper.exe");
        if (!File.Exists(path)) throw new FileNotFoundException("The capture engine is missing. Run the packaging script.", path);
        Process.Start(DaemonLauncher.Create(AppContext.BaseDirectory,path,DaemonLauncher.CanUseNative(AppPaths.Load(),AppContext.BaseDirectory)));
    }
    public static async Task EnsureAsync()
    {
        try { await Control.SendAsync(new("status")); return; } catch (Exception ex) when (ex is IOException or TimeoutException) { }
        StartEngine();
        for (int i = 0; i < 30; i++) { await Task.Delay(150); try { await Control.SendAsync(new("status")); return; } catch (Exception ex) when (ex is IOException or TimeoutException) { } }
        throw new IOException("Engine did not start. Check engine.log in " + AppPaths.Root);
    }
    public static async Task<ControlResponse> SendAsync(ControlRequest request)
    {
        var response = await Control.SendAsync(request);
        if (!response.Ok) throw new IOException(response.Error);
        return response;
    }
    public static void Editor(string? path = null)
    {
        if(EditorLauncher.Launch(path))return;
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, ArgumentList = { "editor" } };
        if (path != null) info.ArgumentList.Add(path); Process.Start(info);
    }
}
