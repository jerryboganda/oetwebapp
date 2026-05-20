SELECT p."Id", LENGTH(p."ExtractedTextJson"::text) AS extracted_len,
       (SELECT COUNT(*) FROM "ContentPaperAssets" a WHERE a."PaperId"=p."Id") AS total_assets,
       (SELECT COUNT(*) FROM "ContentPaperAssets" a WHERE a."PaperId"=p."Id" AND a."Role"=2) AS audioscript_assets,
       p."SourceProvenance" IS NOT NULL AS has_prov
FROM "ContentPapers" p
WHERE p."SubtestCode"='listening' AND p."Status"=0
ORDER BY p."CreatedAt";
