UPDATE "ContentPapers" SET "Status"=0, "ArchivedAt"=NULL WHERE "SubtestCode"='listening' AND "Status"=6 RETURNING "Id","Slug";
