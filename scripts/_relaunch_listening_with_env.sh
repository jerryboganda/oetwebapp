#!/bin/bash
set -e
SRC_PID=2652714
TMP=/tmp/_listening_env.sh

{
  echo '#!/bin/bash'
  cat /proc/$SRC_PID/environ | tr '\0' '\n' | grep -E '^(AI__.*=|API_BASE=|ADMIN_EMAIL=|ADMIN_PASSWORD=)' | while IFS= read -r line; do
    [ -z "$line" ] && continue
    key="${line%%=*}"
    val="${line#*=}"
    printf 'export %s=%q\n' "$key" "$val"
  done
  echo 'cd /opt/oetwebapp'
  echo 'nohup node scripts/admin/generate-listening.mjs --count 120 --resume > /tmp/generate-listening-live.log 2>&1 &'
  echo 'echo "PID=$!"'
} > $TMP
chmod +x $TMP

pkill -f 'generate-listening.mjs' 2>/dev/null || true
sleep 2

bash $TMP
sleep 3
ps -ef | grep generate-listening | grep -v grep
echo
echo "--- env exported (key names only) ---"
grep -E '^export ' $TMP | awk '{print $2}' | cut -d= -f1
