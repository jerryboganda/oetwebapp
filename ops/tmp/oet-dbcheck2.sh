#!/usr/bin/env bash
set +e
echo '=== BUILD ==='
pgrep -f 'docker-compose.vps.yml build' >/dev/null && echo BUILD_RUNNING || echo BUILD_DONE
echo '=== POSTGRES STATUS ==='
docker ps --filter name=oet-postgres --format '{{.Names}} {{.Status}}'
echo '=== COLUMN CHECK ==='
timeout 15 docker exec oet-postgres psql -U oetwithdrhesham -d oetwithdrhesham -tAc "select column_name from information_schema.columns where table_name='RuntimeSettings' and column_name='LiveClassesAiRecordingProcessingEnabled';" 2>&1 || echo psql_failed_or_timeout
echo '=== MIGRATION HEAD ==='
timeout 15 docker exec oet-postgres psql -U oetwithdrhesham -d oetwithdrhesham -tAc 'select max("MigrationId") from "__EFMigrationsHistory";' 2>&1 || echo psql_failed_or_timeout
echo '=== FRESH IMAGES ==='
docker images --format '{{.Repository}}:{{.Tag}} {{.CreatedSince}}' | grep -E 'oetwebsite-web:local|oetwebsite-learner-api:local' || echo no_images
echo '=== DONE ==='
