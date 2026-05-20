#!/bin/sh
# Idempotent: ensure nginx in oet-api router accepts 200m uploads.
set -e
CONF=/etc/nginx/nginx.conf
if grep -q "client_max_body_size 200m" "$CONF"; then
  echo "HAS_413"
  exit 0
fi
sed -i '/http {/a\    client_max_body_size 200m;' "$CONF"
nginx -t
nginx -s reload
echo "PATCHED_413"
