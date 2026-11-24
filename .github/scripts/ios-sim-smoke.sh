#!/usr/bin/env bash
# Runs the game on an iOS Simulator in autotest mode, taking screenshots and collecting logs.
# Usage: ios-sim-smoke.sh <path/to/App.app> <output-dir>
set -uo pipefail
APP="$1"; OUT="$2"
mkdir -p "$OUT"
START_MARK=$(mktemp)

BUNDLE=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$APP/Info.plist")
EXE=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$APP/Info.plist")
echo "bundle=$BUNDLE exe=$EXE" | tee "$OUT/device.txt"
lipo -info "$APP/$EXE" | tee -a "$OUT/device.txt"
lipo -info "$APP/Frameworks/UnityFramework.framework/UnityFramework" | tee -a "$OUT/device.txt"

DEV=$(xcrun simctl list devices available -j | python3 -c '
import json, sys
d = json.load(sys.stdin)["devices"]
phones = [x for k, v in d.items() if "iOS" in k for x in v if x["name"].startswith("iPhone")]
print(phones[-1]["udid"] if phones else "")')
if [ -z "$DEV" ]; then echo "::error::No iPhone simulator available"; exit 1; fi
xcrun simctl list devices | grep "$DEV" | tee -a "$OUT/device.txt"

xcrun simctl boot "$DEV" 2>/dev/null || true
# Headless boots on CI can stall at "Waiting on System App"; opening the Simulator app lets SpringBoard finish.
open -a Simulator --args -CurrentDeviceUDID "$DEV" || true
for attempt in 1 2 3; do
  if xcrun simctl bootstatus "$DEV" -b >/dev/null 2>&1; then echo "boot complete (attempt $attempt)"; break; fi
  echo "bootstatus attempt $attempt failed; waiting"; sleep 20
done
echo "settling for 30s..."; sleep 30

xcrun simctl install "$DEV" "$APP"

# Load diagnostics: what platform the binaries target, how they are signed, and what the loader says
# when the executable is started directly inside the simulator.
{
  echo "== vtool (platform the binaries were built for)"
  xcrun vtool -show-build "$APP/$EXE" 2>&1 | head -20
  xcrun vtool -show-build "$APP/Frameworks/UnityFramework.framework/UnityFramework" 2>&1 | head -20
  echo "== codesign"
  codesign -dv "$APP" 2>&1
  codesign -dv "$APP/Frameworks/UnityFramework.framework" 2>&1
  echo "== linked libraries"
  otool -L "$APP/$EXE" 2>&1
  echo "== Info.plist"
  plutil -p "$APP/Info.plist" 2>&1 | head -60
  CONTAINER=$(xcrun simctl get_app_container "$DEV" "$BUNDLE" app)
  echo "== direct spawn of $CONTAINER/$EXE (20s)"
  SIMCTL_CHILD_OTUKEN_AUTOTEST=1 xcrun simctl spawn "$DEV" "$CONTAINER/$EXE" > "$OUT/spawn.log" 2>&1 &
  SP=$!; sleep 20; kill "$SP" 2>/dev/null || true; wait "$SP" 2>/dev/null; echo "spawn exit status: $?"
  head -c 6000 "$OUT/spawn.log"
} > "$OUT/diagnostics.txt" 2>&1
cat "$OUT/diagnostics.txt"
xcrun simctl terminate "$DEV" "$BUNDLE" 2>/dev/null || true

xcrun simctl spawn "$DEV" log stream --style compact --level debug \
  --predicate "process == \"$EXE\" OR (process == \"SpringBoard\" AND eventMessage CONTAINS \"$BUNDLE\")" \
  > "$OUT/stream.log" 2>&1 &
STREAM=$!
sleep 2

for attempt in 1 2 3; do
  if SIMCTL_CHILD_OTUKEN_AUTOTEST=1 xcrun simctl launch --terminate-running-process \
       --stdout="$OUT/stdout.log" --stderr="$OUT/stderr.log" "$DEV" "$BUNDLE" >> "$OUT/launch.txt" 2>&1; then
    echo "launched on attempt $attempt" | tee -a "$OUT/launch.txt"; break
  fi
  echo "launch attempt $attempt failed" | tee -a "$OUT/launch.txt"; sleep 20
done
cat "$OUT/launch.txt"

prev=0
for t in 5 12 20 30 45 60 80 100 120; do
  sleep $((t - prev)); prev=$t
  xcrun simctl spawn "$DEV" launchctl list 2>/dev/null | grep -m1 "UIKitApplication:$BUNDLE" | sed "s/^/[${t}s] /" >> "$OUT/alive.txt" || echo "[${t}s] not running" >> "$OUT/alive.txt"
  xcrun simctl io "$DEV" screenshot "$OUT/shot_$(printf %03d "$t")s.png" >/dev/null 2>&1 || echo "screenshot at ${t}s failed"
done

if xcrun simctl spawn "$DEV" launchctl list | grep -q "UIKitApplication:$BUNDLE"; then echo "RUNNING after ${prev}s" | tee "$OUT/status.txt"; else echo "NOT RUNNING after ${prev}s (crashed or exited)" | tee "$OUT/status.txt"; fi
kill "$STREAM" 2>/dev/null || true
xcrun simctl terminate "$DEV" "$BUNDLE" 2>/dev/null || true

mkdir -p "$OUT/crashes"
find ~/Library/Logs/DiagnosticReports -type f -newer "$START_MARK" -exec cp {} "$OUT/crashes/" \; 2>/dev/null || true
ls "$OUT/crashes"

echo "---- alive ----"; cat "$OUT/alive.txt"
echo "---- stdout (tail) ----"; tail -n 40 "$OUT/stdout.log" 2>/dev/null
echo "---- OTUKEN lines ----"
grep -h "OTUKEN" "$OUT/stdout.log" "$OUT/stderr.log" "$OUT/stream.log" 2>/dev/null | head -60 || echo "(no OTUKEN log lines found)"
