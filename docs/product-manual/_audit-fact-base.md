# OET Platform Audit Fact Base

This file is the working evidence base used to draft and refresh the product-manual package. It is intentionally operational and source-oriented rather than polished.

Audit date: 2026-05-13

## Source Labels

- `ROUTE-SNAPSHOT-2026-05-13`: `rg --files app -g page.tsx`, 304 page routes
- `NEXT-ROUTE-HANDLERS-2026-05-13`: `rg --files app/api -g route.ts`, 2 route-handler files
- `ENDPOINT-SNAPSHOT-2026-05-13`: backend endpoint-folder files under `backend/src/OetLearner.Api/Endpoints`, 50 files
- `NAV-LEARNER`: `components/layout/sidebar.tsx`
- `NAV-EXPERT`: `app/expert/layout.tsx`
- `NAV-ADMIN`: `app/admin/layout.tsx`
- `NAV-SPONSOR`: `app/sponsor/layout.tsx`
- `AUTH-ROLE-ROUTES`: `lib/auth-routes.ts`
- `MISSION-CRITICAL-RULES`: `AGENTS.md` and linked specs in `docs/`

## Source of Truth Used

- Frontend routes under `app/`
- Frontend route handlers under `app/api/`
- Learner navigation in `components/layout/sidebar.tsx`
- Expert, admin, and sponsor navigation in their route `layout.tsx` files
- Frontend integration and domain contracts in `lib/api.ts`, `lib/auth-routes.ts`, and product-specific API helpers
- Backend endpoint-folder files under `backend/src/OetLearner.Api/Endpoints`
- Backend startup/runtime surfaces in `Program.cs`, configuration options, hosted workers, hubs, storage, and scanner wiring
- Mission-critical docs for scoring, rulebooks, AI usage, content upload, Reading authoring, Grammar, Pronunciation, Conversation, and OET result cards
- Existing product-manual files under `docs/product-manual/`

## Role Model

- `learner`
- `expert`
- `sponsor`
- `admin`

Role defaults are defined in `lib/auth-routes.ts`:

- learner -> `/`
- expert -> `/expert`
- sponsor -> `/sponsor`
- admin -> `/admin`

Backend privileged APIs enforce role policies. Portal UI access is guarded by route shells/AuthGuard and post-auth role routing. Next middleware enforces protected-page authentication, proxy CSRF/origin controls, and security headers; it should not be described as the sole role-by-path authorization gate.

## Route Matrix Summary

The current app contains 304 page routes. The most important correction from the previous manual is that the platform is a four-portal product plus public/support and platform route groups.

### Route group counts

| Route group | Page count |
| --- | ---: |
| `/admin` | 105 |
| `/expert` | 26 |
| `/writing` | 16 |
| `/speaking` | 15 |
| `/listening` | 11 |
| `/mocks` | 9 |
| `/diagnostic` | 8 |
| `/community` | 7 |
| `/vocabulary` | 6 |
| `/reading` | 6 |
| `/billing` | 5 |
| `/settings` | 5 |
| `/lessons` | 5 |
| `/recalls` | 4 |
| `/pronunciation` | 3 |
| `/practice` | 3 |
| `/dashboard` | 3 |
| `/private-speaking` | 3 |
| `/achievements` | 3 |
| `/grammar` | 3 |
| `/sponsor` | 3 |
| `/conversation` | 3 |
| `/submissions` | 3 |
| `/mfa` | 3 |
| all other groups | 46 |

### Learner product routes and domains

Core preparation routes:

- `/`
- `/onboarding`
- `/goals`
- `/goals/study-commitment`
- `/diagnostic/*`
- `/study-plan`
- `/study-plan/drift`
- `/dashboard`
- `/dashboard/project`
- `/dashboard/score-calculator`
- `/readiness`
- `/progress`
- `/progress/comparative`
- `/next-actions`
- `/predictions`
- `/remediation`
- `/learning-paths`

Sub-test and mock routes:

- `/writing/*`
- `/speaking/*`
- `/reading/*`
- `/listening/*`
- `/mocks/*`
- `/submissions/*`

Learning and language-support routes:

- `/grammar`, `/grammar/[lessonId]`, `/grammar/topics/[slug]`
- `/pronunciation`, `/pronunciation/[drillId]`, `/pronunciation/discrimination/[drillId]`
- `/conversation`, `/conversation/[sessionId]`, `/conversation/[sessionId]/results`
- `/vocabulary/*`
- `/recalls/*`
- `/lessons/*`
- `/strategies/*`
- `/practice/*`

Community, growth, and commercial routes:

- `/community/*`
- `/peer-review`
- `/review`
- `/reviews`
- `/escalations/*`
- `/marketplace/*`
- `/private-speaking/*`
- `/tutoring`
- `/exam-booking`
- `/billing/*`
- `/pricing`
- `/freeze`
- `/referral`
- `/achievements/*`
- `/leaderboard`

Support and guide routes:

- `/exam-guide`
- `/feedback-guide`
- `/test-day`
- `/ielts-guide`
- `/settings/*`
- auth, MFA, terms, privacy, and callback routes

Public/auth/support paths include sign-in, register, forgot/reset password, email verification, MFA challenge/setup/recovery, auth callback, terms, and privacy. `(auth)` is a Next.js route group and is stripped from public URLs.

Alias and duplicate-route notes:

- `/dashboard` and `/dashboard/project` re-export the root dashboard.
- `/score-calculator` and `/dashboard/score-calculator` are separate implementations.
- `/pricing` is public, while `/billing/plans` is authenticated.
- `/referral` and `/billing/referral` are separate referral/growth paths.

### Expert routes

The expert route tree includes 26 pages. It is broader than review queue/workspace only:

- `/expert`
- `/expert/onboarding`
- `/expert/queue`
- `/expert/queue-priority`
- `/expert/review/[reviewRequestId]`
- `/expert/review/writing/[reviewRequestId]`
- `/expert/review/speaking/[reviewRequestId]`
- `/expert/calibration`
- `/expert/calibration/[caseId]`
- `/expert/calibration/speaking`
- `/expert/metrics`
- `/expert/scoring-quality`
- `/expert/schedule`
- `/expert/learners`
- `/expert/learners/[learnerId]`
- `/expert/private-speaking`
- `/expert/speaking-room/[bookingId]`
- `/expert/mocks/bookings`
- `/expert/messages`
- `/expert/messages/[threadId]`
- `/expert/compensation`
- `/expert/ask-an-expert`
- `/expert/ai-prefill`
- `/expert/annotation-templates`
- `/expert/mobile-review`
- `/expert/rubric-reference`

### Sponsor routes

The sponsor route tree includes 3 pages:

- `/sponsor`
- `/sponsor/learners`
- `/sponsor/billing`

### Admin routes

The admin route tree includes 105 pages. It should be documented by workstream, not as a short flat CMS list:

- overview, alerts, audit logs, SLA health
- quality, reading, listening, content effectiveness, subscription health, expert efficiency, cohort analytics, and BI
- content hub, content workspace, revisions, library, papers, imports, media, hierarchy, deduplication, generation, quality, analytics, publish requests
- Writing, Reading authoring/policy, Speaking mock sets, Listening, Mocks, Grammar, Pronunciation, Conversation, Vocabulary, Recalls, and Strategies authoring
- Admin Writing rule-violation analytics
- signup catalog, taxonomy legacy route, criteria, rulebooks, roles, permissions
- AI config, AI providers, AI usage, AI usage user drill-down, writing AI options, writing AI draft
- review ops, escalations, marketplace review, private speaking, private speaking calibration, score-guarantee claims
- users, user import, user detail, experts, community, institutions, enterprise
- billing, wallet tiers, credit lifecycle, free tier, subscription freezes, webhooks, flags, notifications
- payment transactions, refunds, disputes, provider lifecycle signals, webhook monitoring, and retry/support operations

`/admin/experts` is a legacy redirect into user operations for tutor/expert management.

## Backend Endpoint Inventory

Endpoint-folder files discovered under `backend/src/OetLearner.Api/Endpoints`:

The count is 50 files: 49 route-mapping files plus one admin route-builder helper.

- Adaptive, Admin, Admin Alerts, AI Escalations, AI Me, AI Tools, AI Usage, Analytics, Auth
- Community, Content Hierarchy, Content Papers Admin, Content Papers Learner, Content Staleness
- Conversation, Device Pairing, Expert, Expert Compensation, Expert Messaging
- Gamification, Learner, Learner Actions, Learning Content
- Listening Admin Analytics, Listening Authoring Admin, Listening Learner, Listening V2
- Marketplace, Media, Mock Admin, Notifications, Predictions, Private Speaking
- Pronunciation, Reading Analytics Admin, Reading Authoring Admin, Reading Learner, Reading Policy Admin
- Recalls, Review Items, Rulebook, Rulebook Admin, Social, Speaking Calibration, Sponsor, Vocabulary, Writing Analytics Admin, Writing Coach, Writing PDF

The full endpoint filename list is maintained in [Route, API, and Domain Surface Index](./route-api-domain-surface-index.md).

## Surface Feature Matrix

### Learner App

- Onboarding, goals, diagnostics, dashboard, study plan, next actions, predictions, readiness, progress: `implemented`
- Writing workflow: `implemented`, with player, result, feedback, revision, model answer, expert request, drills, compare, phrase suggestions, rulebook views, PDF/coach support
- Learner Writing analytics: `partial`; the route exists and renders deterministic seed weakness data until a learner weaknesses API ships
- Speaking workflow: `implemented` overall, `partial` for live AI mode fallback; includes task selection, device check, role cards, task recording, results, transcript, phrasing, mocks, fluency timeline, rulebook, expert review
- Reading workflow: `implemented`; includes classic player/results plus paper/practice routes and backend authoring/policy/analytics
- Listening workflow: `implemented`; includes player, results, review, drills, pathway, curriculum, classes, test rules, analytics and admin authoring
- Grammar: `implemented`; server-authoritative lesson/topic module with admin authoring and AI drafting
- Pronunciation/Recalls: `implemented`; pronunciation routes still exist while learner nav emphasizes Recalls as the integrated recall/pronunciation surface
- Conversation: `partial`; routes and server-authoritative services exist, but learner availability depends on published scenarios/task types and entitlement
- Vocabulary: `implemented`; browse, terms, flashcards, quiz, quiz history, admin import/drafting
- Community and social review: `implemented`; includes threads, groups, ask-an-expert, peer review, reviews, escalations
- Commercial/growth: `implemented`; billing, plans, upgrade, score guarantee, referral, pricing, marketplace, private speaking, tutoring, exam booking, freezes
- Achievements/gamification: `implemented`; achievements, certificates, leaderboard, gamification endpoints
- Settings and account support: `implemented`; settings sections, AI settings, reminders, sessions, auth, MFA, verification, password reset, terms/privacy

### Expert Console

- Dashboard, queue, review workspaces, learners, calibration, metrics, schedule: `implemented`
- Additional operations: `implemented`; private speaking, messages, compensation, onboarding, mock bookings, speaking room, speaking calibration, AI prefill, annotation templates, mobile review, queue priority, rubric reference, scoring quality, ask-an-expert
- Artifact dependency states: `partial`; review workspaces depend on transcript/audio/AI artifact readiness

### Sponsor Portal

- Dashboard: `implemented`
- Sponsored learners, invitations, removal: `implemented`
- Billing: `partial`; sponsor-attributable spend is heuristic until direct sponsor-paid transaction attribution exists

### Admin Dashboard / CMS

- Core operations, content, users, billing, flags, notifications, audit: `implemented`
- Expanded content authoring: `implemented`; content papers/imports/media/hierarchy/dedup/generation plus Writing, Reading authoring/policy, Speaking mock sets, Listening, Mocks, Grammar, Pronunciation, Conversation, Vocabulary, Recalls, Strategies
- Admin Writing analytics: `implemented`; rule-violation summary and attempt drill-down endpoints are exposed under `/v1/admin/writing/analytics`
- AI governance: `implemented`; config, providers, usage, user drill-down, writing AI options/drafts, tool/escalation support
- Learner AI self-service: `implemented`; `/v1/me/ai` credentials, usage, preferences, credits, BYOK custody, validation throttling, fallback preferences, and quota/credit visibility
- Review and quality operations: `implemented`; review ops, escalations, SLA health, marketplace review, private speaking, score-guarantee claims, analytics
- Enterprise and sponsor-adjacent operations: `implemented`; institutions, enterprise, sponsor portal, user/billing governance
- Upload/storage security: `implemented`; role-size limits, 8 MB chunks, 24-hour staging cleanup, ZIP safety limits, ClamAV/NoOp scanner selection, fail-closed default, production NoOp fail-fast, quarantine before publication
- Notification delivery: `implemented`; feed/read/preferences, push subscription/token/consent, admin policy/health/delivery/suppression/test/proof APIs, account-scoped SignalR groups

## Runtime and Platform Surfaces

- Next route handlers: `/api/health` and `/api/backend/[...path]`.
- Backend health: `/health/live`, `/health`, `/health/ready`.
- Real-time hubs: `/v1/notifications/hub`, `/v1/conversations/hub`.
- Analytics ingestion: `POST /v1/analytics/events`, authenticated and write-rate-limited, tolerant of empty invalid bodies as `204`.
- Device pairing: authenticated initiate, anonymous redeem, 6-character 90-second single-use in-memory code scaffold.
- Background workers: retention, content extraction, upload cleanup, attempt expiry, AI credit/quota renewal, billing webhook PII retention, partition maintenance, reminders, seeders/backfills.
- Mobile/desktop/PWA bridge: service worker/offline code, Capacitor secure storage/push/deep links/offline queue, Electron runtime API target resolution, and shared proxy parity.

## Mission-Critical Contracts To Preserve In Manuals

The verbatim engineering wording is held in the [Reference Appendix, Section 1](./reference-appendix.md#1-mission-critical-hard-invariants); this section keeps a working summary that must not contradict it.

- Scoring routes through `lib/scoring.ts` or `OetLearner.Api.Services.OetScoring`.
- Rulebook enforcement routes through rulebook engines, not direct JSON access from UI or endpoint code.
- AI calls route through grounded gateway and usage recording (`PromptNotGroundedException` physically refuses ungrounded prompts; `IAiUsageRecorder` writes one row per call including failures and refusals).
- Content upload uses `ContentPaper -> ContentPaperAsset -> MediaAsset`, source provenance, publish gates, audit events, and storage abstraction.
- Reading authoring remains exact-match with canonical 42-item structure. The AGENTS hard ban is that learner-facing endpoints must never serialize `CorrectAnswerJson`, `ExplanationMarkdown`, or `AcceptedSynonymsJson`. PM-001 tracks the unresolved backend conflict.
- Grammar, Pronunciation, and Conversation remain server-authoritative modules with rulebook-grounded AI drafting and provider-selector enforcement.
- The Statement of Results card remains bound to its design/spec contract and practice disclaimer.

## Observed Partial, Transitional, or Follow-Up Areas

- Some diagnostic, mock, and learner service flows still rely on fixed or legacy task IDs.
- Speaking `ai` mode is not functionally available and is downgraded to self-guided behavior.
- Sponsor billing is currently heuristic.
- Learner Writing analytics currently uses deterministic seed data until a learner-specific backend endpoint ships.
- Reading review output: backend can currently expose `CorrectAnswer` and `ExplanationMarkdown` after submit when policy permits, in conflict with the AGENTS hard ban. PM-001 must be reconciled before release sign-off.
- The legacy admin media upload route and canonical content-paper upload pipeline coexist and need release-level validation.
- Service-worker/offline/mobile/desktop support exists, but queued replay and parity should be validated by scenario.
- UI copy includes visible encoding artifacts on several pages.
- Lessons and Strategies routes/services exist, but learner availability is release-flag and published-content dependent.
- Billing and notification governance are product surfaces, but exact external-provider contracts should be traced per integration release.
- Device pairing is an in-memory scaffold and needs durability/rate-limit review before production reliance.
- Sponsor-role page access needs explicit QA coverage because the standard role matrix may omit sponsor state.
- Product-manual route/API counts are newer than older static key stats in repository instruction files.
