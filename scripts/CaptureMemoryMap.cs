using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

public static class CaptureMemoryMap
{
 [StructLayout(LayoutKind.Sequential)] struct Region { public IntPtr Base,AllocationBase; public uint AllocationProtect; public ushort PartitionId; public UIntPtr Size; public uint State,Protect,Type; }
 [StructLayout(LayoutKind.Sequential)] struct Page { public IntPtr Address; public UIntPtr Flags; }
 public sealed class Entry { public string Name {get;set;}=""; public long ResidentBytes {get;set;} public long PrivateResidentBytes {get;set;} public long CommittedBytes {get;set;} }
 [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint rights,bool inherit,int processId);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
 [DllImport("kernel32.dll")] static extern UIntPtr VirtualQueryEx(IntPtr process,IntPtr address,out Region region,UIntPtr length);
 [DllImport("psapi.dll",SetLastError=true)] static extern bool QueryWorkingSetEx(IntPtr process,[In,Out] Page[] pages,int bytes);
 [DllImport("psapi.dll",CharSet=CharSet.Unicode)] static extern uint GetMappedFileName(IntPtr process,IntPtr address,StringBuilder name,int length);
 public static Entry[] Read(int processId)
 {
  var handle=OpenProcess(0x410,false,processId);if(handle==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
  try {
   var entries=new Dictionary<string,Entry>();var names=new Dictionary<long,string>();var timer=Stopwatch.StartNew();long address=0,scanned=0;int step=Environment.SystemPageSize;
   while(VirtualQueryEx(handle,new IntPtr(address),out var region,(UIntPtr)Marshal.SizeOf<Region>())!=UIntPtr.Zero){long size=checked((long)region.Size.ToUInt64());long next=checked(region.Base.ToInt64()+size);if(next<=address)break;address=next;
    if(timer.Elapsed.TotalSeconds>5)throw new TimeoutException("Memory map watchdog elapsed.");
    if(region.State!=0x1000 || (region.Protect&0x101)!=0)continue;
    scanned+=size;if(scanned>4L*1024*1024*1024)throw new InvalidOperationException("Memory scan exceeds 4 GiB diagnostic limit.");
    string name=region.Type==0x20000?"Private allocations":"Mapped memory";
    if(region.Type!=0x20000){long allocation=region.AllocationBase.ToInt64();if(!names.TryGetValue(allocation,out name)){var text=new StringBuilder(1024);name=GetMappedFileName(handle,region.Base,text,text.Capacity)>0?text.ToString():"Unnamed GPU/shared mapping";names[allocation]=name;}}
    if(!entries.TryGetValue(name,out var entry)){entry=new Entry{Name=name};entries[name]=entry;}entry.CommittedBytes+=size;
    for(long offset=0;offset<size;offset+=(long)step*4096){int count=(int)Math.Min(4096,(size-offset+step-1)/step);var pages=new Page[count];for(int i=0;i<count;i++)pages[i].Address=new IntPtr(region.Base.ToInt64()+offset+(long)i*step);
     if(!QueryWorkingSetEx(handle,pages,count*Marshal.SizeOf<Page>()))throw new System.ComponentModel.Win32Exception();
     foreach(var page in pages){ulong flags=page.Flags.ToUInt64();if((flags&1)!=0){entry.ResidentBytes+=step;if((flags&(1UL<<15))==0)entry.PrivateResidentBytes+=step;}}
    }
   }
   var result=new List<Entry>(entries.Values);result.Sort((a,b)=>b.ResidentBytes.CompareTo(a.ResidentBytes));return result.ToArray();
  }finally{CloseHandle(handle);}
 }
}
