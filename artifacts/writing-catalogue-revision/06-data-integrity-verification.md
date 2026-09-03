# Data Integrity Verification

Local Postgres is unavailable in this environment (port 5432 closed), so row
counts are captured WHEN the migration executes at deploy. The migration is
idempotent and column-scoped; the deploy operator runs the queries below
before/after `database update` (or relies on `AUTO_MIGRATE=true` + these
read-only checks).

## Pre-migration snapshot (run before deploy)

```sql
SELECT COUNT(*) AS total_tasks FROM "WritingScenarios";
SELECT "LetterType", COUNT(*) FROM "WritingScenarios" GROUP BY "LetterType" ORDER BY 2 DESC;
SELECT "Id", "Profession", "Title", "LetterType", "Status"
FROM "WritingScenarios" WHERE UPPER("LetterType") IN ('LT-RP','RESPONSE','UPDATE');
SELECT "Id", "Profession", "Title", "LetterType", "Status"
FROM "WritingScenarios" WHERE "Title" ILIKE '%isabel%garcia%';
SELECT COUNT(*) AS ot_before FROM "WritingScenarios" WHERE "LetterType" = 'LT-OT';
```

## Post-migration acceptance (must ALL hold)

```sql
-- 1. Zero orphaned Response rows.
SELECT COUNT(*) AS rp_after FROM "WritingScenarios"
WHERE UPPER("LetterType") IN ('LT-RP','RESPONSE','UPDATE');  -- expect 0
-- 2. Isabel Garcia correction.
SELECT "Profession", "LetterType" FROM "WritingScenarios"
WHERE "Title" ILIKE '%isabel%garcia%';  -- expect medicine / LT-DG
-- 3. No task loss: total_tasks identical to pre-migration.
-- 4. No profession drift: GROUP BY "Profession" identical except RP rows moved.
-- 5. Model answers intact: every published scenario keeps its row.
SELECT COUNT(*) FROM "WritingTaskModelAnswers";  -- identical before/after
```

## Guarantees by construction

- `UPDATE` predicates match only RP-like rows; reruns affect 0 rows.
- Only `"LetterType"` is assigned; `Profession`, `Title`, content, sentences,
  `WritingTaskModelAnswers` (`ScenarioId` FK), submissions, history untouched.
- `LetterType varchar(8)`: `LT-DG`/`LT-OT` fit; composite index
  `(Profession, LetterType)` reused by catalogue filters — no new indexes, no
  N+1 (single composed query + one batched sentence load already in place).
- Performance impact: none — filter path unchanged (same indexed predicate);
  no new API calls; no new renders (two `<option>` additions reuse existing
  selects); classification stays deterministic pre-AI with no extra model calls.
