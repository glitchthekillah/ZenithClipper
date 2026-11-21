using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;
using ZenithClipper;
namespace ZenithClipper.Native.Audio;
public sealed record AudioProcessView(int[] Included,int[] Excluded,Dictionary<int,int> Parents,Dictionary<int,string> Names);
[SupportedOSPlatform("windows")]
public static partial class NativeAudioSessions
{
 public static unsafe AudioProcessView Read(string[] exclusions, string outputId = "")
 {
  Marshal.ThrowExceptionForHR(Api.CoInitializeEx(0,0));IMMDeviceEnumerator? enumerator=null;IMMDevice? device=null;ISessionManager? manager=null;ISessionEnumerator? sessions=null;
  try
  {
   Guid clsid=new("BCDE0395-E52F-467C-8E3D-C4579291692E"),iid=typeof(IMMDeviceEnumerator).GUID;
   Marshal.ThrowExceptionForHR(CoCreateInstance(ref clsid,0,1,ref iid,out nint native));enumerator=Take<IMMDeviceEnumerator>(native);
   if (outputId.Length == 0) enumerator.GetDefaultAudioEndpoint(0,1,out native); else enumerator.GetDevice(outputId,out native);
   device=Take<IMMDevice>(native);
   iid=typeof(ISessionManager).GUID;device.Activate(ref iid,23,0,out native);manager=Take<ISessionManager>(native);
   manager.GetSessionEnumerator(out native);sessions=Take<ISessionEnumerator>(native);sessions.GetCount(out int count);
   var snapshot=Processes();var candidates=new HashSet<int>();
   for(int i=0;i<count;i++){sessions.GetSession(i,out native);var session=Take<ISessionControl2>(native);try{session.GetState(out int state);session.GetProcessId(out uint pid);if(pid!=0&&pid!=(uint)Environment.ProcessId&&state!=2)candidates.Add((int)pid);}finally{Release(session);}}
   int[] excluded=snapshot.Excluded(exclusions);return new(snapshot.AudioTrees(candidates,exclusions,excluded),excluded,snapshot.Parents,snapshot.Names);
  }
  finally{Release(sessions);Release(manager);Release(device);Release(enumerator);Api.CoUninitialize();}
 }
 static unsafe T Take<T>(nint pointer)where T:class
 {if(pointer==0)throw new COMException("Audio service returned no interface.");try{return UniqueComInterfaceMarshaller<T>.ConvertToManaged((void*)pointer)!;}finally{Marshal.Release(pointer);}}
 static void Release(object? value){if(value is ComObject com)com.FinalRelease();}
 public static unsafe ProcessSnapshot Processes()
 {
  nint handle=CreateToolhelp32Snapshot(2,0);if(handle==-1)throw new System.ComponentModel.Win32Exception();
  try
  {
   Entry entry=default;entry.Size=(uint)sizeof(Entry);var parents=new Dictionary<int,int>();var names=new Dictionary<int,string>();
   if(Process32FirstW(handle,ref entry)==0)throw new System.ComponentModel.Win32Exception();
   do{parents[(int)entry.Pid]=(int)entry.Parent;names[(int)entry.Pid]=new string((char*)entry.File);}while(Process32NextW(handle,ref entry)!=0);
   int error=Marshal.GetLastWin32Error();if(error!=18)throw new System.ComponentModel.Win32Exception(error);return new(parents,names);
  }
  finally{CloseHandle(handle);}
 }
 [StructLayout(LayoutKind.Sequential)]unsafe struct Entry{public uint Size,Usage,Pid;public nuint Heap;public uint Module,Threads,Parent;public int Priority;public uint Flags;public fixed ushort File[260];}
 [LibraryImport("ole32.dll")]private static partial int CoCreateInstance(ref Guid clsid,nint outer,uint context,ref Guid iid,out nint value);
 [DllImport("kernel32.dll",SetLastError=true)]static extern nint CreateToolhelp32Snapshot(uint flags,uint pid);
 [LibraryImport("kernel32.dll",SetLastError=true)]private static partial int Process32FirstW(nint snapshot,ref Entry entry);
 [LibraryImport("kernel32.dll",SetLastError=true)]private static partial int Process32NextW(nint snapshot,ref Entry entry);
 [DllImport("kernel32.dll")][return:MarshalAs(UnmanagedType.Bool)]static extern bool CloseHandle(nint handle);
}
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16),Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]partial interface IMMDeviceEnumerator
{void EnumAudioEndpoints(int flow,uint mask,out nint devices);void GetDefaultAudioEndpoint(int flow,int role,out nint device);void GetDevice(string id,out nint device);}
[GeneratedComInterface,Guid("D666063F-1587-4E43-81F1-B948E807363F")]partial interface IMMDevice{void Activate(ref Guid iid,uint context,nint parameters,out nint result);}
[GeneratedComInterface,Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]partial interface ISessionManager
{void GetAudioSessionControl(nint guid,uint flags,out nint control);void GetSimpleAudioVolume(nint guid,uint flags,out nint volume);void GetSessionEnumerator(out nint enumerator);}
[GeneratedComInterface,Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]partial interface ISessionEnumerator{void GetCount(out int count);void GetSession(int index,out nint control);}
[GeneratedComInterface,Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]partial interface ISessionControl2
{void GetState(out int state);void GetDisplayName(out nint value);void SetDisplayName(nint value,nint context);void GetIconPath(out nint value);void SetIconPath(nint value,nint context);void GetGroupingParam(out Guid value);void SetGroupingParam(ref Guid value,nint context);void RegisterAudioSessionNotification(nint value);void UnregisterAudioSessionNotification(nint value);void GetSessionIdentifier(out nint value);void GetSessionInstanceIdentifier(out nint value);void GetProcessId(out uint pid);}

