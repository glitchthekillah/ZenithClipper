#!/usr/bin/env bash
# Builds ZenithClipper-<version>-x86_64.AppImage from an already published Linux folder (app/ + editor/).
# Bundles MLT 7 (+ the plugin modules the editor uses), frei0r effects and a static FFmpeg, so the AppImage needs
# nothing extra except zenity/pactl/python3 for the controls and audio routing (present on normal desktops).
# Usage: scripts/build-appimage.sh <version> <published-folder> <output-dir>   (Ubuntu 24.04, run as a user with sudo)
set -euo pipefail
version="$1"; published="$(readlink -f "$2")"; out="$(mkdir -p "$3" && readlink -f "$3")"
repo="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
work="$(mktemp -d)"; appdir="$work/AppDir"
trap 'rm -rf "$work"' EXIT

sudo apt-get update -qq
sudo apt-get install -y -qq libmlt7 libmlt-data frei0r-plugins imagemagick file >/dev/null

mkdir -p "$appdir/usr/lib/zenithclipper" "$appdir/usr/bin" "$appdir/usr/lib/mlt-7" "$appdir/usr/share"
cp -a "$published/app" "$published/editor" "$appdir/usr/lib/zenithclipper/"
# .NET's optional LTTng tracing hook needs liblttng-ust, which linuxdeploy can't resolve; nothing uses it.
find "$appdir/usr/lib/zenithclipper" -name libcoreclrtraceptprovider.so -delete

# MLT modules the editor loads (no Qt, OpenCV, Movit, DeckLink, JACK or xine: those are denied at runtime anyway).
mltlib=/usr/lib/x86_64-linux-gnu/mlt-7
for m in avformat core frei0r kdenlive normalize oldfilm plus plusgpl resample rubberband sdl2 sox vidstab vorbis xml; do
  cp "$mltlib/libmlt$m.so" "$appdir/usr/lib/mlt-7/"
done
cp -a /usr/share/mlt-7 "$appdir/usr/share/mlt-7"
cp -a /usr/lib/frei0r-1 "$appdir/usr/lib/frei0r-1"

# Static FFmpeg/FFprobe for the recorder and exports.
curl -sSL https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-linux64-gpl.tar.xz -o "$work/ffmpeg.tar.xz"
tar -xJf "$work/ffmpeg.tar.xz" -C "$work"
cp "$work"/ffmpeg-master-latest-linux64-gpl/bin/{ffmpeg,ffprobe} "$appdir/usr/bin/"

# Icon (largest frame of the Windows .ico) and desktop entry.
convert "$repo/assets/zenithclipper.ico" "$work/icon.png"
icon="$(ls -S "$work"/icon*.png | head -1)"
convert "$icon" -resize 256x256 "$appdir/zenithclipper.png"
cat > "$appdir/zenithclipper.desktop" <<'EOF'
[Desktop Entry]
Type=Application
Name=ZenithClipper
Comment=Instant replay recorder with a built-in clip editor
Exec=zenithclipper panel
Icon=zenithclipper
Categories=AudioVideo;Recorder;
Terminal=false
EOF

ln -s ../lib/zenithclipper/app/zenithclipper "$appdir/usr/bin/zenithclipper"

# Copy libmlt and every library the modules need (minus the base system set linuxdeploy excludes).
curl -sSL https://github.com/linuxdeploy/linuxdeploy/releases/download/continuous/linuxdeploy-x86_64.AppImage -o "$work/linuxdeploy"
curl -sSL https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage -o "$work/appimagetool"
chmod +x "$work/linuxdeploy" "$work/appimagetool"
libs=(--library /usr/lib/x86_64-linux-gnu/libmlt-7.so.7 --library /usr/lib/x86_64-linux-gnu/libmlt++-7.so.7)
for so in "$appdir"/usr/lib/mlt-7/*.so "$appdir"/usr/lib/frei0r-1/*.so; do libs+=(--library "$so"); done
APPIMAGE_EXTRACT_AND_RUN=1 NO_STRIP=1 "$work/linuxdeploy" --appdir "$appdir" "${libs[@]}" \
  --desktop-file "$appdir/zenithclipper.desktop" --icon-file "$appdir/zenithclipper.png" > "$out/linuxdeploy.log" 2>&1 || { tail -30 "$out/linuxdeploy.log"; exit 1; }
# linuxdeploy copied the module .so files into usr/lib as libraries too; MLT reads modules only from MLT_REPOSITORY.

cat > "$appdir/AppRun" <<'EOF'
#!/usr/bin/env bash
# Point the app, the editor and MLT at the bundled copies, then run the normal launcher.
here="$(dirname "$(readlink -f "$0")")"
export PATH="$here/usr/bin:$PATH"
export LD_LIBRARY_PATH="$here/usr/lib${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
export MLT_REPOSITORY="$here/usr/lib/mlt-7"
export MLT_DATA="$here/usr/share/mlt-7"
export MLT_PROFILES_PATH="$here/usr/share/mlt-7/profiles"
export FREI0R_PATH="$here/usr/lib/frei0r-1"
exec "$here/usr/lib/zenithclipper/app/zenithclipper" "${@:-panel}"
EOF
chmod +x "$appdir/AppRun"

target="$out/ZenithClipper-${version}-x86_64.AppImage"
ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 "$work/appimagetool" --no-appstream "$appdir" "$target" >/dev/null
echo "$target"
