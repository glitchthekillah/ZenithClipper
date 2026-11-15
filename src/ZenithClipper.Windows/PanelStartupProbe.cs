using System.Diagnostics;
using System.Text.Json;
namespace ZenithClipper.Windows;

// Explicit, short-lived offscreen diagnostic. It never connects to or starts the recorder.
internal static class PanelStartupProbe
{
    static readonly Dictionary<string,double> stages = [];
    static readonly Dictionary<string,DateTime> stageUtc = [];
    static string? output;
    static readonly long started = Stopwatch.GetTimestamp();
    public static bool Enabled => output is not null;
    public static bool EagerBaseline {get;private set;}
    public static void Configure(string path,bool eagerBaseline=false) { output=Path.GetFullPath(path); EagerBaseline=eagerBaseline; }
    public static void Mark(string name, bool once=false)
    {
        if (!Enabled || once && stages.ContainsKey(name)) return;
        stages[name] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        stageUtc[name] = DateTime.UtcNow;
    }
    public static void Run()
    {
        using var process = Process.GetCurrentProcess();
        using var panel = new PanelForm();
        Application.Run(panel);
        Mark("message_loop_complete");
        var result = new { ProcessId=Environment.ProcessId, ProcessStartUtc=process.StartTime.ToUniversalTime(), LaunchToMainMs=(stageUtc["main"]-process.StartTime.ToUniversalTime()).TotalMilliseconds, StagesMs=stages, StageUtc=stageUtc, EagerBaseline, NoRecorderConnection=true, Offscreen=true };
        Directory.CreateDirectory(Path.GetDirectoryName(output!)!);
        File.WriteAllText(output!, JsonSerializer.Serialize(result, new JsonSerializerOptions{WriteIndented=true}));
    }
}
