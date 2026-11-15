using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ZenithClipper.Native.Video;

internal sealed class HighResolutionPacer : IDisposable
{
    private readonly nint timer = CreateWaitableTimerExW(0, 0, 2, 0x1F0003);
    public HighResolutionPacer()
    { if (timer == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
    public void WaitUntil(Stopwatch clock, double targetSeconds, ref int stopping)
    {
        double remaining;
        while ((remaining = targetSeconds - clock.Elapsed.TotalSeconds) > 0 && Volatile.Read(ref stopping) == 0)
        {
            if (remaining > 0.0003)
            {
                long dueTime = -(long)((Math.Min(remaining, 0.05) - 0.00015) * 10_000_000);
                if (!SetWaitableTimer(timer, ref dueTime, 0, 0, 0, false))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                if (WaitForSingleObject(timer, 1000) != 0) throw new TimeoutException("Video pacing timer failed.");
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

