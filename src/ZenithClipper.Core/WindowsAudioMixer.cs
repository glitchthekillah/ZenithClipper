using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ZenithClipper;

/// <summary>Bounded per-session capture, one mixed PCM stream and one AAC encoder. No excluded audio is captured.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAudioMixer : IAsyncDisposable
{
    readonly Settings settings;
    readonly Dictionary<int, (WindowsAudio capture, PcmRing ring)> sources = [];
    readonly CancellationTokenSource stop = new();
    readonly NamedPipeServerStream pipe;
    readonly object sync = new();
    Task? writer;
    WindowsAudio? allAudio;
    ProcessLoopbackCapture? microphone, output;
    readonly PcmRing microphoneRing = new(), outputRing = new();
    public string Source { get; private set; }
    public bool Faulted => writer?.IsFaulted == true || allAudio?.Completion.IsFaulted == true || microphone?.Faulted == true || output?.Faulted == true || sources.Values.Any(x => x.capture.Completion.IsFaulted);
    public WindowsAudioMixer(Settings settings)
    {
        this.settings = settings; string name = "ZenithClipper-mix-" + Guid.NewGuid().ToString("N"); Source = @"\\.\pipe\" + name;
        pipe = new(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 4096, 65536);
    }
    public async Task StartAsync(CancellationToken ct)
    {
        if (settings.CaptureMicrophone)
        {
            microphone = new(settings.WindowsInputDevice, true, microphoneRing.Write);
            await microphone.Ready.WaitAsync(TimeSpan.FromSeconds(10), ct);
        }
        if (settings.CaptureAudio && settings.ExcludedApps.Length == 0)
        {
            if (settings.WindowsOutputDevice.Length > 0)
            {
                output = new(settings.WindowsOutputDevice, false, outputRing.Write);
                await output.Ready.WaitAsync(TimeSpan.FromSeconds(10), ct);
            }
            else
            {
                allAudio = settings.CaptureMicrophone ? new(0, false, outputRing.Write) : new(0);
                await allAudio.WaitReadyAsync(ct);
                if (!settings.CaptureMicrophone) { Source = allAudio.Source; return; }
            }
        }
        await RefreshAsync(ct);
        writer = Task.Run(WriteAsync);
    }
    public bool RequiresHistoryReset()
    {
        if (!settings.CaptureAudio || allAudio != null || output != null) return false;
        var view=WindowsSessions.Read(settings.ExcludedApps, settings.WindowsOutputDevice);
        return AudioPolicy.RootsContainExcluded(sources.Keys,view.Parents,view.Excluded);
    }
    public async Task<bool> RefreshAsync(CancellationToken ct)
    {
        if (!settings.CaptureAudio || allAudio != null || output != null) return false;
        var view=WindowsSessions.Read(settings.ExcludedApps, settings.WindowsOutputDevice);
        var pids = view.Included;
        bool unsafeChange=AudioPolicy.RootsContainExcluded(sources.Keys,view.Parents,view.Excluded);
        if(unsafeChange)return true; // Recorder must discard history before any source mutation can fail.
        if (pids.Length > 32) throw new IOException("More than 32 audio process trees; capture stopped to keep memory bounded.");
        foreach (var pid in sources.Keys.Except(pids).ToArray())
        {
            var old = sources[pid]; lock (sync) sources.Remove(pid); await old.capture.DisposeAsync();
        }
        foreach (var pid in pids.Except(sources.Keys).ToArray())
        {
            var ring = new PcmRing(); var capture = new WindowsAudio(pid, true, ring.Write);
            try { await capture.WaitReadyAsync(ct); lock (sync) sources.Add(pid, (capture, ring)); }
            catch { await capture.DisposeAsync(); throw; }
        }
        return unsafeChange;
    }
    async Task WriteAsync()
    {
        await pipe.WaitForConnectionAsync(stop.Token);
        lock(sync)foreach(var (_,ring) in sources.Values)ring.Clear(); // Align the PCM epoch with video input readiness.
        microphoneRing.Clear(); outputRing.Clear();
        const int samples = 960 * 2; // 20 ms stereo, fixed allocation
        byte[] output = new byte[samples * 2]; int[] mix = new int[samples]; short[] scratch = new short[samples];
        var clock = Stopwatch.StartNew(); long frame = 0;
        while (!stop.IsCancellationRequested)
        {
            Array.Clear(mix);
            lock (sync)
            {
                foreach (var (_, ring) in sources.Values)
                {
                    ring.Read(scratch);
                    for (int i = 0; i < samples; i++) mix[i] += scratch[i];
                }
            }
            if (microphone != null)
            {
                microphoneRing.Read(scratch);
                for (int i = 0; i < samples; i++) mix[i] += scratch[i];
            }
            if (output != null || allAudio != null)
            {
                outputRing.Read(scratch);
                for (int i = 0; i < samples; i++) mix[i] += scratch[i];
            }
            var destination = MemoryMarshal.Cast<byte, short>(output.AsSpan());
            for (int i = 0; i < samples; i++) destination[i] = (short)Math.Clamp(mix[i], short.MinValue, short.MaxValue);
            await pipe.WriteAsync(output, stop.Token);
            frame++; double delay = frame * 20 - clock.Elapsed.TotalMilliseconds;
            if (delay > 0) await Task.Delay(TimeSpan.FromMilliseconds(delay), stop.Token);
            // Backpressure never builds an unbounded backlog. Rebase after a long scheduling stall.
            if (delay < -250) { clock.Restart(); frame = 0; }
        }
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel(); await pipe.DisposeAsync();
        if (writer != null) try { await writer; } catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
        foreach (var (capture, _) in sources.Values) await capture.DisposeAsync(); sources.Clear();
        if (allAudio != null) await allAudio.DisposeAsync();
        try { microphone?.Dispose(); }
        catch (Exception ex) when (ex is COMException or IOException or TimeoutException) { }
        try { output?.Dispose(); }
        catch (Exception ex) when (ex is COMException or IOException or TimeoutException) { }
        stop.Dispose();
    }
}

/// <summary>Quarter-second fixed PCM queue. Old samples are dropped on overrun.</summary>
public sealed class PcmRing
{
    readonly short[] data = new short[48000 / 4 * 2];
    int head, count;
    public void Clear(){lock(data){head=0;count=0;}}
    public void Write(byte[] bytes, int length)
    {
        var input = MemoryMarshal.Cast<byte, short>(bytes.AsSpan(0, length));
        lock (data)
        {
            // Keep the newest bounded history, including packets larger than the ring.
            if (input.Length >= data.Length)
            {
                input[^data.Length..].CopyTo(data);
                head = 0;
                count = data.Length;
                return;
            }
            int overflow = count + input.Length - data.Length;
            if (overflow > 0)
            {
                head = (head + overflow) % data.Length;
                count -= overflow;
            }
            int tail = (head + count) % data.Length;
            int first = Math.Min(input.Length, data.Length - tail);
            input[..first].CopyTo(data.AsSpan(tail));
            input[first..].CopyTo(data);
            count += input.Length;
        }
    }
    public void Read(short[] destination)
    {
        lock (data)
        {
            int available = Math.Min(count, destination.Length);
            int first = Math.Min(available, data.Length - head);
            data.AsSpan(head, first).CopyTo(destination);
            data.AsSpan(0, available - first).CopyTo(destination.AsSpan(first));
            head = (head + available) % data.Length;
            count -= available;
            destination.AsSpan(available).Clear();
        }
    }
}

