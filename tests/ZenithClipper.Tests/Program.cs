using ZenithClipper;

if(args.FirstOrDefault()=="-hide_banner") { await Task.Delay(TimeSpan.FromSeconds(20)); return 0; }
var tests = new (string, Action)[] {
    ("duration supports seconds and hours", () => { new Settings { ClipSeconds = 1, BufferSeconds = 3600 }.Validate(); }),
    ("duration rejects zero", () => Throws(() => new Settings { ClipSeconds = 0 }.Validate())),
    ("clip cannot exceed retention", () => Throws(() => new Settings { ClipSeconds = 61, BufferSeconds = 60 }.Validate())),
    ("disk cap rejects unbounded buffer", () => Throws(() => new Settings { MaxBufferMiB = 0 }.Validate())),
    ("select newest footage", () => {
        var items = Enumerable.Range(0, 5).Select(i => new Segment($"{i}.ts", i * 2, (i+1)*2, 100)).ToArray();
        var picked = ReplayIndex.Select(items, 3); Equal("3.ts,4.ts", string.Join(',', picked.Select(x => x.Path)));
    }),
    ("retention bounded by bytes", () => {
        var items = Enumerable.Range(0, 5).Select(i => new Segment($"{i}.ts", i*2,(i+1)*2,100)).ToArray();
        Equal(3, ReplayIndex.Expired(items, 60, 250).Count);
    }),
    ("empty buffer cannot save", () => Throws(() => ReplayIndex.Select([], 3))),
    ("audio match is case insensitive exact name", () => { Equal(true, AudioPolicy.Excluded("Spotify.exe", ["spotify"])); Equal(false, AudioPolicy.Excluded("spotify-helper", ["spotify"])); }),
    ("snapshot exclusion finds inactive apps by exact executable name",()=>{var s=new ProcessSnapshot(new(),new(){{1,"Spotify.exe"},{2,"SPOTIFY"},{3,"spotify-helper.exe"},{4,"other.exe"}});Equal("1,2",string.Join(',',s.Excluded(["spotify"])));}),
    ("snapshot audio eligibility rejects unknown session owners",()=>{var s=new ProcessSnapshot(new(),new(){{1,"game.exe"}});Equal("1",string.Join(',',s.AudioTrees([1,99],["spotify"])));}),
    ("snapshot audio eligibility excludes descendants of named apps",()=>{var s=new ProcessSnapshot(new(){{2,1},{3,2}},new(){{1,"Spotify.exe"},{2,"helper.exe"},{3,"audio.exe"},{4,"game.exe"}});Equal("4",string.Join(',',s.AudioTrees([3,4],["spotify"])));}),
    ("snapshot audio eligibility protects excluded children without sessions",()=>{var s=new ProcessSnapshot(new(){{2,1},{3,1}},new(){{1,"host.exe"},{2,"Spotify.exe"},{3,"game.exe"}});Equal("3",string.Join(',',s.AudioTrees([1],["spotify"])));}),
    ("audio executable matching allocates no object per scanned process",()=>{string[] rules=["spotify","discord.exe"];for(int i=0;i<32;i++)_ = AudioPolicy.Excluded("Game.exe",rules);long start=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++){_ = AudioPolicy.Excluded("Spotify.EXE",rules);_ = AudioPolicy.Excluded("Game.exe",rules);}long bytes=GC.GetAllocatedBytesForCurrentThread()-start;if(bytes>4096)throw new Exception($"Process-name matching allocated {bytes} bytes for 2000 checks.");}),
    ("audio packet buffers avoid a one-second allocation per app",()=>{var p=new AudioPacketBuffer();Equal(true,p.ForPacket(3840).Length<=8192);Equal(true,ReferenceEquals(p.ForPacket(3840),p.ForPacket(1920)));}),
    ("audio packet buffers grow for stalls but stay bounded",()=>{var p=new AudioPacketBuffer();var data=p.ForPacket(48000);Equal(true,data.Length>=48000&&data.Length<=192000);Equal(true,ReferenceEquals(data,p.ForPacket(3840)));Equal(192000,p.ForPacket(192000).Length);try{p.ForPacket(192004);throw new Exception("Oversized audio packet accepted.");}catch(IOException){}}),
    ("native AMD source bypasses video demux and decoder queues",()=>{var a=CaptureCommand.BuildNativeAmfGraph(new Settings{Encoder="amf",Fps=165},"buffer",[]);Equal(false,a.Contains("-i"));int graph=a.IndexOf("-filter_complex");Equal(true,graph>=0&&a[graph+1].Contains("vsrc_amf=monitor_index=0:framerate=165"));Equal(true,a.Contains("[clipvideo]"));}),
    ("native source maps the first audio input without a phantom video input",()=>{var a=CaptureCommand.BuildNativeAmfGraph(new Settings{Encoder="amf",Fps=165},"buffer",["audio-pipe"]);Equal(true,a.Contains("0:a:0"));Equal(false,a.Contains("1:a:0"));Equal(1,a.Count(x=>x=="-i"));}),
    ("native source and multiple audio inputs share one complex graph",()=>{var a=CaptureCommand.BuildNativeAmfGraph(new Settings{Encoder="amf",Fps=60},"buffer",["one","two"]);Equal(1,a.Count(x=>x=="-filter_complex"));string g=a[a.IndexOf("-filter_complex")+1];Equal(true,g.Contains("[0:a][1:a]amix=inputs=2"));Equal(true,g.Contains("[clipvideo]"));}),
    ("direct AMF graph polls output with one in-flight frame",()=>{var a=CaptureCommand.BuildNativeAmfGraph(new Settings{Encoder="amf",Fps=165},"buffer",[]);Equal("1",a[a.IndexOf("-async_depth")+1]);Equal("+low_delay",a[a.IndexOf("-flags:v")+1]);Equal("20",a[a.IndexOf("-qp_p")+1]);}),
    ("native daemon is selected directly when its packaged engine is eligible",()=>{string dir=Path.Combine(Path.GetTempPath(),"zc-native-launch-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);try{string native=Path.Combine(dir,"ZenithClipper.Recorder.exe");File.WriteAllText(native,"");var p=DaemonLauncher.Create(dir,Path.Combine(dir,"zenithclipper.exe"),true);Equal(native,p.FileName);Equal("daemon",p.ArgumentList[0]);Equal(false,p.UseShellExecute);Equal(true,p.CreateNoWindow);}finally{Directory.Delete(dir,true);}}),
    ("daemon launcher keeps the legacy engine when native is unavailable",()=>{var p=DaemonLauncher.Create(Path.GetTempPath(),"/usr/bin/dotnet",false);Equal("/usr/bin/dotnet",p.FileName);Equal(Path.Combine(Path.GetTempPath(),"zenithclipper.dll"),p.ArgumentList[0]);Equal("daemon",p.ArgumentList[1]);}),
    ("hardware is default", () => Equal("auto", new Settings().Encoder)),
    ("windows capture uses GPU desktop duplication", () => {
        var args = CaptureCommand.Build(new Settings { Encoder = "nvenc", Fps = 60 }, "windows", "buffer", null);
        Equal(true, args.Contains("ddagrab=output_idx=0:framerate=60:draw_mouse=1"));
        Equal(true, args.Contains("h264_nvenc")); Equal(false, args.Contains("libx264"));
    }),
    ("editor prevents overwriting input", () => Throws(() => new EditRequest("clip.mp4", "clip.mp4").Validate())),
    ("editor rejects invalid crop", () => Throws(() => new EditRequest("a.mp4", "b.mp4") { CropWidth = 5 }.Validate())),
    ("requested clip and menu defaults",()=>{var settings=new Settings();Equal("Alt+C",settings.ClipHotkey);Equal("Shift+~",settings.PanelHotkey);}),
    ("tilde shortcut maps its physical key on Windows and X11",()=>{var key=HotkeySpec.Parse("Shift+~");Equal(0xC0u,key.Key);Equal(0x4004u,key.Modifiers);Equal("grave",key.X11Symbol);Equal(key,HotkeySpec.Parse("Shift+`"));}),
    ("equivalent shortcut spellings cannot collide",()=>Throws(()=>(new Settings{ClipHotkey="Shift+`",PanelHotkey="Shift+~"}).Validate())),
    ("registered shortcut routes only to a listening focused picker",()=>{
        int posted=0; nint destination=0; int forwarded=0; nint data=0; nint payload=(0x43<<16)|1;
        void Post(nint target,int message,nint value){posted++;destination=target;forwarded=message;data=value;}
        Equal(true,WindowsHotkeyCapture.TryRouteRegisteredMessage(0x312,payload,123,true,Post));
        Equal(1,posted);Equal((nint)123,destination);Equal(WindowsHotkeyCapture.ForwardMessage,forwarded);Equal(payload,data);
        Equal(true,WindowsHotkeyCapture.TryDecodeForwardedMessage(forwarded,data,out var binding));Equal("Alt+C",binding);
        Equal(false,WindowsHotkeyCapture.TryRouteRegisteredMessage(0x312,payload,123,false,Post));
        Equal(false,WindowsHotkeyCapture.TryRouteRegisteredMessage(0x312,payload,0,true,Post));
        Equal(false,WindowsHotkeyCapture.TryRouteRegisteredMessage(0x100,payload,123,true,Post));Equal(1,posted);
    }),
    ("forwarded binding decodes tilde and ignores unrelated messages",()=>{
        Equal(true,WindowsHotkeyCapture.TryDecodeForwardedMessage(WindowsHotkeyCapture.ForwardMessage,(0xC0<<16)|4,out var tilde));Equal("Shift+~",tilde);
        Equal(true,WindowsHotkeyCapture.TryDecodeForwardedMessage(WindowsHotkeyCapture.ForwardMessage,(0x4B<<16)|8,out var windows));Equal("Win+K",windows);
        Equal(false,WindowsHotkeyCapture.TryDecodeForwardedMessage(0x312,(0x43<<16)|1,out _));
        Equal(false,WindowsHotkeyCapture.TryDecodeForwardedMessage(WindowsHotkeyCapture.ForwardMessage,0,out _));
    }),
    ("Windows panel shortcut bypasses the intermediate engine process",()=>{
        string folder=Path.Combine(Path.GetTempPath(),"zc-panel-launch-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try{string ui=Path.Combine(folder,"ZenithClipper.Windows.exe");File.WriteAllText(ui,"");var info=PanelLauncher.Create(folder,Path.Combine(folder,"zenithclipper.exe"),true);Equal(ui,info.FileName);Equal("panel",string.Join(',',info.ArgumentList));Equal(false,info.UseShellExecute);Equal(true,info.CreateNoWindow);}finally{Directory.Delete(folder,true);}
    }),
    ("panel launcher keeps the managed-host fallback on Linux",()=>{var info=PanelLauncher.Create(Path.GetTempPath(),"/usr/bin/dotnet",false);Equal("/usr/bin/dotnet",info.FileName);Equal(Path.Combine(Path.GetTempPath(),"zenithclipper.dll"),info.ArgumentList[0]);Equal("panel",info.ArgumentList[1]);}),
    ("AMF compatibility retries only known native capture failures",()=>{Equal(true,EncoderSelection.NeedsAmfCompatibility("No such filter: 'vsrc_amf'"));Equal(true,EncoderSelection.NeedsAmfCompatibility("SubmitInput() failed with error 4"));Equal(false,EncoderSelection.NeedsAmfCompatibility("No such filter: other"));Equal(false,EncoderSelection.NeedsAmfCompatibility("Permission denied"));}),
    ("invalid hotkeys rejected", () => Throws(() => HotkeySpec.Parse("F8"))),
    ("linux uses configured X11 source", () => { var args = CaptureCommand.Build(new Settings { Encoder = "software", CaptureAudio = false, Fps = 60 }, "linux", "buffer", null); Equal(true, args.Contains("x11grab")); Equal(false, args.Contains("pulse")); }),
    ("IPC rejects unknown command", () => Throws(() => ControlRequest.Parse("{\"Command\":\"destroy\"}"))),
    ("IPC rejects oversized input", () => Throws(() => ControlRequest.Parse(new string('x', 70000)))),
    ("editing rejects NaN", () => Throws(() => new EditRequest("a.mp4", "b.mp4") { StartSeconds = double.NaN }.Validate())),
    ("PCM queue pads silence", () => { var ring = new PcmRing(); short[] samples = [9,9,9]; ring.Write([1,0,2,0], 4); ring.Read(samples); Equal("1,2,0", string.Join(',', samples)); }),
    ("PCM queue is bounded", () => { var ring = new PcmRing(); for (int i=0;i<100;i++) ring.Write(new byte[4800],4800); short[] samples = new short[24010]; ring.Read(samples); Equal((short)0,samples[^1]); }),
    ("excluded child cannot leak through allowed parent", () => Equal("3",string.Join(',', AudioPolicy.SafeTrees([1],new Dictionary<int,int>{{2,1},{3,1}},[2])))),
    ("unrelated exclusion keeps single capture tree", () => Equal("1",string.Join(',', AudioPolicy.SafeTrees([1,3],new Dictionary<int,int>{{2,1},{3,1}},[9])))),
    ("live catalog retains segments beyond metadata window", () => {
        string root=Path.Combine(Path.GetTempPath(),"zc-catalog-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try { File.WriteAllText(Path.Combine(root,"0.ts"),"a"); File.WriteAllText(Path.Combine(root,"segments.csv"),"0.ts,0,2\n"); var catalog=new ReplayCatalog(root); catalog.Poll(); File.WriteAllText(Path.Combine(root,"1.ts"),"b"); File.WriteAllText(Path.Combine(root,"segments.csv"),"1.ts,2,4\n"); catalog.Poll(); Equal(2,catalog.Snapshot().Count); catalog.Prune(2,100); Equal(1,catalog.Snapshot().Count); Equal(false,File.Exists(Path.Combine(root,"0.ts"))); }
        finally { Directory.Delete(root,true); }
    }),
    ("Windows pipe identity ignores path casing",()=>{
        if(!OperatingSystem.IsWindows()) return;string? old=Environment.GetEnvironmentVariable("ZENITHCLIPPER_HOME");
        try {Environment.SetEnvironmentVariable("ZENITHCLIPPER_HOME",@"C:\Temp\ZenithClipper");string first=Control.PipeName;Environment.SetEnvironmentVariable("ZENITHCLIPPER_HOME",@"c:\temp\zenithclipper");Equal(first,Control.PipeName);}finally{Environment.SetEnvironmentVariable("ZENITHCLIPPER_HOME",old);}
    }),
    ("auto encoder favors the known display adapter",()=>{Equal("amf,nvenc,qsv",string.Join(',',EncoderSelection.WindowsCandidates("amf")));Equal("qsv,nvenc,amf",string.Join(',',EncoderSelection.WindowsCandidates("qsv")));Equal("nvenc,qsv,amf",string.Join(',',EncoderSelection.WindowsCandidates(null)));}),
    ("adapter identification never assumes unknown means AMD",()=>{Equal("amf",EncoderSelection.FromDeviceId("PCI\\VEN_1002&DEV_15BF"));Equal("nvenc",EncoderSelection.FromDeviceId("PCI\\VEN_10DE&DEV_28E0"));Equal("qsv",EncoderSelection.FromDeviceId("PCI\\VEN_8086&DEV_1234"));Equal<string?>(null,EncoderSelection.FromDeviceId("unknown"));}),
    ("AMD encoder keeps its frame queue small",()=>{var args=CaptureCommand.EncoderArgs(new Settings{Encoder="amf"}).ToArray();int index=Array.IndexOf(args,"-async_depth");Equal(true,index>=0 && args[index+1]=="2");}),
    ("AMD capture stays on its native GPU path",()=>{var args=CaptureCommand.Build(new Settings{Encoder="amf",Fps=60},"windows","buffer",null);Equal(true,args.Any(x=>x.Contains("vsrc_amf=monitor_index=0:framerate=60")));Equal(false,args.Any(x=>x.Contains("hwdownload")));}),
    ("native AMD frames avoid the converter that fails at 165 Hz",()=>{var args=CaptureCommand.Build(new Settings{Encoder="amf",Fps=165},"windows","buffer",null);Equal(false,args.Any(x=>x.Contains("vpp_amf")));Equal(true,args.Any(x=>x.Contains("settb=1/165,setpts=N")));}),
    ("NVENC compatibility capture transfers frames across adapters",()=>{var command=CaptureCommand.Build(new Settings{Encoder="nvenc",WindowsCaptureBackend="desktop-duplication"},"windows","buffer",null);Equal(true,command.Any(x=>x.Contains("hwdownload")));Equal(true,command.Any(x=>x.Contains("hwupload")));Equal(true,command.Contains("d3d11va=encoder:,vendor_id=0x10de"));}),
    ("new excluded descendant invalidates old captured parent",()=>Equal(true,AudioPolicy.RootsContainExcluded([1],new Dictionary<int,int>{{2,1},{3,2}},[3]))),
    ("unrelated excluded launch does not reset buffer",()=>Equal(false,AudioPolicy.RootsContainExcluded([1],new Dictionary<int,int>{{3,2}},[3]))),
    ("unfinished CSV entry is not treated as a closed segment",()=>{
        string root=Path.Combine(Path.GetTempPath(),"zc-partial-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{File.WriteAllText(Path.Combine(root,"0.ts"),"a");File.WriteAllText(Path.Combine(root,"1.ts"),"b");File.WriteAllText(Path.Combine(root,"segments.csv"),"0.ts,0,2\n1.ts,2,4");Equal(1,ReplayIndex.Read(root).Count);}finally{Directory.Delete(root,true);}
    }),
    ("live TS snapshot stops before the last unfinished video frame",()=>{
        byte[] packets=new byte[188*5];for(int i=0;i<5;i++){packets[i*188]=0x47;packets[i*188+3]=0x10;}
        foreach(int index in new[]{1,3}){int offset=index*188;packets[offset+1]=0x40;packets[offset+4]=0;packets[offset+5]=0;packets[offset+6]=1;packets[offset+7]=0xe0;}
        using var input=new MemoryStream(packets);Equal(188L*3,TsSnapshot.SafeLength(input,packets.Length));
    }),
    ("clip length and shortcuts change without resetting the buffer",()=>{var a=new Settings();Equal(false,SettingsChanges.RequiresRestart(a,a with{ClipSeconds=60,BufferSeconds=600,MaxBufferMiB=4096,ClipHotkey="Ctrl+Alt+K",PanelHotkey="Ctrl+Shift+M",ClipDirectory="x",ExcludedApps=["spotify"]}));}),
    ("Linux playback defaults to following the system and persists a pinned device",()=>{var a=new Settings();Equal("",a.LinuxPlaybackDevice);var b=a with{LinuxPlaybackDevice="bluez_output.test",ClipSaveSound=false};Equal(false,SettingsChanges.RequiresRestart(a,b));Equal(b.LinuxPlaybackDevice,System.Text.Json.JsonSerializer.Deserialize(System.Text.Json.JsonSerializer.Serialize(b,CoreJsonContext.Default.Settings),CoreJsonContext.Default.Settings)!.LinuxPlaybackDevice);}),
    ("clip save sound defaults to Amber and persists without restarting capture", () =>
    {
        var defaults = System.Text.Json.JsonSerializer.Deserialize("{}", CoreJsonContext.Default.Settings)!;
        Equal(true, defaults.ClipSaveSound);
        Equal("01-amber", defaults.ClipSaveSoundId);
        Equal(10, ClipSaveSound.SoundIds.Count);
        var selected = defaults with { ClipSaveSoundId = "10-pebble" };
        var json = System.Text.Json.JsonSerializer.Serialize(selected, CoreJsonContext.Default.Settings);
        Equal("10-pebble", System.Text.Json.JsonSerializer.Deserialize(json, CoreJsonContext.Default.Settings)!.ClipSaveSoundId);
        Equal("01-amber", ClipSaveSound.NormalizeId("../unknown"));
        Equal(false, SettingsChanges.RequiresRestart(defaults, selected));
    }),
    ("Linux playback follows device changes without replacing the capture sink", () =>
    {
        var output = "speakers";
        var commands = new List<string[]>();
        var moduleId = 0;
        var captureSink = "zenithclipper_" + Environment.ProcessId;
        Task<string> Pactl(CancellationToken _, string[] args)
        {
            commands.Add(args);
            return Task.FromResult(args[0] switch
            {
                "get-default-sink" => output,
                "load-module" => (++moduleId).ToString(),
                "-f" when args[^1] == "sinks" =>
                    $$"""[{"name":"{{captureSink}}","description":"ZenithClipperCapture","index":3}]""",
                "-f" => "[]",
                "unload-module" => "",
                _ => throw new InvalidOperationException(string.Join(" ", args))
            });
        }

        async Task ExerciseAsync()
        {
            await using var audio = new LinuxAudio(new Settings(), Pactl);
            await audio.StartAsync();
            var source = audio.Source;
            Equal("speakers", audio.PlaybackDevice);
            output = "bluez_headphones";
            await audio.RefreshAsync();
            Equal("bluez_headphones", audio.PlaybackDevice);
            output = "speakers";
            await audio.RefreshAsync();
            Equal("speakers", audio.PlaybackDevice);
            Equal(source, audio.Source);
            Equal(1, commands.Count(args => args.Length > 1 && args[1] == "module-null-sink"));
            Equal("sink=speakers,sink=bluez_headphones,sink=speakers", string.Join(",", commands
                .Where(args => args.Length > 1 && args[1] == "module-loopback").Select(args => args[3])));
        }
        ExerciseAsync().GetAwaiter().GetResult();
    }),
    ("Windows microphone and output choices persist and require a fresh capture", () =>
    {
        var defaults = System.Text.Json.JsonSerializer.Deserialize("{}", CoreJsonContext.Default.Settings)!;
        Equal(false, defaults.CaptureMicrophone);
        Equal("", defaults.WindowsInputDevice);
        Equal("", defaults.WindowsOutputDevice);
        var configured = defaults with { CaptureMicrophone = true, WindowsInputDevice = "{mic}", WindowsOutputDevice = "{speakers}" };
        var json = System.Text.Json.JsonSerializer.Serialize(configured, CoreJsonContext.Default.Settings);
        var restored = System.Text.Json.JsonSerializer.Deserialize(json, CoreJsonContext.Default.Settings)!;
        Equal(true, restored.CaptureMicrophone);
        Equal(configured.WindowsInputDevice, restored.WindowsInputDevice);
        Equal(configured.WindowsOutputDevice, restored.WindowsOutputDevice);
        Equal(true, SettingsChanges.RequiresRestart(defaults, restored));
    }),
    ("capture and audio changes still restart capture",()=>{var a=new Settings();Equal(true,SettingsChanges.RequiresRestart(a,a with{Fps=30}));Equal(true,SettingsChanges.RequiresRestart(a,a with{ExcludedApps=["Spotify","Discord"]}));Equal(true,SettingsChanges.RequiresRestart(a,a with{Encoder="software"}));}),
    ("audio startup discards samples queued before the video connection",()=>{var ring=new PcmRing();ring.Write([1,0,2,0],4);ring.Clear();short[] output=[9,9];ring.Read(output);Equal("0,0",string.Join(',',output));}),
};
int failed = 0;
foreach (var (name, test) in tests) { try { test(); Console.WriteLine($"PASS {name}"); } catch (Exception ex) { failed++; Console.WriteLine($"FAIL {name}: {ex.Message}"); } }
Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed");
if(failed==0&&args.Length>1&&args[1]=="--graph-fixture")return await GraphFixtures.RunAsync(args[0]);
if (failed == 0 && args.Length > 1 && args[1] is "--recovery" or "--native-recovery" && OperatingSystem.IsWindows()) return await WindowsRecovery.RunAsync(args[0],args[1]=="--native-recovery");
if (failed == 0 && args.Length > 0) failed += await Integration.RunAsync(args[0]);
if (failed == 0 && args.Length > 1 && args[1] == "--live" && OperatingSystem.IsWindows()) failed += await WindowsLive.RunAsync(args[0]);
return failed == 0 ? 0 : 1;
static void Equal<T>(T expected, T actual) { if (!Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}"); }
static void Throws(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected validation failure"); }
