#!/bin/bash
set -e
BASE="https://api.oetwithdrhesham.co.uk"
EMAIL="mindreader420123@gmail.com"
PASS="12345678"

echo "=== Login ==="
LOGIN=$(curl -sS -X POST "$BASE/v1/auth/sign-in" -H "Content-Type: application/json" \
  -d "{\"email\":\"$EMAIL\",\"password\":\"$PASS\"}")
echo "$LOGIN" | head -c 400
echo

TOKEN=$(echo "$LOGIN" | python3 -c 'import sys,json;d=json.load(sys.stdin);print(d.get("accessToken") or d.get("access_token") or "")')
if [ -z "$TOKEN" ]; then echo "NO TOKEN"; exit 1; fi
echo "Token len: ${#TOKEN}"
echo

echo "=== GET /v1/vocabulary/stats ==="
curl -sS "$BASE/v1/vocabulary/stats" -H "Authorization: Bearer $TOKEN" | head -c 600
echo
echo

echo "=== GET /v1/vocabulary/recall-sets?examTypeCode=oet ==="
curl -sS "$BASE/v1/vocabulary/recall-sets?examTypeCode=oet" -H "Authorization: Bearer $TOKEN" | head -c 800
echo
echo

echo "=== GET /v1/vocabulary/categories?examTypeCode=oet ==="
curl -sS "$BASE/v1/vocabulary/categories?examTypeCode=oet" -H "Authorization: Bearer $TOKEN" | head -c 800
echo
echo

echo "=== GET /v1/vocabulary/terms?examTypeCode=oet&page=1&pageSize=3 ==="
curl -sS "$BASE/v1/vocabulary/terms?examTypeCode=oet&page=1&pageSize=3" -H "Authorization: Bearer $TOKEN" | head -c 1200
echo
echo

echo "=== GET /v1/recalls/today ==="
curl -sS "$BASE/v1/recalls/today" -H "Authorization: Bearer $TOKEN" | head -c 800
echo
echo

echo "=== GET /v1/recalls/queue?limit=10 ==="
curl -sS "$BASE/v1/recalls/queue?limit=10" -H "Authorization: Bearer $TOKEN" | head -c 800
echo
echo

echo "=== GET /v1/recalls/library ==="
curl -sS "$BASE/v1/recalls/library" -H "Authorization: Bearer $TOKEN" | head -c 800
echo
