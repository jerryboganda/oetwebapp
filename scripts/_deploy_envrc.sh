#!/usr/bin/env bash
set -e
# Write .envrc on VPS with secrets passed via environment.
# Operator: export ADMIN_PASSWORD=... and AI__ApiKey=... locally before running
# this via ssh, OR pipe a heredoc with the values from a local secrets file.
: "${ADMIN_PASSWORD:?must set ADMIN_PASSWORD before running this on VPS}"
: "${AI__ApiKey:?must set AI__ApiKey before running this on VPS}"
cat > /opt/oetwebapp/scripts/admin/.envrc <<EOF
export ADMIN_PASSWORD='${ADMIN_PASSWORD}'
export AI__ApiKey='${AI__ApiKey}'
EOF
chmod 600 /opt/oetwebapp/scripts/admin/.envrc
echo "ENVRC: $(wc -c < /opt/oetwebapp/scripts/admin/.envrc) bytes"
echo "RUN-BULK MD5: $(md5sum /opt/oetwebapp/scripts/admin/run-bulk.sh)"
( source /opt/oetwebapp/scripts/admin/.envrc; echo "AI key len: ${#AI__ApiKey}"; echo "ADMIN_PASSWORD len: ${#ADMIN_PASSWORD}" )
