#!/usr/bin/env bash
set -euo pipefail
cd /root/oetwebsite
# shellcheck disable=SC2046
export $(grep -E '^POSTGRES_(USER|DB|PASSWORD)=' .env.production | xargs)

echo "=== How all admins + mindreader are actually stored ==="
docker exec oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c '
SELECT "Id", "Email", "NormalizedEmail"
  FROM "ApplicationUserAccounts"
 WHERE "NormalizedEmail" ILIKE '"'"'%manwara%'"'"'
    OR "NormalizedEmail" ILIKE '"'"'%mindreader%'"'"'
    OR "Role" = '"'"'admin'"'"'
 ORDER BY "CreatedAt";'

echo ""
echo "=== What NormalizedEmail should look like for a learner (control sample) ==="
docker exec oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c '
SELECT "Email", "NormalizedEmail"
  FROM "ApplicationUserAccounts"
 WHERE "Role" = '"'"'learner'"'"'
 LIMIT 3;'
