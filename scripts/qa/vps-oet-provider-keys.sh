#!/bin/bash
# Read-only: which AI provider credentials are present in the LIVE api container?
set -u

echo "############ oet-api env: AI/provider key NAMES + set/length (no values) ############"
docker exec oet-api sh -c 'env' 2>/dev/null \
  | grep -iE 'anthropic|openai|gemini|ai__|digitalocean|ubag|mistral|eleven|whisper|deepgram' \
  | while IFS='=' read -r k v; do
      printf '  %-42s len=%s\n' "$k" "${#v}"
    done

echo
echo "############ .env.production key names (values never printed) ############"
ENVF=/opt/oetwebapp/.env.production
if [ -f "$ENVF" ]; then
  grep -oE '^[A-Za-z_0-9]+=' "$ENVF" | tr -d '=' | grep -iE 'anthropic|openai|gemini|ai__|digitalocean|ubag|mistral' | while read -r k; do
    v=$(grep -m1 "^${k}=" "$ENVF" | cut -d= -f2-)
    printf '  %-42s len=%s\n' "$k" "${#v}"
  done
else
  echo "  (no $ENVF)"
fi

echo
echo "############ RuntimeSettings: is the speaking/writing Anthropic key set in DB? ############"
U=$(docker exec oet-postgres printenv POSTGRES_USER)
D=$(docker exec oet-postgres printenv POSTGRES_DB)
docker exec -i oet-postgres psql -U "$U" -d "$D" -A -F' | ' -c \
  "SELECT CASE WHEN \"SpeakingAnthropicApiKeyEncrypted\" IS NULL OR \"SpeakingAnthropicApiKeyEncrypted\"='' THEN 'NOT SET' ELSE 'set (len ' || length(\"SpeakingAnthropicApiKeyEncrypted\") || ')' END AS anthropic_key, \"UpdatedAt\" FROM \"RuntimeSettings\";" 2>&1
