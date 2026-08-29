-- W11 vocabulary duplicate merge — RUN ONLY AFTER OWNER APPROVAL.
-- Reassigns LearnerVocabularyItems onto the earliest VocabularyWords row
-- per NormalizedWord, then deletes extras. Review the report first.

-- 1. Report
SELECT "NormalizedWord", COUNT(*) AS "DuplicateCount",
       string_agg("Id"::text, ',' ORDER BY "CreatedAt", "Id") AS "Ids"
FROM "VocabularyWords"
WHERE "NormalizedWord" <> ''
GROUP BY "NormalizedWord"
HAVING COUNT(*) > 1
ORDER BY "NormalizedWord";

-- 2. Reassign learner items (uncomment after approval)
-- WITH survivors AS (
--     SELECT DISTINCT ON ("NormalizedWord") "Id" AS "SurvivorId", "NormalizedWord"
--     FROM "VocabularyWords"
--     WHERE "NormalizedWord" <> ''
--     ORDER BY "NormalizedWord", "CreatedAt", "Id"
-- ),
-- dupes AS (
--     SELECT w."Id" AS "DuplicateId", s."SurvivorId"
--     FROM "VocabularyWords" w
--     JOIN survivors s ON s."NormalizedWord" = w."NormalizedWord"
--     WHERE w."Id" <> s."SurvivorId"
-- )
-- UPDATE "LearnerVocabularyItems" i
-- SET "VocabularyWordId" = d."SurvivorId"
-- FROM dupes d
-- WHERE i."VocabularyWordId" = d."DuplicateId";

-- 3. Delete extras (uncomment after step 2)
-- DELETE FROM "VocabularyWords" w
-- USING (
--     SELECT w2."Id"
--     FROM "VocabularyWords" w2
--     JOIN (
--         SELECT DISTINCT ON ("NormalizedWord") "Id"
--         FROM "VocabularyWords"
--         WHERE "NormalizedWord" <> ''
--         ORDER BY "NormalizedWord", "CreatedAt", "Id"
--     ) keep ON keep."NormalizedWord" = w2."NormalizedWord"
--     WHERE w2."Id" <> keep."Id"
-- ) doomed
-- WHERE w."Id" = doomed."Id";
