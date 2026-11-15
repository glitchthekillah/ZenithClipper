# Native WGC ownership candidate

This candidate moves only capture graph ownership and pacing into `native/wgc_capture.cpp`, compiled together with the unchanged `nvenc_shim.cpp`. The managed API, resolved monitor/dimensions/FPS, CQP, NV12/resize path, fixed timestamps, forced two-second IDR cadence, borrowed packet contract, and dedicated OS worker remain. No C#/WinRT object is created on the capture path.

`Capture` owns its WGC device/item/pool/session, D3D references, encoder and timer with native RAII. Frames are closed before returning to the pool. Encoder flush precedes the exactly-once managed `WorkerFinishing` callback. Pool/session/device close and COM releases precede factory-cache clearing and apartment shutdown. Exceptions return ABI error status; managed callback errors return status and retain the original managed exception. A timeout retains the shared ABI state, callback handle and dependent session until the worker can join.

## Build staging

Run `src/ZenithClipper.Native.Video/build-native.ps1 -OutputDirectory <staging-directory>` using the repository's portable toolchain. Pass `-p:NativeVideoBridgePath=<absolute-staging-dll>` when building/publishing the consumer. The default property remains `native/nvenc_shim.dll`; normal packaging must first build the new bridge or explicitly pass the validated staging DLL. No installed package was replaced for these tests.

Final frozen tested DLL: `tests/NativeVideoComponent/wgc-validated-v2/nvenc_shim.dll`, SHA256 `25E62C48C589E8568D3C9B7340F0611BE48767739B62D3E37FD1BB358677FFDF`. Earlier baseline evidence used `wgc-validated-baseline` with SHA256 `1D225B69F048625E6576B1CEE1DDD0A49380A093DAC42E38D875BED55120D01D`.

The final version preserves an earlier native startup/capture/flush fault if the finishing callback also throws. A distinct internal finish-failure result identifies when the finishing exception is primary. Stop serializes only worker publication under the lifecycle lock and joins outside it, supporting callback stop and concurrent start/disposal without invalidating the worker's state lease. Statistics remain readable after disposal through a final snapshot.

## Checks

The AOT component executable accepts `--lifetime-tests` (no GPU). Its counted real-IUnknown fake owners exercise the same native owning-lifetime helper. Twenty-five repeat lifetimes, partial startup, capture/flush failure, managed packet/finishing exceptions, combined primary/secondary faults, stop timeout followed by late join, post-disposal statistics, repeated never-started disposal, and twenty concurrent start/dispose races all pass. No forced GC is used.

`--restart-test --cycles 5 --seconds 3` passed five real native 2560x1600/165 FPS/CQP20/NV12 capture cycles in one process. Each produced 496–498 packets over 3.005–3.017 seconds and two IDRs. The exactly-once finishing callback ran on the packet worker. Every raw H.264 output strictly decoded cleanly when supplied its actual 165-FPS input time base (`-r 165 -f h264 ... -fps_mode passthrough`).

`--restart-test --cycles 3 --callback-fault` passed three real fault/cleanup cycles, retaining ten valid packets per output and the original managed fault. `--nv12 --finishing-fault` passed a five-second capture followed by the expected finishing fault; its 850 frames also strictly decoded. Logs and raw outputs live under `tests/NativeVideoComponent/wgc-staging-aot`.

The final v2 repeated three normal same-process starts, three packet-fault starts and one finishing-fault capture; all passed and all seven outputs strictly decoded. The final finishing-fault output contained 846 frames. Logs are under `wgc-staging-aot-v2`.

These component checks do not prove 165 fresh desktop frames per second. Encoded repeats are reported separately. They are also not a below-50-MiB full-recorder claim. Five normal disposed samples had stable private commit around 64–65 MB, while working set warmed from 43.7 to 53.2 MB and peak reached 83.1 MB. Three rapid injected packet-fault cycles showed private commit increasing from 64.9 to 78.8 MB; driver/cache behavior still needs investigation before calling repeated fault recovery resource-stable. Kernel handles also increased across normal cycles.

## Pacer diagnostic

`ZENITH_NATIVE_PACER=deadline` opts into exact high-resolution deadline waits, removing the legacy early wake/spin. It is read once per timer creation. Default behavior remains legacy because the live comparison did not establish a whole-capture CPU gain.

`--pacer-tests` runs six interleaved no-GPU samples using the same timer: all produced 825 contiguous packets over 4.99406–4.99444 seconds. Legacy process CPU samples were 62.5, 31.25 and 46.875 ms; deadline samples were 15.625, 31.25 and 15.625 ms. Median isolated pacing CPU fell from 46.875 to 15.625 ms. Full-frame deadline misses were 0/0/0 versus 0/3/0. CPU accounting granularity and system scheduling limit precision. These are isolated timer results, not whole-recorder CPU benchmarks.

Six interleaved real five-second GPU samples are under `wgc-gpu-pacer-ab`. Identical native 2560x1600/165 FPS/CQP20/NV12 configuration produced 827–829 packets over 5.010–5.023 seconds, three IDRs each, and thirteen combined v2/A-B outputs strictly decoded with empty error logs. Legacy CPU samples (including initialization and teardown) were 937.5, 1062.5 and 1046.875 ms; deadline samples were 921.875, 1109.375 and 1062.5 ms. Median CPU was 1046.875 versus 1062.5 ms: the synthetic saving did not translate into a demonstrated capture improvement. Resolution, pixel format, BT709 limited-range tags and no-B-frame configuration remained intact.

Raw H.264 lacks container timestamps. Its ffprobe `r_frame_rate=330/1` / `avg_frame_rate=25/1` heuristic is identical in the pre-bridge, v1 and v2 elementary streams. Supply the actual 165-FPS input time base for raw decoding; validate final replay MP4 timing from the media writer's explicit frame timestamps, not this elementary-stream heuristic.

The old projected capture path's optional `ZENITH_NATIVE_VIDEO_TRACE` startup-stage messages are not emitted by this candidate. Component state/statistics and callback faults remain available; no per-frame logging was introduced.
