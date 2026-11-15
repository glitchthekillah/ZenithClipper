using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ZenithClipper;

[SupportedOSPlatform("windows")]
public static class WindowsDisplay
{
    // DXGI adapter/output zero is the primary display. GDI's adapter enumeration order
    // is not a monitor index: inspect the active PRIMARY flag instead of guessing AMD.
    public static bool HasNvidiaAdapter()
    {
        for(uint index=0;index<64;index++)
        {
            var device=new DisplayDevice{Size=Marshal.SizeOf<DisplayDevice>(),Name="",Description="",Id="",Key=""};
            if(!EnumDisplayDevicesW(null,index,ref device,0))break;
            if(EncoderSelection.FromDeviceId(device.Id)=="nvenc")return true;
        }
        return false;
    }
    public static string? PreferredEncoder(int monitor)
    {
        if(monitor!=0)return null; // Other outputs need an explicit DXGI/LUID mapping.
        var primary=PrimaryDevice();
        return primary.HasValue?EncoderSelection.FromDeviceId(primary.Value.Id):null;
    }
    static DisplayDevice? PrimaryDevice()
    {
        for(uint index=0;index<64;index++)
        {
            var device=new DisplayDevice{Size=Marshal.SizeOf<DisplayDevice>(),Name="",Description="",Id="",Key=""};
            if(!EnumDisplayDevicesW(null,index,ref device,0))break;
            if((device.Flags&5)==5)return device;
        }
        return null;
    }
    public static bool UsesNvidiaAdapter(int monitor) => PreferredEncoder(monitor)=="nvenc";
    public static int RefreshRate(int monitor)
    {
        // EnumDisplaySettings receives a native DEVMODEW buffer; avoids fragile managed union layouts.
        var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>(), Name = "", Description = "", Id = "", Key = "" };
        if(monitor==0 && PrimaryDevice() is { } primary)device=primary;
        else if (!EnumDisplayDevicesW(null, (uint)monitor, ref device, 0)) throw new IOException("Selected display is unavailable.");
        nint mode = Marshal.AllocHGlobal(220);
        try
        {
            for (int i = 0; i < 220; i++) Marshal.WriteByte(mode, i, 0);
            Marshal.WriteInt16(mode, 68, 220);
            if (!EnumDisplaySettingsW(device.Name, -1, mode)) throw new IOException("Could not read display mode. Set FPS explicitly.");
            int refresh = Marshal.ReadInt32(mode, 184);
            return refresh > 1 ? refresh : throw new IOException("Display refresh unavailable. Set FPS explicitly.");
        }
        finally { Marshal.FreeHGlobal(mode); }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct DisplayDevice { public int Size; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool EnumDisplayDevicesW(string? device, uint index, ref DisplayDevice result, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool EnumDisplaySettingsW(string device, int mode, nint result);
}
