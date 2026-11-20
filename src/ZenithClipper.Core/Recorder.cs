using System.Diagnostics;
using System.Text.Json;
using System.Runtime.Versioning;

namespace ZenithClipper;

public sealed record RecorderStatus(bool Capturing, string State, string? Error, string? Encoder, double BufferedSeconds, long BufferBytes, string? LastClip, int ProcessId,string? Notice=null);

public sealed class Recorder : IRecorderEngine
{
    readonly SemaphoreSlim lifecycle = new(1);
    readonly object startupSync = new();
    readonly HashSet<CancellationTokenSource> pendingStarts = [];
    readonly SemaphoreSlim bufferLock = new(1);
    readonly SemaphoreSlim saves = new(1);
    Process? ffmpeg;
    Process? portal;
    WindowsJob? captureJob;
    WindowsAudioMixer? windowsAudio;
    LinuxAudio? linuxAudio;
    Task? audioPump;
    Task? stderrPump;
    Task? portalError;
    string errorTail = "";
    string? session;
    ReplayCatalog? catalog;
    string state = "Paused";
    string? error;
    string? encoder;
    string? lastClip;
    string? notice;
    volatile bool capturing;
    DateTimeOffset progressAt;
    double progressSeconds;
    Settings settings;
    readonly bool allowCompatibilityFallback;
    public Recorder(Settings settings):this(settings,true) { }
    public Recorder(Settings settings,bool allowCompatibilityFallback){this.settings=settings;this.allowCompatibilityFallback=allowCompatibilityFallback;}
    public Settings Settings => settings;
    public async Task StartAsync(CancellationToken ct = default)
    {
        using var cancelled = new CancellationTokenSource();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(ct,cancelled.Token,deadline.Token);
        lock(startupSync) pendingStarts.Add(cancelled);
        bool compatibility=false;
        try
        {
            for(int attempt=0;;attempt++)
            {
                try { await StartAttemptAsync(startup.Token,compatibility); return; }
                catch(IOException ex) when(allowCompatibilityFallback && attempt==0 && OperatingSystem.IsWindows() && encoder=="amf" && EncoderSelection.NeedsAmfCompatibility(ex.Message))
                {
                    // The AMD driver can hand out an invalid initial surface. StartAttempt cleans up
                    // the failed session before one bounded reinitialization; never spin on failures.
                    compatibility=true;
                    notice="AMD native capture failed. Using Desktop Duplication with GPU encoding; this path uses an extra frame copy.";
                    await Task.Delay(250,startup.Token);
                }
            }
        }
        catch(OperationCanceledException) when(deadline.IsCancellationRequested && !ct.IsCancellationRequested && !cancelled.IsCancellationRequested)
        {
            error="Capture startup timed out; recording is stopped. Resume to retry.";state="Error";
            throw new TimeoutException(error);
        }
        catch(OperationCanceledException) when(cancelled.IsCancellationRequested) { error=null;state="Paused";throw; }
        finally { lock(startupSync) pendingStarts.Remove(cancelled); }
    }
    async Task StartAttemptAsync(CancellationToken ct,bool compatibility)
    {
        await lifecycle.WaitAsync(ct);
        try
        {
            if (ffmpeg is { HasExited: false }) return;
            await StopInternalAsync(); settings.Validate(); state = "Starting"; error = null; errorTail = ""; if(!compatibility)notice=null;
            encoder = await MediaTools.ResolveEncoder(settings, ct);
            int fps = settings.Fps;
            string? macSize = null;
            if (OperatingSystem.IsMacOS()) { var probe = await MacCapture.ProbeAsync(settings, ct); macSize = probe.Size; if (fps == 0) fps = probe.Hz; }
            if (fps == 0) fps = OperatingSystem.IsWindows() ? WindowsDisplay.RefreshRate(settings.Monitor) : await LinuxRefreshAsync(ct);
            bool crossAdapter=OperatingSystem.IsWindows() && encoder=="nvenc" && !WindowsDisplay.UsesNvidiaAdapter(settings.Monitor);
            if(crossAdapter)notice="NVIDIA hardware encoding is active; frames are transferred from the display GPU.";
            var capture = settings with { Encoder = encoder, Fps = fps, WindowsCaptureBackend=compatibility||crossAdapter?"desktop-duplication":settings.WindowsCaptureBackend, LinuxVideoSize = macSize ?? settings.LinuxVideoSize };
            await bufferLock.WaitAsync(ct);
            try
            {
                Directory.CreateDirectory(AppPaths.Buffer);
                foreach (string old in Directory.EnumerateDirectories(AppPaths.Buffer))
                    if (Guid.TryParseExact(Path.GetFileName(old), "N", out _) && (File.GetAttributes(old) & FileAttributes.ReparsePoint) == 0) Directory.Delete(old, true);
                session = Path.Combine(AppPaths.Buffer, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(session);
                catalog = new(session);
            }
            finally { bufferLock.Release(); }
            string? source = null;
            if (settings.CaptureAudio)
            {
                if (OperatingSystem.IsWindows())
                {
                    if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348)) throw new PlatformNotSupportedException("Application audio exclusion needs Windows build 20348 or newer (Windows 11 recommended).");
                    windowsAudio = new(settings); await windowsAudio.StartAsync(ct); source = windowsAudio.Source;
                }
                else if (OperatingSystem.IsMacOS()) source = await MacCapture.CreateAudioFifoAsync(session!, ct);
                else
                {
                    linuxAudio = new(settings); await linuxAudio.StartAsync(ct); source = linuxAudio.Source; if (linuxAudio.Notice != null) notice = linuxAudio.Notice;
                }
            }
            bool wayland = OperatingSystem.IsLinux() && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
            if (OperatingSystem.IsMacOS())
            {
                portal = MacCapture.Start(capture, macSize!, fps, source);
                portalError = DrainErrorsAsync(portal);
            }
            if (wayland)
            {
                string helper = Path.Combine(AppContext.BaseDirectory, "linux", "portal_capture.py");
                portal = MediaTools.Start("python3", [helper, encoder, $"{fps}", $"{settings.Quality}", $"{settings.OutputWidth}"]);
                portalError = DrainErrorsAsync(portal);
            }
            var args = OperatingSystem.IsWindows()&&capture.Encoder=="amf"&&capture.WindowsCaptureBackend=="auto"
                ? CaptureCommand.BuildNativeAmfGraph(capture,session,source is null?[]:[source])
                : OperatingSystem.IsWindows()
                    ? CaptureCommand.Build(capture, "windows", session, source, wayland)
                    : OperatingSystem.IsMacOS()
                        ? CaptureCommand.BuildMulti(capture, "mac", session, [.. (source is null ? Array.Empty<string>() : [source]), .. (settings.CaptureMicrophone ? [":default"] : Array.Empty<string>())])
                    : CaptureCommand.BuildMulti(capture, "linux", session, [.. (source is null ? Array.Empty<string>() : [source]), .. (settings.CaptureMicrophone ? ["@DEFAULT_SOURCE@"] : Array.Empty<string>())], wayland);
            if(OperatingSystem.IsWindows())
            {
                ffmpeg = MediaTools.Start(MediaTools.FindFfmpeg(settings), args, true); captureJob=new(ffmpeg);
            }
            else if(OperatingSystem.IsMacOS()) ffmpeg = MediaTools.Start(MediaTools.FindFfmpeg(settings), args, true);
            else ffmpeg=MediaTools.Start("python3",new[]{Path.Combine(AppContext.BaseDirectory,"linux","exec_capture.py"),MediaTools.FindFfmpeg(settings)}.Concat(args),true);
            stderrPump = DrainErrorsAsync(ffmpeg);
            capturing=true;
            progressAt=DateTimeOffset.UtcNow;progressSeconds=0;
            if (portal != null) audioPump = portal.StandardOutput.BaseStream.CopyToAsync(ffmpeg.StandardInput.BaseStream, ct);
            state = wayland ? "Waiting for screen sharing / video" : "Warming up";
            await Task.Delay(750, ct);
            if (ffmpeg.HasExited) throw new IOException(errorTail.Length > 0 ? errorTail : "Capture exited without producing video.");
            if(OperatingSystem.IsWindows())
            {
                var readyBy=DateTimeOffset.UtcNow.AddSeconds(7);
                while(true)
                {
                    ct.ThrowIfCancellationRequested();
                    if(ffmpeg.HasExited) throw new IOException(errorTail.Length>0?errorTail:"Capture exited before producing a complete video segment.");
                    catalog!.Poll();
                    if(catalog.Metrics().Item1>0) { progressSeconds=catalog.NewestEnd;progressAt=DateTimeOffset.UtcNow;state="Capturing";break; }
                    if(DateTimeOffset.UtcNow>=readyBy)throw new IOException("Capture produced no completed video during startup; recording was stopped. "+errorTail);
                    await Task.Delay(100,ct);
                }
            }
        }
        catch (Exception ex)
        {
            await StopInternalAsync(); error = ex.Message; state = "Error"; throw;
        }
        finally { lifecycle.Release(); }
    }
    readonly List<DateTimeOffset> recoveries = [];
    /// <summary>Engine log sink (set by the daemon); capture failures are written here with FFmpeg's full output.</summary>
    public static Action<string>? Log;
    /// <summary>FFmpeg prints harmless warnings first (frame-rate guesses, channel layouts); keep the lines that explain a failure.</summary>
    internal static string Summarize(string tail)
    {
        string[] noise = ["not enough frames to estimate rate", "Guessed Channel Layout", "consider increasing", "Stream #", "Metadata:", "Duration:", "Input #", "Output #", "encoder         :", "Press [q]", "frame=", "size=", "Side data:"];
        var lines = tail.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(l => !noise.Any(n => l.Contains(n, StringComparison.Ordinal))).ToList();
        return string.Join("\n", lines.TakeLast(6));
    }
    async Task DrainErrorsAsync(Process process)
    {
        while (await process.StandardError.ReadLineAsync() is { } line)
        {
            errorTail += line + "\n"; if (errorTail.Length > 6000) errorTail = errorTail[^6000..];
        }
    }
    static async Task<int> LinuxRefreshAsync(CancellationToken ct)
    {
        try
        {
            string output = await MediaTools.RunAsync("xrandr", ["--current"],ct);
            var match = System.Text.RegularExpressions.Regex.Match(output, @"(\d+(?:\.\d+)?)\*\+?");
            if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out var hz)) return Math.Clamp((int)Math.Round(hz), 2, 1000);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception) { }
        throw new IOException("Could not detect display refresh. Set FPS explicitly in settings (required on Wayland).");
    }
    public RecorderStatus Status()
    {
        var metrics = catalog?.Metrics() ?? (0d,0L);
        bool active = capturing;
        return new(active, active && metrics.Item1 > 0 ? "Capturing" : state, error, encoder, metrics.Item1, metrics.Item2, lastClip, Environment.ProcessId,notice);
    }
    public async Task TickAsync(CancellationToken ct)
    {
        bool restart=false;
        await lifecycle.WaitAsync(ct);
        try
        {
            if (ffmpeg == null) return;
            if (ffmpeg.HasExited || (OperatingSystem.IsWindows() && windowsAudio?.Faulted == true) || audioPump?.IsFaulted == true || portal is { HasExited:true } || (portal!=null && audioPump?.IsCompleted==true))
            {
                error = Summarize(errorTail) is { Length: > 0 } why ? why : "Capture or audio stream ended; resume to retry.";
                Log?.Invoke($"Capture ended (exit {(ffmpeg.HasExited ? ffmpeg.ExitCode : 0)}): " + string.Join(" | ", errorTail.Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(15)));
                await StopInternalAsync(); state = "Error";
                // Recover by itself a few times (a display change, an audio device going away) before giving up;
                // the buffer is kept, so a save still reaches back before the hiccup.
                recoveries.RemoveAll(t => DateTimeOffset.UtcNow - t > TimeSpan.FromMinutes(10));
                if (recoveries.Count < 3) { recoveries.Add(DateTimeOffset.UtcNow); notice = "Capture stopped unexpectedly and was restarted: " + error; restart = true; }
                return;
            }
            if (OperatingSystem.IsWindows() && settings.CaptureAudio)
            {
                if (windowsAudio != null && await windowsAudio.RefreshAsync(ct))
                {
                    // An excluded child may have entered a captured parent tree since the last scan.
                    // Discard the affected history and restart, rather than export that transition's audio.
                    await StopInternalAsync();await bufferLock.WaitAsync(ct);
                    try{catalog=null;session=null;}finally{bufferLock.Release();}
                    notice="An excluded app entered a captured process tree. Previous history was cleared; rebuilding the buffer.";state="Audio app changed; rebuilding safe buffer";restart=true;return;
                }
            }
            if (linuxAudio != null) await linuxAudio.RefreshAsync(ct);
            if (session != null)
            {
                await bufferLock.WaitAsync(ct);
                try
                {
                    catalog!.Poll(); catalog.Prune(settings.BufferSeconds, settings.MaxBufferMiB * 1024 * 1024);
                    // Duration remains constant once retention is full, so use the newest segment's timeline end.
                    double newest=catalog.NewestEnd;
                    if(newest>progressSeconds){progressSeconds=newest;progressAt=DateTimeOffset.UtcNow;}
                    int stallSeconds=portal!=null && progressSeconds==0?120:15;
                    if(DateTimeOffset.UtcNow-progressAt>TimeSpan.FromSeconds(stallSeconds)) { error=$"No completed video for {stallSeconds} seconds; capture stalled. "+errorTail;await StopInternalAsync();state="Error";return; }
                    var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(session))!);
                    long total = catalog.Metrics().bytes;
                    if (drive.AvailableFreeSpace < 128L * 1024 * 1024 || total > settings.MaxBufferMiB * 1024 * 1024 + 128L * 1024 * 1024)
                    { error = "Disk buffer limit or low free space reached; capture stopped."; await StopInternalAsync(); state = "Error"; }
                }
                finally { bufferLock.Release(); }
            }
        }
        catch (Exception ex) when(ex is IOException or System.Runtime.InteropServices.COMException or System.ComponentModel.Win32Exception) { error = ex.Message; await StopInternalAsync(); state = "Error"; }
        finally
        {
            lifecycle.Release();
            if(restart) try{await StartAsync(ct);}catch(Exception ex) when(ex is IOException or TimeoutException or System.Runtime.InteropServices.COMException or PlatformNotSupportedException or System.ComponentModel.Win32Exception){error=ex.Message;state="Error";}
        }
    }
    public async Task<string> SaveAsync(int? seconds = null, CancellationToken ct = default)
    {
        if (!await saves.WaitAsync(0, ct)) throw new IOException("A clip is already being saved.");
        ReplaySnapshot? snapshot = null;
        try
        {
            int duration = seconds ?? settings.ClipSeconds;
            if (duration < 1 || duration > settings.BufferSeconds) throw new ArgumentException("Requested duration must fit in the configured buffer.");
            await TickAsync(ct); // Recheck process trees at the hotkey, not only on the periodic scan.
            await bufferLock.WaitAsync(ct);
            IReadOnlyList<Segment> selected;
            try
            {
                catalog?.Poll(); selected = ReplayIndex.Select(catalog?.Snapshot() ?? [], duration);
                snapshot = new ReplaySnapshot(selected, Path.Combine(AppPaths.Root, "exports"),catalog?.UnfinishedPath);
            }
            finally { bufferLock.Release(); }
            lastClip = await snapshot.SaveAsync(settings, duration, ct);
            notice = null;
            ClipSaveSound.Play(settings, linuxAudio?.PlaybackDevice);
            return lastClip;
        }
        finally { snapshot?.Dispose(); saves.Release(); }
    }
    public async Task UpdateAsync(Settings value, CancellationToken ct)
    {
        value.Validate();
        if (!SettingsChanges.RequiresRestart(settings, value))
        {
            // Clip length, history limits, folder and shortcuts are read live; keep the replay history.
            await lifecycle.WaitAsync(ct);
            try
            {
                AppPaths.Save(value);
                settings = value;
                if (linuxAudio != null) linuxAudio.PreferredPlaybackDevice = value.LinuxPlaybackDevice;
            }
            finally { lifecycle.Release(); }
            return;
        }
        bool active = ffmpeg is { HasExited: false }; await StopAsync();
        AppPaths.Save(value); settings = value; if (active) await StartAsync(ct);
    }
    public async Task StopAsync()
    {
        lock(startupSync) foreach(var pending in pendingStarts) pending.Cancel();
        await lifecycle.WaitAsync();
        try
        {
            bool clear=false;
            if(OperatingSystem.IsWindows() && windowsAudio!=null)
                try{clear=windowsAudio.RequiresHistoryReset();}catch(Exception ex) when(ex is IOException or System.Runtime.InteropServices.COMException or System.ComponentModel.Win32Exception){clear=true;}
            await StopInternalAsync();state="Paused";
            if(clear){await bufferLock.WaitAsync();try{catalog=null;session=null;}finally{bufferLock.Release();}notice="Audio rules changed; previous history was cleared.";}
        }
        finally { lifecycle.Release(); }
    }
    async Task StopInternalAsync()
    {
        capturing=false;
        if (portal != null) { if (!portal.HasExited) portal.Kill(true); await portal.WaitForExitAsync(); if(portalError!=null) await portalError; portal.Dispose(); portal = null; try { ffmpeg?.StandardInput.Close(); } catch(IOException) { } }
        if (ffmpeg != null)
        {
            if (!ffmpeg.HasExited)
            {
                try { await ffmpeg.StandardInput.WriteLineAsync("q"); await ffmpeg.StandardInput.FlushAsync(); await ffmpeg.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException) { if (!ffmpeg.HasExited) ffmpeg.Kill(true); }
            }
            await ffmpeg.WaitForExitAsync(); if (stderrPump != null) await stderrPump;
            ffmpeg.Dispose(); ffmpeg = null;
            if(OperatingSystem.IsWindows()) { captureJob?.Dispose(); captureJob=null; }
        }
        if (windowsAudio != null && OperatingSystem.IsWindows()) { await windowsAudio.DisposeAsync(); windowsAudio = null; }
        if (linuxAudio != null) { await linuxAudio.DisposeAsync(); linuxAudio = null; }
    }
    internal static void TryDelete(string path) { try { File.Delete(path); } catch (IOException) { } }
    public async ValueTask DisposeAsync() { await StopAsync(); await saves.WaitAsync(); saves.Release(); }
}
