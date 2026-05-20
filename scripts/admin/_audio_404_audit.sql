-- Listening Published papers: audio asset URL audit
-- For each published listening paper, list its audio MediaAsset(s) + their StoragePath/BlobPath
\echo '=== 28 Listening Published papers + their audio MediaAsset rows ==='
SELECT cp."Id" AS paper_id,
       cp."Title",
       cpa."Role" AS asset_role,
       ma."Id" AS media_id,
       ma."StoragePath",
       ma."ByteLength",
       ma."ContentSha256"
FROM "ContentPapers" cp
LEFT JOIN "ContentPaperAssets" cpa ON cpa."ContentPaperId" = cp."Id" AND cpa."Role" = 0  -- Audio
LEFT JOIN "MediaAssets" ma ON ma."Id" = cpa."MediaAssetId"
WHERE cp."Subtest" = 'listening' AND cp."Status" = 4
ORDER BY cp."Title";

\echo ''
\echo '=== Specifically: dietetics Hard Set 024 paper details ==='
SELECT cp."Id", cp."Title", cp."Status", cp."ProviderProfession"
FROM "ContentPapers" cp
WHERE cp."Id" = 'c5e5f35210ad4f00b4cf0ed45cb1ec5f';

\echo ''
\echo '=== All ContentPaperAssets for that paper ==='
SELECT cpa."Role", cpa."MediaAssetId", ma."StoragePath", ma."ByteLength"
FROM "ContentPaperAssets" cpa
LEFT JOIN "MediaAssets" ma ON ma."Id" = cpa."MediaAssetId"
WHERE cpa."ContentPaperId" = 'c5e5f35210ad4f00b4cf0ed45cb1ec5f';

\echo ''
\echo '=== Specifically: MediaAsset 039884eed71d445bb6bfda4eb0c59b7b ==='
SELECT * FROM "MediaAssets" WHERE "Id" = '039884eed71d445bb6bfda4eb0c59b7b';

\echo ''
\echo '=== Count MediaAssets with NULL or empty StoragePath that are audio-typed ==='
SELECT COUNT(*) AS null_or_empty_storagepath
FROM "MediaAssets"
WHERE "MimeType" LIKE 'audio/%' AND ("StoragePath" IS NULL OR "StoragePath" = '');
