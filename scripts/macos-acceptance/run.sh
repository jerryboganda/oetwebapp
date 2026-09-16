#!/usr/bin/env bash
# macOS desktop acceptance (see .github/workflows/macos-video-acceptance.yml).
# Installs the given DMG, signs the REAL app in to PRODUCTION as the CI learner,
# plays 3 protected videos (play/pause, seek, fullscreen in/out, back to the
# library), checks that no external browser opened, and captures screenshots + a
# screen recording while protected video plays to classify whether the app window
# is hidden from capture. Every check is written to results.tsv as
# PASS / FAIL / NOT_AUTOMATED / INFO; the script exits non-zero on any FAIL or
# NOT_AUTOMATED (an owner criterion that could not be proven is not a pass).
#
# Usage: run.sh <path-to.dmg>
# Env:   OET_CI_LEARNER_EMAIL, OET_CI_LEARNER_PASSWORD (required)
#        VIDEO_IDS (3 comma-separated ids), OUT (output dir), API (API base URL)
set -uo pipefail

DMG="${1:?usage: run.sh <dmg>}"
HERE="$(cd "$(dirname "$0")" && pwd)"
OUT="${OUT:-$PWD/acceptance-out}"
API="${API:-https://api.oetwithdrhesham.co.uk}"
APP="/Applications/OET with Dr. Hesham.app"
BUNDLE_ID="com.oetprep.desktop"
LSREGISTER=/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister
mkdir -p "$OUT"
TOOL="$OUT/oetmac"
RESULTS="$OUT/results.tsv"
: > "$RESULTS"
IFS=',' read -r -a VIDS <<< "${VIDEO_IDS:?VIDEO_IDS is required}"
[ "${#VIDS[@]}" -eq 3 ] || { echo "VIDEO_IDS must list exactly 3 ids (max 3 active video sessions)"; exit 2; }
: "${OET_CI_LEARNER_EMAIL:?secret OET_CI_LEARNER_EMAIL is not configured}"
: "${OET_CI_LEARNER_PASSWORD:?secret OET_CI_LEARNER_PASSWORD is not configured}"

record() { printf '%s\t%s\t%s\n' "$1" "$2" "$3" | tee -a "$RESULTS"; }
CONTROLS_PID=""
WATCHER_PID=""
finish() {
  kill ${CONTROLS_PID:-} ${WATCHER_PID:-} 2>/dev/null
  # AX dumps can contain the CI learner's email (sign-in form, watermark).
  local email_re
  email_re="$(printf '%s' "$OET_CI_LEARNER_EMAIL" | sed 's/[.[*^$/]/\\&/g')"
  for dump in "$OUT"/ax-*.txt; do
    [ -f "$dump" ] && sed -i '' "s/$email_re/<learner>/g" "$dump"
  done
  if ! jq -e 'select(.verdict == "HIDDEN" or .verdict == "VISIBLE")' "$OUT/captures.jsonl" > /dev/null 2>&1; then
    record "capture evidence" FAIL "no capture produced a HIDDEN/VISIBLE verdict (see captures.jsonl)"
  fi
  {
    echo "### macOS acceptance — $(sw_vers -productVersion), app $(defaults read "$APP/Contents/Info" CFBundleShortVersionString 2>/dev/null)"
    echo
    echo "| Check | Result | Detail |"
    echo "|---|---|---|"
    awk -F'\t' '{gsub(/\|/, "/", $3); printf "| %s | %s | %s |\n", $1, $2, $3}' "$RESULTS"
  } > "$OUT/summary.md"
  [ -n "${GITHUB_STEP_SUMMARY:-}" ] && cat "$OUT/summary.md" >> "$GITHUB_STEP_SUMMARY"
  awk -F'\t' '$2 == "FAIL" || $2 == "NOT_AUTOMATED" { failed = 1 } END { exit failed }' "$RESULTS"
}
trap 'finish; exit $?' EXIT
check() { # check <name> <detail> <command…>
  local name="$1" detail="$2"; shift 2
  if "$@" >> "$OUT/commands.log" 2>&1; then record "$name" PASS "$detail"; else record "$name" FAIL "$detail"; return 1; fi
}

# ── API helpers (the app itself is never driven through these) ──────────────
DEVICE_ID="$(uuidgen | tr 'A-Z' 'a-z')"
api_sign_in() {
  # Exactly one attempt: repeated wrong-password attempts lock the account.
  local body
  body="$(jq -n --arg e "$OET_CI_LEARNER_EMAIL" --arg p "$OET_CI_LEARNER_PASSWORD" '{email:$e,password:$p,rememberMe:false}')"
  curl -sS --max-time 30 -X POST "$API/v1/auth/sign-in" -H 'Content-Type: application/json' \
    -H "X-OET-Device-Id: $DEVICE_ID" -d "$body" -o "$OUT/sign-in.json" -w '%{http_code}'
}
api_post() { # api_post <token> <path> <json> → HTTP code
  curl -sS --max-time 30 -X POST -H "Authorization: Bearer $1" -H "X-OET-Device-Id: $DEVICE_ID" \
    -H 'Content-Type: application/json' -d "$3" -o /dev/null -w '%{http_code}' "$API$2"
}
api_token() { jq -r '.accessToken // .session.accessToken // .tokens.accessToken // empty' "$OUT/sign-in.json"; }
api_get() { # api_get <token> <path> → body on stdout, HTTP code in $OUT/last-code
  curl -sS --max-time 30 -H "Authorization: Bearer $1" -H "X-OET-Device-Id: $DEVICE_ID" \
    -o "$OUT/last-body.json" -w '%{http_code}' "$API$2" > "$OUT/last-code"
  cat "$OUT/last-body.json"
}
position() { jq -r '(.progress // .video.progress // {}).positionSeconds // 0 | floor'; }

playing() { pmset -g assertions | grep -qF "HTMLMediaElement playback"; }
wait_playing() { for _ in $(seq 1 30); do playing && return 0; sleep 1; done; return 1; }
wait_not_playing() { for _ in $(seq 1 20); do playing || return 0; sleep 1; done; return 1; }

capture_battery() { # capture_battery <tag> <windowed|fullscreen> [record-seconds]
  local tag="$1" mode="$2" seconds="${3:-0}"
  "$TOOL" windows > "$OUT/$tag-window.json" 2>> "$OUT/commands.log" || { record "capture $tag" FAIL "app window not on screen"; return; }
  screencapture -x "$OUT/$tag-screencapture.png"
  "$TOOL" sck-shot "$OUT/$tag-screencapturekit.png" >> "$OUT/commands.log" 2>&1
  local frames=()
  if [ "$seconds" -gt 0 ]; then
    screencapture -x -v -V "$seconds" "$OUT/$tag-recording.mov" >> "$OUT/commands.log" 2>&1
    "$TOOL" frames "$OUT/$tag-recording.mov" "$OUT/$tag-recording-frame" "2,$((seconds / 2)),$((seconds - 2))" >> "$OUT/commands.log" 2>&1
    frames=("$OUT/$tag"-recording-frame-*.png)
  fi
  # ${frames[@]+…}: an empty array is "unbound" under set -u in macOS's bash 3.2.
  for png in "$OUT/$tag-screencapture.png" "$OUT/$tag-screencapturekit.png" ${frames[@]+"${frames[@]}"}; do
    [ -f "$png" ] || { record "capture $(basename "$png" .png)" INFO "no image produced"; continue; }
    local json verdict
    json="$("$TOOL" classify "$png" "$OUT/$tag-window.json" "$OUT/controls.json" "$mode" 2>> "$OUT/commands.log")"
    echo "$json" >> "$OUT/captures.jsonl"
    verdict="$(jq -r .verdict <<< "$json")"
    case "$verdict" in
      HIDDEN) record "capture $(basename "$png" .png)" PASS "app window hidden from capture" ;;
      VISIBLE) record "capture $(basename "$png" .png)" FAIL "protected app window VISIBLE in capture" ;;
      *) record "capture $(basename "$png" .png)" INFO "inconclusive: $json" ;;
    esac
  done
}

# ── 1. Environment ───────────────────────────────────────────────────────────
{ sw_vers; uname -m; csrutil status; } > "$OUT/environment.txt" 2>&1
record "macOS" INFO "$(sw_vers -productVersion) ($(sw_vers -buildVersion)) $(uname -m)"
if brew install displayplacer >> "$OUT/commands.log" 2>&1; then
  display_id="$(displayplacer list | awk '/Persistent screen id:/ {print $4; exit}')"
  displayplacer "id:$display_id res:1920x1080 hz:60 color_depth:8" >> "$OUT/commands.log" 2>&1 || true
fi
record "display" INFO "$(system_profiler SPDisplaysDataType | awk -F': ' '/Resolution/ {print $2; exit}')"
swiftc -O "$HERE/oetmac.swift" -o "$TOOL" >> "$OUT/commands.log" 2>&1 || { record "helper build" FAIL "swiftc failed"; exit 1; }
record "capture/automation permissions" INFO "$("$TOOL" preflight)"

# ── 2. Install the DMG ───────────────────────────────────────────────────────
mount="$(mktemp -d)"
hdiutil attach "$DMG" -nobrowse -readonly -mountpoint "$mount" >> "$OUT/commands.log" 2>&1
rm -rf "$APP"
ditto "$mount/OET with Dr. Hesham.app" "$APP"
hdiutil detach "$mount" >> "$OUT/commands.log" 2>&1
xattr -dr com.apple.quarantine "$APP" 2>/dev/null || true
codesign --verify "$APP" >> "$OUT/commands.log" 2>&1 || record "codesign" INFO "not signed with a Developer ID (expected until Apple secrets exist)"
"$LSREGISTER" -f -R -trusted "$APP"
record "app version" INFO "$(defaults read "$APP/Contents/Info" CFBundleShortVersionString)"

# ── 3. Preflight over the API ────────────────────────────────────────────────
code="$(api_sign_in)"
[ "$code" = 200 ] || { record "API sign-in" FAIL "HTTP $code (no retry, to avoid lockout)"; exit 1; }
TOKEN="$(api_token)"
declare -a BASE DURATION
for i in 0 1 2; do
  body="$(api_get "$TOKEN" "/v1/video-library/videos/${VIDS[$i]}")"
  jq -e '(.isAccessible // .video.isAccessible) == true' <<< "$body" > /dev/null \
    || { record "video $((i + 1)) access" FAIL "${VIDS[$i]} not accessible (HTTP $(cat "$OUT/last-code"))"; exit 1; }
  DURATION[i]="$(jq -r '(.durationSeconds // .video.durationSeconds // 0) | floor' <<< "$body")"
  # Start every run from 0 so progress proves THIS run's playback (and seek), not
  # an earlier run's. The server stores the posted position as-is.
  reset="$(api_post "$TOKEN" "/v1/video-library/videos/${VIDS[$i]}/progress" '{"positionSeconds":0}')"
  [ "$reset" = 200 ] || { record "video $((i + 1)) progress reset" FAIL "HTTP $reset"; exit 1; }
  BASE[i]=0
  record "video $((i + 1)) access" PASS "${VIDS[$i]} accessible; progress reset to 0 of ${DURATION[i]}s"
done

# ── 4. Controls, external-browser watcher, launch ────────────────────────────
"$TOOL" controls "$OUT/controls.json" &
CONTROLS_PID=$!
( while :; do
    pgrep -xq Safari && echo "$(date +%T) Safari"
    pgrep -fq 'Google Chrome.app|Firefox.app|Microsoft Edge.app' && echo "$(date +%T) other browser"
    sleep 0.5
  done > "$OUT/external-browser.log" ) &
WATCHER_PID=$!
lsappinfo list | grep -o 'bundleID="[^"]*"' | sort -u > "$OUT/apps-before.txt"

open -b "$BUNDLE_ID"
check "app launches" "$BUNDLE_ID running, web content loaded" "$TOOL" wait-webarea 90 || exit 1
"$TOOL" move-window 0 25
sleep 2
"$TOOL" windows > "$OUT/window.json"
if [ "$(jq -r .sharingState "$OUT/window.json")" = 0 ]; then
  record "window protection flag" PASS "kCGWindowSharingState=0 (sharingType none) from launch"
else
  record "window protection flag" FAIL "window sharing state $(jq -r .sharingState "$OUT/window.json")"
fi

# ── 5. Sign in through the app's own sign-in page ────────────────────────────
email_q="$(jq -rn --arg e "$OET_CI_LEARNER_EMAIL" '$e|@uri')"
open "oet-prep://sign-in?email=$email_q&next=%2Fvideos%2F${VIDS[0]}"
check "app sign-in form" "password typed into the secure field" "$TOOL" type-password 60 || { "$TOOL" dump "$OUT/ax-sign-in.txt"; exit 1; }
signed_in=false
for _ in $(seq 1 60); do
  api_get "$TOKEN" "/v1/video-library/videos/${VIDS[0]}" > /dev/null
  [ "$(cat "$OUT/last-code")" = 401 ] && { signed_in=true; break; }
  sleep 1
done
$signed_in && record "app sign-in" PASS "app session replaced the preflight session" \
  || { record "app sign-in" FAIL "app did not sign in within 60s"; "$TOOL" dump "$OUT/ax-after-sign-in.txt"; exit 1; }

player_ready() { # player_ready <n>
  local n="$1"
  if "$TOOL" wait-element Fullscreen 90 >> "$OUT/commands.log" 2>&1 \
    && "$TOOL" wait-element "~Protected course video" 10 >> "$OUT/commands.log" 2>&1; then
    record "video $n player" PASS "protected player session granted; Bunny iframe in the app window"
  else
    "$TOOL" dump "$OUT/ax-video-$n.txt"
    record "video $n player" FAIL "protected player did not load in the app window"
    return 1
  fi
}
toggle_play() { "$TOOL" focus "Video player" && "$TOOL" key space; }
start_playing() { # start_playing <n> — play, and only then judge "no 403 / error"
  local n="$1" text
  toggle_play
  if ! wait_playing; then record "video $n play" FAIL "no playback after Space"; return 1; fi
  record "video $n play" PASS "media playback assertion active"
  for text in "~Could not start playback" "~Secure display protection is unavailable" "~could not be verified" "~Forbidden"; do
    if "$TOOL" wait-element "$text" 1 >> "$OUT/commands.log" 2>&1; then
      record "video $n no 403 / error" FAIL "found: $text"
      return 1
    fi
  done
  record "video $n no 403 / error" PASS "playing in the app, no error panel or 403"
}
back_to_library() { # the page's own "Back to video library" link, not a deep link
  if "$TOOL" press "Back to video library" >> "$OUT/commands.log" 2>&1 \
    && "$TOOL" wait-element "~Video Library" 30 >> "$OUT/commands.log" 2>&1; then
    record "back to library ($1)" PASS "Back to video library link -> library page"
  else
    record "back to library ($1)" FAIL "library page not reached"
  fi
}

# ── 6. Video 1: play / pause, capture while loaded and while playing ─────────
if player_ready 1; then
  capture_battery v1-loaded windowed
  start_playing 1
  sleep 20
  capture_battery v1-playing windowed 20
  toggle_play
  if wait_not_playing; then record "video 1 pause" PASS "playback assertion released"; else record "video 1 pause" FAIL "still playing after Space"; fi
fi

# ── 7. Back to the library, video 2: seek ────────────────────────────────────
back_to_library "after video 1"
open "oet-prep://videos/${VIDS[1]}"
SEEKED=false
if player_ready 2; then
  start_playing 2
  sleep 15
  if "$TOOL" set-slider "~Seek" 50 >> "$OUT/commands.log" 2>&1; then
    SEEKED=true
    record "video 2 seek" INFO "Bunny seek bar set to 50% (verified by final progress)"
  else
    record "video 2 seek" NOT_AUTOMATED "Bunny seek bar not exposed to Accessibility inside the iframe"
  fi
  sleep 30
  toggle_play
  wait_not_playing && record "video 2 pause" PASS "playback assertion released" || record "video 2 pause" FAIL "still playing after Space"
fi

# ── 8. Video 3: fullscreen in / out (inside the same protected window) ───────
back_to_library "after video 2"
open "oet-prep://videos/${VIDS[2]}"
if player_ready 3; then
  start_playing 3
  "$TOOL" press Fullscreen
  sleep 5
  if [ "$("$TOOL" fullscreen-state)" = true ] && "$TOOL" wait-element "Exit fullscreen" 10 >> "$OUT/commands.log" 2>&1; then
    record "video 3 fullscreen in" PASS "app window native fullscreen, player filling it"
  else
    record "video 3 fullscreen in" FAIL "window fullscreen=$("$TOOL" fullscreen-state)"
  fi
  "$TOOL" windows > "$OUT/v3-fullscreen-window.json" 2>/dev/null
  [ "$(jq -r .sharingState "$OUT/v3-fullscreen-window.json" 2>/dev/null)" = 0 ] \
    && record "fullscreen window protection flag" PASS "same protected window (sharing state 0)" \
    || record "fullscreen window protection flag" FAIL "fullscreen window not protected"
  capture_battery v3-fullscreen fullscreen 15
  "$TOOL" press "Exit fullscreen"
  sleep 5
  [ "$("$TOOL" fullscreen-state)" = false ] && "$TOOL" wait-element Fullscreen 10 >> "$OUT/commands.log" 2>&1 \
    && record "video 3 fullscreen out" PASS "window back to normal" \
    || record "video 3 fullscreen out" FAIL "still fullscreen"
  sleep 20
  toggle_play
  wait_not_playing && record "video 3 pause" PASS "playback assertion released" || record "video 3 pause" FAIL "still playing after Space"
  "$TOOL" wait-element PIP 2 >> "$OUT/commands.log" 2>&1 \
    && record "picture-in-picture" FAIL "Bunny PIP control exposed (PiP window would escape protection)" \
    || record "picture-in-picture" PASS "no PiP control offered"
  back_to_library "after video 3"
fi

# ── 9. Quit, verify progress and external browsers ───────────────────────────
osascript -e "tell application id \"$BUNDLE_ID\" to quit" >> "$OUT/commands.log" 2>&1
sleep 5
kill "$WATCHER_PID" 2>/dev/null
DEVICE_ID="$(uuidgen | tr 'A-Z' 'a-z')"
code="$(api_sign_in)"
if [ "$code" = 200 ]; then
  TOKEN="$(api_token)"
  for i in 0 1 2; do
    now="$(api_get "$TOKEN" "/v1/video-library/videos/${VIDS[$i]}" | position)"
    if [ "$i" = 1 ] && $SEEKED; then
      want=$(( DURATION[1] * 45 / 100 ))
    else
      want=$(( BASE[i] + 20 ))
    fi
    [ "$now" -ge "$want" ] \
      && record "video $((i + 1)) progress" PASS "saved position ${BASE[i]}s → ${now}s (needed ≥ ${want}s)" \
      || record "video $((i + 1)) progress" FAIL "saved position ${BASE[i]}s → ${now}s (needed ≥ ${want}s)"
  done
else
  record "final progress check" FAIL "API sign-in HTTP $code"
fi
lsappinfo list | grep -o 'bundleID="[^"]*"' | sort -u > "$OUT/apps-after.txt"
new_browsers="$(comm -13 "$OUT/apps-before.txt" "$OUT/apps-after.txt" | grep -Ei 'safari|chrome|firefox|edgemac' || true)"
if [ -s "$OUT/external-browser.log" ] || [ -n "$new_browsers" ]; then
  record "no external browser" FAIL "$(head -3 "$OUT/external-browser.log") $new_browsers"
else
  record "no external browser" PASS "Safari/Chrome/Firefox/Edge never launched"
fi

# Summary, capture-evidence gate and exit code: finish() (EXIT trap).
