# Final verification (rollout gate)

## Green now
- Backend build: 0 errors.
- Focused tests: 21/21 (12 new + 9 queue).
- Writing export: 210/210 reconciled; regeneration command `pnpm run writing:qa-export`.
- Ship-gate file check on `scripts/writing-qa-export.mjs`: clean. Full `ship:gate` cannot pass in this working tree because of unrelated concurrent changes (Writing taxonomy series, `failed_tests.txt`, `writing_*.txt`, `lib/writing/*`, `20261216090000_*` — not this task; left untouched).

## Must still happen before final rollout / bulk QA (live environment, with credentials)
1. Apply migrations to staging (includes `20261215090000_AddSubscriptionItemQuoteIdempotency`; expect zero-downtime; unique index creation fails loudly if same-quote leftovers exist — run the triage SELECTs first).
2. Fresh test-candidate purchase (standalone Listening Recalls, gateway sandbox): verify Auto+Active, one `Paid` invoice, one entitlement, `StartedAt+180`, Recalls visible, no admin step; replay the webhook (expect same ids, no dupes).
3. Fresh test-candidate fulfilment (Full Condensed Medicine via proof Approve → Mark Fulfilled): verify one entitlement, `StartedAt+180` (e.g. 02/09/2026→01/03/2027), one `Paid` invoice after activation, retry/concurrent clicks stay at one.
4. Failed/pending sandbox payments: verify no learner invoice (list + direct download 404), no `AdminBillingFailureAlert`; AI-budget threshold fires `AdminAiBudgetAlert` instead.
5. Video A/B/C on staging (Published+Ready videos, profession/package scopes): entitled see, unrelated 404, no per-user rows.
6. Run the billing triage SQL on a production replica; repair only reviewed genuine groups.
7. Then run the full backend + frontend gates on a clean tree and deploy via the normal pipeline (not performed here).

## Database changes
- Migration `20261215090000_AddSubscriptionItemQuoteIdempotency` (unique partial index + same-quote dedupe); snapshot updated. No subscription-level uniqueness (renewals preserved). Rollback: drop index.
- No seed data changes; `listening-recalls` identity/price/duration untouched (code `listening-recalls`, 180 days).

## Remaining risks
- Live webhook concurrency (Postgres Serializable + claim + unique index) is code-verified but not load-tested here.
- New-purchaser webhook-before-association timing relies on quote-user binding; edge cases need sandbox replay.
- `ship:gate` full-tree status is red due to others' in-flight work; coordinate before push/deploy. No push or deploy was performed in this session.
