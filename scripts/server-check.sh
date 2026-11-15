#!/usr/bin/env bash
# Run only inside the disposable directory created by test-server.ps1.
set -euo pipefail
test_dir="${1:?Temporary directory required}"
[[ "$test_dir" =~ ^/tmp/zenithclipper-test\.[A-Za-z0-9]+$ && -d "$test_dir" && ! -L "$test_dir" && -O "$test_dir" ]] || exit 2
engine_pid=""
cleanup() {
  if [[ -n "$engine_pid" ]] && kill -0 "$engine_pid" 2>/dev/null; then
    case "$(tr '\0' ' ' < "/proc/$engine_pid/cmdline" 2>/dev/null || true)" in
      *"$test_dir/"*) kill "$engine_pid" 2>/dev/null || true; wait "$engine_pid" 2>/dev/null || true ;;
    esac
  fi
  rm -rf -- "$test_dir"
  [[ ! -e "$test_dir" ]] && printf 'CLEANUP_OK %s\n' "$test_dir"
}
trap cleanup EXIT
trap 'exit 130' INT TERM
mkdir -p "$test_dir/runtime" "$test_dir/scratch" "$test_dir/dotnet-home"
export TMPDIR="$test_dir/scratch"
export DOTNET_CLI_HOME="$test_dir/dotnet-home"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_PROCESSOR_COUNT=2
export DOTNET_BUNDLE_EXTRACT_BASE_DIR="$test_dir/extract"
export XDG_CACHE_HOME="$test_dir/cache"
export XDG_CONFIG_HOME="$test_dir/config"
export XDG_DATA_HOME="$test_dir/data"
export ZENITHCLIPPER_HOME="$test_dir/state"
export DISPLAY=""
export WAYLAND_DISPLAY=""
tar -xzf "$test_dir/runtime.tar.gz" -C "$test_dir/runtime" --no-same-owner --no-same-permissions
chmod +x "$test_dir/runtime/dotnet"
export DOTNET_ROOT="$test_dir/runtime"
printf 'Temporary test footprint: '; du -sh "$test_dir"
"$test_dir/runtime/dotnet" --info
ffmpeg -version | head -n 1 || true
nice -n 10 "$test_dir/runtime/dotnet" "$test_dir/tests/ZenithClipper.Tests.dll" /usr/bin/ffmpeg
python3 -B - "$test_dir" <<'PY'
import ast, pathlib, sys
for path in pathlib.Path(sys.argv[1], "app", "linux").glob("*.py"):
    ast.parse(path.read_text())
print("PASS Linux helper syntax")
PY
mkdir -p "$ZENITHCLIPPER_HOME"
printf '{"AutoStartCapture":false,"CaptureAudio":false,"ExcludedApps":[]}\n' > "$ZENITHCLIPPER_HOME/settings.json"
nice -n 10 "$test_dir/runtime/dotnet" "$test_dir/app/zenithclipper.dll" daemon > "$test_dir/daemon.log" 2>&1 &
engine_pid=$!
ready=false
for attempt in $(seq 1 15); do
  if "$test_dir/runtime/dotnet" "$test_dir/app/zenithclipper.dll" status > "$test_dir/status.json" 2>/dev/null; then ready=true; break; fi
  kill -0 "$engine_pid" 2>/dev/null || { cat "$test_dir/daemon.log"; exit 1; }
  sleep 0.2
done
[[ "$ready" == true ]] || { cat "$test_dir/daemon.log"; exit 1; }
python3 -B - "$test_dir/status.json" <<'PY'
import json, sys
response = json.load(open(sys.argv[1]))
assert response["Ok"] and not response["Status"]["Capturing"], response
print("PASS separate Linux daemon/client IPC, capture disabled")
PY
"$test_dir/runtime/dotnet" "$test_dir/app/zenithclipper.dll" shutdown
wait "$engine_pid"
engine_pid=""
printf 'SERVER_CHECKS_PASSED\n'
