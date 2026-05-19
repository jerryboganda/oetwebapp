#!/bin/bash
# Wait for a PID then run a follow-up bulk launch (chained extension).
set -u
WAIT_PID=$1
SCRIPT=$2
COUNT=$3
LOG=$4
echo "chain: waiting for pid $WAIT_PID before running $SCRIPT --count $COUNT --resume" >> "$LOG"
while kill -0 $WAIT_PID 2>/dev/null; do sleep 30; done
echo "chain: launching $SCRIPT --count $COUNT --resume at $(date -Is)" >> "$LOG"
cd /opt/oetwebapp
exec bash scripts/admin/run-bulk.sh $SCRIPT --count $COUNT --resume >> "$LOG" 2>&1
