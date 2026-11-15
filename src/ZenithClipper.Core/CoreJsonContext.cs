using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace ZenithClipper;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, Converters = [typeof(SettingsJsonConverter), typeof(EditRequestJsonConverter)])]
[JsonSerializable(typeof(Settings))]
[JsonSerializable(typeof(ControlRequest))]
[JsonSerializable(typeof(ControlResponse))]
[JsonSerializable(typeof(RecorderStatus))]
[JsonSerializable(typeof(EditRequest))]
public partial class CoreJsonContext : JsonSerializerContext
{
    private static readonly Lazy<CoreJsonContext> pretty = new(() => new(new JsonSerializerOptions(Default!.Options) { WriteIndented = true }));
    public static CoreJsonContext Pretty => pretty.Value;

    internal static JsonSerializerOptions CreateApplicationOptions()
    {
        var options = new JsonSerializerOptions(Default.Options) { WriteIndented = true };
        options.TypeInfoResolverChain.Clear();
        options.TypeInfoResolverChain.Add(Default);
        // Preserve JIT callers serializing their own models through AppPaths.Json.
        // Both switches are constant-folded off in a NativeAOT application.
        if (RuntimeFeature.IsDynamicCodeSupported && JsonSerializer.IsReflectionEnabledByDefault)
            options.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
        return options;
    }
}

// Preserve property initializer defaults when older or partial JSON omits init-only members.
internal sealed class SettingsJsonConverter : JsonConverter<Settings>
{
    public override Settings Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if(reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Expected object.");
        var result = new Settings();
        try
        {
            while(reader.Read())
            {
                if(reader.TokenType == JsonTokenType.EndObject) return result;
                if(reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Expected property.");
                string name = reader.GetString()!;
                if(!reader.Read()) throw new JsonException("Missing value.");
                if(name.Equals("ClipSeconds", options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) result = result with { ClipSeconds = reader.GetInt32() };
                else if(name.Equals("BufferSeconds", options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) result = result with { BufferSeconds = reader.GetInt32() };
                else if(name.Equals("MaxBufferMiB", options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) result = result with { MaxBufferMiB = reader.GetInt64() };
                else if(name.Equals("Fps", options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) result = result with { Fps = reader.GetInt32() };
                else if(name.Equals("OutputWidth", options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) result = result with { OutputWidth = reader.GetInt32() };
                else if(name.Equals("Quality", options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) result = result with { Quality = reader.GetInt32() };
                else if(name.Equals("Encoder", options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) result = result with { Encoder = reader.GetString()! };
                else if(name.Equals("Monitor", options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) result = result with { Monitor = reader.GetInt32() };
                else if(name.Equals("WindowsCaptureBackend", options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) result = result with { WindowsCaptureBackend = reader.GetString()! };
                else if(name.Equals("CaptureAudio", options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) result = result with { CaptureAudio = reader.GetBoolean() };
                else if(name.Equals("ExcludedApps", options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) result = result with { ExcludedApps = JsonStringArray.Read(ref reader) };
                else if(name.Equals("AppVolumes", options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    if(reader.TokenType!=JsonTokenType.StartObject)throw new JsonException("AppVolumes must be an object.");
                    var volumes=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
                    while(reader.Read()&&reader.TokenType!=JsonTokenType.EndObject){if(reader.TokenType!=JsonTokenType.PropertyName)throw new JsonException("Expected app name.");string app=AudioPolicy.Normalize(reader.GetString()!);if(!reader.Read())throw new JsonException("Missing volume.");volumes[app]=reader.GetInt32();}
                    result = result with { AppVolumes = volumes };
                }
                else if(name.Equals("ClipDirectory", options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) result = result with { ClipDirectory = reader.GetString()! };
                else if(name.Equals("FfmpegPath", options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) result = result with { FfmpegPath = reader.GetString()! };
                else if(name.Equals("LinuxDisplay", options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) result = result with { LinuxDisplay = reader.GetString()! };
                else if(name.Equals("LinuxVideoSize", options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) result = result with { LinuxVideoSize = reader.GetString()! };
                else if(name.Equals("LinuxRenderNode", options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) result = result with { LinuxRenderNode = reader.GetString()! };
                else if(name.Equals("ClipHotkey", options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) result = result with { ClipHotkey = reader.GetString()! };
                else if(name.Equals("PanelHotkey", options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) result = result with { PanelHotkey = reader.GetString()! };
                else if(name.Equals("AutoStartCapture", options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) result = result with { AutoStartCapture = reader.GetBoolean() };
                else reader.Skip();
            }
        }
        catch(Exception ex) when(ex is InvalidOperationException or FormatException) { throw new JsonException("Invalid Settings value.", ex); }
        throw new JsonException("Unterminated object.");
    }
    public override void Write(Utf8JsonWriter writer, Settings value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(options.PropertyNamingPolicy?.ConvertName("ClipSeconds") ?? "ClipSeconds", value.ClipSeconds);
        writer.WriteNumber(options.PropertyNamingPolicy?.ConvertName("BufferSeconds") ?? "BufferSeconds", value.BufferSeconds);
        writer.WriteNumber(options.PropertyNamingPolicy?.ConvertName("MaxBufferMiB") ?? "MaxBufferMiB", value.MaxBufferMiB);
        writer.WriteNumber(options.PropertyNamingPolicy?.ConvertName("Fps") ?? "Fps", value.Fps);
        writer.WriteNumber(options.PropertyNamingPolicy?.ConvertName("OutputWidth") ?? "OutputWidth", value.OutputWidth);
        writer.WriteNumber(options.PropertyNamingPolicy?.ConvertName("Quality") ?? "Quality", value.Quality);
        writer.WriteString(options.PropertyNamingPolicy?.ConvertName("Encoder") ?? "Encoder", value.Encoder);
        writer.WriteNumber(options.PropertyNamingPolicy?.ConvertName("Monitor") ?? "Monitor", value.Monitor);
        writer.WriteString(options.PropertyNamingPolicy?.ConvertName("WindowsCaptureBackend") ?? "WindowsCaptureBackend", value.WindowsCaptureBackend);
        writer.WriteBoolean(options.PropertyNamingPolicy?.ConvertName("CaptureAudio") ?? "CaptureAudio", value.CaptureAudio);
        JsonStringArray.Write(writer, options.PropertyNamingPolicy?.ConvertName("ExcludedApps") ?? "ExcludedApps", value.ExcludedApps);
        writer.WriteStartObject(options.PropertyNamingPolicy?.ConvertName("AppVolumes") ?? "AppVolumes");
        foreach(var pair in value.AppVolumes.OrderBy(p=>p.Key,StringComparer.OrdinalIgnoreCase))writer.WriteNumber(pair.Key,pair.Value);
        writer.WriteEndObject();
        writer.WriteString(options.PropertyNamingPolicy?.ConvertName("ClipDirectory") ?? "ClipDirectory", value.ClipDirectory);
        writer.WriteString(options.PropertyNamingPolicy?.ConvertName("FfmpegPath") ?? "FfmpegPath", value.FfmpegPath);
        writer.WriteString(options.PropertyNamingPolicy?.ConvertName("LinuxDisplay") ?? "LinuxDisplay", value.LinuxDisplay);
        writer.WriteString(options.PropertyNamingPolicy?.ConvertName("LinuxVideoSize") ?? "LinuxVideoSize", value.LinuxVideoSize);
        writer.WriteString(options.PropertyNamingPolicy?.ConvertName("LinuxRenderNode") ?? "LinuxRenderNode", value.LinuxRenderNode);
        writer.WriteString(options.PropertyNamingPolicy?.ConvertName("ClipHotkey") ?? "ClipHotkey", value.ClipHotkey);
        writer.WriteString(options.PropertyNamingPolicy?.ConvertName("PanelHotkey") ?? "PanelHotkey", value.PanelHotkey);
        writer.WriteBoolean(options.PropertyNamingPolicy?.ConvertName("AutoStartCapture") ?? "AutoStartCapture", value.AutoStartCapture);
        writer.WriteEndObject();
    }
}

// Preserve property initializer defaults when older or partial JSON omits init-only members.
internal sealed class EditRequestJsonConverter : JsonConverter<EditRequest>
{
    public override EditRequest Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if(reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Expected object.");
        var result = new EditRequest(null!, null!);
        try
        {
            while(reader.Read())
            {
                if(reader.TokenType == JsonTokenType.EndObject) return result;
                if(reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Expected property.");
                string name = reader.GetString()!;
                if(!reader.Read()) throw new JsonException("Missing value.");
                if(name.Equals("Input", options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) result = result with { Input = reader.GetString()! };
                else if(name.Equals("Output", options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) result = result with { Output = reader.GetString()! };
                else if(name.Equals("StartSeconds", options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) result = result with { StartSeconds = reader.GetDouble() };
                else if(name.Equals("DurationSeconds", options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) result = result with { DurationSeconds = reader.TokenType == JsonTokenType.Null ? null : reader.GetDouble() };
                else if(name.Equals("FastTrim", options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) result = result with { FastTrim = reader.GetBoolean() };
                else if(name.Equals("Mute", options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) result = result with { Mute = reader.GetBoolean() };
                else if(name.Equals("Volume", options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) result = result with { Volume = reader.GetDouble() };
                else if(name.Equals("CropWidth", options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) result = result with { CropWidth = reader.GetInt32() };
                else if(name.Equals("CropHeight", options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) result = result with { CropHeight = reader.GetInt32() };
                else if(name.Equals("CropX", options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) result = result with { CropX = reader.GetInt32() };
                else if(name.Equals("CropY", options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) result = result with { CropY = reader.GetInt32() };
                else if(name.Equals("OutputWidth", options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) result = result with { OutputWidth = reader.GetInt32() };
                else if(name.Equals("Speed", options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) result = result with { Speed = reader.GetDouble() };
                else if(name.Equals("Quality", options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) result = result with { Quality = reader.GetInt32() };
                else if(name.Equals("Encoder", options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) result = result with { Encoder = reader.GetString()! };
                else reader.Skip();
            }
        }
        catch(Exception ex) when(ex is InvalidOperationException or FormatException) { throw new JsonException("Invalid EditRequest value.", ex); }
        throw new JsonException("Unterminated object.");
    }
    public override void Write(Utf8JsonWriter writer, EditRequest value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString(options.PropertyNamingPolicy?.ConvertName("Input") ?? "Input", value.Input);
        writer.WriteString(options.PropertyNamingPolicy?.ConvertName("Output") ?? "Output", value.Output);
        writer.WriteNumber(options.PropertyNamingPolicy?.ConvertName("StartSeconds") ?? "StartSeconds", value.StartSeconds);
        writer.WritePropertyName(options.PropertyNamingPolicy?.ConvertName("DurationSeconds") ?? "DurationSeconds"); if(value.DurationSeconds is double number) writer.WriteNumberValue(number); else writer.WriteNullValue();
        writer.WriteBoolean(options.PropertyNamingPolicy?.ConvertName("FastTrim") ?? "FastTrim", value.FastTrim);
        writer.WriteBoolean(options.PropertyNamingPolicy?.ConvertName("Mute") ?? "Mute", value.Mute);
        writer.WriteNumber(options.PropertyNamingPolicy?.ConvertName("Volume") ?? "Volume", value.Volume);
        writer.WriteNumber(options.PropertyNamingPolicy?.ConvertName("CropWidth") ?? "CropWidth", value.CropWidth);
        writer.WriteNumber(options.PropertyNamingPolicy?.ConvertName("CropHeight") ?? "CropHeight", value.CropHeight);
        writer.WriteNumber(options.PropertyNamingPolicy?.ConvertName("CropX") ?? "CropX", value.CropX);
        writer.WriteNumber(options.PropertyNamingPolicy?.ConvertName("CropY") ?? "CropY", value.CropY);
        writer.WriteNumber(options.PropertyNamingPolicy?.ConvertName("OutputWidth") ?? "OutputWidth", value.OutputWidth);
        writer.WriteNumber(options.PropertyNamingPolicy?.ConvertName("Speed") ?? "Speed", value.Speed);
        writer.WriteNumber(options.PropertyNamingPolicy?.ConvertName("Quality") ?? "Quality", value.Quality);
        writer.WriteString(options.PropertyNamingPolicy?.ConvertName("Encoder") ?? "Encoder", value.Encoder);
        writer.WriteEndObject();
    }
}

internal static class JsonStringArray
{
    internal static string[] Read(ref Utf8JsonReader reader)
    {
        if(reader.TokenType == JsonTokenType.Null) return null!;
        if(reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected string array.");
        var values = new List<string>();
        while(reader.Read()) { if(reader.TokenType == JsonTokenType.EndArray) return values.ToArray(); values.Add(reader.GetString()!); }
        throw new JsonException("Unterminated array.");
    }
    internal static void Write(Utf8JsonWriter writer, string name, string[]? values)
    {
        writer.WritePropertyName(name);
        if(values is null) { writer.WriteNullValue(); return; }
        writer.WriteStartArray(); foreach(string value in values) writer.WriteStringValue(value); writer.WriteEndArray();
    }
}


