SELECT "Id", "MediaKind", "Status", "OriginalFilename", "UploadedAt"
FROM "MediaAssets"
WHERE "Id" IN ('52dd4909d08a4cb9a8af99e976839047','2264284933bf457593c549dab4ae7aff','4ed03289defe4495af60aa70c86a83cd');
SELECT count(*) AS audio_assets FROM "MediaAssets" WHERE "MediaKind" = 'audio';
SELECT "State", count(*) FROM "AdminUploadSessions" GROUP BY "State";
SELECT "State", "IntendedRole", "MediaAssetId", "OriginalFilename" FROM "AdminUploadSessions"
WHERE "State" = 2 AND "IntendedRole"='Audio' ORDER BY "CreatedAt" DESC LIMIT 5;
