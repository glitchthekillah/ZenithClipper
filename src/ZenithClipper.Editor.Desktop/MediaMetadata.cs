using System.Globalization;
using System.Text.Json;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;

internal static class MediaMetadata
{
    public static FrameRate ReadFrameRate(JsonElement video)
    {
        if(video.ValueKind!=JsonValueKind.Object)return FrameRate.Default;
        foreach(var property in new[]{"avg_frame_rate","r_frame_rate"})
        {
            if(!video.TryGetProperty(property,out var value))continue;var text=value.ValueKind==JsonValueKind.String?value.GetString():value.GetRawText();var parts=text?.Split('/');
            if(parts is not {Length:2}||!System.Numerics.BigInteger.TryParse(parts[0],out var numerator)||!System.Numerics.BigInteger.TryParse(parts[1],out var denominator)||numerator<=0||denominator<=0)continue;
            var gcd=System.Numerics.BigInteger.GreatestCommonDivisor(numerator,denominator);numerator/=gcd;denominator/=gcd;double rate=(double)numerator/(double)denominator;
            if(!double.IsFinite(rate)||rate<.1||rate>1000)continue;
            if(numerator<=1000000&&denominator<=1000000)return new FrameRate((int)numerator,(int)denominator);
            // Keep the measured average (VFR captures often report huge rationals), not r_frame_rate's nominal guess.
            int bestN=1,bestD=1;double bestError=double.MaxValue;int maximum=(int)Math.Min(100000,Math.Floor(1000000/rate));
            for(int d=1;d<=maximum;d++){int n=(int)Math.Round(rate*d);if(n<1||n>1000000)continue;double error=Math.Abs((double)n/d-rate);if(error<bestError){bestError=error;bestN=n;bestD=d;if(error<1e-10)break;}}
            int divisor=(int)System.Numerics.BigInteger.GreatestCommonDivisor(bestN,bestD);return new FrameRate(bestN/divisor,bestD/divisor);
        }
        return FrameRate.Default;
    }

    public static MediaAsset ParseProbe(string json,string path)
    {
        using var document=JsonDocument.Parse(json);var root=document.RootElement;if(!root.TryGetProperty("streams",out var list)||list.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("No readable media streams were found.");var streams=list.EnumerateArray().ToArray();
        bool Is(JsonElement stream,string kind)=>stream.TryGetProperty("codec_type",out var type)&&type.GetString()==kind;
        bool Cover(JsonElement stream)=>stream.TryGetProperty("disposition",out var flags)&&flags.TryGetProperty("attached_pic",out var cover)&&cover.GetInt32()==1;
        var video=streams.FirstOrDefault(s=>Is(s,"video")&&!Cover(s));bool audio=streams.Any(s=>Is(s,"audio"));bool hasVideo=video.ValueKind==JsonValueKind.Object;
        if(!hasVideo&&!audio)throw new InvalidDataException("This file has no supported video, audio or image stream.");
        bool image=hasVideo&&new[]{".png",".jpg",".jpeg",".bmp",".webp",".tif",".tiff",".avif",".heic",".heif",".exr",".tga",".psd"}.Contains(Path.GetExtension(path).ToLowerInvariant());
        double Number(JsonElement item,string name){if(!item.TryGetProperty(name,out var token))return 0;return double.TryParse(token.ValueKind==JsonValueKind.String?token.GetString():token.GetRawText(),NumberStyles.Float,CultureInfo.InvariantCulture,out var value)&&double.IsFinite(value)&&value>0?value:0;}
        double duration=root.TryGetProperty("format",out var format)?Number(format,"duration"):0;
        double videoDuration=hasVideo?Number(video,"duration"):0;
        // AAC priming/padding can extend the container slightly beyond its final video frame.
        if(videoDuration>0&&videoDuration<=duration&&duration-videoDuration<=.1+1e-9)duration=videoDuration;
        if(duration<=0)duration=streams.Select(s=>Number(s,"duration")).DefaultIfEmpty().Max();if(image)duration=5;
        if(duration<=0)throw new InvalidDataException("The media duration could not be read. Try remuxing or re-exporting this file with a valid duration.");
        return new MediaAsset{Path=Path.GetFullPath(path),Name=Path.GetFileName(path),Kind=image?MediaKind.Image:hasVideo?MediaKind.Video:MediaKind.Audio,DurationSeconds=duration,Fps=ReadFrameRate(video),Width=hasVideo&&video.TryGetProperty("width",out var w)?w.GetInt32():0,Height=hasVideo&&video.TryGetProperty("height",out var h)?h.GetInt32():0,HasAudio=audio};
    }
    public static bool TryAdoptFirstVideo(EditProject project, MediaAsset asset, bool formatChosen)
    {
        var sequence = project.Sequences.First(s => s.Id == project.ActiveSequenceId);
        if (formatChosen || asset.Kind != MediaKind.Video || asset.Width <= 0 || asset.Height <= 0 ||
            project.Media.Any(m => m.Kind == MediaKind.Video) || sequence.Tracks.Any(t => t.Clips.Count > 0))
            return false;
        int width=asset.Width+(asset.Width%2),height=asset.Height+(asset.Height%2);
        if(width>16384||height>16384||(long)width*height>67108864)return false;
        sequence.Width = width;
        sequence.Height = height;
        sequence.Fps = asset.Fps;
        return true;
    }

    internal static void VerifyMetadata()
    {
        using var document = JsonDocument.Parse("{\"avg_frame_rate\":\"60000/1001\",\"r_frame_rate\":\"60/1\"}");
        if (ReadFrameRate(document.RootElement) != new FrameRate(60000, 1001))
            throw new InvalidOperationException("Fractional video FPS was not preserved.");
        using var fallback = JsonDocument.Parse("{\"avg_frame_rate\":\"0/0\",\"r_frame_rate\":\"165/1\"}");
        if (ReadFrameRate(fallback.RootElement) != new FrameRate(165, 1))
            throw new InvalidOperationException("Video FPS fallback failed.");
        var sequence = new EditSequence();
        var project = new EditProject { ActiveSequenceId = sequence.Id, Sequences = [sequence] };
        var asset = new MediaAsset { Kind = MediaKind.Video, Width = 2560, Height = 1440, Fps = new FrameRate(165, 1) };
        if (!TryAdoptFirstVideo(project, asset, false) || sequence.Fps != asset.Fps || sequence.Width != asset.Width || sequence.Height != asset.Height)
            throw new InvalidOperationException("The first video format was not adopted.");
        sequence.Fps = FrameRate.Default;
        if (TryAdoptFirstVideo(project, asset, true) || sequence.Fps != FrameRate.Default)
            throw new InvalidOperationException("An explicitly chosen sequence format was changed.");
        foreach(var (formatDuration,expected) in new[]{("6.033333",6d),("6.1",6d),("6.100001",6.100001),("12",12d),("5.9",5.9)})
        {
            string json="{\"streams\":[{\"codec_type\":\"video\",\"duration\":\"6\"}],\"format\":{\"duration\":\""+formatDuration+"\"}}";
            if(Math.Abs(ParseProbe(json,"metadata-regression.mp4").DurationSeconds-expected)>1e-9)
                throw new InvalidOperationException("AAC padding/video duration regression: "+formatDuration);
        }
        Console.WriteLine("Metadata duration regression passed: short AAC padding, 100 ms boundary, longer audio tails and shorter container durations.");
        Console.WriteLine("Media metadata smoke passed: fractional/165 fps metadata, first-video adoption and explicit-format preservation.");
    }
}
