using System.Diagnostics;
using System.Runtime.InteropServices;
using ZenithClipper;

const int Capacity = 24000;
var random = new Random(887);
var queue = new Queue<short>();
var ring = new PcmRing();
for (int iteration = 0; iteration < 3000; iteration++)
{
    if (iteration % 47 == 0) { ring.Clear(); queue.Clear(); }
    int length = iteration % 11 == 0 ? random.Next(24000, 60000) : random.Next(0, 4000);
    short[] values = new short[length]; random.NextBytes(MemoryMarshal.AsBytes(values.AsSpan()));
    byte[] bytes = MemoryMarshal.AsBytes(values.AsSpan()).ToArray(); ring.Write(bytes, bytes.Length);
    foreach (short value in values) { if (queue.Count == Capacity) queue.Dequeue(); queue.Enqueue(value); }
    short[] actual = new short[random.Next(0, 28000)]; Array.Fill(actual, (short)123);
    ring.Read(actual);
    foreach (short value in actual) { short expected = queue.TryDequeue(out short next) ? next : (short)0; if (expected != value) throw new Exception($"PCM sample mismatch at iteration {iteration}"); }
}
Console.WriteLine("PASS 3000 independent FIFO comparisons: wrap, overflow, large packets, silence padding and clear");
long beforeAllocation = GC.GetAllocatedBytesForCurrentThread();
for (int i = 0; i < 20; i++) GC.KeepAlive(new BaselineRing());
long baselineAllocation = GC.GetAllocatedBytesForCurrentThread() - beforeAllocation;
beforeAllocation = GC.GetAllocatedBytesForCurrentThread();
for (int i = 0; i < 20; i++) GC.KeepAlive(new PcmRing());
long currentAllocation = GC.GetAllocatedBytesForCurrentThread() - beforeAllocation;
if (currentAllocation > baselineAllocation) throw new Exception("PCM ring allocation increased");
Console.WriteLine($"PASS fixed-capacity allocation: baseline={baselineAllocation/20} current={currentAllocation/20} bytes per ring");
byte[] packet = new byte[3840]; random.NextBytes(packet); short[] output = new short[1920];
var baseline = new BaselineRing();
for (int i = 0; i < 2000; i++) { baseline.Write(packet, packet.Length); baseline.Read(output); ring.Write(packet, packet.Length); ring.Read(output); }
for (int run = 0; run < 4; run++)
{
    if (run % 2 == 0) { Measure("baseline", () => { baseline.Write(packet, packet.Length); baseline.Read(output); }); Measure("current", () => { ring.Write(packet, packet.Length); ring.Read(output); }); }
    else { Measure("current", () => { ring.Write(packet, packet.Length); ring.Read(output); }); Measure("baseline", () => { baseline.Write(packet, packet.Length); baseline.Read(output); }); }
}
static void Measure(string name, Action action)
{
    long allocation = GC.GetAllocatedBytesForCurrentThread(); using var process = Process.GetCurrentProcess(); var cpu = process.TotalProcessorTime; var clock = Stopwatch.StartNew();
    for (int i = 0; i < 100000; i++) action();
    double elapsed = clock.Elapsed.TotalMilliseconds; double cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
    long bytes = GC.GetAllocatedBytesForCurrentThread() - allocation;
    Console.WriteLine($"{name}: iterations=100000 cpuMs={cpuMs:F3} wallMs={elapsed:F3} allocatedBytes={bytes}");
}
