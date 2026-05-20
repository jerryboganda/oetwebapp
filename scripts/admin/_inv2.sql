\pset format aligned
SELECT cp."Status", COUNT(*) FROM "ContentPapers" cp WHERE cp."SubtestCode"='listening' GROUP BY cp."Status" ORDER BY cp."Status";
\echo ===INCOMPLETE-PUBLISHED===
SELECT cp."Slug", cp."Id",
  COUNT(*) FILTER (WHERE cpa."Role"=0) AS audio_rows
FROM "ContentPapers" cp
LEFT JOIN "ContentPaperAssets" cpa ON cpa."PaperId"=cp."Id"
WHERE cp."SubtestCode"='listening' AND cp."Status"=4
GROUP BY cp."Slug", cp."Id"
HAVING COUNT(*) FILTER (WHERE cpa."Role"=0) < 5
ORDER BY cp."Slug";
\echo ===PAPER-026-DETAIL===
SELECT cpa."Role", cpa."DisplayOrder", ma."Sha256", ma."MimeType", ma."SizeBytes"
FROM "ContentPaperAssets" cpa
JOIN "MediaAssets" ma ON ma."Id"=cpa."MediaAssetId"
WHERE cpa."PaperId"='dabaf1c3067542168080c04587086a16'
ORDER BY cpa."Role", cpa."DisplayOrder";
\echo ===REMAINING-DRAFTS===
SELECT cp."Slug", cp."Id",
  COUNT(*) FILTER (WHERE cpa."Role"=0) AS audio_rows,
  COUNT(*) FILTER (WHERE cpa."Role" IN (1,2,3)) AS text_rows
FROM "ContentPapers" cp
LEFT JOIN "ContentPaperAssets" cpa ON cpa."PaperId"=cp."Id"
WHERE cp."SubtestCode"='listening' AND cp."Status"=0
GROUP BY cp."Slug", cp."Id"
ORDER BY audio_rows DESC, cp."Slug";
