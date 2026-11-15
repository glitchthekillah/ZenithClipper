# Isolated native video floor experiments

No production sources/DLL were changed.

All fence candidates and patches here are NOT READY and must not be applied: a clean raw-event WGC run and the verified-query controlled synthetic run hit owned watchdogs. Existing production query synchronization remains the accepted path.

- probe.cpp/build.ps1: C++/WinRT WGC-only, copy or current-shim NV12 modes, optional fault hooks/restarts. Its COM cache must be cleared before uninitialization. WGC-only teardown remains an unresolved probe issue.
- dda.cpp/build-dda.ps1: DDA capture-only on the actual primary output adapter, process DPI awareness only. Captured physical2560x1600 on AMD780M.
- wait_shim.cpp/build-wait.ps1: isolated instrumented original query and raw fence-event DLLs with test-only hooks.
- verified_wait_shim.cpp/build-verified.ps1: isolated instrumented query-proof variants. Retains End(query) before Signal and explicitly verifies query completion after every event wake.
- synthetic-wait.cpp/build-synthetic.ps1: deterministic825freshGPUbarframes at2560x1600/165fps/QP20/NV12, one owned high-resolution waitable timer; no UI/WGC/game interaction. Arguments: nv12 2560 OUTPUT_H264 SHIM_DLL.
- nvenc_shim_verified_candidate.cpp/build-verified-candidate.ps1 and verified-candidate.patch: clean, unapplied source/patch with query proof; NOT READY because instrumented equivalent failed.
- fence-trace.cpp/build-trace.ps1: raw-candidate stage tracing, compiled only; no tracing GPU run authorized after the final controlled sequence.

GPU ownership must be coordinated with root before executing any probe. Use an owned external process watchdog. Recorder PID43000 was a user-owned paused daemon; it was left alone.

Completed controlled files synthetic-wait-poll.h264, synthetic-wait-event.h264 and synthetic-verified-poll.h264 each contain825strictly CPU-decoded frames and are byte-identical. synthetic-verified-event.h264 is a704frame prefix from a failed watchdog test. Do not treat its clean prefix decode as a successful run.

## Deferred destruction cleanup A/B

cleanup-base and cleanup-clear contain completed five-restart comparisons of original teardown versus an isolated ClearState/Flush insertion. Both use original query synchronization, current native WGC bridge and the same AOT component executable. All ten clips fully CPU-decoded; decoder error logs are empty. GPU memory counters are identical between variants; resident/private differences are small and mixed. Root retained original production teardown.

See minimum-video-analysis.md for counter definitions and Microsoft's documented false GPU process-memory leak counter issue. Dedicated Usage alone is not verified physical allocation. No further GPU experiments are authorized in this task.
