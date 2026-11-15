using System.Diagnostics;
using ZenithClipper;
using ZenithClipper.Native;

try
{
    using var own=Process.GetCurrentProcess();own.PriorityClass=ProcessPriorityClass.BelowNormal;
    Directory.CreateDirectory(AppPaths.Root);
    using var instance=new FileStream(Path.Combine(AppPaths.Root,"engine.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
    NativeSpoolCleanup.RemoveAbandoned(AppPaths.Buffer);
    var settings=AppPaths.Load();if(!File.Exists(AppPaths.Config))AppPaths.Save(settings);
    await using var recorder=new NativeRecorder(settings);
    if(args.FirstOrDefault()=="--self-test")
    {
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var clock=Stopwatch.StartNew();await recorder.StartAsync(deadline.Token);
        for(int i=0;i<6;i++){await Task.Delay(1000,deadline.Token);await recorder.TickAsync(deadline.Token);own.Refresh();Console.WriteLine($"sample={i} state={recorder.Status().State} workingMiB={own.WorkingSet64/1048576.0:F2} peakMiB={own.PeakWorkingSet64/1048576.0:F2} privateMiB={own.PrivateMemorySize64/1048576.0:F2} buffered={recorder.Status().BufferedSeconds:F3}");}
        string clip=await recorder.SaveAsync(2,deadline.Token);await recorder.StopAsync();Console.WriteLine("PASS native full capture/audio/save: "+clip);return 0;
    }
    using var shutdown=new CancellationTokenSource();Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;shutdown.Cancel();};
    void Log(string message){try{string path=Path.Combine(AppPaths.Root,"engine.log");if(File.Exists(path)&&new FileInfo(path).Length>1024*1024)File.Move(path,path+".old",true);File.AppendAllText(path,$"{DateTimeOffset.Now:O} {message}\n");}catch(IOException){}}
    void Save()=>_ = Task.Run(async()=>{try{Log("Saved: "+await recorder.SaveAsync(ct:shutdown.Token));}catch(Exception ex){Log("Save failed: "+ex.Message);}});
    void Panel(){try{Process.Start(PanelLauncher.Create(AppContext.BaseDirectory,Environment.ProcessPath!,true));}catch(Exception ex){Log(ex.Message);}}
    GlobalHotkeys? hotkeys=new(settings,Save,Panel);if(hotkeys.Error!=null)Log(hotkeys.Error);
    var server=new ControlServer((IRecorderEngine)recorder,shutdown);server.SettingsChanged+=value=>{hotkeys.Dispose();hotkeys=new(value,Save,Panel);if(hotkeys.Error!=null)Log(hotkeys.Error);};
    var serverTask=server.RunAsync(shutdown.Token);Log($"Native engine started. Data={AppPaths.Root}; Pipe={Control.PipeName}");
    try
    {
        if(settings.AutoStartCapture)try{await recorder.StartAsync(shutdown.Token);}catch(Exception ex){Log("Capture failed: "+ex.Message);}
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(1));while(await timer.WaitForNextTickAsync(shutdown.Token)){if(serverTask.IsCompleted)await serverTask;await recorder.TickAsync(shutdown.Token);}
    }
    catch(OperationCanceledException)when(shutdown.IsCancellationRequested){}
    finally{shutdown.Cancel();hotkeys.Dispose();await serverTask;}
    return 0;
}
catch(Exception error){Console.Error.WriteLine(error);return 1;}

