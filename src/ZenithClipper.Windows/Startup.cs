using Microsoft.Win32;

namespace ZenithClipper.Windows;

/// <summary>
/// Launch at sign-in through the per-user Run key. The key itself is the setting: the toggle in Settings adds or removes it.
/// The first launch of a version with this feature turns it on once; after that the user's choice is never overridden.
/// </summary>
static class Startup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "ZenithClipper";
    static string Marker => Path.Combine(AppPaths.Root, "startup-default-applied");
    static string Command => $"\"{Environment.ProcessPath}\" --background";

    public static bool Enabled
    {
        get { try { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return key?.GetValue(Name) is string; } catch (System.Security.SecurityException) { return false; } }
    }

    public static void Set(bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) key.SetValue(Name, Command); else key.DeleteValue(Name, throwOnMissingValue: false);
    }

    /// <summary>Turns startup on the first time, and keeps the path current if the app moved (portable copies, updates).</summary>
    public static void EnsureDefault()
    {
        try
        {
            if (!File.Exists(Marker)) { Set(true); Directory.CreateDirectory(AppPaths.Root); File.WriteAllText(Marker, "1"); }
            else if (Enabled) Set(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
    }
}
