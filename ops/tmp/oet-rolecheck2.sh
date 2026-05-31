#!/usr/bin/env bash
set +e
echo '=== whoami / roles (as oetlearner) ==='
timeout 20 docker exec oet-postgres psql -U oetlearner -d oetlearner -tAc "select rolname from pg_roles order by 1;" 2>&1 || echo roles_failed
echo '=== databases ==='
timeout 20 docker exec oet-postgres psql -U oetlearner -d oetlearner -tAc "select datname from pg_database order by 1;" 2>&1 || echo db_failed
echo '=== Users row count ==='
timeout 20 docker exec oet-postgres psql -U oetlearner -d oetlearner -tAc "select count(*) from \"Users\";" 2>&1 || echo userscount_failed
echo '=== RuntimeSettings row count ==='
timeout 20 docker exec oet-postgres psql -U oetlearner -d oetlearner -tAc "select count(*) from \"RuntimeSettings\";" 2>&1 || echo rscount_failed
echo '=== Does LiveClassesAiRecordingProcessingEnabled column exist? ==='
timeout 20 docker exec oet-postgres psql -U oetlearner -d oetlearner -tAc "select column_name from information_schema.columns where table_name='RuntimeSettings' and column_name='LiveClassesAiRecordingProcessingEnabled';" 2>&1 || echo colcheck_failed
echo '=== DONE ==='
