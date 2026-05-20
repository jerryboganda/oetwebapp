SELECT "PaperId","Role","Part","MediaAssetId","CreatedAt"
FROM "ContentPaperAssets"
WHERE "PaperId"='cd17cff04b9d40e486035bf2f90411ea'
ORDER BY "CreatedAt" DESC;

SELECT "Id","Status","Skill","Title" FROM "ContentPapers"
WHERE "Status"=0 AND "Skill"='Listening' ORDER BY "Title" LIMIT 5;
