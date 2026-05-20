#!/usr/bin/env bash
set -uo pipefail
cd /opt/oetwebapp
echo "--- envrc lines (key names only) ---"
sed -E 's/(=).*/\1<REDACTED>/' scripts/admin/.envrc
echo ""
echo "--- production env file location? ---"
ls -la .env* 2>&1 | head -5
grep -l 'AI__ApiKey' .env* 2>&1 || echo no_match
echo ""
echo "--- AI__ApiKey present in any .env file ---"
for f in .env .env.production .env.local; do
  [ -f "$f" ] || continue
  if grep -q '^AI__ApiKey=' "$f"; then
    VAL=$(grep '^AI__ApiKey=' "$f" | sed 's/^AI__ApiKey=//')
    LEN=${#VAL}
    echo "$f : AI__ApiKey is set (len=$LEN)"
  else
    echo "$f : no AI__ApiKey line"
  fi
done
