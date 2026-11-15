using System.Diagnostics;
using ZenithClipper.Native.Video;

if (args.Contains("--keyframe-tests")) return KeyFrameChecks.Run(false);
if (args.Contains("--keyframe-live")) return KeyFrameChecks.Run(true);
if (args.Contains("--lifetime-tests")) return LifetimeChecks.Run();
if (args.Contains("--pacer-tests")) return PacerChecks.Run();
if (args.Contains("--restart-test")) return RestartChecks.Run(args);

if (args.Contains("--preload")) {
    Console.Error.WriteLine("Preload native DLL begin");
    System.Runtime.InteropServices.NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "nvenc_shim.dll"));
    Console.Error.WriteLine("Preload native DLL done");
}
bool injectFault = args.Contains("--callback-fault"), finishingFault = args.Contains("--finishing-fault");
bool motion = args.Contains("--motion");
int widthIndex = Array.IndexOf(args, "--width");
int outputWidth = widthIndex < 0 ? 0 : int.Parse(args[widthIndex + 1]);
int bufferIndex = Array.IndexOf(args, "--buffers");
int buffers = bufferIndex < 0 ? 1 : int.Parse(args[bufferIndex + 1]);
using FileStream output = File.Create(Path.Combine(AppContext.BaseDirectory, "component.h264"));
long packets = 0, keys = 0;
int callbackThread = 0, finishingThread = 0, finishingCount = 0;
bool faultRaised = false;
using NativeVideoRecorder recorder = new(new NativeVideoConfiguration { FrameRate = 0, OutputWidth = outputWidth, FramePoolBuffers = buffers, UseNv12Input = args.Contains("--nv12") },
    (ReadOnlySpan<byte> packet, long index, bool key) =>
    {
        callbackThread = Environment.CurrentManagedThreadId;
        if (index != packets) throw new InvalidOperationException("Packet index discontinuity.");
        if (injectFault && index == 10) throw new InvalidOperationException("Injected packet callback fault.");
        output.Write(packet); packets++; if (key) keys++;
    });
recorder.WorkerFinishing += () =>
{
    finishingThread = Environment.CurrentManagedThreadId; finishingCount++;
    if (finishingFault) throw new InvalidOperationException("Injected finishing fault.");
};
recorder.CaptureFault += error => { faultRaised = true; Console.WriteLine("Fault: " + error.Message); };
Console.WriteLine($"Resolved dimensions: {recorder.Width}x{recorder.Height}, source={recorder.SourceWidth}x{recorder.SourceHeight}, fps={recorder.FrameRate}");
using GpuMemorySampler gpuMemory = new();
Console.Error.WriteLine("Pattern launch begin");
Process? pattern = motion ? Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "motion-window.exe"))
    { UseShellExecute = false, CreateNoWindow = true }) : null;
Console.Error.WriteLine("Pattern launch complete");
try
{
    recorder.Start();
    for (int second = 1; second <= 5 && recorder.Fault is null; second++)
    {
        Thread.Sleep(1000);
        using Process process = Process.GetCurrentProcess();
        NativeVideoStatistics stat = recorder.Statistics;
        var gpu = gpuMemory.Read();
        Console.WriteLine($"second={second} packets={stat.EncodedFrames} captured={stat.CapturedFrames} repeats={stat.RepeatedFrames} keys={keys} ws={process.WorkingSet64} peakWs={process.PeakWorkingSet64} private={process.PrivateMemorySize64} misses={stat.DeadlineMisses} gpuLocal={gpu.Local} gpuNonLocal={gpu.NonLocal}");
    }
    recorder.Stop();
}
finally
{
    if (pattern is not null)
    {
        if (!pattern.HasExited) { pattern.CloseMainWindow(); if (!pattern.WaitForExit(1000)) pattern.Kill(); }
        pattern.Dispose();
    }
}
output.Flush();
using (Process final = Process.GetCurrentProcess())
    Console.WriteLine($"Stopped packets={packets} keys={keys} peakWs={final.PeakWorkingSet64} fault={recorder.Fault?.Message ?? "none"} faultEvent={faultRaised} callbackThread={callbackThread} finishingThread={finishingThread} finishingCount={finishingCount}");
if (finishingCount != 1 || finishingThread != callbackThread) return 4;
if (injectFault)
{
    if (!faultRaised || recorder.Fault?.Message != "Injected packet callback fault.") return 2;
}
else if (finishingFault)
{
    if (!faultRaised || recorder.Fault?.Message != "Injected finishing fault.") return 5;
}
else if (recorder.Fault is not null || packets < recorder.FrameRate * 4 || keys < 3) return 3;
return 0;





