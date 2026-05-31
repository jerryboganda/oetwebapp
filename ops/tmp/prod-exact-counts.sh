#!/usr/bin/env bash
set -euo pipefail
timeout 120 docker exec -i oet-postgres psql -U oet_learner -d oet_learner -P pager=off <<'SQL'
select schemaname, relname,
  (xpath('/row/c/text()', query_to_xml(format('select count(*) c from %I.%I', schemaname, relname), false, true, '')))[1]::text::bigint as exact_count
from pg_stat_user_tables
order by exact_count desc, relname
limit 80;
SQL
