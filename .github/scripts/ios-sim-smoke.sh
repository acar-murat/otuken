#!/usr/bin/env bash
# Runs the game on iOS Simulators in autotest mode: the newest iPhone runtime and, when installed,
# an iOS 18 runtime. Launches Safari first on each as a control, then takes screenshots and logs.
# Usage: ios-sim-smoke.sh <path/to/App.app> <output-dir>
set -uo pipefail
APP="$1"; OUT="$2"
mkdir -p "$OUT"
START_MARK=$(mktemp)

BUNDLE=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$APP/Info.plist")
EXE=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$APP/Info.plist")
{
  echo "bundle=$BUNDLE exe=$EXE"
  xcrun vtool -show-build "$APP/$EXE" 2>&1 | grep -E "platform|minos|sdk"
  echo "== Info.plist"
  plutil -p "$APP/Info.plist"
  echo "== entitlements"
  codesign -d --entitlements - "$APP" 2>&1
  echo "== runtimes"
  xcrun simctl list runtimes
} > "$OUT/app-info.txt" 2>&1

# Pick devices: newest iOS runtime, plus an iOS 18 runtime if the runner has one.
xcrun simctl list devices available -j > "$OUT/devices.json"
DEVICES=$(python3 - "$OUT/devices.json" <<'PY'
import json, sys, re
d = json.load(open(sys.argv[1]))["devices"]
def ver(k):
    m = re.search(r"iOS-(\d+)-(\d+)", k)
    return (int(m.group(1)), int(m.group(2))) if m else (0, 0)
runtimes = sorted([k for k in d if "iOS" in k and any(x["name"].startswith("iPhone") for x in d[k])], key=ver)
picked = []
if runtimes:
    picked.append(runtimes[-1])
    older = [k for k in runtimes if ver(k)[0] == 18]
    if older and older[-1] not in picked:
        picked.append(older[-1])
for k in picked:
    phone = [x for x in d[k] if x["name"].startswith("iPhone")][0]
    print(f'{phone["udid"]} iOS-{ver(k)[0]}.{ver(k)[1]}')
PY
)
echo "devices to test:"; echo "$DEVICES"
[ -z "$DEVICES" ] && { echo "::error::No iPhone simulator available"; exit 1; }

open -a Simulator || true

while read -r DEV LABEL; do
  [ -z "$DEV" ] && continue
  D="$OUT/$LABEL"; mkdir -p "$D"
  echo "=================== $LABEL ($DEV)"
  xcrun simctl boot "$DEV" 2>/dev/null || true
  for attempt in 1 2 3; do
    if xcrun simctl bootstatus "$DEV" -b >/dev/null 2>&1; then echo "boot complete (attempt $attempt)"; break; fi
    echo "bootstatus attempt $attempt failed; waiting"; sleep 20
  done
  sleep 20

  echo "-- control: launching Safari"
  if xcrun simctl launch "$DEV" com.apple.mobilesafari > "$D/control.txt" 2>&1; then echo "CONTROL OK: Safari launched" | tee -a "$D/control.txt"
  else echo "CONTROL FAILED: Safari did not launch" | tee -a "$D/control.txt"; fi
  sleep 5; xcrun simctl terminate "$DEV" com.apple.mobilesafari 2>/dev/null || true

  xcrun simctl install "$DEV" "$APP"
  xcrun simctl spawn "$DEV" log stream --style compact --level debug \
    --predicate "process == \"$EXE\" OR (process == \"SpringBoard\" AND eventMessage CONTAINS \"$BUNDLE\") OR (process == \"runningboardd\" AND eventMessage CONTAINS \"$BUNDLE\")" \
    > "$D/stream.log" 2>&1 &
  STREAM=$!
  sleep 2

  for attempt in 1 2; do
    if SIMCTL_CHILD_OTUKEN_AUTOTEST=1 xcrun simctl launch --terminate-running-process \
         --stdout="$D/stdout.log" --stderr="$D/stderr.log" "$DEV" "$BUNDLE" >> "$D/launch.txt" 2>&1; then
      echo "GAME LAUNCHED on attempt $attempt" | tee -a "$D/launch.txt"; break
    fi
    echo "game launch attempt $attempt failed" | tee -a "$D/launch.txt"; sleep 15
  done

  prev=0
  for t in 5 15 30 45 60 90; do
    sleep $((t - prev)); prev=$t
    if xcrun simctl spawn "$DEV" launchctl list 2>/dev/null | grep -q "UIKitApplication:$BUNDLE"; then echo "[${t}s] running" >> "$D/alive.txt"; else echo "[${t}s] not running" >> "$D/alive.txt"; fi
    xcrun simctl io "$DEV" screenshot "$D/shot_$(printf %03d "$t")s.png" >/dev/null 2>&1 || true
  done
  grep -q running "$D/alive.txt" && grep -qv "not running" "$D/alive.txt" && echo "RUNNING" > "$D/status.txt" || echo "NOT RUNNING" > "$D/status.txt"
  kill "$STREAM" 2>/dev/null || true
  xcrun simctl terminate "$DEV" "$BUNDLE" 2>/dev/null || true
  xcrun simctl shutdown "$DEV" 2>/dev/null || true

  echo "-- $LABEL: $(cat "$D/status.txt")"; cat "$D/control.txt"; tail -n 3 "$D/launch.txt"
  grep -h "OTUKEN" "$D/stdout.log" "$D/stderr.log" 2>/dev/null | head -8
  grep -iE "denied|reason|launch.*fail|Aborting|crash|exited" "$D/stream.log" | cut -c1-300 | head -8
done <<< "$DEVICES"

mkdir -p "$OUT/crashes"
find ~/Library/Logs/DiagnosticReports -type f -newer "$START_MARK" -exec cp {} "$OUT/crashes/" \; 2>/dev/null || true
ls "$OUT/crashes"
