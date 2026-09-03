# Data repair report (historical duplicates)

## Status
No repair executed against production in this session (no destructive prod ops without explicit authorization; no live DB credentials in this environment). This report defines the safe, auditable procedure and the current migration coverage.

## What was repaired by migration (automatic, safe)
- `20261215090000_AddSubscriptionItemQuoteIdempotency`: before creating the unique partial index `(SubscriptionId, ItemCode, QuoteId)` where `QuoteId IS NOT NULL`, it deletes later replay rows within the same `(SubscriptionId, ItemCode, QuoteId)` group (keeps earliest `Id`). Same quote = same logical grant, so this only removes defect replays; different quotes (legitimate repurchases) and `QuoteId IS NULL` legacy/admin rows are untouched. Rerunnable (second run deletes zero rows); converges with the application-level `existingItem is null` guard.

## What still requires Billing Ops review (manual, audited)
- `Subscription` (course entitlement) duplicates: same `UserId` + same `PlanId` with multiple owned rows. Use `scripts/billing-dedupe-subscriptions.sql`:
  1. Run query 1 (candidate groups) and query 2 (per-group detail with quote/payment linkage).
  2. Classify each group: genuine defect (same quote/payment, `StartedAt` minutes apart, same amount) vs legitimate renewal/repurchase (different quotes/payments, `StartedAt` months apart).
  3. For genuine defects: keep the earliest `StartedAt` row as canonical; confirm progress/usage/counters/AI lots live on (or move to) the canonical row; `Cancelled` the rest; insert one `AuditEvents` row per group (`subscription.dedupe`, actor, kept + merged ids, reason). All inside a transaction; rerun selects to confirm zero remaining same-quote groups.
  4. Never delete by plan/course name alone; never merge different-quote groups.

## Rollback
- Index: `Down()` drops it (replays would again be possible; application guards remain).
- Merges: reverse by restoring the cancelled rows' prior `Status` from the audit detail (record prior statuses in the audit `Details` before updating).

## Evidence
- Migration file + model snapshot updated; backend builds with 0 errors.
- Triage SQL committed at `scripts/billing-dedupe-subscriptions.sql`.
- Post-repair verification: re-run triage query 1 (expect zero same-quote groups) + `TestG`/`TestH` + new hardening tests green (21/21).
