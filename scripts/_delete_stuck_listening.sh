#!/bin/bash
set -e
IDS="('e223ca4919cd4e85af2c22570ad7be73','f3d25210905b46749b007d827794dd5e')"
docker exec -i oet-postgres psql -U oet_learner -d oet_learner <<SQL
BEGIN;
DELETE FROM "ListeningQuestions" WHERE "PaperId"::text IN $IDS;
DELETE FROM "ListeningParts"    WHERE "PaperId"::text IN $IDS;
DELETE FROM "ContentPaperAssets" WHERE "PaperId"::text IN $IDS;
DELETE FROM "ContentPapers" WHERE "Id"::text IN $IDS;
COMMIT;
SELECT 'remaining', count(*) FROM "ContentPapers" WHERE "Id"::text IN $IDS;
SQL
