#!/bin/bash
set -e
BLUE_IP=$(docker inspect -f '{{range $k,$v := .NetworkSettings.Networks}}{{$v.IPAddress}} {{end}}' oet-api-blue)
echo "BLUE_IP=$BLUE_IP"
for IP in $BLUE_IP; do
  CODE=$(curl -sS -o /tmp/health.out -w '%{http_code}' "http://$IP:8080/health/ready" 2>/dev/null || echo FAIL)
  echo "IP=$IP HTTP=$CODE"
  if [ "$CODE" = "200" ]; then
    echo "--- body ---"
    head -c 300 /tmp/health.out
    echo
    break
  fi
done
