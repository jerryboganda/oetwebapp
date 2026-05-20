#!/bin/bash
set -eu
cd /opt/oetwebapp/scripts/admin
. ./.envrc
TOKEN=$(curl -sk -X POST "$API_BASE/v1/auth/sign-in" -H 'content-type: application/json' \
  -d "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\"}" | jq -r .accessToken)
PAPER=ace9585ac0974f52b27d453502352dc4
ASSET=14521a27684643f2bf40590e2c9dba34
attach() {
  local NAME="$1"; shift
  local BODY="$1"; shift
  echo "=== $NAME ==="
  curl -sk -i -X POST "$API_BASE/v1/admin/papers/$PAPER/assets" \
    -H "authorization: bearer $TOKEN" -H 'content-type: application/json' \
    -d "$BODY" | head -15
  echo
}
attach "role string Audio" "{\"role\":\"Audio\",\"mediaAssetId\":\"$ASSET\",\"part\":\"A1\",\"title\":\"x\",\"displayOrder\":1,\"makePrimary\":true}"
attach "role int 0" "{\"role\":0,\"mediaAssetId\":\"$ASSET\",\"part\":\"A1\",\"title\":\"x\",\"displayOrder\":1,\"makePrimary\":true}"
attach "minimal" "{\"role\":\"Audio\",\"mediaAssetId\":\"$ASSET\"}"
attach "AudioScript text" "{\"role\":\"AudioScript\",\"mediaAssetId\":\"4ed03289defe4495af60aa70c86a83cd\"}"
echo "--- recent kestrel logs ---"
SLOT=$(cat /opt/oetwebapp/scripts/deploy/.active-slot 2>/dev/null || echo green)
docker logs --tail 200 oet-api-$SLOT 2>&1 | grep -iE 'paper|asset|warn|fail|error' | tail -40
