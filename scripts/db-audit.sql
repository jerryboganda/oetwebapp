-- Live DB health audit queries

\echo === 1. Table sizes (top 25) ===
SELECT
  n.nspname||'.'||c.relname AS table,
  pg_size_pretty(pg_total_relation_size(c.oid)) AS total,
  pg_size_pretty(pg_relation_size(c.oid)) AS heap,
  pg_size_pretty(pg_indexes_size(c.oid)) AS indexes,
  c.reltuples::bigint AS est_rows
FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
WHERE c.relkind IN ('r','p') AND n.nspname='public'
ORDER BY pg_total_relation_size(c.oid) DESC
LIMIT 25;

\echo === 2. Unused indexes (idx_scan=0, excluding PK/unique) ===
SELECT s.schemaname||'.'||s.relname AS table, s.indexrelname AS index,
       pg_size_pretty(pg_relation_size(s.indexrelid)) AS size,
       s.idx_scan
FROM pg_stat_user_indexes s
JOIN pg_index i ON i.indexrelid=s.indexrelid
WHERE s.idx_scan=0 AND NOT i.indisunique AND NOT i.indisprimary
  AND s.schemaname='public'
ORDER BY pg_relation_size(s.indexrelid) DESC
LIMIT 30;

\echo === 3. Duplicate indexes (same cols, same table) ===
SELECT a.indrelid::regclass AS table,
       a.indexrelid::regclass AS idx_a, b.indexrelid::regclass AS idx_b,
       a.indkey::text AS cols
FROM pg_index a JOIN pg_index b
  ON a.indrelid=b.indrelid AND a.indkey=b.indkey AND a.indexrelid<b.indexrelid
WHERE a.indrelid IN (SELECT oid FROM pg_class WHERE relnamespace=(SELECT oid FROM pg_namespace WHERE nspname='public'));

\echo === 4. Tables with sequential scans dominating index scans ===
SELECT schemaname||'.'||relname AS table,
       seq_scan, idx_scan,
       seq_tup_read, idx_tup_fetch,
       n_live_tup AS live_rows,
       CASE WHEN seq_scan+idx_scan=0 THEN 0
            ELSE round(100.0*seq_scan/(seq_scan+idx_scan),1) END AS pct_seq
FROM pg_stat_user_tables
WHERE schemaname='public' AND n_live_tup > 100 AND seq_scan > idx_scan
ORDER BY seq_tup_read DESC
LIMIT 20;

\echo === 5. Bloat estimate (pgstattuple-free approximation) ===
SELECT schemaname||'.'||relname AS table,
       n_live_tup, n_dead_tup,
       round(100.0*n_dead_tup/GREATEST(n_live_tup+n_dead_tup,1),1) AS pct_dead,
       last_autovacuum, last_autoanalyze
FROM pg_stat_user_tables
WHERE schemaname='public' AND n_dead_tup > 1000
ORDER BY n_dead_tup DESC
LIMIT 20;

\echo === 6. Foreign keys missing supporting index on the child column ===
SELECT conrelid::regclass AS table, conname, a.attname AS fk_col
FROM pg_constraint c
JOIN pg_attribute a ON a.attrelid=c.conrelid AND a.attnum=ANY(c.conkey)
WHERE c.contype='f'
  AND c.connamespace=(SELECT oid FROM pg_namespace WHERE nspname='public')
  AND NOT EXISTS (
    SELECT 1 FROM pg_index i
    WHERE i.indrelid=c.conrelid
      AND c.conkey[1]=ANY(i.indkey::smallint[])
  )
ORDER BY 1,2;

\echo === 7. Long/slow queries currently running (snapshot) ===
SELECT pid, now()-query_start AS duration, state, wait_event_type, wait_event,
       left(query,120) AS query
FROM pg_stat_activity
WHERE state<>'idle' AND query_start<now()-interval '2 seconds'
ORDER BY query_start
LIMIT 10;

\echo === 8. Cache hit ratio ===
SELECT 'heap' AS kind,
       round(100.0*sum(heap_blks_hit)/GREATEST(sum(heap_blks_hit)+sum(heap_blks_read),1),2) AS hit_pct
FROM pg_statio_user_tables
UNION ALL
SELECT 'index',
       round(100.0*sum(idx_blks_hit)/GREATEST(sum(idx_blks_hit)+sum(idx_blks_read),1),2)
FROM pg_statio_user_indexes;

\echo === 9. Connections by state ===
SELECT state, count(*) FROM pg_stat_activity GROUP BY 1 ORDER BY 2 DESC;

\echo === 10. Tables without primary key ===
SELECT n.nspname||'.'||c.relname AS table
FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
WHERE c.relkind='r' AND n.nspname='public'
  AND NOT EXISTS (SELECT 1 FROM pg_index i WHERE i.indrelid=c.oid AND i.indisprimary)
ORDER BY 1;
