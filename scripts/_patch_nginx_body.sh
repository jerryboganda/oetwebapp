#!/usr/bin/env bash
set -euo pipefail
F=/opt/oetwebapp/scripts/deploy/nginx/api-bluegreen.conf.template
if grep -q 'client_max_body_size' "$F"; then
  echo "[already-patched]"
else
  cp "$F" "$F.bak.$(date +%s)"
  # Insert client_max_body_size + proxy_request_buffering off inside the server block (after listen line)
  python3 - <<'PY'
import re, pathlib
p = pathlib.Path("/opt/oetwebapp/scripts/deploy/nginx/api-bluegreen.conf.template")
s = p.read_text()
s = s.replace(
  "server_name _;\n",
  "server_name _;\n    client_max_body_size 200m;\n    client_body_buffer_size 256k;\n    proxy_request_buffering off;\n",
  1,
)
p.write_text(s)
print("[patched]")
PY
fi
echo === new file ===
grep -nE 'client_max_body_size|proxy_request_buffering|server_name' "$F" | head
echo === reload router ===
docker exec oet-api sh -c 'envsubst "\$ACTIVE_SLOT" < /etc/nginx/templates/default.conf.template > /etc/nginx/conf.d/default.conf && nginx -t && nginx -s reload'
echo === confirm in container ===
docker exec oet-api grep client_max_body_size /etc/nginx/conf.d/default.conf
