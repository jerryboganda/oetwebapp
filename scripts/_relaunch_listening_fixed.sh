#!/usr/bin/env bash
set +e
cd /opt/oetwebapp

echo "=== Kill old listening orch (PID 3207996) ==="
kill 3207996 2>/dev/null
sleep 2
kill -9 3207996 2>/dev/null
sleep 1
ps -p 3207996 -o pid,cmd 2>/dev/null && echo "STILL ALIVE" || echo "GONE"

echo ""
echo "=== Delete unrescuable drafts ==="
source /opt/oetwebapp/scripts/admin/.envrc
TOK=$(curl -sS -X POST 'https://api.oetwithdrhesham.co.uk/v1/auth/sign-in' -H 'content-type: application/json' \
  -d "{\"email\":\"manwara575@gmail.com\",\"password\":\"$ADMIN_PASSWORD\"}" | python3 -c 'import sys,json;d=json.load(sys.stdin);print(d.get("accessToken") or "")')
for id in 16203e2a53344e598c532d67bb8d4cb8 06ed32dd4bce4800bbe84c16ec8507ca; do
  CODE=$(curl -sS -o /tmp/d.json -w '%{http_code}' -X DELETE "https://api.oetwithdrhesham.co.uk/v1/admin/papers/$id" -H "authorization: Bearer $TOK")
  echo "DELETE $id -> $CODE $(cat /tmp/d.json | head -c 200)"
done

# Also try direct DB delete if API refuses
docker exec oet-postgres psql -U oet_learner -d oet_learner -c "DELETE FROM \"ContentPapers\" WHERE \"Id\" IN ('16203e2a53344e598c532d67bb8d4cb8','06ed32dd4bce4800bbe84c16ec8507ca') AND \"Status\"=0;"

echo ""
echo "=== Status (should be 0 listening drafts) ==="
docker exec oet-postgres psql -U oet_learner -d oet_learner -c "SELECT \"SubtestCode\", \"Status\", COUNT(*) FROM \"ContentPapers\" GROUP BY 1,2 ORDER BY 1,2;"

echo ""
echo "=== Relaunch listening orch with fixed code ==="
mv /tmp/generate-listening-live.log /tmp/generate-listening-live.log.before-fix 2>/dev/null
cd /opt/oetwebapp
setsid nohup bash scripts/admin/run-bulk.sh generate-listening.mjs --count 120 --resume > /tmp/generate-listening-live.log 2>&1 &
NEWPID=$!
echo "NEW PID: $NEWPID"
sleep 5
ps -p $NEWPID -o pid,etime,cmd
echo ""
echo "=== First lines of new log ==="
sleep 5
tail -n 20 /tmp/generate-listening-live.log
