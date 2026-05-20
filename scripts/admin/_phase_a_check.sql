-- Compare physio-004 dupes (keep newer)
SELECT "Id", "Status", "CreatedAt", "UpdatedAt"
FROM "ContentPapers"
WHERE "Id" IN ('834bfdfe2ae24927a295a3dc6bfb36a6','167124ddf8ec412e948df16dd0ea9784');

-- Foreign keys referencing ContentPapers (need to know what cascades)
SELECT conrelid::regclass AS table_name, conname, pg_get_constraintdef(oid)
FROM pg_constraint
WHERE confrelid = '"ContentPapers"'::regclass AND contype='f'
ORDER BY table_name;

-- Sanity: confirm IDs to delete exist with expected status
SELECT "Id", "Status", "Title"
FROM "ContentPapers"
WHERE "Id" IN (
  '30b2245a46cf45e39e4e72f0de77ff63',
  '167124ddf8ec412e948df16dd0ea9784',
  '834bfdfe2ae24927a295a3dc6bfb36a6',
  '1322a10d2e4644378ffdb131c3c2cb71'
);
