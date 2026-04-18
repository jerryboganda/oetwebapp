#!/usr/bin/env bash
# Check api logs around the sign-in attempt + confirm account state
set -euo pipefail
cd /root/oetwebsite
# shellcheck disable=SC2046
export $(grep -E '^POSTGRES_(USER|DB|PASSWORD)=' .env.production | xargs)

echo "=== Account state for manwara575@gmail.com ==="
docker exec oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c '
SELECT "Id", "Email", "NormalizedEmail", "Role",
       "EmailVerifiedAt", "AuthenticatorEnabledAt", "DeletedAt",
       "LastLoginAt", "UpdatedAt",
       LENGTH("PasswordHash") AS pwd_hash_len,
       SUBSTRING("PasswordHash", 1, 3) AS pwd_hash_prefix
  FROM "ApplicationUserAccounts"
 WHERE "NormalizedEmail" = '"'"'MANWARA575@GMAIL.COM'"'"';'

echo ""
echo "=== Latest OTP challenge state ==="
docker exec oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "
SELECT c.\"Id\", c.\"Purpose\", c.\"CreatedAt\", c.\"ExpiresAt\", c.\"VerifiedAt\", c.\"AttemptCount\"
  FROM \"EmailOtpChallenges\" c
  JOIN \"ApplicationUserAccounts\" a ON a.\"Id\" = c.\"ApplicationUserAccountId\"
 WHERE a.\"NormalizedEmail\" = 'MANWARA575@GMAIL.COM'
 ORDER BY c.\"CreatedAt\" DESC
 LIMIT 3;"

echo ""
echo "=== API logs last 5 min: errors / sign-in / reset ==="
docker logs oet-api --since 5m 2>&1 | grep -iE 'error|exception|fail|manwara|sign-in|reset' | tail -40 || true
