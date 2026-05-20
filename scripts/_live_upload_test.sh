#!/bin/bash
# Live test: trigger PUT and check immediately if file persists on disk.
set -e

cd /opt/oetwebapp

# 1) Get a fresh admin token by reading from env or from .env.production
ADMIN_TOKEN=$(cat /tmp/admin_token.txt 2>/dev/null || echo "")
if [ -z "$ADMIN_TOKEN" ]; then
  echo "Need /tmp/admin_token.txt with current bearer token. Will try login..."
  ADMIN_TOKEN=$(curl -sS -X POST https://api.oetwithdrhesham.co.uk/v1/auth/sign-in \
    -H 'Content-Type: application/json' \
    -d '{"email":"manwara575@gmail.com","password":"12345678"}' | jq -r '.accessToken' 2>/dev/null || echo "")
  if [ -z "$ADMIN_TOKEN" ] || [ "$ADMIN_TOKEN" = "null" ]; then
    echo "Could not get token via sign-in. Skipping live test."
    exit 1
  fi
fi
echo "Token: ${ADMIN_TOKEN:0:20}..."

# 2) Start an upload session
SESSION=$(curl -sS -X POST https://api.oetwithdrhesham.co.uk/v1/admin/uploads \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"originalFilename":"test-debug.txt","mimeType":"text/plain","extension":"txt","declaredSizeBytes":42,"totalParts":1,"kind":"document"}')
echo "START response: $SESSION"
UID_S=$(echo "$SESSION" | jq -r '.sessionId // .id // empty')
ADM=$(echo "$SESSION" | jq -r '.adminUserId // empty')
echo "sessionId=$UID_S adminId=$ADM"
if [ -z "$UID_S" ]; then echo "no session id"; exit 1; fi

# 3) PUT one part
echo "Hello world from debug test. 42-bytes exactly!!" > /tmp/dbg_part.bin
SZ=$(stat -c%s /tmp/dbg_part.bin)
echo "part size on disk: $SZ (should be 48 actually... let's use a 42-byte payload)"
printf '012345678901234567890123456789012345678901' > /tmp/dbg_part.bin
SZ=$(stat -c%s /tmp/dbg_part.bin)
echo "part size: $SZ"

PUT_RESP=$(curl -sS -X PUT "https://api.oetwithdrhesham.co.uk/v1/admin/uploads/$UID_S/parts/1" \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H 'Content-Type: application/octet-stream' \
  --data-binary @/tmp/dbg_part.bin -w '\nHTTP=%{http_code}\n')
echo "PUT response: $PUT_RESP"

# 4) IMMEDIATELY check disk
echo "=== disk state right after PUT ==="
docker exec oet-api-green find /var/opt/oet-learner/storage/uploads/staging/ -type f -name '*.bin' -newer /tmp/dbg_part.bin 2>&1 || true
docker exec oet-api-green ls -la "/var/opt/oet-learner/storage/uploads/staging/$ADM/$UID_S/" 2>&1 || true

# 5) Try Complete
echo "=== try complete ==="
curl -sS -X POST "https://api.oetwithdrhesham.co.uk/v1/admin/uploads/$UID_S/complete" \
  -H "Authorization: Bearer $ADMIN_TOKEN" -w '\nHTTP=%{http_code}\n'
echo
echo "=== disk after complete ==="
docker exec oet-api-green ls -la "/var/opt/oet-learner/storage/uploads/staging/$ADM/$UID_S/" 2>&1 || true
