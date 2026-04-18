#!/usr/bin/env bash
set -euo pipefail
cd /root/oetwebsite

echo "=== EMAIL CONFIG KEYS PRESENT (values hidden) ==="
grep -oE '^(SMTP__[A-Z_]+|BREVO__[A-Z_]+|EMAILVERIFICATION__[A-Z_]+)=' .env.production \
  | sed 's/=$//' | sort -u || true

echo ""
echo "=== Do the SMTP values look non-empty? (length only, content hidden) ==="
for k in SMTP__HOST SMTP__PORT SMTP__USERNAME SMTP__PASSWORD SMTP__FROMEMAIL BREVO__APIKEY; do
  v=$(grep -E "^${k}=" .env.production | cut -d= -f2- || true)
  if [ -z "$v" ]; then
    echo "$k: MISSING or empty"
  else
    echo "$k: set (length=${#v})"
  fi
done

echo ""
echo "=== Outbound port reachability (SMTP 587 to smtp-relay.brevo.com / sendgrid / gmail) ==="
for host in smtp-relay.brevo.com smtp.sendgrid.net smtp.gmail.com; do
  timeout 5 bash -c "exec 3<>/dev/tcp/${host}/587" 2>&1 && echo "${host}:587 -> OK" || echo "${host}:587 -> UNREACHABLE"
done

echo ""
echo "=== Recent API logs: any SMTP / Brevo activity? ==="
docker logs oet-api --since 24h 2>&1 | grep -iE 'smtp|brevo|email|mail' | tail -10 || echo "(no email log lines in last 24h)"
