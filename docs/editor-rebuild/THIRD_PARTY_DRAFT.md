# Editor third-party notice inventory draft

Status: development inventory; **package scripts exercised; final inventory/clean-machine acceptance pending**. This document records observed dependencies and gaps, not a final distribution notice or legal conclusion. Preserve the existing root `THIRD_PARTY.md` until a final package inventory is assembled.

| Component | Observed evidence | Packaging action still pending |
| --- | --- | --- |
| .NET 10 | Editor target is `net10.0`; existing root notice describes local SDK/runtime. | Record actual redistributed runtime/version and retain its notices. |
| Avalonia | Editor csproj references Avalonia, Avalonia.Desktop, Avalonia.Headless and Avalonia.Themes.Fluent **12.1.5**. | Resolve transitive runtime/native packages from locked restore assets; copy their actual license/notices into each package. Headless development reference does not prove a production need. |
| MLT | Native adapter loads `libmlt-7.dll` on Windows or `libmlt-7.so.7` on Linux. Runtime discovery recognizes the local Shotcut directory. | Observed native runtime version is **7.41.0**. Exact module/license inventory and corresponding source/notices remain incomplete. Do not label the entire assembled package LGPL based solely on framework licensing. |
| Local portable runtime source | Observed `tools/mlt-runtime/shotcut-win64-26.9.27.zip`, with extracted `package/Shotcut`. Its top-level `COPYING.txt` contains GPL version 3 text; top-level `LICENSE` contains Apache License 2.0 text. | Preserve these files and identify their component applicability from upstream/package manifests. These two files do not establish the license of every DLL/plugin in the archive. No complete package license audit is claimed. |
| FFmpeg/FFprobe and codecs | Existing root notice identifies a Gyan essentials build and GPL-enabled components. New MLT adapter links the libraries included with its native runtime; the smoke test separately invokes `build/windows/ffmpeg.exe`. | Inventory linked MLT-runtime FFmpeg libraries separately from the standalone executable. Record actual build configuration and codec dependencies; preserve applicable notices/source provenance. |
| SDL2 | Audio adapter resolves `SDL2.dll` on Windows or `libSDL2-2.0.so.0` on Linux. | Identify exact bundled/system version and include applicable package notices. |
| Optional services/model packs | Advanced features may later add Qt/Movit/OpenCV/OpenFX/audio plugins, ASR/translation/segmentation models and vendor format SDKs. Several modules are currently denied by the native loader. | Inventory each component actually distributed; installed/disabled does not mean absent from a distributed folder. No licensing assertion is made for an unselected future backend. |

Official MLT documentation identifies its framework as LGPLv2.1 and modules as GPLv2 or LGPLv2.1 depending on the build, with some modules unavailable under LGPL. See [MLT features](https://www.mltframework.org/features/). This factual upstream statement does not substitute for inspecting the actual downloaded build.

Before shipping, generate an inventory from the exact Windows and Linux packages, map notices to components, retain required upstream texts and record source/build provenance. Verify package scripts copy the editor/runtime notices rather than only the pre-existing standalone FFmpeg notices. Windows PowerShell 5.1 packaging and Linux editor cross-publish were reported successful. Earlier source checks and the final Windows-bundle 18-check native suite passed; this does not establish clean-machine deployment, Linux execution or a public-ready notice inventory.



## Recorded runtime provenance

The local `tools/mlt-runtime/PROVENANCE.txt` identifies Shotcut v26.9.27 win64, archive size 213,335,062 bytes, and locally calculated SHA256 `1f8c50ce7b125a2f0dcb60777b358e86baf03b8515982612b6adae10a83d93a5`. Runtime tests reported MLT **7.41.0**. The local hash is not presented as an independently authenticated upstream signature.

Source URL recorded in the local provenance: [official Shotcut release archive](https://github.com/mltframework/shotcut/releases/download/v26.9.27/shotcut-win64-26.9.27.zip). Packaging copies native runtime DLLs/plugins/data and upstream notices while excluding Shotcut application/UI executables, according to the recorded package provenance. Verify the exact distributed inventory before public release.


