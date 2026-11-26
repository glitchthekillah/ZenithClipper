using ZenithClipper.Editing;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ZenithClipper.Mlt;

namespace ZenithClipper.Editor.Desktop;

public sealed partial class EditorWindow
{
    long graphRevision, loadedRevision = -1, renderGeneration;
    int renderRequested, renderLoopRunning, framePostPending;
    PendingFrame? latestFrame;
    bool editorClosed, closeInProgress;
    TaskCompletionSource? exportFinished;
    sealed record PendingFrame(PreviewFrame Frame, long Generation);

    void VerifyPreviewMailbox()
    {
        var generation = Volatile.Read(ref renderGeneration);
        for (var index = 0; index < 100; index++)
            QueueFrame(new PreviewFrame(index, 1, 1, [0, 0, 0, 255]), generation);
        if (latestFrame?.Frame.Position != 99 || framePostPending != 1)
            throw new InvalidOperationException("Preview callbacks did not retain only the latest queued frame.");
        InvalidatePreview();
        QueueFrame(new PreviewFrame(100, 1, 1, [0, 0, 0, 255]), generation);
        if (latestFrame is not null)
            throw new InvalidOperationException("An invalidated preview frame was accepted.");
        QueueFrame(new PreviewFrame(3, 1, 1, [0, 0, 0, 255]), Volatile.Read(ref renderGeneration));
        DrainFrame();
        if (playhead != 3 || framePostPending != 0)
            throw new InvalidOperationException("The latest preview frame was not displayed.");
        playhead = 0;
        UpdateTime();
        Console.WriteLine("Preview mailbox smoke passed: 100 frames coalesced, stale generation rejected, newest frame displayed.");
    }

    void InvalidatePreview()
    {
        Interlocked.Increment(ref renderGeneration);
        Interlocked.Exchange(ref latestFrame, null);
    }

    async Task EnsureGraphLoaded()
    {
        var revision = graphRevision;
        if (loadedRevision == revision)
            return;
        await engine.LoadAsync(PreviewProject(), PreviewWidth());
        loadedRevision = revision;
    }

    async Task Render()
    {
        if (HeadlessVerification || playing || editorClosed || exportFinished is not null)
            return;
        Interlocked.Exchange(ref renderRequested, 1);
        if (Interlocked.CompareExchange(ref renderLoopRunning, 1, 0) != 0)
            return;
        try
        {
            while (Interlocked.Exchange(ref renderRequested, 0) != 0 && !editorClosed)
            {
                await renderGate.WaitAsync();
                try
                {
                    if (playing || exportFinished is not null)
                        return;
                    if (MonitorDuration == 0)
                    {
                        preview.Source = null;
                        return;
                    }
                    var generation = Volatile.Read(ref renderGeneration);
                    var position = playhead;
                    await EnsureGraphLoaded();
                    if (generation != Volatile.Read(ref renderGeneration))
                        continue;
                    QueueFrame(await engine.GetFrameAsync(position), generation);
                }
                finally { renderGate.Release(); }
            }
        }
        finally
        {
            Interlocked.Exchange(ref renderLoopRunning, 0);
            if (Volatile.Read(ref renderRequested) != 0 && !editorClosed)
                Run(Render);
        }
    }

    void QueueFrame(PreviewFrame frame, long generation)
    {
        if (Volatile.Read(ref editorClosed) || generation != Volatile.Read(ref renderGeneration))
            return;
        Interlocked.Exchange(ref latestFrame, new PendingFrame(frame, generation));
        if (Interlocked.CompareExchange(ref framePostPending, 1, 0) == 0)
            Dispatcher.UIThread.Post(DrainFrame, DispatcherPriority.Render);
    }

    void DrainFrame()
    {
        var pending = Interlocked.Exchange(ref latestFrame, null);
        if (!editorClosed && pending is not null && pending.Generation == Volatile.Read(ref renderGeneration))
            DisplayFrame(pending.Frame);
        Interlocked.Exchange(ref framePostPending, 0);
        if (Volatile.Read(ref latestFrame) is not null && Interlocked.CompareExchange(ref framePostPending, 1, 0) == 0)
            Dispatcher.UIThread.Post(DrainFrame, DispatcherPriority.Render);
    }

    void DisplayFrame(PreviewFrame frame)
    {
        if (bitmap is null || bitmap.PixelSize.Width != frame.Width || bitmap.PixelSize.Height != frame.Height)
        {
            bitmap?.Dispose();
            bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
            preview.Source = bitmap;
            if (fullScreenImage is not null) fullScreenImage.Source = bitmap;
        }
        using (var buffer = bitmap.Lock())
        {
            int stride = frame.Width * 4;
            if (buffer.RowBytes == stride) Marshal.Copy(frame.Rgba, 0, buffer.Address, stride * frame.Height);
            else
                for (var y = 0; y < frame.Height; y++)
                    Marshal.Copy(frame.Rgba, y * stride, buffer.Address + y * buffer.RowBytes, stride);
        }
        preview.InvalidateVisual(); fullScreenImage?.InvalidateVisual();
        FeedScopes(frame);
        // Dropped-frame indicator: during playback a gap between consecutive frames means frames were skipped.
        if (playing && lastPlayedFrame >= 0 && frame.Position > lastPlayedFrame + 1) droppedFrames += frame.Position - lastPlayedFrame - 1;
        lastPlayedFrame = playing ? frame.Position : -1;
        UpdateDroppedIndicator();
        playhead = frame.Position;
        UpdateTime();
        timeline.UpdatePlayhead();
        if (playStopAt is { } stop && playing && frame.Position >= stop)
        {
            playStopAt = null; long back = playReturnTo;
            Run(async () => { if (playing) await TogglePlay(); Seek(back); });
        }
    }

    long? playStopAt; long playReturnTo;
    /// <summary>Play around (Shift+K): from pre-roll before the playhead to post-roll after it, then back to where it was.</summary>
    async Task PlayAround()
    {
        if (playing) await TogglePlay();
        long origin = playhead;
        playhead = Math.Max(0, origin - (long)Math.Round(Prefs.PreRollSeconds * timelineFps.Value)); UpdateTime(); timeline.UpdatePlayhead();
        playStopAt = Math.Min(MonitorDuration - 1, origin + (long)Math.Round(Prefs.PostRollSeconds * timelineFps.Value)); playReturnTo = origin;
        await TogglePlay();
    }

    async Task TogglePlay()
    {
        if (exportFinished is not null)
        {
            status.Text = "Finish or cancel the export before playing.";
            return;
        }
        if (playing)
        {
            playing = false; UpdateTransportState(); playStopAt = null;
            InvalidatePreview();
            await engine.PauseAsync();
            return;
        }
        if (MonitorDuration == 0)
            return;
        InvalidatePreview();
        var generation = Volatile.Read(ref renderGeneration);
        await renderGate.WaitAsync();
        try
        {
            await EnsureGraphLoaded();
            await engine.GetFrameAsync(playhead);
            if (generation != Volatile.Read(ref renderGeneration))
                return;
            playing = true; UpdateTransportState();
        }
        finally { renderGate.Release(); }
        try
        {
            await engine.PlayAsync(frame => QueueFrame(frame, generation));
        }
        finally { if (generation == Volatile.Read(ref renderGeneration)) playing = false; UpdateTransportState(); if (!editorClosed && engine.AudioError is { } error) status.Text = error; }
    }

    bool inspectorRebuildPending;
    internal void Seek(long frame)
    {
        if (exportFinished is not null)
            return;
        InvalidatePreview();
        playhead = Math.Clamp(frame, 0, Math.Max(0, MonitorDuration - 1));
        UpdateTime();
        timeline.UpdatePlayhead();
        UpdateTransformOverlay();
        if (PanelShown("Mixer") && !playing) UpdateMixer();
        // Keyframed values follow the playhead in the inspector, like Premiere's Effect Controls.
        if (Sequence.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == selectedClip) is { Motion.Count: > 0 } && !inspectorRebuildPending)
        { inspectorRebuildPending = true; Dispatcher.UIThread.Post(() => { inspectorRebuildPending = false; if (!editorClosed) BuildInspector(); }, DispatcherPriority.Background); }
        Run(async () => { if (playing) { playing = false; UpdateTransportState(); await engine.PauseAsync(); } await Render(); });
    }

    void EnsureEditable()
    {
        if (exportFinished is not null)
            throw new InvalidOperationException("Finish or cancel the export before editing the project.");
    }

    void Undo()
    {
        if (exportFinished is not null)
        {
            status.Text = "Finish or cancel the export before editing.";
            return;
        }
        session.Undo();
        Refresh(true);
    }
    void Redo()
    {
        if (exportFinished is not null)
        {
            status.Text = "Finish or cancel the export before editing.";
            return;
        }
        session.Redo();
        Refresh(true);
    }

    async Task ConfirmClose()
    {
        if (closeInProgress)
            return;
        closeInProgress = true;
        try
        {
            if (exportFinished is { } active)
            {
                exportCancellation?.Cancel();
                status.Text = "Cancelling export before closing…";
                await active.Task;
            }
            if (!await CanReplace())
                return;
            InvalidatePreview();
            closingApproved = true;
            Close();
        }
        finally { closeInProgress = false; }
    }

    /// <summary>Runs exports one after another with the progress bar; Cancel stops the current and remaining ones.</summary>
    async Task RunExports(IReadOnlyList<(EditProject Project, string Path, ExportOptions Options)> jobs)
    {
        InvalidatePreview();
        if (playing) { playing = false; UpdateTransportState(); await engine.PauseAsync(); }
        exportCancellation = new();
        progress.IsVisible = true; cancelExport.IsVisible = true;
        int done = 0;
        foreach (var (project, path, options) in jobs)
        {
            exportCancellation.Token.ThrowIfCancellationRequested();
            status.Text = jobs.Count > 1 ? $"Exporting {done + 1} of {jobs.Count}: {Path.GetFileName(path)}…" : "Exporting…";
            await renderGate.WaitAsync();
            try { await engine.ExportAsync(project, path, options, new Progress<double>(value => { if (!editorClosed) progress.Value = value * 100; }), exportCancellation.Token); }
            finally { renderGate.Release(); loadedRevision = -1; }
            done++;
        }
        status.Text = jobs.Count > 1 ? $"Export queue finished: {done} files." : "Export complete: " + jobs[0].Path;
    }

    async Task StartExportQueue()
    {
        if (exportQueue.Count == 0) { status.Text = "The export queue is empty. Choose Add to queue in the Export dialog."; return; }
        if (exportFinished is not null) { status.Text = "An export is already in progress."; return; }
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); exportFinished = finished;
        var jobs = exportQueue.ToList(); exportQueue.Clear();
        try { await RunExports(jobs); }
        finally { progress.IsVisible = false; cancelExport.IsVisible = false; exportCancellation?.Dispose(); exportCancellation = null; exportFinished = null; finished.TrySetResult(); }
    }

    async Task Export()
    {
        if (Sequence.Duration <= 0)
        {
            status.Text = "Add a clip to the timeline before exporting.";
            return;
        }
        if (exportFinished is not null)
        {
            status.Text = "An export is already in progress.";
            return;
        }
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        exportFinished = finished;
        try
        {
            if (await ChooseExport() is not { } options) return;
            var ext = options.Preset.Extension;
            var file = await StorageProvider.SaveFilePickerAsync(new()
            {
                Title = "Export · " + options.Preset.Name,
                SuggestedFileName = Project.Name + "." + ext,
                DefaultExtension = ext,
                FileTypeChoices = [new(ext.ToUpperInvariant() + " file") { Patterns = ["*." + ext] }]
            });
            if (file?.TryGetLocalPath() is not { } path)
                return;
            if (Project.Media.Where(m => !string.IsNullOrWhiteSpace(m.Path)).Any(m => string.Equals(Path.GetFullPath(m.Path), Path.GetFullPath(path), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
                throw new IOException("Choose an output different from your source media.");
            var exportProject = exportCaptions == 0 ? ProjectCopy : WithoutCaptions(ProjectCopy);
            if (exportCaptions is 1 or 2)
            {
                var cues = CaptionCues(Project, options.In ?? 0, options.Out ?? Sequence.Duration);
                if (cues.Count > 0) await File.WriteAllTextAsync(Path.ChangeExtension(path, exportCaptions == 1 ? ".srt" : ".vtt"), exportCaptions == 1 ? SubtitleCodec.WriteSrt(cues) : SubtitleCodec.WriteVtt(cues), new System.Text.UTF8Encoding(false));
            }
            if (queueChosen)
            {
                exportQueue.Add((exportProject, path, options));
                status.Text = $"Queued {Path.GetFileName(path)}. {exportQueue.Count} in the export queue (File → Start export queue).";
                return;
            }
            await RunExports([(exportProject, path, options)]);
        }
        finally
        {
            progress.IsVisible = false;
            cancelExport.IsVisible = false;
            exportCancellation?.Dispose();
            exportCancellation = null;
            exportFinished = null;
            finished.TrySetResult();
        }
    }
}
