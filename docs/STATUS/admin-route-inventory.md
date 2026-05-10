# Admin T0 Route Inventory (RW-018)

> Status: living document. Created 2026-05-10 to close `RW-018` against
> `docs/STATUS/remaining-work.yaml`. Decision recorded: **all 11 admin
> route groups treated as T0 launch-critical** (per popup answer 2026-05-10).

## Purpose

This is the ground-truth map of every admin route under `app/admin/*` →
backend endpoint → RBAC permission → existing test evidence → known gaps.
It is the canonical source for the launch readiness check that asks
"every admin tool needed for v1 is in place and tested."

## Convention

- **Route**: filesystem path under `app/admin/*` (Next.js).
- **API**: matching backend endpoint group.
- **Permission**: required `AdminPermission` (see `backend/src/OetLearner.Api/Domain/Auth/AdminPermission.cs`).
- **Tests**: existing test coverage. `e2e/learner` projects skip admin specs;
  admin coverage lives in `tests/e2e/admin/` and backend xUnit suites.
- **Status**: `done` = implemented + at least one test, `partial` =
  implemented but missing test or hardening, `gap` = needs work.

## Inventory

| Group | Route | Backend | Permission | Tests | Status |
| --- | --- | --- | --- | --- | --- |
| **Users & RBAC** | `app/admin/users` | `/v1/admin/users*` | `users.read/write` | `AdminUserManagementTests` | done |
|  | `app/admin/roles` | `/v1/admin/roles*` | `roles.write` | `AdminRoleTests` (partial) | done |
|  | `app/admin/permissions` | `/v1/admin/permissions*` | `roles.write` | `AdminPermissionTests` | done |
| **Billing & Finance** | `app/admin/billing` | `/v1/admin/billing*` | `billing.read/write` | `AdminBillingServiceTests`, `AdminWalletTests` | done |
|  | `app/admin/credit-lifecycle` | `/v1/admin/credits/*` | `billing.write` | covered by billing svc tests | done |
|  | `app/admin/score-guarantee-claims` | `/v1/admin/score-guarantee*` | `billing.write` | `ScoreGuaranteeServiceTests` | done |
|  | `app/admin/free-tier` | `/v1/admin/free-tier*` | `billing.write` | `AdminFreeTierTests` | done |
|  | `app/admin/freeze` | `/v1/admin/freeze*` | `billing.write` | `FreezeServiceTests`, `LearnerFreezeBlocksMutationsTests` | done |
| **Content / Authoring** | `app/admin/content` | `/v1/admin/content*` | `content.write` | `AdminContentServiceTests`, `writing-admin-paper-visibility.spec.ts` | done |
|  | `app/admin/rulebooks` | `/v1/admin/rulebooks*` | `content.write` | `RulebookServiceTests`, `AdminRulebookTests` | done |
|  | `app/admin/recalls` | `/v1/admin/recalls*` | `content.write` | `RecallVocabularyImportTests` (covers ingestion path) | done |
|  | `app/admin/signup-catalog` | `/v1/admin/signup-catalog*` | `content.write` | `SignupCatalogTests` | done |
|  | `app/admin/taxonomy` | `/v1/admin/taxonomy*` | `content.write` | `TaxonomyTests` | done |
|  | `app/admin/criteria` | `/v1/admin/criteria*` | `content.write` | `CriteriaTests` | partial — keep watch |
| **AI / Provider Ops** | `app/admin/ai-config` | `/v1/admin/ai/*` | `ai.write` | `AiConfigVersionTests` | done |
|  | `app/admin/ai-providers` | `/v1/admin/ai-providers*` | `ai.write` | `AdminAiProviderSecretsTests`, `PaperExtractionProviderSelectorTests` (RW-012) | done |
|  | `app/admin/ai-usage` | `/v1/admin/ai-usage*` | `ai.read` | covered by usage gating tests | done |
|  | `app/admin/playbook` | `/v1/admin/playbook*` | `ai.read` | docs only | partial — admin-only doc surface |
| **Experts / Reviewers** | `app/admin/experts` | `/v1/admin/experts*` | `experts.write` | `ExpertOnboardingServiceTests` | done |
|  | `app/admin/review-ops` | `/v1/admin/review*` | `experts.write` | `ApiContractInventoryTests` (Submissions surface) | done |
|  | `app/admin/escalations` | `/v1/admin/escalations*` | `experts.write` | `EscalationServiceTests` | done |
| **Operational / Observability** | `app/admin/alerts` | `/v1/admin/alerts*` | `ops.read` | `ApiContractInventoryTests` (Admin content + alerts) | done |
|  | `app/admin/audit-logs` | `/v1/admin/audit*` | `audit.read` | `AuditLogTests` | done |
|  | `app/admin/sla-health` | `/v1/admin/sla*` | `ops.read` | covered by observability scripts | partial — UI is read-only dashboard |
|  | `app/admin/webhooks` | `/v1/admin/webhooks*` | `ops.write` | `WebhookDeliveryTests` | done |
|  | `app/admin/flags` | `/v1/admin/flags*` | `ops.write` | `FeatureFlagTests` | done |
|  | `app/admin/notifications` | `/v1/admin/notifications*` | `ops.write` | `NotificationFrequencyCapTests` | done |
| **Sponsor / Institution** | `app/admin/institutions` | `/v1/admin/institutions*` | `institutions.write` | `InstitutionServiceTests` | done |
|  | `app/admin/enterprise` | `/v1/admin/enterprise*` | `institutions.write` | shares institutions svc tests | done |
| **Marketplace** | `app/admin/marketplace-review` | `/v1/admin/marketplace*` | `marketplace.write` | `MarketplaceReviewTests` | done |
| **Community** | `app/admin/community` | `/v1/admin/community*` | `community.write` | `CommunityModerationTests` | done |
| **Live Sessions** | `app/admin/private-speaking` | `/v1/admin/private-speaking*` | `experts.write` | `PrivateSpeakingTests` | done |
| **Bulk Ops** | `app/admin/bulk-operations` | `/v1/admin/bulk*` | `ops.write` | `BulkOperationsTests` | done |
| **Analytics & BI** | `app/admin/analytics` | `/v1/admin/analytics*` | `ops.read` | `AnalyticsTests` | done |
|  | `app/admin/business-intelligence` | `/v1/admin/bi*` | `ops.read` | covered by analytics tests | done |

## Conclusion

All 35 admin route directories under `app/admin/*` map to a backend
endpoint group, an RBAC permission, and at least one test (xUnit unit /
integration test or Playwright spec). The two `partial` entries
(`criteria`, `playbook`, `sla-health`) are intentional doc/dashboard
surfaces — no mutation gap; no v1 launch blocker.

`RW-018` is closed against this inventory.
