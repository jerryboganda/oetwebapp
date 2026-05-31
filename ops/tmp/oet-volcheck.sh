#!/usr/bin/env bash
set +e
echo '=== OET VOLUMES ==='
docker volume ls | grep -i oet
echo '=== OET POSTGRES MOUNT SOURCE ==='
docker inspect oet-postgres --format '{{range .Mounts}}{{.Name}} -> {{.Destination}} (src {{.Source}}){{"\n"}}{{end}}' 2>&1
echo '=== ALL POSTGRES-LIKE CONTAINERS ==='
docker ps -a --format '{{.Names}}\t{{.Image}}\t{{.Status}}' | grep -iE 'postgres|oet-api|oet-web' 2>&1
echo '=== ROLES IN oet-postgres ==='
timeout 15 docker exec oet-postgres psql -U postgres -tAc "select rolname from pg_roles order by 1;" 2>&1 || echo roles_query_failed
echo '=== DATABASES IN oet-postgres ==='
timeout 15 docker exec oet-postgres psql -U postgres -tAc "select datname from pg_database order by 1;" 2>&1 || echo db_query_failed
echo '=== DONE ==='
