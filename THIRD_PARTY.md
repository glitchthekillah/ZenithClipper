# Third-party components

- .NET 10 SDK/runtime: Microsoft and .NET contributors; MIT and other licenses distributed with the SDK/runtime. The SDK under tools is for local development, not loaded by the app.
- FFmpeg/FFprobe: FFmpeg contributors. Local Windows build: **9.0.2 essentials, gyan.dev**, downloaded from https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip. Source project: https://ffmpeg.org/. Its enabled libraries determine the applicable GPL/LGPL terms; inspect `ffmpeg -version` and the included package license. This Gyan build uses GPL components including libx264.
- The package script copies FFmpeg's included top-level license/readme files to `build/windows/licenses/ffmpeg`. The full downloaded archive remains under tools. Before public distribution, retain notices and satisfy the actual build's source/code-offer obligations; this private development handoff is not a public release package.
- Optional Linux system dependencies: libX11, PulseAudio/PipeWire, Python/PyGObject, GLib/GIO/GStreamer, zenity and desktop portals, each under its own distribution licenses. They are not vendored into this repository.

The legacy recorder and editor tools run FFmpeg as a separate process. The native Windows/NVIDIA recorder uses the native components below. Microsoft process-loopback documentation informed the independently written interop definitions: https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/.

## Separate MLT editor

The new on-demand editor uses Avalonia 12.1.5 and its restored SkiaSharp/HarfBuzz/native dependencies. It uses MLT and SDL2 for composition and preview audio. These dependencies are confined to the editor package; the recorder does not load them.

The private Windows development package extracts native runtime libraries/plugins/data from official Shotcut v26.9.27 portable Windows distribution: https://github.com/mltframework/shotcut/releases/tag/v26.9.27 . Shotcut's application/UI executables are not included in the editor package. Original runtime license files are retained under build/editor/licenses/mlt-runtime. MLT's core license does not describe every plugin or codec in that build. See docs/editor-rebuild/THIRD_PARTY_DRAFT.md for the observed inventory, source provenance and outstanding distribution notice work. This package is a private development handoff, not a public-release licensing audit.
## Native Windows/NVIDIA recorder

- NVIDIA NVENC API header (`src/ZenithClipper.Native.Video/native/nvEncodeAPI.h`): upstream copyright and MIT license are preserved in the header, also copied into `build/windows/licenses/nvenc`. The shim implementation is project source. The NVIDIA display/encoder driver is supplied by the installed system, not redistributed.
- Windows Graphics Capture, Direct3D, process-loopback audio and the Media Foundation AAC encoder are system APIs. Audio is encoded with Windows' own AAC encoder (Media Foundation), so no third-party AAC encoder is shipped. This package does not redistribute Windows components.
