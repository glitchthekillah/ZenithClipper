using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ZenithClipper.Native.Video;

/// <summary>One OS worker calls an owning native WGC/NVENC loop. No projected WinRT objects cross the ABI.</summary>
public sealed unsafe class NativeVideoRecorder : IDisposable
{
    private static readonly Lazy<nint> ShimModule = new(() => NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "nvenc_shim.dll")));
    private readonly object lifecycle = new();
    private readonly NativeVideoConfiguration configuration;
    private readonly VideoPacketHandler packetHandler;
    private readonly nint monitor;
    private readonly NativeStateHandle nativeState = new();
    private readonly TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly int simulation;
    private readonly TimeSpan stopTimeout;
    private Thread? worker;
    private int started, stopping, disposed, finishing;
    private Exception? fault, callbackFault, finishingFault;
    private NativeVideoStatistics finalStatistics = new(0, 0, 0, 0, 0, TimeSpan.Zero);
    public event Action<Exception>? CaptureFault;
    /// <summary>Runs once on the video OS worker after encoder flush, before native COM teardown.</summary>
    public event Action? WorkerFinishing;
    public int Width { get; }
    public int Height { get; }
    public int SourceWidth { get; }
    public int SourceHeight { get; }
    public int FrameRate { get; }
    public Exception? Fault => Volatile.Read(ref fault);
    public bool IsRunning => worker?.IsAlive == true && Volatile.Read(ref stopping) == 0;
    public NativeVideoStatistics Statistics
    {
        get
        {
            bool lease = false;
            try
            {
                nativeState.DangerousAddRef(ref lease); var state = (NativeState*)nativeState.DangerousGetHandle();
                return ReadStatistics(state);
            }
            catch (ObjectDisposedException) { return Volatile.Read(ref finalStatistics); }
            finally { if (lease) nativeState.DangerousRelease(); }
        }
    }
    private static NativeVideoStatistics ReadStatistics(NativeState* state) => new(Interlocked.Read(ref state->Encoded), Interlocked.Read(ref state->Captured),
        Interlocked.Read(ref state->Repeated), Interlocked.Read(ref state->Missed), Interlocked.Read(ref state->Bytes),
        TimeSpan.FromSeconds((double)Interlocked.Read(ref state->ElapsedQpc) / Stopwatch.Frequency));
    public NativeVideoRecorder(NativeVideoConfiguration configuration, VideoPacketHandler packetHandler)
        : this(configuration, packetHandler, null, -1, TimeSpan.FromSeconds(10)) { }

    // The friend test executable uses the real native owning loop with counted fake COM objects, without display/GPU access.
    internal NativeVideoRecorder(NativeVideoConfiguration configuration, VideoPacketHandler packetHandler,
        (int Width, int Height, int Refresh)? testDisplay, int simulation, TimeSpan stopTimeout)
    {
        ArgumentNullException.ThrowIfNull(configuration); ArgumentNullException.ThrowIfNull(packetHandler);
        if (configuration.FrameRate < 0 || configuration.FrameRate > 1000 || configuration.FrameRate == 1 || configuration.Quantizer < 0 || configuration.Quantizer > 51 ||
            configuration.FramePoolBuffers is < 1 or > 2 || configuration.OutputWidth < 0 || configuration.OutputHeight < 0)
            throw new ArgumentOutOfRangeException(nameof(configuration));
        this.configuration = configuration; this.packetHandler = packetHandler; this.simulation = simulation; this.stopTimeout = stopTimeout;
        if (testDisplay is { } test) { SourceWidth = test.Width; SourceHeight = test.Height; FrameRate = configuration.FrameRate > 0 ? configuration.FrameRate : test.Refresh; }
        else { var display = DisplayInterop.Select(configuration.MonitorIndex); monitor = display.Monitor; SourceWidth = display.Width; SourceHeight = display.Height; FrameRate = configuration.FrameRate > 0 ? configuration.FrameRate : display.RefreshRate; }
        Width = configuration.OutputWidth > 0 ? Math.Max(2, configuration.OutputWidth & ~1) : SourceWidth;
        Height = configuration.OutputHeight > 0 ? Math.Max(2, configuration.OutputHeight & ~1) :
            configuration.OutputWidth > 0 ? Math.Max(2, (int)((long)Width * SourceHeight / SourceWidth) & ~1) : SourceHeight;
        if ((Width & 1) != 0 || (Height & 1) != 0) throw new NotSupportedException("H.264 4:2:0 requires even dimensions.");
    }
    public void Start()
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this); _ = ShimModule.Value;
            if (Interlocked.CompareExchange(ref started, 1, 0) != 0) throw new InvalidOperationException("A recorder instance can be started only once.");
            worker = new Thread(CaptureWorker) { IsBackground = true, Name = "Zenith native video" }; worker.Start();
        }
        try { ready.Task.WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult(); }
        catch { RequestStop(); throw; }
    }
    private void RequestStop()
    {
        Volatile.Write(ref stopping, 1);
        bool lease = false;
        try { nativeState.DangerousAddRef(ref lease); Interlocked.Exchange(ref ((NativeState*)nativeState.DangerousGetHandle())->Stopping, 1); }
        catch (ObjectDisposedException) { }
        finally { if (lease) nativeState.DangerousRelease(); }
    }
    /// <summary>Requests an IDR on the next frame accepted by the encoding worker; never waits for the GPU.</summary>
    public void RequestKeyFrame()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Volatile.Read(ref stopping) != 0 || Volatile.Read(ref worker)?.IsAlive != true || !ready.Task.IsCompletedSuccessfully)
            throw new InvalidOperationException("Capture is not running; a keyframe cannot be requested.");
        bool lease = false;
        try
        {
            // No lifecycle/GPU wait: the SafeHandle lease alone protects against concurrent disposal.
            nativeState.DangerousAddRef(ref lease); var state = (NativeState*)nativeState.DangerousGetHandle();
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            if (Volatile.Read(ref stopping) != 0 || Volatile.Read(ref state->Stopping) != 0) throw new InvalidOperationException("Capture is stopping; a keyframe cannot be requested.");
            Interlocked.Or(ref state->ControlFlags, 2);
        }
        finally { if (lease) nativeState.DangerousRelease(); }
    }
    public void Stop()
    {
        Thread? captureThread;
        // Serialize with worker publication, but never hold this lock while joining callbacks.
        lock (lifecycle) { RequestStop(); captureThread = worker; }
        if (captureThread is not null && captureThread != Thread.CurrentThread && !captureThread.Join(stopTimeout))
            throw new TimeoutException("The native video driver did not stop within ten seconds.");
    }
    public void Dispose()
    {
        Interlocked.Exchange(ref disposed, 1); Stop();
        // A callback may request Dispose on the worker. Its ABI state and GCHandle remain alive until it returns.
        nativeState.Dispose();
    }
    private void CaptureWorker()
    {
        bool lease = false; GCHandle callbackHandle = default; NativeState* state = null;
        try
        {
            nativeState.DangerousAddRef(ref lease); state = (NativeState*)nativeState.DangerousGetHandle(); callbackHandle = GCHandle.Alloc(this);
            NativeConfig config = new() { Monitor = monitor, SourceWidth = (uint)SourceWidth, SourceHeight = (uint)SourceHeight,
                Width = (uint)Width, Height = (uint)Height, Fps = (uint)FrameRate, Qp = (uint)configuration.Quantizer,
                Buffers = (uint)configuration.FramePoolBuffers, Nv12 = configuration.UseNv12Input ? 1u : 0u };
            byte* error = stackalloc byte[2048]; error[0] = 0;
            fixed (char* output = configuration.OutputPath)
            {
                config.Output = output;
                int status = simulation < 0
                    ? wgc_run(&config, (NativeState*)nativeState.DangerousGetHandle(), &DeliverPacket, &LifecycleEvent, GCHandle.ToIntPtr(callbackHandle), error, 2048)
                    : wgc_run_test(&config, (NativeState*)nativeState.DangerousGetHandle(), &DeliverPacket, &LifecycleEvent, GCHandle.ToIntPtr(callbackHandle), error, 2048, (uint)simulation);
                if (status != 0) throw callbackFault ?? (status == -4097 ? finishingFault : null) ?? new InvalidOperationException(Marshal.PtrToStringUTF8((nint)error) ?? $"Native capture status {status}");
                if (callbackFault != null) throw callbackFault;
                if (finishingFault != null) throw finishingFault;
            }
            if (!ready.Task.IsCompleted) ready.TrySetCanceled();
        }
        catch (Exception error) { RecordFault(error); }
        finally
        {
            // Also covers loader/ABI failures before native code could issue its lifecycle event.
            FinishWorker();
            if (state != null) Volatile.Write(ref finalStatistics, ReadStatistics(state));
            if (callbackHandle.IsAllocated) callbackHandle.Free();
            if (lease) nativeState.DangerousRelease();
        }
    }
    private void RecordFault(Exception error)
    {
        if (Interlocked.CompareExchange(ref fault, error, null) != null) return;
        RequestStop(); ready.TrySetException(error);
        try { CaptureFault?.Invoke(error); } catch { }
    }
    private int FinishWorker()
    {
        if (Interlocked.Exchange(ref finishing, 1) != 0) return finishingFault == null ? 0 : -4096;
        try { WorkerFinishing?.Invoke(); return 0; }
        catch (Exception error) { finishingFault ??= error; return -4096; }
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int DeliverPacket(nint user, byte* data, uint length, ulong index, uint key)
    {
        NativeVideoRecorder? recorder = null;
        try { recorder = (NativeVideoRecorder)GCHandle.FromIntPtr(user).Target!; recorder.packetHandler(new(data, checked((int)length)), checked((long)index), key != 0); return 0; }
        catch (Exception error) { if (recorder != null) { recorder.callbackFault ??= error; recorder.RequestStop(); } return -4096; }
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int LifecycleEvent(nint user, uint kind)
    {
        try
        {
            var recorder = (NativeVideoRecorder)GCHandle.FromIntPtr(user).Target!;
            if (kind == 1) { recorder.ready.TrySetResult(true); return 0; }
            if (kind == 2) return recorder.FinishWorker();
            return -4096;
        }
        catch { return -4096; }
    }
    internal (int Alive, int Destroyed) TestReferences
    {
        get { bool lease = false; try { nativeState.DangerousAddRef(ref lease); var state = (NativeState*)nativeState.DangerousGetHandle(); return (Volatile.Read(ref state->FakeAlive), Volatile.Read(ref state->FakeDestroyed)); } finally { if (lease) nativeState.DangerousRelease(); } }
    }
    internal int TestControlFlags { get { bool lease = false; try { nativeState.DangerousAddRef(ref lease); return Volatile.Read(ref ((NativeState*)nativeState.DangerousGetHandle())->ControlFlags); } finally { if (lease) nativeState.DangerousRelease(); } } }
    internal void ReleaseTestWorker() { bool lease = false; try { nativeState.DangerousAddRef(ref lease); Interlocked.Or(ref ((NativeState*)nativeState.DangerousGetHandle())->ControlFlags, 1); } finally { if (lease) nativeState.DangerousRelease(); } }
    [StructLayout(LayoutKind.Sequential)] private struct NativeConfig { public nint Monitor; public uint SourceWidth, SourceHeight, Width, Height, Fps, Qp, Buffers, Nv12; public char* Output; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeState { public int Stopping, ControlFlags; public long Encoded, Captured, Repeated, Missed, Bytes, ElapsedQpc; public int FakeAlive, FakeDestroyed; }
    private sealed class NativeStateHandle : SafeHandle
    {
        public NativeStateHandle() : base(0, true) { SetHandle((nint)NativeMemory.AllocZeroed((nuint)sizeof(NativeState))); if (handle == 0) throw new OutOfMemoryException(); }
        public override bool IsInvalid => handle == 0;
        protected override bool ReleaseHandle() { NativeMemory.Free((void*)handle); handle = 0; return true; }
    }
    [DllImport("nvenc_shim.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int wgc_run(NativeConfig* config, NativeState* state, delegate* unmanaged[Cdecl]<nint, byte*, uint, ulong, uint, int> packet, delegate* unmanaged[Cdecl]<nint, uint, int> lifecycle, nint user, byte* error, uint capacity);
    [DllImport("nvenc_shim.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int wgc_run_test(NativeConfig* config, NativeState* state, delegate* unmanaged[Cdecl]<nint, byte*, uint, ulong, uint, int> packet, delegate* unmanaged[Cdecl]<nint, uint, int> lifecycle, nint user, byte* error, uint capacity, uint mode);
}
