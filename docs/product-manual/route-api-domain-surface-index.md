# Route, API, and Domain Surface Index

This index is the route/API evidence layer for the product manual package. It is not a replacement for source code or mission-critical engineering specs; it points the manuals at the current implemented surface area.

Audit date: 2026-05-13

## Source Labels

Use these labels when tracing claims in the other manuals:

- `ROUTE-SNAPSHOT-2026-05-13`: `rg --files app -g page.tsx`, 304 page routes
- `NEXT-ROUTE-HANDLERS-2026-05-13`: `rg --files app/api -g route.ts`, 2 route-handler files
- `ENDPOINT-SNAPSHOT-2026-05-13`: endpoint-folder files under `backend/src/OetLearner.Api/Endpoints`, 50 files
- `NAV-LEARNER`: `components/layout/sidebar.tsx`
- `NAV-EXPERT`: `app/expert/layout.tsx`
- `NAV-ADMIN`: `app/admin/layout.tsx`
- `NAV-SPONSOR`: `app/sponsor/layout.tsx`
- `AUTH-ROLE-ROUTES`: `lib/auth-routes.ts`
- `MISSION-CRITICAL-RULES`: `AGENTS.md` and the linked domain specs under `docs/`

## Frontend Route Group Counts

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
| `/marketplace` | 2 |
| `/progress` | 2 |
| `/goals` | 2 |
| `/register` | 2 |
| `/escalations` | 2 |
| `/forgot-password` | 2 |
| `/reset-password` | 2 |
| `/strategies` | 2 |
| `/study-plan` | 2 |
| single-page groups | 28 |

Single-page groups include `/`, `/history`, `/ielts-guide`, `/leaderboard`, `/exam-guide`, `/learning-paths`, `/exam-booking`, `/sign-in`, `/score-calculator`, `/remediation`, `/review`, `/reviews`, `/test-day`, `/referral`, `/onboarding`, `/auth/callback/[provider]`, `/peer-review`, `/pricing`, `/onboarding-tour`, `/readiness`, `/predictions`, `/tutoring`, `/next-actions`, `/terms`, `/verify-email`, `/privacy`, `/freeze`, and `/feedback-guide`.

## Portal Route Coverage

### Learner and public/support routes

The learner product is larger than the core OET loop. Route groups to account for in learner-facing documentation are:

- core preparation: `/`, `/onboarding`, `/goals`, `/diagnostic`, `/study-plan`, `/readiness`, `/progress`, `/next-actions`, `/predictions`
- dashboard aliases: `/dashboard`, `/dashboard/project`, `/dashboard/score-calculator`
- sub-tests: `/writing`, `/speaking`, `/reading`, `/listening`, `/mocks`, `/submissions`
- newer learning modules: `/grammar`, `/pronunciation`, `/conversation`, `/vocabulary`, `/recalls`, `/lessons`, `/strategies`, `/learning-paths`, `/remediation`, `/practice`
  - Lessons and Strategies routes/services exist, but learner availability is release-flag and published-content dependent.
- community and review: `/community`, `/peer-review`, `/review`, `/reviews`, `/escalations`
- commercial and growth: `/billing`, `/pricing`, `/freeze`, `/referral`, `/marketplace`, `/private-speaking`, `/tutoring`, `/exam-booking`, `/achievements`, `/leaderboard`
- guides and account support: `/exam-guide`, `/feedback-guide`, `/test-day`, `/ielts-guide`, `/settings`, auth and MFA routes

Public, auth, and support routes include `/sign-in`, `/register`, `/register/success`, `/forgot-password`, `/forgot-password/verify`, `/reset-password`, `/reset-password/success`, `/verify-email`, `/mfa/challenge`, `/mfa/setup`, `/mfa/recovery`, `/auth/callback/[provider]`, `/terms`, and `/privacy`. `(auth)` is a Next.js route group and is not part of the public URL.

Alias and duplicate-route notes:

- `/dashboard` and `/dashboard/project` re-export the root dashboard experience.
- `/score-calculator` and `/dashboard/score-calculator` are separate implementations, not a single alias.
- `/pricing` is public acquisition/comparison, while `/billing/plans` is the authenticated learner billing-plan surface.
- `/referral` and `/billing/referral` are separate growth/commercial flows and should be checked together during release QA.

Role-guard precision: backend privileged APIs enforce authorization policies. Portal UI access is guarded by route shells/AuthGuard and post-auth role routing. Next middleware enforces protected-page authentication, proxy CSRF/origin checks, and security headers, but should not be described as the sole role-by-path authorization gate.

### Expert routes

Expert documentation must cover review work plus tutor/operations work:

- dashboard, queue, review workspaces, learners, schedule, metrics
- calibration and speaking calibration
- private speaking tutor workspace and speaking room bookings
- mock bookings
- messages and ask-an-expert
- compensation
- onboarding
- AI prefill, annotation templates, mobile review, queue priority, rubric reference, scoring quality

### Admin routes

Admin documentation must treat `/admin` as a large control plane rather than a small CMS:

- overview, alerts, audit logs, SLA health, analytics and BI
- content hub, content papers, chunked imports, media, hierarchy, deduplication, generation, quality, publish requests
- module authoring for writing, reading authoring/policy, speaking mock sets, listening, mocks, grammar, pronunciation, conversation, vocabulary, recalls, strategies
- writing analytics for admin rule-violation dashboards and attempt drill-down
- governance for signup catalog, taxonomy legacy mirror, criteria, rulebooks, roles, permissions, flags, notifications
- AI operations for config, providers, usage, user usage drill-down, tools/escalation support, writing AI options/drafts
- review operations, escalations, marketplace review, private speaking, score guarantee claims
- people, experts, community moderation, institutions, enterprise, users and imports
- billing operations, wallet tiers, credit lifecycle, free tier, freezes, webhooks

`/admin/experts` is a legacy deep link that redirects into user operations for tutor/expert administration; `/admin/users?tab=tutors` is the operational home for that workstream.

### Sponsor routes

Sponsor documentation must cover:

- `/sponsor`: sponsor dashboard
- `/sponsor/learners`: sponsored learner list, invite, remove sponsorship
- `/sponsor/billing`: sponsor-attributable spend and invoice-like rows

## Backend Endpoint Folder Files

Endpoint-folder files discovered under `backend/src/OetLearner.Api/Endpoints`:

The count is 50 files: 49 route-mapping files plus one admin route-builder helper.

- `AdaptiveEndpoints.cs`
- `AdminAlertEndpoints.cs`
- `AdminEndpoints.cs`
- `AdminRouteBuilderExtensions.cs`
- `AiEscalationAdminEndpoints.cs`
- `AiMeEndpoints.cs`
- `AiToolsAdminEndpoints.cs`
- `AiUsageAdminEndpoints.cs`
- `AnalyticsEndpoints.cs`
- `AuthEndpoints.cs`
- `CommunityEndpoints.cs`
- `ContentHierarchyEndpoints.cs`
- `ContentPapersAdminEndpoints.cs`
- `ContentPapersLearnerEndpoints.cs`
- `ContentStalenessEndpoints.cs`
- `ConversationEndpoints.cs`
- `DevicePairingEndpoints.cs`
- `ExpertCompensationEndpoints.cs`
- `ExpertEndpoints.cs`
- `ExpertMessagingEndpoints.cs`
- `GamificationEndpoints.cs`
- `LearnerActionsEndpoints.cs`
- `LearnerEndpoints.cs`
- `LearningContentEndpoints.cs`
- `ListeningAdminAnalyticsEndpoints.cs`
- `ListeningAuthoringAdminEndpoints.cs`
- `ListeningLearnerEndpoints.cs`
- `ListeningV2Endpoints.cs`
- `MarketplaceEndpoints.cs`
- `MediaEndpoints.cs`
- `MockAdminEndpoints.cs`
- `NotificationEndpoints.cs`
- `PredictionEndpoints.cs`
- `PrivateSpeakingEndpoints.cs`
- `PronunciationEndpoints.cs`
- `ReadingAnalyticsAdminEndpoints.cs`
- `ReadingAuthoringAdminEndpoints.cs`
- `ReadingLearnerEndpoints.cs`
- `ReadingPolicyAdminEndpoints.cs`
- `RecallsEndpoints.cs`
- `ReviewItemEndpoints.cs`
- `RulebookAdminEndpoints.cs`
- `RulebookEndpoints.cs`
- `SocialEndpoints.cs`
- `SpeakingCalibrationEndpoints.cs`
- `SponsorEndpoints.cs`
- `VocabularyEndpoints.cs`
- `WritingAnalyticsAdminEndpoints.cs`
- `WritingCoachEndpoints.cs`
- `WritingPdfEndpoints.cs`

## Endpoint Behavior Reference

The following per-folder summaries promote each endpoint family above into a behavior contract instead of a filename. They are intentionally concise; for exact request/response schemas read the matching `*.cs` source. Endpoint families not listed here either are documented inline in the portal manuals or are thin wrappers whose behavior is fully described by their group prefix.

- `AdaptiveEndpoints.cs` — `/v1` (`LearnerOnly`): adaptive practice surface; serves the next-best item or paper based on the learner's profile and submitted attempts.
- `AdminAlertEndpoints.cs` — `/v1/admin/alerts`: lists, dismisses, and routes operational alerts (worker stalls, scanner failures, webhook backlog) into the admin alerts feed.
- `AdminEndpoints.cs` — `/v1/admin`: cross-cutting admin queries (dashboard counts, escalations summary, basic ops) gated by admin permissions.
- `AdminRouteBuilderExtensions.cs` — helper file. Defines `MapAdminGroup`/`Require*` extension wrappers so admin endpoint files can declare their permission policies consistently. Has no routes of its own.
- `AiEscalationAdminEndpoints.cs` — `/v1/admin`: triage queue for AI feature escalations, allowing admins to review refusals, manual overrides, and provider fallbacks.
- `AiMeEndpoints.cs` — `/v1/me/ai`: learner self-service for credentials (BYOK), per-feature preferences, usage history, and credit balance. Validation throttling is applied to credential checks.
- `AiToolsAdminEndpoints.cs` — `/v1/admin/ai-tools`: catalog management for AI tool definitions used by gateway features.
- `AiUsageAdminEndpoints.cs` — `/v1/admin/ai`: per-user and per-feature usage drill-downs sourced from `AiUsageRecord` rows. Source of truth for AI billing/quota analysis.
- `AnalyticsEndpoints.cs` — `/v1/analytics`: write-rate-limited event ingestion (`POST /events`) and read paths for dashboards. Empty/invalid bodies return `204` instead of failing.
- `AuthEndpoints.cs` — `/v1/auth` (anonymous + authenticated): sign-in, refresh, MFA, password reset, email verification, external auth callbacks. Suspension/deletion checks run on every issue/refresh.
- `CommunityEndpoints.cs` — `/v1` (`LearnerOnly`): community/forum threads, posts, reports, and group memberships.
- `ContentHierarchyEndpoints.cs` — `/v1/admin` for write and `/v1` for read: profession/category taxonomy used by content papers and learner discovery.
- `ContentPapersAdminEndpoints.cs` — `/v1/admin/papers`, `/v1/admin/uploads`, `/v1/admin/imports`: full content-paper CRUD, chunked upload (initiate, append, complete), ZIP imports. Publish gate requires required-role assets and non-empty `SourceProvenance`. Every mutation writes `AuditEvent`.
- `ContentPapersLearnerEndpoints.cs` — `/v1/papers`: learner discovery and asset-fetch for published papers.
- `ContentStalenessEndpoints.cs` — `/v1/admin`: list and triage content papers flagged stale by the staleness worker.
- `ConversationEndpoints.cs` — `/v1` (`LearnerOnly`): conversation session lifecycle (start, send turn, evaluate, end). All AI calls grounded; ASR/TTS via provider selectors; audio via `IConversationAudioService`. Pairs with `/v1/conversations/hub`.
- `DevicePairingEndpoints.cs` — `/v1/device-pairing`: `initiate` (authenticated) and `redeem` (anonymous). 6-character 90-second codes via in-memory broker (PM-009).
- `ExpertCompensationEndpoints.cs` — `/v1/expert/compensation`: read-only views of expert payable activity, periods, and rate cards.
- `ExpertEndpoints.cs` — `/v1/expert`: review queues, claim, complete, comment threads, profile.
- `ExpertMessagingEndpoints.cs` — `/v1/expert/messages`: expert↔learner threaded messaging linked to review records.
- `GamificationEndpoints.cs` — `/v1` (`LearnerOnly`): streaks, achievements, points, daily commitment status.
- `LearnerActionsEndpoints.cs` — `/v1/learner/actions`: action ingestion endpoints for in-app learner workflows (commitments, opt-ins, dismissals).
- `LearnerEndpoints.cs` — `/v1/public` (anonymous), `/v1` (`LearnerOnly`), and `/v1/payment/webhooks` (anonymous, signature-verified): learner profile, plan/subscription state, paywalled content gating, and provider payment webhook ingestion. Webhook handlers verify signatures and write to billing tables.
- `LearningContentEndpoints.cs` — `/v1` (`LearnerOnly`): learner-side content lookup endpoints used by lessons/strategies and study-plan flows.
- `ListeningAdminAnalyticsEndpoints.cs` — `/v1/admin/listening`: analytics read paths plus narrow write paths for analytics annotations.
- `ListeningAuthoringAdminEndpoints.cs` — `/v1/admin/papers/{paperId}/listening`: structured authoring (parts, segments, items, audio asset binding) plus a read group used by the authoring UI.
- `ListeningLearnerEndpoints.cs` — `/v1/listening-papers`: legacy/learner-facing Listening practice surface.
- `ListeningV2Endpoints.cs` — `/v1/listening/v2` (learner) plus `/v1/listening/v2/teacher`. The teacher subgroup is a fourth audience surface (teacher in addition to learner/expert/admin) for class management, pathway/curriculum review, and analytics. Document teacher coverage in release notes.
- `MarketplaceEndpoints.cs` — `/v1` (`LearnerOnly`) plus `/v1/admin/marketplace`: learner browsing/booking of marketplace experts and admin moderation/curation.
- `MediaEndpoints.cs` — `/v1/media`: legacy media operations including the 10 MB upload path. New code must use the content-paper upload pipeline (PM-002).
- `MockAdminEndpoints.cs` — three sibling groups: `/v1/admin/mock-bundles` (bundle CRUD), `/v1/admin/mocks` (mock paper authoring/publish), `/v1/admin/mock-bookings` (booking ops, cancel, reschedule, refund). Booking ops respect `MockBookingReminderWorker` cadence.
- `NotificationEndpoints.cs` — `/v1/notifications` (user feed, prefs, push subscriptions) plus `/v1/admin/notifications` (catalog, policy, health, delivery, suppression, test-email, proof trigger).
- `PredictionEndpoints.cs` — `/v1` (`LearnerOnly`): score prediction reads sourced from learner attempt history.
- `PrivateSpeakingEndpoints.cs` — three sibling groups: `/v1/private-speaking` (`LearnerOnly`), `/v1/expert/private-speaking` (`ExpertOnly`), `/v1/admin/private-speaking` (`AdminOnly`). Private speaking sessions, calibration, and admin oversight live together.
- `PronunciationEndpoints.cs` — `/v1` (`LearnerOnly`): drill discovery, attempt scoring through `IPronunciationAsrProviderSelector`, advisory band via `OetScoring.PronunciationProjectedScaled`. Audio retention swept by `PronunciationAudioRetentionWorker`.
- `ReadingAnalyticsAdminEndpoints.cs` — `/v1/admin`: Reading analytics aggregates for content owners.
- `ReadingAuthoringAdminEndpoints.cs` — `/v1/admin/papers/{paperId}/reading`: structured Reading authoring; publish gate enforces 20 + 6 + 16 = 42 items.
- `ReadingLearnerEndpoints.cs` — `/v1/reading-papers`: learner Reading practice. Projection layer must never serialize `CorrectAnswerJson` / `ExplanationMarkdown` / `AcceptedSynonymsJson` to learner-facing payloads. PM-001 captures the unresolved release-blocking conflict.
- `ReadingPolicyAdminEndpoints.cs` — `/v1/admin/reading-policy`: policy configuration for Reading retry, timer, explanation visibility, and AI extraction.
- `RecallsEndpoints.cs` — `/v1` (`LearnerOnly`) plus `/v1/admin/recalls` (`AdminContentWrite`): student-recall corpus browse + admin curation. See repo memory `recalls-vocabulary-import-hardening` and `recalls-year-classification`.
- `ReviewItemEndpoints.cs` — `/v1` (`LearnerOnly`): per-learner review-item queue (Speaking/Writing/Reading/Conversation issues seeded with `SourceType` from each module).
- `RulebookAdminEndpoints.cs` — `/v1/admin/rulebooks`: CMS for canonical rulebook JSON. Mission-critical: edits are versioned and re-grounded.
- `RulebookEndpoints.cs` — `/v1/rulebooks` (`RulebookReader`): rulebook reads for clients that consume rulebook data through the engine.
- `SocialEndpoints.cs` — `/v1` (`LearnerOnly`): social graph features (follow/unfollow, activity).
- `SpeakingCalibrationEndpoints.cs` — `/v1/expert/calibration/speaking` and `/v1/expert/speaking/attempts`: expert calibration drills and per-attempt comments for inter-rater reliability.
- `SponsorEndpoints.cs` — `/v1/sponsor`: sponsor portal API surface (learner roster, exam-status, billing snapshot, invitations).
- `VocabularyEndpoints.cs` — `/v1` (`LearnerOnly`): vocabulary lists, drills, learner progress.
- `WritingAnalyticsAdminEndpoints.cs` — `/v1/admin/writing/analytics`: rule-violation analytics for Writing.
- `WritingCoachEndpoints.cs` — `/v1` (`LearnerOnly`): grounded AI writing coach interactions; routed through the AI gateway with feature codes for writing assistance.
- `WritingPdfEndpoints.cs` — Writing PDF generation/download surface.

## Configuration, Workers, and Hubs Reference

These surfaces are inventoried in [`reference-appendix.md`](./reference-appendix.md):

- Configuration options classes (21) → [Section 2](./reference-appendix.md#2-configuration-options-reference).
- Background workers (20 hosted services) → [Section 3](./reference-appendix.md#3-background-workers-reference).
- SignalR hubs (`/v1/notifications/hub`, `/v1/conversations/hub`) → [Section 4](./reference-appendix.md#4-signalr-hubs).
- Notification catalog → [Section 5](./reference-appendix.md#5-notification-catalog).
- Retention windows → [Section 6](./reference-appendix.md#6-retention-and-lifecycle).
- Admin permission keys → [Section 8](./reference-appendix.md#8-admin-permission-keys).

When adding or renaming an options class, worker, or hub, update both this section's source label list (in the Source Labels above) and the appendix.

## Non-Module Runtime API Surfaces

Program startup also registers runtime surfaces outside the endpoint-folder file list:

- `/health/live`: process liveness.
- `/health`: database connectivity.
- `/health/ready`: database readiness, storage writeability, and stuck-job warning checks.
- `/v1/notifications/hub`: SignalR notification hub; authenticated clients join account-scoped groups.
- `/v1/conversations/hub`: SignalR conversation hub for real-time conversation surfaces.

Next.js route handlers under `app/api` are also part of the runtime contract:

- `/api/health`: web container health response with `status`, `service`, and timestamp.
- `/api/backend/[...path]`: Node runtime proxy for backend `/v1/*` calls. It supports the main HTTP verbs, validates origin/CSRF for proxied requests, sanitizes request/response headers, validates proxy paths, and returns empty `204` responses for empty analytics event bodies.

Platform bridge surfaces to remember in release planning:

- service worker and PWA manifest surfaces
- Capacitor runtime, native secure storage, push, deep-link handling, and offline queue/cache code
- Electron runtime API-target resolution
- device-pairing deep links such as `/pair?code=...`
- shared `/api/backend` parity path for web/runtime clients

Security, auth, and throttling policies include per-user read/write rate limits, AI credential validation throttling, auth brute-force and refresh protections, JWT validation with short clock skew, account deletion/suspension checks, verified-email gates where required, SignalR token exceptions, CSRF/CSP protections, and granular admin permission policies.

Background worker families include retention workers, upload cleanup, content text extraction, attempt expiry, AI credit/quota renewal, billing webhook PII retention, partition maintenance, reminder workers, and seeders/backfill workers.

Notification surfaces include user feed/read/preference APIs, push subscription/token/consent APIs, admin catalog/policy/health/delivery/consent/suppression/test-email/proof-trigger APIs, and real-time delivery through account-scoped SignalR groups.

Learner AI self-service surfaces live under `/v1/me/ai`: credentials, usage, preferences, and credits. They are user-derived from JWT identity, include BYOK credential custody and validation throttling, support per-feature preference overrides and platform fallback behavior, and expose quota/credit visibility.

Analytics ingestion uses `POST /v1/analytics/events`: authenticated, write-rate-limited, tolerant of empty invalid bodies as `204`, and stores accepted non-empty event names with properties JSON and user identity.

Device pairing is an H13 scaffold: authenticated `/v1/device-pairing/initiate`, anonymous `/v1/device-pairing/redeem`, 6-character 90-second single-use codes, and an in-memory broker that is not a multi-replica or restart-durable pairing store.

Upload and storage contracts include per-role content upload limits, 8 MB chunks, 24-hour staging cleanup, ZIP entry/count/uncompressed/compression-ratio limits, ClamAV versus NoOp scanner modes, production fail-fast when NoOp scanning is configured, fail-closed scanner errors by default, and quarantine behavior before publication.

Billing operations include payment transactions, refunds, disputes, provider lifecycle signals, webhook event monitoring, summaries, and retry/support actions in addition to plan, coupon, wallet, freeze, credit, and entitlement management.

## Mission-Critical Domain Contracts

The product manuals must not dilute these engineering contracts:

- OET scoring: pass/fail and raw-to-scaled logic route through `lib/scoring.ts` or `OetLearner.Api.Services.OetScoring`.
- Rulebooks: Writing, Speaking, Grammar, Pronunciation, and Conversation rule enforcement uses rulebook engine APIs, not direct UI/endpoint JSON reads.
- AI gateway: every AI invocation is grounded and recorded through the AI gateway/usage policy path.
- Content upload: learner content assets use `ContentPaper -> ContentPaperAsset -> MediaAsset`, source provenance, publish gates, audit events, and storage abstraction.
- Reading authoring: canonical 42-item structure and exact-match grading are server-governed; learner DTOs must stay safe.
- Statement of Results: the learner result card has a fixed design contract and practice disclaimer.
- Grammar, Pronunciation, and Conversation: server-authoritative modules with admin drafting, learner delivery, rulebooks, and policy/provider boundaries.

## Manual Issue Register

These findings remain explicit so product, QA, and engineering do not mistake surface inventory for full release closure.

| ID | Area | Current limitation | Next verifier or trigger |
| --- | --- | --- | --- |
| PM-001 | Reading review | Backend can emit `CorrectAnswer` and `ExplanationMarkdown` after submit when policy permits it, while the mission-critical learner-safe DTO invariant says answer-key-only data must not leak. This is an unresolved spec/code decision, not a benign wording issue. | Reading release owner before learner review changes |
| PM-002 | Media/content upload | The legacy admin media page posts to `/v1/media/upload` with a 10 MB limit while the canonical content-paper upload pipeline uses typed assets, chunking, scanner policy, source provenance, and publish gates. | Content/media release owner |
| PM-003 | Writing analytics | Learner `/writing/analytics` uses deterministic seed weakness data until a learner weaknesses API ships; admin Writing rule-violation analytics are implemented separately. | Writing analytics release owner |
| PM-004 | Sponsor billing | Sponsor billing is heuristic until sponsor-paid transaction attribution exists. | Billing/sponsor release owner |
| PM-005 | Legacy IDs | Some learner diagnostic, mock, and service flows still use fixed or legacy task IDs. | Release QA for dynamic content publication |
| PM-006 | Speaking AI mode | Speaking live AI mode remains unavailable and falls back to self-guided behavior. | Speaking/conversation release owner |
| PM-007 | Lessons and Strategies | Routes/services exist, but learner availability remains release-flag and published-content dependent. | Content release owner |
| PM-008 | Conversation availability | Routes and server-authoritative services exist, but learner availability depends on published scenario/task types and entitlement; empty-state behavior is valid. | Conversation release owner |
| PM-009 | Device pairing | The scaffold uses in-memory 90-second codes and is not restart- or multi-replica durable; redeem is anonymous and should be rate-limit verified before production reliance. | Mobile/desktop release owner |
| PM-010 | Role-guard validation | Backend APIs enforce policies, while portal page protection also relies on AuthGuard/post-auth routing; sponsor role E2E coverage is not part of the standard role matrix documented in AGENTS. | Auth/QA owner |
| PM-011 | Source-stat drift | Product-manual route/API counts are newer than older static key stats in repo instruction files. | Documentation maintainer when refreshing repo instructions |
| PM-012 | Platform parity | Web, desktop, mobile, service-worker, offline-sync, and native integrations exist, but queued replay and parity need release-scenario validation. | Release QA owner |
| PM-013 | UI copy encoding | Some UI copy has visible encoding artifacts and transitional mock-data language. | Product/content QA owner |
| PM-014 | Per-module deep dives | Role manuals (`learner-app-manual.md`, `expert-console-manual.md`, `admin-dashboard-cms-manual.md`) document module behavior in narrative form rather than per-page numbered sections for every surface. The Reference Appendix Edge-State Contracts (§9) and the Endpoint Behavior Reference above keep contract coverage complete; the role manuals will be expanded into numbered sections in a follow-up wave without changing semantics. | Documentation maintainer |
| PM-015 | Listening v2 teacher subgroup | `ListeningV2Endpoints.cs` exposes a `/v1/listening/v2/teacher` subgroup. Teacher coverage is documented in the Endpoint Behavior Reference above and in the Platform Parity Matrix in the Reference Appendix, but the role-matrix in the master manual still lists four primary roles (learner, expert, sponsor, admin); teacher is currently a Listening v2 sub-audience rather than a portal. | Listening v2 release owner |
| PM-016 | Reading PM-001 reconciliation | The AGENTS hard ban (no `CorrectAnswerJson` / `ExplanationMarkdown` / `AcceptedSynonymsJson` to learners) is now stated verbatim in `cross-system-business-logic-and-workflows.md`, `learner-app-manual.md`, `_audit-fact-base.md`, and the Reference Appendix. PM-001 remains open until the backend code path is reconciled. | Reading release owner |
