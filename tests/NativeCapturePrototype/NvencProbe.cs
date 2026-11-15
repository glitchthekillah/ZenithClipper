using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

internal static class NvencProbe
{
    internal static int Run(nint nativeDevice, IDirect3DDevice device, GraphicsCaptureItem item,
        int seconds, int buffers, int fps, Action<string, long> snapshot)
    {
        if (fps < 1 || fps > 240) throw new ArgumentOutOfRangeException(nameof(fps));
        string output = Path.Combine(AppContext.BaseDirectory, "wgc-nvenc-probe.h264");
        nint encoder = 0;
        try
        {
            Check(nc_create(nativeDevice, (uint)item.Size.Width, (uint)item.Size.Height, (uint)fps, 20, output, out encoder));
            snapshot("nvenc-created", 0);
            using Direct3D11CaptureFramePool pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                device, DirectXPixelFormat.B8G8R8A8UIntNormalized, buffers, item.Size);
            using GraphicsCaptureSession session = pool.CreateCaptureSession(item);
            session.StartCapture();
            long captured = 0, encoded = 0, repeats = 0, deadlineMisses = 0;
            long firstWait = Stopwatch.GetTimestamp();
            Direct3D11CaptureFrame? first = null;
            while (first is null && Stopwatch.GetElapsedTime(firstWait).TotalSeconds < 3)
            { first = pool.TryGetNextFrame(); if (first is null) Thread.Sleep(1); }
            if (first is null) throw new TimeoutException("No WGC frame within 3 seconds.");
            using (first) { Copy(encoder, first.Surface); captured++; }
            Console.WriteLine($"{{\"mode\":\"direct-nvenc\",\"width\":{item.Size.Width},\"height\":{item.Size.Height},\"fps\":{fps},\"qp\":20,\"bframes\":0,\"buffers\":{buffers},\"ownedBgraTextures\":1}}");
            using HighResolutionPacer pacer = new();
            Stopwatch clock = Stopwatch.StartNew();
            for (int index = 0; index < fps * seconds; index++)
            {
                double due = (double)index / fps;
                pacer.WaitUntil(clock, due);

                using (Direct3D11CaptureFrame? frame = pool.TryGetNextFrame())
                {
                    if (frame is not null) { Copy(encoder, frame.Surface); captured++; }
                    else repeats++;
                }
                Check(nc_encode(encoder, (ulong)index * 10_000_000 / (uint)fps));
                encoded++;
                if (clock.Elapsed.TotalSeconds > (double)(index + 1) / fps) deadlineMisses++;
                if (encoded % fps == 0)
                {
                    snapshot($"nvenc-{encoded / fps}s", encoded);
                    Console.WriteLine(FormattableString.Invariant($"{{\"encoded\":{encoded},\"captured\":{captured},\"repeats\":{repeats},\"deadlineMisses\":{deadlineMisses},\"elapsedSeconds\":{clock.Elapsed.TotalSeconds:F6}}}"));
                }
            }
            Check(nc_flush(encoder));
            snapshot("nvenc-complete", encoded);
            return 0;
        }
        finally { if (encoder != 0) nc_destroy(encoder); }
    }
    private sealed class HighResolutionPacer : IDisposable
    {
        private readonly nint timer = CreateWaitableTimerExW(0, 0, 2, 0x1F0003);
        public HighResolutionPacer()
        {
            if (timer == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        public void WaitUntil(Stopwatch clock, double targetSeconds)
        {
            double remaining;
            while ((remaining = targetSeconds - clock.Elapsed.TotalSeconds) > 0)
            {
                if (remaining > 0.0003)
                {
                    long dueTime = -(long)((remaining - 0.00015) * 10_000_000);
                    if (!SetWaitableTimer(timer, ref dueTime, 0, 0, 0, false))
                        throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                    if (WaitForSingleObject(timer, 1000) != 0) throw new TimeoutException("Pacing timer failed.");
                }
                else Thread.SpinWait(100);
            }
        }
        public void Dispose() => CloseHandle(timer);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern nint CreateWaitableTimerExW(nint attributes, nint name, uint flags, uint access);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWaitableTimer(nint timer, ref long dueTime, int period, nint callback, nint argument, [MarshalAs(UnmanagedType.Bool)] bool resume);
        [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(nint handle, uint milliseconds);
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(nint handle);
    }
    private static void Copy(nint encoder, IDirect3DSurface surface)
    {
        nint abi = WinRT.MarshalInterface<IDirect3DSurface>.FromManaged(surface);
        try { Check(nc_copy_surface(encoder, abi)); }
        finally { Marshal.Release(abi); }
    }
    private static void Check(int status)
    {
        if (status != 0) throw new InvalidOperationException(Marshal.PtrToStringAnsi(nc_error()) ?? $"NVENC status {status}");
    }
    [DllImport("nvenc_shim.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private static extern int nc_create(nint device, uint width, uint height, uint fps, uint qp, string output, out nint encoder);
    [DllImport("nvenc_shim.dll", CallingConvention = CallingConvention.Cdecl)] private static extern nint nc_error();
    [DllImport("nvenc_shim.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int nc_copy_surface(nint encoder, nint surface);
    [DllImport("nvenc_shim.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int nc_encode(nint encoder, ulong timestamp);
    [DllImport("nvenc_shim.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int nc_flush(nint encoder);
    [DllImport("nvenc_shim.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void nc_destroy(nint encoder);
}

