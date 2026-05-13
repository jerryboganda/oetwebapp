# OET Prep Platform Master Product Manual

This documentation package explains the implemented product across four portals and the platform layers that support them:

- [Learner App Manual](./learner-app-manual.md)
- [Expert Console Manual](./expert-console-manual.md)
- [Sponsor Portal Manual](./sponsor-portal-manual.md)
- [Admin Dashboard and CMS Manual](./admin-dashboard-cms-manual.md)
- [Cross-System Business Logic and Workflows](./cross-system-business-logic-and-workflows.md)
- [Route, API, and Domain Surface Index](./route-api-domain-surface-index.md)
- [Reference Appendix](./reference-appendix.md) — mission-critical hard invariants, options, workers, hubs, notifications, retention, glossary, permissions, edge states, parity, observability, support, and the release/QA quick start.
- Working evidence base: [_Audit Fact Base](./_audit-fact-base.md)

## 1. Executive Summary

The OET Prep Platform is a role-based OET preparation and operations system. It serves learners preparing for the exam, experts reviewing and tutoring productive-skill work, sponsors managing learner cohorts, and admins operating the platform.

Its purpose is to help healthcare professionals prepare for the OET exam through structured diagnostics, study planning, sub-test practice, mock testing, rulebook-grounded feedback, expert review, sponsor-supported cohorts, billing and entitlement controls, and operational oversight.

This is not a generic learning platform. The current implementation is organized around the realities of OET:

- four sub-tests with different preparation needs
- profession-aware practice, goal setting, and signup/catalog governance
- objective scoring for Reading and Listening
- criterion-based evaluation and expert intervention for Writing and Speaking
- server-authoritative Grammar, Pronunciation, and Conversation modules
- readiness tracking based on accumulated evidence, not only content completion
- admin-governed AI, rulebooks, content publishing, billing, notifications, and auditability
- sponsor workflows for institutional learner management and spend visibility
- web, desktop, and mobile packaging layers that share the same product model

## 2. Core Product Model

### Portal 1: Learner App

The learner app is where candidates:

- define goals and target geography
- complete diagnostics
- follow a study plan
- practice Writing, Speaking, Reading, and Listening
- use Grammar, Pronunciation, Conversation, Vocabulary, Recalls, Lessons, Strategies, and practice/remediation tools
- request expert reviews and participate in private speaking or marketplace/tutoring flows where entitled
- take mocks and inspect reports
- monitor readiness, progress, achievements, submissions, billing, settings, and community activity

### Portal 2: Expert Console

The expert console is where reviewers and tutors:

- receive review work
- claim, release, and complete Writing and Speaking reviews
- inspect learner context
- complete calibration and speaking calibration
- manage schedule and private-speaking availability
- handle messages, mock bookings, live speaking rooms, compensation, templates, AI-prefill support, queue priority, scoring quality, and mobile review workflows

### Portal 3: Sponsor Portal

The sponsor portal is where institutional or funding users:

- inspect sponsored learner coverage
- invite learners into sponsorship
- remove sponsorships
- view sponsor-attributable billing snapshots
- operate within a sponsor-only authorization boundary

Sponsor billing is currently operational reporting, not accounting-grade institutional billing, because spend is inferred from linked learner transactions inside active sponsorship windows.

### Portal 4: Admin Dashboard and CMS

The admin surface is the platform control plane. Admins manage:

- content hub, content papers, imports, media, hierarchy, deduplication, generation, publish requests, and content quality
- authoring for Writing, Reading authoring/policy, Speaking mock sets, Listening, Mocks, Grammar, Pronunciation, Conversation, Vocabulary, Recalls, and Strategies
- signup catalog, profession taxonomy legacy mirror, rubrics, criteria, and rulebooks
- AI configuration, providers, usage, budgets, options, and tool/escalation operations
- review ops, escalations, marketplace review, private speaking, score-guarantee claims, and SLA health
- users, experts, roles, permissions, institutions, enterprise, and sponsor-adjacent operations
- billing, wallet tiers, credit lifecycle, free tier, freezes, webhooks, flags, notifications, alerts, analytics, BI, and audit logs

### Platform Layers

The product is packaged as a Next.js web app with an ASP.NET Core API, PostgreSQL, SignalR, Electron desktop shell, and Capacitor mobile shell. Platform features include auth/MFA, role routing, service-worker/offline support, device pairing, secure storage/mobile integrations, media authorization, notifications, analytics ingestion, background workers, and deployment/runtime infrastructure.

Security and privacy caveats are part of the product contract: JWT/refresh-token rotation, account deletion/suspension checks, CSRF/CSP protections, granular admin permissions, BYOK key encryption, platform-only AI restrictions for sensitive scoring/drafting features, AI response-body retention defaults, audio consent/retention, mobile certificate pinning/secure storage, and upload-scanner fail-closed behavior must be considered when changing the user-facing product.

Operationally, this manual is not a deployment checklist. Production environment details, Nginx Proxy Manager networking, `.env.production`, named volumes, backup/restore expectations, and destructive-volume prohibitions remain governed by the deployment runbooks.

## 3. Role Matrix

| Role | Primary objective | Main permissions |
| --- | --- | --- |
| Learner | Prepare for OET and reach target score | goals, diagnostics, study plan, sub-test practice, learning modules, mocks, review requests, community, billing, settings |
| Expert | Review and tutor accurately and on time | queue, claim/release, Writing review, Speaking review, learner context, calibration, private speaking, messages, schedule, metrics, compensation |
| Sponsor | Manage a funded learner cohort | sponsor dashboard, sponsored learners, invitations, sponsorship removal, sponsor billing snapshot |
| Admin | Operate and govern the platform | content, authoring, rulebooks, AI, review ops, users, experts, sponsor/enterprise, billing, notifications, analytics, flags, audit logs |

What each role does not do:

- learners do not directly manage review assignment, sponsor cohorts, or platform configuration
- experts do not publish content, configure billing, or administer users globally
- sponsors do not review private learner submissions or control platform policy unless a dedicated sharing/governance workflow exists
- admins govern the platform but are not documented as ordinary learner users inside these manuals

## 4. End-to-End Platform Flow

1. Admin defines what the platform can deliver: content, papers, rulebooks, criteria, AI policy, catalog, billing, entitlements, notifications, and flags.
2. Learners consume the preparation experience and create evidence through diagnostics, practice, mocks, community/review flows, and paid or sponsored services.
3. Productive-skill submissions and private-speaking flows generate expert work where human judgment is required.
4. Experts produce structured feedback, calibration outputs, messages, and tutoring/session outcomes.
5. Sponsors manage cohorts and view sponsor-attributable activity inside their authorization boundary.
6. Admin monitors quality, throughput, AI usage, content performance, billing health, escalations, and audit trails.
7. Review outcomes, scoring results, content usage, and operational events flow back into readiness, progress, notifications, and analytics.

## 5. OET Domain Mapping

### Reading

Reading is server-governed objective practice and authoring. The canonical authored structure is 20 Part A items, 6 Part B items, and 16 Part C items. Grading is exact-match and raw-to-scaled conversion routes through the canonical scoring service.

### Listening

Listening is objective audio practice with player, results, transcript-backed review, drills, pathway, curriculum, class/test-rules surfaces, admin authoring, and analytics. Full-test authoring preserves the canonical 42-item structure: Part A 24, Part B 6, and Part C 12. Scoring uses the canonical raw-to-scaled service.

### Writing

Writing includes task library, timed player, result summary, detailed criterion feedback, revision, model-answer explanation, drills, compare, phrase suggestions, PDF/coach support, and expert review request workflows. Learner Writing analytics exists but is partial because it currently renders deterministic seed data until the learner weaknesses API ships. Admin Writing rule-violation analytics are implemented separately.

### Speaking

Speaking includes task selection, device check, role cards, recording, results, transcript review, phrasing drills, mocks, fluency timeline, rulebook views, expert review requests, private-speaking bookings, and live-room support. Live AI speaking mode remains unavailable and falls back to self-guided behavior.

### Grammar

Grammar is server-authoritative. Admins author or generate lessons through grounded AI workflows, and learners consume lessons/topics subject to entitlement policy. Free tier is capped at 3 lessons per rolling 7 days. Admin AI drafts are platform-only, rulebook-grounded, must cite valid applied rule IDs, and fall back to deterministic starter content with an admin warning when unusable.

### Pronunciation and Recalls

Pronunciation is server-authoritative and connected to ASR/provider policy, scoring projection, audio storage, retention, and admin drafting. Scoring never uses random scoring, projects `70/100 == 350/500`, and routes through the ASR provider selector. Published drills require phoneme, label, tips, at least 3 example words, and at least 1 sentence. The learner navigation emphasizes Recalls as the integrated pronunciation/vocabulary recall surface, while legacy pronunciation routes still exist.

### Conversation

Conversation is server-authoritative AI roleplay/communication practice with ASR/TTS provider selection, scenario templates, audio handling, evaluation, review-item seeding, and admin scenario/session management. Evaluation projects mean `4.2/6 == 350/500`, valid task types are `oet-roleplay` and `oet-handover`, and audio flows through the conversation audio service and storage abstraction. Learner availability is still published-content and entitlement dependent; if no scenario types are enabled, the learner surface can validly show an empty state.

### Vocabulary, Lessons, Strategies, and Remediation

These modules expand the platform beyond mock/practice flows into guided learning, flashcards/quizzes, strategy guides, learning paths, remediation, interleaved practice, quick sessions, and recall cards/words/library workflows. Lessons and Strategies routes/services exist, but learner availability is release-flag and published-content dependent.

### Mocks and Readiness

Mocks provide exam-readiness evidence through setup, player, diagnostic, simulation, bookings, speaking-room, report, and admin mock-authoring/operations flows. Readiness aggregates diagnostic, task, mock, review, and blocker signals.

## 6. Mission-Critical Product and Engineering Contracts

These rules are product behavior, not implementation trivia. The verbatim engineering wording (including the AI-gateway `PromptNotGroundedException` enforcement, the `IAiUsageRecorder` one-row-per-call rule, the publish-gate composition for each module, the Reading learner-DTO hard ban, advisory-band projection anchors, and the Statement-of-Results pixel contract) is consolidated in the [Reference Appendix, Section 1](./reference-appendix.md#1-mission-critical-hard-invariants). The bullets below are the product-facing summary; the appendix is the source of truth.

- Scoring: pass/fail and raw-to-scaled logic must route through `lib/scoring.ts` or `OetLearner.Api.Services.OetScoring`; inline threshold comparisons are not the source of truth. Listening/Reading raw-to-scaled conversion anchors `30/42 == 350/500`; Writing pass is 350 for UK/IE/AU/NZ/CA and 300 for US/QA; Speaking pass is always 350.
- Rulebooks: Writing, Speaking, Grammar, Pronunciation, and Conversation enforcement must use rulebook engine APIs.
- AI gateway: every AI call must use grounded prompt construction and usage recording, including provider failures and refusals.
- Content upload: learner content assets must use the `ContentPaper -> ContentPaperAsset -> MediaAsset` model, source provenance, publish gates, audit events, and storage abstraction.
- Result card: the OET Statement of Results card has a fixed design/spec contract and practice disclaimer.
- Reading authoring: learner-facing DTOs must remain safe while exact-match grading and 42-item paper structure stay server-governed.
- Grammar, Pronunciation, and Conversation: these are not static pages; they are server-authoritative modules with admin authoring, AI/provider policy, retention, entitlement, and rulebook constraints.
- Upload/storage: content uploads are scanner-gated, role-size-limited, chunked where required, source-provenance-aware, and fail closed in production when scanner configuration is unsafe.
- Auth/platform: backend privileged APIs enforce authorization policies; portal UI access also depends on route shells/AuthGuard and post-auth role routing.

## 7. System-Wide Functional Inventory

### Learner-facing areas

- onboarding, goals, diagnostics, study plan, dashboard, next actions, predictions
- Writing, Speaking, Reading, Listening, Mocks, submissions/history
- Grammar, Pronunciation, Conversation, Vocabulary, Recalls, Lessons, Strategies, Learning Paths, Remediation, Practice
- Readiness, Progress, Achievements, Certificates, Leaderboard
- Community, peer review, reviews, escalations, ask-an-expert style flows
- Billing, pricing, upgrade, plans, freeze, referral, score guarantee
- Marketplace, private speaking, tutoring, exam booking, test-day and guide surfaces
- Settings, AI settings, reminders, sessions, account/auth/MFA support

### Expert-facing areas

- dashboard, queue, review redirect, Writing review, Speaking review
- learners, schedule, metrics, calibration, speaking calibration
- private speaking, speaking room, mock bookings, messages, ask-an-expert
- compensation, onboarding, AI prefill, annotation templates, mobile review, queue priority, rubric reference, scoring quality

### Sponsor-facing areas

- dashboard
- sponsored learners
- learner invitation and removal
- sponsor billing snapshot

### Admin-facing areas

- operations, alerts, audit logs, SLA health, analytics, BI
- content hub, papers, imports, media, hierarchy, deduplication, generation, publish requests, quality, analytics
- module authoring for Writing, Reading authoring/policy, Speaking mock sets, Listening, Mocks, Grammar, Pronunciation, Conversation, Vocabulary, Recalls, Strategies
- Admin Writing analytics and rule-violation drill-down
- signup catalog, taxonomy mirror, criteria, rulebooks, roles, permissions
- AI config, providers, usage, user usage drill-down, writing AI options/drafts
- review ops, escalations, marketplace review, private speaking, score guarantee claims
- users, imports, experts, community, institutions, enterprise
- billing, wallet tiers, credit lifecycle, free tier, freezes, webhooks, flags, notifications
- payment transactions, refunds, disputes, provider lifecycle signals, webhook monitoring, notification delivery health, and upload/security operations

## 8. Key Business Workflows

### Onboarding to study plan

1. Learner completes onboarding.
2. Learner defines goals, profession, target country, target scores, and study capacity.
3. Learner starts diagnostics.
4. Diagnostic evidence feeds study-plan, readiness, and next-action surfaces.

### Writing attempt to revision and review

1. Learner opens a Writing task.
2. Learner drafts and submits.
3. The system produces a grounded evaluation result.
4. Learner reviews detailed criterion feedback.
5. Learner revises, compares, drills, or requests expert review.
6. Expert review returns structured evidence into history and readiness.

### Speaking attempt to expert or private-speaking support

1. Learner selects a task or booking context.
2. Learner completes device check and records or joins the relevant room.
3. Results, transcript, phrasing, and rulebook evidence are produced.
4. Learner escalates to expert review or uses private-speaking/tutoring flows where entitled.

### Objective-skill attempt to review and analytics

1. Learner opens a Reading paper/practice task or Listening player.
2. Learner submits objective answers.
3. Results expose scores and review data that must be reconciled against PM-001 before release sign-off.
4. Reading feeds paper/practice analytics while answer-key-only review exposure remains PM-001-tracked.
5. Listening feeds transcript-backed review, drills, pathway, and analytics.
6. Admin authoring, policy, and analytics surfaces govern future content quality.

### Content creation to learner availability

1. Admin creates or imports content/paper assets.
2. Required roles, source provenance, rulebook/scoring constraints, and publish gates are validated.
3. Published content becomes available to learner routes and analytics surfaces.
4. Audit, quality, deduplication, staleness, and rollback tools govern later changes.

### Sponsor cohort lifecycle

1. Sponsor signs in and lands on `/sponsor`.
2. Sponsor invites learners or monitors active/pending sponsorships.
3. Learner-linked activity contributes to sponsor-attributable reporting.
4. Admin enterprise/institution/billing surfaces govern the broader sponsor relationship.

## 9. Known Partial or Fragile Areas

- Speaking live AI mode is not active and falls back to self-guided behavior.
- Some diagnostic, mock, and learner service paths still use fixed or legacy content IDs.
- Learner Writing analytics is still seed-data backed until the learner weaknesses endpoint ships.
- Lessons and Strategies routes/services exist, but learner availability is release-flag and published-content dependent.
- Conversation learner availability depends on published scenario/task types and entitlement even though routes and backend services exist.
- Sponsor billing attribution is heuristic until direct sponsor-paid transaction flags exist.
- The legacy media upload surface needs reconciliation with the canonical content-paper upload pipeline.
- Reading review-answer visibility needs reconciliation with the learner-safe DTO invariant.
- Device pairing is currently an H13 scaffold with in-memory single-use codes, not a restart- or multi-replica durable pairing broker.
- Sponsor route protection should be validated explicitly because standard E2E role matrices may not include a sponsor auth state.
- Offline/mobile/desktop packaging exists, but parity and queued replay behavior should be validated per release.
- Some UI copy has encoding artifacts and transitional mock-data language.

## 10. Conclusion

The OET Prep Platform is a four-portal preparation and operations system. Its strongest implemented themes are OET-specific workflow design, strong productive-skill review loops, server-authoritative rulebook/AI/content domains, sponsor and commercial operations, and admin governance across content, quality, billing, and audit.

The remaining weaknesses are mostly documentation drift and transitional implementation details. The manual package now treats those as explicit product facts rather than hiding them inside broad surface summaries.
