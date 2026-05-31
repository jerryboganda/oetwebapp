#!/usr/bin/env bash
set +e
echo '=== COLUMN CHECK ==='
docker exec oet-postgres psql -U oetlearner -d oetlearner -tAc "select column_name from information_schema.columns where table_name='RuntimeSettings' and column_name='LiveClassesAiRecordingProcessingEnabled';" 2>&1 || echo psql_failed
echo '=== MIGRATION HEAD ==='
docker exec oet-postgres psql -U oetlearner -d oetlearner -tAc 'select max("MigrationId") from "__EFMigrationsHistory";' 2>&1 || echo psql_failed
echo '=== BUILD ==='
pgrep -f 'docker-compose.vps.yml build' >/dev/null && echo BUILD_RUNNING || echo BUILD_DONE
echo '=== FRESH IMAGES ==='
docker images --format '{{.Repository}}:{{.Tag}} {{.CreatedSince}}' | grep -E 'oetwebsite-web:local|oetwebsite-learner-api:local'
