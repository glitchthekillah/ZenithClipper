using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ZenithClipper;

public sealed record ControlRequest(string Command, int? Seconds = null, Settings? Settings = null)
{
    public static ControlRequest Parse(string json)
    {
        if (json.Length > 65536) throw new ArgumentException("Control request too large.");
        ControlRequest request;
        try { request = JsonSerializer.Deserialize(json, CoreJsonContext.Default.ControlRequest) ?? throw new ArgumentException("Empty request."); }
        catch (JsonException ex) { throw new ArgumentException("Invalid control request JSON.", ex); }
        if (!new[] { "status", "save", "pause", "resume", "settings", "configure", "shutdown" }.Contains(request.Command)) throw new ArgumentException("Unknown command.");
        if (request.Command == "configure") (request.Settings ?? throw new ArgumentException("Settings required.")).Validate();
        if (request.Seconds is < 1 or > 86400) throw new ArgumentException("Invalid clip duration.");
        return request;
    }
}
public sealed record ControlResponse(bool Ok, string? Error = null, RecorderStatus? Status = null, Settings? Settings = null, string? Path = null);

public static class Control
{
    public static string PipeName
    {
        get { string path=Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppPaths.Root)); if(OperatingSystem.IsWindows()) path=path.ToUpperInvariant(); return "ZenithClipper-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..20]; }
    }
    public static async Task<ControlResponse> SendAsync(ControlRequest request, CancellationToken ct = default)
    {
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(2000, ct);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(request, CoreJsonContext.Default.ControlRequest));
        string json = await ReadLineAsync(reader, ct) ?? throw new IOException("Engine disconnected.");
        return JsonSerializer.Deserialize(json, CoreJsonContext.Default.ControlResponse) ?? throw new IOException("Invalid engine response.");
    }
    public static async Task<string?> ReadLineAsync(StreamReader reader, CancellationToken ct)
    {
        var builder = new StringBuilder(); var buffer = new char[1];
        while (await reader.ReadAsync(buffer.AsMemory(), ct) > 0)
        {
            if (buffer[0] == '\n') return builder.ToString();
            if (buffer[0] != '\r') builder.Append(buffer[0]);
            if (builder.Length > 65536) throw new ArgumentException("Control message too large.");
        }
        return builder.Length == 0 ? null : builder.ToString();
    }
}

public sealed class ControlServer(IRecorderEngine recorder, CancellationTokenSource shutdown)
{
    public ControlServer(Recorder recorder, CancellationTokenSource shutdown) : this((IRecorderEngine)recorder, shutdown) { }
    readonly SemaphoreSlim slots = new(8);
    readonly SemaphoreSlim mutations = new(1);
    readonly object pendingSync=new();
    readonly HashSet<CancellationTokenSource> pendingMutations=[];
    readonly List<Task> clients = [];
    public event Action<Settings>? SettingsChanged;
    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await slots.WaitAsync(ct);
                NamedPipeServerStream? pipe = null;
                try
                {
                    pipe = new(Control.PipeName, PipeDirection.InOut, 8, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(ct);
                    var client = HandleAsync(pipe, ct); pipe = null;
                    clients.RemoveAll(x => x.IsCompleted); clients.Add(client);
                }
                catch { pipe?.Dispose(); slots.Release(); throw; }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { await Task.WhenAll(clients); }
    }
    async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        using (pipe)
        {
            CancellationTokenSource? pending=null;
            try
            {
                using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct); readTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                ControlResponse response;
                bool quit = false;
                try
                {
                    var request = ControlRequest.Parse(await Control.ReadLineAsync(reader, readTimeout.Token) ?? "");
                    bool mutate=request.Command is "resume" or "configure";
                    if(mutate) { lock(pendingSync) { pending=CancellationTokenSource.CreateLinkedTokenSource(ct);pendingMutations.Add(pending); } }
                    else if(request.Command is "pause" or "shutdown") { lock(pendingSync)foreach(var old in pendingMutations)old.Cancel(); }
                    var operation=pending?.Token??ct;
                    if(mutate) await mutations.WaitAsync(operation);
                    try { switch (request.Command)
                    {
                        case "save": response = new(true, Path: await recorder.SaveAsync(request.Seconds, ct)); break;
                        case "pause": await recorder.StopAsync(); response = new(true, Status: recorder.Status()); break;
                        case "resume": await recorder.StartAsync(operation); response = new(true, Status: recorder.Status()); break;
                        case "settings": response = new(true, Settings: recorder.Settings); break;
                        case "configure": await recorder.UpdateAsync(request.Settings!, operation); SettingsChanged?.Invoke(recorder.Settings); response = new(true, Settings: recorder.Settings); break;
                        case "shutdown": response = new(true); quit = true; break;
                        default: response = new(true, Status: recorder.Status()); break;
                    } } finally { if(mutate) mutations.Release(); }
                }
                catch (Exception ex) when (ex is ArgumentException or IOException or TimeoutException or OperationCanceledException or InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException) { response = new(false, ex.Message, recorder.Status()); }
                await writer.WriteLineAsync(JsonSerializer.Serialize(response, CoreJsonContext.Default.ControlResponse));
                if (quit) shutdown.Cancel();
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ArgumentException) { }
            finally { if(pending!=null){lock(pendingSync)pendingMutations.Remove(pending);pending.Dispose();} slots.Release(); }
        }
    }
}
