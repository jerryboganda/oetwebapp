SELECT "SubtestCode", "Status", COUNT(*) AS n
FROM "ContentPapers"
GROUP BY "SubtestCode", "Status"
ORDER BY "SubtestCode", "Status";

-- Specifically list all non-Published non-Archived (status NOT IN (4,6)) papers
SELECT "SubtestCode", "Status", "Id", "Title"
FROM "ContentPapers"
WHERE "Status" NOT IN (4, 6)
ORDER BY "SubtestCode", "Status", "Title";
