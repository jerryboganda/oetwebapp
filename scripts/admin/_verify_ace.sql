SELECT "Part", "DisplayOrder", "IsPrimary", "MediaAssetId", left("Id", 8) AS id8
FROM "ContentPaperAssets"
WHERE "PaperId" = 'ace9585ac0974f52b27d453502352dc4' AND "Role" = 0
ORDER BY "DisplayOrder";

SELECT "Status", "PublishedAt" FROM "ContentPapers" WHERE "Id" = 'ace9585ac0974f52b27d453502352dc4';

-- Count Listening drafts remaining (excluding archived/published)
SELECT "Status", count(*) FROM "ContentPapers" WHERE "SubtestCode"='listening' GROUP BY "Status" ORDER BY "Status";
