using System.Buffers.Binary;
using System.Text;

namespace ZenithClipper.Media;

public sealed record VideoTrackConfig(int Width, int Height, int FramesPerSecond = 165);
public sealed record AudioTrackConfig(int SampleRate = 48000, int Channels = 2, int BitRate = 192000, int EncoderDelaySamples = 0);
public sealed record ReplayExport(string Path, double DurationSeconds, int Fragments);
public sealed record ReplayMetrics(double DurationSeconds, long TotalBytes, long CompletedFragments, int PendingFragments, bool ExportActive);

/// <summary>Single-producer disk-backed H264/AAC replay ring. Calls are serialized internally.
/// Video must be Annex B, constant-rate, no B frames; audio is raw AAC-LC (no ADTS).
/// Timestamp origins are shared: video frame zero and audio sample zero denote the same instant.
/// Completed GOPs are committed after the audio watermark crosses their end.</summary>
public sealed class FragmentedReplayWriter : IDisposable
{
    readonly object gate = new();
    readonly string directory;
    readonly VideoTrackConfig video;
    readonly AudioTrackConfig audio;
    double retention;
    long maximumBytes;
    readonly bool includeAudio;
    const int IndexRecordSize = 64, IndexRecordsPerBlock = 4096;
    long firstRecord, nextRecord, indexHeadBlock, retainedBytes, indexBytes;
    long oldestRecordOrdinal = -1;
    Finished oldestRecord;
    long exportLease = -1;
    bool exportActive, pruning, pruneRequested;
    Exception? pruneFailure;
    FileStream? indexWriter;
    long indexWriterBlock = -1;
    readonly CancellationTokenSource lifetime = new();
    readonly int maxSamples;
    readonly List<Part> pending = [];
    byte[]? sps, pps, initialization;
    long lastVideo = -1, lastAudio = -1;
    long movieDurationOffset, videoTrackDurationOffset, audioTrackDurationOffset, videoMediaDurationOffset, audioMediaDurationOffset, audioEditDurationOffset;
    int sequence;
    uint committedSequence;
    long lastCommittedAudioEnd = -1;
    bool ended, disposed;
    readonly record struct Sample(uint Size, uint Duration, bool Key);
    readonly record struct Finished(long StartFrame, long EndFrame, long AudioStart, long VideoTfdtOffset, long AudioTfdtOffset, long Bytes, long AudioEnd);
    sealed class Part : IDisposable
    {
        public required string VideoPath, AudioPath;
        public required FileStream VideoStream, AudioStream;
        public long StartFrame, EndFrame = -1, AudioStart = -1;
        public List<Sample> Video = [], Audio = [];
        public void Dispose() { VideoStream.Dispose(); AudioStream.Dispose(); }
    }
    public FragmentedReplayWriter(string spoolDirectory, VideoTrackConfig video, AudioTrackConfig? audio = null,
        double retentionSeconds = 120, int maximumSamplesPerFragment = 4096, bool includeAudio = true, long maxBytes = 8L * 1024 * 1024 * 1024)
    {
        if (video.Width <= 0 || video.Height <= 0 || video.Width > 65535 || video.Height > 65535 || video.FramesPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(video));
        this.audio = audio ?? new();
        if (this.audio.SampleRate != 48000 || this.audio.Channels != 2 || this.audio.BitRate != 192000 || this.audio.EncoderDelaySamples < 0 || this.audio.EncoderDelaySamples > 48000) throw new ArgumentException("Only 48 kHz stereo 192 kbit/s AAC-LC is supported.", nameof(audio));
        if (!double.IsFinite(retentionSeconds) || retentionSeconds <= 0 || retentionSeconds > 86400 || maximumSamplesPerFragment < 2 || maximumSamplesPerFragment > 65536)
            throw new ArgumentOutOfRangeException(nameof(retentionSeconds));
        if (maxBytes < 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        this.video = video; retention = retentionSeconds; maxSamples = maximumSamplesPerFragment;
        this.includeAudio = includeAudio; maximumBytes = maxBytes;
        // A private directory prevents concurrent sessions or export paths from clobbering a ring.
        directory = Path.Combine(Path.GetFullPath(spoolDirectory), "replay-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
    }
    public string SpoolDirectory => directory;
    /// <summary>Exclusive video-frame boundary of the newest committed fragment, or zero if none.</summary>
    public long CompletedThroughVideoFrame
    {
        get { lock (gate) { ObjectDisposedException.ThrowIf(disposed, this); ThrowCleanupFailure(); return firstRecord < nextRecord ? ReadRecord(nextRecord - 1).EndFrame : 0; } }
    }
    public ReplayMetrics Metrics
    {
        get { lock (gate) { ObjectDisposedException.ThrowIf(disposed, this); ThrowCleanupFailure(); return new(CurrentDuration(), DiskBytes(), nextRecord - firstRecord, pending.Count, exportActive); } }
    }
    public void SetRetention(double seconds, long maxBytes)
    {
        if (!double.IsFinite(seconds) || seconds <= 0 || seconds > 86400) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (maxBytes < 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        lock (gate) { ObjectDisposedException.ThrowIf(disposed, this); ThrowCleanupFailure(); retention = seconds; maximumBytes = maxBytes; if (Prune()) SchedulePrune(); }
    }
    long DiskBytes()
    {
        long bytes = retainedBytes + indexBytes;
        foreach (var p in pending) bytes += p.VideoStream.Length + p.AudioStream.Length;
        return bytes;
    }
    double CurrentDuration()
    {
        long first = firstRecord < nextRecord ? OldestRecord().StartFrame : pending.Count > 0 ? pending[0].StartFrame : lastVideo + 1;
        return (lastVideo + 1 - first) / (double)video.FramesPerSecond;
    }
    void EnsureBudget(long incoming)
    {
        ThrowCleanupFailure();
        if (Prune(incoming)) SchedulePrune();
        if (DiskBytes() > maximumBytes - incoming)
            throw new IOException(exportActive ? "Disk budget is held by an active export; cancel the export or increase the budget." : "Open GOP exceeds the disk budget; shorten the GOP or increase the budget.");
    }
    public void AddVideo(ReadOnlySpan<byte> annexB, long frameIndex, bool keyFrame)
    {
        lock (gate)
        {
            Check();
            EnsureBudget(annexB.Length + 4096L);
            if (frameIndex != lastVideo + 1) throw new ArgumentException("Video frame indexes must start at zero and be contiguous.", nameof(frameIndex));
            if (lastVideo < 0 && !keyFrame) throw new ArgumentException("The first video frame must be an IDR.", nameof(keyFrame));
            if (keyFrame)
            {
                if (pending.Count >= 3) throw new InvalidOperationException("Audio is too far behind video (three uncommitted GOPs).");
                if (pending.Count != 0) pending[^1].EndFrame = frameIndex;
                pending.Add(NewPart(frameIndex));
            }
            Part part = pending[^1];
            if (part.Video.Count >= maxSamples) throw new InvalidOperationException("GOP exceeded the configured metadata bound; request an IDR.");
            long begin = part.VideoStream.Position;
            bool hasIdr = false;
            int cursor = StartCode(annexB, 0, out int prefix);
            if (cursor < 0) throw new ArgumentException("Expected Annex B H264.", nameof(annexB));
            while (cursor >= 0)
            {
                int start = cursor + prefix;
                int next = StartCode(annexB, start, out int nextPrefix);
                int end = next < 0 ? annexB.Length : next;
                while (end > start && annexB[end - 1] == 0) end--;
                var nal = annexB[start..end];
                if (!nal.IsEmpty)
                {
                    int type = nal[0] & 31;
                    if (type is 7 or 8 && nal.Length > 65535) throw new ArgumentException("Parameter set exceeds avcC limit.", nameof(annexB));
                    if (type == 7) { if (sps != null && !nal.SequenceEqual(sps)) throw new InvalidOperationException("SPS changed during recording."); sps ??= nal.ToArray(); }
                    if (type == 8) { if (pps != null && !nal.SequenceEqual(pps)) throw new InvalidOperationException("PPS changed during recording."); pps ??= nal.ToArray(); }
                    if (type == 5) hasIdr = true;
                    if (type is not (7 or 8 or 9)) { WriteU32(part.VideoStream, checked((uint)nal.Length)); part.VideoStream.Write(nal); }
                }
                cursor = next; prefix = nextPrefix;
            }
            if (hasIdr != keyFrame) throw new ArgumentException("IDR flag does not match access unit.", nameof(keyFrame));
            if (sps == null || pps == null || sps.Length < 4) throw new ArgumentException("Initial IDR must include SPS and PPS.", nameof(annexB));
            part.Video.Add(new(checked((uint)(part.VideoStream.Position - begin)), 1, keyFrame));
            lastVideo = frameIndex;
            CommitReady(false);
        }
    }
    public void AddAudio(ReadOnlySpan<byte> rawAac, long sampleIndex, int sampleCount = 1024)
    {
        lock (gate)
        {
            Check();
            if (!includeAudio) throw new InvalidOperationException("This session has no audio track.");
            EnsureBudget(rawAac.Length);
            if (rawAac.IsEmpty || rawAac.Length > 65536 || sampleCount != 1024 || sampleIndex < 0 || (lastAudio >= 0 && sampleIndex != lastAudio))
                throw new ArgumentException("AAC packets must be contiguous 1024-sample frames.", nameof(sampleIndex));
            if (pending.Count == 0) throw new InvalidOperationException("Add the initial video IDR before audio.");
            Part? part = null;
            for (int i = pending.Count - 1; i >= 0; i--)
                if (pending[i].StartFrame == 0 || (sampleIndex - audio.EncoderDelaySamples) * (double)video.FramesPerSecond >= pending[i].StartFrame * (double)audio.SampleRate) { part = pending[i]; break; }
            if (part == null) throw new InvalidOperationException("Audio precedes the first GOP.");
            if (part.Audio.Count >= maxSamples) throw new InvalidOperationException("Audio metadata bound exceeded.");
            if (part.AudioStart < 0) part.AudioStart = sampleIndex;
            part.AudioStream.Write(rawAac);
            part.Audio.Add(new((uint)rawAac.Length, (uint)sampleCount, true));
            lastAudio = checked(sampleIndex + sampleCount);
            CommitReady(false);
        }
    }
    public void Finish()
    {
        lock (gate) { Check(); if (pending.Count != 0) pending[^1].EndFrame = lastVideo + 1; CommitReady(true); ended = true; }
    }
    public ReplayExport ExportNewest(string outputPath, double seconds, CancellationToken cancellationToken = default)
    {
        string path = Path.GetFullPath(outputPath);
        long selected, snapshotEnd, end, videoOrigin, audioOrigin, audioEnd = 0, timestampDelay;
        byte[] init;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (exportActive) throw new InvalidOperationException("An export is already active.");
            cancellationToken.ThrowIfCancellationRequested();
            if (!double.IsFinite(seconds) || seconds <= 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (firstRecord == nextRecord || initialization == null) throw new InvalidOperationException("No complete fragment is available.");
            if (path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Export must be outside the spool directory.", nameof(outputPath));
            end = ReadRecord(nextRecord - 1).EndFrame;
            double desired = end - seconds * video.FramesPerSecond;
            // Binary-search disk records; only one fixed-size record is live at a time.
            long low = firstRecord, high = nextRecord;
            while (low < high) { long middle = low + (high - low) / 2; if (ReadRecord(middle).StartFrame <= desired) low = middle + 1; else high = middle; }
            selected = Math.Max(firstRecord, low - 1); snapshotEnd = nextRecord;
            videoOrigin = ReadRecord(selected).StartFrame;
            audioOrigin = (long)Math.Floor(videoOrigin * (double)audio.SampleRate / video.FramesPerSecond);
            // FFmpeg applies elst priming as packet Skip Samples even when the first coded DTS
            // already lies beyond that edit. Later-GOP exports instead shift nonnegative DTS.
            timestampDelay = videoOrigin > 0 ? audio.EncoderDelaySamples : 0;
            if (includeAudio)
                for (long ordinal = snapshotEnd - 1; ordinal >= selected; ordinal--)
                {
                    long trackEnd = ReadRecord(ordinal).AudioEnd;
                    if (trackEnd >= 0) { audioEnd = Math.Max(0, trackEnd - audioOrigin); break; }
                }
            init = initialization;
            exportActive = true; exportLease = selected;
        }
        FileStream? output = null;
        bool created = false;
        try
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
            var token = cancellation.Token;
            token.ThrowIfCancellationRequested();
            output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536);
            created = true; output.Write(init);
            byte[] buffer = new byte[65536];
            // The lease protects this immutable snapshot. Ingest/pruning continue without this lock.
            for (long ordinal = selected; ordinal < snapshotEnd; ordinal++)
            {
                token.ThrowIfCancellationRequested();
                Finished fragment = ReadRecord(ordinal);
                long offset = output.Position;
                using (var input = new FileStream(FragmentPath(ordinal), FileMode.Open, FileAccess.Read, FileShare.Read, 65536))
                {
                    if (input.Length != fragment.Bytes) throw new InvalidDataException("Fragment size changed after commitment.");
                    int count;
                    while ((count = input.Read(buffer)) != 0) { token.ThrowIfCancellationRequested(); output.Write(buffer, 0, count); }
                }
                long position = output.Position;
                output.Position = offset + fragment.VideoTfdtOffset; WriteU64(output, (ulong)(fragment.StartFrame - videoOrigin));
                if (fragment.AudioTfdtOffset > 0) { output.Position = offset + fragment.AudioTfdtOffset; WriteU64(output, checked((ulong)(fragment.AudioStart - audioOrigin - timestampDelay))); }
                output.Position = position;
            }
            WriteRandomAccessIndex(output, selected, snapshotEnd, videoOrigin, init.Length, token);
            PatchDurations(output, end - videoOrigin, Math.Max(0, audioEnd - timestampDelay), audio.EncoderDelaySamples - (int)timestampDelay);
            token.ThrowIfCancellationRequested();
            output.Dispose(); output = null;
            return new(path, (end - videoOrigin) / (double)video.FramesPerSecond, checked((int)(snapshotEnd - selected)));
        }
        catch { output?.Dispose(); if (created) File.Delete(path); throw; }
        finally
        {
            lock (gate) { exportActive = false; exportLease = -1; Monitor.PulseAll(gate); if (!disposed && Prune()) SchedulePrune(); }
        }
    }
    void PatchDurations(Stream output, long videoDuration, long audioDuration, int editDelay)
    {
        long end = output.Position;
        uint videoPresentation = checked((uint)videoDuration);
        long audibleDuration = Math.Max(0, audioDuration - editDelay);
        uint audioPresentation = checked((uint)Math.Min(videoDuration, (audibleDuration * video.FramesPerSecond + audio.SampleRate - 1) / audio.SampleRate));
        Patch(movieDurationOffset, videoPresentation);
        Patch(videoTrackDurationOffset, videoPresentation); Patch(videoMediaDurationOffset, checked((uint)videoDuration));
        if (includeAudio) { Patch(audioTrackDurationOffset, audioPresentation); Patch(audioMediaDurationOffset, checked((uint)audioDuration)); if (audioEditDurationOffset > 0) { Patch(audioEditDurationOffset, audioPresentation); Patch(audioEditDurationOffset + 4, (uint)editDelay); } }
        output.Position = end;
        void Patch(long offset, uint value) { output.Position = offset; WriteU32(output, value); }
    }
    void WriteRandomAccessIndex(Stream output, long selected, long snapshotEnd, long videoOrigin, int initializationLength, CancellationToken token)
    {
        long count = snapshotEnd - selected;
        uint indexSize = checked((uint)(48 + 19 * count));
        WriteU32(output, indexSize); output.Write("mfra"u8);
        WriteU32(output, checked((uint)(24 + 19 * count))); output.Write("tfra"u8);
        WriteU32(output, 0x01000000); // version 1: 64-bit times and absolute moof offsets
        WriteU32(output, 1); WriteU32(output, 0); // video track, one-byte traf/trun/sample numbers
        WriteU32(output, checked((uint)count));
        long moofOffset = initializationLength;
        // Second streaming pass over immutable disk records: no clip-sized index in memory.
        for (long ordinal = selected; ordinal < snapshotEnd; ordinal++)
        {
            token.ThrowIfCancellationRequested();
            Finished fragment = ReadRecord(ordinal);
            WriteU64(output, checked((ulong)(fragment.StartFrame - videoOrigin)));
            WriteU64(output, checked((ulong)moofOffset)); output.Write([1, 1, 1]);
            moofOffset = checked(moofOffset + fragment.Bytes);
        }
        WriteU32(output, 16); output.Write("mfro"u8); WriteU32(output, 0); WriteU32(output, indexSize);
    }
    string FragmentPath(long ordinal) => Path.Combine(directory, "fragment-" + ordinal.ToString("D12") + ".m4s");
    string IndexPath(long block) => Path.Combine(directory, "index-" + block.ToString("D12") + ".bin");
    void AppendRecord(Finished record)
    {
        long block = nextRecord / IndexRecordsPerBlock;
        if (block != indexWriterBlock)
        {
            indexWriter?.Dispose();
            indexWriter = new FileStream(IndexPath(block), FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096);
            indexWriterBlock = block;
        }
        Span<byte> data = stackalloc byte[IndexRecordSize]; data.Clear();
        BinaryPrimitives.WriteInt64LittleEndian(data, record.StartFrame);
        BinaryPrimitives.WriteInt64LittleEndian(data[8..], record.EndFrame);
        BinaryPrimitives.WriteInt64LittleEndian(data[16..], record.AudioStart);
        BinaryPrimitives.WriteInt64LittleEndian(data[24..], record.VideoTfdtOffset);
        BinaryPrimitives.WriteInt64LittleEndian(data[32..], record.AudioTfdtOffset);
        BinaryPrimitives.WriteInt64LittleEndian(data[40..], record.Bytes);
        BinaryPrimitives.WriteInt64LittleEndian(data[48..], record.AudioEnd);
        indexWriter!.Write(data); indexWriter.Flush(); indexBytes += IndexRecordSize;
        nextRecord++; retainedBytes += record.Bytes;
    }
    // Used only under gate. Export reads remain independent and uncached.
    Finished OldestRecord()
    {
        if (oldestRecordOrdinal != firstRecord)
        {
            oldestRecord = ReadRecord(firstRecord);
            oldestRecordOrdinal = firstRecord;
        }
        return oldestRecord;
    }
    Finished ReadRecord(long ordinal)
    {
        using var input = new FileStream(IndexPath(ordinal / IndexRecordsPerBlock), FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1);
        input.Position = ordinal % IndexRecordsPerBlock * IndexRecordSize;
        Span<byte> data = stackalloc byte[IndexRecordSize]; input.ReadExactly(data);
        return new(BinaryPrimitives.ReadInt64LittleEndian(data), BinaryPrimitives.ReadInt64LittleEndian(data[8..]),
            BinaryPrimitives.ReadInt64LittleEndian(data[16..]), BinaryPrimitives.ReadInt64LittleEndian(data[24..]),
            BinaryPrimitives.ReadInt64LittleEndian(data[32..]), BinaryPrimitives.ReadInt64LittleEndian(data[40..]), BinaryPrimitives.ReadInt64LittleEndian(data[48..]));
    }
    bool Prune(long incoming = 0)
    {
        long cutoff = lastVideo + 1 - (long)Math.Ceiling(retention * video.FramesPerSecond);
        int removed = 0;
        while (removed < 8 && firstRecord < nextRecord && (exportLease < 0 || firstRecord < exportLease))
        {
            var oldest = OldestRecord();
            if (oldest.EndFrame > cutoff && DiskBytes() <= maximumBytes - incoming) break;
            File.Delete(FragmentPath(firstRecord)); retainedBytes -= oldest.Bytes; firstRecord++; removed++;
        }
        // Index blocks can be removed once all their payload records are pruned.
        long blockLimit = firstRecord / IndexRecordsPerBlock;
        while (indexHeadBlock < blockLimit)
        {
            if (indexWriterBlock == indexHeadBlock) { indexWriter?.Dispose(); indexWriter = null; indexWriterBlock = -1; }
            string path = IndexPath(indexHeadBlock++);
            long bytes = new FileInfo(path).Length; File.Delete(path); indexBytes -= bytes;
        }
            if (firstRecord >= nextRecord || (exportLease >= 0 && firstRecord >= exportLease)) return false;
        return OldestRecord().EndFrame <= cutoff || DiskBytes() > maximumBytes - incoming;
    }
    void ThrowCleanupFailure()
    {
        if (pruneFailure != null) throw new IOException("Replay cache cleanup failed.", pruneFailure);
    }
    void SchedulePrune()
    {
        if (disposed) return;
        if (pruning) { pruneRequested = true; return; }
        pruning = true; pruneRequested = false;
        _ = Task.Run(() =>
        {
            try
            {
                while (true)
                {
                    lock (gate) { pruneRequested = false; if (disposed || !Prune()) break; }
                    Thread.Yield();
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { lock (gate) pruneFailure = error; }
            finally { lock (gate) { pruning = false; if (pruneRequested && !disposed && pruneFailure == null) SchedulePrune(); Monitor.PulseAll(gate); } }
        });
    }
    Part NewPart(long frame)
    {
        string prefix = Path.Combine(directory, (++sequence).ToString("D10"));
        return new() { StartFrame = frame, VideoPath = prefix + ".video", AudioPath = prefix + ".audio",
            VideoStream = new(prefix + ".video", FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536),
            AudioStream = new(prefix + ".audio", FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 16384) };
    }
    void CommitReady(bool force)
    {
        while (pending.Count > 0)
        {
            var p = pending[0];
            if (p.EndFrame < 0) break;
            // An IDR requested beside a natural boundary can close a one-frame GOP
            // with no AAC starts. Only already committed AAC may cover that GOP.
            // Carry its coded end across adjacent video-only GOPs and subtract the
            // encoder delay exactly once when testing presentation-time coverage.
            bool coveredTinyTail = p.Audio.Count == 0 && lastCommittedAudioEnd >= 0 &&
                (p.EndFrame - p.StartFrame) * (double)audio.SampleRate < 1024.0 * video.FramesPerSecond &&
                (lastCommittedAudioEnd - audio.EncoderDelaySamples) * (double)video.FramesPerSecond >= p.EndFrame * (double)audio.SampleRate;
            if (includeAudio && !force && !coveredTinyTail && (lastAudio - audio.EncoderDelaySamples) * (double)video.FramesPerSecond < p.EndFrame * (double)audio.SampleRate + 1024 * (double)video.FramesPerSecond) break;
            if (p.Video.Count == 0 || (includeAudio && p.Audio.Count == 0 && !coveredTinyTail))
            { if (force) throw new InvalidOperationException("A fragment requires audio, except a tiny GOP covered by preceding committed AAC."); break; }
            initialization ??= BuildInitialization();
            var moof = BuildFragment(p, out long vo, out long ao);
            string file = FragmentPath(nextRecord);
            using (var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536))
            {
                output.Write(moof); WriteU32(output, checked((uint)(8 + p.VideoStream.Length + p.AudioStream.Length))); output.Write("mdat"u8);
                p.VideoStream.Position = 0; p.VideoStream.CopyTo(output, 65536);
                p.AudioStream.Position = 0; p.AudioStream.CopyTo(output, 65536);
            }
            committedSequence++;
            long committedAudioEnd = p.AudioStart < 0 ? lastCommittedAudioEnd : p.AudioStart + p.Audio.Count * 1024L;
            AppendRecord(new(p.StartFrame, p.EndFrame, p.AudioStart, vo, ao, new FileInfo(file).Length, committedAudioEnd));
            lastCommittedAudioEnd = committedAudioEnd;
            p.Dispose(); File.Delete(p.VideoPath); File.Delete(p.AudioPath); pending.RemoveAt(0);
            if (Prune()) SchedulePrune();
        }
    }
    byte[] BuildFragment(Part p, out long videoOffset, out long audioOffset)
    {
        long v = 0, a = 0;
        byte[] Create(int dataOffset) => Box("moof", b =>
        {
            b.Full("mfhd", 0, 0, x => x.U32(committedSequence + 1));
            Track(1, p.StartFrame, p.Video, dataOffset, true);
            if (includeAudio && p.Audio.Count > 0) Track(2, p.AudioStart, p.Audio, checked(dataOffset + (int)p.VideoStream.Length), false);
            void Track(uint id, long start, List<Sample> samples, int offset, bool isVideo)
            {
                b.Box("traf", t =>
                {
                    t.Full("tfhd", 0, 0x020000, x => x.U32(id));
                    t.Full("tfdt", 1, 0, x => { if (isVideo) v = x.Position; else a = x.Position; x.U64((ulong)start); });
                    t.Full("trun", 0, 0x000701, x => { x.U32((uint)samples.Count); x.U32((uint)offset); foreach (var s in samples) { x.U32(s.Duration); x.U32(s.Size); x.U32(s.Key ? 0x02000000u : 0x01010000u); } });
                });
            }
        });
        var provisional = Create(0); var result = Create(provisional.Length + 8);
        videoOffset = v; audioOffset = a; return result;
    }
    byte[] BuildInitialization()
    {
        var ftyp = Box("ftyp", b => { b.Bytes("isom"u8); b.U32(512); b.Bytes("isomiso6mp41avc1"u8); });
        var moov = Box("moov", b =>
        {
            b.Full("mvhd", 0, 0, x => { x.Zero(8); x.U32((uint)video.FramesPerSecond); movieDurationOffset = x.Position; x.U32(0); x.U32(0x10000); x.U16(0x100); x.Zero(10); x.Matrix(); x.Zero(24); x.U32(3); });
            Track(1, true); if (includeAudio) Track(2, false);
            b.Box("mvex", x => { for (uint id = 1; id <= (includeAudio ? 2 : 1); id++) x.Full("trex", 0, 0, t => { t.U32(id); t.U32(1); t.Zero(12); }); });
            void Track(uint id, bool v)
            {
                b.Box("trak", t =>
                {
                    t.Full("tkhd", 0, 7, x => { x.Zero(8); x.U32(id); x.U32(0); if (v) videoTrackDurationOffset = x.Position; else audioTrackDurationOffset = x.Position; x.U32(0); x.Zero(8); x.U16(0); x.U16(0); x.U16(v ? 0 : 0x100); x.U16(0); x.Matrix(); x.U32(v ? (uint)video.Width << 16 : 0); x.U32(v ? (uint)video.Height << 16 : 0); });
                    if (!v)
                        t.Box("edts", e => e.Full("elst", 0, 0, x =>
                        {
                            x.U32(1); audioEditDurationOffset = x.Position; x.U32(0);
                            x.U32((uint)audio.EncoderDelaySamples); x.U16(1); x.U16(0);
                        }));
                    t.Box("mdia", m =>
                    {
                        m.Full("mdhd", 0, 0, x => { x.Zero(8); x.U32(v ? (uint)video.FramesPerSecond : (uint)audio.SampleRate); if (v) videoMediaDurationOffset = x.Position; else audioMediaDurationOffset = x.Position; x.U32(0); x.U16(0x55c4); x.U16(0); });
                        m.Full("hdlr", 0, 0, x => { x.U32(0); x.Bytes(v ? "vide"u8 : "soun"u8); x.Zero(12); x.Bytes(Encoding.ASCII.GetBytes(v ? "Video\0" : "Audio\0")); });
                        m.Box("minf", n =>
                        {
                            if (v) n.Full("vmhd", 0, 1, x => x.Zero(8)); else n.Full("smhd", 0, 0, x => x.Zero(4));
                            n.Box("dinf", d => d.Full("dref", 0, 0, x => { x.U32(1); x.Full("url ", 0, 1, _ => { }); }));
                            n.Box("stbl", s =>
                            {
                                s.Full("stsd", 0, 0, x => { x.U32(1); if (v) VideoEntry(x); else AudioEntry(x); });
                                s.Full("stts", 0, 0, x => x.U32(0)); s.Full("stsc", 0, 0, x => x.U32(0));
                                s.Full("stsz", 0, 0, x => x.Zero(8)); s.Full("stco", 0, 0, x => x.U32(0));
                            });
                        });
                    });
                });
            }
        });
        movieDurationOffset += ftyp.Length; videoTrackDurationOffset += ftyp.Length; videoMediaDurationOffset += ftyp.Length;
        if (includeAudio) { audioTrackDurationOffset += ftyp.Length; audioMediaDurationOffset += ftyp.Length; if (audioEditDurationOffset > 0) audioEditDurationOffset += ftyp.Length; }
        return [.. ftyp, .. moov];
    }
    void VideoEntry(Boxes b) => b.Box("avc1", x =>
    {
        x.Zero(6); x.U16(1); x.Zero(16); x.U16(video.Width); x.U16(video.Height); x.U32(0x480000); x.U32(0x480000); x.U32(0); x.U16(1); x.Zero(32); x.U16(24); x.U16(65535);
        x.Box("avcC", c => { c.Bytes([1, sps![1], sps[2], sps[3], 255, 225]); c.U16(sps.Length); c.Bytes(sps); c.Bytes([1]); c.U16(pps!.Length); c.Bytes(pps); WriteProfileExtension(c); });
    });
    void WriteProfileExtension(Boxes config)
    {
        if (sps![1] is not (100 or 110 or 122 or 144 or 244 or 44 or 83 or 86 or 118 or 128 or 138 or 139 or 134 or 135)) return;
        byte[] rbsp = new byte[sps.Length - 1]; int count = 0;
        for (int i = 1; i < sps.Length; i++)
        {
            if (i >= 3 && sps[i] == 3 && sps[i - 1] == 0 && sps[i - 2] == 0) continue;
            rbsp[count++] = sps[i];
        }
        var bits = new SpsBits(rbsp.AsSpan(0, count));
        bits.Read(24); bits.UnsignedGolomb(); // profile, constraints, level, seq_parameter_set_id
        uint chroma = bits.UnsignedGolomb();
        if (chroma == 3) bits.Read(1);
        uint lumaDepth = bits.UnsignedGolomb(), chromaDepth = bits.UnsignedGolomb();
        if (chroma != 1 || lumaDepth != 0 || chromaDepth != 0)
            throw new NotSupportedException("Replay supports 8-bit 4:2:0 H264, matching the native NV12 encoder input.");
        config.Bytes([0xfd, 0xf8, 0xf8, 0]); // chroma_format=1, 8-bit, no SPS extensions
    }
    ref struct SpsBits
    {
        readonly ReadOnlySpan<byte> bytes;
        int position;
        public SpsBits(ReadOnlySpan<byte> data) { bytes = data; position = 0; }
        public uint Read(int count)
        {
            uint value = 0;
            for (int i = 0; i < count; i++)
            {
                if (position >= bytes.Length * 8) throw new InvalidDataException("Truncated SPS.");
                value = value << 1 | (uint)((bytes[position / 8] >> (7 - position % 8)) & 1); position++;
            }
            return value;
        }
        public uint UnsignedGolomb()
        {
            int zeros = 0;
            while (Read(1) == 0) if (++zeros > 31) throw new InvalidDataException("Invalid SPS Exp-Golomb code.");
            return ((1u << zeros) - 1) + Read(zeros);
        }
    }
    void AudioEntry(Boxes b) => b.Box("mp4a", x =>
    {
        x.Zero(6); x.U16(1); x.Zero(8); x.U16(audio.Channels); x.U16(16); x.Zero(4); x.U32((uint)audio.SampleRate << 16);
        x.Full("esds", 0, 0, e => e.Bytes([3, 25, 0, 2, 0, 4, 17, 0x40, 0x15, 0, 0, 0, 0, 2, 0xee, 0, 0, 2, 0xee, 0, 5, 2, 0x11, 0x90, 6, 1, 2]));
    });
    static int StartCode(ReadOnlySpan<byte> data, int start, out int size)
    {
        for (int i = start; i + 2 < data.Length; i++) if (data[i] == 0 && data[i + 1] == 0)
        { if (data[i + 2] == 1) { size = 3; return i; } if (i + 3 < data.Length && data[i + 2] == 0 && data[i + 3] == 1) { size = 4; return i; } }
        size = 0; return -1;
    }
    void Check() { ObjectDisposedException.ThrowIf(disposed, this); if (ended) throw new InvalidOperationException("Recording is finished."); }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true; lifetime.Cancel();
            while (exportActive || pruning) Monitor.Wait(gate);
            foreach (var p in pending) p.Dispose();
            indexWriter?.Dispose(); lifetime.Dispose(); Directory.Delete(directory, true);
        }
    }
    static byte[] Box(string name, Action<Boxes> contents) { using var memory = new MemoryStream(); var b = new Boxes(memory); b.Box(name, contents); return memory.ToArray(); }
    static void WriteU32(Stream s, uint value) { Span<byte> data = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(data, value); s.Write(data); }
    static void WriteU64(Stream s, ulong value) { Span<byte> data = stackalloc byte[8]; BinaryPrimitives.WriteUInt64BigEndian(data, value); s.Write(data); }
    sealed class Boxes(MemoryStream stream)
    {
        public long Position => stream.Position;
        public void U32(uint n) => WriteU32(stream, n);
        public void U64(ulong n) => WriteU64(stream, n);
        public void U16(int n) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, checked((ushort)n)); stream.Write(b); }
        public void Bytes(ReadOnlySpan<byte> b) => stream.Write(b);
        public void Zero(int length) { for (int i = 0; i < length; i++) stream.WriteByte(0); }
        public void Matrix() { U32(0x10000); U32(0); U32(0); U32(0); U32(0x10000); U32(0); U32(0); U32(0); U32(0x40000000); }
        public void Box(string name, Action<Boxes> contents) { long start = stream.Position; U32(0); Bytes(Encoding.ASCII.GetBytes(name)); contents(this); long end = stream.Position; stream.Position = start; U32(checked((uint)(end - start))); stream.Position = end; }
        public void Full(string name, byte version, uint flags, Action<Boxes> contents) => Box(name, b => { b.U32((uint)version << 24 | flags); contents(b); });
    }
}
















