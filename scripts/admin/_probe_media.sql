SELECT "Id","CreatedAt","Mime","ContentHash" FROM "MediaAssets"
WHERE "CreatedAt" > NOW() - INTERVAL '4 hours'
ORDER BY "CreatedAt" DESC LIMIT 20;

SELECT NOW() AS now_utc;
