#!/usr/bin/env bash
# Verify the OTP challenge was created + email was attempted.
set -euo pipefail
cd /root/oetwebsite
# shellcheck disable=SC2046
export $(grep -E '^POSTGRES_(USER|DB|PASSWORD)=' .env.production | xargs)

echo "=== EmailOtp table name ==="
docker exec oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At -c "
SELECT tablename FROM pg_tables
 WHERE schemaname='public' AND tablename ~* 'otp';"

echo ""
echo "=== Full wide log scan (last 10 min) ==="
docker logs oet-api --since 10m 2>&1 | grep -iE '(otp|manwara|smtp|mail|brevo|email)' | tail -20 || true
