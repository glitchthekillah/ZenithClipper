using System.Diagnostics;
using System.IO.Pipes;
// Isolated controller floor. No audio, capture, or product IPC.
using var pipe = new NamedPipeServerStream("ZenithClipper-AotFeasibility-" + Environment.ProcessId, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
using var process = Process.GetCurrentProcess();
for (int sample = 0; sample < 6; sample++) {
 await Task.Delay(1000); process.Refresh();
 Console.WriteLine($"sample={sample} privateMiB={process.PrivateMemorySize64 / 1048576.0:F2} workingMiB={process.WorkingSet64 / 1048576.0:F2}");
}
