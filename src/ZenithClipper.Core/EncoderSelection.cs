namespace ZenithClipper;

/// <summary>Prefer hardware on the display GPU before paying for a cross-adapter transfer.</summary>
public static class EncoderSelection
{
    public static string? FromDeviceId(string? deviceId)
    {
        if(string.IsNullOrWhiteSpace(deviceId))return null;
        if(deviceId.Contains("VEN_1002",StringComparison.OrdinalIgnoreCase))return "amf";
        if(deviceId.Contains("VEN_10DE",StringComparison.OrdinalIgnoreCase))return "nvenc";
        if(deviceId.Contains("VEN_8086",StringComparison.OrdinalIgnoreCase))return "qsv";
        return null;
    }
    public static bool NeedsAmfCompatibility(string error) =>
        error.Contains("SubmitInput() failed with error 4",StringComparison.Ordinal) ||
        (error.Contains("No such filter",StringComparison.OrdinalIgnoreCase) && error.Contains("vsrc_amf",StringComparison.OrdinalIgnoreCase));
    public static string[] WindowsCandidates(string? displayEncoder)=>displayEncoder switch
    {
        "amf"=>["amf","nvenc","qsv"],
        "qsv"=>["qsv","nvenc","amf"],
        _=>["nvenc","qsv","amf"]
    };
}
