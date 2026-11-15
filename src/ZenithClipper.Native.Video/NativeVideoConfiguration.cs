namespace ZenithClipper.Native.Video;

public sealed record NativeVideoConfiguration
{
    public int MonitorIndex { get; init; }
    public int FrameRate { get; init; }
    public int Quantizer { get; init; } = 20;
    public int FramePoolBuffers { get; init; } = 1;
    public int OutputWidth { get; init; }
    public int OutputHeight { get; init; }
    public bool UseNv12Input { get; init; }
    public string? OutputPath { get; init; }
}

/// <summary>Annex B packet bytes are borrowed only until this callback returns. Copy them if retained.</summary>
public delegate void VideoPacketHandler(ReadOnlySpan<byte> packet, long frameIndex, bool keyFrame);

public sealed record NativeVideoStatistics(long EncodedFrames, long CapturedFrames, long RepeatedFrames,
    long DeadlineMisses, long EncodedBytes, TimeSpan Elapsed);

