#!/bin/bash
set +e
USER=$(docker exec oet-postgres bash -c 'echo $POSTGRES_USER')
DB=$(docker exec oet-postgres bash -c 'echo $POSTGRES_DB')
echo "=== SQL fix ==="
docker exec oet-postgres psql -U "$USER" -d "$DB" -c "UPDATE \"ListeningExtracts\" SET \"DifficultyRating\"=3 WHERE \"DifficultyRating\" IS NULL;"
docker exec oet-postgres psql -U "$USER" -d "$DB" -c "UPDATE \"ListeningQuestions\" SET \"DifficultyLevel\"=3 WHERE \"DifficultyLevel\" IS NULL;"
API=https://api.oetwithdrhesham.co.uk
echo "=== Login admin ==="
: "${ADMIN_EMAIL:?ADMIN_EMAIL required}"
: "${ADMIN_PASSWORD:?ADMIN_PASSWORD required}"
TOK=$(curl -sS -X POST "$API/v1/auth/sign-in" -H 'content-type: application/json' \
  -d "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\"}" | python3 -c 'import sys,json;d=json.load(sys.stdin);print(d.get("accessToken") or d.get("token") or "")')
echo "token len=${#TOK}"
echo "=== Publish all listening Draft papers ==="
IDS=$(docker exec oet-postgres psql -U "$USER" -d "$DB" -tA -c "SELECT \"Id\" FROM \"ContentPapers\" WHERE \"SubtestCode\"='listening' AND \"Status\"=0;")
PUB=0; FAIL=0
for id in $IDS; do
  CODE=$(curl -sS -o /tmp/p.json -w '%{http_code}' -X POST "$API/v1/admin/papers/$id/publish" \
    -H "authorization: Bearer $TOK" -H 'content-type: application/json' -d '{}')
  if [ "$CODE" = "204" ] || [ "$CODE" = "200" ]; then
    PUB=$((PUB+1))
  else
    FAIL=$((FAIL+1))
    echo "FAIL $id $CODE $(cat /tmp/p.json | head -c 200)"
  fi
done
echo "Published=$PUB Failed=$FAIL"
echo "=== Final counts ==="
docker exec oet-postgres psql -U "$USER" -d "$DB" -tA -c "SELECT \"SubtestCode\",\"Status\",COUNT(*) FROM \"ContentPapers\" WHERE \"SubtestCode\"='listening' GROUP BY 1,2 ORDER BY 2;"
