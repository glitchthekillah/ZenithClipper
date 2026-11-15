using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

// Experimental WGC-only floor probe. No encoder, audio, file output, or CPU pixel mapping.
internal static unsafe class Program
{
    private const int NotFound = unchecked((int)0x887A0002);
    private static readonly Guid FactoryId = new("770aae78-f26f-4dba-a829-253c83d1b387");
    private static readonly Guid Adapter3Id = new("645967a4-1392-4310-a798-8053ce3e93fd");
    private static readonly Guid DxgiDeviceId = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    private static readonly Guid ItemInteropId = new("3628e81b-3cac-4c60-b7f4-23ce0e0c3356");
    private static readonly Guid ItemId = new("79c3f95b-31f7-4ec2-a464-632ef5d30760");

    private static int Main(string[] args)
    {
        if (!args.Contains("--capture"))
        {
            Console.WriteLine("WGC floor probe: --capture --adapter <index> [--seconds 5] [--buffers 2]");
            Console.WriteLine("Creates a GPU device and captures the primary monitor only with --capture. No encoding.");
            return 0;
        }
        int adapterIndex = Option(args, "--adapter", -1);
        int seconds = Option(args, "--seconds", 5);
        int buffers = Option(args, "--buffers", 2);
        if (adapterIndex < 0 || seconds < 1 || seconds > 60 || buffers < 1 || buffers > 3)
            throw new ArgumentException("Require explicit adapter index, seconds 1..60, buffers 1..3.");
        Check(RoInitialize(1));
        nint factory = 0, adapter = 0, device = 0, context = 0, dxgi = 0, inspectable = 0;
        try
        {
            Guid factoryId = FactoryId;
            Check(CreateDXGIFactory1(ref factoryId, out factory));
            nint value = 0;
            Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Table(factory)[12])(factory, (uint)adapterIndex, &value));
            adapter = value;
            Snapshot("before-device", adapter, 0);
            Check(D3D11CreateDevice(adapter, 0, 0, 0x20, 0, 0, 7, out device, out _, out context));
            dxgi = Query(device, DxgiDeviceId);
            Check(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out inspectable));
            using IDirect3DDevice projectedDevice = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
            GraphicsCaptureItem item = CreateItem();
            if (args.Contains("--nvenc")) return NvencProbe.Run(device, projectedDevice, item, seconds, buffers, Option(args, "--fps", 165),
                (phase, frames) => Snapshot(phase, adapter, frames));
            if (args.Contains("--encode")) return EncodeProbe.Run(projectedDevice, item, seconds, buffers, Option(args, "--fps", 165),
                (phase, frames) => { Snapshot(phase, adapter, frames); nint other = 0;
                    Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Table(factory)[12])(factory, 0, &other));
                    try { Snapshot(phase + "-adapter0", other, frames); } finally { Release(other); }
                }).GetAwaiter().GetResult();
            Snapshot("before-pool", adapter, 0);
            using Direct3D11CaptureFramePool pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                projectedDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, buffers, item.Size);
            using GraphicsCaptureSession session = pool.CreateCaptureSession(item);
            long frames = 0;
            pool.FrameArrived += (sender, _) =>
            {
                using Direct3D11CaptureFrame? frame = sender.TryGetNextFrame();
                if (frame is not null) Interlocked.Increment(ref frames);
            };
            Console.WriteLine(JsonSerializer.Serialize(new CaptureConfiguration("capture-only", adapterIndex, buffers,
                item.Size.Width, item.Size.Height, seconds), CaptureJson.Default.CaptureConfiguration));
            session.StartCapture();
            for (int second = 1; second <= seconds; second++)
            {
                Thread.Sleep(1000);
                Snapshot($"capture-{second}s", adapter, Interlocked.Read(ref frames));
            }
            session.Dispose();
            pool.Dispose();
            Snapshot("after-pool", adapter, Interlocked.Read(ref frames));
            return 0;
        }
        finally
        {
            Release(inspectable); Release(dxgi); Release(context); Release(device); Release(adapter); Release(factory);
            RoUninitialize();
        }
    }

    private static int Option(string[] args, string key, int fallback)
    {
        int i = Array.IndexOf(args, key);
        return i < 0 ? fallback : i + 1 < args.Length ? int.Parse(args[i + 1]) : throw new ArgumentException(key);
    }
    private static GraphicsCaptureItem CreateItem()
    {
        string name = "Windows.Graphics.Capture.GraphicsCaptureItem";
        Check(WindowsCreateString(name, name.Length, out nint hstring));
        nint interop = 0, item = 0;
        try
        {
            Guid iid = ItemInteropId;
            Check(RoGetActivationFactory(hstring, ref iid, out interop));
            Guid itemId = ItemId;
            nint monitor = MonitorFromPoint(default, 1);
            Check(((delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)Table(interop)[4])(
                interop, monitor, &itemId, &item));
            return WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(item);
        }
        finally { Release(item); Release(interop); WindowsDeleteString(hstring); }
    }
    private static void Snapshot(string phase, nint adapter, long frames)
    {
        using Process process = Process.GetCurrentProcess();
        nint adapter3 = Query(adapter, Adapter3Id);
        try
        {
            MemoryInfo local = default, nonlocal = default;
            Check(((delegate* unmanaged[Stdcall]<nint, uint, int, MemoryInfo*, int>)Table(adapter3)[14])(adapter3, 0, 0, &local));
            Check(((delegate* unmanaged[Stdcall]<nint, uint, int, MemoryInfo*, int>)Table(adapter3)[14])(adapter3, 0, 1, &nonlocal));
            Console.WriteLine(JsonSerializer.Serialize(new CaptureSnapshot(phase, frames, process.WorkingSet64,
                process.PrivateMemorySize64, local.CurrentUsage,
                nonlocal.CurrentUsage, GC.GetTotalMemory(false)), CaptureJson.Default.CaptureSnapshot));
        }
        finally { Release(adapter3); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryInfo
    { public ulong Budget, CurrentUsage, AvailableForReservation, CurrentReservation; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    private static nint* Table(nint p) => *(nint**)p;
    private static nint Query(nint p, Guid iid)
    { nint result = 0; Check(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Table(p)[0])(p, &iid, &result)); return result; }
    private static void Release(nint p) { if (p != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)Table(p)[2])(p); }
    private static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid iid, out nint factory);
    [DllImport("d3d11.dll")] private static extern int D3D11CreateDevice(nint adapter, int driverType, nint software,
        uint flags, nint featureLevels, uint featureLevelCount, uint sdkVersion, out nint device, out int featureLevel, out nint context);
    [DllImport("d3d11.dll")] private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint inspectable);
    [DllImport("combase.dll")] private static extern int RoInitialize(uint initType);
    [DllImport("combase.dll")] private static extern void RoUninitialize();
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string text, int length, out nint hstring);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(nint hstring);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(nint hstring, ref Guid iid, out nint instance);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(Point point, uint flags);
}





internal sealed record CaptureConfiguration(string mode, int adapterIndex, int buffers, int width, int height, int seconds);
internal sealed record CaptureSnapshot(string phase, long frames, long workingSetBytes, long privateBytes,
    ulong gpuLocalBytes, ulong gpuNonLocalBytes, long managedBytes);
internal sealed record MfConfiguration(string mode, bool CanTranscode, string failure, string output,
    int width, int height, int requestedFrameRate, int bitrate, bool constantQpValidated, int buffers);
internal sealed record MfSamples(long samples, int retainedFrames);
[System.Text.Json.Serialization.JsonSerializable(typeof(CaptureConfiguration))]
[System.Text.Json.Serialization.JsonSerializable(typeof(CaptureSnapshot))]
[System.Text.Json.Serialization.JsonSerializable(typeof(MfConfiguration))]
[System.Text.Json.Serialization.JsonSerializable(typeof(MfSamples))]
internal partial class CaptureJson : System.Text.Json.Serialization.JsonSerializerContext { }
