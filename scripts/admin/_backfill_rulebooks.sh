#!/usr/bin/env bash
# Backfill missing grammar/pronunciation/listening rulebooks for all 12 professions
# by cloning medicine/*/rulebook.v1.json and rewriting the "profession" field.
# Also purges legacy "other-allied-health" rulebook trees.
set -euo pipefail

cd /opt/oetwebapp/rulebooks

PROFS=(medicine nursing dentistry pharmacy physiotherapy veterinary optometry radiography occupational-therapy speech-pathology podiatry dietetics)
KINDS=(grammar pronunciation listening)

for kind in "${KINDS[@]}"; do
  src="$kind/medicine/rulebook.v1.json"
  if [ ! -f "$src" ]; then
    echo "  ! no medicine rulebook for $kind — SKIP"
    continue
  fi
  for prof in "${PROFS[@]}"; do
    dst_dir="$kind/$prof"
    dst_file="$dst_dir/rulebook.v1.json"
    if [ -f "$dst_file" ]; then
      echo "  = $kind/$prof already exists"
      continue
    fi
    mkdir -p "$dst_dir"
    # Rewrite "profession": "medicine" -> "profession": "<prof>"
    python3 -c "
import json,sys
src=json.load(open('$src','r',encoding='utf-8'))
src['profession']='$prof'
open('$dst_file','w',encoding='utf-8').write(json.dumps(src,ensure_ascii=False,indent=2))
"
    echo "  + $kind/$prof created"
  done
done

echo ""
echo "=== Purging legacy other-allied-health ==="
for kind in grammar pronunciation listening reading speaking writing conversation; do
  d="$kind/other-allied-health"
  if [ -d "$d" ]; then
    rm -rf "$d"
    echo "  - purged $d"
  fi
done

echo ""
echo "=== Strip BOMs from any rulebook JSON ==="
find . -name 'rulebook.v*.json' -exec sed -i '1s/^\xEF\xBB\xBF//' {} \;

echo ""
echo "=== Final inventory ==="
for kind in "${KINDS[@]}"; do
  echo "[$kind] $(ls $kind 2>/dev/null | grep -v common | tr '\n' ' ')"
done
