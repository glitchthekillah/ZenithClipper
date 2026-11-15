# ZenithClipper benchmarks
Latest CPU-only update (2026-11-07): see [CPU optimization results](docs/native-memory/CPU-OPTIMIZATION-20261107.md). Measurements below are historical November 4 results.


Updated: 2026-11-04T20:29:26.4321526-05:00

Machine: AMD Ryzen 7 8845HS w/ Radeon 780M Graphics; 32 GiB RAM; NVIDIA GeForce RTX 4060 Laptop GPU + AMD Radeon 780M Graphics.
GPU driver versions: NVIDIA GeForce RTX 4060 Laptop GPU 32.0.16.1047; AMD Radeon 780M Graphics 32.0.21030.14003.

## Same-settings desktop comparison

Back-to-back comparison of the original build and the optimized profiling build before the live-save correctness fix. One isolated recorder process includes capture, encoding, app-audio selection, IPC and registered shortcuts. Each run starts fresh, records, saves and pauses three times. Native 2560x1600, 165 FPS encoded cadence, QP20, stereo 48 kHz AAC at 192 kbps. No quality reduction or working-set trimming. The source desktop was not a deterministic game benchmark.

| Build | Peak resident RAM (MiB) | Peak private commit (MiB) | Average CPU (% of machine) | Median save response (ms) | Clips decoded | Below 50 MiB |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Previous build | 107.32 | 178.71 | 1.41 | 105.03 | 3 | No |
| Optimized profiling build | 94.07 | 122.62 | 1.11 | 105.54 | 3 | No |

Observed reductions: 12.3% peak resident RAM, 31.4% peak private commit, 21.7% average process CPU.
Desktop content and scheduling vary; small overall CPU differences are not statistically established.

The November 4 executable has since received the audio-only update linked above; the following November 4 measurements are historical.

Final installed save behavior differs from both profiling builds: it waits for a prompt keyframe/audio checkpoint covering the shortcut moment. The profiling builds could stop at an older completed fragment, omitting up to two seconds. Their faster save response is not equivalent correctness. Final installed median response was211ms.

## Other workloads

| Workload | Peak resident RAM (MiB) | Peak private commit (MiB) | Average CPU (% of machine) |
| --- | ---: | ---: | ---: |
| Final installed build: uncontrolled workload, game process present | 89.09 | 118.59 | 1.37 |
| Earlier quiet desktop | 88.19 | 167.05 | 0.89 |
| Previous build: Overwatch asset-load scene | 108.69 | 182.79 | 2.02 |
| Optimized profiling build: ten restarts with GPU counters | 99.53 | 129.84 | 1 |
| Optimized profiling build: 120 seconds | 95.21 | 124.11 | 0.89 |

The earlier quiet desktop result is not a substitute for active-game testing. The Overwatch custom mode ran in the background; no game controls or foreground change were sent. All three saved clips decoded; one had 330 decoded frames and 319 distinct reduced-frame hashes. This does not prove 165 fresh game frames per second or zero FPS loss. A new Overwatch process started at20:09 before the final installed-build test at20:17; that final workload was uncontrolled and is not an apples-to-apples desktop or intentional game benchmark. The newer game process was left untouched. No controlled optimized game/frame-time test has been performed.

## GPU measurements: optimized ten-restart run

Windows per-process counters, sampled about every two seconds: peak reported Dedicated Usage 213.29 MiB; peak reported Shared Usage 14.34 MiB. Dedicated Usage is not a validated physical-allocation figure: a separate restart test showed this counter rising while Local Usage and Total Committed stayed constant. GPU counters are separate from resident CPU RAM.

| GPU engine | Peak sampled utilization |
| --- | ---: |
| 3D | 9% |
| VideoEncode | 66% |
| Copy | 0% |

VideoEncode is the dedicated encoder engine, not overall GPU or game 3D utilization. These sampled peaks can miss shorter spikes. Save-latency results from this instrumented run are excluded because counter collection can delay the test client.

Extended-run counter cross-check: Local Usage 50.79 MiB; NonLocal Usage 14.34 MiB; Total Committed 72.77 MiB. These are different GPU accounting measures, not values to add to one another.

## Isolated replay bookkeeping

| Measurement | Before | After |
| --- | ---: | ---: |
| CPU: 12,000 video + 3,409 AAC packets and 12,000 metrics polls | 6,765.62 ms | 234.38 ms |
| Managed allocation | 26,413,208 B | 1,064,056 B |
| Repeated oldest-index reader opens | 42,818 | 0 |

Tiny synthetic payloads isolate bookkeeping; this is not an overall capture/GPU speedup. The one-record cache adds 64 bytes. Full quick Media regressions passed.

## Limits and evidence

- Below-50-MiB total resident usage has not been achieved. No universal maximum or zero game-FPS-loss guarantee is claimed.
- MiB = 1,048,576 bytes. Resident RAM includes shared executable/library pages; private commit is a separate allocation measure. GPU memory and on-demand UI/editor memory are separate.
- CPU uses process CPU time divided by test wall time and 16 logical processors; it includes startup, saves and pauses.
- Game frame-time impact, optimized active-game performance, competitors, Linux and other machines remain unmeasured.
- Rejected fence-event synchronization experiments hung in stress tests and are not in the product. Original GPU synchronization and pacing are retained.
- Raw measurements: outputs/desktop-comparison-old.json, outputs/desktop-comparison-candidate-v2.json, outputs/desktop-comparison-final.json, outputs/minimum-overhead-v2-ten-restarts.json and outputs/overwatch-active-capture.json. Native bridge and audio tests: tests/NativeVideoComponent/wgc-bridge-validation.md.
- The original game test threshold mistakenly depended on duration. Its raw counters were retained, its result corrected to fail, and the harness now uses an independent explicit memory budget.

Launch with Launch ZenithClipper.cmd. Alt+C saves; Shift+~ (same physical key as Shift+`) opens the menu. Both shortcuts are configurable. Benchmarking leaves capture off.

