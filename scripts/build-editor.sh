#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
rid="${1:-linux-x64}"
case "$rid" in linux-x64|linux-arm64) ;; *) printf 'Use linux-x64 or linux-arm64.\n' >&2; exit 2 ;; esac
if [[ "$(uname -s)" != Linux ]]; then
  printf 'Run this script on Linux. The Linux desktop has not been tested from Windows.\n' >&2
  exit 2
fi
sdk="${ZENITH_DOTNET:-dotnet}"
if [[ -x "$root/tools/dotnet/dotnet" ]]; then sdk="$root/tools/dotnet/dotnet"; fi
export DOTNET_CLI_HOME="$root/.build-home"
export NUGET_PACKAGES="$root/.packages"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
project=src/ZenithClipper.Editor.Desktop/ZenithClipper.Editor.Desktop.csproj
"$sdk" restore "$project" --configfile NuGet.Config -r "$rid"
"$sdk" publish "$project" -c Release -r "$rid" --self-contained false --no-restore -p:UseAppHost=true -o build/editor-linux
chmod +x build/editor-linux/ZenithClipper.Editor
cat <<'NOTICE' > build/editor-linux/RUNTIME-REQUIREMENTS.txt
Requires .NET 10 runtime, a graphical Linux session, system MLT 7 (libmlt-7.so.7),
MLT core/avformat/plus/frei0r plugins and data, SDL2 (libSDL2-2.0.so.0),
FFmpeg/ffprobe on PATH, and the native font/display dependencies used by Avalonia.
Native MLT and its licenses are provided by your Linux distribution; no Windows DLLs are copied.
The real Linux desktop/audio/backend has not been tested by the Windows build process.
NOTICE
cp THIRD_PARTY.md build/editor-linux/THIRD_PARTY.md
if [[ -f docs/editor-rebuild/THIRD_PARTY_DRAFT.md ]]; then
  mkdir -p build/editor-linux/licenses
  cp docs/editor-rebuild/THIRD_PARTY_DRAFT.md build/editor-linux/licenses/THIRD_PARTY_DRAFT.md
fi
cat <<'PROVENANCE' > build/editor-linux/PROVENANCE.txt
ZenithClipper editor private development package; framework-dependent .NET 10 and Avalonia 12.1.5.
Built from the local ZenithClipper source and restored NuGet packages through NuGet.Config.
System dependencies, not bundled: MLT 7 (libmlt-7.so.7), MLT plugins/data, SDL2 (libSDL2-2.0.so.0), FFmpeg/ffprobe.
Your distribution supplies native package versions, provenance and license notices.
See THIRD_PARTY.md and licenses/THIRD_PARTY_DRAFT.md for the observed dependency inventory and remaining notice work.
No Windows DLLs or Shotcut application UI are copied. Real Linux desktop and audio execution remain untested.
This private development inventory is not a complete public-distribution licensing audit or source offer.
PROVENANCE
printf 'System MLT 7 and plugins/data\nSystem SDL2\nSystem FFmpeg/ffprobe\nSystem .NET 10 runtime\nAvalonia display/font dependencies\n' > build/editor-linux/RUNTIME-COMPONENTS.txt
printf 'Built build/editor-linux/ZenithClipper.Editor (%s; requires system .NET 10 and MLT 7).\n' "$rid"
printf 'Linux desktop execution remains untested. No existing output or user data was removed.\n'
