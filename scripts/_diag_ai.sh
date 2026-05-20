#!/usr/bin/env bash
set -uo pipefail
echo "--- API container AI vars ---"
docker exec oet-api-green env | grep '^AI__' | awk -F= '{print $1"="(length($2)>0?"<set len="length($2)">":"<empty>")}'
echo ""
echo "--- envrc keys (no values) ---"
grep -oE '^[A-Z_]+=' /opt/oetwebapp/scripts/admin/.envrc
echo ""
echo "--- reading orch dead since 08:41, restart ---"
pgrep -af generate-reading || echo not_running
echo ""
echo "--- listening skip-tts orch alive? ---"
pgrep -af generate-listening | head -2
echo ""
echo "--- last 5 entries of reading-resume.json (paper indices done) ---"
python3 -c "import json; d=json.load(open('/opt/oetwebapp/output/admin-bulk/generate-reading-resume.json')); print('keys:', list(d.keys())[:5]); print('count:', len(d) if isinstance(d, (list,dict)) else 'n/a')" 2>&1 || head -10 /opt/oetwebapp/output/admin-bulk/generate-reading-resume.json
