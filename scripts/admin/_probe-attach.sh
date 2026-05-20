#!/bin/bash
# Probe attach endpoint: capture response body
set -eu
cd /opt/oetwebapp/scripts/admin
. ./.envrc
TOKEN=$(curl -sk -X POST "$API_BASE/v1/auth/sign-in" -H 'content-type: application/json' \
  -d "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\"}" | jq -r .accessToken)
echo "TOKEN_PREFIX=${TOKEN:0:30}..."
PAPER=ace9585ac0974f52b27d453502352dc4
echo "--- GET paper detail ---"
curl -sk "$API_BASE/v1/admin/papers/$PAPER" -H "authorization: bearer $TOKEN" | jq '{id, status, subtest:.subtestCode, assets:[.assets[]?|{role, part, mediaAssetId, isPrimary}]}'
echo "--- probe attach with bogus mediaAssetId ---"
curl -sk -i -X POST "$API_BASE/v1/admin/papers/$PAPER/assets" \
  -H "authorization: bearer $TOKEN" -H 'content-type: application/json' \
  -d '{"role":"Audio","mediaAssetId":"deadbeefdeadbeefdeadbeefdeadbeef","part":"A1","title":"probe","displayOrder":1,"makePrimary":true}'
echo
echo "--- list recent MediaAssets created in last 30 min ---"
docker exec -i oet-postgres psql -U oet_learner -d oet_learner -c \
  "SELECT \"Id\", \"CreatedAt\", \"Kind\", \"OriginalFileName\" FROM \"MediaAssets\" WHERE \"CreatedAt\" > NOW() - INTERVAL '30 minutes' ORDER BY \"CreatedAt\" DESC LIMIT 10;"
