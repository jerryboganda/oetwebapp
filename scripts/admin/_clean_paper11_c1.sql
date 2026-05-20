-- Clean paper 11 (nursing Standard Set 026): drop contaminated C1 row + its DO-fallback media asset
BEGIN;

\set paperId '\'dabaf1c3067542168080c04587086a16\''
\set badAssetRowId '\'eff1006251d84d2090a971ab7b373e9d\''
\set badMediaId '\'4b295e79fb4b493ba0f4a0b46cc62b1a\''

-- Confirm row before delete
SELECT 'BEFORE' AS phase, "Id", "Part", "MediaAssetId"
FROM "ContentPaperAssets"
WHERE "Id" = :badAssetRowId;

DELETE FROM "ContentPaperAssets" WHERE "Id" = :badAssetRowId;
DELETE FROM "MediaAssets" WHERE "Id" = :badMediaId;

-- Confirm remaining rows
SELECT 'AFTER' AS phase, "Id", "Part", "DisplayOrder"
FROM "ContentPaperAssets"
WHERE "PaperId" = :paperId AND "Role" = 0
ORDER BY "DisplayOrder";

COMMIT;
