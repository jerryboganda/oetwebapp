#!/usr/bin/env bash
set -euo pipefail
EMAIL="mindreader420123@gmail.com"

echo "=== Triggering /v1/auth/forgot-password for ${EMAIL} ==="

# Probe the exact contract first — some endpoints want the email as a form
# field, others as JSON. Try JSON first (what ForgotPasswordRequest expects).
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
echo "=== API logs immediately after (last 20 email-related lines) ==="
docker logs oet-api --since 2m 2>&1 | grep -iE 'forgot|otp|reset|brevo|smtp|mail' | tail -20 || true
