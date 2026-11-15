using System.Globalization;

namespace ZenithClipper;

public static class CaptureCommand
{
    public static List<string> Build(Settings s, string platform, string directory, string? audioSource, bool wayland = false) => BuildMulti(s, platform, directory, audioSource == null ? [] : [audioSource], wayland);
    public static List<string> BuildMulti(Settings s,string platform,string directory,IReadOnlyList<string> audioSources,bool wayland=false)
        => BuildCommand(s,platform,directory,audioSources,wayland,false);

    // Native source-to-encoder path: avoid intermediate video queues and keep packet polling low-delay.
    public static List<string> BuildNativeAmfGraph(Settings s,string directory,IReadOnlyList<string> audioSources)
    {
        if(s.Encoder!="amf"||s.WindowsCaptureBackend!="auto")throw new ArgumentException("The direct native graph requires AMF auto capture.");
        return BuildCommand(s,"windows",directory,audioSources,false,true);
    }
    static List<string> BuildCommand(Settings s,string platform,string directory,IReadOnlyList<string> audioSources,bool wayland,bool directSource)
    {
        s.Validate(); if (s.Encoder == "auto") throw new ArgumentException("Resolve hardware encoder before building capture command.");
        int fps = s.Fps > 0 ? s.Fps : 60;
        var args = new List<string> { "-hide_banner", "-loglevel", "warning", "-filter_threads", "1", "-filter_complex_threads", "1", "-y" };
        bool amfNative=platform=="windows" && s.Encoder=="amf" && s.WindowsCaptureBackend=="auto";
        bool amfGraph=amfNative&&directSource;
        if(amfNative&&!amfGraph)args.AddRange(["-f","lavfi","-i",$"vsrc_amf=monitor_index={s.Monitor}:framerate={fps}"]);
        else if (!amfGraph && platform == "windows") args.AddRange(["-f", "lavfi", "-i", $"ddagrab=output_idx={s.Monitor}:framerate={fps}:draw_mouse=1"]);
        else if (!amfGraph && wayland) args.AddRange(["-thread_queue_size", "64", "-f", "matroska", "-i", "pipe:0"]);
        else if (!amfGraph) args.AddRange(["-thread_queue_size", "64", "-f", "x11grab", "-framerate", $"{fps}", "-video_size", s.LinuxVideoSize, "-i", s.LinuxDisplay]);
        foreach (var audioSource in audioSources)
        {
            args.AddRange(["-thread_queue_size", "128"]);
            if (platform == "windows") args.AddRange(["-probesize", "32", "-analyzeduration", "0", "-f", "s16le", "-ar", "48000", "-ac", "2", "-channel_layout", "stereo", "-i", audioSource]);
            else args.AddRange(["-f", "pulse", "-i", audioSource]);
        }
        int audioIndex=amfGraph?0:1;
        var graphs=new List<string>();
        if(amfGraph)
        {
            // A source in the output graph feeds AMF directly, avoiding wrapped-frame demux/decoder queues.
            string scale=s.OutputWidth>0?$",vpp_amf=format=nv12:w={s.OutputWidth}:h=-2":"";
            graphs.Add($"vsrc_amf=monitor_index={s.Monitor}:framerate={fps}{scale},settb=1/{fps},setpts=N[clipvideo]");
        }
        if(audioSources.Count>1)
            graphs.Add(string.Concat(Enumerable.Range(audioIndex,audioSources.Count).Select(i=>$"[{i}:a]"))+$"amix=inputs={audioSources.Count}:duration=longest:normalize=0[a]");
        if(graphs.Count>0)args.AddRange(["-filter_complex",string.Join(';',graphs)]);
        args.AddRange(["-map",amfGraph?"[clipvideo]":"0:v:0"]);
        if(audioSources.Count>0)
        {
            args.AddRange(["-map",audioSources.Count>1?"[a]":$"{audioIndex}:a:0","-c:a","aac","-b:a","192k"]);
            if(audioSources.Count==1)args.AddRange(["-af","aresample=async=1:first_pts=0"]);
        }
        if (wayland) args.AddRange(["-c:v", "copy"]);
        else
        {
            bool gpuDirect = platform == "windows" && s.Encoder == "nvenc" && s.OutputWidth == 0 && s.WindowsCaptureBackend=="auto";
            var filters = new List<string>();
            if(amfNative&&!amfGraph&&s.OutputWidth>0)filters.Add($"vpp_amf=format=nv12:w={s.OutputWidth}:h=-2");
            if(!amfNative)
            {
                if(platform=="windows"&&!gpuDirect)filters.AddRange(["hwdownload","format=bgra"]);
                if(s.OutputWidth>0)filters.Add($"scale={s.OutputWidth}:-2");
            }
            if(platform=="windows" && s.Encoder=="nvenc" && !gpuDirect)
            {
                args.InsertRange(0,["-init_hw_device","d3d11va=encoder:,vendor_id=0x10de","-filter_hw_device","encoder"]);
                filters.Add("hwupload");
            }
            if(platform=="windows"&&!amfGraph) filters.Add($"settb=1/{fps},setpts=N");
            if (s.Encoder == "vaapi") filters.Add("format=nv12,hwupload");
            if (filters.Count > 0) args.AddRange(["-vf", string.Join(',', filters)]);
            if(amfGraph)args.AddRange(["-flags:v","+low_delay"]);
            args.AddRange(EncoderArgs(s,amfGraph));
            args.AddRange(["-r", $"{fps}", "-fps_mode", "cfr", "-g", $"{fps * 2}", "-bf", "0"]);
        }
        // Fixed GOP boundaries permit cheap remuxing. CSV contains only finished segments.
        args.AddRange(["-f", "segment", "-segment_time", "2", "-segment_list", Path.Combine(directory, "segments.csv"), "-segment_list_type", "csv", "-segment_list_size", "32", "-reset_timestamps", "1", Path.Combine(directory, "%09d.ts")]);
        if (s.Encoder == "vaapi" && !wayland) args.InsertRange(0, ["-vaapi_device", s.LinuxRenderNode]);
        return args;
    }
    public static IEnumerable<string> EncoderArgs(Settings s)=>EncoderArgs(s,false);
    static IEnumerable<string> EncoderArgs(Settings s,bool directGraph) => s.Encoder switch
    {
        "nvenc" => ["-c:v", "h264_nvenc", "-preset", "p1", "-tune", "ll", "-surfaces", "4", "-rc-lookahead", "0", "-delay", "0", "-rc", "vbr", "-cq", $"{s.Quality}", "-b:v", "0"],
        "qsv" => ["-c:v", "h264_qsv", "-preset", "veryfast", "-async_depth", "1", "-look_ahead", "0", "-global_quality", $"{s.Quality}"],
        "amf" => ["-c:v", "h264_amf", "-quality", "speed", "-async_depth", directGraph?"1":"2", "-vbaq", "0", "-rc", "cqp", "-qp_i", $"{s.Quality}", "-qp_p", $"{s.Quality}"],
        "vaapi" => ["-c:v", "h264_vaapi", "-qp", $"{s.Quality}"],
        "software" => ["-c:v", "libx264", "-preset", "ultrafast", "-crf", $"{s.Quality}", "-pix_fmt", "yuv420p"],
        _ => throw new ArgumentException("Unknown encoder")
    };
}
