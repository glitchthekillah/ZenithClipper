using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace ZenithClipper;

[SupportedOSPlatform("windows")]
public static partial class WindowsEndpoints
{
    public sealed record Device(string Id, string Name);

    public static Device[] List(bool microphone)
    {
        var result = new List<Device>();
        var enumerator = CreateEnumerator();
        EndpointCollection? devices = null;
        try
        {
            enumerator.EnumAudioEndpoints(microphone ? 1 : 0, 1, out nint pointer);
            devices = Take<EndpointCollection>(pointer);
            devices.GetCount(out uint count);
            for (uint index = 0; index < count; index++)
            {
                devices.Item(index, out pointer);
                var device = Take<EndpointDevice>(pointer);
                try
                {
                    device.GetId(out nint id);
                    string name = Marshal.PtrToStringUni(id)!;
                    Marshal.FreeCoTaskMem(id);
                    result.Add(new(name, FriendlyName(device) ?? name));
                }
                finally { Release(device); }
            }
        }
        finally { Release(devices); Release(enumerator); }
        return [.. result.OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    internal static T Activate<T>(bool microphone, string id) where T : class
    {
        var enumerator = CreateEnumerator();
        EndpointDevice? device = null;
        try
        {
            nint pointer;
            if (id.Length == 0) enumerator.GetDefaultAudioEndpoint(microphone ? 1 : 0, 1, out pointer);
            else enumerator.GetDevice(id, out pointer);
            device = Take<EndpointDevice>(pointer);
            var iid = typeof(T).GUID;
            device.Activate(ref iid, 23, 0, out pointer);
            return Take<T>(pointer);
        }
        finally { Release(device); Release(enumerator); }
    }

    static string? FriendlyName(EndpointDevice device)
    {
        device.OpenPropertyStore(0, out nint pointer);
        var store = Take<EndpointProperties>(pointer);
        PropertyValue value = default;
        try
        {
            var key = new PropertyKey { Format = new("A45C254E-DF1C-4EFD-8020-67D146A850E0"), Id = 14 };
            store.GetValue(ref key, out value);
            return value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) : null;
        }
        finally { PropVariantClear(ref value); Release(store); }
    }

    static EndpointEnumerator CreateEnumerator()
    {
        Guid cls = new("BCDE0395-E52F-467C-8E3D-C4579291692E"), iid = typeof(EndpointEnumerator).GUID;
        Marshal.ThrowExceptionForHR(CoCreateInstance(ref cls, 0, 1, ref iid, out nint pointer));
        return Take<EndpointEnumerator>(pointer);
    }

    static unsafe T Take<T>(nint pointer) where T : class
    {
        if (pointer == 0) throw new COMException("Windows returned no audio interface.");
        try { return UniqueComInterfaceMarshaller<T>.ConvertToManaged((void*)pointer)!; }
        finally { Marshal.Release(pointer); }
    }

    static void Release(object? value) { if (value is ComObject wrapper) wrapper.FinalRelease(); }
    [LibraryImport("ole32.dll")] private static partial int CoCreateInstance(ref Guid cls, nint outer, uint context, ref Guid iid, out nint value);
    [LibraryImport("ole32.dll")] private static partial int PropVariantClear(ref PropertyValue value);
}

[StructLayout(LayoutKind.Sequential)] internal struct PropertyKey { public Guid Format; public uint Id; }
[StructLayout(LayoutKind.Explicit, Size = 24)] internal struct PropertyValue
{
    [FieldOffset(0)] public ushort Type;
    [FieldOffset(8)] public nint Pointer;
}
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16), Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
internal partial interface EndpointEnumerator
{
    void EnumAudioEndpoints(int flow, uint mask, out nint devices);
    void GetDefaultAudioEndpoint(int flow, int role, out nint device);
    void GetDevice(string id, out nint device);
}
[GeneratedComInterface, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
internal partial interface EndpointCollection
{
    void GetCount(out uint count);
    void Item(uint index, out nint device);
}
[GeneratedComInterface, Guid("D666063F-1587-4E43-81F1-B948E807363F")]
internal partial interface EndpointDevice
{
    void Activate(ref Guid iid, uint context, nint parameters, out nint result);
    void OpenPropertyStore(uint access, out nint store);
    void GetId(out nint id);
}
[GeneratedComInterface, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
internal partial interface EndpointProperties
{
    void GetCount(out uint count);
    void GetAt(uint index, out PropertyKey key);
    void GetValue(ref PropertyKey key, out PropertyValue value);
}
