#!/bin/bash
echo === ENV BACKUP FILES ===
for f in /opt/oetwebapp/.env.production.bak* /opt/oetwebapp/.env.production.smtpbackup* /opt/oetwebapp/.env.production.predeploy*; do
  v=$(grep -hE '^AI__APIKEY=.+' "$f" 2>/dev/null | head -1)
  if [ -n "$v" ]; then echo "$f -> ${v:0:40}..."; fi
done
echo
echo === RUNTIME SETTINGS ===
docker exec -i oet-postgres psql -U oet_learner -d oet_learner <<'SQL'
\d "RuntimeSettings"
SELECT "Key", LEFT("Value",10) FROM "RuntimeSettings" WHERE "Key" ILIKE 'ai_%' OR "Key" ILIKE '%apikey%';
SQL
