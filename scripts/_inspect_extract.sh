#!/usr/bin/env bash
# Show ExtractedTextJson keys/shape for two problem drafts
for ID in 51900b7211b84a8dbeb1d336b6e7c14a b8e0e9def00a4dd192beb08a5121deb9; do
  echo "=== $ID ==="
  docker exec -i oet-postgres psql -U oet_learner -d oet_learner -At -P pager=off <<SQL
SELECT length("ExtractedTextJson"::text),
       jsonb_typeof("ExtractedTextJson"::jsonb),
       (SELECT string_agg(k, ',') FROM jsonb_object_keys("ExtractedTextJson"::jsonb) k)
FROM "ContentPapers" WHERE "Id"='$ID';
SQL
done
