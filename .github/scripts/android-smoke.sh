#!/usr/bin/env bash
# Runs the game on a booted Android emulator in autotest mode, taking screenshots and collecting logs.
# Usage: android-smoke.sh <path/to/app.apk> <output-dir>
set -uo pipefail
APK="$1"; OUT="$2"
PKG=com.otuken.arena
mkdir -p "$OUT"

adb shell getprop ro.build.version.release | sed 's/^/android=/' | tee "$OUT/device.txt"
adb shell wm size | tee -a "$OUT/device.txt"
adb shell settings put system accelerometer_rotation 0
adb shell settings put system user_rotation 1   # landscape

adb install -r "$APK" | tee "$OUT/install.txt"
adb logcat -c
ACT=$(adb shell cmd package resolve-activity --brief "$PKG" | tail -n 1 | tr -d '\r')
echo "activity=$ACT" | tee -a "$OUT/device.txt"
adb shell am start -n "$ACT" -e unity "-otukenAutotest"

prev=0
for t in 3 8 14 22 32 45 60 80; do
  sleep $((t - prev)); prev=$t
  adb exec-out screencap -p > "$OUT/shot_$(printf %02d "$t")s.png" || echo "screenshot at ${t}s failed"
done

if adb shell pidof "$PKG" >/dev/null; then echo "RUNNING after ${prev}s" | tee "$OUT/status.txt"; else echo "NOT RUNNING after ${prev}s (crashed or exited)" | tee "$OUT/status.txt"; fi
adb logcat -d -s Unity:V > "$OUT/unity.log"
adb logcat -d '*:E' > "$OUT/errors.log"
adb logcat -d > "$OUT/logcat-full.log"

echo "---- OTUKEN lines ----"
grep -h "OTUKEN" "$OUT/unity.log" | head -60 || echo "(no OTUKEN log lines found)"
