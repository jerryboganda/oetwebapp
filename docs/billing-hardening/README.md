# Billing Hardening — Parallel Subagent Workspace

This folder is the coordination surface for the May 2026 billing hardening pass.

## File-ownership matrix (strict)

Each subagent owns the files listed under its slice and may edit them freely.
Any required change to a SHARED file MUST be written here as a unified diff
rather than applied directly, to avoid parallel-write conflicts.

| Slice | Owner files (free to edit) | Shared files (diff-only) |
| ----- | -------------------------- | ------------------------ |
| A — Wallet | `backend/src/OetLearner.Api/Services/WalletService.cs`, `AdminWalletTierService.cs`, `Domain/WalletTopUpTierConfig.cs`, new migration `*_HardenWallet*.cs` | `Domain/BillingEntities.cs`, `Contracts/BillingContracts.cs`, `Endpoints/LearnerEndpoints.cs`, `Endpoints/AdminEndpoints.cs` |
| B — Payments / Webhooks / Refunds | `backend/src/OetLearner.Api/Services/PaymentGatewayService.cs`, new files `Services/Billing/RefundService.cs`, `Services/Billing/DisputeService.cs`, new migration `*_AddRefundDispute*.cs` | same shared set as A |
| C — Catalog (Plans / Add-ons / Coupons) version history | `backend/src/OetLearner.Api/Services/AdminService.Billing.*.cs` (if exists; otherwise create), new migration `*_HardenCatalog*.cs` | same shared set |
| D — Checkout / Quote / Subscription / Invoice flow | `backend/src/OetLearner.Api/Services/LearnerService.Billing.cs` (split out via partial if needed), new migration `*_HardenCheckout*.cs` | same shared set |
| E — Entitlement resolver + AI quota mapping | `backend/src/OetLearner.Api/Services/AiQuotaService.cs`, `Services/EffectiveEntitlementResolver.cs` (or its current host), `lib/billing-entitlements.ts` (if exists) | same shared set |
| F — Frontend learner billing | `app/billing/**`, `lib/billing-types.ts`, `components/domain/billing/**` | none (frontend is independent) |
| G — Frontend admin billing | `app/admin/billing/**`, `components/admin/billing/**` | none |
| H — Tests | `backend/tests/OetLearner.Api.Tests/**Billing*.cs` (NEW files only — do NOT edit existing tests except to extend assertions), `app/billing/__tests__/**`, `app/admin/billing/__tests__/**` (NEW files only), `tests/e2e/billing.spec.ts` (NEW) | none |
| I — Docs, observability, runbook | `docs/BILLING.md` (create), `docs/runbooks/billing-incident.md` (create), `instrumentation.ts` augmentations | none |

## Coordination rules

1. Each slice writes a final report to `docs/billing-hardening/<slice>.md`
   summarizing: changes applied, proposed diffs for shared files, risks,
   migration order requirements, and validation evidence.
2. Migrations must use timestamps **after `20260504120000`** so they apply
   in deterministic order. Use timestamps spaced by slice letter:
   A=`20260504130000`, B=`20260504140000`, C=`20260504150000`, D=`20260504160000`.
3. NEVER modify production secrets, .env files, deployment scripts, or
   `docker-compose.production*.yml`.
4. NEVER drop columns, drop tables, or write destructive migrations
   without an `IF EXISTS` guard. Prefer additive schema changes.
5. Always go through `IFileStorage`, `IAiGatewayService`, `OetScoring` — never
   bypass the canonical wrappers.
6. End every slice report with a **Validation** section listing the exact
   commands run (`dotnet build`, `npx tsc --noEmit`, focused tests) and
   their results.
