# ZenithClipper UI

Both windows are native Windows Forms with a small custom-drawn kit (`src/ZenithClipper.Windows/Ui`). No browser engine, WebView, NuGet package or designer files. Each runs as its own process only while open; the engine never loads UI code.

## Controls panel (Shift+~)

Game Bar style overlay: borderless, top-most, opens centred on the monitor under the cursor. Esc, the close button or pressing the panel shortcut again closes it (a second launch posts WM_CLOSE to the open panel).

- **Instant replay**: one large Save button, clip length chips (10s to 1h, plus custom), live buffer meter (time ready, disk used against the limit), background recording switch (pause/resume).
- **Audio in clips**: apps with an audio session on the default output, plus excluded apps. Click a row to include/exclude; add any process by name. Changes are staged and applied with one button because they restart capture.
- **Recent clips**: newest four with cached thumbnails; click opens the editor on that clip.
- **Settings** (gear): replay history, disk limit, shortcut length, resolution, frame rate, quality, encoder, display, capture method, app audio, exclusions, press-to-record shortcut fields, clip folder, log, quit. The apply bar says up front whether the change keeps the buffer or restarts capture.

## Editor

Clipchamp style layout, launched from the panel or `Open Editor.cmd`. A second `editor <file>` launch forwards the file to the open window (WM_COPYDATA).

- **Library**: thumbnails, duration, resolution/fps, search, right-click open/show/rename/recycle. Drop any video file on the window to open it.
- **Preview**: frame-accurate scrubbing and silent real-time playback (FFmpeg decodes scaled BGRA frames on a pipe; newest frame wins, so a slow repaint drops frames instead of queueing). "Play with sound" opens the default player.
- **Timeline**: ruler, 16-frame filmstrip, audio waveform, draggable in/out handles, playhead.
- **Inspector**: trim (typed times or I/O keys), keyframe-fast toggle (auto-disabled when an edit needs a re-encode), aspect crop presets with drag-to-position in the preview (16:9, 9:16, 1:1, 4:5, 21:9), output size, speed, mute/volume, export summary, export with cancel.

Keys: Space play/pause, I/O set in/out, Left/Right one frame, Shift+Left/Right one second, Home/End jump to in/out, Ctrl+E export.

Thumbnail/probe cache: `<data>/cache` (keyed by path, size and modified time; safe to delete).

## Engine change made for the UI

`SettingsChanges.RequiresRestart` lets `configure` apply clip length, history length, disk limit, clip folder, shortcuts and auto-start live, keeping the replay buffer. Capture, encoder, FPS, resolution, monitor, backend and audio changes still restart capture as before.

## Not done

Audio during in-editor playback, timeline zoom, multi-clip joining, captions/text, per-app volume, export progress percentage (export shows an indeterminate bar).

## Colour identity
ZenithClipper uses neutral charcoal surfaces and ice-blue highlights, including the brand orb and timeline. Red indicates recording, green success and amber warnings. This replaces the shared Zenith amber/pink and purple palette.
> Current MLT editor rebuild: the editor is now `src/ZenithClipper.Editor.Desktop`, launched from `build/editor`. Editor descriptions below describe the earlier WinForms trimmer. See the Desktop README for current functionality; preserve the panel shortcut-capture HWND marker when changing its UI.


Preview decoding is capped at 30 FPS with one decoder/filter thread and one thumbnail worker. Export/capture FPS follows user settings. Corrupted metadata caches regenerate; cancelled helper tasks await child exit.

