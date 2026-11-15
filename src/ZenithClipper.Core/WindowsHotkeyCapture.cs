using System.Runtime.InteropServices;

namespace ZenithClipper;

/// <summary>Forwards registered keys to a focused shortcut picker without interrupting recording.</summary>
public static class WindowsHotkeyCapture
{
    public const string ListeningProperty = "ZenithClipper.HotkeyCapture.Listening.v1";
    public const int ForwardMessage = 0x8000 + 0x245;
    const uint RegisteredHotkeyMessage = 0x312;

    public static bool TryForwardRegistered(nint payload)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        if (!GetGUIThreadInfo(0, ref info) || info.Focus == 0) return false;
        bool listening = GetPropW(info.Focus, ListeningProperty) != 0;
        return TryRouteRegisteredMessage(RegisteredHotkeyMessage, payload, info.Focus, listening,
            (target, message, data) => { _ = PostMessageW(target, (uint)message, 0, data); });
    }

    /// <summary>Pure routing seam: only a listening focused picker consumes the global action.</summary>
    public static bool TryRouteRegisteredMessage(uint message, nint payload, nint focusedWindow, bool listening, Action<nint, int, nint> forward)
    {
        if (message != RegisteredHotkeyMessage || focusedWindow == 0 || !listening || !TryDecodePayload(payload, out _)) return false;
        forward(focusedWindow, ForwardMessage, payload);
        // If the picker disappears while posting, never turn its binding keystroke into a save/menu action.
        return true;
    }

    public static bool TryDecodeForwardedMessage(int message, nint payload, out string binding)
    {
        binding = "";
        return message == ForwardMessage && TryDecodePayload(payload, out binding);
    }

    static bool TryDecodePayload(nint payload, out string binding)
    {
        binding = "";
        long data = payload.ToInt64();
        uint modifiers = (uint)(data & 0xF);
        uint key = (uint)((data >> 16) & 0xFFFF);
        string name = key == 0xC0 ? "~" : key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A ? ((char)key).ToString() : key is >= 0x70 and <= 0x87 ? "F" + (key - 0x6F) : "";
        if (modifiers == 0 || name.Length == 0) return false;
        var parts = new List<string>();
        if ((modifiers & 2) != 0) parts.Add("Ctrl");
        if ((modifiers & 4) != 0) parts.Add("Shift");
        if ((modifiers & 1) != 0) parts.Add("Alt");
        if ((modifiers & 8) != 0) parts.Add("Win");
        parts.Add(name);
        binding = string.Join('+', parts);
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct GuiThreadInfo
    {
        public uint Size, Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public int Left, Top, Right, Bottom;
    }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint GetPropW(nint window, string name);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool PostMessageW(nint window, uint message, nuint wParam, nint lParam);
}
