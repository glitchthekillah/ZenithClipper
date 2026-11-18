using System.Runtime.InteropServices;

namespace ZenithClipper;

public sealed class GlobalHotkeys : IDisposable
{
    readonly Thread thread;
    uint threadId;
    nint display;
    volatile bool stopping;
    readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string? Error { get; private set; }
    public GlobalHotkeys(Settings settings, Action clip, Action panel)
    {
        thread = new(() =>
        {
            try { if (OperatingSystem.IsWindows()) WindowsLoop(settings, clip, panel); else if (OperatingSystem.IsMacOS()) { /* The menu bar app owns the Mac shortcuts. */ } else if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))) X11Loop(settings, clip, panel); else Error = "On Wayland, bind desktop shortcuts to zenithclipper save and zenithclipper panel."; }
            catch (Exception ex) { Error = ex.Message; }
            finally { ready.TrySetResult(); }
        }) { IsBackground = true, Name = "ZenithClipper shortcuts" };
        thread.Start(); ready.Task.Wait(TimeSpan.FromSeconds(3));
    }
    void WindowsLoop(Settings settings, Action clip, Action panel)
    {
        threadId = GetCurrentThreadId(); PeekMessageW(out _, 0, 0, 0, 0);
        var c = HotkeySpec.Parse(settings.ClipHotkey); var p = HotkeySpec.Parse(settings.PanelHotkey);
        if (!RegisterHotKey(0, 1, c.Modifiers, c.Key)) Error = "Clip hotkey is already in use. Change it in settings.";
        if (!RegisterHotKey(0, 2, p.Modifiers, p.Key)) Error = "Panel hotkey is already in use. Change it in settings.";
        ready.TrySetResult();
        try
        {
            while (GetMessageW(out var message, 0, 0, 0) > 0)
                if (message.Id == 0x312 && (message.WParam == 1 || message.WParam == 2))
                {
                    if (WindowsHotkeyCapture.TryForwardRegistered(message.LParam)) continue;
                    if (message.WParam == 1) clip(); else panel();
                }
        }
        finally { UnregisterHotKey(0, 1); UnregisterHotKey(0, 2); }
    }
    void X11Loop(Settings settings, Action clip, Action panel)
    {
        XInitThreads(); display = XOpenDisplay(0); if (display == 0) throw new IOException("Cannot open X display for shortcuts.");
        nint root = XDefaultRootWindow(display);
        var keys = new Dictionary<(uint key, uint mods), Action>();
        foreach (var (value, action) in new[] { (settings.ClipHotkey, clip), (settings.PanelHotkey, panel) })
        {
            var spec = HotkeySpec.Parse(value); uint mods = ((spec.Modifiers & 2) != 0 ? 4u : 0) | ((spec.Modifiers & 4) != 0 ? 1u : 0) | ((spec.Modifiers & 1) != 0 ? 8u : 0) | ((spec.Modifiers & 8) != 0 ? 64u : 0);
            string symbol = spec.X11Symbol; uint key = XKeysymToKeycode(display, XStringToKeysym(symbol));
            foreach (uint extra in new uint[] { 0, 2, 16, 18 }) XGrabKey(display, (int)key, mods | extra, root, 0, 1, 1);
            keys[(key, mods)] = action;
        }
        XSync(display, 0); ready.TrySetResult(); nint evt = Marshal.AllocHGlobal(192);
        try
        {
            while (!stopping)
            {
                // Poll only the X socket event queue at 10 Hz; Windows uses a blocking message loop.
                if (XPending(display) == 0) { Thread.Sleep(100); continue; }
                XNextEvent(display, evt);
                if (Marshal.ReadInt32(evt) == 2 && keys.TryGetValue(((uint)Marshal.ReadInt32(evt, 84), (uint)Marshal.ReadInt32(evt, 80) & ~18u), out var action)) action();
            }
        }
        finally { Marshal.FreeHGlobal(evt); XUngrabKey(display, 0, 1u << 15, root); XCloseDisplay(display); display = 0; }
    }
    public void Dispose() { stopping = true; if (threadId != 0) PostThreadMessageW(threadId, 0x12, 0, 0); thread.Join(1500); }
    [StructLayout(LayoutKind.Sequential)] struct Message { public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("user32.dll")] static extern int GetMessageW(out Message message, nint window, uint min, uint max);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool PeekMessageW(out Message message, nint window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool PostThreadMessageW(uint thread, uint message, nuint wParam, nint lParam);
    [DllImport("libX11.so.6")] static extern int XInitThreads();
    [DllImport("libX11.so.6")] static extern nint XOpenDisplay(nint name);
    [DllImport("libX11.so.6")] static extern nint XDefaultRootWindow(nint display);
    [DllImport("libX11.so.6")] static extern nuint XStringToKeysym(string name);
    [DllImport("libX11.so.6")] static extern byte XKeysymToKeycode(nint display, nuint symbol);
    [DllImport("libX11.so.6")] static extern int XGrabKey(nint display, int key, uint modifiers, nint window, int owner, int pointer, int keyboard);
    [DllImport("libX11.so.6")] static extern int XUngrabKey(nint display, int key, uint modifiers, nint window);
    [DllImport("libX11.so.6")] static extern int XPending(nint display);
    [DllImport("libX11.so.6")] static extern int XNextEvent(nint display, nint evt);
    [DllImport("libX11.so.6")] static extern int XSync(nint display, int discard);
    [DllImport("libX11.so.6")] static extern int XCloseDisplay(nint display);
}
