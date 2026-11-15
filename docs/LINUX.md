# Linux setup and validation

Status: core/media tests and separate Linux daemon/client IPC passed on an Ubuntu 26.04.1 server. No Linux desktop was available to validate capture. Do not advertise Linux desktop production readiness until the checklist below passes. See VERIFICATION.md for server cleanup evidence.

## Dependencies

- .NET 10 runtime (SDK for building), FFmpeg and FFprobe on PATH.
- `python3` for the startup-only parent-death helper; it execs FFmpeg and does not stay as an extra interpreter for X11 capture.
- `pactl` from PulseAudio utilities, working with PulseAudio or PipeWire's PulseAudio compatibility server.
- `zenity` for on-demand dialogs; `xdg-open` for file/player opening.
- X11: libX11, `xrandr`, FFmpeg built with x11grab and PulseAudio input.
- Wayland additionally: xdg-desktop-portal plus a matching compositor backend, python3-gi, GStreamer introspection, PipeWire source, H.264 encoder/parser and Matroska muxer plugins. On Debian/Ubuntu the relevant package families are python3-gi, gir1.2-gstreamer-1.0, gstreamer1.0-pipewire, gstreamer1.0-plugins-base/good/bad, gstreamer1.0-va (Intel/AMD) or an NVENC-capable plugin build (NVIDIA).

Build: `bash scripts/build-linux.sh`. Launch: `bash linux/launch.sh panel`. CLI examples:

```bash
build/linux/zenithclipper init
build/linux/zenithclipper doctor
build/linux/zenithclipper daemon
build/linux/zenithclipper save 30
build/linux/zenithclipper pause
build/linux/zenithclipper resume
build/linux/zenithclipper shutdown
```

The Linux build is framework-dependent: a system .NET 10 runtime is needed. Auto-start is optional: `bash linux/install-startup.sh`, remove using `--remove`.

## Display configuration

For X11, set `LinuxDisplay` (normally the actual `$DISPLAY`) and `LinuxVideoSize` to the monitor rectangle, plus `Fps` or leave it 0 for xrandr detection. Default source is `:0.0`, size `1920x1080`; inspect and configure these before recording a different display. Monitor index is a Windows setting. Offset can be specified in the X11 display string, e.g. `:0.0+1920,0`.

Wayland uses the system screen-sharing portal and must respect user consent. Select the monitor in the portal. Set FPS explicitly. It uses a GStreamer hardware encoder and sends already encoded H.264 to the shared segment writer. Portal cancellation, missing plugins, or an unsupported GPU must produce a visible failure, not a fake active state. Restore tokens, unattended consent persistence, HDR, GNOME/KDE compositor compatibility and DMA-BUF zero-copy are not verified. The helper currently converts raw frames and cannot be claimed the optimal GPU path.

Global shortcuts on Wayland belong to the compositor: bind `zenithclipper save` and `zenithclipper panel` to your chosen keys in desktop settings. The app does not bypass this restriction. X11 registers the configured shortcuts through libX11.

## Audio routing

The Linux adapter creates a dedicated null sink, loops its monitor back to the current playback sink, and moves eligible output streams into the recording sink. Excluded streams stay on their original sink. FFmpeg records the dedicated sink monitor. This introduces a requested loopback latency of about 20 ms for included playback; it is a compatibility approach, not the same mechanism as Windows process loopback. Volume is not forced to 100%; playback levels are preserved.

The adapter polls for new streams about once a second, avoids its own loopback stream, and restores moved streams/unloads its modules on normal shutdown. Abrupt termination may leave temporary sinks/modules in the sound server. Inspect `pactl list short modules` and `pactl list short sinks`; restore application outputs to your normal sink in the desktop mixer and unload only modules belonging to ZenithClipper. Crash-safe automatic restoration still requires real PulseAudio/PipeWire testing.

## Release checklist

- X11: Ubuntu/GNOME Xorg and one other desktop; actual monitor dimensions and 60/144 FPS.
- Wayland: GNOME and KDE portal consent, cancel, stop sharing, restart, logout/login.
- PipeWire/PulseAudio: included game + excluded Spotify simultaneously, app restart, multiple exclusions, device switch, disconnect and engine crash cleanup.
- NVENC/VAAPI/QSV: encoder initialization, decode saved clip, audio sync over an hour, sustained memory/disk limits.
- Hotkeys and panel/editor termination; compositor shortcuts under Wayland.
- Suspend/resume and fullscreen games; user-visible failure and recovery.

## Custom shortcuts
The Linux controls menu includes **Keyboard shortcuts**. Edit the Save clip and Clipping menu bindings there; both are validated and saved together. Defaults are Alt+C and Shift+~; Shift+backtick is the same binding. X11 uses the grave key symbol for this key. On Wayland, configure compositor/desktop global shortcuts to `zenithclipper save` and `zenithclipper panel`; the compositor owns global key registration.
