#!/usr/bin/env bash
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
destination="${XDG_CONFIG_HOME:-$HOME/.config}/autostart/zenithclipper.desktop"
if [[ "${1:-}" == "--remove" ]]; then
  rm -f -- "$destination"
  printf 'ZenithClipper startup disabled.\n'
  exit
fi
[[ -x "$root/build/linux/zenithclipper" ]] || { printf 'Build Linux first.\n' >&2; exit 1; }
mkdir -p -- "$(dirname -- "$destination")"
escaped="${root//\\/\\\\}"
escaped="${escaped//\"/\\\"}"
escaped="${escaped//\$/\\\$}"
escaped="${escaped//\`/\\\`}"
escaped="${escaped//%/%%}"
cat > "$destination" <<EOF
[Desktop Entry]
Type=Application
Name=ZenithClipper
Comment=Start the rolling capture engine
Exec="$escaped/build/linux/zenithclipper" daemon
Terminal=false
X-GNOME-Autostart-enabled=true
EOF
printf 'Startup enabled. Wayland may request screen-sharing consent at login.\n'
