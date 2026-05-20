#!/usr/bin/env bash
sleep 180
echo "=== ORCHESTRATORS ==="
ps -eo pid,etime,cmd | grep -E 'node scripts/admin/(generate-|publish-)' | grep -v grep || echo '(none running)'
echo
for s in reading listening publish-vocab; do
  echo "=== $s ==="
  if [ -f "/tmp/$s-live.log" ]; then
    tail -8 "/tmp/$s-live.log"
  elif [ -f "/tmp/generate-$s-live.log" ]; then
    tail -8 "/tmp/generate-$s-live.log"
  fi
done
