using System.Globalization;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace ZenithClipper;

/// <summary>A newer GitHub release and the download that fits this copy of the app.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string PageUrl, string? DownloadUrl, string? DownloadName, string? ChecksumsUrl);

/// <summary>Checks GitHub releases for a newer version and downloads it with checksum verification.
/// Nothing runs in the recorder: the panel, the login/daily check and the Linux controls call this on demand.</summary>
public static class Updates
{
    public const string Repository = "glitchthekillah/ZenithClipper";
    public static string ReleasesPage => $"https://github.com/{Repository}/releases/latest";

    public static Version Current
    {
        get
        {
            string? text = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? typeof(Updates).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return Parse(text?.Split('+')[0]) ?? new Version(0, 0, 0);
        }
    }

    /// <summary>How this copy was installed decides what "Update now" does.</summary>
    public enum Kind { WindowsInstalled, WindowsPortable, AppImage, LinuxDeb, LinuxTarball, MacApp }
    public static Kind Flavor =>
        OperatingSystem.IsWindows() ? (File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.flag")) ? Kind.WindowsPortable : Kind.WindowsInstalled)
        : OperatingSystem.IsMacOS() ? Kind.MacApp
        : Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } ? Kind.AppImage
        : AppContext.BaseDirectory.StartsWith("/opt/zenithclipper", StringComparison.Ordinal) ? Kind.LinuxDeb : Kind.LinuxTarball;
    /// <summary>Every packaged copy installs updates by itself. Only development builds run through `dotnet` open the release page.</summary>
    public static bool CanSelfUpdate => !OperatingSystem.IsMacOS() && Path.GetFileNameWithoutExtension(Environment.ProcessPath) != "dotnet";

    static readonly HttpClient Http = CreateClient();
    static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ZenithClipper", Current.ToString()));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    /// <summary>The newest release if it is newer than this build, otherwise null. Never throws for network trouble.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken cancel = default)
    {
        try
        {
            using var response = await Http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", cancel);
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancel));
            var root = doc.RootElement;
            string tag = root.GetProperty("tag_name").GetString() ?? "";
            if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;
            if (Parse(tag.TrimStart('v')) is not { } version || version <= Current) { SaveState(State with { LastCheck = DateTime.UtcNow }); return null; }
            string page = root.TryGetProperty("html_url", out var url) ? url.GetString() ?? ReleasesPage : ReleasesPage;
            string? download = null, name = null, sums = null;
            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                string n = asset.GetProperty("name").GetString() ?? "", link = asset.GetProperty("browser_download_url").GetString() ?? "";
                if (n == "SHA256SUMS") sums = link;
                else if (Matches(n)) (download, name) = (link, n);
            }
            SaveState(State with { LastCheck = DateTime.UtcNow });
            return new UpdateInfo(version, tag, page, download, name, sums);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException or IOException) { return null; }
    }
    static bool Matches(string asset) => Flavor switch
    {
        Kind.WindowsInstalled => asset.EndsWith("_x64-setup.exe", StringComparison.OrdinalIgnoreCase),
        Kind.WindowsPortable => asset.EndsWith("_x64-portable.zip", StringComparison.OrdinalIgnoreCase),
        Kind.AppImage => asset.EndsWith("-x86_64.AppImage", StringComparison.OrdinalIgnoreCase),
        Kind.LinuxDeb => asset.EndsWith("_amd64.deb", StringComparison.OrdinalIgnoreCase),
        Kind.LinuxTarball => asset.EndsWith("-linux-x64.tar.gz", StringComparison.OrdinalIgnoreCase),
        Kind.MacApp => asset.EndsWith("_mac-arm64.dmg", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    /// <summary>Downloads the update and checks it against the release's SHA256SUMS. Returns the verified file.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        if (update.DownloadUrl is null || update.DownloadName is null) throw new InvalidOperationException("This release has no download for this kind of install.");
        if (update.ChecksumsUrl is null) throw new InvalidOperationException("This release has no SHA256SUMS, so the download can't be verified.");
        string expected = (await Http.GetStringAsync(update.ChecksumsUrl, cancel)).Split('\n')
            .Select(l => l.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries))
            .FirstOrDefault(p => p.Length == 2 && p[1].TrimStart('*').Trim() == update.DownloadName)?[0]
            ?? throw new InvalidOperationException("SHA256SUMS doesn't list " + update.DownloadName + ".");
        string folder = Path.Combine(Path.GetTempPath(), "ZenithClipper-update"); Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, update.DownloadName), partial = target + ".part";
        using (var response = await Http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancel))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? 0, done = 0;
            await using var input = await response.Content.ReadAsStreamAsync(cancel);
            await using var output = File.Create(partial);
            var buffer = new byte[1 << 16]; int read;
            while ((read = await input.ReadAsync(buffer, cancel)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancel); done += read;
                if (total > 0) progress?.Report(done / (double)total);
            }
        }
        string actual;
        await using (var check = File.OpenRead(partial)) actual = Convert.ToHexString(await SHA256.HashDataAsync(check, cancel));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) { File.Delete(partial); throw new InvalidDataException("The download didn't match its published checksum, so it was discarded."); }
        File.Move(partial, target, true);
        return target;
    }

    // ---- Remembered state: when we last looked, which version we already told the user about ----
    public sealed record UpdateState(DateTime LastCheck, string Notified);
    static string StateFile => Path.Combine(AppPaths.Root, "update.ini");
    public static UpdateState State
    {
        get
        {
            try
            {
                var map = File.ReadAllLines(StateFile).Select(l => l.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0].Trim(), p => p[1].Trim());
                DateTime.TryParse(map.GetValueOrDefault("last_check"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var last);
                return new UpdateState(last, map.GetValueOrDefault("notified") ?? "");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new UpdateState(default, ""); }
        }
    }
    public static void SaveState(UpdateState state)
    {
        try { Directory.CreateDirectory(AppPaths.Root); File.WriteAllText(StateFile, $"last_check={state.LastCheck.ToString("O", CultureInfo.InvariantCulture)}\nnotified={state.Notified}\n"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
    /// <summary>True if we haven't looked in the last <paramref name="hours"/> hours (keeps background checks rare).</summary>
    public static bool Due(double hours) => DateTime.UtcNow - State.LastCheck > TimeSpan.FromHours(hours);

    static Version? Parse(string? text) => Version.TryParse(text?.Trim(), out var v) ? new Version(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build)) : null;
}
