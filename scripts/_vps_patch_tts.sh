#!/usr/bin/env bash
set -euo pipefail
F=/opt/oetwebapp/scripts/admin/_lib.mjs
cp "$F" "${F}.bak.$(date +%s)"

# Inject `instructions` default into the destructuring block.
python3 - "$F" <<'PY'
import sys, re
p = sys.argv[1]
src = open(p, 'r', encoding='utf-8').read()

old = """    model = CONFIG.ai.ttsModel,
    voice = CONFIG.ai.ttsVoice,
    retries = 3,
    format = 'mp3',
  } = opts;"""

new = """    model = CONFIG.ai.ttsModel,
    voice = CONFIG.ai.ttsVoice,
    retries = 3,
    format = 'mp3',
    instructions = 'A calm, clear, professional native English voice suitable for OET listening practice.',
  } = opts;"""

if old not in src:
    print("ERROR: destructuring block not found"); sys.exit(2)
src = src.replace(old, new, 1)

old2 = "  const body = { model, input: text, voice, response_format: format };"
new2 = "  // Qwen3 voice-design models require BOTH `voice` and `instructions`.\n  const body = { model, input: text, voice, response_format: format, instructions };"
if old2 not in src:
    print("ERROR: body line not found"); sys.exit(3)
src = src.replace(old2, new2, 1)

open(p,'w',encoding='utf-8').write(src)
print("Patched OK")
PY

grep -nE 'instructions' "$F" | head -5
echo "--- bytes: $(wc -c < $F) lines: $(wc -l < $F)"

pkill -f generate-listening.mjs || true
sleep 2
cd /opt/oetwebapp
nohup node scripts/admin/generate-listening.mjs --resume > /tmp/generate-listening-live.log 2>&1 &
disown
sleep 6
echo "=== Running ==="
ps -eo pid,etime,cmd | grep -E 'node scripts/admin/' | grep -v grep
echo "=== Tail ==="
tail -25 /tmp/generate-listening-live.log
