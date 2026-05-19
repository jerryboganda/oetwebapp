#!/usr/bin/env bash
set +e
cd /opt/oetwebapp

echo "=== Backfill ALL NULL difficulty in one shot (rescue stuck papers) ==="
docker exec oet-postgres psql -U oet_learner -d oet_learner -c "UPDATE \"ListeningExtracts\" SET \"DifficultyRating\"=3 WHERE \"DifficultyRating\" IS NULL;"
docker exec oet-postgres psql -U oet_learner -d oet_learner -c "UPDATE \"ListeningQuestions\" SET \"DifficultyLevel\"=3 WHERE \"DifficultyLevel\" IS NULL;"

echo ""
echo "=== Auth ==="
source /opt/oetwebapp/scripts/admin/.envrc
TOK=$(curl -sS -X POST 'https://api.oetwithdrhesham.co.uk/v1/auth/sign-in' -H 'content-type: application/json' \
  -d "{\"email\":\"manwara575@gmail.com\",\"password\":\"$ADMIN_PASSWORD\"}" | python3 -c 'import sys,json;d=json.load(sys.stdin);print(d.get("accessToken") or d.get("token") or "")')
echo "token len=${#TOK}"

echo ""
echo "=== Republish all listening Drafts ==="
IDS=$(docker exec oet-postgres psql -U oet_learner -d oet_learner -tA -c "SELECT \"Id\" FROM \"ContentPapers\" WHERE \"SubtestCode\"='listening' AND \"Status\"=0;")
PUB=0; FAIL=0; FAILIDS=""
for id in $IDS; do
  CODE=$(curl -sS -o /tmp/p.json -w '%{http_code}' -X POST "https://api.oetwithdrhesham.co.uk/v1/admin/papers/$id/publish" \
    -H "authorization: Bearer $TOK" -H 'content-type: application/json' -d '{}')
  if [ "$CODE" = "204" ] || [ "$CODE" = "200" ]; then
    PUB=$((PUB+1))
  else
    FAIL=$((FAIL+1))
    FAILIDS="$FAILIDS $id"
    echo "FAIL $id $CODE $(cat /tmp/p.json | head -c 250)"
  fi
done
echo ""
echo "RESULT: published=$PUB failed=$FAIL"
[ -n "$FAILIDS" ] && echo "FAILED IDS:$FAILIDS"

echo ""
echo "=== Status after ==="
docker exec oet-postgres psql -U oet_learner -d oet_learner -c "SELECT \"SubtestCode\", \"Status\", COUNT(*) FROM \"ContentPapers\" GROUP BY 1,2 ORDER BY 1,2;"
