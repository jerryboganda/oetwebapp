# Duplicate entitlement analysis (observed 7× Full Condensed Medicine)

## Symptom
One Mark as Fulfilled action left the same `Full Condensed Recorded OET Course — Medicine` visible ~7 times for one candidate (seven `Subscription` rows, one logical order).

## Actual root cause (compound)
1. **No stable fulfillment identity on the creates.** `AdminService.CreateSubscriptionCoreAsync` always inserted a new `Subscription` row (new GUID) with no idempotency key and never set `ExpiresAt` via the canonical bundle. Any admin double-click / browser retry / HTTP retry / queue redelivery therefore inserted N rows for one logical event. `WithSubscriptionConcurrencyRetryAsync` even re-executed the whole insert on a concurrency conflict (clearing the tracker then inserting again), turning one conflict into two rows.
2. **Additive aggregation without ownership.** `EffectiveEntitlementResolver` aggregates packages additively and `UserAccessAllocationService.GrantPackageAsync` only recently gained the Pending-owns-slot reuse check; the older create path had no `AllocatedStatuses`/`CurrentOwnershipStatuses` reuse, so parallel rows all counted as entitlements and all rendered.
3. **Mark Fulfilled itself did not insert rows** — it activates one row idempotently (Fulfilled short-circuit + `processing` claim via `ExecuteUpdateAsync` + Serializable transaction + concurrency catch). The 7 rows were created upstream (repeated creates / checkout scaffolds / approval reuse gaps); Mark Fulfilled then activated one of them while the other six remained visible, which reads as "one click created seven".
4. **Expiry doubling masked the count.** `SubscriptionBundleInitializer.ResolveExpiry` anchored on a future `ExpiresAt`, so checkout completion + approval double-applied the bundle (≈180+180=360 days, observed 02/09/2026→28/08/2027). Multiple rows with staggered expiries look like multiple purchases.

## Why seven
Seven = seven inserts for one logical order (retries/clicks/redeliveries with no dedupe key). Each insert got its own `sub-*` id, same `UserId`+`PlanId`, slightly different `StartedAt`/`ChangedAt`, and (after the fix) the same clamped `ExpiresAt`. No unique DB guard existed on subscriptions (correctly — renewals must be allowed), and `SubscriptionItems` had no `(SubscriptionId, ItemCode, QuoteId)` unique guard, so addon replays could also fan out.

## Idempotency identity (chosen)
- Course entitlement (plan grant): **quote identity** — `(QuoteId → SubscriptionId)` for checkout; `(ManualPaymentRequest.Id → SubscriptionId)` for proof approval; `plan:{SubscriptionId}:{PlanCode}` / `manual:{RequestId}:{PlanCode}` / `admin-package:{SubscriptionId}:{PlanCode}` for AI-credit lots; `(WalletId, plan_grant, subscription, SubscriptionId)` for wallet lots. Same quote/proof/admin-event replays converge; a new quote/proof is a new purchase and grants anew.
- Add-on item: **(SubscriptionId, ItemCode, QuoteId)** unique partial index (`QuoteId IS NOT NULL`). Same quote replays converge on one row; different quotes (legitimate repurchases) coexist.
- Fulfilment claim: **(SubscriptionId, FulfilmentStatus)** atomic claim (`PendingManual`/`PendingVerification`/stale `Processing` → `Processing` via `ExecuteUpdateAsync`) + `Fulfilled` short-circuit returning the same DTO. Concurrent clicks converge on one winner; the loser reads the fulfilled row.
- Global `(UserId, PlanId)` uniqueness was REJECTED: legitimate renewals/repurchases share that pair at different times and must remain allowed.

## Backend implementation
- `SubscriptionBundleInitializer.ResolveExpiry`: anchor on `StartedAt` (idempotent set), still clamped to 180-day ceiling.
- `AdminService.CreateSubscriptionCoreAsync`: now stamps the canonical bundle (`ApplyBundle` → counters + `AccessDurationDays` + `ExpiresAt`) and mirrors one-time expiry into `NextRenewalAt`.
- `LearnerService.ApplyCheckoutCompletionAsync`: quote-`Completed` early-return (replay no-op) + `existingItem is null` addon guard + wallet/AI idempotency keys unchanged; Listening Recalls auto-grant sets `FulfilmentStatus=Auto` + `Active` without touching other plans.
- `ManualPaymentService.ApproveAsync`: `processing` claim + subscription reuse by quote then by plan + wallet/AI idempotency keys; promotes Pending invoices to Paid on activation.
- `BillingExpansionEndpoints.MarkSubscriptionFulfilled`: Fulfilled short-circuit (Ok, same DTO) + atomic `processing` claim + Serializable transaction + concurrency catch returning the fulfilled row; promotes Pending→Paid invoices on web-access release.
- DB: `IX_SubscriptionItems_SubscriptionId_ItemCode_QuoteId` unique partial index + migration `20261215090000_AddSubscriptionItemQuoteIdempotency` (dedupes same-quote replays first; different quotes untouched). No global subscription uniqueness (renewals preserved).

## Concurrency protection
- Checkout/webhook: single DB transaction per `ApplyVerifiedPaymentWebhookEventAsync` + quote-status guard + unique wallet idempotency + AI-lot keys + new SubscriptionItems unique index (concurrent inserts → one wins, other hits unique violation and converges).
- Approve: Serializable transaction + `processing` claim via `ExecuteUpdateAsync` (stale-window takeover for crashed claims).
- Mark Fulfilled: Serializable transaction + atomic claim + xmin reload + concurrency catch.

## Existing duplicates: classification + repair
- Genuine defect duplicate: same `UserId` + same `PlanId` + same `QuoteId`/`ManualPaymentRequest`/`StartedAt` window (seconds–minutes) + same price/currency, with only one backing payment. Preserve the earliest row (canonical), migrate progress/usage/counters/AI lots to it where split, cancel the rest, audit every step.
- Legitimate separate purchase/renewal: different `QuoteId` / different `ManualPaymentRequest` / `StartedAt` months apart / different payment transactions. NEVER merged or deleted.
- Repair: `scripts/billing-dedupe-subscriptions.sql` (dry-run SELECTs first, then audited merge; rerunnable; logs to `AuditEvents`). Run the SELECTs, review with Billing Ops, then run the merge in a transaction. Details in `10-data-repair-report.md`.
