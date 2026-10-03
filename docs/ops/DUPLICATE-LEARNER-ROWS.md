# Duplicate per-learner rows (Goals / Settings / Wallets)

`Goals`, `Settings` and `Wallets` are meant to hold one row per learner, but `LearnerDbContext` gives each only a
non-unique index on `"UserId"`, so a race (two first-load requests) can insert a second row. No admin endpoint counts
these, so the count is a read-only SQL file.

## 1. Count (owner-approved, read-only)

`scripts/ops/count-duplicate-learner-rows.sql` is SELECT-only and runs inside `BEGIN TRANSACTION READ ONLY ... ROLLBACK`.

```bash
ssh vps 'docker exec -i oet-postgres sh -c '"'"'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB"'"'"'' \
  < scripts/ops/count-duplicate-learner-rows.sql
```

Output: per table, learners with duplicates and total extra rows; then five sample learners per table with their row
ids (Wallets also show balance, last update and transaction count per wallet).

If every count is 0, stop here; only step 3 (unique indexes) is worth doing.

## 2. Dedupe proposal — FOR OWNER APPROVAL, NOT EXECUTED

1. **Backup first:** `pg_dump` of `Goals`, `Settings`, `Wallets`, `WalletTransactions` (see
   `docs/security/runbook-backup-restore.md`), plus `CREATE TABLE ..._dupe_backup_YYYYMMDD AS SELECT` of every row
   that will be touched.
2. **Goals:** keep the newest row per `UserId` (`ORDER BY "UpdatedAt" DESC, "Id"`), delete the rest.
3. **Settings:** no timestamp column. Keep one row per `UserId`, chosen after a manual look at the samples (the
   row whose JSON columns are not all `{}`); delete the rest.
4. **Wallets: never delete blindly — this is money.** Per learner: pick the wallet with the most recent
   `LastUpdatedAt`, re-point `WalletTransactions."WalletId"` of the others to it, set its `CreditBalance` to the sum
   of the balances, then delete the emptied wallets. Owner reviews the per-learner before/after balances first.
5. Run each table in its own transaction; compare counts with step 1 before `COMMIT`.

## 3. Prevent recurrence (after cleanup only)

An EF migration that replaces the three `IX_..._UserId` indexes with unique ones
(`HasIndex(x => x.UserId).IsUnique()`). Creating it before cleanup fails on existing duplicates. The code paths that
create these rows must then treat a unique-violation as "already exists" and re-read.

## Rollback

- Data: restore the touched rows from the `_dupe_backup_` tables (or the `pg_dump`) inside one transaction.
- Index: revert the migration (`dotnet ef database update <previous>` via the normal deploy pipeline).
