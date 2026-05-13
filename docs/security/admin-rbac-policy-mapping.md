# Admin RBAC Policy Mapping

Review date: 2026-05-09

Scope: granular admin authorization policies registered in `backend/src/OetLearner.Api/Program.cs` and applied through endpoint `RequireAuthorization(...)`, `WithAdminRead(...)`, and `WithAdminWrite(...)` calls.

## Policy Model

- Group-level admin endpoints still require authenticated admin role plus verified email through `AdminOnly` where route groups opt into that policy.
- Endpoint-level policies use the `admin_permissions` claim through `HasAdminPermission(...)`.
- `system_admin` is the break-glass permission accepted by every granular admin policy.
- `WithAdminWrite(...)` applies `PerUserWrite` plus the granular policy. `WithAdminRead(...)` applies the granular policy on top of group read limits.

## Registered Policies

- `AdminContentRead`: requires `content:read` or `system_admin`.
- `AdminContentWrite`: requires `content:write` or `system_admin`.
- `AdminContentPublish`: requires `content:publish` or `system_admin`.
- `AdminContentEditorReview`: requires `content:editor_review`, `content:publish`, or `system_admin`.
- `AdminContentPublisherApproval`: requires `content:publisher_approval`, `content:publish`, or `system_admin`.
- `AdminContentPublishRequestsRead`: requires `content:editor_review`, `content:publisher_approval`, `content:publish`, or `system_admin`.
- `AdminBillingRead`: requires `billing:read` or `system_admin`.
- `AdminBillingWrite`: requires `billing:write` or `system_admin`.
- `AdminBillingRefundWrite`: requires `billing:refund_write`, `billing:write`, or `system_admin`.
- `AdminBillingCatalogWrite`: requires `billing:catalog_write`, `billing:write`, or `system_admin`.
- `AdminBillingSubscriptionWrite`: requires `billing:subscription_write`, `billing:write`, or `system_admin`.
- `AdminFreezeRead`: requires `billing:read` or `system_admin`.
- `AdminFreezeWrite`: requires `billing:write` or `system_admin`.
- `AdminUsersRead`: requires `users:read` or `system_admin`.
- `AdminUsersWrite`: requires `users:write` or `system_admin`.
- `AdminReviewOps`: requires `review_ops` or `system_admin`.
- `AdminAiConfig`: requires `ai_config` or `system_admin`.
- `AdminSystemAdmin`: requires `system_admin`.

## Representative Endpoint Coverage

### Content Operations

- Read examples: rulebook admin reads, content staleness reads, reading/listening analytics reads.
- Write examples: content create/update/delete, rulebook edits, reading/listening authoring mutations, chunked upload and ZIP import.
- Publish examples: rulebook publish/retire and grammar lesson publish/unpublish.
- Existing tests: `AdminFlowsTests` covers admin content list/create/publish and publish workflow permission denial paths.
- Remaining launch evidence: add a generated endpoint-policy matrix test for all content admin endpoint groups.

### User Operations

- Read examples: `GET /v1/admin/users`, `GET /v1/admin/users/{userId}`.
- Write examples: invite, import, status update, delete, restore, credit adjustment.
- Existing tests: production dev-auth bypass is now covered by `ProductionReadinessTests.App_IgnoresDevelopmentAuthHeaders_InProduction`.
- Remaining launch evidence: add endpoint-level tests proving `users:read` cannot mutate and `users:write` cannot access unrelated billing/content writes.

### Billing And Freeze Operations

- Read examples: billing diagnostics, provider lifecycle signals, payment transaction ledgers, freeze state reads.
- Write examples: catalog/provider mutations use `AdminBillingCatalogWrite`, refunds/dispute evidence use `AdminBillingRefundWrite`, subscription lifecycle/webhook retry/wallet adjustments use `AdminBillingSubscriptionWrite`, and freeze writes still use `AdminFreezeWrite`.
- Existing tests: billing/freeze tests use debug admin permission headers in development-mode factories.
- Existing tests: `BillingGranularPermissionsTests` covers read-only denial, granular refund/catalog/subscription grants, and legacy `billing:write` as a superset.
- Remaining launch evidence: keep endpoint-policy matrix tests current as billing routes expand.

### AI Configuration

- Endpoint family: `/v1/admin/ai/*`, `/v1/admin/ai-config`, and AI provider registry operations.
- Policy: `AdminAiConfig`.
- Safety note: provider secret material must remain write-only/masked; admin read responses may expose hints/status only.
- Existing tests: provider registry and usage tests cover pieces of this flow; `AdminFlowsTests` covers dedicated AI config permission access and content-permission denial for `/v1/admin/ai-config`.
- Remaining launch evidence: add tests for masked secret reads, server-side test connection, and non-exportability.

### Review Operations And Audit Logs

- Review operations policy: `AdminReviewOps`.
- Audit/system policy: `AdminSystemAdmin`.
- Existing tests: admin dashboard and audit read paths have partial coverage.
- Remaining launch evidence: add least-privilege tests for audit export and review queue mutations.

## Production Dev-Auth Guard

- Startup computes development auth as `Auth:UseDevelopmentAuth && Environment.IsDevelopment()`.
- The `DevelopmentAuthHandler` also rejects debug headers outside Development.
- Evidence: `ProductionReadinessTests.App_IgnoresDevelopmentAuthHeaders_InProduction` boots a Production host with `Auth:UseDevelopmentAuth=true`, sends debug admin headers, and expects `401 Unauthorized` from `/v1/admin/dashboard`.

## Closeout Criteria

- All admin endpoint groups have at least one positive and one negative least-privilege test.
- `system_admin` is tested as an override for each granular policy family.
- Debug headers remain ineffective in Production.
- Secret-bearing admin provider endpoints have redaction and no-plaintext tests.
