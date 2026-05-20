#!/bin/bash
# Stream logs and look at correlation between PUT 200 and disk state.
set -e

echo "=== api logs - last 200 lines correlating PUT 200 -> Complete failure ==="
docker logs --tail 400 oet-api-green 2>&1 | grep -E 'admin/uploads|staged parts|Upload' | tail -80
echo
echo "=== orchestrator latest events ==="
tail -100 /tmp/generate-reading-live.log 2>/dev/null | tail -50
echo
echo "=== currently running orchestrators ==="
ps -ef | grep -E 'generate-(reading|listening|vocab|mocks|grammar|pronunciation)' | grep -v grep
echo
echo "=== check appuser permissions on staging ==="
docker exec oet-api-green sh -c 'id ; ls -ld /var/opt/oet-learner/storage/uploads/staging/ ; touch /var/opt/oet-learner/storage/uploads/staging/_perm_check.txt && echo "WRITE OK" || echo "WRITE FAIL"; ls -la /var/opt/oet-learner/storage/uploads/staging/_perm_check.txt 2>&1; rm -f /var/opt/oet-learner/storage/uploads/staging/_perm_check.txt'
echo
echo "=== disk space ==="
docker exec oet-api-green df -h /var/opt/oet-learner/storage/ 2>&1
echo
echo "=== check if there's a tmpfs or something sweeping ==="
docker exec oet-api-green mount | grep -E 'opt|var' 2>&1 | head -20
