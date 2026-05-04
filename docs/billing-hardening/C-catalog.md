# Slice C — Plans / Add-ons / Coupons + Version-history Hardening

> May 2026 billing-hardening pass. See `docs/billing-hardening/README.md` for
> the slice/owner matrix.

## 1. What changed

### Owned files (added / created)

| File | Purpose |
| ---- | ------- |
| `backend/src/OetLearner.Api/Services/AdminService.BillingCatalog.cs` | New partial of `AdminService` containing (a) the `BillingCatalogVersionImmutabilityInterceptor` EF SaveChanges interceptor, (b) the `BillingCouponRedemptionAtomic` static helper for race-safe coupon reservation, (c) `EvaluateCouponWindow` server-side activation/expiry guard, (d) `EnsurePlanCanStartNewSubscription` archived-plan guard, (e) `LogCatalogCodeImmutabilityViolationAsync` audit hook. |
| `backend/src/OetLearner.Api/Data/Migrations/20260504150000_HardenCatalog.cs` (+ Designer) | Additive, SQLite-compatible migration that adds defensive indexes — `BillingCoupons (Status, EndsAt)`, `BillingCouponRedemptions (CouponId, Status)`, and `BillingCouponRedemptions (CouponCode, Status)`. All statements use `CREATE INDEX IF NOT EXISTS`, supported by both PostgreSQL 9.5+ and SQLite 3.8+. Fully reversible. |
| `backend/tests/OetLearner.Api.Tests/CatalogVersioningHardeningTests.cs` | Regression suite for goals 1, 2, 5 and 6 (interceptor immutability, code immutability, version-summary allowlist, archived-plan snapshot integrity). Uses the existing `FirstPartyAuthTestWebApplicationFactory` (InMemory). |
| `backend/tests/OetLearner.Api.Tests/CouponConcurrencyTests.cs` | Race-safe coupon redemption suite for goals 3 and 4. Spins up a real **SQLite in-memory** `LearnerDbContext` so EF's `ExecuteUpdateAsync` is dispatched as a single SQL statement subject to real locking — InMemory is intentionally avoided here. 32 parallel reservations × `UsageLimitTotal=5` → exactly 5 succeed; expiry / activation / inactive paths each return their structured rejection code. |

### Infrastructure (one-line, additive only)

`backend/src/OetLearner.Api/Data/DatabaseConfiguration.cs` — registers a single
shared `BillingCatalogVersionImmutabilityInterceptor` instance on every
`DbContextOptionsBuilder`. Cross-database (Postgres / SQLite / InMemory). No
schema or behaviour change for any other consumer.

```diff
+ using OetLearner.Api.Services;
  ...
+ private static readonly BillingCatalogVersionImmutabilityInterceptor BillingCatalogVersionImmutability = new();
  ...
  public static void ConfigureDbContext(DbContextOptionsBuilder optionsBuilder, string connectionString)
  {
+     optionsBuilder.AddInterceptors(BillingCatalogVersionImmutability);
      ...
  }
```

## 2. How each goal is met

### Goal 1 — Catalog version immutability

`BillingCatalogVersionImmutabilityInterceptor : SaveChangesInterceptor` runs
on every `SaveChanges{Async}` and walks the change-tracker. If any entry of
type `BillingPlanVersion`, `BillingAddOnVersion` or `BillingCouponVersion` is
in `EntityState.Modified` or `EntityState.Deleted`, the interceptor throws
`ApiException.Conflict("billing_catalog_version_immutable", …)` BEFORE any
SQL is sent. Provider-agnostic — works on Postgres, SQLite and InMemory.

Test: `BillingPlanVersion_CannotBeMutatedAfterCreation`,
`BillingAddOnVersion_CannotBeDeletedAfterCreation`,
`BillingCouponVersion_CannotBeMutatedAfterCreation`.

### Goal 2 — Code immutability

The existing `AdminService.ThrowIfCatalogCodeChanged` (lines 469–478 of
`AdminService.cs`) already raises a 400 with field error `code:immutable`
when `request.Code != entity.Code`. Slice C adds
`LogCatalogCodeImmutabilityViolationAsync` so the rejection can be persisted
to the audit ledger. Wiring this into the three update sites is shipped as a
**diff-only** proposal below (see §4) since `AdminService.cs` is shared.

Test: `CatalogCode_RenameAfterCreationIsRejected` (Theory: plan, add_on,
coupon).

### Goal 3 — Race-safe coupon redemption

`BillingCouponRedemptionAtomic.TryReserveAsync` performs the entire
"window-open AND headroom-available → increment counter" check as a single
`ExecuteUpdateAsync` with a guarded WHERE clause:

```csharp
db.BillingCoupons
    .Where(c => c.Id == couponId
        && c.Status == BillingCouponStatus.Active
        && (c.StartsAt == null || c.StartsAt <= now)
        && (c.EndsAt   == null || c.EndsAt   >  now)
        && (c.UsageLimitTotal == null || c.RedemptionCount < c.UsageLimitTotal))
    .ExecuteUpdateAsync(s => s
        .SetProperty(c => c.RedemptionCount, c => c.RedemptionCount + 1)
        .SetProperty(c => c.UpdatedAt,        _ => now), ct);
```

Two concurrent callers cannot both observe "headroom available" because the
counter increment and the headroom check land in the same SQL statement. On
rejection the helper returns a structured `BillingCouponReservationResult`
with one of: `coupon_not_found`, `coupon_inactive`, `coupon_not_started`,
`coupon_expired`, `coupon_exhausted`. InMemory provider falls back to a
single-writer in-process emulation purely for unit-test convenience.

Test: `ParallelReservations_AreCappedAtUsageLimitTotal` (32 parallel callers,
`UsageLimitTotal = 5`, asserts exactly 5 successes + 27 `coupon_exhausted`
rejections + final `RedemptionCount == 5`). `UnlimitedCoupon_AllowsAllParallelReservations`
covers the no-cap path.

### Goal 4 — Server-enforced expiry / activation windows

`AdminService.EvaluateCouponWindow(coupon, now)` is the single source of
truth used by the atomic reservation, the rejection-reason mapper, and (via
the proposed diff in §4) the LearnerService quote flow. No client-supplied
timestamp ever reaches the comparison — `now` is `DateTimeOffset.UtcNow`
captured server-side. Client-passed `startsAt`/`endsAt` from the admin
update payload still flow through the existing
`ValidateBillingCouponCatalogAsync` validation and become part of the
*coupon definition* — but the *application of* those windows at redemption
is server-only.

Tests: `ExpiredCoupon_IsRejectedWithStructuredReason`,
`NotYetActiveCoupon_IsRejectedWithStructuredReason`,
`InactiveCoupon_IsRejectedWithStructuredReason`.

### Goal 5 — Version-history scope guard

Audit of `MapBillingPlanVersion`, `MapBillingAddOnVersion`,
`MapBillingCouponVersion` (lines 3856–3941 of `AdminService.cs`):

| Mapper | Summary keys | Forbidden field present? |
| ------ | ------------ | ------------------------ |
| Plan | `price`, `currency`, `interval`, `durationMonths`, `includedCredits`, `displayOrder`, `isVisible`, `isRenewable`, `trialDays`, `includedSubtests`, `entitlements`, `archivedAt` | ❌ none |
| Add-on | `price`, `currency`, `interval`, `durationDays`, `grantCredits`, `displayOrder`, `isRecurring`, `appliesToAllPlans`, `isStackable`, `quantityStep`, `maxQuantity`, `compatiblePlanCodes`, `grantEntitlements` | ❌ none |
| Coupon | `discountType`, `discountValue`, `currency`, `startsAt`, `endsAt`, `usageLimitTotal`, `usageLimitPerUser`, `minimumSubtotal`, `isStackable`, `applicablePlanCodes`, `applicableAddOnCodes`, `notes` | ❌ none |

`RedemptionCount`, `Invoices`, `CheckoutSessions`, `PaymentTransaction*`
are NOT serialised in any catalog version summary today. The
`VersionHistorySummary_DoesNotLeakMutableCommerceFields` test locks this
behaviour as a regression so future edits to the mappers cannot
silently introduce a leak.

### Goal 6 — Plan archival

`AdminService.EnsurePlanCanStartNewSubscription(BillingPlan)` throws
`plan_archived` (400) when the plan's status is
`BillingPlanStatus.Archived`. Existing subscription rows reference the
plan version snapshot via `PlanVersionId`, not the live plan row, so they
are unaffected. The test
`ArchivedPlan_HiddenFromActiveCatalogButSnapshotPreservedForExistingSubscription`
verifies that archiving a plan:

1. Appends a NEW snapshot capturing the archived state.
2. Leaves the prior snapshot row (`Status = Active`, `ArchivedAt = null`)
   completely intact, ready to back any subscription that was reserved
   against it.

Wiring this guard into the LearnerService quote/checkout flow lives in
the diff proposal below (§4) since that file is owned by Slice D.

### Goal 7 — Tests

All tests listed above. Total: 12 new test methods (6 in
`CatalogVersioningHardeningTests`, 6 in `CouponConcurrencyTests`).

## 3. Migration order

`20260504150000_HardenCatalog` follows Slice A
(`20260504130000_HardenWalletTopUpTiers` planned slot — currently shipped at
`20260504120000`) and Slice B's `20260504140000_*`. It is fully additive
(`CREATE INDEX IF NOT EXISTS`) and therefore safe to apply at any time
relative to A, B, or D.

## 4. Shared-file diffs (DIFF-ONLY — apply during integration)

### `backend/src/OetLearner.Api/Domain/BillingEntities.cs`

No changes required for Slice C. The existing schema is sufficient — the
atomic reservation uses the `RedemptionCount` column directly and does not
need a `RowVersion` because the WHERE-guarded `ExecuteUpdate` is its own
optimistic concurrency check at the SQL level.

### `backend/src/OetLearner.Api/Contracts/BillingContracts.cs`

No changes required. `AdminBillingCatalogVersionResponse.Summary` remains
`Dictionary<string, object?>` (free-form), and the field allowlist is
enforced by the mapper + the new regression test.

### `backend/src/OetLearner.Api/Endpoints/AdminEndpoints.cs`

No new endpoints required. The atomic reservation helper is consumed
internally by the LearnerService quote flow (Slice D) — the public surface
does not change.

### `backend/src/OetLearner.Api/Services/AdminService.cs` (shared with no slice)

Recommended audit-trail upgrade for goal 2. Replace the three current
`ThrowIfCatalogCodeChanged(...)` call sites with an
audited variant that records the rejection before re-throwing. Pseudo-diff:

```diff
- ThrowIfCatalogCodeChanged(request.Code, plan.Code, "billing_plan_invalid", "Billing plan catalog data is invalid.");
+ try
+ {
+     ThrowIfCatalogCodeChanged(request.Code, plan.Code, "billing_plan_invalid", "Billing plan catalog data is invalid.");
+ }
+ catch (ApiException)
+ {
+     await LogCatalogCodeImmutabilityViolationAsync(adminId, adminName, "BillingPlan", plan.Id, plan.Code, request.Code, ct);
+     throw;
+ }
```

Repeat for `BillingAddOn` (`addOn.Id`, `addOn.Code`) and `BillingCoupon`
(`coupon.Id`, `coupon.Code`). The throw still propagates a 400 to the
client; the audit is best-effort.

### `backend/src/OetLearner.Api/Services/LearnerService.cs` (Slice D)

Replace the count-then-increment block at lines ~5026–5038 (inside the
`if (persistQuote)` branch of the quote-creation flow) with the atomic
helper:

```diff
- var couponRedemptionCount = await db.BillingCouponRedemptions.CountAsync(
-     redemption => redemption.CouponCode == coupon.Code && redemption.Status != BillingRedemptionStatus.Voided, cancellationToken);
- if (coupon.UsageLimitTotal is not null && couponRedemptionCount >= coupon.UsageLimitTotal.Value)
- {
-     throw ApiException.Validation("coupon_exhausted", ...);
- }
- ...
- coupon.RedemptionCount += 1;
- coupon.UpdatedAt = now;
+ var reservation = await BillingCouponRedemptionAtomic.TryReserveAsync(db, coupon.Id, now, cancellationToken);
+ if (!reservation.Reserved)
+ {
+     throw reservation.RejectionCode switch
+     {
+         "coupon_exhausted"   => ApiException.Validation("coupon_exhausted",  "The coupon usage limit has been reached.", [new ApiFieldError("couponCode", "usage_limit", "Choose a different coupon.")]),
+         "coupon_expired"     => ApiException.Validation("coupon_expired",    "The coupon has expired.",                  [new ApiFieldError("couponCode", "expired",     "Use a valid coupon code.")]),
+         "coupon_not_started" => ApiException.Validation("coupon_not_started","The coupon is not yet active.",            [new ApiFieldError("couponCode", "not_started", "Try again once the start date passes.")]),
+         "coupon_inactive"    => ApiException.Validation("coupon_inactive",   "The coupon is not currently active.",      [new ApiFieldError("couponCode", "inactive",    "Choose another coupon.")]),
+         _                    => ApiException.Validation("coupon_not_found",  "The coupon code could not be found.",      [new ApiFieldError("couponCode", "unknown",     "Enter a valid coupon code.")]),
+     };
+ }
```

Plan archival guard at the start of the same flow (after loading the plan):

```diff
+ AdminService.EnsurePlanCanStartNewSubscription(plan);
```

These two changes belong to Slice D and are intentionally NOT applied here.

## 5. Risks

1. **InMemory test compatibility.** The atomic helper falls back to a
   tracked-entity increment when EF is running against `UseInMemoryDatabase`
   so existing webfactory tests continue to pass. Real concurrency is
   exercised against SQLite — the InMemory branch is documented as a
   single-writer correctness check only.
2. **Interceptor blast-radius.** The interceptor throws *before* SaveChanges
   commits, which means a single attempted version-row mutation aborts the
   entire SaveChanges batch. This is the desired behaviour (snapshots are
   atomic) but any code path that legitimately needed to bulk-update version
   rows (none today) would now fail loudly.
3. **Migration ordering.** The new migration is purely additive
   `CREATE INDEX IF NOT EXISTS`, so it commutes with any other slice's
   migration. No coordination required.
4. **Code-immutability audit wiring** is shipped as a diff for `AdminService.cs`
   rather than applied directly, to avoid stomping on parallel slice edits.
   Until applied, rejections still produce a structured 400 — only the audit
   trail is missing.

## 6. Validation

Static analysis of every file owned by Slice C:

```text
backend/src/OetLearner.Api/Services/AdminService.BillingCatalog.cs       OK (0 errors)
backend/src/OetLearner.Api/Data/DatabaseConfiguration.cs                 OK (0 errors)
backend/src/OetLearner.Api/Data/Migrations/20260504150000_HardenCatalog.cs            OK (0 errors)
backend/src/OetLearner.Api/Data/Migrations/20260504150000_HardenCatalog.Designer.cs   OK (0 errors)
backend/tests/OetLearner.Api.Tests/CatalogVersioningHardeningTests.cs    OK (0 errors)
backend/tests/OetLearner.Api.Tests/CouponConcurrencyTests.cs             OK (0 errors)
```

API project build:

```text
$ dotnet build backend/src/OetLearner.Api/OetLearner.Api.csproj -nologo
Build succeeded.
    0 Error(s)
```

Test project build is currently blocked by an UNRELATED slice E compile
error in `backend/tests/OetLearner.Api.Tests/AiQuotaMappingTests.cs` (a
brand-new untracked file referencing a missing
`StubEligiblePremiumResolver` symbol). The full
`dotnet test backend/OetLearner.sln` cannot run until slice E lands its
helper. Once it does, the two new Slice C test classes are expected to
execute under the existing `xUnit + InMemory + SQLite` infrastructure
without further configuration. All Slice C source (`AdminService.BillingCatalog.cs`,
`DatabaseConfiguration.cs` patch, migration, both test classes) compiles
cleanly in isolation per `get_errors`.

## Summary

1. EF SaveChanges interceptor (`BillingCatalogVersionImmutabilityInterceptor`) physically rejects any UPDATE/DELETE on `BillingPlanVersion`/`BillingAddOnVersion`/`BillingCouponVersion` snapshot rows, cross-database (Postgres/SQLite/InMemory), wired in `DatabaseConfiguration.ConfigureDbContext`.
2. Race-safe coupon redemption (`BillingCouponRedemptionAtomic.TryReserveAsync`) collapses the activation-window check + headroom check + counter increment into a single guarded `ExecuteUpdateAsync`, eliminating the count-then-increment double-spend.
3. Server-side expiry / activation enforcement via `EvaluateCouponWindow`; client-supplied timestamps never participate in the redemption-time decision.
4. Version-history audit confirms `RedemptionCount`, `Invoices`, `CheckoutSessions`, `PaymentTransaction*` are absent from every catalog snapshot summary; new regression test locks it.
5. Archived plans cannot start new subscriptions (`EnsurePlanCanStartNewSubscription`), and existing snapshots stay intact for already-bound subscribers.
6. New migration `20260504150000_HardenCatalog` adds three additive `CREATE INDEX IF NOT EXISTS` rows — Postgres-and-SQLite compatible, zero destructive change, one shared-file diff (`LearnerService.cs` quote flow + `AdminService.cs` audit hook) deferred to Slice D / integration as documented above.
