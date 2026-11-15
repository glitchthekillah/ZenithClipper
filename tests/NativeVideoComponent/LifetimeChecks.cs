using ZenithClipper.Native.Video;

static class LifetimeChecks
{
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static NativeVideoRecorder Create(int mode, VideoPacketHandler? packet = null, double stopMilliseconds = 10000)
        => new(new NativeVideoConfiguration { FrameRate = 165, UseNv12Input = true }, packet ?? ((ReadOnlySpan<byte> _, long _, bool _) => { }), (2560, 1600, 165), mode, TimeSpan.FromMilliseconds(stopMilliseconds));
    public static int Run()
    {
        for (int cycle = 0; cycle < 25; cycle++)
        {
            int count = 0, finishingThread = 0, packetThread = 0;
            using var recorder = Create(0, (ReadOnlySpan<byte> _, long index, bool key) => { packetThread = Environment.CurrentManagedThreadId; Require(index == 0 && key, "Borrowed packet identity changed."); });
            recorder.WorkerFinishing += () => { count++; finishingThread = Environment.CurrentManagedThreadId; Require(recorder.TestReferences.Alive == 1, "COM owner released before finishing."); };
            recorder.Start(); recorder.Stop(); recorder.Stop();
            Require(count == 1 && finishingThread == packetThread, "Finishing callback thread/count changed.");
            Require(recorder.TestReferences == (0, 1), "Counted COM reference survived stop.");
            Require(recorder.Fault == null, "Normal stop failed.");
        }
        Console.WriteLine("PASS 25 repeat lifetimes: counted COM released after worker finishing without GC; repeated Stop/Dispose safe");
        using (var startup = Create(1))
        {
            int count = 0, faults = 0; startup.WorkerFinishing += () => { count++; Require(startup.TestReferences.Alive == 1, "Partial startup released owner before finishing."); }; startup.CaptureFault += _ => faults++;
            try { startup.Start(); throw new Exception("Partial startup failure was hidden."); } catch (InvalidOperationException error) when (error.Message.Contains("partial startup")) { }
            startup.Stop(); Require(count == 1 && faults == 1 && startup.TestReferences == (0, 1), "Partial startup leaked refs or callbacks.");
        }
        Console.WriteLine("PASS partial startup failure releases COM and raises finishing/fault exactly once");
        foreach (int mode in new[] { 2, 3 })
        {
            using var recorder = Create(mode); int count = 0; recorder.WorkerFinishing += () => count++;
            recorder.Start(); if (mode == 3) recorder.Stop();
            Require(SpinWait.SpinUntil(() => recorder.Fault != null, 5000), "Native run/flush failure was hidden."); recorder.Stop();
            Require(count == 1 && recorder.TestReferences == (0, 1), "Fault teardown leaked counted COM.");
        }
        Console.WriteLine("PASS capture and flush faults finish once and release native ownership");
        using (var callback = Create(0, (ReadOnlySpan<byte> _, long _, bool _) => throw new InvalidOperationException("Injected managed packet fault.")))
        {
            int count = 0; callback.WorkerFinishing += () => count++; callback.Start();
            Require(SpinWait.SpinUntil(() => callback.Fault != null, 5000), "Managed callback fault was hidden."); callback.Stop();
            Require(callback.Fault?.Message == "Injected managed packet fault." && count == 1 && callback.TestReferences == (0, 1), "Managed callback identity/lifetime changed.");
        }
        Console.WriteLine("PASS managed packet exception crosses ABI only as result and preserves original fault");
        using (var finish = Create(0))
        {
            int count = 0; finish.WorkerFinishing += () => { count++; throw new InvalidOperationException("Injected managed finishing fault."); };
            finish.Start(); finish.Stop(); Require(finish.Fault?.Message == "Injected managed finishing fault." && count == 1 && finish.TestReferences == (0, 1), "Finishing fault leaked COM or ran twice.");
        }
        Console.WriteLine("PASS finishing callback fault remains managed and still releases native ownership");
        foreach (int mode in new[] { 1, 2, 3 })
        {
            using var combined = Create(mode); int count = 0;
            combined.WorkerFinishing += () => { count++; throw new InvalidOperationException("Secondary finishing fault."); };
            try { combined.Start(); } catch (InvalidOperationException) when (mode == 1) { }
            if (mode == 3) combined.Stop();
            Require(SpinWait.SpinUntil(() => combined.Fault != null, 5000), "Combined fault was hidden."); combined.Stop();
            string expected = mode == 1 ? "partial startup" : mode == 2 ? "native capture" : "encoder flush";
            Require(combined.Fault!.Message.Contains(expected) && count == 1 && combined.TestReferences == (0, 1), "Secondary finishing fault replaced the original native fault.");
        }
        Console.WriteLine("PASS earlier native startup/capture/flush faults survive secondary finishing exceptions");
        using (var timeout = Create(4, stopMilliseconds: 30))
        {
            int count = 0; timeout.WorkerFinishing += () => count++; timeout.Start();
            try { timeout.Dispose(); throw new Exception("Unjoined worker released its ownership."); } catch (TimeoutException) { }
            Require(timeout.TestReferences == (1, 0) && count == 0, "Timed-out worker was not quarantined.");
            timeout.ReleaseTestWorker(); timeout.Stop();
            Require(timeout.TestReferences == (0, 1) && count == 1, "Late worker cleanup failed.");
        }
        Console.WriteLine("PASS Stop timeout keeps callback/state/native refs alive until worker joins; later Dispose releases safely");
        using (var neverStarted = Create(0)) { neverStarted.Stop(); neverStarted.Dispose(); neverStarted.Dispose(); Require(neverStarted.Statistics.EncodedFrames == 0, "Statistics unavailable after disposal."); }
        Console.WriteLine("PASS Stop/Dispose before startup and repeated Dispose are safe");
        for (int cycle = 0; cycle < 20; cycle++)
        {
            using var gate = new ManualResetEventSlim(); using var recorder = Create(0); Exception? failure = null;
            var start = new Thread(() => { gate.Wait(); try { recorder.Start(); } catch (Exception error) when (error is ObjectDisposedException or OperationCanceledException) { } catch (Exception error) { Interlocked.CompareExchange(ref failure, error, null); } });
            var dispose = new Thread(() => { gate.Wait(); try { recorder.Dispose(); } catch (Exception error) { Interlocked.CompareExchange(ref failure, error, null); } });
            start.Start(); dispose.Start(); gate.Set();
            Require(start.Join(5000) && dispose.Join(5000), "Concurrent startup/disposal did not finish.");
            Require(failure == null && !recorder.IsRunning, "Concurrent startup/disposal failed: " + failure);
            _ = recorder.Statistics;
        }
        Console.WriteLine("PASS concurrent Start/Dispose serializes worker publication and keeps statistics safe");
        return 0;
    }
}
