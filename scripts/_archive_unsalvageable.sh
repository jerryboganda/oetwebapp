#!/usr/bin/env bash
docker exec -i oet-postgres psql -U oet_learner -d oet_learner -At -P pager=off <<'SQL'
UPDATE "ContentPapers"
SET "Status"=6, "UpdatedAt"=now()
WHERE "Id" IN ('51900b7211b84a8dbeb1d336b6e7c14a','b8e0e9def00a4dd192beb08a5121deb9')
RETURNING "Id","Title","Status";
SQL
echo "--- post-archive listening status counts ---"
docker exec -i oet-postgres psql -U oet_learner -d oet_learner -At -F'|' -P pager=off <<'SQL'
SELECT "Status", count(*) FROM "ContentPapers"
WHERE "SubtestCode"='listening' GROUP BY "Status" ORDER BY 1;
SQL
