using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ZenithClipper;

public static partial class ClipSaveSound
{
    public const string DefaultId = "01-amber";

    public static IReadOnlyList<string> SoundIds { get; } = Array.AsReadOnly(new[]
    {
        "01-amber",
        "02-bloom",
        "03-cedar",
        "04-daylight",
        "05-ember",
        "06-fern",
        "07-halo",
        "08-linen",
        "09-morning",
        "10-pebble"
    });

    public static string NormalizeId(string? id) => id != null && SoundIds.Contains(id) ? id : DefaultId;

    public static string DisplayName(string id)
    {
        var name = NormalizeId(id);
        return $"{int.Parse(name[..2])}. {char.ToUpperInvariant(name[3])}{name[4..]}";
    }

    public static void Play(Settings settings, string? linuxDevice = null)
    {
        if (!settings.ClipSaveSound) return;
        var path = Path.Combine(AppContext.BaseDirectory, "clip-save-sounds", NormalizeId(settings.ClipSaveSoundId) + ".wav");
        if (!File.Exists(path)) return;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                const uint asyncFileWithoutFallback = 0x00020003;
                PlaySound(path, IntPtr.Zero, asyncFileWithoutFallback);
                return;
            }

            var player = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "/usr/bin/afplay" : "paplay")
            {
                UseShellExecute = false
            };
            if (OperatingSystem.IsLinux())
            {
                // The Linux router skips filter streams so software playback stays out of the capture sink.
                player.ArgumentList.Add("--property=media.role=filter");
                if (!string.IsNullOrEmpty(linuxDevice)) player.ArgumentList.Add("--device=" + linuxDevice);
            }
            player.ArgumentList.Add(path);
            using var process = Process.Start(player);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            // Playback is optional feedback. The clip has already been saved.
        }
    }

    [LibraryImport("winmm.dll", EntryPoint = "PlaySoundW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PlaySound(string sound, IntPtr module, uint flags);
}
