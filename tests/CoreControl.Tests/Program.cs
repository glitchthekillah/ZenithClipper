using System.Text.Json;
using ZenithClipper;

void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
var request=ControlRequest.Parse("{\"cOmMaNd\":\"configure\",\"sEtTiNgS\":{\"qUaLiTy\":20,\"fPs\":165}}");
Assert(request.Settings?.ClipSeconds==30 && request.Settings.BufferSeconds==120 && request.Settings.Quality==20 && request.Settings.ExcludedApps.SequenceEqual(["Spotify"]), "Missing init-only Settings defaults changed.");
Assert(request.Settings?.Fps==165,"Case-insensitive source-generated request read failed.");
string wire=JsonSerializer.Serialize(new ControlResponse(true,Settings:request.Settings),CoreJsonContext.Default.ControlResponse);
Assert(!wire.Contains('\n') && wire.Contains("\"Ok\":true"),"Wire JSON must be compact with existing PascalCase names.");
Assert(JsonSerializer.Deserialize(wire,CoreJsonContext.Default.ControlResponse)?.Settings?.Quality==20,"Response roundtrip failed.");
var edit=(EditRequest?)JsonSerializer.Deserialize("{\"iNpUt\":\"a.mp4\",\"oUtPuT\":\"b.mp4\",\"sPeEd\":2}",AppPaths.Json.GetTypeInfo(typeof(EditRequest)));
Assert(edit?.FastTrim==true && edit.Quality==20 && edit.Volume==1 && edit.Encoder=="auto", "Missing init-only EditRequest defaults changed.");
Assert(edit?.Speed==2,"AppPaths.Json edit contract missing.");
try { ControlRequest.Parse("{\"Command\":\"configure\",\"Settings\":{\"Fps\":\"bad\"}}"); throw new Exception("Invalid JSON setting type accepted."); } catch(ArgumentException) { }
string? old=Environment.GetEnvironmentVariable("ZENITHCLIPPER_HOME");
string root=Path.Combine(Path.GetTempPath(),"zc-core-aot-"+Guid.NewGuid().ToString("N"));
try{
    Environment.SetEnvironmentVariable("ZENITHCLIPPER_HOME",root);
    AppPaths.Save(new Settings { Fps=165,ClipDirectory=Path.Combine(root,"clips") });
    Assert(File.ReadAllText(AppPaths.Config).Contains('\n'),"Persisted settings indentation changed.");
    Assert(AppPaths.Load().Fps==165,"Settings source-generated persistence failed.");
    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
    using var shutdown=new CancellationTokenSource();
    using var stop=CancellationTokenSource.CreateLinkedTokenSource(timeout.Token,shutdown.Token);
    IRecorderEngine recorder=new FakeEngine();
    var server=new ControlServer(recorder,shutdown);var running=server.RunAsync(stop.Token);
    async Task<ControlResponse> Send(ControlRequest value){var response=await Control.SendAsync(value,timeout.Token);Assert(response.Ok,response.Error??"IPC failed");return response;}
    Assert((await Send(new("status"))).Status?.State=="Paused","Interface status IPC failed.");
    Assert((await Send(new("settings"))).Settings?.Quality==20,"Interface settings IPC failed.");
    await Send(new("configure",Settings:new Settings{Fps=165}));
    await Send(new("resume"));Assert(recorder.Status().Capturing,"Interface resume failed.");
    Assert((await Send(new("save",1))).Path=="saved.mp4","Interface save failed.");
    await Send(new("pause"));Assert(!recorder.Status().Capturing,"Interface pause failed.");
    await Send(new("shutdown"));await running.WaitAsync(timeout.Token);await recorder.DisposeAsync();
    Console.WriteLine("PASS source-generated JSON/casing/compact wire/pretty settings/EditRequest/interface IPC configure-resume-save-pause-shutdown");
    Console.WriteLine($"reflectionDefault={JsonSerializer.IsReflectionEnabledByDefault}");
}finally{Environment.SetEnvironmentVariable("ZENITHCLIPPER_HOME",old);if(Directory.Exists(root))Directory.Delete(root,true);}

internal sealed class FakeEngine : IRecorderEngine
{
    public Settings Settings{get;private set;}=new();private bool active;
    public RecorderStatus Status()=>new(active,active?"Capturing":"Paused",null,"fake",0,0,null,Environment.ProcessId);
    public Task StartAsync(CancellationToken ct=default){active=true;return Task.CompletedTask;}
    public Task StopAsync(){active=false;return Task.CompletedTask;}
    public Task TickAsync(CancellationToken ct)=>Task.CompletedTask;
    public Task<string> SaveAsync(int? seconds=null,CancellationToken ct=default)=>Task.FromResult("saved.mp4");
    public Task UpdateAsync(Settings value,CancellationToken ct){Settings=value;return Task.CompletedTask;}
    public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
}
