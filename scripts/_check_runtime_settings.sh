#!/usr/bin/env bash
set -e
echo '=== Existing PayPal/Brevo/etc columns ==='
docker exec oet-postgres psql -U oet_learner -d oet_learner -c "
SELECT column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_name = 'RuntimeSettings'
ORDER BY column_name;
"
echo
echo '=== Latest applied migrations ==='
docker exec oet-postgres psql -U oet_learner -d oet_learner -c "
SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\" DESC LIMIT 15;
"
