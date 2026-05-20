#!/usr/bin/env bash
# Schedules a delayed snapshot — run once via ssh, returns immediately
# Usage: bash /tmp/schedsnap.sh <sleep_seconds>
SECS="${1:-720}"
SNAP_SH=/tmp/sn.sh
OUT=/tmp/snap.out
(
  sleep "$SECS"
  bash "$SNAP_SH" > "$OUT" 2>&1
) </dev/null >/dev/null 2>&1 &
disown 2>/dev/null || true
echo "scheduled snapshot in ${SECS}s, output -> $OUT"
