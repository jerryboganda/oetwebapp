#!/usr/bin/env bash
set -euo pipefail
TS="$(date +%Y%m%d-%H%M%S)"
BK="/tmp/oet-prod-backup-${TS}.sql"

echo "===== container status ====="
timeout 30 docker ps --filter name=oet-postgres --format '{{.Names}} {{.Status}} {{.Image}}' || true

echo "===== taking backup -> ${BK} ====="
timeout 300 docker exec oet-postgres pg_dump -U oet_learner -d oet_learner > "${BK}"
ls -la "${BK}"
echo "backup line count:"
wc -l "${BK}"
echo "backup head:"
head -n 15 "${BK}"

echo "===== PROD users (email/role) ====="
timeout 60 docker exec oet-postgres psql -U oet_learner -d oet_learner -P pager=off -c 'select "Email","Role","CreatedAtUtc" from "Users" order by "CreatedAtUtc"' || true

echo "===== PROD migrations count + last 3 ====="
timeout 60 docker exec oet-postgres psql -U oet_learner -d oet_learner -tAc 'select count(*) from "__EFMigrationsHistory"' || true
timeout 60 docker exec oet-postgres psql -U oet_learner -d oet_learner -P pager=off -c 'select "MigrationId" from "__EFMigrationsHistory" order by "MigrationId" desc limit 3' || true

echo "===== PROD top tables ====="
timeout 60 docker exec oet-postgres psql -U oet_learner -d oet_learner -P pager=off -c 'select relname, n_live_tup from pg_stat_user_tables where n_live_tup > 0 order by n_live_tup desc limit 50' || true

echo "===== DONE ${TS} ====="
