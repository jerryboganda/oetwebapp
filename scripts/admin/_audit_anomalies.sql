-- =========================================================================
-- Audit: 3 anomaly papers + full backfill inventory
-- =========================================================================

\echo == A: speech-pathology-standard-set-011 (Status=4, audio=1) ==
SELECT cp."Id", cp."Slug", cp."Status", cp."UpdatedAt"
FROM "ContentPapers" cp
WHERE cp."Slug" = 'oet-listening-practice-speech-pathology-standard-set-011';

SELECT cpa."Part", cpa."DisplayOrder", cpa."Role", cpa."CreatedAt", ma."SizeBytes", ma."MimeType"
FROM "ContentPaperAssets" cpa
LEFT JOIN "MediaAssets" ma ON ma."Id" = cpa."MediaAssetId"
WHERE cpa."PaperId" = (SELECT "Id" FROM "ContentPapers" WHERE "Slug"='oet-listening-practice-speech-pathology-standard-set-011')
ORDER BY cpa."Role", cpa."DisplayOrder";

\echo == B: nursing-standard-set-002 (Status=4, audio=4) ==
SELECT cp."Id", cp."Slug", cp."Status"
FROM "ContentPapers" cp
WHERE cp."Slug" = 'oet-listening-practice-nursing-standard-set-002';

SELECT cpa."Part", cpa."DisplayOrder", cpa."Role", ma."SizeBytes"
FROM "ContentPaperAssets" cpa
LEFT JOIN "MediaAssets" ma ON ma."Id" = cpa."MediaAssetId"
WHERE cpa."PaperId" = (SELECT "Id" FROM "ContentPapers" WHERE "Slug"='oet-listening-practice-nursing-standard-set-002')
ORDER BY cpa."Role", cpa."DisplayOrder";

\echo == C: dentistry-hard-set-003 (Status=0, audio=3) ==
SELECT cp."Id", cp."Slug", cp."Status"
FROM "ContentPapers" cp
WHERE cp."Slug" = 'oet-listening-practice-dentistry-hard-set-003';

SELECT cpa."Part", cpa."DisplayOrder", cpa."Role", ma."SizeBytes"
FROM "ContentPaperAssets" cpa
LEFT JOIN "MediaAssets" ma ON ma."Id" = cpa."MediaAssetId"
WHERE cpa."PaperId" = (SELECT "Id" FROM "ContentPapers" WHERE "Slug"='oet-listening-practice-dentistry-hard-set-003')
ORDER BY cpa."Role", cpa."DisplayOrder";

\echo == D: Full listening backfill inventory ==
SELECT
  COUNT(*) FILTER (WHERE cp."Status" = 4) AS published,
  COUNT(*) FILTER (WHERE cp."Status" = 0) AS draft,
  COUNT(*) FILTER (WHERE cp."Status" = 6) AS archived,
  COUNT(*) AS total
FROM "ContentPapers" cp
WHERE cp."SubtestCode" = 'listening';

\echo == E: Drafts with zero audio (truly untouched) ==
SELECT cp."Slug"
FROM "ContentPapers" cp
LEFT JOIN "ContentPaperAssets" cpa ON cpa."PaperId" = cp."Id" AND cpa."Role" = 0
WHERE cp."SubtestCode" = 'listening' AND cp."Status" = 0
GROUP BY cp."Slug"
HAVING COUNT(cpa."Id") = 0
ORDER BY cp."Slug";

\echo == F: Drafts with partial audio (started, not finished) ==
SELECT cp."Slug", COUNT(cpa."Id") AS audio_rows
FROM "ContentPapers" cp
LEFT JOIN "ContentPaperAssets" cpa ON cpa."PaperId" = cp."Id" AND cpa."Role" = 0
WHERE cp."SubtestCode" = 'listening' AND cp."Status" = 0
GROUP BY cp."Slug"
HAVING COUNT(cpa."Id") BETWEEN 1 AND 4
ORDER BY cp."Slug";
