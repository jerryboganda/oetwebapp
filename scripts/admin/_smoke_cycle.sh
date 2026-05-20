#!/usr/bin/env bash
set -e
TOKEN=$(curl -sS -X POST https://api.oetwithdrhesham.co.uk/v1/auth/sign-in \
  -H 'Content-Type: application/json' \
  -d '{"email":"manwara575@gmail.com","password":"12345678"}' \
  | python3 -c 'import sys,json; d=json.load(sys.stdin); print(d.get("accessToken") or d.get("token") or "")')
echo "TOKLEN=${#TOKEN}"
PID=cd17cff04b9d40e486035bf2f90411ea
for PATH_SUFFIX in "papers/$PID" "papers/$PID/assets" "papers?pageSize=5"; do
  echo "== /v1/admin/$PATH_SUFFIX =="
  HTTP=$(curl -sS -o /tmp/r.json -w '%{http_code}' \
    "https://api.oetwithdrhesham.co.uk/v1/admin/$PATH_SUFFIX" \
    -H "Authorization: Bearer $TOKEN")
  echo "HTTP=$HTTP"
  head -c 400 /tmp/r.json
  echo
done
echo '--- recent api errors ---'
docker logs --since 2m oet-api-green 2>&1 \
  | grep -E 'JsonException|cycle|ERROR|Exception' | tail -5 \
  || echo NONE