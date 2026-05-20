#!/bin/bash
set -e
echo "=== ExamTypeCode distribution ==="
docker exec oet-postgres bash -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "
SELECT \"ExamTypeCode\", COUNT(*) FROM \"VocabularyTerms\" GROUP BY \"ExamTypeCode\";"'

echo
echo "=== Category distribution (top 20) ==="
docker exec oet-postgres bash -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "
SELECT \"Category\", COUNT(*) FROM \"VocabularyTerms\" GROUP BY \"Category\" ORDER BY COUNT(*) DESC LIMIT 20;"'

echo
echo "=== OetSubtestTagsJson sample ==="
docker exec oet-postgres bash -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "
SELECT \"Id\", \"Term\", \"OetSubtestTagsJson\", \"RecallSetCodesJson\" FROM \"VocabularyTerms\" WHERE \"OetSubtestTagsJson\" IS NOT NULL AND \"OetSubtestTagsJson\" != '\''[]'\'' LIMIT 5;"'

echo
echo "=== Users table columns ==="
docker exec oet-postgres bash -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "
SELECT column_name, data_type FROM information_schema.columns WHERE table_name='\''Users'\'' ORDER BY ordinal_position LIMIT 40;"'

echo
echo "=== Test learner row ==="
docker exec oet-postgres bash -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "
SELECT \"Id\", \"Email\", \"DisplayName\" FROM \"Users\" WHERE \"Email\"='\''mindreader420123@gmail.com'\'';"'

echo
echo "=== Test live API as anonymous (expect 401) and the response shape ==="
curl -sS -o /tmp/out.txt -w "HTTP %{http_code}\n" "http://localhost:8080/v1/vocabulary/terms?examTypeCode=oet&page=1&pageSize=5" -H "Host: api.oetwithdrhesham.co.uk" || true
head -c 500 /tmp/out.txt
echo
