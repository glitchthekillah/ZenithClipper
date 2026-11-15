using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ZenithClipper.Native.Video;

internal static unsafe class DisplayInterop
{
    internal sealed record Display(nint Monitor, string DeviceName, int Width, int Height, int RefreshRate, bool Primary);
    internal static Display Select(int index)
    {
        List<Display> displays = [];
        GCHandle handle = GCHandle.Alloc(displays);
        try
        {
            if (!EnumDisplayMonitors(0, 0, &Collect, GCHandle.ToIntPtr(handle)))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { handle.Free(); }
        Display[] ordered = displays.OrderByDescending(display => display.Primary).ToArray();
        if ((uint)index >= (uint)ordered.Length) throw new ArgumentOutOfRangeException(nameof(index), "Monitor index is unavailable (primary monitor is index zero).");
        return ordered[index];
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Collect(nint monitor, nint dc, nint rect, nint parameter)
    {
        try
        {
            MonitorInfo info = default; info.Size = (uint)sizeof(MonitorInfo);
            if (!GetMonitorInfoW(monitor, &info)) return 0;
            string name = new(info.Device);
            byte* mode = stackalloc byte[220];
            new Span<byte>(mode, 220).Clear();
            *(ushort*)(mode + 68) = 220;
            int width = info.Bounds.Right - info.Bounds.Left, height = info.Bounds.Bottom - info.Bounds.Top, refresh = 60;
            if (EnumDisplaySettingsW(name, -1, mode))
            { width = *(int*)(mode + 172); height = *(int*)(mode + 176); refresh = *(int*)(mode + 184); }
            ((List<Display>)GCHandle.FromIntPtr(parameter).Target!).Add(new Display(monitor, name, width, height, refresh > 1 ? refresh : 60, (info.Flags & 1) != 0));
            return 1;
        }
        catch { return 0; } // Never unwind a managed exception through user32's callback.
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfo
    { public uint Size; public Rect Bounds, Work; public uint Flags; public fixed char Device[32]; }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint dc, nint clip, delegate* unmanaged[Stdcall]<nint, nint, nint, nint, int> callback, nint parameter);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfoW(nint monitor, MonitorInfo* info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplaySettingsW(string device, int mode, byte* settings);
}
