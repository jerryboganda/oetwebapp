#!/bin/bash
set -e
for n in oet-api-green oet-api; do
  echo "=== $n ==="
  docker inspect $n --format '{{range .Mounts}}{{.Type}} {{.Source}} -> {{.Destination}}{{println}}{{end}}'
  echo "--- staging contents ---"
  docker exec $n find /var/opt/oet-learner/storage/uploads/staging/ -ls 2>&1 | head -10
done
echo
echo "=== which container does nginx route TO? ==="
docker exec oet-api cat /etc/nginx/conf.d/*.conf 2>&1 | grep -E 'proxy_pass|upstream' | head -10
echo
echo "=== published dir ==="
docker exec oet-api-green find /var/opt/oet-learner/storage/uploads/published/ -type d 2>&1 | head -10
echo
echo "=== staging TTL ==="
docker exec oet-api-green env | grep -iE 'staging|ttl' 2>&1 | head
