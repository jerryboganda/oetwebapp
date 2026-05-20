#!/usr/bin/env bash
docker exec -i oet-postgres psql -U oet_learner -d oet_learner -v ON_ERROR_STOP=1 <<'SQL'
\timing off
\echo === LISTENING_ASSETS ===
select p."Slug" as slug, p."Status" as status,
  coalesce((select count(*) from "ContentPaperAssets" a where a."PaperId"=p."Id"),0) as total,
  coalesce((select string_agg(distinct a."Role"::text,',' order by a."Role"::text) from "ContentPaperAssets" a where a."PaperId"=p."Id"),'-') as roles,
  coalesce((select string_agg(distinct a."Part",',') from "ContentPaperAssets" a where a."PaperId"=p."Id"),'-') as parts
from "ContentPapers" p
where p."SubtestCode"='listening' and p."CreatedAt" > now() - interval '48 hours'
order by p."CreatedAt";
SQL
