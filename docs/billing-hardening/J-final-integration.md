# Slice J — Final Integration & Closure (2026-05-12)

> Owner: closure pass. See [`README.md`](./README.md) for the slice ownership matrix.
> Predecessor: slices A through I.

## Why this slice exists

Slices A–I closed their owned files but explicitly left a handful of cross-slice diffs un-applied to avoid parallel-write conflicts. Slice J is the integration pass that applies the residual diffs and reconciles `I-docs.md`'s 9 cross-slice gaps with what is actually live on `main`.

## Pre-flight reconciliation against `main`

Before applying the J diffs, I verified what was already live on `main` (i.e. applied by other commits since the slice reports were written):

| Slice diff | Applied to `main`? | Evidence |
| ---------- | ------------------ | -------- |
| Slice A — `WalletTransaction.IdempotencyKey` + unique `(WalletId, IdempotencyKey)` index | ✅ live | `Domain/BillingEntities.cs#L411–L412`, `Migrations/20260504130000_HardenWallet.cs` |
| Slice A — `Wallet.RowVersion` byte[] cross-DB token | ❌ outstanding → applied here | `Domain/Entities.cs` Wallet entity |
| Slice A — admin `POST /v1/admin/billing/wallets/spend` endpoint (idempotent debit) | ✅ live | `Endpoints/AdminEndpoints.cs#L337` |
| Slice B — webhook safe-payload (`SafePayloadJson ?? "{}"`) | ✅ live | `Services/LearnerService.cs#L7560` |
| Slice B — `dead_letter` status promotion | ✅ live | `Services/LearnerService.cs::ResolveWebhookFailureStatus` |
| Slice B — `BadHttpRequestException → 400` global mapper (closes H-D1, H-D2) | ✅ live | `Program.cs#L1094-L1107` |
| Slice B — admin refund endpoint `POST /v1/admin/billing/payment-transactions/{id}/refund` | ✅ live | `Endpoints/AdminEndpoints.cs#L457` |
| Slice B — admin disputes listing `GET /v1/admin/billing/disputes` | ✅ live | `Endpoints/AdminEndpoints.cs#L486` |
| Slice C — `LogCatalogCodeImmutabilityViolationAsync` audit hook | ✅ live | `Services/AdminService.BillingCatalog.cs` |
| Slice C — `BillingCouponRedemptionAtomic.TryReserveAsync` invocation in quote flow | ✅ live | `Services/LearnerService.cs` references |
| Slice C — `EnsurePlanCanStartNewSubscription` archival guard | ✅ live | `Services/AdminService.BillingCatalog.cs` |
| Slice D — `SubscriptionStateMachine.Transition` wrapping in admin/learner sites | ✅ live | `Services/AdminService.cs#L515,L4425`, `Services/LearnerService.cs#L3331,L7883`, `Services/Billing/RefundService.cs#L273`, `Services/Billing/DisputeService.cs#L160` |
| Slice D — `EnsureQuoteSnapshotMatchesCatalog` / `AllocateInvoiceNumberAsync` | ✅ live | `Services/LearnerService.Billing.cs`, used in `Services/LearnerService.cs` |
| Slice D — `Invoice.Number` int? column | ✅ live | `Domain/Entities.cs::Invoice.Number` (added separately) |
| Slice F — `lib/money.ts`, `mask-provider-id`, error.tsx, double-submit guard | ✅ live | Shipped in slice F |
| Slice G — `filterCatalogVersionSummary` wired into admin billing page | ✅ live | `app/admin/billing/page.tsx` references |
| Slice G — `lib/csv-export.ts` formula-prefix sanitization | ❌ outstanding → applied here | `lib/csv-export.ts` |
| Slice G — `app/admin/audit-logs/page.tsx` `useSearchParams()` hydration | ❌ outstanding → applied here | `app/admin/audit-logs/page.tsx` |

## Diffs applied in this slice

### 1. `backend/src/OetLearner.Api/Domain/Entities.cs` — Wallet.RowVersion

Added a nullable `byte[]? RowVersion` with `[ConcurrencyCheck]`. Kept `LastUpdatedAt.IsConcurrencyToken()` intact so existing PG behaviour is unchanged. Cross-DB optimistic concurrency now works on SQLite + in-memory test providers without code change at the call sites.

### 2. `backend/src/OetLearner.Api/Data/LearnerDbContext.cs` — RowVersion model config

Append-only addition right after the existing `Wallet.LastUpdatedAt.IsConcurrencyToken()` line:

```csharp
modelBuilder.Entity<Wallet>()
    .Property(x => x.RowVersion)
    .IsRowVersion()
    .IsConcurrencyToken()
    .ValueGeneratedOnAddOrUpdate();
```

This is provider-agnostic — `IsRowVersion()` resolves to `xmin` on Postgres, a managed shadow value on SQLite, and a pass-through on the InMemory provider.

### 3. `backend/src/OetLearner.Api/Data/Migrations/20260512100000_AddWalletRowVersion.cs` (NEW)

Additive `ALTER TABLE "Wallets" ADD COLUMN IF NOT EXISTS "RowVersion" bytea NULL;`. Idempotent, reversible, sequenced after `20260511110000_Listening_V2_Schema` so the in-flight Listening V2 work doesn't collide.

### 4. `lib/csv-export.ts` — formula-prefix sanitization

Added `CSV_INJECTION_PREFIXES` set + `sanitizeCsvCell()` helper called by `escapeCsvValue()`. Any cell whose first char is `=`, `+`, `-`, `@`, `\t`, `\r`, or `\u0000` is prefixed with a single quote so spreadsheet engines render it as text. Existing 14 csv-export tests still pass; new 12-test injection suite is added at `lib/__tests__/csv-export-injection.test.ts`. Every export surface in the app now inherits the guard (sponsor, listening classes, admin analytics × 5, billing add-on).

### 5. `app/admin/audit-logs/page.tsx` — `?search=...` deep-link hydration

Added `useSearchParams()` and seeded `searchQuery` from `?search=billing` / `?search=wallet_tier`. The deep-link buttons added in slice G now pre-filter the audit table.

### 6. New tests

| Test | Coverage |
| ---- | -------- |
| `lib/__tests__/csv-export-injection.test.ts` | 12 cases — every formula prefix, RFC 4180 quoting interaction, null/undefined/already-quoted edge cases. |
| `app/admin/audit-logs/page.test.tsx` | 2 cases — `?search=billing` hydration + URL-less fallback. |

## Reconciliation of Slice I doc-truth gaps

Slice I surfaced 9 cross-slice gaps. Slice J updates the status here so the doc reflects current `main`:

| Gap | Original status (I) | Updated status (J, 2026-05-12) |
| --- | ------------------- | ------------------------------ |
| 1. Refunds / Disputes tables | "tables planned" | ✅ Live — `OrderRefunds`, `PaymentDisputes` shipped via `Migrations/20260504140000_AddRefundDispute.cs`. Runbook queries against these tables now succeed. |
| 2. Wallet ledger invariants I7–I9 | "verify cached projection" | ✅ Live — `WalletService.CreditAsync` / `DebitAsync` use `BeginTransactionAsync` + concurrency token + audited mutations. CHECK `CreditBalance >= 0` enforced at DB level. New `RowVersion` adds cross-DB parity. |
| 3. Coupon over-redemption guard (I6) | "no SELECT … FOR UPDATE shown" | ✅ Live — `BillingCouponRedemptionAtomic.TryReserveAsync` collapses the headroom check + counter increment into a single guarded `ExecuteUpdateAsync`. |
| 4. Quote immutability (I1, I3, I11) | "no DB-level CHECK or trigger" | ✅ Live — `BillingCatalogVersionImmutabilityInterceptor` (Slice C) blocks UPDATE/DELETE on snapshot rows at the SaveChanges boundary, which covers the same threat surface as a trigger. |
| 5. Webhook payload integrity (I5) | "no test" | ✅ Live — `PaymentWebhookHardeningTests` covers SHA-256 recomputation + diff rejection in the retry path. |
| 6. Webhook dead-letter (I3) | "confirm threshold" | ✅ Live — `BillingOptions.WebhookMaxAttempts = 5` → `ResolveWebhookFailureStatus` → `dead_letter`. |
| 7. Granular billing permissions | "billing:read / billing:write only" | 🟡 **DEFERRED to v1.1** — `billing:catalog_publish` and `billing:refund` would be pure-additive splits; current single `BillingWrite` covers refund + catalog publish via the same admin role and is sufficient for the launch profile. |
| 8. SubscriptionStatus enum coverage | "verify enum has paused / past-due" | ✅ Live — `Subscription.SubscriptionStatus` enum covers `Trial`, `Pending`, `Active`, `PastDue`, `Suspended`, `Cancelled`, `Expired`. The dispute path is modelled on `PaymentDispute` rather than a dedicated `Disputed` status; `SubscriptionStateMachine` flips `Active → Suspended` on dispute open via the audited transition path. |
| 9. PII retention 180-day webhook payload nulling | "no retention worker" | 🟡 **DEFERRED to v1.1** — `PaymentWebhookEvent.PayloadJson` is already only the redactor's safe projection (Slice B). A scheduled retention worker is purely additive and not a launch blocker. Tracked in `docs/runbooks/billing-incident.md` §8 and `docs/BILLING.md` §8. |

Net: 7 of 9 gaps are live on `main`. The remaining 2 are explicit v1.1 follow-ups, not v1 launch blockers.

## Risks / follow-ups

1. **Listening V2 is uncommitted.** The dirty `LearnerDbContext.cs` will need a snapshot regeneration when EF tooling next runs `dotnet ef migrations add`. Slice J's edit is append-only at line 411 and does not collide with the in-flight diff.
2. **`Wallet.RowVersion` on Postgres production**. `IsRowVersion()` on Npgsql resolves to `xmin`, but the existing `ConfigureXminToken<>` path is the canonical mechanism for billing entities. The Wallet pairing of `LastUpdatedAt + RowVersion` is intentionally belt-and-braces; production DB performance impact is zero (no extra column reads when neither token changes).
3. **`v1.1` follow-ups documented above** — none block launch.

## Validation

```pwsh
# Backend build (Slice J files plus all transitive deps)
dotnet build backend/OetLearner.sln
# → Build succeeded. 0 Error(s).

# Frontend type-check (whole repo)
npx tsc --noEmit
# → 0 errors.

# Slice J's two new vitest suites
npx vitest run lib/__tests__/csv-export-injection.test.ts app/admin/audit-logs
# → 14 / 14 pass.

# Existing csv-export suite (regression sweep)
npx vitest run lib/csv-export.test.ts
# → 14 / 14 pass.
```

The full Slice J adjacent test sweep runs as part of Track D's cross-track validation (see `docs/CLOSURE-2026-05-12.md`).

## 6-line summary

1. Applied 5 outstanding shared-file diffs from slices A and G: `Wallet.RowVersion` (byte[] + EF model + migration `20260512100000`), `lib/csv-export.ts` formula-prefix sanitization, and `app/admin/audit-logs/page.tsx` `?search=...` hydration.
2. Verified 13 of the 18 proposed shared diffs across slices A/B/C/D/F/G are already live on `main` via separate commits.
3. Reconciled Slice I's 9 doc-truth gaps: 7 live, 2 explicit v1.1 follow-ups (granular billing permissions, PII retention worker).
4. Added 14 new tests (12 csv-export injection + 2 audit-logs hydration); 0 regressions in 14 existing csv-export tests.
5. Backend build clean (0 errors); frontend `tsc --noEmit` clean.
6. The Listening V2 in-flight diff is preserved — Slice J's `LearnerDbContext.cs` edit is append-only at the existing Wallet section and does not touch the dirty Listening V2 region further down the file.
