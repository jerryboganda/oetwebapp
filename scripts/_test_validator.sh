#!/bin/bash
# Test validator: sign in as admin, start upload, send 1 part with txt content, complete.
set -e
API="https://api.oetwithdrhesham.co.uk"
# Sign in
TOK=$(curl -sS -X POST "$API/v1/auth/sign-in" -H 'content-type: application/json' \
  -d '{"email":"manwara575@gmail.com","password":"12345678"}' | jq -r '.accessToken // .access_token')
echo "Token len: ${#TOK}"
[ -z "$TOK" ] || [ "$TOK" = "null" ] && { echo "SIGN-IN FAILED"; exit 1; }

# Start session for .txt
START=$(curl -sS -X POST "$API/v1/admin/uploads" -H "authorization: Bearer $TOK" -H 'content-type: application/json' \
  -d '{"originalFilename":"validator-test.txt","declaredSizeBytes":1024,"declaredMimeType":"text/plain","intendedRole":"QuestionPaper"}')
echo "Start: $START"
SID=$(echo "$START" | jq -r '.uploadId // .sessionId // .id')
PARTSIZE=$(echo "$START" | jq -r '.chunkSizeBytes // .partSize // .chunkSize // 5242880')
echo "Session: $SID partSize: $PARTSIZE"

# Upload single part with text
BODY="READING PAPER — validator smoke test. Plain ASCII text content."
BYTES=$(printf '%s' "$BODY" | wc -c)
echo "body bytes: $BYTES"
# re-start with correct declared size
START=$(curl -sS -X POST "$API/v1/admin/uploads" -H "authorization: Bearer $TOK" -H 'content-type: application/json' \
  -d "{\"originalFilename\":\"validator-test.txt\",\"declaredSizeBytes\":$BYTES,\"declaredMimeType\":\"text/plain\",\"intendedRole\":\"QuestionPaper\"}")
echo "Start2: $START"
SID=$(echo "$START" | jq -r '.uploadId // .sessionId // .id')
curl -sS -X PUT "$API/v1/admin/uploads/$SID/parts/1" -H "authorization: Bearer $TOK" \
  -H 'content-type: application/octet-stream' --data-binary "$BODY" -w "\nPUT http=%{http_code}\n"

# Complete
COMPLETE=$(curl -sS -X POST "$API/v1/admin/uploads/$SID/complete" -H "authorization: Bearer $TOK" -H 'content-type: application/json' -d '{}' -w "\nhttp=%{http_code}")
echo "Complete: $COMPLETE"
