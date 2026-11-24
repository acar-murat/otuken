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
xcrun simctl bootstatus "$DEV" -b >/dev/null 2>&1 || echo "bootstatus reported an error (continuing)"
echo "settling for 30s..."; sleep 30

xcrun simctl install "$DEV" "$APP"
xcrun simctl spawn "$DEV" log stream --style compact --level debug \
  --predicate "process == \"$EXE\" OR (process == \"SpringBoard\" AND eventMessage CONTAINS \"$BUNDLE\")" \
  > "$OUT/stream.log" 2>&1 &
STREAM=$!
sleep 2

SIMCTL_CHILD_OTUKEN_AUTOTEST=1 xcrun simctl launch --terminate-running-process \
  --stdout="$OUT/stdout.log" --stderr="$OUT/stderr.log" "$DEV" "$BUNDLE" 2>&1 | tee "$OUT/launch.txt"

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
