using System.Text.Json;
using ZenithClipper.Windows.Media;

internal static class DurationMetadataChecks
{
    internal static int Run()
    {
        int passed=0;
        try
        {
            void Check(string name,string? format,string? video,double expected)
            {
                using var root=JsonDocument.Parse(format is null?"{}":"{\"format\":{\"duration\":"+format+"}}");
                using var stream=JsonDocument.Parse(video??"{}");
                double actual=Frames.ReadDuration(root.RootElement,stream.RootElement);
                if(Math.Abs(actual-expected)>1e-9)throw new Exception($"{name}: expected {expected}, got {actual}");
                passed++;Console.WriteLine("PASS Windows metadata "+name);
            }
            const string video="{\"duration\":\"6\"}";
            Check("AAC padding uses video duration","\"6.033333\"",video,6);
            Check("100ms boundary uses video duration","6.1",video,6);
            Check("over 100ms retains format duration","\"6.100001\"",video,6.100001);
            Check("long audio tail retained","\"12\"",video,12);
            Check("shorter format retained","\"5.9\"",video,5.9);
            Check("missing video duration retained","\"6.033333\"",null,6.033333);
            Check("missing format duration remains unknown",null,video,0);
            Check("invalid format duration remains unknown","\"N/A\"",video,0);
            Check("audio-only duration unchanged","\"6.033333\"",null,6.033333);
            Check("cover art does not shorten audio","\"6.033333\"","{\"duration\":\"6\",\"disposition\":{\"attached_pic\":1}}",6.033333);
            Console.WriteLine($"{passed} Windows metadata duration checks passed");return 0;
        }
        catch(Exception ex){Console.WriteLine("FAIL Windows metadata "+ex);return 1;}
    }
}
