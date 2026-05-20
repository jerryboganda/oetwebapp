#!/bin/bash
PID="${1:-e223ca4919cd4e85af2c22570ad7be73}"
echo === KEYS in ExtractedTextJson ===
docker exec oet-postgres psql -U oet_learner oet_learner -tA <<SQL
SELECT jsonb_object_keys("ExtractedTextJson") FROM "ContentPapers" WHERE "Id"='$PID';
SQL
echo === Length per top key ===
docker exec oet-postgres psql -U oet_learner oet_learner -tA <<SQL
SELECT k, jsonb_typeof("ExtractedTextJson"->k), CASE WHEN jsonb_typeof("ExtractedTextJson"->k)='array' THEN jsonb_array_length("ExtractedTextJson"->k) ELSE NULL END
FROM "ContentPapers", jsonb_object_keys("ExtractedTextJson") k WHERE "Id"='$PID';
SQL
echo === First 1200 chars ===
docker exec oet-postgres psql -U oet_learner oet_learner -tA <<SQL
SELECT LEFT("ExtractedTextJson"::text, 1200) FROM "ContentPapers" WHERE "Id"='$PID';
SQL
