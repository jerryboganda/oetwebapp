-- READ-ONLY. SELECT statements only; safe to run against production.
-- Counts learners that have more than one row in tables that are meant to hold
-- ONE row per learner but have only a non-unique index on "UserId"
-- (LearnerDbContext: Goals, Settings, Wallets).
-- Run: see docs/ops/DUPLICATE-LEARNER-ROWS.md
\set ON_ERROR_STOP on
BEGIN TRANSACTION READ ONLY;

-- 1. Summary per table: learners with >1 row, and how many extra rows exist.
SELECT 'Goals' AS table_name,
       count(*) AS learners_with_duplicates,
       coalesce(sum(n - 1), 0) AS extra_rows
FROM (SELECT "UserId", count(*) AS n FROM "Goals" GROUP BY "UserId" HAVING count(*) > 1) d
UNION ALL
SELECT 'Settings', count(*), coalesce(sum(n - 1), 0)
FROM (SELECT "UserId", count(*) AS n FROM "Settings" GROUP BY "UserId" HAVING count(*) > 1) d
UNION ALL
SELECT 'Wallets', count(*), coalesce(sum(n - 1), 0)
FROM (SELECT "UserId", count(*) AS n FROM "Wallets" GROUP BY "UserId" HAVING count(*) > 1) d;

-- 2. Five sample learners per table, with their row ids.
SELECT 'Goals' AS table_name, "UserId", count(*) AS rows,
       string_agg("Id"::text, ', ' ORDER BY "UpdatedAt" DESC) AS ids_newest_first
FROM "Goals" GROUP BY "UserId" HAVING count(*) > 1 ORDER BY count(*) DESC, "UserId" LIMIT 5;

-- Settings has no timestamp column; ids are listed in physical (ctid) order.
SELECT 'Settings' AS table_name, "UserId", count(*) AS rows,
       string_agg("Id"::text, ', ' ORDER BY ctid DESC) AS ids
FROM "Settings" GROUP BY "UserId" HAVING count(*) > 1 ORDER BY count(*) DESC, "UserId" LIMIT 5;

-- Wallets: money. Show each duplicate's balance and ledger size, never just the ids.
SELECT w."UserId", w."Id", w."CreditBalance", w."LastUpdatedAt",
       (SELECT count(*) FROM "WalletTransactions" t WHERE t."WalletId" = w."Id") AS transactions
FROM "Wallets" w
WHERE w."UserId" IN (
    SELECT "UserId" FROM "Wallets" GROUP BY "UserId" HAVING count(*) > 1
    ORDER BY count(*) DESC, "UserId" LIMIT 5)
ORDER BY w."UserId", w."LastUpdatedAt" DESC;

ROLLBACK;
