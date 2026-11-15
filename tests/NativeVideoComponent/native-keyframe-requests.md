# Prompt native replay checkpoints

`NativeVideoRecorder.RequestKeyFrame()` is a nonblocking request for an IDR on the next frame accepted by the encoder worker. It performs atomic state checks, takes a SafeHandle lease and ORs control mask 2. It does not invoke the GPU or wait on the lifecycle lock. Calls before startup, after stopping, or after disposal fail clearly.

The shared native state remains 64 bytes and all existing native function signatures are unchanged. Its former test-release field is now `controlFlags`: mask 1 releases the non-GPU timeout harness, and mask 2 requests a keyframe. The worker atomically clears only mask 2 immediately before encoding. The new worker-only `nc_request_idr` sets an encoder-owned boolean; the next accepted picture consumes it and uses the existing FORCEIDR / OUTPUT_SPSPPS flags. The automatic two-second force cadence, CQP, FPS, NV12 conversion, resizing, original GPU query synchronization, timestamps and packet callback ABI remain unchanged.

This method requests a boundary; it does not guarantee that the fragment has already committed when it returns. The save caller must wait for its replay writer's completed-frame checkpoint to reach the frame recorded at the hotkey, watching cancellation and capture failure. That integration belongs to `NativeRecorder.SaveAsync`.

## Validation

New tests first failed compilation against the prior API. The AOT non-GPU `--keyframe-tests` now passes: fake frame 20 requests IDR21, frame21 requests IDR22; keyframe bit2 does not release timeout quarantine; fake-release bit1 preserves bit2; and twenty concurrent request/disposal trials pass. Unstarted/stopped/disposed requests reject. All nine existing native lifetime/fault groups still pass.

The actual `--keyframe-live` test ran native 2560x1600 at165 FPS, CQP20, NV12. It issued requests inside packet callbacks at250 and330. The encoder reported IDRs at exactly0,251,330,331 across381 contiguous packets. Independent ffprobe frame parsing found the same IDRs; all381 frames strictly software-decoded with an empty error log. The330→331 interval is a real one-frame GOP and is available as a fixture for the replay writer's tiny-fragment/audio tests.

Evidence: `tests/NativeVideoComponent/keyframe-staging-aot/keyframe-live.log`, `keyframe-request.h264`, `keyframe.decode.err`. The test executable also exposes `--keyframe-tests` for non-GPU checks.

Frozen DLL: `tests/NativeVideoComponent/wgc-validated-keyframe/nvenc_shim.dll`.

SHA256: `343FAEBC73D95DAF840D089AFDF60AC8081543BC0FA2BECBFE258B6A663C362D`.

The source project's default `native/nvenc_shim.dll` has been updated to the same tested binary. The installed application package has not been replaced by this component task.
