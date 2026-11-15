using System.Runtime.InteropServices;

internal sealed unsafe class GpuMemorySampler : IDisposable
{
    private nint factory, adapter;
    public GpuMemorySampler()
    {
        Guid iid = new("770aae78-f26f-4dba-a829-253c83d1b387");
        Check(CreateDXGIFactory1(ref iid, out factory));
        byte* description = stackalloc byte[312];
        for (uint index = 0; ; index++)
        {
            nint candidate = 0;
            int hr = ((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Table(factory)[12])(factory, index, &candidate);
            Check(hr);
            Check(((delegate* unmanaged[Stdcall]<nint, void*, int>)Table(candidate)[10])(candidate, description));
            if (*(uint*)(description + 256) == 0x10de)
            {
                Guid adapterId = new("645967a4-1392-4310-a798-8053ce3e93fd");
                nint value = 0;
                Check(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Table(candidate)[0])(candidate, &adapterId, &value));
                adapter = value; Release(candidate); break;
            }
            Release(candidate);
        }
    }
    public (ulong Local, ulong NonLocal) Read()
    {
        MemoryInfo local = default, nonlocal = default;
        Check(((delegate* unmanaged[Stdcall]<nint, uint, int, MemoryInfo*, int>)Table(adapter)[14])(adapter, 0, 0, &local));
        Check(((delegate* unmanaged[Stdcall]<nint, uint, int, MemoryInfo*, int>)Table(adapter)[14])(adapter, 0, 1, &nonlocal));
        return (local.CurrentUsage, nonlocal.CurrentUsage);
    }
    public void Dispose() { Release(adapter); Release(factory); adapter = factory = 0; }
    private static nint* Table(nint p) => *(nint**)p;
    private static void Release(nint p) { if (p != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)Table(p)[2])(p); }
    private static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryInfo { public ulong Budget, CurrentUsage, AvailableForReservation, CurrentReservation; }
    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid iid, out nint result);
}
