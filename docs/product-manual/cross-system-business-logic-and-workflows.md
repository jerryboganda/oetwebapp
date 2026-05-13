# Cross-System Business Logic and Workflows

This document explains how the learner, expert, sponsor, admin, and platform layers operate as one OET preparation system.

Related documents:

- [Master Product Manual](./master-product-manual.md)
- [Learner App Manual](./learner-app-manual.md)
- [Expert Console Manual](./expert-console-manual.md)
- [Sponsor Portal Manual](./sponsor-portal-manual.md)
- [Admin Dashboard and CMS Manual](./admin-dashboard-cms-manual.md)
- [Route, API, and Domain Surface Index](./route-api-domain-surface-index.md)
- [Reference Appendix](./reference-appendix.md)
- [_Audit Fact Base](./_audit-fact-base.md)

## 1. Business Logic Breakdown

### 1.1 OET-specific preparation logic

The platform treats OET as four related but different assessment domains:

- objective sub-tests: Reading and Listening
- productive sub-tests: Writing and Speaking
- language support domains: Grammar, Pronunciation, Vocabulary, Recalls, Strategies, Lessons, and Conversation
- readiness domains: Diagnostics, Mocks, Study Plan, Progress, Readiness, Predictions, Remediation, and Next Actions

This distinction shapes the product:

- objective sub-tests are answer-, timing-, explanation-, and analytics-driven
- productive sub-tests are evidence-, rubric-, revision-, and expert-review-driven
- language support modules are server-authoritative where rulebooks, AI, ASR/TTS, or entitlement affect behavior
- readiness is based on diagnostic, practice, mock, review, and blocker signals

### 1.2 Criterion-first and rulebook-grounded feedback logic

The platform does not stop at summary scoring. Current feedback flows are built around:

1. source task or attempt evidence
2. canonical scoring and rulebook context
3. grounded AI or deterministic service evaluation where applicable
4. criterion-level findings
5. learner revision, drill, remediation, or expert escalation
6. analytics and auditability for admin oversight

### 1.3 Four-portal operating logic

- Learners create preparation evidence.
- Experts validate, review, tutor, calibrate, and communicate around productive-skill evidence.
- Sponsors manage funded cohorts and sponsor-attributable billing snapshots.
- Admins govern the platform contract: content, catalog, rulebooks, AI, reviews, billing, notifications, users, analytics, and audit logs.

### 1.4 Platform packaging logic

The same product model is delivered through web, desktop, and mobile layers. Service worker/offline code, Electron, Capacitor, secure storage, push/deep-link integrations, device pairing, media authorization, and retention jobs should be treated as product infrastructure, not separate products. Release planning should include the PWA/service-worker path, Capacitor deep links and offline queue/cache, Electron API-target resolution, mobile secure storage and certificate pinning, and shared `/api/backend` proxy parity.

Runtime health and real-time surfaces are also part of the platform contract: `/health/live` for process liveness, `/health` for database connectivity, `/health/ready` for database/storage/stuck-job readiness, `/v1/notifications/hub` for account-scoped notifications, and `/v1/conversations/hub` for real-time conversation surfaces.

Frontend route handlers under `app/api` are part of the contract too: `/api/health` is the web container health endpoint, and `/api/backend/[...path]` is the CSRF/origin-validated proxy for backend `/v1/*` calls.

Security and privacy boundaries include JWT/refresh-token rotation, account suspension/deletion checks, verified-email gates where required, CSRF/CSP, rate limits for per-user writes and AI credential validation, BYOK credential custody, AI response-body retention defaults, audio consent/retention, and upload scanner fail-closed behavior.

## 2. Dashboard Interdependency Map

| Source surface | Action | Dependent surface | Operational effect |
| --- | --- | --- | --- |
| Admin | publish content paper/assets | Learner | practice content becomes available |
| Admin | edit signup catalog or taxonomy mirror | Learner / Expert / Sponsor / Admin | profession and catalog options change across account setup, content, and filters |
| Admin | edit criteria or rulebook | Learner / Expert / AI | feedback, scoring, and rubric interpretation change |
| Admin | activate AI config/provider/usage policy | Learner / Expert / Admin | AI routing, budgets, provider use, and fallbacks change |
| Admin | edit billing, wallet, free-tier, freeze, or webhook settings | Learner / Sponsor | entitlements, credits, purchases, and billing state change |
| Learner | complete diagnostic/practice/mock | Learner / Admin | readiness, progress, analytics, and next actions update |
| Learner | request Writing or Speaking review | Expert / Admin | expert queue and review ops receive work |
| Learner | book private speaking or marketplace/tutoring item | Expert / Admin / Billing | schedule, room, marketplace, and payment workflows activate |
| Expert | submit review or calibration | Learner / Admin | feedback returns to learner evidence and quality metrics update |
| Expert | manage schedule/private-speaking availability | Learner / Admin | booking and capacity behavior changes |
| Sponsor | invite or remove learner | Learner / Sponsor / Admin | sponsorship state and sponsor cohort reporting change |
| Admin | edit notification policy | Learner / Expert / Sponsor / Admin | delivery behavior changes for operational events |

## 3. Feature Dependency Graphs

### 3.1 Learner planning loop

`Onboarding -> Goals -> Diagnostics -> Study Plan -> Practice -> Progress/Readiness -> Next Actions`

### 3.2 Writing improvement loop

`Writing Library/Home -> Writing Player -> Writing Result -> Writing Feedback -> Writing Revision -> Compare/Drills/Phrase Suggestions`

Optional escalation:

`Writing Result/History -> Expert Review Request -> Expert Queue -> Expert Writing Review -> Learner Evidence -> Admin Quality Analytics`

### 3.3 Speaking improvement loop

`Speaking Home -> Task Selection -> Device Check -> Role Card -> Speaking Task -> Result -> Transcript/Phrasing/Rulebook`

Optional escalation:

`Speaking Result -> Expert Review Request -> Expert Queue -> Expert Speaking Review -> Learner Evidence`

Private-speaking branch:

`Marketplace/Private Speaking -> Booking -> Expert Private Speaking -> Speaking Room -> Billing/Admin Oversight`

### 3.4 Reading objective-skill loop

`Reading Home/Practice/Paper -> Submit -> PM-001-tracked Results -> Error Patterns/Analytics`

Reading review visibility must stay aligned with the AGENTS hard ban: learner-facing endpoints must never serialize `CorrectAnswerJson`, `ExplanationMarkdown`, or `AcceptedSynonymsJson`. The projection layer in `ReadingLearnerEndpoints.cs` is the enforcement point. The current backend ability to emit `CorrectAnswer` and `ExplanationMarkdown` after submit when policy permits is an unresolved release-blocking conflict tracked as PM-001 and must be reconciled (either by removing the policy switch or by updating the AGENTS invariant with sign-off) before release.

### 3.5 Listening objective-skill loop

`Listening Home/Player -> Submit -> Results -> Transcript Review -> Drills/Pathway/Analytics`

Listening full-test authoring preserves the canonical 42-item structure: Part A 24, Part B 6, and Part C 12. Scoring routes through the canonical raw-to-scaled service.

Authoring branch:

`Admin Authoring -> Publish Gate -> Learner Projection -> Attempt -> Analytics -> Admin Quality/Content Effectiveness`

### 3.6 Grammar, Pronunciation, and Conversation loops

Grammar:

`Admin Grammar Authoring/AI Draft -> Rulebook Validation -> Publish -> Learner Lesson/Topic -> Entitlement/Progress`

Grammar free tier is capped at 3 lessons per rolling 7 days. Admin AI drafts are platform-only, grounded in the grammar rulebook, and must cite valid applied rule IDs.

Pronunciation:

`Admin Drill Authoring/AI Draft -> Publish Gate -> Learner Drill/Discrimination -> ASR Provider -> Score/Feedback -> Audio Retention`

Pronunciation scoring routes through the ASR provider selector, projects `70/100 == 350/500`, and never uses random scoring. Published drills require phoneme, label, tips, at least 3 example words, and at least 1 sentence.

Conversation:

`Admin Scenario Authoring -> Template Publish -> Learner Session -> ASR/TTS/AI Reply -> Evaluation -> Review Items/Results`

Conversation ASR/TTS routes through provider selectors. Evaluation projects mean `4.2/6 == 350/500`, supports `oet-roleplay` and `oet-handover`, writes audio through storage services, and seeds review items for rule-cited issues. Learner availability depends on published scenario/task types and entitlement; an empty scenario state is expected when nothing is enabled.

Learner AI self-service:

`Settings AI -> BYOK Credentials/Preferences -> Feature Policy -> Gateway/Quota -> Usage/Credits`

The `/v1/me/ai/*` endpoints derive user identity from the token, throttle credential validation, and expose preferences, usage, and credit ledgers without making platform-only AI features BYOK-eligible.

### 3.7 Sponsor lifecycle

`Sponsor Account -> Dashboard -> Invite Learner -> Sponsorship Active/Pending -> Learner Activity -> Sponsor Billing Snapshot -> Admin Enterprise/Billing Context`

### 3.8 Admin governance loop

`Catalog/Content/Rulebooks/AI/Billing/Notifications/Flags -> Learner/Expert/Sponsor Behavior -> Analytics/Alerts/Audit/Review Ops -> Admin Intervention`

## 4. Operational Lifecycle Examples

### 4.1 Diagnostic to study-plan lifecycle

1. Learner completes onboarding and goals.
2. Learner starts diagnostics across the four sub-tests.
3. Diagnostic attempts create baseline evidence.
4. Diagnostic results identify weak areas and risk.
5. Study plan, readiness, next actions, predictions, and remediation use the evidence.

### 4.2 Content-paper lifecycle

1. Admin creates or imports a paper/content unit.
2. Admin attaches typed assets and source provenance.
3. Publish gates check required roles and metadata.
4. Published learner projections become available to relevant routes.
5. Attempts and analytics feed back into content quality, effectiveness, and audit tools.
6. Revisions, deduplication, staleness, and rollback flows protect quality.

### 4.3 Rulebook-grounded AI lifecycle

1. A learner, expert, or admin workflow requests AI-supported behavior.
2. The gateway builds a grounded prompt using the canonical rulebook/scoring context.
3. Provider selection, quota, BYOK/platform policy, and kill-switch logic determine execution.
4. Success, provider error, or refusal creates usage records and visible downstream state.
5. Admin AI usage/provider/config surfaces monitor and adjust policy.

### 4.4 Review request lifecycle

1. Learner requests expert review for Writing or Speaking.
2. The request enters review operations with SLA, priority, and artifact readiness metadata.
3. Expert queue exposes claim/release/open actions.
4. Expert completes the relevant workspace and can save drafts or request rework.
5. Admin can intervene through review ops, escalations, alerts, and quality analytics.
6. Review output returns to learner history, readiness, and progress.

### 4.5 Sponsor learner lifecycle

1. Sponsor signs in and reaches the sponsor dashboard.
2. Sponsor invites a learner by email.
3. Sponsorship appears as pending or active depending on account/link state.
4. Sponsor can remove sponsorship without becoming an admin.
5. Billing snapshot computes spend from linked learner transactions inside active sponsorship windows.
6. Admin enterprise, institution, user, billing, and audit surfaces govern wider sponsor operations.

### 4.6 Billing and entitlement lifecycle

1. Admin configures plans, wallet tiers, add-ons, free-tier policy, freezes, coupons, webhooks, and score-guarantee claims.
2. Learner-facing billing, pricing, upgrade, referral, freeze, private-speaking, marketplace, and score-guarantee flows consume those rules.
3. Payment and webhook outcomes update entitlement and operational state.
4. Admin billing, credit lifecycle, BI, audit, and support surfaces inspect and correct issues.

### 4.7 Community and escalation lifecycle

1. Learner participates in community, threads, groups, reviews, peer review, or ask-an-expert flows.
2. Issues can create escalations or moderation needs.
3. Admin community, escalations, alerts, and audit surfaces track and intervene.
4. Expert messaging or ask-an-expert surfaces may handle domain-specific responses.

## 5. Business-to-Feature Mapping

| Business need | Implementing features |
| --- | --- |
| Establish learner baseline | onboarding, goals, diagnostics, diagnostic results |
| Personalize preparation | study plan, weak-skill focus, next actions, predictions, readiness, remediation |
| Support productive-skill improvement | Writing feedback/revision/drills, Speaking transcript/phrasing, expert review, private speaking |
| Support objective-skill improvement | Reading/Listening players, paper results, transcript/explanations, drills, pathways, analytics |
| Expand learning beyond test attempts | Grammar, Pronunciation, Conversation, Vocabulary, Recalls, Lessons, Strategies, Learning Paths |
| Add trusted human review | expert queue, review workspaces, calibration, scoring quality, review ops |
| Support institutional cohorts | sponsor portal, learner invites/removals, sponsor billing, admin enterprise/institutions |
| Govern AI behavior | AI config, providers, usage, grounded gateway, rulebooks, kill-switch/fallback policy |
| Govern content quality | content papers, imports, media, source provenance, publish gates, revisions, audit logs |
| Control monetization | billing, wallet tiers, freezes, free tier, marketplace, private speaking, referrals, webhooks |
| Control communications | learner settings, admin notifications, expert messages, sponsor events |
| Protect uploaded assets | role upload limits, chunking, scanner/quarantine policy, ZIP limits, storage abstraction |
| Operate runtime health | health endpoints, SignalR hubs, background workers, deployment runbooks |

## 6. Risks, Gaps, and Fragile Areas

### 6.1 Transitional routing and content IDs

Some diagnostic, mock, and learner service flows still reference fixed or legacy task IDs. This should remain visible in release testing because dynamic content publication can otherwise drift from learner routing.

### 6.2 Unavailable live AI speaking mode

Speaking route logic exposes an AI mode, but live AI speaking is not functionally available and falls back to self-guided behavior.

### 6.3 Sponsor billing attribution

Sponsor billing is computed from learner transactions inside sponsorship windows. It is operationally useful but should not be treated as a final accounting model until direct sponsor-paid transaction attribution exists.

### 6.4 Content upload model reconciliation

The canonical content-paper upload pipeline and the legacy media upload route both appear in the codebase. Any content/media release should validate which path is authoritative for the asset type being changed.

### 6.5 Learner-safe Reading review contract

Reading review output must be validated against the AGENTS hard ban that learner-facing DTOs must never serialize `CorrectAnswerJson`, `ExplanationMarkdown`, or `AcceptedSynonymsJson`. Any backend code path that exposes those fields to a learner is a release-blocking regression and must be reconciled per PM-001 before release.

### 6.6 Platform parity and offline behavior

Web, desktop, mobile, service-worker, offline-sync, and native integrations exist, but full parity and queued submission replay must be validated by release scenario.

### 6.7 Device-pairing durability

Device pairing currently uses 6-character, 90-second, single-use in-memory codes. It is useful as a scaffold, but it should not be treated as restart-durable or multi-replica production pairing without a durable broker and verified anonymous redeem rate limiting.

### 6.8 Conversation content availability

Conversation routes and backend services are implemented, but the learner experience depends on published scenarios/task types and entitlement. Empty-state behavior is valid and should be part of release QA.

### 6.9 Role-guard validation

Privileged backend APIs enforce role policies, while portal page access also depends on route shells/AuthGuard and role routing. Sponsor-role page access should be explicitly tested because the standard E2E matrix may not include sponsor state.

## 7. What to Validate End to End

- onboarding -> goals -> diagnostics -> study plan -> next actions
- Writing attempt -> result -> feedback -> revision -> expert review -> learner visibility
- Speaking task -> transcript/phrasing -> expert review or private-speaking booking
- Reading paper authoring -> publish -> learner attempt -> PM-001 review DTO reconciliation -> analytics
- Listening authoring -> learner player -> review/drills/pathway -> analytics
- Grammar/pronunciation/conversation admin draft -> publish -> learner usage -> policy/retention behavior
- sponsor invite -> learner sponsorship state -> sponsor billing snapshot
- marketplace/private-speaking purchase -> booking -> expert room -> billing/admin oversight
- content paper import -> required assets -> source provenance -> publish gate -> audit event
- AI config/provider/quota change -> grounded invocation -> usage record -> admin usage visibility
- notification policy update -> learner/expert/sponsor/admin delivery behavior
- billing webhook/freeze/free-tier/credit update -> entitlement visibility
- `/api/backend` proxy request -> CSRF/origin/header/path controls -> backend `/v1/*` response
- upload/import -> scanner/quarantine -> content-paper publish gate -> learner availability
- device-pairing initiate/redeem -> deep link -> mobile/desktop handoff failure modes
- sponsor role sign-in -> sponsor pages -> blocked admin/expert/learner-only paths

## 8. Cross-System Conclusion

The system behaves like an integrated OET operations platform rather than disconnected dashboards. Learners create evidence, experts validate and enrich it, sponsors manage cohort-level funding context, admins define and govern the operating contract, and platform services make the experience portable across web, desktop, and mobile shells.
