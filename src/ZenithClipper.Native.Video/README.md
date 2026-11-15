# Native video component

AOT-compatible Windows library for WGC capture on an automatically selected NVIDIA D3D11 device and direct synchronous NVENC H.264 output. Build `build-native.ps1` once with the repository-local MSVC/SDK toolchain, then build/publish the consumer normally. The native DLL is copied transitively to build and publish outputs.

```csharp
using ZenithClipper.Native.Video;

using var recorder = new NativeVideoRecorder(
    new NativeVideoConfiguration { MonitorIndex = 0, FrameRate = 0, Quantizer = 20 },
    (ReadOnlySpan<byte> annexB, long frameIndex, bool keyFrame) =>
    {
        // Consume synchronously or copy encoded bytes before returning.
        muxer.WriteVideo(annexB, frameIndex, keyFrame);
    });

// These are resolved from display metadata before Start, so create the muxer first.
int width = recorder.Width, height = recorder.Height, fps = recorder.FrameRate;
recorder.CaptureFault += error => ReportCaptureFault(error);
recorder.Start();
// Later: Stop joins the capture worker and flushes the encoder.
recorder.Stop();
```

`VideoPacketHandler` receives one Annex B access unit. Its span remains valid only inside the callback while NVENC's output buffer is locked. Packet frame indices start at zero, with a constant time base of `1 / recorder.FrameRate`. IDR frames are forced every two seconds, with SPS/PPS repeated. The callback runs on the capture worker and must not retain the span or block on work that needs that same worker. Slow callbacks introduce encoding/pacing delay rather than an unbounded queue.

Managed callback exceptions are caught before returning through the C ABI. The worker then stores the original exception in `Fault`, raises `CaptureFault`, and cleans up. Event-handler exceptions are contained. `Stop` can be called from a packet/fault callback; in that case it requests stop without joining itself. `Start` initializes a dedicated MTA worker and waits up to 15 seconds. Each instance can start only once. `Stop` waits up to ten seconds; a stuck native driver requires the enclosing daemon's process watchdog because .NET cannot safely abort an in-progress driver call.

`NativeVideoStatistics` reports encoded/captured/repeated frame counts, strict pacing deadline misses, encoded byte count, and elapsed capture time. If WGC does not update, the recorder safely re-encodes the last owned texture. One owned BGRA texture is reused only after blocking bitstream completion. A GPU event query completes each WGC-to-owned-texture copy before returning the borrowed capture frame. There is no CPU pixel readback or FFmpeg worker.

Configuration:

- `MonitorIndex`: zero is primary; other monitors follow native enumeration order.
- `FrameRate`: zero resolves current monitor refresh rate; otherwise 2..1000 (1 is rejected; hardware may reject unsupported rates).
- `Quantizer`: constant QP 0..51; default 20.
- `FramePoolBuffers`: 1 or 2; default 1.
- `OutputPath`: optional diagnostic raw H.264 file; null disables native file output.
- `OutputWidth`/`OutputHeight`: zero means native dimensions. Setting width alone derives an even height preserving the monitor aspect ratio; explicit dimensions are rounded down to even (minimum2). Actual resizing uses D3D11VideoProcessorBlt on the NVIDIA GPU, directly into the one owned BGRA encoder texture, with full-RGB input/output, format-capability checks, progressive frames, and automatic image enhancements disabled. The native-size path retains its direct CopyResource.

The Windows11 capture session explicitly sets MinUpdateInterval to zero: the measured default was160000 ticks (16ms), which otherwise throttles fresh captures near60Hz. This is a session property and changes no global graphics setting. The native DLL is explicitly loaded once before the GPU worker starts to avoid observed NativeAOT lazy-binding startup stalls.

There is no AMD encoder fallback in this component. A supported NVIDIA adapter is required. Display geometry changes/device loss are reported as faults; session recovery belongs to the daemon. This component handles video only and does not change display configuration, system timer resolution, or audio routing.

Validation: `tests/NativeVideoComponent` NativeAOT harness produced 841 sequential callback packets at native 2560x1600, resolved FPS0 to165, emitted three IDRs across roughly five seconds, and stopped normally. All 841 frames software-decoded without errors. The separate injected exception test failed the packet callback at index10, raised the original fault, cleaned up, and exited successfully. Sampled working set peaked at 91,070,464 bytes (86.85 MiB), excluding controller/audio/mux integration. Logs and decode evidence are under that test directory. Process working set is not interchangeable with private commit or GPU allocation.

The complete NVIDIA MIT header license remains in `native/nvEncodeAPI.h`; header source is FFmpeg nv-codec-headers n13.0.19.0. The NVIDIA driver DLL is loaded from System32 and is not bundled.

`WorkerFinishing` runs exactly once on the video OS worker, after the last packet and successful flush (or after a capture fault), before native encoder/COM teardown. Subscribe before Start to drain/close thread-affine AAC encoders and finalize the muxer there. A finishing-handler exception becomes Fault/CaptureFault when no earlier fault exists; an earlier fault is preserved and GPU cleanup continues. The component harness verified callback and finishing managed thread IDs match and the hook runs once.

Color: H.264 VUI explicitly marks BT709 limited range. Hardware-resized1280x800 output from known full-range RGB bars decoded with maximum component error2/255 at QP20, with verified tv/bt709/bt709/bt709 tags and842 valid frames. This validates actual conversion/range for that path, not metadata alone. Native-size color-bar sampling and validated full-rate moving-stimulus capture are tracked separately; encoded repeats must not be represented as fresh captured frames.

Source checkpoint before resizing is preserved at `tests/NativeVideoComponent/native-video-before-resize.zip` (SHA256 C5D4121B0DDB7CF4FDDB9D136FEAF76E6437C5C99ECF9DC039AADAE1CF3E0902). Opt-in `ZENITH_NATIVE_VIDEO_TRACE=1` reports startup stages and the initial WGC update interval to stderr; it does not log every frame.

## Optional direct NV12 input

Set `NativeVideoConfiguration.UseNv12Input = true` to replace the owned BGRA encoder texture with one owned NV12 texture. The BGRA path remains the default fallback. WGC still produces native BGRA8 frames; the hardware video processor scales/converts directly into the encoder's NV12 target, with no additional owned BGRA scratch texture or CPU pixel mapping. The NV12 path validates input/output format flags and exact color-conversion support, then uses `ID3D11VideoContext1` with `RGB_FULL_G22_NONE_P709` input and `YCBCR_STUDIO_G22_LEFT_P709` output. NVENC registers NV12 directly, bypassing its internal ARGB conversion path. CQP20, no B frames, one reference, IDR cadence, callback lifetime, and completed-GPU-copy ownership are unchanged.

Measured standalone NativeAOT NV12 component: peak working set67,796,992 bytes (64.66MiB), largest logged private commit108,244,992 bytes (103.23MiB), NVIDIA local50,176,000 bytes (47.85MiB), nonlocal8,937,472 bytes (8.52MiB). This saves roughly19–22MiB of process working set versus BGRA. It is a component measurement, not a full-controller claim. That live run captured the current protected/idle desktop and safely repeated frames; it does not prove165 fresh captures/second.

A synthetic GPU test independently cleared a BGRA source texture with16 known colors, then used the exact production CopySource/video-processor/NVENC code. Native BGRA2560x1600, native NV122560x1600, and resized NV121280x800 all encoded/strictly decoded three frames successfully. All three had maximum decoded RGB component error3/255 across primaries, secondary colors, black/white, gray64/128/192, and mixed tones. Black0 and white255 were exact. Evidence: `tests/NativeVideoComponent/synthetic-color-validation.json` and matching `synthetic-*-ffprobe.json`/decode logs. This verifies matrix/range conversion beyond VUI annotation, without relying on desktop visibility.

The validated BGRA source checkpoint before NV12 changes is `tests/NativeVideoComponent/native-video-bgra-validated.zip`.

## Live moving-stimulus limitation

The owned no-activate GDI helper genuinely rendered1254 frames in8.000813 seconds (156.734fps) in a ready-first attempt. Inspection of the captured output showed an almost-black Windows lock/idle view with small system widgets, not the owned window/user desktop. WGC delivered only four fresh frames during that capture. Therefore this is **not** a valid fresh165fps capture result; the stimulus was not visible to the capture source. No unlocking/access-control bypass or system/display setting change was attempted. An unlocked visible desktop is required to complete that live acceptance test. Session-local MinUpdateInterval=0 and one/two pool buffers are available for that test; encoded repeated frames must remain distinct from fresh captured frames in reporting.


## Prompt replay boundary

`RequestKeyFrame()` atomically requests an IDR on the next accepted encoder frame without waiting on a lifecycle lock or GPU call. It rejects unstarted/stopped/disposed recorders. The encoding worker consumes the flag; the ordinary two-second IDR cadence is preserved. Save callers must wait for the muxer completed-frame checkpoint before exporting, because the request itself does not wait for fragment/audio commitment. See `tests/NativeVideoComponent/native-keyframe-requests.md` for the verified frame250→251 and frame330→331 IDRs, one-frame-GOP fixture, race tests and staged DLL hash.
