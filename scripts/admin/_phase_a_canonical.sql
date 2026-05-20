SELECT "Id", "Status", "Title", "CreatedAt"
FROM "ContentPapers"
WHERE "SubtestCode"='listening'
  AND ("Title" LIKE '%nursing Standard Set 002%' OR "Title" LIKE '%speech-pathology Standard Set 011%')
ORDER BY "Title", "CreatedAt";

-- Count audio assets for each sibling so we pick the right canonical
SELECT p."Id", p."Title", p."Status",
  COUNT(CASE WHEN pa."Role"=0 THEN 1 END) AS audio,
  COUNT(CASE WHEN pa."Role"=2 THEN 1 END) AS script
FROM "ContentPapers" p
LEFT JOIN "ContentPaperAssets" pa ON pa."PaperId"=p."Id"
WHERE p."SubtestCode"='listening'
  AND (p."Title" LIKE '%nursing Standard Set 002%' OR p."Title" LIKE '%speech-pathology Standard Set 011%')
GROUP BY p."Id", p."Title", p."Status"
ORDER BY p."Title", p."Status";
