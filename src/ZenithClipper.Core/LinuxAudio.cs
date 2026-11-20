using System.Diagnostics;
using System.Text.Json;

namespace ZenithClipper;

/// <summary>Dedicated recording sink; excluded streams remain on their original playback sink.</summary>
public sealed class LinuxAudio : IAsyncDisposable
{
    readonly Settings settings;
    readonly Func<CancellationToken, string[], Task<string>> runPactl;
    readonly Dictionary<uint, string> moved = [];
    readonly List<string> modules = [];
    public string PreferredPlaybackDevice { private get; set; }
    readonly SemaphoreSlim gate = new(1);
    readonly string sink = "zenithclipper_" + Environment.ProcessId;
    string playback = "";
    string? loopback;
    uint captureSink;
    public string PlaybackDevice => playback;
    bool direct;
    /// <summary>The recording sink's monitor, or the normal output's monitor when routing isn't possible (see StartAsync).</summary>
    public string Source => direct ? playback + ".monitor" : sink + ".monitor";
    /// <summary>Set when app exclusion had to be switched off; shown to the user.</summary>
    public string? Notice { get; private set; }
    public LinuxAudio(Settings settings) : this(settings, RunPactlAsync) { }

    internal LinuxAudio(Settings settings, Func<CancellationToken, string[], Task<string>> runPactl)
    {
        this.settings = settings;
        this.runPactl = runPactl;
        PreferredPlaybackDevice = settings.LinuxPlaybackDevice;
    }
    Task<string> Pactl(CancellationToken ct, params string[] args) => runPactl(ct, args);

    static async Task<string> RunPactlAsync(CancellationToken ct, params string[] args)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try{return await MediaTools.RunAsync("pactl",args,timeout.Token);}
        catch(OperationCanceledException ex) when(!ct.IsCancellationRequested){throw new IOException("PulseAudio command timed out.",ex);}
    }
    public static async Task<(string Name, string Label)[]> PlaybackDevicesAsync(CancellationToken ct = default)
        => ParseDevices(await RunPactlAsync(ct, "-f", "json", "list", "sinks"));

    static (string Name, string Label)[] ParseDevices(string json)
    {
        using var devices = JsonDocument.Parse(json);
        return devices.RootElement.EnumerateArray()
            .Select(item => (Name: item.GetProperty("name").GetString()!, Label: item.GetProperty("description").GetString()!))
            .Where(device => !device.Name.StartsWith("zenithclipper_", StringComparison.Ordinal))
            .ToArray();
    }

    async Task<string> ResolvePlaybackAsync(CancellationToken ct)
    {
        var preferred = PreferredPlaybackDevice;
        var systemDefault = (await Pactl(ct, "get-default-sink")).Trim();
        if (preferred.Length == 0 && !systemDefault.StartsWith("zenithclipper_", StringComparison.Ordinal))
            return systemDefault;
        var devices = ParseDevices(await Pactl(ct, "-f", "json", "list", "sinks"));
        if (devices.Any(device => device.Name == preferred))
            return preferred;
        if (devices.Any(device => device.Name == systemDefault))
            return systemDefault;
        return devices.FirstOrDefault().Name ?? playback;
    }

    async Task<string> LoadLoopbackAsync(string destination, CancellationToken ct)
    {
        var module = (await Pactl(ct, "load-module", "module-loopback", $"source={Source}", $"sink={destination}",
            "latency_msec=20", "source_dont_move=true", "sink_dont_move=true")).Trim();
        modules.Add(module);
        return module;
    }

    async Task RefreshPlaybackAsync(CancellationToken ct)
    {
        var destination = await ResolvePlaybackAsync(ct);
        if (destination.Length == 0 || destination == playback) return;
        // Keep the capture monitor and FFmpeg input stable while replacing the output route.
        var previous = loopback;
        loopback = await LoadLoopbackAsync(destination, ct);
        playback = destination;
        if (previous != null)
        {
            try { await Pactl(ct, "unload-module", previous); modules.Remove(previous); }
            catch (IOException) { } // An unplugged output may already have removed the module.
        }
    }

    public async Task StartAsync(CancellationToken ct=default)
    {
        playback = await ResolvePlaybackAsync(ct);
        modules.Add((await Pactl(ct,"load-module", "module-null-sink", $"sink_name={sink}", "sink_properties=device.description=ZenithClipperCapture")).Trim());
        try
        {
            try { loopback = await LoadLoopbackAsync(playback, ct); }
            catch (IOException)
            {
                // Some outputs (e.g. PulseAudio's auto_null, some HDMI/Bluetooth states) refuse a loopback. Moving apps to our
                // sink would then silence them, so record the normal output directly and leave every app where it is.
                await DisposeAsync(); modules.Clear(); direct = true;
                Notice = "Recording all desktop audio: this sound setup doesn't allow keeping individual apps out of clips.";
                return;
            }
            using var sinks = JsonDocument.Parse(await Pactl(ct,"-f", "json", "list", "sinks"));
            captureSink = sinks.RootElement.EnumerateArray().Single(x => x.GetProperty("name").GetString() == sink).GetProperty("index").GetUInt32();
            await RefreshAsync(ct);
        }
        catch { await DisposeAsync(); throw; }
    }
    public async Task RefreshAsync(CancellationToken ct=default)
    {
        if (direct) return;
        await gate.WaitAsync(ct);
        try
        {
            try { await RefreshPlaybackAsync(ct); }
            catch (IOException) { return; } // Retry on the next tick if the new device is still connecting.
            using var doc = JsonDocument.Parse(await Pactl(ct,"-f", "json", "list", "sink-inputs"));
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                uint id = item.GetProperty("index").GetUInt32(); uint current = item.GetProperty("sink").GetUInt32();
                if(item.TryGetProperty("owner_module",out var owner) && modules.Contains(owner.ToString())) continue;
                if(item.TryGetProperty("driver",out var driver) && driver.GetString()?.Contains("loopback",StringComparison.OrdinalIgnoreCase)==true) continue;
                var props = item.GetProperty("properties");
                string Property(string key) => props.TryGetProperty(key, out var value) ? value.GetString() ?? "" : "";
                // Never route our monitor-to-speaker loopback back into itself.
                if (Property("media.name").Contains(Source, StringComparison.Ordinal) || Property("application.id") == "org.PulseAudio.Loopback" || Property("media.role") == "filter") continue;
                bool excluded = AudioPolicy.Excluded(Property("application.process.binary"), settings.ExcludedApps) || AudioPolicy.Excluded(Property("application.name"), settings.ExcludedApps);
                if (excluded)
                {
                    if (current == captureSink && moved.Remove(id)) await Pactl(ct, "move-sink-input", $"{id}", playback);
                }
                else if (current != captureSink)
                {
                    moved[id] = $"{current}";
                    try { await Pactl(ct,"move-sink-input", $"{id}", sink); } catch (IOException) { moved.Remove(id); }
                }
            }
        }
        finally { gate.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        using var cleanup=new CancellationTokenSource(TimeSpan.FromSeconds(8));var ct=cleanup.Token;
        try{await gate.WaitAsync(ct);}catch(OperationCanceledException){return;}
        try
        {
            foreach(var (id,original) in moved)
            {
                if(ct.IsCancellationRequested)break;
                try{await Pactl(ct,"move-sink-input",$"{id}",playback);}
                catch(Exception ex) when(ex is IOException or OperationCanceledException)
                {try{await Pactl(ct,"move-sink-input",$"{id}",original);}catch(Exception retry) when(retry is IOException or OperationCanceledException){}}
            }
            foreach(var id in modules.AsEnumerable().Reverse())
            {
                using var unload=new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try{await Pactl(unload.Token,"unload-module",id);}catch(Exception ex) when(ex is IOException or OperationCanceledException){}
            }
            moved.Clear();modules.Clear();
        }
        finally{gate.Release();}
    }
}