# Cross-platform editor foundation

This code-only Avalonia desktop app uses the shared `ZenithClipper.Editing` project model and `ZenithClipper.Mlt` playback/export engine. It does not reference the Windows recording/panel/editor project.

Open a saved project with `--project path.zenith`, or pass a media path to import it into a new project. `ZENITH_FFPROBE` can specify an ffprobe executable; otherwise the app checks its ancestors for the bundled executable and falls back to PATH.

Import media, double-click a library item to add it at the playhead, and drag clips between compatible unlocked tracks. The inspector edits frame-based timing, source in, playback speed, reverse, gain, opacity, transform, brightness, gamma and box blur. Speed changes preserve the amount of source used. Tracks can be muted, hidden or locked. Ripple delete closes the gap on the selected track.

File → Create title creates transparent PNG text assets on an overlay track. Import SRT captions creates editable text assets at their cue times; Export SRT captions writes the current Captions track timing and text. Text assets use bounded raster sizes and unique files in a project asset folder (or the editor's asset cache for an unsaved project). Select a text clip and use Edit title / caption text to replace its text. Position and scale remain editable in the inspector. These are basic raster titles and timed captions, without transcription or a full motion graphics authoring system.

Brightness, gamma and blur have clip-local keyframes with linear, discrete and smooth interpolation. Put the playhead inside a selected clip, enter a parameter value and choose Add keyframe here. Video fade buttons add real alpha keyframes. The first video imported into an untouched new sequence sets its native dimensions and rational frame rate; manually chosen and saved sequence formats are preserved.

Shortcuts: Space play/pause; S or Ctrl/Cmd+K split; Delete remove; Shift+Delete ripple delete; arrows seek one frame; Shift+arrows seek 30 frames; Ctrl/Cmd+Z undo; Ctrl/Cmd+Shift+Z or Ctrl/Cmd+Y redo; Ctrl/Cmd+S save; Ctrl/Cmd+Shift+S save as.

Projects autosave every 30 seconds while dirty. Recover the current project's autosave from File → Recover autosave. Missing media can be relinked when opening a project. Export currently requests MP4 through the MLT engine and displays progress with cancellation.

`--headless-smoke` constructs, populates and lays out the actual editor controls using Avalonia.Headless with software Skia rendering. It also verifies title PNG glyph/transparent pixels and native frame-rate metadata behavior. It opens no visible window and does not load native media playback. Advanced professional editing parity is tracked separately; this project does not claim complete Premiere parity.

## Packaging and verification

On Windows, first package the recorder's .NET 10 runtime into `build/windows/runtime` using the existing Windows packaging workflow. With the portable MLT runtime extracted in `tools/mlt-runtime/package/Shotcut`, run from the repository root:

```powershell
pwsh -File scripts/build-editor.ps1
pwsh -File scripts/test-editor.ps1
```

The editor is published to `build/editor/ZenithClipper.Editor.exe` as a framework-dependent win-x64 apphost. It explicitly locates .NET through `../windows/runtime`; keep the `editor` and `windows` folders beside one another. Media import can use `build/windows/ffprobe.exe`, or the `ZENITH_FFPROBE` override. MLT DLLs, media plugins, data and original license notices are copied under `build/editor/mlt` and `build/editor/licenses`. Shotcut executables and its Qt/QML UI are excluded. Existing output and user data are not deleted.

The PowerShell 7 test script runs domain tests, the published headless apphost smoke when available, and one native MLT fixture suite at a time. Each child process has a watchdog of at most 45 seconds and only its own process tree can be terminated. SDL uses dummy video/audio drivers, and no desktop capture is performed. `-SkipNative` skips native fixtures; `-SkipBuild` uses existing Release binaries. Run a fresh build before using `-SkipBuild` after source changes.

To render a layout preview, add `-SnapshotPath` with an absolute PNG path to the test script, or run the editor with `--headless-smoke --snapshot <absolute.png>`. This renders a 1440 × 920 virtual headless window through software Skia; it does not open or capture the user's desktop.

The redesigned workspace uses seconds for timing fields by default. **Show frame numbers** switches the three timing fields to precise frame inputs without changing the edit. Transform values use percentages, and the inspector remembers expanded sections while editing.

Headless snapshots also accept `--width 1000 --height 650`, `--empty`, and `--scale 1.5`. `scripts/render-editor-previews.ps1` renders empty/populated layouts at 1000 × 650, 1440 × 920 and 1920 × 1080, plus a 150% software raster DPI image. It logs the semantic inspector, timing-unit, search, transport, native-isolation and bottom-track alignment checks under `outputs/editor-redesign/verification.log`. The 150% render checks scaled output; it does not claim a real operating-system DPI or Linux desktop session test.

On a Linux machine with the .NET 10 SDK installed:

```bash
bash scripts/build-editor.sh                  # linux-x64
bash scripts/build-editor.sh linux-arm64      # optional alternate architecture
```

This publishes to `build/editor-linux`. Install the .NET 10 runtime, system MLT 7 and its core/avformat/plus/frei0r plugins/data, SDL2, FFmpeg/ffprobe, and Avalonia's native display/font dependencies through the Linux distribution. The Linux package loads system `libmlt-7.so.7` and `libSDL2-2.0.so.0`; it bundles no Windows native libraries. A real Linux desktop/audio session has not been tested by the Windows build process.

## Visual workspace
The charcoal/ice-blue workspace separates media, preview/transport, clip properties and timeline tools. The inspector groups Timing, Transform, Sound/opacity and Color/effects, preserving expansion state across edits. Timing is shown in seconds by default; Show frame numbers switches display without modifying the project. Search filters the real media library. Empty Export/playback actions are disabled. `EditorChrome.cs`, `EditorTheme.cs` and `EditorInspector.cs` own presentation. See docs/editor-rebuild/VISUAL-REDESIGN-VERIFICATION.md and outputs/editor-redesign for the rendered matrix.
