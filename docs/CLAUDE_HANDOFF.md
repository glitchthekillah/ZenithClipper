# ZenithClipper — Claude handoff

User chose **ZenithClipper**. Home: `C:\Users\szajf\My Projects\ZenithClipper`. Other user projects have moved under `C:\Users\szajf\My Projects` too. This app is independent of GamePal/ZenithLink and Zenith.

## 2026-11-04 minimum-overhead update (supersedes earlier acceptance)

Final optimized source uses native C++/WinRT ownership, bounded replay-index cache, recorder-self audio exclusion and prompt IDR/audio checkpoints so saving includes the shortcut moment. Final three-cycle nativeAOT peak89.09MiB/private118.59MiB; earlier ten-cycle profiling peak99.53MiB. Below50MiB is NOT achieved; no zero-FPS-loss claim. Full notes: docs/native-memory/MINIMUM-OVERHEAD-VERIFICATION.md. Benchmarks sit beside the app as BENCHMARKS.md/.csv. Rejected event synchronization/heap purging are not in production. Preserve original GPU completion and native FPS/QP20; no quality cuts to improve counters.

## 2026-11-04 native recorder memory update

The installed Windows/NVIDIA backend now uses `src/ZenithClipper.Recorder.Native` (NativeAOT), `Native.Video` (WGC/direct NVENC), `Native.Audio` (generated COM process loopback/FDK), and `Media` (disk-backed fMP4). Current normal-daemon tests including IPC/hotkeys/save peak **88.19 MiB resident**, with a 120-second run at **81.04 MiB**. These are protected/idle desktop measurements, not fresh 165 FPS gameplay acceptance or total private-commit/GPU budgets. Read `docs/native-memory/VERIFICATION-20261104.md` before making performance claims. Capture is left off. Launcher picks native on supported Windows/NVIDIA; Linux and other backends retain legacy, with no sub-100 MiB claim. Keep editor dependencies out of recorder.
## 2026-11-04 visual redesign

The user rejected the plain foundation UI. The editor now has the actual charcoal/ice-blue workspace: compact branded header and Export, real searchable media rows and import states, framed preview with transport, grouped timeline tools/track icons, and persistent inspector sections. Timing inputs default to seconds, with optional exact frame numbers. Controls have disabled/focus/press states and vector icons. Styling adds no animation timer, web view, native media process or recorder dependency.

Presentation source: `EditorChrome.cs`, `EditorTheme.cs`, `EditorInspector.cs`; behavior remains in the existing editor/model/MLT files. Preserve existing menus/commands, captured unit conversions, cached per-frame FPS and synchronized scroll viewports when changing the layout. The earlier unstyled screenshots are superseded by `outputs/editor-redesign/`.

Verification: see `docs/editor-rebuild/VISUAL-REDESIGN-VERIFICATION.md` and `outputs/editor-redesign/verification.log`. Actual headless controls were rendered empty/populated at 1000x650,1440x920,1920x1080 and 150% raster DPI, and independently visually inspected. Tests include bottom-track scroll alignment, inspector expansion persistence, pending timing-edit conversion, search empty state, fractional clock, transport and native-backend isolation. Headless rendering is narrower than real-device/Linux desktop acceptance.
## Current MLT editor rebuild (2026-11-03)

The user approved MLT and excluded Firefly/Adobe Stock/Frame.io. The new editor is `src/ZenithClipper.Editor.Desktop` (Avalonia), backed by `src/ZenithClipper.Editing` and `src/ZenithClipper.Mlt`. **Beautify this interface**, not the legacy `EditorForm.cs` trimmer. Launch `Open Editor.cmd`; the panel scissors and `zenithclipper editor [file]` redirect to `build/editor/ZenithClipper.Editor.exe`. Linux uses `build/editor-linux/ZenithClipper.Editor` with system MLT. Keep all editor dependencies out of Core/App/recorder.

Implemented scoped foundations: multitrack timeline, native audio/video preview, frame-based edits, source-preserving project storage/recovery, undo/redo, transforms, opacity/gain, basic color/blur and numeric effect keyframes, editable PNG titles, SRT captions, H.264/AAC CPU export with cancellation. First-video native format adoption preserves rational FPS; bounded preview does not change export format. Full Premiere parity is **not delivered**: consult `docs/editor-rebuild/CAPABILITIES.md` and its machine-readable ledger (111 normalized targets, not an exhaustive Adobe article audit). Do not relabel partial workflows complete.

Windows packages are siblings: `build/editor` depends on `../windows/runtime` and FFprobe. Build with `scripts/build-editor.ps1`; verify with `scripts/test-editor.ps1` (own-process 45-second watchdog, tiny generated media, dummy audio, no desktop capture). Linux cross-build is not proof of Linux desktop execution. No server was accessed during this rebuild. The recorder remains paused after tests.

The following historical editor descriptions describe the earlier trimmer and are superseded by this section. Historical capture verification remains useful, with newer entries taking precedence.
## Current shortcut controls (2026-11-03)

User defaults: **Alt+C** saves the replay, **Shift+~** opens/closes the clipping controls. **Shift+backtick** is an accepted alias for the same key. Windows: **Settings → Shortcuts → click a shortcut → press a combination → Apply**. Linux controls now expose **Keyboard shortcuts**, using text-entry dialogs for both values. These are global recorder/control shortcuts, separate from editor-local timeline bindings.

Fresh parent-reported verification: Core **39/39**, WinForms **8 checks** including arbitrary shortcut capture/settings persistence, and a real Win32 registration/unregistration roundtrip for Alt+C/Shift+~ without the capture engine. Windows App/panel and Linux CLI were republished. Linux graphical-session shortcut execution remains untested. Preserve the current portable `AutoStartCapture=false`; do not start recording merely to test shortcut controls.

Final editor evidence includes **18 bundled native checks passing, exit 0, MLT 7.41.0**, including resolved one-FPS WAV/AAC PCM and exact one-frame export with nonzero one-second audio. Final published-apphost populated headless checks passed, including odd-size rejection then 165 FPS native adoption. Windows App/panel/editor and Linux CLI/editor were republished; cached preview FPS removes per-frame project cloning. Registered-key WM_APP capture passed unit/control checks; no actual input injection or recording was performed. Read `docs/editor-rebuild/VERIFICATION.md`; do not infer full visible desktop, real-audio-device, Linux or Premiere parity from headless/generated-media checks.
## Preserve

- Lowest background overhead is the main product priority. Do not add a persistent browser, editor, preview, social/feed/cloud service or account system.
- Engine: `src/ZenithClipper.App`; reusable logic: `src/ZenithClipper.Core`. It supervises hardware capture, bounded compressed disk segments, hotkeys, native app-audio filtering, one mixed audio track and user-only IPC.
- Controls: `src/ZenithClipper.Windows/PanelForm.cs`; editor: `EditorForm.cs`. They are separate processes launched only when needed. `Program.cs` handles panel/editor single instances and headless startup.
- UI can be rewritten. Use `docs/IPC.md`; keep UI dependencies out of the core/daemon. The engine must remain able to capture with both UI processes exited.
- Default exclusion is Spotify. Other excluded process names are supported. Do not change the user's listening volume to implement exclusion.
- Never silently fall back from hardware to CPU video encoding. Surface capture/audio/export failures. Preserve originals on export. Keep storage and queues bounded.

## What exists

On-demand controls, native monitor capture, auto hardware selection, native-size/default-refresh settings, lower resolution/FPS/quality controls, seconds-to-hours compressed buffer with a disk cap, save shortcut, pause/resume, process audio exclusion before mixing, clip library, external playback preview, trims/crop/scale/mute/volume/speed export and Windows cancellation, atomic settings, error log, portable Windows build, startup/uninstall-startup scripts, Linux source/adapters, tests and CI configuration.

## Known limits to respect in the UI

- Live Windows capture and controlled exclusion have been exercised. Linux core/media/daemon IPC passed on the user's Ubuntu server, with all temporary test files removed. Actual Linux desktop capture is **not verified**; X11/Wayland paths need a desktop test, not just visual polish.
- Fast saves include a finite snapshot of the current segment and retain a preceding keyframe. An incomplete/unprobeable tail falls back to closed segments; precise trim is an editor re-encode. One-second saves have actual video and can include an extra GOP.
- SDR monitor capture; no game-only video allowlist, exclusive-fullscreen guarantee, HDR pipeline, microphone capture, captions, join/compositing timeline or embedded player yet.
- App audio names match executable names, not browser tabs. Safe sibling branches are used when an excluded child shares a captured parent; the parent's own audio may be omitted. A newly detected unsafe tree resets the buffer and save rechecks eligibility. An excluded child that starts and exits entirely between one-second scans remains a known edge case; do not market privacy guarantees.
- AMD auto mode now uses the successfully tested FFmpeg 9 `vsrc_amf`/`vpp_amf` GPU path. The earlier ddagrab-to-AMF direct path and D3D11 conversion failed on this driver; the compatibility override still downloads frames. Intel and resized NVIDIA compatibility paths also transfer frames through CPU memory. Do not claim universal zero-copy.
- The Windows publish apphosts select a bundled local .NET 10.0.14 runtime. Do not remove `build/windows/runtime` without retesting cold starts against the replacement runtime. The user's installed older runtime had inconsistent standalone behavior during development.
- Linux routing adds a playback loopback; read `docs/LINUX.md`, especially cleanup and consent requirements.
- Shortcuts conflicting with other apps are logged; a redesigned UI should make those diagnostics easier to discover.
- Startup script is provided, but persistent recording was not enabled on the user's PC as part of development tests.

## Read before changes

`README.md`, `docs/DESIGN.md`, `docs/VERIFICATION.md`, `docs/LINUX.md`, `docs/IPC.md`. The brief is functional and intentionally visually plain. Do not present missing functionality as completed by changing labels.

## Verify after changes

Run `scripts/dev.ps1 test`, package script, FFmpeg integration tests; open/close both UI processes and confirm the engine keeps recording. Check Save, pause/resume, restart rules, settings errors, export cancel/failure, file picker cancellation and existing export paths. For a Linux release, complete the real desktop checklist.

## UI pass (Claude, Nov 2026)

Panel and editor rebuilt as native custom-drawn WinForms; see `docs/UI.md`. Engine change: non-capture settings (clip length, history, disk limit, folder, shortcuts) now apply without restarting capture (`SettingsChanges.RequiresRestart`, covered by two new core tests).

## 2026-11-03 AMD startup and Quit fix

Reproduced native AMD capture/conversion SubmitInput error 4 at 2560x1600 / 165 Hz on the Radeon 780M hybrid laptop. Removed the unnecessary native-resolution AMF converter, reduced raw PCM probing, normalized Windows frame timestamps and requested CFR encoding. A known AMD invalid-surface startup failure gets one Desktop Duplication fallback while keeping hardware AMF encoding, with an explicit status notice. Other failures remain stopped; there is no continuous retry loop. Startup has a 30-second cancellation deadline.

Quit no longer waits for the mutation semaphore held by Resume. Shutdown cancels pending capture startup. A regression that timed out before this change now passes.

Fresh final checks: 33/33 core tests; 16/16 media/IPC integrations; both Windows form construction/layout tests; failed-start audio cleanup in about one second; two native-refresh capture/save/strict-decode/stop cycles with Spotify excluded, averaging 165.06 and 165.71 FPS. One cycle exercised the automatic compatibility fallback. Test videos were removed.

A five-second desktop-only measurement of the packaged compatible backend at native 165 Hz used approximately 5.9% total CPU across 16 logical processors and 507 MiB combined working set (including shared pages). This is materially higher than the earlier 60 FPS native-path sample; it is not a gaming benchmark. GPU load and gameplay impact remain unmeasured. Raw data: benchmark-windows.json.

This PC is explicitly configured to desktop-duplication. Automatic capture remains disabled after recovery; open the panel and enable recording manually. The compatible path uses an extra frame copy. All test capture/engine processes were checked after cleanup. No server access or server changes were made during this fix.

### Reduced-load recovery testing
After the user reported severe desktop lag during testing, routine --recovery tests were changed to 1280-wide / 30 FPS with Desktop Duplication; full-refresh testing requires --native-recovery. Strict video decode tests now use one decoder thread and one filter thread. This reduces test contention; unrestricted decoding was a possible contributor to the reported lag, not a proven sole cause. The reduced-load recovery test passed failed-start cleanup and two 1280x800 capture/save/decode/stop cycles (30.00 and 29.94 FPS). The test was run below normal CPU priority with an external 45-second watchdog.

High-refresh desktop responsiveness remains an unresolved performance gate. Successful capture/decode tests do not certify the original ultra-lightweight performance requirement. Automatic recording remains off on this PC.


## Current repair status
See REPAIR_REPORT.md for the final NVIDIA detection/hybrid-GPU fix, cancellation/privacy repairs, fresh verification and resource measurements. This supersedes intermediate AMD-only and unresolved-startup notes above. Native 165 FPS capture passes; gameplay and Linux graphical-session certification remain separate release gates.




## 2026-11-04 performance and import follow-up

Latest evidence and limitations: [PERFORMANCE_IMPORT_REPAIR.md](PERFORMANCE_IMPORT_REPAIR.md). This supersedes the older plain-UI and encoder-baseline statements below.


## 2026-11-04 recorder memory second pass

Latest recorder changes/evidence: [MEMORY_SECOND_PASS.md](MEMORY_SECOND_PASS.md). Native direct source graph and one-frame low-delay polling measured 205.15 MiB combined in a short native-rate capture/audio/save test. Keep the graph source and audio-input index mapping together. Preserve safe allowlist exclusion; the single-exclusion whole-system experiment was rejected.





## CPU-only update — 2026-11-07
Only PcmRing block copies were retained; identical 48,056 allocated bytes per instance and 24,000-short capacity. Native video DLL/pacing are unchanged. See docs/native-memory/CPU-OPTIMIZATION-20261107.md for test results and rollback. Shorter-spin, DONOTFLUSH and timer-backoff artifacts are rejected experiments, not product implementations. Whole-recorder CPU improvement is not statistically established.

