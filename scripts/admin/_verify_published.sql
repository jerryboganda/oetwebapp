SELECT cp."Slug", cp."Status",
       COUNT(*) FILTER (WHERE cpa."Role"=0) AS audio_rows,
       cp."UpdatedAt"
FROM "ContentPapers" cp
LEFT JOIN "ContentPaperAssets" cpa ON cpa."PaperId" = cp."Id"
WHERE cp."SubtestCode"='listening'
  AND cp."UpdatedAt" > NOW() - INTERVAL '4 hours'
GROUP BY cp."Slug", cp."Status", cp."UpdatedAt"
ORDER BY cp."UpdatedAt" DESC;
