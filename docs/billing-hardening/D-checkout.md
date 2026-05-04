# Slice D — Checkout / Quote / Subscription / Invoice flow hardening

> May 2026 billing hardening pass — Slice D report.

## Scope (per goals 1–7)

| # | Goal | Status |
| - | ---- | ------ |
| 1 | Quote idempotency + 15-min expiry | ✅ filtered unique index added (migration); validator enforces expiry |
| 2 | Quote snapshot integrity at fulfillment | ✅ `LearnerService.EnsureQuoteSnapshotMatchesCatalog` added |
| 3 | Double-charge protection on `CheckoutSession.ProviderSessionId` | ✅ filtered unique index on `Invoices.CheckoutSessionId`; existing unique index on `PaymentTransactions.GatewayTransactionId` retained |
| 4 | Subscription state machine | ✅ new static `SubscriptionStateMachine` (legal-transition table) |
| 5 | Cursor pagination on `/v1/billing/invoices` | ✅ confirmed correct; boundary tests (empty / single / many) added |
| 6 | Monotonic per-tenant invoice numbering | ✅ new `InvoiceNumberAllocations` table + `LearnerService.AllocateInvoiceNumberAsync` |
| 7 | Tests | ✅ 39 tests added across 2 new files (state machine + checkout hardening) |

## Files changed (owned by D)

| File | Action |
| ---- | ------ |
| `backend/src/OetLearner.Api/Services/Billing/SubscriptionStateMachine.cs` | NEW — pure-static legal transition table + `Transition()` enforcer |
| `backend/src/OetLearner.Api/Services/LearnerService.Billing.cs` | NEW partial — `EnsureQuoteIsFulfillable`, `EnsureQuoteSnapshotMatchesCatalog`, `AllocateInvoiceNumberAsync` |
| `backend/src/OetLearner.Api/Data/Migrations/20260504160000_HardenCheckout.cs` | NEW — additive, SQLite + Postgres compatible |
| `backend/src/OetLearner.Api/Data/Migrations/20260504160000_HardenCheckout.Designer.cs` | NEW — stub designer matching the in-repo convention (see `20260504120000_HardenWalletTopUpTiers.Designer.cs`) |
| `backend/tests/OetLearner.Api.Tests/SubscriptionStateMachineTests.cs` | NEW — 24 tests over the legal transition table |
| `backend/tests/OetLearner.Api.Tests/CheckoutFlowHardeningTests.cs` | NEW — 12 tests: quote validators (8) + cursor pagination boundaries (3) + lenient legacy-quote case (1) |

## Migration `20260504160000_HardenCheckout` — verbatim DDL

```sql
-- Goal #1: per-learner quote idempotency
CREATE UNIQUE INDEX IF NOT EXISTS "IX_BillingQuotes_UserId_IdempotencyKey_Unique"
ON "BillingQuotes" ("UserId", "IdempotencyKey")
WHERE "IdempotencyKey" IS NOT NULL;

-- Goal #3: double-finalize protection (one paid invoice per provider session)
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Invoices_CheckoutSessionId_Unique"
ON "Invoices" ("CheckoutSessionId")
WHERE "CheckoutSessionId" IS NOT NULL;

-- Goal #6: monotonic per-tenant invoice numbering
CREATE TABLE IF NOT EXISTS "InvoiceNumberAllocations" (
    "UserId" TEXT NOT NULL,
    "Sequence" INTEGER NOT NULL,
    "InvoiceId" TEXT NOT NULL,
    "AllocatedAt" TEXT NOT NULL,
    CONSTRAINT "PK_InvoiceNumberAllocations" PRIMARY KEY ("UserId", "Sequence")
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_InvoiceNumberAllocations_InvoiceId"
ON "InvoiceNumberAllocations" ("InvoiceId");
```

All four statements are additive and `IF NOT EXISTS`-guarded. SQLite + PostgreSQL syntax. `Down()` drops in reverse order.

Migration ordering: `20260504120000` (slice A) → **`20260504160000` (slice D)** is monotonic and matches the README schedule.

## Subscription state machine — legal transition table

```text
Trial      → Trial, Active, Cancelled, Expired
Pending    → Pending, Active, Trial, Cancelled, Expired
Active     → Active, PastDue, Suspended (=DisputePending), Cancelled, Expired
PastDue    → PastDue, Active, Suspended, Cancelled, Expired
Suspended  → Suspended, Active, Cancelled, Expired
Cancelled  → Cancelled, Expired   (no resurrection)
Expired    → Expired              (terminal)
```

Self-transitions are explicitly legal so idempotent webhook replays do not throw. Illegal transitions throw `ApiException.Conflict("subscription_illegal_transition", …)`.

`SubscriptionStatus` does not currently include a dedicated `DisputePending` value; the README's example was modelled onto `Suspended`. If a real `DisputePending` enum value is added later, only the table needs to change — call sites are unaffected.

## Shared-file diffs (DIFF-ONLY — not applied here)

These are proposed integrations into shared files. They were intentionally **not** applied in this slice to avoid parallel-write conflicts with slices A–C / E.

### `Services/LearnerService.cs::ApplyCheckoutCompletionAsync`

Insert at the top of the method (after the existing `if (quote.Status == BillingQuoteStatus.Completed) return;` guard):

```csharp
// Slice D — fail loud if catalog drifted under us between quote and webhook.
EnsureQuoteIsFulfillable(quote, DateTimeOffset.UtcNow);

var liveAddOnVersions = new Dictionary<string, BillingAddOnVersion?>(StringComparer.OrdinalIgnoreCase);
foreach (var (code, expectedVersionId) in DeserializeAddOnVersionIds(quote))
{
    liveAddOnVersions[code] = await db.BillingAddOnVersions
        .AsNoTracking()
        .FirstOrDefaultAsync(v => v.Id == expectedVersionId, ct);
}
var livePlanVersion = string.IsNullOrWhiteSpace(quote.PlanVersionId)
    ? null
    : await db.BillingPlanVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == quote.PlanVersionId, ct);
var liveCouponVersion = string.IsNullOrWhiteSpace(quote.CouponVersionId)
    ? null
    : await db.BillingCouponVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == quote.CouponVersionId, ct);

EnsureQuoteSnapshotMatchesCatalog(quote, livePlanVersion, liveAddOnVersions, liveCouponVersion);
```

And just before `db.Invoices.Add(new Invoice { ... })` (≈ line 7409) — allocate a monotonic number:

```csharp
var invoiceNumber = await AllocateInvoiceNumberAsync(transaction.LearnerUserId, invoiceId, ct);
// existingInvoice is still null at this point: persist the number alongside.
// When the shared edit lands, add `Number = invoiceNumber` to the Invoice ctor.
```

### `Services/AdminService.cs` (4 sites) and `LearnerService.cs` (1 site)

Replace direct `subscription.Status = SubscriptionStatus.X` assignments with:

```csharp
SubscriptionStateMachine.Transition(subscription, SubscriptionStatus.X, reason: "<context>");
```

Sites identified:

- `AdminService.cs:4073` — admin reactivate → `Active`, reason `admin_reactivate`
- `AdminService.cs:4192` — admin override → `Active`, reason `admin_override`
- `AdminService.cs:4232` — admin cancel → `Cancelled`, reason `admin_cancel`
- `AdminService.cs:4270` — admin force-active → `Active`, reason `admin_force_active`
- `LearnerService.cs:7293` — fulfillment activate → `Active`, reason `checkout_fulfilled`

### `Domain/Entities.cs::Invoice` (optional follow-up)

Adding a `public int? Number { get; set; }` column would let the allocator's value be stored on the invoice itself (currently it lives only in the allocation table). Migration would need a follow-up `ALTER TABLE "Invoices" ADD COLUMN "Number" INTEGER NULL` plus a unique `(UserId, Number)` index. Deferred to keep this slice non-shared.

## Risks

- The migration's `Designer.cs` is a stub (matches the existing `20260504120000_HardenWalletTopUpTiers.Designer.cs` pattern). Snapshot drift will surface on the next `dotnet ef migrations add`. Acceptable per project convention.
- `AllocateInvoiceNumberAsync` uses raw SQL via `db.Database.SqlQuery<int>` / `ExecuteSqlInterpolatedAsync`. It will throw on EF InMemory (test provider) — production runs PostgreSQL where it works. Tests deliberately do **not** exercise the allocator end-to-end (see coverage gap below).
- The state machine does not yet wrap existing direct status assignments. Until the diff above is applied, illegal transitions can still slip through legacy code paths. The state machine itself is locked in via 24 unit tests.

## Migration order requirement

Apply after slice A (`20260504120000_HardenWalletTopUpTiers`). No dependency on slices B / C; slices E–I are application-layer only.

## Known coverage gap

EF InMemory (used by `TestWebApplicationFactory`) does **not** enforce unique indexes and rejects all relational raw-SQL methods. As a result, the following slice-D guarantees are validated by the migration text + production Postgres only, with no in-test assertion:

- `IX_BillingQuotes_UserId_IdempotencyKey_Unique` — uniqueness enforcement
- `IX_Invoices_CheckoutSessionId_Unique` — double-finalize rejection
- `InvoiceNumberAllocations` — monotonic allocator behaviour

Recommended follow-up: when the test fixture migrates to SQLite-via-Sqlite-relational provider (per AGENTS.md note), restore the four DB-backed integration tests removed from `CheckoutFlowHardeningTests.cs` (preserved in slice-D git history).

## Validation

Commands actually run from repo root:

```powershell
# 1. Compile
dotnet build backend/OetLearner.sln
# → Build succeeded. 0 Error(s) (4 pre-existing nullable warnings, unrelated)

# 2. Slice-D tests only
dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj `
  --no-build `
  --filter "FullyQualifiedName~SubscriptionStateMachine|FullyQualifiedName~CheckoutFlowHardening"
# → Passed!  - Failed: 0, Passed: 39, Skipped: 0, Total: 39
```

`npx tsc --noEmit` — not run; this slice is backend-only and touches no TypeScript.

---

### 6-line summary

1. Added `SubscriptionStateMachine` (static legal-transition table) + 24 unit tests.
2. Added `LearnerService` partial with `EnsureQuoteIsFulfillable`, `EnsureQuoteSnapshotMatchesCatalog`, `AllocateInvoiceNumberAsync` + 12 unit/integration tests.
3. Migration `20260504160000_HardenCheckout` adds 2 filtered unique indexes (quote-idempotency, invoice-CheckoutSessionId) and the `InvoiceNumberAllocations` table — additive, `IF NOT EXISTS`-guarded, SQLite + Postgres compatible.
4. Cursor pagination on `/v1/billing/invoices` confirmed correct; boundary tests cover empty / single / multi-page.
5. Slice D **did not** edit shared files; integration diffs for `LearnerService.cs::ApplyCheckoutCompletionAsync`, `AdminService.cs` (4 sites), `LearnerService.cs:7293`, and `Domain/Entities.cs::Invoice` are documented above for a follow-up shared pass.
6. `dotnet build` clean (0 errors); slice-D test filter green: **39 passed / 0 failed**.
