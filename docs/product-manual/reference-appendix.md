# Reference Appendix

This file complements the role manuals with reference material that previously was missing or scattered: mission-critical hard invariants, configuration options, background workers, real-time hubs, notification taxonomy, retention windows, the glossary, the admin permission catalog, edge-state contracts, the platform parity matrix, the observability contract, support flows, and the release QA quick start.

Related documents:

- [README](./README.md)
- [Master Product Manual](./master-product-manual.md)
- [Learner App Manual](./learner-app-manual.md)
- [Expert Console Manual](./expert-console-manual.md)
- [Sponsor Portal Manual](./sponsor-portal-manual.md)
- [Admin Dashboard and CMS Manual](./admin-dashboard-cms-manual.md)
- [Cross-System Business Logic and Workflows](./cross-system-business-logic-and-workflows.md)
- [Route, API, and Domain Surface Index](./route-api-domain-surface-index.md)
- [Audit Fact Base](./_audit-fact-base.md)

---

## 1. Mission-Critical Hard Invariants

These invariants come from `AGENTS.md` and the linked domain specs. They are not soft guidelines. Reviewers, release owners, and engineers must treat them as gates.

### 1.1 Scoring

- Listening and Reading raw-to-scaled conversion is anchored at `30/42 ≡ 350/500`.
- Writing pass thresholds depend on country: **350** for UK/IE/AU/NZ/CA and **300** for US/QA.
- Speaking pass threshold is always **350**.
- All scoring access must route through `lib/scoring.ts` (TS) or `OetLearner.Api.Services.OetScoring` (.NET).
- Inline comparisons such as `score >= 350` are forbidden.

### 1.2 Rulebooks

- All Writing/Speaking/Grammar/Pronunciation/Conversation rule enforcement must route through `lib/rulebook` (TS) or `OetLearner.Api.Services.Rulebook` (.NET).
- Canonical rulebook content lives at `rulebooks/<kind>/<profession>/rulebook.v*.json`.
- UI and endpoint code must never read those JSON files directly. Always use the engine API.

### 1.3 AI Gateway

- Every AI invocation must use `buildAiGroundedPrompt()` (TS) or `AiGatewayService.BuildGroundedPrompt()` + `CompleteAsync()` (.NET).
- The .NET gateway physically refuses ungrounded prompts at runtime by throwing `PromptNotGroundedException`. Grounding is enforced, not advisory.
- Adding a provider means implementing `IAiModelProvider`; grounding code is never modified.
- Every AI call (success, provider error, refusal) writes exactly one `AiUsageRecord` row through `IAiUsageRecorder`. Quota and billing correctness depends on this one-call/one-row guarantee.
- Sensitive features (scoring, evaluation, admin drafting) are platform-only and must reject BYOK at the policy layer.
- Feature codes that are platform-only include `AiFeatureCodes.AdminGrammarDraft`, `PronunciationScore`, `PronunciationFeedback`, `ConversationEvaluation`, `AdminConversationDraft`, `AdminPronunciationDraft`. BYOK is not eligible for these features.

### 1.4 Content Upload

- Learner content is `ContentPaper` → `ContentPaperAsset` (typed file slot by `PaperAssetRole`) → `MediaAsset` (physical file, content-addressed by SHA-256).
- All physical I/O must go through `IFileStorage`. Direct `File.*` and `Path.*` writes are prohibited. The same exclusivity applies to conversation and pronunciation audio.
- Publish requires both: every required role-specific primary asset present per `IContentPaperService.RequiredRolesFor`, and a non-empty `SourceProvenance`. Both are independent gates.
- Admin mutations write `AuditEvent` rows.
- ClamAV is the production scanner; NoOp scanning is rejected at startup outside non-production. Scanner errors fail closed by default.

### 1.5 OET Statement of Results card

- The learner-facing card in `components/domain/OetStatementOfResultsCard.tsx` is a pixel-faithful reproduction of the official CBLA Statement of Results.
- Do not restyle. Do not "improve". Do not remove the practice disclaimer.
- Any visual change requires a pixel diff against the reference screenshots in `Project Real Content/Create Similar Table Formats for Results to show to Candidates/`.
- Construction goes through `lib/adapters/oet-sor-adapter.ts`. Do not assemble the shape inline.

### 1.6 Reading

- Canonical paper structure is **20 (Part A) + 6 (Part B) + 16 (Part C) = 42 items**, enforced at the publish gate.
- Grading is exact-match against authored structure, not against AI. Raw-to-scaled goes through `OetScoring.OetRawToScaled` only.
- Learner-facing endpoints must never serialize `CorrectAnswerJson`, `ExplanationMarkdown`, or `AcceptedSynonymsJson`. The projection layer in `ReadingLearnerEndpoints.cs` enforces this.
- Tracked exception: PM-001 documents the current backend ability to emit answer/explanation fields after submit when policy permits, which conflicts with this invariant and must be reconciled before release sign-off.

### 1.7 Grammar

- Canonical rulebook lives at `rulebooks/grammar/<profession>/rulebook.v1.json`.
- AI drafts route through `GrammarDraftService` with grounded prompt `Kind = Grammar`, `Task = GenerateGrammarLesson`, feature `AdminGrammarDraft`. Platform-only — BYOK is refused.
- Every `appliedRuleIds` value in the AI reply must exist in the loaded grammar rulebook. Validation runs server-side; unusable replies fall back to a deterministic starter template with an admin warning.
- Free-tier learners are capped at three lessons per rolling seven-day window via `GrammarEntitlementService` and `/v1/grammar/entitlement`.

### 1.8 Pronunciation

- Canonical rulebook lives at `rulebooks/pronunciation/<profession>/rulebook.v1.json`.
- ASR scoring routes through `IPronunciationAsrProviderSelector`. There is no RNG scoring anywhere.
- Advisory band projection is anchored at `70/100 ≡ 350/500` via `OetScoring.PronunciationProjectedScaled()` or `lib/scoring.ts:pronunciationProjectedScaled()`. Inline comparisons such as `overall >= 70` are forbidden.
- Audio retention is governed by `PronunciationOptions.AudioRetentionDays` and swept by `PronunciationAudioRetentionWorker`.
- Publish gate requires phoneme + label + tips + at least three example words + at least one sentence.

### 1.9 Conversation

- Canonical rulebook lives at `rulebooks/conversation/<profession>/rulebook.v1.json`.
- ASR routes through `IConversationAsrProviderSelector`; TTS routes through `IConversationTtsProviderSelector`.
- Advisory rubric projection is anchored at `mean 4.2/6 ≡ 350/500` via `OetScoring.ConversationProjectedScaled()` or `lib/scoring.ts:conversationProjectedScaled()`. Inline comparisons such as `mean >= 4.2` are forbidden.
- Audio I/O goes through `IConversationAudioService` → `IFileStorage` and is content-addressed by SHA-256.
- Audio retention is governed by `ConversationOptions.AudioRetentionDays` and swept by `ConversationAudioRetentionWorker`.
- Publish gate for `ConversationTemplate` requires title, scenario, role, patient context, **at least three objectives**, duration, and a valid task type (`oet-roleplay` or `oet-handover`).
- Every evaluation seeds `ReviewItem` rows with `SourceType = "conversation_issue"` for rule-cited mistakes.

---

## 2. Configuration Options Reference

These options classes live under `backend/src/OetLearner.Api/Configuration/` and are bound from configuration. They change runtime behavior, security posture, retention, or commercial behavior. Operational owners should review them per environment.

| Options class | Bound section | Owner area | Primary impact |
| --- | --- | --- | --- |
| `AiProviderOptions` | `AiProviders` | AI Platform | Provider catalog, model allowlist, per-feature routing. |
| `AuthOptions` | `Auth` | Auth/Security | JWT secret, refresh token secret, clock skew. |
| `AuthTokenOptions` | `AuthTokens` | Auth/Security | Token lifetimes, rotation cadence, session lifetime. |
| `BillingOptions` | `Billing` | Commerce | Stripe/provider keys, plan/coupon catalog defaults, refund policy. |
| `BootstrapOptions` | `Bootstrap` | Platform | First-run admin seeding and bootstrap toggles. |
| `BrevoOptions` | `Brevo` | Notifications | Transactional email provider credentials. |
| `ConversationOptions` | `Conversation` | Conversation Module | ASR/TTS provider selection, audio retention days, evaluation policy. |
| `DataRetentionOptions` | `DataRetention` | Privacy/Compliance | Per-domain retention windows for attempts, AI bodies, audit, recordings. |
| `ExternalAuthOptions` | `ExternalAuth` | Auth | OAuth/OIDC provider keys, callback URLs, scope/claim mapping. |
| `NotificationProofHarnessOptions` | `NotificationProofHarness` | Admin Notifications | Proof/test harness for delivery validation. |
| `PasswordPolicyOptions` | `PasswordPolicy` | Auth | Length, complexity, breach-list policy, lockout window. |
| `PdfExtractionOptions` | `PdfExtraction` | Content | PDF text extraction worker timeouts, size cap. |
| `PlatformOptions` | `Platform` | Platform | Public app URLs, branding, regional defaults. |
| `PronunciationOptions` | `Pronunciation` | Pronunciation Module | ASR provider, audio retention days, scoring policy. |
| `SmtpOptions` | `Smtp` | Notifications | Direct SMTP fallback for transactional email. |
| `SpeakingComplianceOptions` | `SpeakingCompliance` | Speaking | Recording consent, retention policy, geographic gates. |
| `StorageOptions` | `Storage` | Uploads | Local root path, max upload bytes, audio MIME allowlist, content-upload sub-options (chunk size, ZIP limits, staging TTL). |
| `UploadScannerOptions` | `UploadScanner` | Security | `clamav` vs `noop`, host/port, timeout, fail-closed default. Production refuses NoOp. |
| `WebPushOptions` | `WebPush` | Notifications | VAPID keys, subject, push delivery toggles. |
| `WritingSeedOptions` | `WritingSeed` | Writing Authoring | Seeder cadence and content for sample writing prompts. |
| `ZoomOptions` | `Zoom` | Live Sessions | Zoom credentials for private speaking and live sessions. |

Operational rules:

- Secrets must come from a secret manager or environment variables, never committed to repo.
- Production startup must reject unsafe combinations (NoOp scanner in Production, missing JWT secret).
- Changes to retention or scanner mode must be reviewed by Privacy/Security owners.

---

## 3. Background Workers Reference

These hosted services run inside the API process. Each worker has a defined sweep window and observable side effect.

| Worker | Cadence trigger | Sweeps / produces | Owner area |
| --- | --- | --- | --- |
| `PartitionMaintenanceWorker` | Daily | Postgres partition rotation for high-volume tables. | Platform/DB |
| `DataRetentionWorker` | Daily | Generic retention sweeps per `DataRetentionOptions`. | Privacy |
| `AuthDataRetentionWorker` | Daily | Expired refresh tokens, expired auth challenges, dormant sessions. | Auth |
| `AdminUploadCleanupWorker` | Hourly | Incomplete chunked uploads older than `Storage.ContentUpload.StagingTtlHours`. | Content |
| `ContentTextExtractionWorker` | On enqueue | PDF/text extraction for newly published assets. | Content |
| `ContentStalenessWorker` | Daily | Marks long-unrevised papers as stale for admin review. | Content |
| `WritingSampleSeeder` | Startup/scheduled | Seeds canonical Writing prompts per `WritingSeedOptions`. | Writing |
| `ListeningV2BackfillService` | One-shot/triggered | Backfills Listening v2 schema for legacy papers. | Listening |
| `ListeningAttemptExpireWorker` | Periodic | Expires abandoned Listening attempts past timeout. | Listening |
| `ReadingAttemptExpireWorker` | Periodic | Expires abandoned Reading attempts past timeout. | Reading |
| `SpeakingAudioRetentionWorker` | Daily | Deletes Speaking audio past `SpeakingCompliance` retention. | Speaking |
| `PronunciationAudioRetentionWorker` | Daily | Deletes Pronunciation audio past `PronunciationOptions.AudioRetentionDays`. | Pronunciation |
| `ConversationAudioRetentionWorker` | Daily | Deletes Conversation audio past `ConversationOptions.AudioRetentionDays`. | Conversation |
| `MockBookingReminderWorker` | Periodic | Sends reminders for upcoming Mock and Speaking-Room bookings. | Mocks/Live |
| `WebhookPiiRetentionWorker` | Daily | Redacts PII in stored billing webhook payloads after retention window. | Billing/Privacy |
| `AiCreditRenewalWorker` | Periodic | Renews periodic AI credit grants per plan. | AI/Billing |
| `AiAccountQuotaResetWorker` | Periodic | Resets per-account AI quotas at the configured window boundary. | AI/Billing |
| `AiToolCatalogSeederHostedService` | Startup | Seeds the AI tool catalog rows. | AI Platform |
| `AiVoiceProviderSeeder` | Startup | Seeds voice/TTS provider catalog rows. | AI/Voice |
| `BackgroundJobProcessor` | Continuous | Generic background job queue worker. | Platform |

Release rule: when changing any retention worker cadence or window, update [`docs/product-manual/_audit-fact-base.md`](./_audit-fact-base.md) and the matching `*Options.cs` documentation in this appendix.

---

## 4. SignalR Hubs

### 4.1 `/v1/notifications/hub`

- Authenticated clients connect; the hub joins each client to an account-scoped group keyed by `authAccountId`.
- Server-pushed events include notification feed updates, suppression changes, and admin proof events when enabled.
- Disconnect/reconnect uses the standard SignalR backoff. Clients re-fetch the feed on reconnect rather than relying on missed pushes.

### 4.2 `/v1/conversations/hub`

- Authenticated clients connect for a conversation session.
- Group join is scoped by conversation session id; only the session owner and authorized observers join.
- Server pushes turn-by-turn ASR partials, AI replies, evaluation status transitions, and audio-readiness signals.
- Audio bytes still travel through HTTP to `IConversationAudioService`; the hub carries control and metadata only.

Both hubs follow the same security posture as `/v1/*` REST endpoints (JWT auth, account suspension/deletion checks, CSRF/CSP outside SignalR negotiation).

---

## 5. Notification Catalog

Notifications are produced by domain services and delivered through one or more channels. Categories below are the product-visible groups; exact event keys live in the notification policy catalog under `/v1/admin/notifications`.

| Category | Examples | Channels | Audience |
| --- | --- | --- | --- |
| Account | sign-in alert, MFA changes, password reset, email verification | email, in-app | learner/expert/admin/sponsor |
| Billing | invoice, refund, dispute, payment failure, score-guarantee outcome | email, in-app | learner, sponsor, admin |
| Review lifecycle | review request created/assigned/completed, escalation, SLA risk | email, in-app, SignalR | learner, expert, admin |
| Content | publish requests, staleness alerts, import results | in-app, email | admin, expert |
| Module reminders | mock booking reminder, study commitment, daily streak | email, push, in-app | learner |
| AI usage | quota approaching, kill-switch, fallback applied | email, in-app, admin alert | learner (limited), admin |
| Operations | scanner failure, worker stall, webhook backlog, partition warning | admin alert, email | admin |

Delivery rules:

- All channels respect per-user notification preferences and per-policy admin overrides.
- Push delivery requires a registered subscription/token and active consent.
- Real-time SignalR delivery is account-scoped; suppressed/banned accounts do not receive pushes.
- Frequency caps are enforced before send; see repository memory `notification-frequency-caps`.

---

## 6. Retention and Lifecycle

| Artefact | Default window source | Sweeper | Notes |
| --- | --- | --- | --- |
| Speaking audio | `SpeakingComplianceOptions` | `SpeakingAudioRetentionWorker` | Consent gates apply before storage. |
| Pronunciation audio | `PronunciationOptions.AudioRetentionDays` | `PronunciationAudioRetentionWorker` | Content-addressed by SHA-256. |
| Conversation audio | `ConversationOptions.AudioRetentionDays` | `ConversationAudioRetentionWorker` | Content-addressed by SHA-256. |
| AI request/response bodies | `DataRetentionOptions` (AI sub-window) | `DataRetentionWorker` | Default short window; admin policy can shorten further. |
| Auth refresh tokens / sessions | `AuthTokenOptions`, `DataRetentionOptions` | `AuthDataRetentionWorker` | Suspended accounts have refresh tokens revoked immediately. |
| Billing webhook payloads (PII) | `BillingOptions` retention window | `WebhookPiiRetentionWorker` | Redacts PII after window; raw payload kept for audit minus PII. |
| Reading/Listening attempts (abandoned) | Module timeout policy | `ReadingAttemptExpireWorker`, `ListeningAttemptExpireWorker` | Expiry frees seat and finalizes any partial state. |
| Content upload staging | `Storage.ContentUpload.StagingTtlHours` (default 24h) | `AdminUploadCleanupWorker` | Removes orphaned chunks. |
| Audit events | `DataRetentionOptions` (audit sub-window) | `DataRetentionWorker` | Admin actions are audited even if other data is purged. |

---

## 7. Glossary

- **Profession** — the OET clinical specialty the learner is preparing for (Doctor, Nurse, Pharmacy, etc.). Drives rulebook selection.
- **Sub-test** — one of Listening, Reading, Writing, Speaking. Each has its own scoring contract.
- **Raw score** — the number of correct items (or rubric-based count) before scaling.
- **Scaled score** — the OET 0–500 scale. Pass thresholds vary by country for Writing; Speaking is always 350.
- **Anchor** — the canonical equivalence used by `OetScoring`: `30/42 ≡ 350/500` for Listening/Reading; `70/100 ≡ 350/500` for Pronunciation; `mean 4.2/6 ≡ 350/500` for Conversation.
- **Attempt** — one learner submission against a paper or task, with timing and answer state.
- **Submission** — the immutable saved form of an attempt after submit.
- **Review request** — a record asking an expert to review a productive submission (Writing/Speaking).
- **Paper** — a curated `ContentPaper` containing typed assets (PDF, audio, transcript) and metadata.
- **Task** — an item authored within a paper or as a stand-alone learning unit (e.g. a Writing prompt, a Speaking role-play).
- **Item** — a single answerable unit inside a paper (Reading question, Listening blank, etc.).
- **Role** — a portal-level role: learner, expert, sponsor, admin.
- **Permission** — a granular admin capability key from the admin permission catalog (see Section 8).
- **BYOK** — bring your own AI key; learner-supplied API credentials. Platform-only AI features refuse BYOK regardless of credentials.
- **Rulebook** — the JSON specification under `rulebooks/<kind>/<profession>/` enforcing rule grounding and validation. Read only via the rulebook engine.
- **Source provenance** — non-empty origin metadata required for publishing a content paper.
- **Audit event** — a row written by `AuditEvent` capturing an admin or sensitive mutation for forensics.

---

## 8. Admin Permission Keys

The granular admin permission catalog (`lib/admin-permissions.ts`, mirroring `AdminPermissions` in `AuthEntities.cs`):

- `content:read`
- `content:write`
- `content:publish`
- `content:editor_review`
- `content:publisher_approval`
- `billing:read`
- `billing:write` (legacy superset)
- `billing:refund_write`
- `billing:catalog_write`
- `billing:subscription_write`
- `users:read`
- `users:write`
- `review_ops`
- `quality_analytics`
- `ai_config`
- `feature_flags`
- `audit_logs`
- `system_admin` (implicit super-permission)

Notes:

- `system_admin` satisfies any other permission check.
- Page-level access maps live in the same file (`/admin/...` → required permissions). Add new admin pages and their required permissions together.
- `billing:write` is preserved for legacy compatibility; new code should grant the narrower `billing:refund_write`, `billing:catalog_write`, or `billing:subscription_write` instead.

---

## 9. Edge State and Failure Mode Contract

A consistent failure contract per module helps QA and support classify incidents quickly.

| Module | Timeout | Missing input | Server refusal | Offline | Payment fail |
| --- | --- | --- | --- | --- | --- |
| Listening / Reading | Attempt is auto-expired by worker; learner sees a "session expired, results saved" state. | UI blocks submit until required answers are present (or warns for skip). | Backend returns 4xx with reason; UI surfaces a non-destructive error toast and retains state. | Service worker queues read requests; submit blocks until online. | N/A unless behind paywall; entitlement check upstream. |
| Writing | Auto-save retains last draft; review request is not created. | UI blocks submit; server validates token-budget. | Grading or AI feedback shows a degraded state with retry. | Draft saved locally; sync on reconnect. | Review request gated by entitlement; payment failure surfaces upgrade CTA. |
| Speaking | Recording auto-finalizes if interrupted; consent retained. | Mic permission missing → guided permission UI. | AI live mode unavailable → falls back to self-guided (PM-006). | Local recording held until network returns. | Review or marketplace booking blocked with upgrade CTA. |
| Conversation | Session ends on long disconnect; partial transcript saved. | Missing scenario → empty state with discoverability. | ASR/TTS provider error → degraded mode with reason; session can resume. | Session paused; client reconnects via SignalR backoff. | N/A. |
| Pronunciation | Drill auto-finalizes; latest scored attempt retained. | Missing audio → guided permission UI. | ASR provider error → "scoring unavailable" with retry. | Local audio queued; sync on reconnect. | N/A. |
| Grammar | N/A. | Missing answer → UI block. | AI draft refusal → deterministic fallback with admin warning. | Lesson cached for offline reading. | Free-tier cap enforced; upgrade CTA. |
| Mocks | Section auto-expires per timer; partial data retained. | UI blocks submit. | Backend 4xx; learner receives clear next-step. | Submit blocked offline. | Booking gated by entitlement; payment failure surfaces. |
| Uploads | Staging upload expires after 24h; cleanup removes parts. | Server validates required role assets. | Scanner reject → quarantine; not published. | N/A admin-only. | N/A. |
| Notifications | Delivery retried per channel policy; suppression respected. | N/A. | Provider failure surfaces to admin alerts. | Push deferred until reachable. | N/A. |
| Billing | Webhook retried; provider state authoritative. | UI blocks invalid actions. | Refund/dispute path requires `billing:refund_write`. | N/A — server-driven. | Dispute / refund / score-guarantee paths handle failure outcomes. |

---

## 10. Platform Parity Matrix

This matrix records which features are intended to work on each platform surface. Release QA validates parity per scenario.

| Feature | Web | Desktop (Electron) | Mobile (Capacitor) | Offline replay |
| --- | --- | --- | --- | --- |
| Auth, MFA, password reset | yes | yes | yes | n/a |
| Learner dashboard | yes | yes | yes | partial (cached read) |
| Listening/Reading practice | yes | yes | yes | partial (queued submit) |
| Writing draft + submit | yes | yes | yes | yes (auto-save + sync) |
| Speaking practice (self-guided) | yes | yes | yes | partial (recording held) |
| Speaking AI live mode | n/a | n/a | n/a | n/a (PM-006 unavailable) |
| Conversation sessions | yes | yes | yes | partial (resume via SignalR) |
| Pronunciation drills | yes | yes | yes | partial (audio queued) |
| Grammar lessons | yes | yes | yes | yes (read), no (write while offline) |
| Mocks (full and section) | yes | yes | yes | partial (queued submit) |
| Notifications feed | yes | yes | yes | partial (cached read) |
| Push notifications | n/a (web push only) | n/a | yes (native) | n/a |
| Device pairing | initiator | initiator | redeemer | n/a (PM-009 in-memory) |
| Admin / Sponsor portals | yes | yes | not targeted | no |
| Expert console (full) | yes | yes | review-only on mobile | partial |
| Billing flows | yes | yes | yes | no |

---

## 11. Observability and Audit Contract

- `AuditEvent` rows are written for: admin mutations (content, users, billing, AI config, feature flags, rulebooks), refund/dispute/subscription lifecycle changes, account suspension/deletion, and security-sensitive auth events.
- Structured logs cover request lifecycle, AI gateway grounding decisions, scanner outcomes, worker sweeps, SignalR connect/disconnect, and provider failures. Logs must not contain secrets, tokens, or PII bodies.
- Analytics events go through `POST /v1/analytics/events`. Accepted events include user-initiated UI actions and product engagement signals; sensitive payloads are not accepted.
- AI usage records are independent of logs and are the source of truth for billing/quota.

---

## 12. Support and Incident Response Flows

| Learner-visible failure | Operational owner | Resolution path |
| --- | --- | --- |
| Failed exam outcome under score guarantee | Billing/Sponsor ops | Score-guarantee claim through `/billing/score-guarantee` and `/admin/score-guarantee-claims`. |
| Failed payment | Billing ops | Provider webhook retried; admin can issue manual retry or refund. Learner sees upgrade CTA and dunning. |
| Refund or dispute | Billing ops with `billing:refund_write` | Admin processes refund/dispute, audit event written, learner notified. |
| Upload reject (scanner) | Content ops | File quarantined; admin re-uploads from clean source; not published. |
| Stuck mock booking | Mocks/Live ops | `/admin/mocks/operations` cancels/refunds and notifies learner. |
| AI feature unavailable / kill-switch | AI ops | Kill-switch via AI config; learner sees graceful fallback; admin alert raised. |
| Account suspension / deletion | Auth/Privacy | Sign-in and refresh refused; refresh tokens revoked; data retained per privacy policy until purge. |

---

## 13. Release and QA Quick Start

Before shipping, validate:

- Build and unit tests: `npx tsc --noEmit`, `npm run lint`, `npm test`, `npm run build`, `dotnet test backend/OetLearner.sln`.
- Mission-critical invariants in Section 1, including PM-001 reconciliation status if Reading review changes.
- Per-portal smoke E2E: `npm run test:e2e:smoke` and the role-specific projects in `playwright.config.ts`.
- Platform parity: validate the rows in Section 10 that the release touches.
- Security-sensitive surfaces: scanner mode in target environment, AI gateway grounding refusal still active, refund/dispute permission enforcement, push consent revocation, sponsor-role page protection.
- Notification delivery: send a test through `/v1/admin/notifications` and confirm it reaches the in-app feed and an account-scoped push.
- Operational readiness: worker health, partition status, webhook backlog, scanner host reachable, AI provider health.

When changing a hard invariant in Section 1, update the matching domain doc (`docs/SCORING.md`, `docs/RULEBOOKS.md`, `docs/AI-USAGE-POLICY.md`, `docs/CONTENT-UPLOAD-PLAN.md`, etc.) before merging.
