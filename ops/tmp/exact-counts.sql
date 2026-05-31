\pset pager off
select schemaname, relname,
  (xpath('/row/c/text()', query_to_xml(format('select count(*) c from %I.%I', schemaname, relname), false, true, '')))[1]::text::bigint as exact_count
from pg_stat_user_tables
order by exact_count desc, relname
limit 80;
