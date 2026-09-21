#!/bin/bash
# Read-only: is the configured Gemini key alive, and where is it consumed?
set -u
ENVF=/opt/oetwebapp/.env.production
GEM=$(grep -m1 '^GEMINI_API_KEY=' "$ENVF" | cut -d= -f2- | tr -d '"' | tr -d '\r')
echo "Gemini key present: len=${#GEM}"

echo
echo "############ LIVE Gemini API test (models list) ############"
docker exec oet-api sh -c "wget -qO- --timeout=25 'https://generativelanguage.googleapis.com/v1beta/models?key=$GEM'" 2>&1 | head -c 900

echo
echo
echo "############ LIVE Gemini generateContent smoke test ############"
docker exec oet-api sh -c "wget -qO- --timeout=30 --header='Content-Type: application/json' --post-data='{\"contents\":[{\"parts\":[{\"text\":\"Reply with the single word: ready\"}]}]}' 'https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key=$GEM'" 2>&1 | head -c 700

echo
echo
echo "############ Where is GEMINI_API_KEY consumed in the app? ############"
grep -rn "GEMINI_API_KEY\|Gemini:ApiKey\|Gemini__ApiKey" /opt/oetwebapp/backend/src --include=*.cs 2>/dev/null | head -10
grep -rn "GEMINI_API_KEY" /opt/oetwebapp/.env.production 2>/dev/null | sed 's/=.*/=<set>/'
