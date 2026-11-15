namespace ZenithClipper;

/// <summary>Recorder lifecycle and replay operations shared by managed and native engines.</summary>
public interface IRecorderEngine : IAsyncDisposable
{
    Settings Settings { get; }
    RecorderStatus Status();
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();
    Task TickAsync(CancellationToken ct);
    Task<string> SaveAsync(int? seconds = null, CancellationToken ct = default);
    Task UpdateAsync(Settings value, CancellationToken ct);
}
