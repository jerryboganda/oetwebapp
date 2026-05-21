#!/usr/bin/env bash
# Get a fresh admin access token. Echoes the token to stdout.
set -euo pipefail
EMAIL="${QA_EMAIL:-manwara575@gmail.com}"
PASSWORD="${QA_PASSWORD:-12345678}"
API="${QA_API:-https://api.oetwithdrhesham.co.uk}"
curl -sS -X POST "$API/v1/auth/sign-in" \
  -H 'Content-Type: application/json' \
  -H 'Origin: https://app.oetwithdrhesham.co.uk' \
  -d "{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\",\"rememberMe\":false}" \
  | python3 -c 'import json,sys;print(json.load(sys.stdin)["accessToken"])'
