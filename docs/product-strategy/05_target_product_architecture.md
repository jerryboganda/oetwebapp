# 05 - Target Product Architecture

## Document intent

This target architecture is a code-grounded evolution plan, not a greenfield design.

Verified foundations already exist across:

- Next.js learner, expert, and admin web surfaces
- ASP.NET Core minimal API endpoints grouped by learner, expert, admin, notifications, analytics, and auth
- Entity Framework domain models for billing, review ops, content revisions, AI config, notifications, and exam-family references
- Electron and Capacitor shells around the shared web product

The target state is therefore:

1. Keep the strong shared core.
2. Remove OET-only assumptions from shared workflows.
3. Deepen OET where it creates premium trust and outcomes.
4. Add IELTS on top of the shared core next.
5. Treat PTE as a later dedicated engine, not a thin exam toggle.

## Architecture principles

1. Shared-core first. Anything that can be reused across OET and IELTS should live in shared services, contracts, analytics, and admin tooling.
2. Premium trust over shallow scale. Human review, AI confidence framing, and auditability matter more than adding exam logos quickly.
3. Additive data-model changes only. Reuse current billing, wallet, review, revision, and AI config foundations.
4. OET stays flagship. The architecture must protect OET profession depth while still enabling multi-exam scale.
5. Packaging layers stay thin. Electron and Capacitor should remain wrappers around the main web product until core learning and monetization loops are stronger.

## Target layered model

### 1. Client layer

Verified today:

- learner-facing Next.js routes
- expert workspace routes
- admin operations routes
- Electron desktop shell
- Capacitor mobile shell

Target role:

- keep presentation logic, routing, local cache helpers, and entitlements-aware UI in the client layer
- move payment-provider, score-normalization, and escalation rules into backend-owned contracts

### 2. API and orchestration layer

Verified today:

- learner, expert, admin, auth, notification, and analytics endpoint groups
- service-oriented orchestration via `LearnerService`, `ExpertService`, `AdminService`, `AuthService`, `NotificationService`, and `BackgroundJobProcessor`
- new `PaymentGatewayService` and `WalletService` enhancements added in this session

Target role:

- keep endpoint groups role-scoped
- make exam-family-aware scoring, AI trust, entitlements, and payment state backend-owned
- expose only normalized, learner-safe summaries to the frontend

### 3. Domain and persistence layer

Verified today:

- user, attempt, evaluation, review, readiness, study-plan, content, billing, notification, and audit entities
- exam-family references already present for `oet`, `ielts`, and `pte`
- billing entities already present for plans, add-ons, coupons, subscriptions, invoices, wallet, payment transactions, and webhook events

Target role:

- keep shared-core entities exam-aware where necessary
- keep OET-specific logic in content taxonomy, profession taxonomy, criteria mapping, reporting, and coaching layers
- add only the minimum extra persistence needed for payment reliability, AI auditability, and content provenance

## Shared-core platform domains

| Domain | Verified foundation | Target evolution |
| --- | --- | --- |
| Identity and access | Auth accounts, refresh tokens, OTP, MFA recovery, external identity links, role-scoped surfaces | Keep exam-agnostic; ensure registration and onboarding capture exam-family intent cleanly |
| Learner profile and goals | Learner profile, onboarding, goals, exam-family references | Normalize goal validation and readiness targets by exam family |
| Practice engine | Writing, speaking, reading, listening, diagnostics, mocks, task delivery | Remove OET-only copy and response assumptions from shared task orchestration |
| Evaluation trust boundary | AI evaluations, productive-skill summaries, review escalation, admin AI config | Add confidence bands, provenance labels, escalation rules, and explicit non-official score framing everywhere it matters |
| Expert review operations | Queue, claim, release, drafts, calibration, quality metrics, admin review ops | Improve SLA visibility, escalation context, and QA reporting |
| Readiness and planning | Study plans, readiness states, diagnostics, dashboard summaries | Generalize readiness logic for OET and IELTS while preserving OET-specific blockers |
| Billing and entitlements | Plans, add-ons, coupons, quotes, subscriptions, invoices, wallet, payment transactions, webhook events | Harden provider-grade checkout, webhook reliability, reconciliation, and strict entitlement enforcement |
| Content operations | Content items, revisions, taxonomy, criteria, admin editing | Add provenance, stale-content review, rubric coverage, QA visibility, and performance analytics |
| Notifications and engagement | Inbox, preferences, policies, delivery attempts, push subscriptions | Focus next on study reminders, readiness digests, and review-status trust messaging |
| Analytics and audit | Admin analytics, audit logs, review and billing reporting | Track conversion, entitlement use, AI confidence routing, expert SLA, readiness drift, and content performance by exam family |
| Packaging shells | Electron and Capacitor wrappers | Keep thin and shared-product-aligned; do not let shell work outrun core product value |

## Exam-family layers

### OET flagship layer

OET remains the premium depth layer.

Target responsibilities:

- profession-aware onboarding and goal framing
- profession-specific writing remediation
- speaking role-play and transcript coaching grounded in healthcare communication
- compare-attempt analytics for productive skills
- expert-review premium flows with clear SLA and quality visibility
- readiness blockers tied to profession, subtest, and evidence quality

### IELTS operational layer

IELTS should be the next exam family operationalized on the shared core.

Target responsibilities:

- Academic versus General pathway selection
- IELTS-specific score scale, task families, and writing/speaking criteria handling
- reuse of shared diagnostics, practice plumbing, entitlements, notifications, analytics, and admin tooling
- task-family-specific reporting that feels native to IELTS rather than re-skinned OET

### PTE future layer

PTE should be deferred until its engine is designed properly.

Target responsibilities when started:

- question-type bank and rapid drill delivery
- integrated-skill orchestration
- computer-based timing and simulation
- AI-heavy speaking and writing scoring
- PTE-native analytics and remediation

## Service ownership model

| Service | Owns now | Should own next |
| --- | --- | --- |
| `LearnerService` | learner flows, study plans, diagnostics, billing actions, productive-skill orchestration | exam-family-aware validation, trusted score summaries, compare-attempt insights, entitlement-safe purchase flows |
| `ExpertService` | review queue, drafts, expert operations | escalation context, SLA visibility, review-quality signals, premium turnaround reporting |
| `AdminService` | content ops, AI config, billing admin, flags, audit surfaces | confidence-policy controls, provenance analytics, content freshness workflows, experiment oversight |
| `PaymentGatewayService` | provider selection, checkout creation, webhook processing | production provider hardening, normalization, reconciliation hooks, provider health telemetry |
| `BackgroundJobProcessor` | async evaluation and notifications | confidence-triggered escalation, payment reconciliation, content QA refresh, engagement snapshots |
| `NotificationService` | inbox and delivery orchestration | lifecycle messaging tied to readiness risk, review state, entitlements, and re-engagement |

## Data-model strategy

The plan is intentionally additive and minimal.

Reuse existing foundations:

- exam-family references
- billing and wallet entities
- review entities
- content revision entities
- admin AI config entities

Add only where there is clear product value:

- provider-grade payment metadata and reconciliation state where the current billing domain still needs it
- AI auditability fields such as confidence category, escalation reason, provenance label, and human-review recommendation context where those are not yet persisted
- content provenance, stale-review, rubric-coverage, and performance-summary fields needed for operational QA

## Target product shape

The end state is a single platform with:

- one shared authentication, billing, analytics, notification, and admin backbone
- one shared practice and readiness engine
- one trust boundary for AI versus expert evaluation
- one premium OET flagship layer with deeper profession-specific value
- one IELTS layer built on the shared core next
- one future PTE engine that is started only when its product model is clear

That architecture lets the platform scale without flattening what makes OET premium.
