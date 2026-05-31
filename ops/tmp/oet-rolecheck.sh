#!/usr/bin/env bash
set +e
echo '=== ROLES ==='
timeout 20 docker exec oet-postgres psql -U postgres -tAc "select rolname from pg_roles order by 1;" 2>&1 || echo roles_failed
echo '=== DATABASES ==='
timeout 20 docker exec oet-postgres psql -U postgres -tAc "select datname from pg_database order by 1;" 2>&1 || echo db_failed
echo '=== TABLE COUNT in oetlearner ==='
timeout 20 docker exec oet-postgres psql -U postgres -d oetlearner -tAc "select count(*) from information_schema.tables where table_schema='public';" 2>&1 || echo tablecount_failed
echo '=== Users row count ==='
timeout 20 docker exec oet-postgres psql -U postgres -d oetlearner -tAc "select count(*) from \"Users\";" 2>&1 || echo userscount_failed
echo '=== DONE ==='
