#!/bin/sh
# Android microphone smoke (mobile-ci.yml -> android-mic-smoke). Runs inside the
# emulator step with the debug APK built against http://localhost:8080/mic-probe.html
# and that page served on the runner.
#   $1 = fixed                           -> granted mic must record (MIC_OK), and a
#                                           permanently denied mic must surface as
#                                           NotAllowedError (the app-settings path).
#   $1 = without-modify-audio-settings   -> control run on the pre-fix manifest: a
#                                           granted mic must STILL be denied, proving
#                                           this smoke catches the production bug.
set -eu

PKG=com.oetwithdrhesham.app
VARIANT="$1"
APK=android/app/build/outputs/apk/debug/app-debug.apk
RESULT=""

adb reverse tcp:8080 tcp:8080
adb install -r "$APK"

SERVER_LOG=/tmp/probe-server.log

# The probe page reports by requesting /result/<MIC_* line> from the probe server
# (see mic-probe.html); only lines logged after this launch count.
probe() {
  baseline=$(wc -l < "$SERVER_LOG")
  adb logcat -c
  adb shell am force-stop "$PKG"
  adb shell am start -n "$PKG/.MainActivity" >/dev/null
  RESULT=""
  i=0
  while [ "$i" -lt 180 ]; do
    RESULT=$(tail -n +"$((baseline + 1))" "$SERVER_LOG" | grep -o 'GET /result/MIC_[^ ]*' | tail -n 1 | sed 's|GET /result/||' | python3 -c 'import sys,urllib.parse;print(urllib.parse.unquote(sys.stdin.read().strip()))' || true)
    [ -n "$RESULT" ] && break
    i=$((i + 1))
    sleep 1
  done
  echo "[$1] ${RESULT:-<no MIC_* result within 180 s>}"
  if [ -z "$RESULT" ]; then
    echo "--- probe server log ---"; cat "$SERVER_LOG"
    echo "--- logcat (Capacitor / chromium / permissions) ---"
    adb logcat -d | grep -iE 'Capacitor|chromium|permission|MIC_|AndroidRuntime' | tail -n 80 || true
  fi
}

adb shell pm grant "$PKG" android.permission.RECORD_AUDIO
probe granted

if [ "$VARIANT" != "fixed" ]; then
  case "$RESULT" in
    MIC_DENIED*) echo "Control run reproduced the production denial, as expected." ; exit 0 ;;
    *) echo "::error::Control run (MODIFY_AUDIO_SETTINGS removed) did not reproduce the denial: $RESULT" ; exit 1 ;;
  esac
fi

case "$RESULT" in
  MIC_OK*) ;;
  *) echo "::error::RECORD_AUDIO is granted but the app WebView still could not record: $RESULT" ; exit 1 ;;
esac

# Permanently denied (user chose "Don't allow" twice): Capacitor must deny the
# page immediately with NotAllowedError — the state the Speaking pages answer
# with "Open app settings" — rather than hang on a prompt that never shows.
adb shell pm revoke "$PKG" android.permission.RECORD_AUDIO
adb shell pm set-permission-flags "$PKG" android.permission.RECORD_AUDIO user-fixed
probe permanently-denied
case "$RESULT" in
  MIC_DENIED*) ;;
  *) echo "::error::A permanently denied microphone did not surface as NotAllowedError: $RESULT" ; exit 1 ;;
esac

echo "Android microphone smoke passed."
