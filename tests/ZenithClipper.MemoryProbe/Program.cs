using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

if(!OperatingSystem.IsWindows()||args.Length!=2)return 2;
using var process=Process.GetCurrentProcess();process.PriorityClass=ProcessPriorityClass.BelowNormal;
var assembly=Assembly.LoadFrom(Path.GetFullPath(args[1]));
var type=assembly.GetType("ZenithClipper.WindowsSessions",true)!;
var read=type.GetMethod("Read",BindingFlags.Public|BindingFlags.Static)!;
object? Query()=>read.Invoke(null,[new[]{"Spotify"}]);
_ = Query(); // Compare steady calls after initialization and JIT, not first-use costs.
var samples=new List<object>();long allocated=GC.GetTotalAllocatedBytes(true);double cpu=process.TotalProcessorTime.TotalSeconds;var timer=Stopwatch.StartNew();string included="",excluded="";
for(int i=0;i<5;i++)
{
    Thread.Sleep(150);var start=Stopwatch.GetTimestamp();var view=Query()!;double milliseconds=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    var viewType=view.GetType();included=string.Join(',',(int[])viewType.GetProperty("Included")!.GetValue(view)!);excluded=string.Join(',',(int[])viewType.GetProperty("Excluded")!.GetValue(view)!);
    process.Refresh();samples.Add(new {Milliseconds=milliseconds,ResidentMiB=process.WorkingSet64/1048576.0,PrivateCommitMiB=process.PrivateMemorySize64/1048576.0});
}
process.Refresh();var result=new{Label=args[0],Assembly=Path.GetFullPath(args[1]),Calls=5,AllocatedBytesPerScan=(GC.GetTotalAllocatedBytes(true)-allocated)/5,ProcessCpuMilliseconds=(process.TotalProcessorTime.TotalSeconds-cpu)*1000,ElapsedMilliseconds=timer.Elapsed.TotalMilliseconds,Included=included,Excluded=excluded,Samples=samples};
Console.WriteLine(JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
return 0;
