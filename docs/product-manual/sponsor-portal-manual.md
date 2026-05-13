# Sponsor Portal Manual

This manual documents the sponsor-facing portal as implemented in the current codebase.

Related documents:

- [Master Product Manual](./master-product-manual.md)
- [Learner App Manual](./learner-app-manual.md)
- [Expert Console Manual](./expert-console-manual.md)
- [Admin Dashboard and CMS Manual](./admin-dashboard-cms-manual.md)
- [Cross-System Business Logic and Workflows](./cross-system-business-logic-and-workflows.md)
- [Route, API, and Domain Surface Index](./route-api-domain-surface-index.md)
- [Reference Appendix](./reference-appendix.md)

Status labels used in this document:

- `implemented`: confirmed in the current UI and supported by the current API surface
- `partial`: available but constrained, heuristic, or not fully data-driven
- `unclear`: referenced by the system, but not confirmed strongly enough to document as complete behavior

These labels follow the canonical vocabulary in [README](./README.md) and do not by themselves mean end-to-end release validation.

## Sponsor Route Inventory

- `/sponsor`
- `/sponsor/learners`
- `/sponsor/billing`

`defaultRouteForRole('sponsor')` resolves to `/sponsor`. Backend sponsor operations require the `SponsorOnly` policy; portal page access also relies on route shells/AuthGuard and post-auth role routing. Treat sponsor route protection as implemented-by-design but still requiring explicit sponsor-role QA coverage.

## Sponsor API Surface

Backend sponsor operations are grouped under `/v1/sponsor` and require the `SponsorOnly` authorization policy.

- `GET /v1/sponsor/dashboard`
- `GET /v1/sponsor/learners`
- `POST /v1/sponsor/learners/invite`
- `DELETE /v1/sponsor/learners/{id}`
- `GET /v1/sponsor/billing`

## 1. Sponsor Dashboard

- Status: `implemented`
- Purpose: Provide an organization-level view of sponsored learner coverage and spend.
- Business logic served: Gives sponsors a controlled operating surface without granting admin rights.
- Location: `/sponsor`
- Who uses it: sponsor users representing institutions, employers, or funding organizations
- Main inputs:
  - sponsor account identity
  - active, pending, and revoked sponsorship records
  - billing snapshot derived from linked learner transactions
- Main outputs:
  - sponsor and organization name
  - learners sponsored
  - active sponsorship count
  - pending sponsorship count
  - total spend and currency where aggregate currency is meaningful
- Main actions:
  - inspect portfolio status
  - move into sponsored learners
  - move into sponsor billing
- Dependencies:
  - sponsor account record
  - sponsorship records
  - payment transaction records
  - sponsor authorization policy

## 2. Sponsored Learners

- Status: `implemented`
- Purpose: Manage the learner cohort connected to the sponsor.
- Business logic served: Allows sponsor-side learner lifecycle operations without exposing full admin user controls.
- Location: `/sponsor/learners`
- Who uses it: sponsor account operators
- Main inputs:
  - paginated sponsorship list
  - learner email invite target
- Main outputs:
  - sponsored learner list
  - pending invitations
  - active sponsorship state
  - removed sponsorship state
- Main actions:
  - view sponsored learners
  - invite a learner by email
  - remove a sponsorship
- Dependencies:
  - `/v1/sponsor/learners`
  - `/v1/sponsor/learners/invite`
  - `/v1/sponsor/learners/{id}` delete endpoint
  - learner account acceptance or matching logic

## 3. Sponsor Billing

- Status: `partial`
- Purpose: Show sponsor-attributable spend and recent invoice-like transaction rows.
- Business logic served: Gives sponsors financial visibility for linked learner activity.
- Location: `/sponsor/billing`
- Who uses it: sponsor account operators and finance contacts
- Main inputs:
  - completed payment transactions for linked learners
  - sponsorship active windows
- Main outputs:
  - total spend
  - current-month spend
  - recent sponsor-attributable invoices
  - aggregate currency when all invoices share a currency
- Main actions:
  - inspect current spend
  - review recent learner-linked transactions
- Important limitation:
  - Sponsor-attributable spend is currently computed heuristically from learner transactions inside active sponsorship windows. There is not yet a direct sponsor-paid flag on `PaymentTransaction`, so this is operational reporting rather than accounting-grade institutional billing.

## 4. Cross-System Dependencies

Sponsor workflows connect to the rest of the platform through these boundaries:

- Auth and RBAC: backend sponsor APIs enforce sponsor-only access; portal UI isolation is handled by AuthGuard/route shells and role routing, so sponsor-role page access should be tested explicitly.
- Learner lifecycle: sponsor invites and removals affect which learners are represented in sponsor views.
- Billing: sponsor billing depends on learner transactions and sponsorship windows.
- Admin operations: institution, enterprise, user, billing, and audit surfaces provide the operator-side governance around sponsor accounts.
- Notifications: invite and sponsorship lifecycle events should remain consistent with notification policy governance.

## 5. What Sponsors Do Not Control

Sponsors do not:

- publish or edit content
- assign expert reviews
- configure AI, scoring, rulebooks, or rubrics
- manage arbitrary users outside their sponsored cohort
- view private learner practice details unless a dedicated sharing workflow explicitly grants that access

## 6. QA Focus Areas

- sponsor-only API protection and portal page access via AuthGuard/role routing
- sponsor auth-state coverage, because the standard E2E role matrix may not include sponsor by default
- sponsor default post-auth routing
- learner invite creation and pending-state display
- removal of a sponsorship from sponsor view
- billing calculation for active, revoked, and multi-currency sponsorships
- pagination and empty states for sponsored learners
- no cross-role access to `/admin`, `/expert`, or learner-only paths
