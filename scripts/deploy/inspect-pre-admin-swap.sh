#!/usr/bin/env bash
# Quick lookup: does manwara575@gmail.com already exist? Also confirm the
# exact Role string values used (admin/learner/expert).
set -euo pipefail
cd /root/oetwebsite
# shellcheck disable=SC2046
export $(grep -E '^POSTGRES_(USER|DB|PASSWORD)=' .env.production | xargs)

echo "=== Does manwara575@gmail.com exist? ==="
docker exec oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "
SELECT \"Id\", \"Email\", \"Role\", \"EmailVerifiedAt\", \"CreatedAt\"
  FROM \"ApplicationUserAccounts\"
 WHERE \"NormalizedEmail\" = 'manwara575@gmail.com';"

echo ""
echo "=== Distinct Role values in use ==="
docker exec oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c '
SELECT "Role", COUNT(*) FROM "ApplicationUserAccounts"
 GROUP BY "Role" ORDER BY "Role";'

echo ""
echo "=== Current mindreader420123 state (role + learner profile?) ==="
docker exec oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "
SELECT a.\"Id\" AS auth_id, a.\"Email\", a.\"Role\",
       (SELECT COUNT(*) FROM \"Users\" u WHERE u.\"AuthAccountId\" = a.\"Id\") AS learner_rows,
       (SELECT COUNT(*) FROM \"ExpertUsers\" e WHERE e.\"AuthAccountId\" = a.\"Id\") AS expert_rows
  FROM \"ApplicationUserAccounts\" a
 WHERE a.\"NormalizedEmail\" = 'mindreader420123@gmail.com';"
