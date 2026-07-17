#!/usr/bin/env bash
set +e
echo '=== DOCKER DAEMON ==='
timeout 10 docker version --format '{{.Server.Version}}' 2>&1 || echo docker_daemon_unresponsive
echo '=== LOADAVG ==='
cat /proc/loadavg
echo '=== POSTGRES RECOVERY LOG (tail) ==='
timeout 10 docker logs --tail 15 oet-postgres 2>&1 || echo logs_failed
echo '=== POSTGRES READY ==='
timeout 10 docker exec oet-postgres pg_isready -U oetwithdrhesham 2>&1 || echo not_ready
echo '=== DONE ==='
