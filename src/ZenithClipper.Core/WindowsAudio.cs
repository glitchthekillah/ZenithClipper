using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ZenithClipper;

/// <summary>One native WASAPI process-loopback stream, PCM16 stereo 48 kHz.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAudio : IAsyncDisposable
{
    readonly CancellationTokenSource stop = new();
    readonly NamedPipeServerStream? pipe;
    readonly Action<byte[], int>? onAudio;
    readonly Task worker;
    readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string Source { get; }
    public Task Completion => worker;

    public WindowsAudio(int excludePid, bool include = false, Action<byte[], int>? onAudio = null)
    {
        string name = "ZenithClipper-audio-" + Guid.NewGuid().ToString("N");
        Source = @"\\.\pipe\" + name;
        this.onAudio = onAudio;
        if (onAudio == null) pipe = new(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 4096, 65536);
        // WASAPI waits block for the lifetime of a capture. Do not consume CLR pool workers needed by IPC/timers.
        worker = Task.Factory.StartNew(() => Capture(excludePid, include), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
    public Task WaitReadyAsync(CancellationToken ct) => ready.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
    void Capture(int excludePid, bool include)
    {
        IAudioClient? audio = null; IAudioCaptureClient? capture = null;
        try
        {
            Marshal.ThrowExceptionForHR(CoInitializeEx(0, 0));
            var activation = new Activation { Type = 1, ProcessId = (uint)excludePid, Mode = include ? 0u : 1u };
            var memory = Marshal.AllocCoTaskMem(Marshal.SizeOf<Activation>());
            IActivateOperation operation;
            var callback = new ActivationCallback();
            try
            {
                Marshal.StructureToPtr(activation, memory, false);
                var prop = new PropVariant { Vt = 65, Size = (uint)Marshal.SizeOf<Activation>(), Data = memory };
                var iid = typeof(IAudioClient).GUID;
                Marshal.ThrowExceptionForHR(ActivateAudioInterfaceAsync("VAD\\Process_Loopback", ref iid, ref prop, callback, out operation));
                audio = callback.Result.Task.WaitAsync(TimeSpan.FromSeconds(10), stop.Token).GetAwaiter().GetResult();
                GC.KeepAlive(operation);
            }
            finally { Marshal.FreeCoTaskMem(memory); }
            var format = new WaveFormat { Tag = 1, Channels = 2, Samples = 48000, BytesPerSecond = 192000, BlockAlign = 4, Bits = 16 };
            audio.Initialize(0, 0x80000000 | 0x00020000 | 0x00040000, 0, 0, ref format, 0);
            var captureId = typeof(IAudioCaptureClient).GUID;
            audio.GetService(ref captureId, out var service); capture = (IAudioCaptureClient)service;
            using var signal = new AutoResetEvent(false);
            audio.SetEventHandle(signal.SafeWaitHandle.DangerousGetHandle());
            ready.TrySetResult();
            pipe?.WaitForConnectionAsync(stop.Token).GetAwaiter().GetResult();
            audio.Start();
            var packets=new AudioPacketBuffer();
            var handles = new[] { signal, stop.Token.WaitHandle };
            while (!stop.IsCancellationRequested)
            {
                if (WaitHandle.WaitAny(handles, 1000) == 1) break;
                capture.GetNextPacketSize(out var packet);
                while (packet > 0 && !stop.IsCancellationRequested)
                {
                    capture.GetBuffer(out var data, out var frames, out var flags, out _, out _);
                    int count; byte[] buffer;
                    try
                    {
                        count=checked((int)frames*4); buffer=packets.ForPacket(count);
                        if ((flags & 2) != 0) Array.Clear(buffer, 0, count); else Marshal.Copy(data, buffer, 0, count);
                    }
                    finally { capture.ReleaseBuffer(frames); }
                    if (onAudio != null) onAudio(buffer, count);
                    else pipe!.WriteAsync(buffer.AsMemory(0, count), stop.Token).AsTask().GetAwaiter().GetResult();
                    capture.GetNextPacketSize(out packet);
                }
            }
        }
        catch (Exception ex) { ready.TrySetException(ex); if (!stop.IsCancellationRequested) throw; }
        finally
        {
            if (audio != null) { try { audio.Stop(); } catch (COMException) { } }
            if (capture != null) Marshal.ReleaseComObject(capture);
            if (audio != null) Marshal.ReleaseComObject(audio);
            CoUninitialize();
        }
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel(); if (pipe != null) await pipe.DisposeAsync();
        try { await worker.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception ex) when (ex is OperationCanceledException or IOException or TimeoutException or COMException) { }
        stop.Dispose();
    }

    // Activation parameters are 12 bytes. BLOB's pointer is naturally aligned at offset 16 on x64.
    [StructLayout(LayoutKind.Sequential)] struct Activation { public uint Type, ProcessId, Mode; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] struct PropVariant { [FieldOffset(0)] public ushort Vt; [FieldOffset(8)] public uint Size; [FieldOffset(16)] public nint Data; }
    [StructLayout(LayoutKind.Sequential, Pack = 2)] public struct WaveFormat { public ushort Tag, Channels; public uint Samples, BytesPerSecond; public ushort BlockAlign, Bits, Extra; }
    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioClient
    {
        void Initialize(int mode, uint flags, long duration, long periodicity, ref WaveFormat format, nint session);
        void GetBufferSize(out uint frames); void GetStreamLatency(out long latency); void GetCurrentPadding(out uint padding);
        [PreserveSig] int IsFormatSupported(int mode, nint format, out nint closest);
        void GetMixFormat(out nint format); void GetDevicePeriod(out long normal, out long minimum);
        void Start(); void Stop(); void Reset(); void SetEventHandle(nint handle);
        void GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }
    [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioCaptureClient
    {
        void GetBuffer(out nint data, out uint frames, out uint flags, out ulong position, out ulong qpc);
        void ReleaseBuffer(uint frames); void GetNextPacketSize(out uint frames);
    }
    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IActivateOperation { void GetActivateResult(out int result, [MarshalAs(UnmanagedType.IUnknown)] out object client); }
    [Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), ComVisible(true)]
    public interface ICompletion { [PreserveSig] int ActivateCompleted(IActivateOperation operation); }
    [Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), ComVisible(true)]
    public interface IAgileObject { }
    [ClassInterface(ClassInterfaceType.None), ComVisible(true)]
    public sealed class ActivationCallback : ICompletion, IAgileObject
    {
        public TaskCompletionSource<IAudioClient> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ActivateCompleted(IActivateOperation operation)
        {
            try { operation.GetActivateResult(out int hr, out var client); Marshal.ThrowExceptionForHR(hr); Result.TrySetResult((IAudioClient)client); }
            catch (Exception ex) { Result.TrySetException(ex); }
            return 0;
        }
    }
    [DllImport("Mmdevapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int ActivateAudioInterfaceAsync(string device, ref Guid iid, ref PropVariant parameters, ICompletion callback, out IActivateOperation operation);
    [DllImport("ole32.dll")] static extern int CoInitializeEx(nint reserved, uint mode);
    [DllImport("ole32.dll")] static extern void CoUninitialize();
}

public static class ProcessAudioPolicy
{
    public static int ResolveExcludedPid(IEnumerable<string> excluded)
    {
        var matches = new List<Process>();
        foreach (var process in Process.GetProcesses())
        {
            try { if (AudioPolicy.Excluded(process.ProcessName, excluded)) matches.Add(process); else process.Dispose(); }
            catch (InvalidOperationException) { process.Dispose(); }
        }
        try
        {
            if (matches.Count == 0) return 0; // exclude nonexistent PID 0 = all audible applications
            if (!OperatingSystem.IsWindows()) return matches[0].Id;
            var parents = WindowsProcesses.Parents(); var ids = matches.Select(x => x.Id).ToHashSet();
            var roots = matches.Where(x => !HasMatchingAncestor(x.Id, parents, ids)).ToArray();
            if (roots.Length != 1) throw new IOException("Windows lightweight exclusion supports one running process tree. Multiple unrelated excluded apps are running; capture is paused rather than leak their audio. Keep one exclusion or disable audio.");
            return roots[0].Id;
        }
        finally { foreach (var process in matches) process.Dispose(); }
    }
    static bool HasMatchingAncestor(int pid, Dictionary<int, int> parents, HashSet<int> matches)
    {
        var seen = new HashSet<int>();
        while (parents.TryGetValue(pid, out pid) && pid != 0 && seen.Add(pid)) if (matches.Contains(pid)) return true;
        return false;
    }
}

[SupportedOSPlatform("windows")]
static class WindowsProcesses
{
    public static Dictionary<int,int> Parents()=>Snapshot().Parents;
    public static ProcessSnapshot Snapshot()
    {
        var result = new Dictionary<int, int>(); var names=new Dictionary<int,string>(); nint snap = CreateToolhelp32Snapshot(2, 0);
        if (snap == -1) throw new System.ComponentModel.Win32Exception();
        try
        {
            var entry = new Entry { Size = (uint)Marshal.SizeOf<Entry>(), File = "" };
            if(!Process32FirstW(snap,ref entry))throw new System.ComponentModel.Win32Exception();
            do { result[(int)entry.Pid] = (int)entry.Parent; names[(int)entry.Pid]=entry.File; } while (Process32NextW(snap, ref entry));
            int failure=Marshal.GetLastWin32Error();if(failure!=18)throw new System.ComponentModel.Win32Exception(failure);
        }
        finally { CloseHandle(snap); }
        return new(result,names);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct Entry { public uint Size, Usage, Pid; public nuint Heap; public uint Module, Threads, Parent; public int Priority; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string File; }
    [DllImport("kernel32.dll", SetLastError = true)] static extern nint CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError=true)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool Process32FirstW(nint snapshot, ref Entry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError=true)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool Process32NextW(nint snapshot, ref Entry entry);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool CloseHandle(nint handle);
}
