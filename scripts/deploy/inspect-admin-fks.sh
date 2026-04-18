#!/usr/bin/env bash
# Inspect foreign-key references to ApplicationUserAccounts so we know what
# will be affected by deleting the old admin row.
set -euo pipefail
cd /root/oetwebsite
# shellcheck disable=SC2046
export $(grep -E '^POSTGRES_(USER|DB|PASSWORD)=' .env.production | xargs)

OLD_ADMIN_ID='auth_admin_146163e5a8f5457b9c6d3'

echo "=== FKs pointing at ApplicationUserAccounts ==="
docker exec oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "
SELECT
    tc.table_name AS referencing_table,
    kcu.column_name AS referencing_column,
    rc.delete_rule AS on_delete
  FROM information_schema.table_constraints tc
  JOIN information_schema.key_column_usage kcu
    ON tc.constraint_name = kcu.constraint_name
  JOIN information_schema.referential_constraints rc
    ON tc.constraint_name = rc.constraint_name
  JOIN information_schema.constraint_column_usage ccu
    ON rc.unique_constraint_name = ccu.constraint_name
 WHERE tc.constraint_type = 'FOREIGN KEY'
   AND ccu.table_name = 'ApplicationUserAccounts'
 ORDER BY tc.table_name;"

echo ""
echo "=== Rows referencing the old admin id ==="
docker exec oet-postgres psql -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -c "
SELECT 'RefreshTokenRecords' AS tbl, COUNT(*) FROM \"RefreshTokenRecords\" WHERE \"ApplicationUserAccountId\" = '${OLD_ADMIN_ID}'
UNION ALL SELECT 'EmailOtpRecords', COUNT(*) FROM \"EmailOtpRecords\" WHERE \"ApplicationUserAccountId\" = '${OLD_ADMIN_ID}'
UNION ALL SELECT 'AiUsageRecords', COUNT(*) FROM \"AiUsageRecords\" WHERE \"AuthAccountId\" = '${OLD_ADMIN_ID}';" 2>&1 || true

echo ""
echo "=== Sanity: old admin still exists? ==="
docker exec oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At -c "
SELECT \"Id\", \"Email\", \"Role\" FROM \"ApplicationUserAccounts\" WHERE \"Id\" = '${OLD_ADMIN_ID}';"
