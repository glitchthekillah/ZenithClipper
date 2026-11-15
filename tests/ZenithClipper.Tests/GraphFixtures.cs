using System.Text.Json;
using ZenithClipper;

static class GraphFixtures
{
    public static async Task<int> RunAsync(string ffmpeg)
    {
        string root=Path.Combine(Path.GetTempPath(),"ZenithClipper-graph-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(25));
            for(int count=0;count<=2;count++)
            {
                string output=Path.Combine(root,$"audio-{count}");Directory.CreateDirectory(output);
                var audio=new List<string>();
                for(int channel=0;channel<count;channel++)
                {
                    string path=Path.Combine(output,$"input-{channel}.pcm");
                    using(var writer=new BinaryWriter(File.Create(path)))
                        for(int i=0;i<48000*4;i++){short value=(short)(1600*Math.Sin(2*Math.PI*(440+440*channel)*i/48000));writer.Write(value);writer.Write(value);}
                    audio.Add(path);
                }
                var args=CaptureCommand.BuildNativeAmfGraph(new Settings{Encoder="amf",Fps=30},output,audio);
                // Exercise the actual graph/maps/segmenter with a tiny CPU source; never initialize capture or a GPU.
                for(int i=0;i<args.Count;i++)args[i]=args[i].Replace("vsrc_amf=monitor_index=0:framerate=30","testsrc2=size=96x64:rate=30");
                int start=args.IndexOf("-c:v"),end=args.IndexOf("-r");args.RemoveRange(start,end-start);
                args.InsertRange(start,["-threads:v","1","-c:v","libx264","-preset","ultrafast","-crf","20"]);
                args.InsertRange(args.Count-1,["-t","4"]);
                await MediaTools.RunAsync(ffmpeg,args,timeout.Token);
                var segments=ReplayIndex.Read(output);if(segments.Count<2)throw new Exception("Expected two finished synthetic graph segments.");
                foreach(var segment in segments)
                {
                    using var metadata=JsonDocument.Parse(await MediaTools.ProbeAsync(ffmpeg,segment.Path,timeout.Token));
                    var streams=metadata.RootElement.GetProperty("streams").EnumerateArray().ToArray();
                    var video=streams.Single(s=>s.GetProperty("codec_type").GetString()=="video");
                    if(video.GetProperty("width").GetInt32()!=96||video.GetProperty("height").GetInt32()!=64)throw new Exception("Synthetic graph dimensions changed.");
                    if(streams.Count(s=>s.GetProperty("codec_type").GetString()=="audio")!=(count>0?1:0))throw new Exception("Audio graph mapping failed.");
                    await MediaTools.RunAsync(ffmpeg,["-v","error","-xerror","-threads","1","-filter_threads","1","-i",segment.Path,"-f","null","-"],timeout.Token);
                }
                Console.WriteLine($"PASS offline direct graph: {count} audio inputs, finished segments and strict audio/video decode");
            }
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
        finally
        {
            string checkedRoot=Path.GetFullPath(root);string expected=Path.GetFullPath(Path.GetTempPath());
            if(!checkedRoot.StartsWith(Path.TrimEndingDirectorySeparator(expected)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(checkedRoot).StartsWith("ZenithClipper-graph-",StringComparison.Ordinal))throw new IOException("Unsafe graph fixture cleanup path.");
            Directory.Delete(checkedRoot,true);
        }
    }
}
