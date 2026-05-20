#!/bin/bash
set -e
H="api.oetwithdrhesham.co.uk"
RESP=$(curl -sS -X POST "https://${H}/v1/auth/sign-in" \
  -H 'Content-Type: application/json' \
  -d '{"email":"mindreader420123@gmail.com","password":"12345678"}')
TOKEN=$(echo "$RESP" | python3 -c 'import sys,json;d=json.load(sys.stdin);print(d.get("accessToken") or d.get("token") or "")')
PID="c5e5f35210ad4f00b4cf0ed45cb1ec5f"
echo "=== GET /v1/papers/$PID ==="
BODY=$(curl -sS -w "\nHTTP:%{http_code}" "https://${H}/v1/papers/${PID}" -H "Authorization: Bearer $TOKEN")
echo "$BODY" | head -c 4000
echo
SIGNED=$(echo "$BODY" | python3 -c '
import sys,json,re
text=sys.stdin.read()
m=re.search(r"\{.*\}", text, re.S)
if not m: print(""); sys.exit(0)
d=json.loads(m.group(0))
for a in d.get("assets", []):
  md=a.get("media") or {}
  dp=md.get("downloadPath")
  if dp: print(dp); break
')
echo "=== signed path: $SIGNED ==="
if [ -n "$SIGNED" ]; then
  echo "=== HEAD signed url ==="
  curl -sS -I "https://${H}${SIGNED}" | head -20
fi

