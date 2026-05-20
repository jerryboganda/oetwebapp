-- Check mock bundle references (RESTRICT FK blocker)
SELECT mbs."Id", mbs."ContentPaperId", mb."Title" AS bundle_title
FROM "MockBundleSections" mbs
LEFT JOIN "MockBundles" mb ON mb."Id" = mbs."BundleId"
WHERE mbs."ContentPaperId" IN (
  '30b2245a46cf45e39e4e72f0de77ff63',
  '167124ddf8ec412e948df16dd0ea9784',
  '1322a10d2e4644378ffdb131c3c2cb71'
);

-- All FKs to ContentPapers (broader — any constraint type, also schema)
SELECT n.nspname||'.'||conrelid::regclass AS tbl, conname, pg_get_constraintdef(c.oid)
FROM pg_constraint c
JOIN pg_class cl ON cl.oid = c.conrelid
JOIN pg_namespace n ON n.oid = cl.relnamespace
WHERE confrelid = '"ContentPapers"'::regclass
ORDER BY tbl;

-- Tables that have a PaperId column (may not be strict FK but still data we care about)
SELECT table_name, column_name FROM information_schema.columns
WHERE column_name IN ('PaperId','ContentPaperId') AND table_schema='public'
ORDER BY table_name;
