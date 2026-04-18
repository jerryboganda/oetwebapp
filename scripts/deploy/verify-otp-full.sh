#!/usr/bin/env bash
set -euo pipefail
cd /root/oetwebsite
# shellcheck disable=SC2046
export $(grep -E '^POSTGRES_(USER|DB|PASSWORD)=' .env.production | xargs)

echo "=== EmailOtpChallenges columns ==="
docker exec oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c '
SELECT column_name FROM information_schema.columns
 WHERE table_name = '"'"'EmailOtpChallenges'"'"'
 ORDER BY ordinal_position;'

echo ""
echo "=== Recent OTP challenges for manwara575 ==="
docker exec oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "
SELECT c.\"Id\", c.\"Purpose\", c.\"CreatedAt\", c.\"ExpiresAt\", c.\"VerifiedAt\", a.\"Email\"
  FROM \"EmailOtpChallenges\" c
  JOIN \"ApplicationUserAccounts\" a ON a.\"Id\" = c.\"ApplicationUserAccountId\"
 WHERE a.\"NormalizedEmail\" = 'manwara575@gmail.com'
 ORDER BY c.\"CreatedAt\" DESC
 LIMIT 3;"

echo ""
echo "=== Latest 30 lines of oet-api logs (all) ==="
docker logs oet-api --tail 30 2>&1
