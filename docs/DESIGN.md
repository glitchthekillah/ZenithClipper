# ZenithClipper functional base

Approved intent: a very lightweight Windows/Linux replay recorder, capturing by default, saving seconds through hours on a hotkey. Exclude unwanted app audio before capture into one mixed track. A shortcut panel and separate editor run only when requested. All project files live under My Projects/ZenithClipper. The user selected the name ZenithClipper.

Architecture: a small .NET engine supervises an FFmpeg hardware encoder and bounded disk segments. No Electron, browser, account, upload, telemetry, or persistent editor. Windows uses Desktop Duplication and native process-loopback audio; Linux uses X11/FFmpeg or an optional Wayland portal helper and PulseAudio-compatible routing. Platform limitations must be visible, never silently capture excluded audio. Native resolution and display refresh are defaults, not a promise of lossless compression or matching a game's variable frame delivery.

Alternatives: embed a complete OBS runtime (larger dependency surface); implement all encoders/muxers directly (high correctness risk); supervise FFmpeg (selected for a smaller maintainable base and established codecs). UI: native Windows Forms on demand; Linux zenity on demand. Other clients can replace these without changing the engine protocol.

Resource policy: hardware encoding first; software encoding only explicitly selected; no live preview in background; bounded queues and disk; source refresh default with configurable FPS; compressed segments, never a raw-frame replay buffer. Exact timing and visual effects may require editor re-encoding. Main capture is SDR monitor capture; HDR, protected content, exclusive fullscreen and multi-GPU require explicit compatibility testing.

The core is built and tested on Windows here. Linux source/build and checks are supplied, but Linux capture cannot be claimed verified without a Linux desktop. Ship no fabricated performance targets: measure CPU, working set, GPU encode/3D, frame times, output FPS, disk writes, audio sync and exclusion.
