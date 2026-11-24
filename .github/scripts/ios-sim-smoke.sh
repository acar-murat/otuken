#!/usr/bin/env bash
# Runs the game on an iOS Simulator in autotest mode, taking screenshots and collecting logs.
# Usage: ios-sim-smoke.sh <path/to/App.app> <output-dir>
set -uo pipefail
APP="$1"; OUT="$2"
mkdir -p "$OUT"

BUNDLE=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$APP/Info.plist")
EXE=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$APP/Info.plist")
DEV=$(xcrun simctl list devices available -j | python3 -c '
import json, sys
d = json.load(sys.stdin)["devices"]
phones = [x for k, v in d.items() if "iOS" in k for x in v if x["name"].startswith("iPhone")]
print(phones[-1]["udid"] if phones else "")')
if [ -z "$DEV" ]; then echo "::error::No iPhone simulator available"; exit 1; fi
xcrun simctl list devices | grep "$DEV" | tee "$OUT/device.txt"

xcrun simctl boot "$DEV" 2>/dev/null || true
xcrun simctl bootstatus "$DEV" -b
xcrun simctl install "$DEV" "$APP"

SIMCTL_CHILD_OTUKEN_AUTOTEST=1 xcrun simctl launch --console-pty "$DEV" "$BUNDLE" > "$OUT/console.log" 2>&1 &
LAUNCHER=$!

prev=0
for t in 3 8 14 22 32 45 60 80; do
  sleep $((t - prev)); prev=$t
  xcrun simctl io "$DEV" screenshot "$OUT/shot_$(printf %02d "$t")s.png" >/dev/null 2>&1 || echo "screenshot at ${t}s failed"
done

if xcrun simctl spawn "$DEV" launchctl list | grep -q "$BUNDLE"; then echo "RUNNING after ${prev}s" | tee "$OUT/status.txt"; else echo "NOT RUNNING after ${prev}s (crashed or exited)" | tee "$OUT/status.txt"; fi
kill "$LAUNCHER" 2>/dev/null || true
xcrun simctl terminate "$DEV" "$BUNDLE" 2>/dev/null || true

xcrun simctl spawn "$DEV" log show --last 10m --style compact --predicate "process == \"$EXE\"" > "$OUT/system.log" 2>&1 || true
find ~/Library/Logs/DiagnosticReports -name "*${EXE}*" -newer "$APP" -exec cp {} "$OUT/" \; 2>/dev/null || true

echo "---- OTUKEN lines ----"
grep -h "OTUKEN" "$OUT/console.log" "$OUT/system.log" | head -60 || echo "(no OTUKEN log lines found)"
