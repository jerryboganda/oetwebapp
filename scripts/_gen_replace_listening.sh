#!/usr/bin/env bash
set -uo pipefail
cd /opt/oetwebapp
pkill -f "generate-listening.mjs" 2>/dev/null || true
# .env.production uses UPPERCASE; Node script expects mixed-case (ASP.NET binding).
export AI__ApiKey="$(grep -E '^AI__APIKEY=' /opt/oetwebapp/.env.production | head -1 | cut -d= -f2-)"
export AI__BaseUrl="$(grep -E '^AI__BASEURL=' /opt/oetwebapp/.env.production | head -1 | cut -d= -f2-)"
export AI__DefaultModel="$(grep -E '^AI__DEFAULTMODEL=' /opt/oetwebapp/.env.production | head -1 | cut -d= -f2-)"
export AI__ProviderId="$(grep -E '^AI__PROVIDERID=' /opt/oetwebapp/.env.production | head -1 | cut -d= -f2-)"
echo "AI__ApiKey len=${#AI__ApiKey}  model=$AI__DefaultModel  provider=$AI__ProviderId"
nohup node scripts/admin/generate-listening.mjs \
  --count 4 \
  > /tmp/gen-listening-replace.log 2>&1 &
echo "REPLACE_PID=$!"
sleep 2
tail -5 /tmp/gen-listening-replace.log 2>/dev/null
