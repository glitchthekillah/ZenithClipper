using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ZenithClipper;

/// <summary>Prevent orphaned capture processes if the engine crashes.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsJob : IDisposable
{
    nint handle;
    public WindowsJob(Process process)
    {
        handle=CreateJobObjectW(0,null); if(handle==0) throw new System.ComponentModel.Win32Exception();
        nint info=Marshal.AllocHGlobal(144);
        try
        {
            for(int i=0;i<144;i++) Marshal.WriteByte(info,i,0);
            Marshal.WriteInt32(info,16,0x2000); // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            if(!SetInformationJobObject(handle,9,info,144) || !AssignProcessToJobObject(handle,process.Handle)) throw new System.ComponentModel.Win32Exception();
        }
        catch { Dispose(); if(!process.HasExited) process.Kill(true); throw; }
        finally { Marshal.FreeHGlobal(info); }
    }
    public void Dispose() { if(handle!=0) { CloseHandle(handle); handle=0; } }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern nint CreateJobObjectW(nint attributes,string? name);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool SetInformationJobObject(nint job,int infoClass,nint info,uint size);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool AssignProcessToJobObject(nint job,nint process);
    [DllImport("kernel32.dll")] [return:MarshalAs(UnmanagedType.Bool)] static extern bool CloseHandle(nint handle);
}
