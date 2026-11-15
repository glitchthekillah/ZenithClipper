extern alias desktop;
using desktop::ZenithClipper.Editor.Desktop;

internal static class MetadataDurationChecks
{
    internal static int Run()
    {
        int passed=0;
        void Check(string name,string streams,string? format,double expected,string extension="mp4")
        {
            string json="{\"streams\":["+streams+"]"+(format is null?"":",\"format\":{\"duration\":"+format+"}")+"}";
            double actual=MediaMetadata.ParseProbe(json,"duration-check."+extension).DurationSeconds;
            if(Math.Abs(actual-expected)>1e-9)throw new Exception($"{name}: expected {expected}, got {actual}");
            passed++;Console.WriteLine("PASS metadata "+name);
        }
        const string video="{\"codec_type\":\"video\",\"duration\":\"6\",\"width\":2560,\"height\":1600}";
        const string audio="{\"codec_type\":\"audio\",\"duration\":\"6.033333\"}";
        Check("AAC padding uses video duration",video+","+audio,"\"6.033333\"",6);
        Check("100ms boundary uses video duration",video,"6.1",6);
        Check("over 100ms retains format duration",video,"\"6.100001\"",6.100001);
        Check("intentional long audio tail retained",video+",{\"codec_type\":\"audio\",\"duration\":\"12\"}","\"12\"",12);
        Check("shorter format duration retained",video,"\"5.9\"",5.9);
        Check("missing video duration retains format","{\"codec_type\":\"video\"}","\"6.033333\"",6.033333);
        Check("missing format uses longest stream",video+","+audio,null,6.033333);
        Check("invalid format uses stream fallback",video,"\"N/A\"",6);
        Check("audio-only duration unchanged",audio,"\"6.033333\"",6.033333,"m4a");
        Check("cover art does not shorten audio","{\"codec_type\":\"video\",\"duration\":\"6\",\"disposition\":{\"attached_pic\":1}},"+audio,"\"6.033333\"",6.033333,"m4a");
        Check("image duration unchanged",video,"\"6.033333\"",5,"png");
        Console.WriteLine($"{passed} metadata duration checks passed");return passed;
    }
}
