using System.Diagnostics;
using System.Runtime.InteropServices;
using ZenithClipper.Native.Audio;

bool collect = args.Contains("--collect", StringComparer.Ordinal);
string[] rules = ["Spotify"];
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
byte[] packet = new byte[3840];
Console.WriteLine($"NativeAudioRestarts pid={Environment.ProcessId} collect={collect} rules=Spotify cycles=5 durationSeconds=3");
Snapshot("start", 0);
for (int cycle = 1; cycle <= 5; cycle++)
{
    var view = NativeAudioSessions.Read(rules);
    Console.WriteLine($"POLICY cycle={cycle} included={string.Join(',',view.Included)} excluded={string.Join(',',view.Excluded)}");
    long allocated = GC.GetTotalAllocatedBytes(false);
    using var process = Process.GetCurrentProcess();
    TimeSpan cpu = process.TotalProcessorTime;
    await Capture(cycle, view.Included.Where(pid => pid != Environment.ProcessId).ToArray());
    Console.WriteLine($"WORK cycle={cycle} cpuMs={(process.TotalProcessorTime-cpu).TotalMilliseconds:F3} allocatedBytes={GC.GetTotalAllocatedBytes(false)-allocated}");
    await Task.Delay(200, timeout.Token);
    Snapshot("paused", cycle);
    if (collect)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        await Task.Delay(200, timeout.Token);
        Snapshot("collected", cycle);
    }
}
Console.WriteLine("PASS five audio-only restart cycles; all mixers disposed without capture faults");

async Task Capture(int cycle, int[] externalOwners)
{
    using var mixer = new NativeAudioMixer(new ZenithClipper.Settings { ExcludedApps = rules });
    await mixer.StartAsync(timeout.Token);
    mixer.BeginEpoch();
    var live = NativeAudioSessions.Read(rules);
    Console.WriteLine($"LIVE cycle={cycle} included={string.Join(',',live.Included)} selfIncluded={live.Included.Contains(Environment.ProcessId)}");
    if (args.Contains("--verify-no-self", StringComparer.Ordinal))
    {
        if (live.Included.Contains(Environment.ProcessId)) throw new Exception("Recorder own loopback session became an included capture tree.");
        foreach (int pid in externalOwners) if (!live.Included.Contains(pid)) throw new Exception($"External audio owner {pid} was lost.");
    }
    Snapshot("active", cycle);
    Stopwatch timer = Stopwatch.StartNew();
    for (int index = 0; index < 150; index++)
    {
        double remaining = index * 20 - timer.Elapsed.TotalMilliseconds;
        if (remaining > 0) await Task.Delay(TimeSpan.FromMilliseconds(remaining), timeout.Token);
        if (mixer.Faulted) throw new IOException("Audio capture faulted.");
        mixer.Read20Milliseconds(packet);
    }
    double finalDelay = 3000 - timer.Elapsed.TotalMilliseconds;
    if (finalDelay > 0) await Task.Delay(TimeSpan.FromMilliseconds(finalDelay), timeout.Token);
    Snapshot("active-end", cycle);
}

static void Snapshot(string stage, int cycle)
{
    // Read the GC's counters without requesting a collection or finalization.
    long managed = GC.GetTotalMemory(false);
    var gc = GC.GetGCMemoryInfo();
    using var process = Process.GetCurrentProcess();
    process.Refresh();
    ulong privateCommitted = 0; nuint address = 0;
    while (Native.VirtualQuery((nint)address, out var region, (nuint)Marshal.SizeOf<Native.MemoryRegion>()) != 0)
    {
        if (region.State == 0x1000 && region.Type == 0x20000) privateCommitted += (ulong)region.Size;
        nuint next = (nuint)region.Base + region.Size;
        if (next <= address) break;
        address = next;
    }
    Console.WriteLine(FormattableString.Invariant($"MEM stage={stage} cycle={cycle} wsMiB={process.WorkingSet64/1048576.0:F3} peakMiB={process.PeakWorkingSet64/1048576.0:F3} privateMiB={process.PrivateMemorySize64/1048576.0:F3} privateCommittedMiB={privateCommitted/1048576.0:F3} handles={process.HandleCount} threads={process.Threads.Count} managedBytes={managed} gcHeapBytes={gc.HeapSizeBytes} gcCommittedBytes={gc.TotalCommittedBytes} gen0={GC.CollectionCount(0)} gen1={GC.CollectionCount(1)} gen2={GC.CollectionCount(2)}"));
}

static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryRegion
    {
        internal nint Base, AllocationBase;
        internal uint AllocationProtect;
        internal ushort PartitionId;
        internal nuint Size;
        internal uint State, Protect, Type;
    }
    [DllImport("kernel32.dll")]
    internal static extern nuint VirtualQuery(nint address, out MemoryRegion info, nuint length);
}

