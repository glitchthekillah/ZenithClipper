# CPU optimization — 2026-11-07

Scope: reduce avoidable CPU work while preserving the recorder's established fixed memory footprint, native resolution/frame-rate setting, QP20, audio settings and before-capture exclusions. No UI, server, game-control, OS power or display-setting changes.

## Changes

- PCM history now uses at most two contiguous span copies per write/read instead of per-sample indexing/modulo operations. It retains the same 24,000-short / quarter-second capacity, the same object fields and monitor lock, and the same drop-oldest/silence-padding behavior. Packets larger than the ring retain their newest samples directly. No scratch buffers, caches, pooling or GC/working-set manipulation were introduced.
- Production video pacing, timers, GPU synchronization and encoder are unchanged. A shorter-spin timing candidate was tested but rejected because its whole-recorder improvement did not reproduce reliably. Only the PCM block-copy change is retained.

## Verification

- Independent reference FIFO: 3,000 randomized comparisons, including wrapping, overrun, packets larger than capacity, empty packets, oversized reads, silence padding and clear.
- Allocated bytes per ring: **48,056 before and after**. No increase in measured hot-loop allocations. Median CPU for 100,000 write/read pairs fell from roughly 2.5 seconds to 31 milliseconds (isolated managed test, not the whole recorder).
- Current core regression suite: **57/57 passed**.
- Current-source native component harness: fixed 825-frame synthetic pacing, nine lifetime/fault groups, concurrent keyframe flags and real keyframes at 251/330/331 with 381 contiguous encoded frames. Real NV12 capture passes; native format retained. H264 and saved MP4 files receive strict CPU decoding.
- The first published component harness was stale and ignored explicit test-mode arguments. Its labeled mode results are not accepted as those tests. The harness was rebuilt from current source and positively checks the expected mode output. Old empty-output decoder logs are also not proof of failure or success; explicit exit-code verification is required.

## Interpreting CPU and memory

These are sequential desktop tests, not deterministic game workloads. CPU varies with desktop changes, audio sources and scheduling. The first full pacing candidate measured about 0.80% machine CPU, but its repeat measured about 1.12%; controls were about 1.05–1.08%. A repeatable whole-recorder percentage speedup is **not established**. Component CPU savings must not be advertised as overall CPU savings.

Short three-cycle tests measured about 88–91 MiB peak resident RAM across control and candidate builds. Sub-MiB / few-MiB differences between individual runs are retained rather than presented as exact savings. Unchanged allocated buffer capacity is directly tested; this is not a universal working-set maximum. The under-50-MiB objective remains unmet. No game FPS-loss conclusion is available.

## Rejected experiment

An isolated GPU-query DONOTFLUSH candidate timed out during the synthetic GPU test and was killed by its watchdog. It is rejected. A separate timer-backed query-polling experiment also encountered a timeout in its synthetic control build, so that experiment cannot establish benefit or safety and is rejected. Production copy synchronization remains the original query/Flush/SwitchToThread path. Earlier fence-event experiments remain rejected. The shorter-spin candidate also remains experimental despite passing functional and ten-restart memory checks; its CPU gain was inconsistent. No video timing or synchronization candidate is deployed.

## Evidence

- `tests/PcmRingChecks/` and `outputs/cpu-ring-verified.log`
- `outputs/cpu-core-checks.log`
- `outputs/cpu-20261107-*.json`: individual recorder measurements, hashes and functional results
- `outputs/cpu-shortspin-20261107/`: staged binaries, component checks and pacing comparison
- `outputs/cpu-query-20261107/`: rejected isolated query experiment
- `outputs/cpu-copywait-20261107/`: rejected timer-backoff experiment with failing synthetic control


## Final full-recorder results

| Run | Peak resident MiB | Peak private commit MiB | Average machine CPU | Clips decoded |
|---|---:|---:|---:|---:|
| cpu-20261107-baseline-clean | 88.25 | 121.63 | 1.05% | 3 |
| cpu-20261107-original-repeat | 90.48 | 121.54 | 1.08% | 3 |
| cpu-20261107-ring-candidate | 89.38 | 122.33 | 1.05% | 3 |
| cpu-20261107-ring-reverse | 90.65 | 122.24 | 1.07% | 3 |
| cpu-20261107-ring-ten-restarts | 95.95 | 130.70 | 1.01% | 10 |

Installed changes: audio PCM block copies only. Frame pacing and video DLL remain exactly as before.

Native recorder SHA256: 04B83FB4719206B189D3E59A48CA5E4FA635F5C6D2D0F4E3793F46C72CA1937A
Video bridge SHA256 (unchanged): 9602FF82B244EE8D39E200D4B5439EB2F6A83BFA665D1537B3C2614D030C4037
Rollback: C:\Users\szajf\My Projects\ZenithClipper\build\cpu-audio-rollback-20261107-064003

Actual installed smoke: one start/save/pause cycle passed, all streams strictly decoded, peak resident75.796875MiB. This shorter run is not directly comparable to the ten-cycle95.953125MiB peak. User's existing paused recorder was gracefully restarted into the updated build with settings unchanged.

