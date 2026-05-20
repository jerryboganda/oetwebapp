-- All listening papers grouped by status with audio counts.
SELECT
  p."Id"             AS paper_id,
  p."Title"          AS title,
  p."Status"         AS status,
  COUNT(CASE WHEN pa."Role" = 0 THEN 1 END) AS audio_count,
  COUNT(CASE WHEN pa."Role" = 2 THEN 1 END) AS audioscript_count,
  COUNT(CASE WHEN pa."Role" = 1 THEN 1 END) AS questionpaper_count
FROM "ContentPapers" p
LEFT JOIN "ContentPaperAssets" pa ON pa."PaperId" = p."Id"
WHERE p."SubtestCode" = 'listening'
GROUP BY p."Id", p."Title", p."Status"
ORDER BY p."Status", p."Title";
