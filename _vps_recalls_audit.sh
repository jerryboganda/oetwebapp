#!/bin/bash
# Recalls coverage audit.
set -e

echo "=== 1. Tables containing recall/vocab ==="
docker exec oet-postgres bash -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "
SELECT table_name FROM information_schema.tables
WHERE table_schema='\''public'\'' AND (table_name ILIKE '\''%recall%'\'' OR table_name ILIKE '\''%vocab%'\'' OR table_name ILIKE '\''%reviewitem%'\'')
ORDER BY table_name;"'

echo
echo "=== 2. VocabularyTerms status mix ==="
docker exec oet-postgres bash -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "
SELECT \"Status\", COUNT(*) AS rows FROM \"VocabularyTerms\" GROUP BY \"Status\" ORDER BY rows DESC;"'

echo
echo "=== 3. By profession ==="
docker exec oet-postgres bash -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "
SELECT COALESCE(\"ProfessionId\",'\''(general)'\'') AS profession,
  COUNT(*) FILTER (WHERE \"Status\"='\''active'\'') AS active,
  COUNT(*) FILTER (WHERE \"Status\"='\''draft'\'') AS draft,
  COUNT(*) AS total
FROM \"VocabularyTerms\" GROUP BY \"ProfessionId\" ORDER BY total DESC;"'

echo
echo "=== 4. By recall-set code ==="
docker exec oet-postgres bash -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "
SELECT code,
  COUNT(*) FILTER (WHERE \"Status\"='\''active'\'') AS active,
  COUNT(*) FILTER (WHERE \"Status\"='\''draft'\'') AS draft,
  COUNT(*) AS total
FROM \"VocabularyTerms\",
  LATERAL (SELECT unnest(ARRAY['\''old'\'','\''2023-2025'\'','\''2026'\'']) AS code) c
WHERE \"RecallSetCodesJson\"::jsonb ? c.code
GROUP BY code ORDER BY code;"'

echo
echo "=== 5. Untagged vs tagged ==="
docker exec oet-postgres bash -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "
SELECT
  COUNT(*) FILTER (WHERE jsonb_array_length(COALESCE(NULLIF(\"RecallSetCodesJson\",'\'''\''),'\''[]'\'')::jsonb)=0) AS untagged,
  COUNT(*) FILTER (WHERE jsonb_array_length(COALESCE(NULLIF(\"RecallSetCodesJson\",'\'''\''),'\''[]'\'')::jsonb)>0) AS tagged,
  COUNT(*) AS total
FROM \"VocabularyTerms\";"'

echo
echo "=== 6. LearnerVocabularies row count and per-user ==="
docker exec oet-postgres bash -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "
SELECT COUNT(*) AS total_cards, COUNT(DISTINCT \"UserId\") AS users FROM \"LearnerVocabularies\";"'

echo
echo "=== 7. ReviewItems ==="
docker exec oet-postgres bash -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "
SELECT \"SourceType\", COUNT(*) AS rows FROM \"ReviewItems\" GROUP BY \"SourceType\" ORDER BY rows DESC LIMIT 10;"'

echo
echo "=== 8. Test learner specifically ==="
docker exec oet-postgres bash -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "
SELECT u.\"Id\", u.\"Email\", u.\"ProfessionId\",
  (SELECT COUNT(*) FROM \"LearnerVocabularies\" lv WHERE lv.\"UserId\"=u.\"Id\") AS vocab_cards,
  (SELECT COUNT(*) FROM \"ReviewItems\" ri WHERE ri.\"UserId\"=u.\"Id\") AS review_items
FROM \"Users\" u WHERE u.\"Email\"='\''mindreader420123@gmail.com'\'';"'

echo
echo "=== Done ==="
