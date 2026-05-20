-- PHASE A: dry-run preview
-- Canonical replacements:
--   nursing-002 broken  30b2245a  -> canonical 01ca161e
--   physio-004 old dupe 167124dd  -> canonical 834bfdfe (newly sweep-published)
--   speech-path-011     1322a10d  -> UNPUBLISH (Status 4 -> 0) for sweep #3

BEGIN;

-- 1) Repoint mock bundle sections (RESTRICT FK) BEFORE delete
UPDATE "MockBundleSections" SET "ContentPaperId" = '01ca161ecbfd4c8c949ccc928856dc6d'
 WHERE "ContentPaperId" = '30b2245a46cf45e39e4e72f0de77ff63';
UPDATE "MockBundleSections" SET "ContentPaperId" = '834bfdfe2ae24927a295a3dc6bfb36a6'
 WHERE "ContentPaperId" = '167124ddf8ec412e948df16dd0ea9784';

-- 2) Orphan cleanup (no FK -> manual). ListeningAttempts already verified 0 rows; skip.
DELETE FROM "ListeningQuestions" WHERE "PaperId" IN
  ('30b2245a46cf45e39e4e72f0de77ff63','167124ddf8ec412e948df16dd0ea9784');
DELETE FROM "ListeningParts" WHERE "PaperId" IN
  ('30b2245a46cf45e39e4e72f0de77ff63','167124ddf8ec412e948df16dd0ea9784');

-- 3) Delete the two duplicate papers (CASCADE clears ContentPaperAssets + ListeningExtractionDrafts)
DELETE FROM "ContentPapers" WHERE "Id" IN
  ('30b2245a46cf45e39e4e72f0de77ff63','167124ddf8ec412e948df16dd0ea9784');

-- 4) Unpublish broken speech-pathology-011 so sweep #3 can refill it
UPDATE "ContentPapers" SET "Status" = 0, "UpdatedAt" = NOW()
 WHERE "Id" = '1322a10d2e4644378ffdb131c3c2cb71';

-- Verification
\echo '--- AFTER STATE: nursing-002, physio-004, speech-path-011 ---'
SELECT "Id", "Status", "Title" FROM "ContentPapers"
 WHERE "Title" LIKE '%nursing Standard Set 002%'
    OR "Title" LIKE '%physiotherapy Standard Set 004%'
    OR "Title" LIKE '%speech-pathology Standard Set 011%'
 ORDER BY "Title";

\echo '--- Mock bundle sections now reference: ---'
SELECT "Id","ContentPaperId" FROM "MockBundleSections"
 WHERE "ContentPaperId" IN
  ('01ca161ecbfd4c8c949ccc928856dc6d','834bfdfe2ae24927a295a3dc6bfb36a6');

ROLLBACK;
