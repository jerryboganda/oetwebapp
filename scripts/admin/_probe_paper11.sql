SELECT column_name FROM information_schema.columns WHERE table_name='MediaAssets' ORDER BY ordinal_position;
SELECT cpa."Id", cpa."Part", cpa."DisplayOrder", cpa."CreatedAt", cpa."MediaAssetId"
FROM "ContentPaperAssets" cpa
WHERE cpa."PaperId" = 'dabaf1c3067542168080c04587086a16'
  AND cpa."Role" = 0
ORDER BY cpa."DisplayOrder";
