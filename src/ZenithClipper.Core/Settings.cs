using System.Text.Json;

namespace ZenithClipper;

public sealed record Settings
{
    public int ClipSeconds { get; init; } = 30;
    public int BufferSeconds { get; init; } = 120;
    public long MaxBufferMiB { get; init; } = 2048;
    public int Fps { get; init; } // 0: current display refresh
    public int OutputWidth { get; init; } // 0: native
    public int Quality { get; init; } = 20;
    public string Encoder { get; init; } = "auto";
    public int Monitor { get; init; }
    public string WindowsCaptureBackend { get; init; } = "auto";
    public bool CaptureAudio { get; init; } = true;
    /// <summary>Mix microphone audio into newly captured clips.</summary>
    public bool CaptureMicrophone { get; init; } = true;
    public string WindowsInputDevice { get; init; } = "";
    public string WindowsOutputDevice { get; init; } = "";
    public string LinuxPlaybackDevice { get; init; } = "";
    public bool ClipSaveSound { get; init; } = true;
    public string ClipSaveSoundId { get; init; } = ZenithClipper.ClipSaveSound.DefaultId;
    public string[] ExcludedApps { get; init; } = ["Spotify"];
    /// <summary>Per-app level in clips, in percent (0–200). Apps not listed record at 100%. Applied live.</summary>
    public Dictionary<string, int> AppVolumes { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public int VolumeFor(string app) => AppVolumes.TryGetValue(AudioPolicy.Normalize(app), out int v) ? v : 100;
    /// <summary>Per-app capture is needed when any app is excluded or has a non-default level.</summary>
    public bool NeedsPerAppAudio => ExcludedApps.Length > 0 || AppVolumes.Values.Any(v => v != 100);
    public string ClipDirectory { get; init; } = OperatingSystem.IsMacOS() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Movies", "ZenithClipper") : Path.Combine(AppPaths.Root, "clips");
    public string FfmpegPath { get; init; } = "";
    /// <summary>X display to record; empty follows $DISPLAY (falls back to :0).</summary>
    public string LinuxDisplay { get; init; } = "";
    /// <summary>"auto" records the whole screen at its real size; or a fixed WIDTHxHEIGHT region from the top-left.</summary>
    public string LinuxVideoSize { get; init; } = "auto";
    public string LinuxRenderNode { get; init; } = "/dev/dri/renderD128";
    public string ClipHotkey { get; init; } = "Alt+C";
    public string PanelHotkey { get; init; } = "Shift+~";
    public bool AutoStartCapture { get; init; } = true;

    public void Validate()
    {
        if (ClipSeconds < 1 || BufferSeconds < ClipSeconds || BufferSeconds > 86400) throw new ArgumentException("Clip duration must be 1 second to 24 hours and fit inside the buffer duration.");
        if (MaxBufferMiB is < 32 or > 1048576) throw new ArgumentException("Disk buffer must be 32 MiB to 1 TiB.");
        if (Fps is < 0 or > 1000 || Fps == 1) throw new ArgumentException("FPS must be 0 (display refresh) or 2–1000.");
        if (OutputWidth < 0 || OutputWidth > 16384 || OutputWidth % 2 != 0) throw new ArgumentException("Output width must be 0 (native) or an even number up to 16384.");
        if (Quality is < 0 or > 51 || Monitor is < 0 or > 32) throw new ArgumentException("Invalid quality or monitor index.");
        if (!new[] { "auto", "nvenc", "qsv", "amf", "vaapi", "videotoolbox", "software" }.Contains(Encoder)) throw new ArgumentException("Unknown encoder.");
        if(WindowsCaptureBackend is not ("auto" or "desktop-duplication")) throw new ArgumentException("Unknown Windows capture backend.");
        if (string.IsNullOrWhiteSpace(ClipDirectory)) throw new ArgumentException("Choose a clip directory.");
        if (AppVolumes is null || AppVolumes.Count > 64 || AppVolumes.Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Key.Length > 128 || x.Value is < 0 or > 200)) throw new ArgumentException("App volumes must be 0–200% for at most 64 apps.");
        if (ExcludedApps is null || ExcludedApps.Length > 64 || ExcludedApps.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 128)) throw new ArgumentException("Invalid excluded app list.");
        if (LinuxVideoSize != "auto" && !System.Text.RegularExpressions.Regex.IsMatch(LinuxVideoSize, @"^\d{2,5}x\d{2,5}$")) throw new ArgumentException("Linux video size must be auto or look like 1920x1080.");
        HotkeySpec.Parse(ClipHotkey); HotkeySpec.Parse(PanelHotkey);
        if (HotkeySpec.Parse(ClipHotkey) == HotkeySpec.Parse(PanelHotkey)) throw new ArgumentException("Hotkeys must differ.");
    }
}

public static class SettingsChanges
{
    /// <summary>True when the change affects capture, encoding or audio routing and so needs a fresh buffer.</summary>
    public static bool RequiresRestart(Settings current, Settings next)
    {
        bool sameApps = current.ExcludedApps.Length == next.ExcludedApps.Length && current.ExcludedApps.Zip(next.ExcludedApps).All(x => string.Equals(x.First, x.Second, StringComparison.OrdinalIgnoreCase));
        var live = current with
        {
            ClipSeconds = next.ClipSeconds, BufferSeconds = next.BufferSeconds, MaxBufferMiB = next.MaxBufferMiB,
            ClipDirectory = next.ClipDirectory, ClipHotkey = next.ClipHotkey, PanelHotkey = next.PanelHotkey,
            AutoStartCapture = next.AutoStartCapture, ClipSaveSound = next.ClipSaveSound, ClipSaveSoundId = next.ClipSaveSoundId, LinuxPlaybackDevice = next.LinuxPlaybackDevice, ExcludedApps = sameApps ? next.ExcludedApps : current.ExcludedApps, AppVolumes = next.AppVolumes,
        };
        return live != next;
    }
}

public static class AppPaths
{
    public static string Root => Environment.GetEnvironmentVariable("ZENITHCLIPPER_HOME") ?? PortableRoot() ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZenithClipper");
    /// <summary>Portable copies keep data beside the app. The editor lives in a sibling folder (editor\ next to windows\),
    /// so it follows the app's portable.flag too; otherwise panel and editor would read different settings.</summary>
    static string? PortableRoot()
    {
        foreach (var dir in new[] { AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "..", "windows"), Path.Combine(AppContext.BaseDirectory, "..", "app") })
            if (File.Exists(Path.Combine(dir, "portable.flag"))) return Path.GetFullPath(Path.Combine(dir, "data"));
        return null;
    }
    public static string Config => Path.Combine(Root, "settings.json");
    public static string Buffer => Path.Combine(Root, "buffer");
    public static JsonSerializerOptions Json { get; } = CoreJsonContext.CreateApplicationOptions();
    public static Settings Load()
    {
        if (!File.Exists(Config)) return new Settings();
        var settings = JsonSerializer.Deserialize(File.ReadAllText(Config), CoreJsonContext.Pretty.Settings) ?? throw new ArgumentException("Empty settings file.");
        settings.Validate(); return settings;
    }
    public static void Save(Settings settings)
    {
        settings.Validate(); Directory.CreateDirectory(Root);
        var temp = Config + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(settings, CoreJsonContext.Pretty.Settings)); File.Move(temp, Config, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public static class AudioPolicy
{
    public static bool Excluded(string name,IEnumerable<string> rules)
    {
        var normalized=WithoutExtension(name.AsSpan());
        // Arrays are the recording hot path; avoid a closure, substring and iterator per PID.
        if(rules is string[] array)
        {
            foreach(string rule in array)if(normalized.Equals(WithoutExtension(rule.AsSpan()),StringComparison.OrdinalIgnoreCase))return true;
        }
        else foreach(string rule in rules)if(normalized.Equals(WithoutExtension(rule.AsSpan()),StringComparison.OrdinalIgnoreCase))return true;
        return false;
    }
    /// <summary>App key used for per-app settings: the process name without ".exe".</summary>
    public static string Normalize(string name) => WithoutExtension(name.AsSpan().Trim()).ToString();
    static ReadOnlySpan<char> WithoutExtension(ReadOnlySpan<char> name) => name.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)?name[..^4]:name;
    public static bool RootsContainExcluded(IEnumerable<int> roots,Dictionary<int,int> parents,IEnumerable<int> excluded)
    {
        var captured=roots.ToHashSet();
        foreach(int target in excluded)
        {
            int pid=target;var seen=new HashSet<int>();
            while(pid!=0 && seen.Add(pid)) { if(captured.Contains(pid)) return true;if(!parents.TryGetValue(pid,out pid))break; }
        }
        return false;
    }
    public static int[] SafeTrees(IEnumerable<int> candidates, Dictionary<int,int> parents, IEnumerable<int> excluded)
    {
        var blocked = excluded.ToHashSet(); var unsafeParents = new HashSet<int>();
        foreach (int excludedPid in blocked)
        {
            int pid=excludedPid; var seen=new HashSet<int>();
            while(parents.TryGetValue(pid,out pid) && pid!=0 && seen.Add(pid)) unsafeParents.Add(pid);
        }
        var result=new HashSet<int>(); var visited=new HashSet<int>();
        void Add(int pid)
        {
            if(!visited.Add(pid) || blocked.Contains(pid)) return;
            // Parent-only audio cannot be separated by the process-tree API. Omit it rather than leak an excluded child.
            if(unsafeParents.Contains(pid)) { foreach(var child in parents.Where(x=>x.Value==pid).Select(x=>x.Key)) Add(child); }
            else result.Add(pid);
        }
        foreach(int pid in candidates) Add(pid);
        return result.Where(pid=> {
            var seen=new HashSet<int>(); int parent=pid;
            while(parents.TryGetValue(parent,out parent) && parent!=0 && seen.Add(parent)) if(result.Contains(parent)) return false;
            return true;
        }).Order().ToArray();
    }
}

public sealed record HotkeySpec(uint Modifiers, uint Key)
{
    public string X11Symbol => Key == 0xC0 ? "grave" : Key is >= 112 and <= 135 ? "F" + (Key - 111) : ((char)Key).ToString();
    public static HotkeySpec Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Hotkey required.");
        var parts = value.Split('+', StringSplitOptions.TrimEntries); uint mods = 0;
        foreach (var part in parts[..^1]) mods |= part.ToLowerInvariant() switch { "ctrl" => 2u, "shift" => 4u, "alt" => 1u, "win" => 8u, _ => throw new ArgumentException("Use Ctrl, Shift, Alt or Win modifiers.") };
        string key = parts[^1].ToUpperInvariant();
        uint vk = key is "~" or "`" or "GRAVE" or "TILDE" ? 0xC0u : key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]) ? key[0] : key.StartsWith('F') && int.TryParse(key[1..], out var f) && f is >= 1 and <= 24 ? (uint)(111 + f) : throw new ArgumentException("Use a letter, digit, ~ key or F1–F24 for hotkeys.");
        if (mods == 0) throw new ArgumentException("Hotkey needs at least one modifier.");
        return new(mods | 0x4000, vk);
    }
}
