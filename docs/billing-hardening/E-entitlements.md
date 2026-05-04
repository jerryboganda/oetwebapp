# SLICE E — Effective Entitlement Resolver + AI Quota Mapping Hardening

> Owner: SLICE E
> Status: complete (build + focused tests green)

## Summary

Hardened the entitlement and AI-quota mapping pipeline so that **any
ambiguity collapses to FREE tier** instead of silently elevating users.
Pinned the renewal-worker exactly-once contract with a parallel-execution
test backed by SQLite (which honours the unique filtered index).

## Changes applied

### Production code

- `backend/src/OetLearner.Api/Services/Entitlements/EffectiveEntitlementResolver.cs`
  - Added structured fail-low semantics. The resolver now demotes an
    otherwise-Active/Trial subscription to `Tier="free"` /
    `HasEligibleSubscription=false` when:
    1. `Subscription.PlanId` is empty (`fail_low.plan.id_missing`).
    2. The referenced `BillingPlan` row no longer exists
       (`fail_low.plan.missing`).
    3. `Subscription.PlanVersionId` references a `BillingPlanVersion`
       row that is missing (`fail_low.plan.version.missing`).
    4. The resolved `BillingPlan.EntitlementsJson` is malformed
       (non-parseable JSON or not a JSON object —
       `fail_low.entitlements.malformed`).
  - Added `ILogger<EffectiveEntitlementResolver>` (optional, DI-resolved)
    and emits a `LogWarning` with `userId`, `subscriptionId`,
    `subscription.Status`, `planId`, `planVersionId`, and `reason` for
    every fail-low. This is the structured error event required by the
    slice goals.
  - Constructor signature change is backwards compatible — existing
    `new EffectiveEntitlementResolver(db)` callsites continue to compile.
  - The snapshot still surfaces the underlying `SubscriptionStatus` for
    diagnostics; it just NEVER lets that status confer paid entitlements.

- `backend/src/OetLearner.Api/Services/AiManagement/AiQuotaService.cs`
  - No code change needed — the existing `ResolvePlanAsync` already
    treats `explicit-invalid` and `unknown` quotaPlanCodes as fall-back
    to FREE, and now inherits the resolver's stricter fail-low
    propagation transitively (an ineligible subscription returns the
    FREE plan via `ResolveDefaultPlanAsync`).
  - Verified by `AiQuotaMappingTests` (see below).

- `backend/src/OetLearner.Api/Services/AiManagement/AiCreditRenewalWorker.cs`
  - No code change needed — the worker already uses the
    `UX_AiCreditLedger_PlanRenewal_ReferenceId` unique filtered index
    plus `IsUniqueConstraintViolation` swallowing for the race. New
    test pins this contract under 10× parallel execution.

### Tests added

- `backend/tests/OetLearner.Api.Tests/EntitlementResolverHardeningTests.cs`
  (NEW — 12 tests, all passing)
  - `NonEligibleStatuses_AlwaysResolveToFreeTier` (theory ×5):
    Suspended / PastDue / Pending / Cancelled / Expired all collapse to
    free tier.
  - `DeletedPlan_OnActiveSubscription_FailsLowToFree`.
  - `MissingPlanVersionRow_FailsLowToFree`.
  - `ValidPlanVersionRow_PreservesEligibility` (anti-regression).
  - `MalformedEntitlementsJson_FailsLowToFree`.
  - `EntitlementsJsonIsArray_TreatedAsMalformed`.
  - `DowngradeMidCycle_NewActionsResolveLowerTier_NoCachingElevation` —
    pins the in-flight idempotency invariant: a snapshot captured before
    the downgrade preserves the higher tier (so an already-running AI
    evaluation completes), but the next `ResolveAsync` reflects the
    downgrade immediately. The resolver does **not cache**, which the
    test documents as the safe default.
  - `NoUserSubscription_ReturnsAnonymousFree`.

- `backend/tests/OetLearner.Api.Tests/AiQuotaMappingTests.cs` (NEW — 6
  tests, all passing)
  - `UnknownExplicitQuotaPlanCode_FallsBackToFree_DoesNotElevate`.
  - `MalformedEntitlementsJson_OnActivePlan_ResolvesToFreePolicy`.
  - `NonEligibleSubscription_ResolvesToFreePolicy` (theory ×3:
    Suspended / PastDue / Cancelled).
  - `ParallelRenewal_GrantsExactlyOnce_PerUserPeriod` —
    fans out 10 parallel `RunOnceAsync` calls against a shared in-memory
    SQLite database (so the unique filtered index
    `UX_AiCreditLedger_PlanRenewal_ReferenceId` is enforced). Asserts:
    1. Exactly **one** `AiCreditLedger` row with `Source=PlanRenewal`
       exists for `(user, period)`.
    2. The reported `renewed` count summed across all 10 workers is
       exactly **1** — the other 9 either skipped on the existence
       check or caught the unique-violation and treated it as a no-op.
    3. The grant value is the full plan token cap (idempotency does not
       cause a half-grant).
  - Uses a test-only `StubEligiblePremiumResolver` to bypass an
    unrelated SQLite query-translation limitation in the production
    resolver (the `EndsAt > now` disjunction in
    `ResolveActiveAddOnCodesAsync` does not translate on the SQLite
    provider). The contract under test is the unique-index protection
    in the worker, not the resolver internals — and resolver internals
    are exercised separately under EF InMemory by
    `EntitlementResolverHardeningTests`.

### Test edits (existing files — minimal)

These pre-existing tests embedded the *old* lax behavior. Updated only
their seed data / assertions to match the slice E security tightening,
without changing what each test was conceptually verifying:

- `backend/tests/OetLearner.Api.Tests/GrammarServiceTests.cs`:
  `Entitlement_PaidSubscriberUnlimited` — added the missing
  `BillingPlan { Id = "pro" }` seed row. The previous assertion relied
  on the resolver returning `Tier="paid"` even when no plan row existed
  (which is exactly the loophole slice E closes).
- `backend/tests/OetLearner.Api.Tests/CompactEntitlementGateTests.cs`:
  `ContentEntitlement_MissingPlanVersionAnchorFailsClosed` and
  `ContentEntitlement_MissingLivePlanWithoutSnapshotFailsClosed` —
  widened the reason assertion to accept either `plan_does_not_grant`
  (old) or `no_active_subscription` (new). Both reasons are
  fail-closed; the new reason is strictly stronger because the user is
  now demoted upstream rather than allowed past the resolver only to be
  blocked at the content gate.

## Goal-by-goal coverage

| Goal | Status | Evidence |
| ---- | ------ | -------- |
| 1. Fail-low on ambiguity (missing version, deleted plan, malformed JSON, unknown quotaPlanCode) | DONE | Resolver edits + structured `LogWarning`. 7 dedicated tests in `EntitlementResolverHardeningTests` + `AiQuotaMappingTests`. |
| 2. Suspended / PastDue must not confer entitlements | DONE | Theory test `NonEligibleStatuses_AlwaysResolveToFreeTier` covers Suspended, PastDue, Pending, Cancelled, Expired. Pre-existing eligibility gate (`Status is Active or Trial`) is now also covered by hardening tests. |
| 3. Cache safety | DONE (no-cache by design) | Resolver does **not** cache per-user state; every `ResolveAsync` re-queries the DB. `DowngradeMidCycle_NewActionsResolveLowerTier_NoCachingElevation` pins this invariant. The 15-second `AiGlobalPolicyCacheTtl` in `AiQuotaService` is platform-wide and unrelated to user-scoped entitlements. |
| 4. AI quota race — exactly-once renewal | DONE | `ParallelRenewal_GrantsExactlyOnce_PerUserPeriod` runs 10× in parallel against SQLite (which enforces `UX_AiCreditLedger_PlanRenewal_ReferenceId`). Exactly one ledger row emerges. |
| 5. Downgrade mid-cycle: in-flight ops keep snapshot, new ops drop tier | DONE | `DowngradeMidCycle_NewActionsResolveLowerTier_NoCachingElevation`. The contract relies on the resolver returning a value-object snapshot (`record EffectiveEntitlementSnapshot`) rather than a mutable handle, so any captured snapshot is naturally idempotent for the duration of the in-flight call. |
| 6. Tests: malformed→free, suspended→free, dispute (=PastDue/Suspended status proxy)→free, renewal race→exactly-once, cache invalidation, downgrade | DONE | Covered by the two new test files (18 tests total) plus theory matrices. Note: there is no first-class `Disputed` `SubscriptionStatus` in `Entities.cs`; the dispute path is modelled at the `PaymentTransaction.Status` level (slice B's domain). The eligibility gate already excludes any non-Active/Trial status, which transitively covers the dispute case once a payment dispute flips the subscription out of Active. Tracked under "Risks" below. |

## Shared-file diff requests

> No edits applied to shared files. The required slice-A/B/C/D shared
> entities (`BillingEntities.cs`, `BillingContracts.cs`) **did not need
> to be modified** to land slice E. The only entity field this slice
> reads (`Subscription.PlanVersionId`) already exists; no new contract
> properties are introduced.

If the future audit-event work surfaces the need for a dedicated
`EntitlementResolutionFailLowEvent` row in `BillingEntities.cs`, that
would be the first additive request — but it is intentionally not
scoped here to keep the slice surgical.

## Risks / Follow-ups

1. **`Disputed` subscription status** — there is currently no first-class
   `SubscriptionStatus.Disputed`. The dispute path lives on
   `PaymentTransaction.Status = "disputed"`. Slice B (Refund/Dispute)
   should add a `Subscription` state transition (`Active → Suspended`
   when a dispute opens) so the resolver's existing eligibility gate
   covers it. Until that lands, a disputed payment that hasn't yet
   flipped the subscription state could leave the user on Active.
2. **`EffectiveEntitlementResolver.ResolveActiveAddOnCodesAsync`** uses
   a `(EndsAt == null || EndsAt > now)` disjunction that does **not**
   translate on the SQLite provider (encountered while writing the
   renewal-race test). It works on Postgres in production. Consider
   rewriting as `EndsAt == null ? true : EndsAt > now` projection or
   client-evaluated to make the resolver SQLite-portable — relevant for
   any future desktop standalone-runtime test that exercises addons.
   Out of scope for slice E.
3. **No per-user entitlement cache** is the deliberate safe default. If
   future perf work introduces one, the cache key MUST include
   `(UserId, Subscription.Id, Subscription.ChangedAt, PlanVersionId)`
   and MUST be invalidated on any subscription state-change webhook.
4. **`lib/billing-entitlements.ts`** TS mirror was marked OPTIONAL in
   the slice spec and was not created — frontend slice F owns
   `lib/billing-types.ts` and is the right place for any TS-side
   entitlement projection.

## Validation

Commands run from repo root unless noted. Backend pwd:
`C:\Users\Dr Faisal Maqsood PC\Desktop\New OET Web App\backend`.

| Command | Result |
| ------- | ------ |
| `dotnet build OetLearner.sln` | **succeeded** — 0 errors, 4 pre-existing warnings (unchanged). |
| `dotnet test … --filter "FullyQualifiedName~EntitlementResolverHardeningTests"` | **12 / 12 passed**. |
| `dotnet test … --filter "FullyQualifiedName~AiQuotaMappingTests"` | **6 / 6 passed** (incl. parallel renewal exactly-once). |
| `dotnet test … --filter "FullyQualifiedName~CompactEntitlementGateTests \| FullyQualifiedName~GrammarServiceTests \| FullyQualifiedName~Entitlement \| FullyQualifiedName~AiQuota \| FullyQualifiedName~AiCredit"` | **95 / 95 passed** — full slice-adjacent regression. |

## 6-line summary

1. Hardened `EffectiveEntitlementResolver` to fail-low on missing plans, missing plan-version rows, malformed entitlements JSON, and ineligible subscription statuses.
2. Added structured `LogWarning` on every demotion so SREs can spot catalog drift.
3. Verified `AiQuotaService` correctly inherits the new fail-low posture (unknown / malformed quota mappings now fall back to FREE).
4. Pinned the renewal-worker exactly-once invariant under 10× parallel execution against a shared SQLite database that enforces `UX_AiCreditLedger_PlanRenewal_ReferenceId`.
5. Documented and pinned the no-cache contract: the resolver re-queries on every call so downgrade-mid-cycle takes effect immediately for new ops while in-flight snapshots stay idempotent.
6. Two new test files (18 tests) plus three minimal compatibility edits to existing tests; full slice-adjacent regression of 95 tests is green.
