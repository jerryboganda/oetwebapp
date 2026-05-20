#!/usr/bin/env bash
set -uo pipefail
echo "=== AI-related env keys in .env.production ==="
grep -E '^(AI__|ANTHROPIC|OPENAI|API_KEY|DO_|DIGITALOCEAN|TTS|QWEN)' /opt/oetwebapp/.env.production 2>/dev/null | sed 's/=.*/=<redacted>/'
echo "=== scripts/_*.sh ==="
ls /opt/oetwebapp/scripts/_*.sh 2>/dev/null
echo "=== Running processes (node) ==="
ps auxww | grep -E 'node.*(generate|retry|orchestr)' | grep -v grep | head -10
echo "=== Container env (oet-api-green) ==="
docker exec oet-api-green sh -c 'env | grep -E "^(AI__|ANTHROPIC|DO_|TTS|QWEN)"' 2>/dev/null | sed 's/=.*/=<redacted>/'
