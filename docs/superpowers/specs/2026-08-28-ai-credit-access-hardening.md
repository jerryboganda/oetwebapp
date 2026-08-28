---
title: 'Subscription-scoped AI credit and admin access hardening'
type: 'feature'
created: '2026-08-28'
status: 'in-progress'
baseline_commit: '8ca409c0ae8aef65b0b5c51285bcfcb6b0720c99'
context:
  - '{project-root}/AGENTS.md'
  - '{project-root}/docs/OET_2026_MASTER_CATALOGUE_AI_CREDITS_ACCESS.md'
  - '{project-root}/docs/OET_2026_Product_Portfolio_Claude_Code_Codex.md'
---

<frozen-after-approval reason="human-owned intent - do not modify unless human renegotiates">

## Intent

**Problem:** Per-user AI credits and package access are not consistently owned by a specific subscription. Reversal can affect a different purchase with the same package code, refunds can leave the new credit lots active, future or expired dates can be ignored, and the admin profile cannot reliably edit or display the resulting state.

**Approach:** Make every grant and reversal subscription/source-scoped, enforce one shared access-window rule across entitlements and credit consumption, synchronize linked unused lots when package dates change, and expose the authoritative result through the existing admin APIs and profile controls.

## Boundaries & Constraints

**Always:** Preserve used-credit history; reverse only unused balance; make grants, gifts, refunds, and admin mutations idempotent; use `StartedAt <= now` and `ExpiresAt == null || now < ExpiresAt`; keep subtest-specific, Flexible W/S, Shared, and Mock balances separate; allow explicit expiry clearing; keep candidate/admin balances sourced from the same ledger; retain all unrelated dirty Listening changes.

**Ask First:** None. A package date extension makes linked unused lots valid under the new window; a revoked/removal operation remains terminal and does not silently revive historical grants.

**Never:** Do not identify a subscription only by package/product code; do not restore consumed credits; do not equate candidate credits with provider tokens; do not add client-only authorization; do not rewrite unrelated Listening files or use destructive Git commands.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Same package twice | Two active subscriptions share one package code; reverse one source | Only that subscription's unused lots and entitlement are reversed | Missing/ambiguous source is rejected, never guessed |
| Refund | Paid package/add-on refund with unused and used balance | New-ledger unused balance is reversed; used transaction history remains | Idempotent repeat produces no second reversal |
| Future start | Package starts after current time | No access and no credit consumption | Protected start request is denied |
| Explicit clear | Admin sends `expiresAt: null`/clear intent | Package and linked eligible lots become non-expiring | Invalid end-before-start returns field validation |
| Exact zero set | Admin sets every bucket to zero | All selected buckets are written as zero | Omitted fields retain their existing values |

</frozen-after-approval>

## Code Map

- `backend/src/OetLearner.Api/Domain/AiPackageCreditEntities.cs` -- credit accounts, lots, transactions, and grant source/validity.
- `backend/src/OetLearner.Api/Services/Billing/AiPackageCreditService.cs` -- grant, consume, summarize, and reverse ledger operations.
- `backend/src/OetLearner.Api/Services/Billing/UserAccessAllocationService.cs` -- admin grants, package dates, and bucket adjustments.
- `backend/src/OetLearner.Api/Services/Billing/AddonGrantProcessor.cs` -- paid/admin add-on grant and reversal paths.
- `backend/src/OetLearner.Api/Services/Billing/RefundService.cs` and `Services/LearnerService.cs` -- refund and confirmed checkout integration.
- `backend/src/OetLearner.Api/Services/Entitlements/EffectiveEntitlementResolver.cs` -- server-side active-window enforcement.
- `backend/src/OetLearner.Api/Contracts/AdminRequests.cs`, `Endpoints/AdminEndpoints.cs` -- admin mutation contracts and routes.
- `lib/api/user-access-packages.ts`, `lib/user-access.ts`, `components/admin/user-access/*`, `app/admin/users/[id]/page.tsx` -- typed admin profile API and controls.
- `backend/src/OetLearner.Api/Data/LearnerDbContext.cs` and `Data/Migrations/` -- persistence changes if required.

## Tasks & Acceptance

**Execution:**
- [ ] `AiPackageCreditEntities.cs`, `AiPackageCreditService.cs`, and `LearnerDbContext.cs` -- persist and use an unambiguous subscription/source reference plus valid-from/expiry metadata -- prevent cross-subscription reversal and expired/future consumption.
- [ ] `UserAccessAllocationService.cs`, `AddonGrantProcessor.cs`, `RefundService.cs`, and `LearnerService.cs` -- pass source identity through gifts, add-ons, refunds, and admin operations -- keep all lifecycle paths on the authoritative ledger and honor requested quantities.
- [ ] `EffectiveEntitlementResolver.cs`, `AdminRequests.cs`, and `AdminEndpoints.cs` -- apply the shared date-window rule and explicit null semantics -- prevent future/expired access and support safe date edits.
- [ ] `lib/api/user-access-packages.ts`, `lib/user-access.ts`, `components/admin/user-access/package-list.tsx`, `manage-access-panel.tsx`, `credit-bucket-adjuster.tsx`, `ai-credit-summary.tsx`, and `app/admin/users/[id]/page.tsx` -- add saved package date editing, immediate refresh, exact-zero setting, and grant-level validity -- keep the admin surface dense and accessible.
- [ ] Relevant backend and frontend test files -- cover source isolation, refunds, idempotency, quantities, date boundaries, explicit clearing, exact zero, and displayed validity -- prevent regressions.

**Acceptance Criteria:**
- Given two subscriptions with the same package code, when one is removed or refunded, then only its unused grants are reversed and the other remains usable.
- Given a refund or duplicate webhook, when the lifecycle event is processed repeatedly, then the new ledger is reversed or granted exactly once.
- Given a package outside its active date window, when a protected entitlement or credit start is requested, then the request is denied without consuming balance.
- Given an admin date edit, when either date is changed or expiry is explicitly cleared, then the package and its linked unused lots expose the same effective window without changing unrelated grants.
- Given an admin exact-set request containing zero for every bucket, when it is saved, then all selected balances become zero and the UI refreshes from the server.
- Given multiple grants feed one balance, when the admin profile loads, then source, total, used, remaining, valid-from, expiry, and days left are visible without provider-token data.

## Spec Change Log

## Verification

**Commands:**
- `pnpm test -- components/admin/user-access/package-list.test.tsx components/admin/user-access/manage-access-panel.test.tsx components/admin/user-access/credit-bucket-adjuster.test.tsx components/admin/user-access/ai-credit-summary.test.tsx` -- expected: focused admin tests pass.
- `pnpm run backend:test` -- expected: backend credit, allocation, entitlement, and refund tests pass.
- `pnpm run ship:gate` -- expected: repository ship gate completes without new errors.
