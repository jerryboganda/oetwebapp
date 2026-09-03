# Response (LT-RP) Migration Report

Migration: `20261216090000_ReclassifyWritingResponseLetterType`
(convention: hand-authored, data-only, no Designer file). Runs automatically
on deploy (`AUTO_MIGRATE=true`). Idempotent: predicates only match rows still
carrying a Response-like code, so reruns affect 0 rows. Reversal is
intentionally not automated — restore from backup if a row was misclassified.

## Statements (Postgres)

```sql
-- 1. Explicit business correction: Medicine - Ms Isabel Garcia → Discharge.
UPDATE "WritingScenarios"
SET "LetterType" = 'LT-DG'
WHERE UPPER("LetterType") IN ('LT-RP', 'RESPONSE', 'UPDATE')
  AND LOWER("Profession") = 'medicine'
  AND "Title" ILIKE '%isabel%garcia%';

-- 2. All remaining Response-classified rows → Other Letters fallback.
UPDATE "WritingScenarios"
SET "LetterType" = 'LT-OT'
WHERE UPPER("LetterType") IN ('LT-RP', 'RESPONSE', 'UPDATE');
```

Only `WritingScenarios.LetterType` is written. Profession, title, task
content, structured case-note sentences, `WritingTaskModelAnswers`
(keyed by `ScenarioId`), candidate submissions/history, and every other
column/relation are untouched — no orphans, no duplicates, no deletes.

## Rule audit (see `response-reclassification.csv`)

| task_id | profession | title | old_letter_type | new_letter_type | reason |
|---|---|---|---|---|---|
| (resolved at deploy) | medicine | Medicine - Ms Isabel Garcia | LT-RP (or RESPONSE/UPDATE variant) | LT-DG | explicit business correction in brief |
| (resolved at deploy) | * | * (any other RP-classified row) | LT-RP (or variant) | LT-OT | uncertain-case fallback policy: never force into a known category |

Per-row IDs are production data and are resolved when the migration executes.
The deploy operator must capture `GETUTCDATE()`-stamped counts — see
`06-data-integrity-verification.md` for the exact verification queries
(expected: RP count after = 0; total task count unchanged; profession
changes = 0).

## Why no other RP producers can recreate these rows

- Onboarding focus, pathway normalize/defaults, pathway generator: RP removed.
- Authoring upsert + legacy scenario view: RP normalizes to OT at save.
- Import mapping: RP/response/reply/update/advice labels map to OT.
- Publish gate: `letter_type_unsupported` blocks any residual RP row.
- AI scenario-generate prompt + schema: RP banned, OT fallback instructed.
- Frontend/admin never offered RP (verified by search).
