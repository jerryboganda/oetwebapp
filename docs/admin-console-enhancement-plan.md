# Admin Console Enhancement Plan — Ultra-High-Level

## Executive Summary

The Admin Console spans **92 pages, ~150+ API endpoints, 16 RBAC permissions**, and 8 navigation groups. It controls every facet of the platform: content management, user operations, billing, review quality, AI configuration, analytics, and governance.

**Critical open gaps from analysis**: Sponsor billing (Phase 5 of ADMIN-BILLING-AUDIT-PLAN), admin ops reporting (Phase 6), 60% untested pages, no UX audit scores, no centralized admin alert dashboard, no bulk content operations.

---

## 1. Admin Operations Command Center

### 1.1 Centralized Admin Alert Dashboard (NEW)

| Component | Details |
|---|---|
| **New route** | `/admin/alerts` — unified alert hub |
| **Backend** | `GET /v1/admin/alerts` — aggregates: SLA violations, stuck reviews, failed webhooks, escalation backlog, freeze requests, publish-request backlog, anomalous AI usage, credit-lifecycle expiries |
| **Severity levels** | Critical (action required), Warning (review needed), Info |
| **Actions** | Each alert links to its respective admin page for resolution |

### 1.2 Bulk Content Operations (NEW)

| Component | Details |
|---|---|
| **New route** | `/admin/content/bulk-operations` |
| **Backend** | `POST /v1/admin/content/bulk-publish`, `POST /v1/admin/content/bulk-archive`, `POST /v1/admin/content/bulk-tag` |
| **Frontend** | Multi-select mode on content library with bulk action toolbar |

### 1.3 Admin Dashboard Redesign

| Change | Details |
|---|---|
| **Alert-first layout** — top section shows unresolved alerts by severity |
| **"At a Glance" metrics**: active learners, reviews in queue, new signups, MRR trend |
| **Quick-action shortcuts** grouped by category |

---

## 2. Sponsor Billing Implementation

### 2.1 Sponsor Billing Accounts & Seat Packs
- `SponsorBillingAccount`, `SponsorSeatPack`, `SponsorInvoice` entities
- Endpoints: create billing account, add seat packs, generate invoices, mark paid

### 2.2 Sponsor Invoice Generation
- Monthly invoice generation from seat packs
- Invoice history with status badges, payment recording

---

## 3. Admin Operations & Reporting

### 3.1 Payment Transaction Management
- Full payment ledger with refund capability, CSV export

### 3.2 Provider Sync & Reconciliation
- Stripe/Provider sync dashboard with diff view, manual sync trigger

### 3.3 Admin Reporting Exports
- Unified export hub: revenue, users, content performance reports

---

## 4. Testing Coverage Enhancement

| Priority | Target | Tests |
|---|---|---|
| **P0** | Escalations, Flags, Freeze, Community pages | 19 unit tests |
| **P1** | AI Usage, Conversation, Pronunciation, Grammar pages | 27 unit tests |
| **P2** | Bulk Operations, Content Quality | 8 unit tests |
| **E2E P0** | Alert hub, sponsor billing, bulk content | 3 new E2E specs |
| **Backend P0** | Escalations, flags, freeze, sponsor billing | 15 backend tests |

---

## 5. Implementation Sequence

**Phase A** — Admin Alerts + Bulk Content + Sponsor Billing Backend
**Phase B** — Frontend Pages + Dashboard Redesign
**Phase C** — Testing Backfill
**Phase D** — Reports + Reconciliation + UX Audit

### Files

**New (12)**: alert hub page, bulk content page, reports page, 3 backend endpoint files, 2 service files, 2 entity/contract files, 2 E2E specs

**Modified (25)**: dashboard, layout, content library, billing, escalations, flags, freeze, community, API client, types, permissions, auth hook, admin endpoints, admin service, DbContext, Program.cs, NotificationHub, playwright config, product manual
