#!/usr/bin/env bash
# Runs the game on a booted Android emulator in autotest mode, taking screenshots and collecting logs.
# Usage: android-smoke.sh <path/to/app.apk> <output-dir>
set -uo pipefail
APK="$1"; OUT="$2"
PKG=com.otuken.arena
mkdir -p "$OUT"

# Let the freshly booted emulator settle, and keep system pop-ups (ANR, crash, full-screen tips)
# from stealing focus: Unity pauses the game whenever it loses focus.
adb shell settings put global hide_error_dialogs 1
adb shell settings put secure immersive_mode_confirmations confirmed
adb shell settings put system accelerometer_rotation 0
adb shell settings put system user_rotation 1   # landscape
adb shell input keyevent KEYCODE_WAKEUP
adb shell wm dismiss-keyguard 2>/dev/null || true
echo "settling for 45s..."; sleep 45
adb shell am broadcast -a android.intent.action.CLOSE_SYSTEM_DIALOGS >/dev/null

adb shell getprop ro.build.version.release | sed 's/^/android=/' | tee "$OUT/device.txt"
adb shell wm size | tee -a "$OUT/device.txt"
adb install -r "$APK" | tee "$OUT/install.txt"

adb logcat -G 16M 2>/dev/null || true
adb logcat -c
adb logcat -v time > "$OUT/logcat-full.log" 2>&1 &
LOGCAT=$!

ACT=$(adb shell cmd package resolve-activity --brief "$PKG" | tail -n 1 | tr -d '\r')
echo "activity=$ACT" | tee -a "$OUT/device.txt"
adb shell am start -n "$ACT" -e unity "-otukenAutotest"

prev=0
for t in 5 12 20 30 45 60 80 100 120; do
  sleep $((t - prev)); prev=$t
  adb shell am broadcast -a android.intent.action.CLOSE_SYSTEM_DIALOGS >/dev/null 2>&1
  adb shell dumpsys window | grep -m1 mCurrentFocus | sed "s/^/[${t}s] /" | tee -a "$OUT/focus.txt"
  adb exec-out screencap -p > "$OUT/shot_$(printf %03d "$t")s.png" || echo "screenshot at ${t}s failed"
done

if adb shell pidof "$PKG" >/dev/null; then echo "RUNNING after ${prev}s" | tee "$OUT/status.txt"; else echo "NOT RUNNING after ${prev}s (crashed or exited)" | tee "$OUT/status.txt"; fi
kill "$LOGCAT" 2>/dev/null || true
grep -E " (I|W|E|V|D)/Unity" "$OUT/logcat-full.log" > "$OUT/unity.log" || true
grep -E "AndroidRuntime|FATAL|tombstone|libc  .*Fatal|ANR in" "$OUT/logcat-full.log" > "$OUT/crashes.log" || true

echo "---- OTUKEN lines ----"
grep -h "OTUKEN" "$OUT/unity.log" | head -60 || echo "(no OTUKEN log lines found)"
echo "---- Unity errors ----"
grep -E " E/Unity" "$OUT/unity.log" | head -30 || true
