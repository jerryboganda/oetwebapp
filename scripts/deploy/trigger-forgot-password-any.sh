#!/usr/bin/env bash
# Parametrised forgot-password trigger.
set -euo pipefail
EMAIL="${1:?usage: $0 <email>}"

echo "=== Triggering /v1/auth/forgot-password for ${EMAIL} ==="

RESPONSE_FILE=$(mktemp)
HTTP=$(curl -sS -o "$RESPONSE_FILE" -w '%{http_code}' \
    -X POST https://api.oetwithdrhesham.co.uk/v1/auth/forgot-password \
    -H 'Content-Type: application/json' \
    -H 'Accept: application/json' \
    --data-binary "{\"email\":\"${EMAIL}\"}")

echo "HTTP status: $HTTP"
echo "Response body:"
cat "$RESPONSE_FILE"
echo ""
rm -f "$RESPONSE_FILE"

echo ""
echo "=== API logs immediately after (last 10 email-related lines) ==="
docker logs oet-api --since 2m 2>&1 | grep -iE 'forgot|otp|reset|brevo|smtp|sending email|email sent' | tail -10 || true
