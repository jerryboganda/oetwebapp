-- Fresh inventory: all listening papers + audio asset counts
\pset format aligned
SELECT
  cp."Status" AS status_code,
  COUNT(*)   AS papers
FROM "ContentPapers" cp
WHERE cp."SubtestCode" = 'listening'
GROUP BY cp."Status"
ORDER BY cp."Status";

\echo ===PUBLISHED-WITH-AUDIO-COUNTS===
SELECT
  cp."Slug",
  cp."Id",
  COUNT(*) FILTER (WHERE cpa."Role" = 0) AS audio_rows,
  COUNT(*) FILTER (WHERE cpa."Role" = 1) AS qpaper,
  COUNT(*) FILTER (WHERE cpa."Role" = 2) AS script,
  COUNT(*) FILTER (WHERE cpa."Role" = 3) AS answerkey
FROM "ContentPapers" cp
LEFT JOIN "ContentPaperAssets" cpa ON cpa."PaperId" = cp."Id"
WHERE cp."SubtestCode" = 'listening' AND cp."Status" = 4
GROUP BY cp."Slug", cp."Id"
HAVING COUNT(*) FILTER (WHERE cpa."Role" = 0) < 5
ORDER BY cp."Slug";

\echo ===DRAFT-INVENTORY===
SELECT
  cp."Slug",
  cp."Id",
  COUNT(*) FILTER (WHERE cpa."Role" = 0) AS audio_rows,
  COUNT(*) FILTER (WHERE cpa."Role" IN (1,2,3)) AS text_rows
FROM "ContentPapers" cp
LEFT JOIN "ContentPaperAssets" cpa ON cpa."PaperId" = cp."Id"
WHERE cp."SubtestCode" = 'listening' AND cp."Status" = 0
GROUP BY cp."Slug", cp."Id"
ORDER BY audio_rows DESC, cp."Slug";
