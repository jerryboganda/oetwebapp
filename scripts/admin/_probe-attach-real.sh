#!/bin/bash
set -eu
cd /opt/oetwebapp/scripts/admin
. ./.envrc
TOKEN=$(curl -sk -X POST "$API_BASE/v1/auth/sign-in" -H 'content-type: application/json' \
  -d "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\"}" | jq -r .accessToken)
PAPER=ace9585ac0974f52b27d453502352dc4
ASSET=14521a27684643f2bf40590e2c9dba34  # real audio asset for A1
echo "--- attempt attach with REAL mediaAssetId ($ASSET) ---"
curl -sk -i -X POST "$API_BASE/v1/admin/papers/$PAPER/assets" \
  -H "authorization: bearer $TOKEN" -H 'content-type: application/json' \
  -d "{\"role\":\"Audio\",\"mediaAssetId\":\"$ASSET\",\"part\":\"A1\",\"title\":\"Listening Part A1\",\"displayOrder\":1,\"makePrimary\":true}"
echo
echo
echo "--- tail oet-api logs ---"
SLOT=$(cat /opt/oetwebapp/scripts/deploy/.active-slot 2>/dev/null || echo green)
docker logs --tail 80 oet-api-$SLOT 2>&1 | tail -60
