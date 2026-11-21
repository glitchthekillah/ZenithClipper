using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ZenithClipper;

[SupportedOSPlatform("windows")]
public static class WindowsSessions
{
    // Include only known eligible sessions when a single exclusion tree cannot express the policy.
    // A newly launched excluded app is never added to this allowlist.
    public static int[] IncludedPids(string[] exclusions)=>Read(exclusions).Included;
    public sealed record View(int[] Included,int[] Excluded,Dictionary<int,int> Parents);
    public static View Read(string[] exclusions, string outputId = "")
    {
        var enumerator = (IMMDeviceEnumerator)new DeviceEnumerator();
        IMMDevice? device = null; IAudioSessionManager2? manager = null; ISessionEnumerator? sessions = null;
        try
        {
            if (outputId.Length == 0) enumerator.GetDefaultAudioEndpoint(0, 1, out device);
            else enumerator.GetDevice(outputId, out device);
            var id = typeof(IAudioSessionManager2).GUID; device.Activate(ref id, 23, 0, out var obj); manager = (IAudioSessionManager2)obj;
            manager.GetSessionEnumerator(out sessions); sessions.GetCount(out int count);
            var snapshot=WindowsProcesses.Snapshot(); var parents=snapshot.Parents; var allowed = new HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                sessions.GetSession(i, out var session);
                try
                {
                    var control = (ISessionControl2)session; control.GetProcessId(out uint pid); control.GetState(out int state);
                    if (pid == 0 || state == 2) continue;
                    allowed.Add((int)pid);
                }
                finally { Marshal.ReleaseComObject(session); }
            }
            var blocked=snapshot.Excluded(exclusions);
            return new(snapshot.AudioTrees(allowed,exclusions,blocked),blocked,parents);
        }
        finally
        {
            if (sessions != null) Marshal.ReleaseComObject(sessions);
            if (manager != null) Marshal.ReleaseComObject(manager);
            if (device != null) Marshal.ReleaseComObject(device);
            Marshal.ReleaseComObject(enumerator);
        }
    }
    /// <summary>Executable names of processes with an active or inactive (not expired) session on the default output.</summary>
    public static string[] ActiveAppNames()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        IMMDeviceEnumerator? enumerator = null; IMMDevice? device = null; IAudioSessionManager2? manager = null; ISessionEnumerator? sessions = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new DeviceEnumerator();
            enumerator.GetDefaultAudioEndpoint(0, 1, out device);
            var id = typeof(IAudioSessionManager2).GUID; device.Activate(ref id, 23, 0, out var obj); manager = (IAudioSessionManager2)obj;
            manager.GetSessionEnumerator(out sessions); sessions.GetCount(out int count);
            int self = Environment.ProcessId;
            for (int i = 0; i < count; i++)
            {
                sessions.GetSession(i, out var session);
                try
                {
                    var control = (ISessionControl2)session; control.GetProcessId(out uint pid); control.GetState(out int state);
                    if (pid == 0 || pid == self || state == 2) continue;
                    try { using var process = Process.GetProcessById((int)pid); names.Add(process.ProcessName); } catch (ArgumentException) { } catch (InvalidOperationException) { }
                }
                finally { Marshal.ReleaseComObject(session); }
            }
        }
        catch (COMException) { }
        finally
        {
            if (sessions != null) Marshal.ReleaseComObject(sessions);
            if (manager != null) Marshal.ReleaseComObject(manager);
            if (device != null) Marshal.ReleaseComObject(device);
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);
        }
        return [.. names];
    }
    static bool HasAncestor(int pid, Dictionary<int, int> parents, HashSet<int> allowed)
    {
        var seen = new HashSet<int>();
        while (parents.TryGetValue(pid, out pid) && pid != 0 && seen.Add(pid)) if (allowed.Contains(pid)) return true;
        return false;
    }
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class DeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int flow, uint mask, out nint devices); void GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IMMDevice
    {
        void Activate(ref Guid iid, uint context, nint parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result);
    }
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IAudioSessionManager2
    {
        void GetAudioSessionControl(nint guid, uint flags, out nint control); void GetSimpleAudioVolume(nint guid, uint flags, out nint volume);
        void GetSessionEnumerator(out ISessionEnumerator enumerator);
    }
    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface ISessionEnumerator
    {
        void GetCount(out int count); void GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object control);
    }
    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface ISessionControl2
    {
        void GetState(out int state); void GetDisplayName(out nint value); void SetDisplayName(nint value, nint context); void GetIconPath(out nint value); void SetIconPath(nint value, nint context); void GetGroupingParam(out Guid value); void SetGroupingParam(ref Guid value, nint context); void RegisterAudioSessionNotification(nint value); void UnregisterAudioSessionNotification(nint value); void GetSessionIdentifier(out nint value); void GetSessionInstanceIdentifier(out nint value); void GetProcessId(out uint pid);
    }
}
