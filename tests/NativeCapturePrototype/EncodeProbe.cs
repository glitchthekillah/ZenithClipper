using System.Collections.Concurrent;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;

// A bounded Windows Media Foundation/WinRT alternative. Not constant-QP validated.
internal static class EncodeProbe
{
    public static async Task<int> Run(IDirect3DDevice device, GraphicsCaptureItem item,
        int seconds, int buffers, int fps, Action<string, long> snapshot)
    {
        string output = Path.Combine(AppContext.BaseDirectory, "wgc-mf-probe.mp4");
        using Direct3D11CaptureFramePool pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            device, DirectXPixelFormat.B8G8R8A8UIntNormalized, buffers, item.Size);
        using GraphicsCaptureSession session = pool.CreateCaptureSession(item);
        using AutoResetEvent available = new(false);
        using CancellationTokenSource watchdog = new(TimeSpan.FromSeconds(seconds + 15));
        ConcurrentQueue<Direct3D11CaptureFrame> queue = new();
        ConcurrentDictionary<Direct3D11CaptureFrame, byte> outstanding = new();
        int ended = 0;
        long frames = 0, samples = 0;
        TimeSpan? origin = null;
        VideoEncodingProperties raw = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8,
            (uint)item.Size.Width, (uint)item.Size.Height);
        raw.PixelAspectRatio.Numerator = 1; raw.PixelAspectRatio.Denominator = 1;
        raw.FrameRate.Numerator = (uint)fps; raw.FrameRate.Denominator = 1;
        MediaStreamSource source = new(new VideoStreamDescriptor(raw))
            { BufferTime = TimeSpan.Zero, CanSeek = false, IsLive = true };
        source.Starting += (_, e) => e.Request.SetActualStartPosition(TimeSpan.Zero);
        source.SampleRequested += (sourceSender, e) =>
        {
            try
            {
                while (Volatile.Read(ref ended) == 0 && !watchdog.IsCancellationRequested)
                {
                    if (!queue.TryDequeue(out Direct3D11CaptureFrame? frame))
                    { available.WaitOne(100); continue; }
                    origin ??= frame.SystemRelativeTime;
                    MediaStreamSample sample = MediaStreamSample.CreateFromDirect3D11Surface(frame.Surface,
                        frame.SystemRelativeTime - origin.Value);
                    sample.Duration = TimeSpan.FromSeconds(1.0 / fps);
                    // Keep the WGC frame checked out until Media Foundation finishes consuming it.
                    // No texture is returned early or overwritten while the encoder reads it.
                    sample.Processed += (_, _) =>
                    {
                        if (outstanding.TryRemove(frame, out _)) frame.Dispose();
                    };
                    e.Request.Sample = sample;
                    Interlocked.Increment(ref samples);
                    return;
                }
                e.Request.Sample = null;
            }
            catch (Exception error)
            { Console.Error.WriteLine(error); e.Request.Sample = null; }
        };
        pool.FrameArrived += (sender, _) =>
        {
            Direct3D11CaptureFrame? frame = sender.TryGetNextFrame();
            if (frame is null) return;
            Interlocked.Increment(ref frames);
            if (Volatile.Read(ref ended) != 0) { frame.Dispose(); return; }
            outstanding.TryAdd(frame, 0);
            queue.Enqueue(frame);
            available.Set();
        };
        MediaEncodingProfile profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto);
        profile.Audio = null;
        profile.Video.Width = (uint)item.Size.Width; profile.Video.Height = (uint)item.Size.Height;
        profile.Video.PixelAspectRatio.Numerator = 1; profile.Video.PixelAspectRatio.Denominator = 1;
        profile.Video.FrameRate.Numerator = (uint)fps; profile.Video.FrameRate.Denominator = 1;
        profile.Video.Bitrate = 40_000_000;
        MediaTranscoder transcoder = new() { HardwareAccelerationEnabled = true, AlwaysReencode = true };
        using FileStream file = File.Create(output);
        using Windows.Storage.Streams.IRandomAccessStream destination = file.AsRandomAccessStream();
        snapshot("encode-before-prepare", frames);
        session.StartCapture();
        Task<PrepareTranscodeResult> preparing = transcoder.PrepareMediaStreamSourceTranscodeAsync(source, destination, profile).AsTask(watchdog.Token);
        PrepareTranscodeResult preparation = await preparing;
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new MfConfiguration("mf-encode", preparation.CanTranscode,
            preparation.FailureReason.ToString(), output, item.Size.Width, item.Size.Height,
            fps, 40_000_000, false, buffers), CaptureJson.Default.MfConfiguration));
        if (!preparation.CanTranscode) return 2;
        Task encoding = preparation.TranscodeAsync().AsTask(watchdog.Token);
        try
        {
            for (int second = 1; second <= seconds; second++)
            {
                await Task.Delay(1000, watchdog.Token);
                snapshot($"encode-{second}s", Interlocked.Read(ref frames));
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new MfSamples(Interlocked.Read(ref samples),
                    outstanding.Count), CaptureJson.Default.MfSamples));
            }
            Volatile.Write(ref ended, 1);
            available.Set();
            await encoding.WaitAsync(TimeSpan.FromSeconds(5));
            snapshot("encode-complete", Interlocked.Read(ref frames));
            return 0;
        }
        finally
        {
            Volatile.Write(ref ended, 1);
            available.Set();
            watchdog.Cancel();
            session.Dispose(); pool.Dispose();
            // After the graph has completed or cancellation has been requested, terminate this
            // isolated probe promptly; do not reuse outstanding surfaces in another encoder.
            foreach (Direct3D11CaptureFrame frame in outstanding.Keys)
                if (outstanding.TryRemove(frame, out _)) frame.Dispose();
        }
    }
}


