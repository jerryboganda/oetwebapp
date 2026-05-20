-- Listening Published papers audio asset audit (corrected: PaperId column)
\echo '=== MediaAsset columns ==='
SELECT column_name, data_type FROM information_schema.columns
WHERE table_name = 'MediaAssets' ORDER BY ordinal_position;

\echo ''
\echo '=== Specifically: dietetics Hard Set 024 audio chain ==='
SELECT cpa."Role", cpa."Part", cpa."MediaAssetId", ma.*
FROM "ContentPaperAssets" cpa
LEFT JOIN "MediaAssets" ma ON ma."Id" = cpa."MediaAssetId"
WHERE cpa."PaperId" = 'c5e5f35210ad4f00b4cf0ed45cb1ec5f' AND cpa."Role" = 0;

\echo ''
\echo '=== All 28 published listening papers: audio asset count per paper ==='
SELECT cp."Id", cp."Title",
  COUNT(cpa."Id") FILTER (WHERE cpa."Role" = 0) AS audio_asset_rows,
  COUNT(ma."Id") FILTER (WHERE cpa."Role" = 0 AND ma."Id" IS NOT NULL) AS audio_with_media_row
FROM "ContentPapers" cp
LEFT JOIN "ContentPaperAssets" cpa ON cpa."PaperId" = cp."Id"
LEFT JOIN "MediaAssets" ma ON ma."Id" = cpa."MediaAssetId"
WHERE cp."Subtest" = 'listening' AND cp."Status" = 4
GROUP BY cp."Id", cp."Title"
ORDER BY audio_with_media_row, cp."Title";
