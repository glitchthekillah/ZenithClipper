#!/usr/bin/env bash
# Builds ZenithClipper.app and a .dmg for Apple Silicon Macs on a GitHub macOS runner.
# Usage: scripts/build-macos.sh VERSION OUTDIR
# The app is ad-hoc signed only (no Apple developer account), so the first launch needs right-click > Open.
set -euo pipefail
version="$1"; out="$(mkdir -p "$2" && cd "$2" && pwd)"
brew install mlt sdl2 ffmpeg frei0r dylibbundler >/dev/null
app="$out/ZenithClipper.app"; C="$app/Contents"; R="$C/Resources"; L="$R/mlt/lib"
rm -rf "$app"; mkdir -p "$C/MacOS" "$R/app" "$R/editor" "$L/mlt-7" "$L/frei0r-1" "$R/mlt/share"
# Homebrew libraries are built for the runner's macOS, so that is the oldest macOS the app can run on.
minos="$(sw_vers -productVersion | cut -d. -f1).0"

echo "== menu bar app and capture helper"
swiftc -O -target arm64-apple-macos13.0 -o "$C/MacOS/ZenithClipper" mac/main.swift \
  -framework ScreenCaptureKit -framework Carbon -framework ServiceManagement -framework CoreMedia -framework CoreVideo
sed -e "s/VERSION/$version/g" -e "s/MINOS/$minos/" mac/Info.plist > "$C/Info.plist"
iconset="$(mktemp -d)/ZenithClipper.iconset"; mkdir -p "$iconset"
for s in 16 32 128 256 512; do
  sips -z $s $s assets/zenithclipper.png --out "$iconset/icon_${s}x${s}.png" >/dev/null
  sips -z $((s*2)) $((s*2)) assets/zenithclipper.png --out "$iconset/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$R/ZenithClipper.icns"

echo "== engine and editor"
sed -i '' "s#<Version>[^<]*</Version>#<Version>${version}</Version>#" Directory.Build.props
dotnet publish src/ZenithClipper.App -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=false -o "$R/app"
dotnet publish src/ZenithClipper.Editor.Desktop -c Release -r osx-arm64 --self-contained true -o "$R/editor"
find "$R" -name '*.pdb' -delete

echo "== FFmpeg, MLT, frei0r, SDL"
brewlib="$(brew --prefix)/lib"
mlt="$(brew --prefix mlt)"; ls "$mlt/lib" "$mlt/share"
cp "$(brew --prefix ffmpeg)/bin/ffmpeg" "$(brew --prefix ffmpeg)/bin/ffprobe" "$R/app/"
cp "$mlt"/lib/mlt*/*.so "$L/mlt-7/"
cp -R "$mlt"/share/mlt*/. "$R/mlt/share/mlt-7"
cp "$(brew --prefix frei0r)"/lib/frei0r-1/*.so "$L/frei0r-1/" 2>/dev/null || true
cp "$(brew --prefix sdl2)"/lib/libSDL2-2.0.0.dylib "$L/"
for sdl3 in "$(brew --prefix)"/opt/sdl3/lib/libSDL3*.dylib; do [ -f "$sdl3" ] && cp "$sdl3" "$L/"; done
# Copy every dependency into Resources/mlt/lib and point all references at @rpath.
bundle() { dylibbundler -of -cd -b -ns -s "$brewlib" -d "$L" -p @rpath/ "$@" </dev/null; }
args=(); for f in "$L"/mlt-7/*.so "$L"/frei0r-1/*.so "$L"/*.dylib "$R/app/ffmpeg" "$R/app/ffprobe"; do [ -f "$f" ] && args+=(-x "$f"); done
bundle "${args[@]}"
rpath() { install_name_tool -add_rpath "$1" "$2" 2>/dev/null || true; }
for f in "$L"/*.dylib; do rpath @loader_path "$f"; done
for f in "$L"/mlt-7/*.so "$L"/frei0r-1/*.so; do [ -f "$f" ] && rpath @loader_path/.. "$f"; done
for f in "$R/app/ffmpeg" "$R/app/ffprobe"; do rpath @executable_path/../mlt/lib "$f"; done
echo "Bundled libraries: $(ls "$L" | wc -l)"

echo "== signing (ad-hoc)"
find "$C" -type f | while read -r f; do
  if file "$f" | grep -q 'Mach-O'; then codesign --force --sign - --timestamp=none "$f" 2>/dev/null || echo "sign failed: $f"; fi
done
codesign --force --sign - --timestamp=none "$app"
codesign --verify --verbose=1 "$app" || true

echo "== dmg"
stage="$(mktemp -d)"; cp -R "$app" "$stage/"; ln -s /Applications "$stage/Applications"
hdiutil create -volname ZenithClipper -srcfolder "$stage" -ov -format UDZO "$out/ZenithClipper_${version}_mac-arm64.dmg"
