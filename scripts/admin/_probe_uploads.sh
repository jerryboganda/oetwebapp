#!/bin/bash
set -eu
BASE="${API_BASE:?API_BASE env var required}"
: "${ADMIN_EMAIL:?ADMIN_EMAIL env var required}"
: "${ADMIN_PASSWORD:?ADMIN_PASSWORD env var required}"
TOKEN=$(curl -s -X POST "$BASE/v1/auth/sign-in" \
  -H 'Content-Type: application/json' \
  -d "$(python3 - <<'PY'
import json, os
print(json.dumps({"email": os.environ["ADMIN_EMAIL"], "password": os.environ["ADMIN_PASSWORD"]}))
PY
)" \
  | python3 -c 'import sys,json;print(json.load(sys.stdin)["accessToken"])')
echo "token_len=${#TOKEN}"
echo "--- POST /v1/admin/uploads ---"
curl -s -X POST "$BASE/v1/admin/uploads" \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"originalFilename":"test.txt","declaredMimeType":"text/plain","declaredSizeBytes":11,"intendedRole":"Supplementary"}' \
  -w '\nHTTP=%{http_code}\n'
