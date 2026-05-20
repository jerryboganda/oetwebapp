#!/usr/bin/env bash
set -euo pipefail
docker exec nginx-proxy-manager-app-1 bash -c '
F=/data/nginx/proxy_host/26.conf
if grep -q client_max_body_size "$F"; then
  echo "[npm-already-patched]"
else
  cp "$F" "$F.bak.$(date +%s)"
  sed -i "/server_name api.oetwithdrhesham.co.uk;/a\\  client_max_body_size 200m;\\n  proxy_request_buffering off;\\n  proxy_read_timeout 600s;\\n  proxy_send_timeout 600s;" "$F"
fi
nginx -t && nginx -s reload && grep -nE "client_max_body_size|proxy_request_buffering|proxy_read_timeout" "$F"
'
