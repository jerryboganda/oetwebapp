# OET Platform Architecture Audit

Audit status: complete. Secrets and config values are intentionally omitted; only key names and code paths are recorded.

Evidence standard used in this report:

- File references use `path:line` or `path:line-line`.
- If code was not found, the report says `Not found in codebase`.
- External services are counted when a provider, package, config key, route, or service implementation exists in the repo.
- This audit is read-only except for writing this Markdown report as requested.

## 1. Project Shape

### 1.1 Stack and runtime facts

| Finding | Evidence | Status |
| --- | --- | --- |
| F-001: The frontend is a Next.js 15 app using React 19 and TypeScript 5.9. | `package.json:105`, `package.json:108`, `package.json:195` | Verified |
| F-002: The frontend build is configured for standalone output. | `next.config.ts:28` | Verified |
| F-003: Next build currently ignores TypeScript and ESLint errors during build. | `next.config.ts:23-24` | Verified |
| F-004: CSP is emitted from middleware rather than static Next headers. | `next.config.ts:5`, `next.config.ts:70` | Verified |
| F-005: Capacitor, Electron, SignalR, Sentry, and Zod are direct dependencies. | `package.json:60`, `package.json:73`, `package.json:89`, `package.json:118`, `package.json:187` | Verified |
| F-006: The backend targets .NET 10. | `backend/src/OetLearner.Api/OetLearner.Api.csproj:4` | Verified |
| F-007: The backend uses EF Core 10 with PostgreSQL plus SQLite/InMemory test providers. | `backend/src/OetLearner.Api/OetLearner.Api.csproj:14-20` | Verified |
| F-008: The backend embeds `rulebooks/**/*.json` as resources. | `backend/src/OetLearner.Api/OetLearner.Api.csproj:53-61` | Verified |
| F-009: The backend has direct package support for S3, Azure AI inference, Sentry, QuestPDF, PDF extraction, and HTML sanitization. | `backend/src/OetLearner.Api/OetLearner.Api.csproj:10-11`, `backend/src/OetLearner.Api/OetLearner.Api.csproj:26`, `backend/src/OetLearner.Api/OetLearner.Api.csproj:37`, `backend/src/OetLearner.Api/OetLearner.Api.csproj:42`, `backend/src/OetLearner.Api/OetLearner.Api.csproj:49` | Verified |
| F-010: Repomix config labels the stack as Next.js 15 frontend, ASP.NET Core 10 backend, Electron desktop, and Capacitor mobile. | `repomix.config.json:9` | Verified from earlier read |

### 1.2 Top-level source shape

Concise 3-level source tree excerpt, excluding `node_modules`, `.git`, generated reports, build outputs, large media, and transient workspace folders:

```text
app/
  (auth)/, admin/, api/, billing/, conversation/, dashboard/, diagnostic/, expert/, listening/, mocks/, pronunciation/, reading/, speaking/, writing/
backend/
  src/OetLearner.Api/
  tests/OetLearner.Api.Tests/
components/
  auth/, domain/, layout/, ui/
contexts/
hooks/
lib/
  mobile/, network/, rulebook/, adapters/
docs/
electron/
scripts/
tests/
  e2e/
rulebooks/
  conversation/, grammar/, listening/, pronunciation/, reading/, remediation/, speaking/, vocabulary/, writing/
types/
pages/
public/
android/
ios/
capacitor-web/
ops/
```

### 1.3 Repository size facts

| Metric | Value | Source |
| --- | ---: | --- |
| Tracked files | 3,481 | `git ls-files` local read-only stats command |
| Tracked text LOC | 1,154,702 | Corrected local read-only LOC command |
| Top tracked extensions | `.cs=1059`, `.tsx=1029`, `.ts=343`, `.md=305`, `.sh=172`, `.json=156` | `git ls-files` local read-only stats command |

The first LOC attempt returned `7` because the counting expression was wrong. It is intentionally ignored. The corrected value above uses file-by-file text line counting over tracked files.

### 1.4 Configuration surfaces

Values are intentionally omitted.

| Surface | Key groups observed | Evidence |
| --- | --- | --- |
| Frontend/env examples | `APP_URL`, `NEXT_PUBLIC_API_BASE_URL`, mobile app-store URLs, AI provider keys, Cloudflare gateway keys, LiveKit keys, Anthropic/OpenAI/ElevenLabs/Whisper/AWS keys, speaking compliance flags | `.env.example:1-220` from earlier read |
| Staging env example | Postgres, public URLs, auth token lifetimes, Brevo/SMTP, Stripe, Sentry, upload scanner, AI defaults, Docker network | `.env.staging.example:1-160` from earlier read |
| Backend appsettings | `ConnectionStrings`, `Auth`, `AuthTokens`, `Bootstrap`, `Platform`, `ExternalAuth`, `Billing`, `Storage`, `Proxy`, `Features`, `AI`, `LiveKit`, `Logging`, `AllowedHosts` | `backend/src/OetLearner.Api/appsettings.json:1-220` from earlier read |

## 2. Feature Inventory

### 2.1 Feature status table

| Feature | Status in code | Primary evidence |
| --- | --- | --- |
| Writing AI grading | Live | Routes for create/draft/submit/feedback in `backend/src/OetLearner.Api/Endpoints/LearnerEndpoints.cs:79-111`; evaluation pipeline registered in `backend/src/OetLearner.Api/Program.cs:729`; pipeline comments and gateway call in `backend/src/OetLearner.Api/Services/Writing/WritingEvaluationPipeline.cs:18-26`, `backend/src/OetLearner.Api/Services/Writing/WritingEvaluationPipeline.cs:159` |
| Speaking async grading | Live | Speaking attempt/audio/upload/submit/review routes in `backend/src/OetLearner.Api/Endpoints/LearnerEndpoints.cs:113-132`; speaking evaluation pipeline registered in `backend/src/OetLearner.Api/Program.cs:728` |
| Speaking live interactive roleplay | Partial/live depending on configuration | Live-room and LiveKit webhook evidence in `backend/src/OetLearner.Api/Program.cs:1698-1700`, `backend/src/OetLearner.Api/Endpoints/SpeakingLiveRoomEndpoints.cs:73-80`, `backend/src/OetLearner.Api/Hubs/ConversationHub.SpeakingRoleplay.cs:25-33` |
| Listening deterministic marking | Live | Listening attempt/answer/submit/evaluation routes in `backend/src/OetLearner.Api/Endpoints/LearnerEndpoints.cs:201-210`; DB sets for listening paper/attempt/answer/policy tables in `backend/src/OetLearner.Api/Data/LearnerDbContext.cs:265-273` |
| Reading deterministic marking | Live, with legacy learner routes redirected to structured reading-paper routes | Legacy route shims in `backend/src/OetLearner.Api/Endpoints/LearnerEndpoints.cs:191-199`; DB sets for reading paper/attempt/answer/policy tables in `backend/src/OetLearner.Api/Data/LearnerDbContext.cs:249-258` |
| Full mock exams | Live | Mock attempt lifecycle routes in `backend/src/OetLearner.Api/Endpoints/LearnerEndpoints.cs:214-221`; mock DB sets in `backend/src/OetLearner.Api/Data/LearnerDbContext.cs:31-42`; aggregation service registered in `backend/src/OetLearner.Api/Program.cs:583-584` |
| Payment/credit system | Live | Billing summary/plan/quote/checkout/wallet routes in `backend/src/OetLearner.Api/Endpoints/LearnerEndpoints.cs:285-328`; wallet/payment/webhook DB sets in `backend/src/OetLearner.Api/Data/LearnerDbContext.cs:24-26`, `backend/src/OetLearner.Api/Data/LearnerDbContext.cs:80-82`; Stripe gateway registered in `backend/src/OetLearner.Api/Program.cs:753` |
| Candidate dashboard | Live | Dashboard surfaces are present in `app/dashboard/`; backend learner state tables include goals, settings, readiness, study plans, streaks, XP, achievements, and leaderboard in `backend/src/OetLearner.Api/Data/LearnerDbContext.cs:8-19`, `backend/src/OetLearner.Api/Data/LearnerDbContext.cs:104-108` |
| Admin/tutor/content management | Live | Admin, rulebook, expert, content, upload, and audit DB sets appear in `backend/src/OetLearner.Api/Data/LearnerDbContext.cs:88-101`, `backend/src/OetLearner.Api/Data/LearnerDbContext.cs:180-189`, `backend/src/OetLearner.Api/Data/LearnerDbContext.cs:242-246` |

### 2.2 Major database table families observed

| Family | Representative DbSets | Evidence |
| --- | --- | --- |
| Core learner | Users, Goals, Settings, ContentItems, Attempts, Evaluations, StudyPlans | `backend/src/OetLearner.Api/Data/LearnerDbContext.cs:8-20` |
| Billing/payments | Subscriptions, Wallets, Invoices, WalletTransactions, PaymentTransactions, PaymentWebhookEvents, BillingPlans, BillingAddOns | `backend/src/OetLearner.Api/Data/LearnerDbContext.cs:24-26`, `backend/src/OetLearner.Api/Data/LearnerDbContext.cs:64-82`, `backend/src/OetLearner.Api/Data/LearnerDbContext.cs:281-282` |
| Mock exams | MockAttempts, MockReports, MockBundles, section attempts, reservations, bookings, proctoring, entitlement ledger | `backend/src/OetLearner.Api/Data/LearnerDbContext.cs:31-42` |
| Reading/listening | ReadingParts/Questions/Attempts/Answers/Policies; ListeningParts/Questions/Attempts/Answers/Policies/TTS jobs | `backend/src/OetLearner.Api/Data/LearnerDbContext.cs:249-280` |
| Conversation/speaking AI | ConversationSessions, ConversationTurns, ConversationEvaluations, ConversationSettings, speaking live/session entities in domain files | `backend/src/OetLearner.Api/Data/LearnerDbContext.cs:122-128` |
| AI platform | AiUsageRecords, quota plans/counters, BYOK credentials/preferences, providers/accounts/routes, credit ledger, tools, assistant threads/messages | `backend/src/OetLearner.Api/Data/LearnerDbContext.cs:193-234` |
| Rulebooks/content admin | RulebookVersion/Section/Rule rows, ContentPapers, ContentPaperAssets, AdminUploadSessions, AuditEvents | `backend/src/OetLearner.Api/Data/LearnerDbContext.cs:99-101`, `backend/src/OetLearner.Api/Data/LearnerDbContext.cs:189`, `backend/src/OetLearner.Api/Data/LearnerDbContext.cs:242-246` |

## 3. AI Call Inventory

### 3.1 Gateway and provider model

| Finding | Evidence | Status |
| --- | --- | --- |
| F-011: Writing grading uses the AI gateway with `FeatureCode = AiFeatureCodes.WritingGrade`. | `backend/src/OetLearner.Api/Services/Writing/WritingEvaluationPipeline.cs:23`, `backend/src/OetLearner.Api/Services/Writing/WritingEvaluationPipeline.cs:159` | Verified |
| F-012: Writing pipeline persists both completed AI evaluations and failed deterministic-rule-only fallback states. | `backend/src/OetLearner.Api/Services/Writing/WritingEvaluationPipeline.cs:233-256`, `backend/src/OetLearner.Api/Services/Writing/WritingEvaluationPipeline.cs:316-353` | Verified |
| F-013: Admin writing draft generation is a separate AI feature code. | `backend/src/OetLearner.Api/Services/Writing/WritingDraftService.cs:14-21`, `backend/src/OetLearner.Api/Services/Writing/WritingDraftService.cs:123` | Verified |
| F-014: AI usage/quota/BYOK/provider routing are persisted as first-class tables. | `backend/src/OetLearner.Api/Data/LearnerDbContext.cs:193-234` | Verified |
| F-015: No provider Batch API call was found in backend provider/service code. | `Not found in backend/src/OetLearner.Api/** for provider Batch API markers` | Verified gap |
| F-016: Anthropic-style prompt caching is documented and flagged in defaults, but current Anthropic provider transport sends `system = request.SystemPrompt` as a flat string. | `backend/src/OetLearner.Api/Services/Rulebook/README.md:73`, `backend/src/OetLearner.Api/Services/Rulebook/README.md:110-120`, `backend/src/OetLearner.Api/Hubs/ConversationHub.SpeakingRoleplay.cs:25-26`, `backend/src/OetLearner.Api/Services/Rulebook/AiProviderRegistry.cs:241` | Verified gap |

### 3.2 External AI/voice/OCR services observed

| Service/provider | Why it counts | Evidence |
| --- | --- | --- |
| OpenAI-compatible chat providers | Backend provider implementation supports `/chat/completions`; registry comments list OpenAI-compatible vendors. | `backend/src/OetLearner.Api/Services/Rulebook/OpenAiCompatibleProvider.cs:9-11`, `backend/src/OetLearner.Api/Services/Rulebook/AiProviderRegistry.cs:13-15` |
| DigitalOcean serverless inference | Registry comments explicitly include DigitalOcean Serverless in OpenAI-compatible provider set. | `backend/src/OetLearner.Api/Services/Rulebook/AiProviderRegistry.cs:13-15` |
| Anthropic | Native Anthropic Messages API adapter exists. | `backend/src/OetLearner.Api/Services/Rulebook/AiProviderRegistry.cs:19`, `backend/src/OetLearner.Api/Services/Rulebook/AiProviderRegistry.cs:203-253` |
| Cloudflare Workers AI/Gateway | Cloudflare dialect and native adapter exist. | `backend/src/OetLearner.Api/Domain/AiProviderEntities.cs:7-15`, `backend/src/OetLearner.Api/Services/Rulebook/AiProviderRegistry.cs:286-360` |
| GitHub Models/Copilot | Copilot dialect and `CopilotAiModelProvider` exist; provider uses GitHub Models inference endpoint through Azure AI Inference client. | `backend/src/OetLearner.Api/Domain/AiProviderEntities.cs:17-27`, `backend/src/OetLearner.Api/Services/Rulebook/CopilotAiModelProvider.cs:9-28` |
| ElevenLabs STT/TTS | Conversation options include ElevenLabs STT and TTS keys/base URLs/models. | `backend/src/OetLearner.Api/Configuration/ConversationOptions.cs:47-60`, `backend/src/OetLearner.Api/Configuration/ConversationOptions.cs:60-70` |
| Whisper/OpenAI-compatible ASR | Conversation options include Whisper base URL, key, and model. | `backend/src/OetLearner.Api/Configuration/ConversationOptions.cs:11-14` |
| Deepgram ASR | Conversation options include Deepgram key, model, and language. | `backend/src/OetLearner.Api/Configuration/ConversationOptions.cs:15-17` |
| Azure Speech / Azure AI | Backend package `Azure.AI.Inference`; admin conversation settings include Azure TTS voice; prior inventory found speech providers. | `backend/src/OetLearner.Api/OetLearner.Api.csproj:11`, `backend/src/OetLearner.Api/Contracts/AdminRequests.cs:728` |
| LiveKit | Live-room route/webhook and config surface. | `backend/src/OetLearner.Api/Program.cs:1698-1700`, `backend/src/OetLearner.Api/Endpoints/SpeakingLiveRoomEndpoints.cs:73-80` |
| AWS S3-compatible storage | Backend package and storage config surface. | `backend/src/OetLearner.Api/OetLearner.Api.csproj:10`; `backend/src/OetLearner.Api/appsettings.json:1-220` from earlier read |

## 4. Rulebooks And Content

### 4.1 Rulebook architecture

| Finding | Evidence | Status |
| --- | --- | --- |
| F-017: Rulebooks are described in the backend project as the single source of truth for AI grounding and the rule engine. | `backend/src/OetLearner.Api/OetLearner.Api.csproj:53-55` | Verified |
| F-018: Rulebooks are embedded into the backend assembly from `rulebooks/**/*.json`. | `backend/src/OetLearner.Api/OetLearner.Api.csproj:59-61` | Verified |
| F-019: Runtime DB-backed rulebook tables exist. | `backend/src/OetLearner.Api/Data/LearnerDbContext.cs:99-101` | Verified |
| F-020: Rulebook folders include conversation, grammar, listening, pronunciation, reading, remediation, speaking, vocabulary, and writing. | Source tree captured earlier | Verified |

### 4.2 Content sources and seed surfaces

| Content surface | Evidence | Status |
| --- | --- | --- |
| Writing seed samples | `backend/src/OetLearner.Api/OetLearner.Api.csproj:94` | Verified |
| Recalls seed data | `backend/src/OetLearner.Api/OetLearner.Api.csproj:70` | Verified |
| Recall sets seed data | `backend/src/OetLearner.Api/OetLearner.Api.csproj:82` | Verified |
| Reading authored content tables | `backend/src/OetLearner.Api/Data/LearnerDbContext.cs:249-258` | Verified |
| Listening authored content tables and TTS job table | `backend/src/OetLearner.Api/Data/LearnerDbContext.cs:265-280` | Verified |
| Content upload/admin paper asset model | `backend/src/OetLearner.Api/Data/LearnerDbContext.cs:242-246` | Verified |

## 5. User Journey Traces

Status: first pass complete from repository evidence. No runtime traffic, DB rows, production logs, or secrets were inspected.

### 5.1 Quick Check package purchase

Important naming note: a search for `Quick Check`, `quick-check`, and `quick_check` found no product/code string outside this report. The codebase exposes a generic learner checkout, add-on, review-credit, and wallet top-up pipeline. Treat "Quick Check package" as a product/offer name that still needs SKU mapping.

| Step | Evidence | Status |
| --- | --- | --- |
| Learner sees billing summary/plans/quote surfaces. | `backend/src/OetLearner.Api/Endpoints/LearnerEndpoints.cs:285-306` | Verified |
| Learner creates a checkout session through `/v1/billing/checkout-sessions`; only `review_credits`, `plan_upgrade`, `plan_downgrade`, and `addon_purchase` are accepted product types. | `backend/src/OetLearner.Api/Endpoints/LearnerEndpoints.cs:314`, `backend/src/OetLearner.Api/Services/LearnerService.cs:3225-3240` | Verified |
| Checkout request validates quantity, target item, gateway, add-ons, and idempotency before provider session creation. | `backend/src/OetLearner.Api/Services/LearnerService.cs:3242-3284` | Verified |
| Checkout creates or updates a `PaymentTransaction` with gateway transaction id, transaction type, amount/currency, quote snapshot, and metadata. | `backend/src/OetLearner.Api/Services/LearnerService.cs:3435-3505` | Verified |
| Wallet top-up is a separate path at `/v1/billing/wallet/top-up`; it validates Stripe/PayPal, configured tiers, and idempotency. | `backend/src/OetLearner.Api/Endpoints/LearnerEndpoints.cs:325-328`, `backend/src/OetLearner.Api/Services/LearnerService.cs:8126-8170` | Verified |
| Wallet top-up uses the selected payment gateway, writes a pending `PaymentTransaction`, and returns a checkout URL/session id. | `backend/src/OetLearner.Api/Services/WalletService.cs:388-475` | Verified |
| Payment gateways registered in the lookup include Stripe, PayPal, PayTabs, Paymob, and Checkout.com, though the learner checkout validation message still says choose Stripe or PayPal. | `backend/src/OetLearner.Api/Services/PaymentGatewayService.cs:760-787`, `backend/src/OetLearner.Api/Services/LearnerService.cs:3257-3264` | Verified |
| Stripe webhook endpoint is unauthenticated by design, reads raw body/headers, calls `HandleStripeWebhookAsync`, and returns 400 on rejected verification/processing. | `backend/src/OetLearner.Api/Endpoints/LearnerEndpoints.cs:330-340` | Verified |
| Webhook handler delegates to provider verification, rejects failed verification, de-duplicates completed/ignored events, stores payload hash/parser/transaction/status metadata, then applies verified side effects. | `backend/src/OetLearner.Api/Services/LearnerService.cs:8248-8390`, `backend/src/OetLearner.Api/Services/LearnerService.cs:8390-8495` | Verified |
| Completed wallet top-up webhooks credit the wallet, create/update an invoice, and emit a billing event. | `backend/src/OetLearner.Api/Services/LearnerService.cs:8574-8576`, `backend/src/OetLearner.Api/Services/LearnerService.cs:8675-8735` | Verified |
| Completed normal checkout webhooks apply subscription/add-on state, grant included plan credits or add-on credits, mark quote complete, create invoice, emit billing events, analytics, and payment/subscription notifications. | `backend/src/OetLearner.Api/Services/LearnerService.cs:8737-8805`, `backend/src/OetLearner.Api/Services/LearnerService.cs:8860-9020` | Verified |
| Wallet mutation is append-only and idempotency-aware; credit/debit writes `WalletTransaction` plus audit event and refuses negative balances. | `backend/src/OetLearner.Api/Services/WalletService.cs:7-11`, `backend/src/OetLearner.Api/Services/WalletService.cs:85-260` | Verified |
| Persistence includes `WalletTransactions`, `PaymentTransactions`, `PaymentWebhookEvents`, unique gateway event/transaction indexes, and wallet transaction idempotency indexes. | `backend/src/OetLearner.Api/Data/LearnerDbContext.cs:80-82`, `backend/src/OetLearner.Api/Data/LearnerDbContext.cs:906-929` | Verified |
| F-021: Dedicated `Quick Check` SKU/name was not found in code. Claude should ask the product owner which concrete plan/add-on/review-credit product represents it before assigning a model/cost strategy. | `Not found in codebase` for `Quick Check` search | Gap |

### 5.2 Writing submission

| Step | Evidence | Status |
| --- | --- | --- |
| Learner creates a writing attempt and may save drafts/heartbeat before submission. | `backend/src/OetLearner.Api/Endpoints/LearnerEndpoints.cs:100-106` | Verified |
| Submit path is idempotency-aware and returns cached submission response for repeated keys. | `backend/src/OetLearner.Api/Services/LearnerService.cs:1791-1805` | Verified |
| Already submitted/evaluating/completed attempts return existing queued/evaluation/review state rather than requeueing blindly. | `backend/src/OetLearner.Api/Services/LearnerService.cs:1807-1830` | Verified |
| Paper-mode submission attaches assets, requires OCR completion, requires extracted text, and copies extracted text into the draft. | `backend/src/OetLearner.Api/Services/LearnerService.cs:1832-1874` | Verified |
| AI assessor path checks writing entitlement before queueing AI grading. | `backend/src/OetLearner.Api/Services/LearnerService.cs:1888-1905` | Verified |
| Instructor assessor path completes the attempt, creates a review request, tries to assign Dr Ahmed, records analytics, saves idempotent response, and does not call AI grading. | `backend/src/OetLearner.Api/Services/LearnerService.cs:1916-1959` | Verified |
| AI assessor path marks attempt `Evaluating`, creates a queued `Evaluation`, queues `JobType.WritingEvaluation`, records `task_submitted`, and returns a polling response. | `backend/src/OetLearner.Api/Services/LearnerService.cs:1961-1980` | Verified |
| Background worker dispatches `JobType.WritingEvaluation` to `IWritingEvaluationPipeline.CompleteEvaluationAsync`, then runs writing side effects. | `backend/src/OetLearner.Api/Services/BackgroundJobProcessor.cs:225-233` | Verified |
| Writing evaluation pipeline runs deterministic rule-engine findings before prompt/gateway usage. | `backend/src/OetLearner.Api/Services/Writing/WritingEvaluationPipeline.cs:51-135` | Verified |
| Writing AI grading builds a grounded Writing prompt and calls the AI gateway with `FeatureCode = AiFeatureCodes.WritingGrade`, temperature `0.2`, and template id `writing.score.v1`. | `backend/src/OetLearner.Api/Services/Writing/WritingEvaluationPipeline.cs:136-166` | Verified |
| Pipeline handles ungrounded prompt, quota denial, provider failure, malformed JSON, and kill-switch states as failed/retryable/non-retryable evaluation states rather than silent success. | `backend/src/OetLearner.Api/Services/Writing/WritingEvaluationPipeline.cs:77-102`, `backend/src/OetLearner.Api/Services/Writing/WritingEvaluationPipeline.cs:167-213` | Verified |
| On success, pipeline persists score range, grade, confidence, strengths, issues, criterion scores, feedback items, AI provenance, advisory pass info, and completed attempt/evaluation state. | `backend/src/OetLearner.Api/Services/Writing/WritingEvaluationPipeline.cs:214-270` | Verified |
| Side effects after completed grading emit `evaluation_completed`, refresh readiness, update diagnostic progress, queue study-plan regeneration, and send learner notifications. | `backend/src/OetLearner.Api/Services/BackgroundJobProcessor.cs:516-570` | Verified |
| Results and feedback endpoints exist for polling/retrieval. | `backend/src/OetLearner.Api/Endpoints/LearnerEndpoints.cs:107-108` | Verified |

### 5.3 Live speaking roleplay

This journey is split across speaking-roleplay hub helpers and the generic conversation audio loop. It is also configuration-dependent because STT/TTS/realtime providers are selected at runtime.

| Step | Evidence | Status |
| --- | --- | --- |
| Speaking live rooms and LiveKit webhook ingestion are mapped separately from ConversationHub. | `backend/src/OetLearner.Api/Program.cs:1698-1700`, `backend/src/OetLearner.Api/Endpoints/SpeakingLiveRoomEndpoints.cs:73-80` | Verified |
| Speaking roleplay wrapper states that full audio/STT/TTS continues through `StartSession`. | `backend/src/OetLearner.Api/Hubs/ConversationHub.SpeakingRoleplay.cs:25-33` | Verified |
| Roleplay starts through `StartSpeakingRoleplay`, composes persona/task prompt context, and starts a timer. | `backend/src/OetLearner.Api/Hubs/ConversationHub.SpeakingRoleplay.cs:58-183` | Verified |
| Timer expiry auto-ends through `SpeakingSessionService.EndSessionAsync`, while client may also end manually. | `backend/src/OetLearner.Api/Hubs/ConversationHub.SpeakingRoleplay.cs:256-264` | Verified |
| Conversation audio loop begins at `StartSession`: checks authenticated user/session ownership, feature enabled flag, session state, group membership, existing turns, then generates an opening AI reply. | `backend/src/OetLearner.Api/Hubs/ConversationHub.cs:53-103` | Verified |
| Opening reply uses `IConversationAiOrchestrator.GenerateOpeningAsync`, optional TTS, `IConversationAudioService.WriteAsync`, stores an AI `ConversationTurn`, updates transcript JSON, and sends `ReceiveAIResponse`. | `backend/src/OetLearner.Api/Hubs/ConversationHub.cs:104-160` | Verified |
| Conversation orchestrator grounds opening/reply prompts with conversation rulebook context and calls gateway with `ConversationOpening`/`ConversationReply`; default model is `anthropic-claude-opus-4.7` unless runtime options override it. | `backend/src/OetLearner.Api/Services/Conversation/ConversationAiOrchestrator.cs:40-88` | Verified |
| Learner audio turn is idempotency-checked by client/provider ids, transcribed through selected ASR provider, stored through conversation audio service, persisted as learner `ConversationTurn`, and emitted as `ReceiveTranscript`. | `backend/src/OetLearner.Api/Hubs/ConversationHub.cs:751-829` | Verified |
| After transcript update, hub calls grounded conversation reply generation; prompt grounding/provider errors are surfaced to caller. | `backend/src/OetLearner.Api/Hubs/ConversationHub.cs:830-856` | Verified |
| AI reply can be synthesized through selected TTS provider, persisted as an AI `ConversationTurn` with `AiFeatureCodes.ConversationReply`, transcript JSON is updated, and `ReceiveAIResponse` is sent. | `backend/src/OetLearner.Api/Hubs/ConversationHub.cs:857-890` | Verified |
| EndSession marks the session `evaluating`, stores duration, queues `JobType.ConversationEvaluation`, sends state change, and leaves SignalR group. | `backend/src/OetLearner.Api/Hubs/ConversationHub.cs:638-668` | Verified |
| Background worker dispatches conversation evaluation jobs to `CompleteConversationEvaluationAsync`. | `backend/src/OetLearner.Api/Services/BackgroundJobProcessor.cs:270-271`, `backend/src/OetLearner.Api/Services/BackgroundJobProcessor.cs:1233-1333` | Verified |
| Conversation evaluation calls the AI gateway with `AiFeatureCodes.ConversationEvaluation`; default evaluation model is also `anthropic-claude-opus-4.7` unless runtime options override it. | `backend/src/OetLearner.Api/Services/Conversation/ConversationAiOrchestrator.cs:90-120` | Verified |
| Conversation persistence has dedicated turns/evaluations/annotations DbSets and uniqueness indexes for session+turn client/provider events. | `backend/src/OetLearner.Api/Data/LearnerDbContext.cs:123-125`, `backend/src/OetLearner.Api/Data/LearnerDbContext.cs:793-799` | Verified |
| F-022: Live speaking uses multiple provider selectors and runtime options; Claude should design model selection separately for ASR, live reply LLM, TTS, and post-session evaluation instead of treating it as one AI call. | Evidence rows above | Gap |

### 5.4 Full mock exam

| Step | Evidence | Status |
| --- | --- | --- |
| Mock attempt lifecycle routes exist: create, get, section start, section complete, submit, cancel, proctoring event, and report retrieval. | `backend/src/OetLearner.Api/Endpoints/LearnerEndpoints.cs:214-221` | Verified |
| Creating a mock attempt resolves a published bundle, ordered sections, profession/delivery/strictness/review settings, checks wallet credits for selected tutor review, and writes a `MockAttempt`. | `backend/src/OetLearner.Api/Services/MockService.cs:309-380` | Verified |
| Starting and completing sections are implemented in `MockService`; exact per-section answer handling was not expanded in this pass. | `backend/src/OetLearner.Api/Services/MockService.cs:471`, `backend/src/OetLearner.Api/Services/MockService.cs:649` | Partially traced |
| Submit refuses zero completed sections and refuses incomplete required sections. | `backend/src/OetLearner.Api/Services/MockService.cs:876-920` | Verified |
| Submit creates or requeues a `MockReport`, marks attempt `Evaluating`, queues `JobType.MockReportGeneration`, and returns polling route/interval. | `backend/src/OetLearner.Api/Services/MockService.cs:921-960` | Verified |
| Background worker dispatches `JobType.MockReportGeneration` to `IMockReportAggregationService.GenerateAsync`, then runs mock report side effects. | `backend/src/OetLearner.Api/Services/BackgroundJobProcessor.cs:247-250`, `backend/src/OetLearner.Api/Services/BackgroundJobProcessor.cs:1123` | Verified |
| Aggregator loads mock attempt, creates/reuses report, joins section attempts to bundle sections/content papers, and resolves each section result through a section resolver. | `backend/src/OetLearner.Api/Services/Mocks/Results/MockReportAggregationService.cs:206-237` | Verified |
| Aggregator joins review requests for productive sections and labels subtest state from either resolved result or review state. | `backend/src/OetLearner.Api/Services/Mocks/Results/MockReportAggregationService.cs:238-266` | Verified |
| Aggregator computes overall average from available scaled scores, weakest subtest, prior report comparison, proctoring summary, per-module readiness, timing analysis, booking/retake/remediation advice. | `backend/src/OetLearner.Api/Services/Mocks/Results/MockReportAggregationService.cs:267-420` | Verified |
| Report payload is explicitly versioned as V1 and consumers must update if schema changes. | `backend/src/OetLearner.Api/Services/Mocks/Results/MockReportAggregationService.cs:319`, `backend/src/OetLearner.Api/Services/Mocks/Results/MockReportPayloadV1.cs:1-45` | Verified |
| Aggregator marks report completed, marks mock attempt completed, writes `mock_completed` analytics if missing, and sends learner `LearnerMockReportReady` notification. | `backend/src/OetLearner.Api/Services/Mocks/Results/MockReportAggregationService.cs:319-420` | Verified |
| F-023: Full mock report generation is an aggregation/scoring workflow over section evidence and review state; no AI report-writing call was found in the report aggregation path read here. | `backend/src/OetLearner.Api/Services/Mocks/Results/MockReportAggregationService.cs:206-420` | Verified |
| F-024: Section-by-section internals for listening, reading, writing, and speaking inside full mocks need a deeper pass if Claude needs per-section model strategy. This pass only traces the attempt/report lifecycle. | `backend/src/OetLearner.Api/Services/MockService.cs:471`, `backend/src/OetLearner.Api/Services/MockService.cs:649` | Gap |

## 6. AI Cost Reality

Status: first pass complete from repository evidence. No production database rows, billing exports, provider invoices, or logs were queried.

### 6.1 Accounting model in code

| Finding | Evidence | Status |
| --- | --- | --- |
| F-025: `AiUsageRecord` is designed as one row per AI call, regardless of success/refusal/error. | `backend/src/OetLearner.Api/Domain/AiEntities.cs:24-42`, `backend/src/OetLearner.Api/Domain/AiEntities.cs:44-64` | Verified |
| F-026: `AiUsageRecord` stores user/feature/provider/account/failover/model/key-source/rulebook/template/prompt hashes. | `backend/src/OetLearner.Api/Domain/AiEntities.cs:76-156` | Verified |
| F-027: `AiUsageRecord` stores prompt tokens, completion tokens, computed total tokens, estimated USD cost, outcome, error code/message, latency, retry count, policy trace, and day/month period keys. | `backend/src/OetLearner.Api/Domain/AiEntities.cs:158-202` | Verified |
| F-028: Canonical AI feature codes include high-stakes scoring (`writing.grade`, `mock.full_grade`, `pronunciation.score`, `conversation.evaluation`), low-stakes coach/summary/recall features, admin draft features, and AI assistant features. | `backend/src/OetLearner.Api/Domain/AiEntities.cs:210-270` | Verified |
| F-029: Provider registry rows carry dialect/category/base URL/default model/reasoning effort/allowed models and rate-card fields `PricePer1kPromptTokens` and `PricePer1kCompletionTokens`. | `backend/src/OetLearner.Api/Domain/AiProviderEntities.cs:5-55`, `backend/src/OetLearner.Api/Domain/AiProviderEntities.cs:70-139` | Verified |
| F-030: BYOK credentials are encrypted per user/provider and preferences support `Auto`, `ByokOnly`, and `PlatformOnly`. | `backend/src/OetLearner.Api/Domain/AiCredentialEntities.cs:14-63`, `backend/src/OetLearner.Api/Domain/AiCredentialEntities.cs:65-96` | Verified |
| F-031: AI quota plans support token caps, daily caps, concurrent request caps, overage policy, allowed features, and allowed models. | `backend/src/OetLearner.Api/Domain/AiQuotaEntities.cs:45-124` | Verified |
| F-032: AI quota counters track tokens, request count, and accumulated USD cost per user/period. | `backend/src/OetLearner.Api/Domain/AiQuotaEntities.cs:126-158` | Verified |
| F-033: AI credit ledger exists and has a `UsageDebit` source intended for gateway debits on successful calls. | `backend/src/OetLearner.Api/Domain/AiCreditEntities.cs:6-20`, `backend/src/OetLearner.Api/Domain/AiCreditEntities.cs:22-62` | Verified |

### 6.2 What is actually persisted per call

| Finding | Evidence | Status |
| --- | --- | --- |
| F-034: `AiUsageRecorder` is fail-soft and persists usage without storing prompt bodies. | `backend/src/OetLearner.Api/Services/Rulebook/AiUsageRecorder.cs:8-20`, `backend/src/OetLearner.Api/Services/Rulebook/AiUsageRecorder.cs:195-204` | Verified |
| F-035: Successful usage recording accepts provider id, model, key source, provider-reported usage tokens, latency, retry count, policy trace, account id, and failover trace. | `backend/src/OetLearner.Api/Services/Rulebook/AiUsageRecorder.cs:22-36`, `backend/src/OetLearner.Api/Services/Rulebook/AiUsageRecorder.cs:74-100` | Verified |
| F-036: Failed/refused usage recording accepts provider/model/key source/outcome/error/latency/retry/policy/account/failover fields. | `backend/src/OetLearner.Api/Services/Rulebook/AiUsageRecorder.cs:38-52`, `backend/src/OetLearner.Api/Services/Rulebook/AiUsageRecorder.cs:102-130` | Verified |
| F-037: The recorder persists token counts from provider usage, but hardcodes `CostEstimateUsd = 0m`. Its own comment says rate-card cost wiring was deferred. | `backend/src/OetLearner.Api/Services/Rulebook/AiUsageRecorder.cs:176-186` | Gap |
| F-038: The gateway aggregates multi-turn tool-loop tokens into a single `AiUsageRecord`; this avoids per-tool-turn row explosion but loses per-turn cost detail. | `backend/src/OetLearner.Api/Services/Rulebook/AiGatewayService.cs:299-320`, `backend/src/OetLearner.Api/Services/Rulebook/AiGatewayService.cs:493-524` | Verified |
| F-039: Provider failure paths are recorded as failed usage rows with sanitized provider error messages. | `backend/src/OetLearner.Api/Services/Rulebook/AiGatewayService.cs:480-500`, `backend/src/OetLearner.Api/Services/Rulebook/AiGatewayService.cs:653-684` | Verified |

### 6.3 Quota and budget path

| Finding | Evidence | Status |
| --- | --- | --- |
| F-040: Gateway resolves BYOK/platform key source before quota, because BYOK short-circuits quota enforcement. | `backend/src/OetLearner.Api/Services/Rulebook/AiGatewayService.cs:230-254` | Verified |
| F-041: Quota service checks disabled feature list, kill switch, BYOK bypass, anonymous/system bypass, global monthly budget, user override, plan feature allow-list, and period caps. | `backend/src/OetLearner.Api/Services/AiManagement/AiQuotaService.cs:89-257` | Verified |
| F-042: BYOK calls are allowed with policy trace `byok.unmetered` and do not consume quota counters. | `backend/src/OetLearner.Api/Services/AiManagement/AiQuotaService.cs:128-139`, `backend/src/OetLearner.Api/Services/Rulebook/AiGatewayService.cs:528-535` | Verified |
| F-043: Platform-funded successful calls compute a USD estimate from provider rate-card rows, then commit tokens/cost to quota counters and global spend. | `backend/src/OetLearner.Api/Services/Rulebook/AiGatewayService.cs:528-552`, `backend/src/OetLearner.Api/Services/Rulebook/AiGatewayService.cs:630-649`, `backend/src/OetLearner.Api/Services/AiManagement/AiQuotaService.cs:259-342` | Verified |
| F-044: Quota counters can therefore have non-zero `CostAccumulatedUsd` while matching `AiUsageRecord.CostEstimateUsd` rows remain zero. | `backend/src/OetLearner.Api/Services/Rulebook/AiUsageRecorder.cs:183-186`, `backend/src/OetLearner.Api/Services/AiManagement/AiQuotaService.cs:293-302` | Gap |
| F-045: Global budget hard-kill is driven by `AiGlobalPolicy.CurrentSpendUsd`, which is updated only by quota commit for non-BYOK successful calls. | `backend/src/OetLearner.Api/Services/AiManagement/AiQuotaService.cs:140-153`, `backend/src/OetLearner.Api/Services/AiManagement/AiQuotaService.cs:336-342` | Verified |

### 6.4 Dashboard and forecast reality

| Surface | What it reads | Consequence | Evidence |
| --- | --- | --- | --- |
| Learner AI usage summary | `AiUsageRecords` sums calls/tokens/`CostEstimateUsd`; credit usage from negative `AiCreditLedger` rows; wallet balance from `Wallets`. | If recorder cost remains zero, learner visible AI cost is likely zero even when quota counters have cost. | `backend/src/OetLearner.Api/Services/Billing/AiUsageAnalyticsService.cs:57-126` |
| Admin AI analytics summary | `AiUsageRecords` grouped by feature/provider/day/user and summed `CostEstimateUsd`. | Admin per-feature/provider cost dashboard likely underreports cost as zero. | `backend/src/OetLearner.Api/Services/Billing/AiUsageAnalyticsService.cs:137-224` |
| Admin usage explorer | `/v1/admin/ai/usage` exposes provider/model/key source/tokens/cost/outcome/error/latency/policy/failover rows. | It exposes the right columns, but cost column inherits recorder zero. | `backend/src/OetLearner.Api/Endpoints/AiUsageAdminEndpoints.cs:22-108` |
| Admin usage summary | `/v1/admin/ai/usage/summary` groups by provider/account/outcome/user/feature and sums `CostEstimateUsd`. | Summary can be token-accurate but cost-inaccurate. | `backend/src/OetLearner.Api/Endpoints/AiUsageAdminEndpoints.cs:111-208` |
| Learner/admin analytics routes | `/v1/ai-usage/me`, `/v1/ai-usage/me/forecast`, `/v1/admin/ai-analytics/summary`, `/v1/admin/ai-analytics/forecast`. | Route surface exists, but repo-only audit cannot verify live numbers. | `backend/src/OetLearner.Api/Endpoints/AiAnalyticsEndpoints.cs:15-48`, `backend/src/OetLearner.Api/Endpoints/AiAnalyticsEndpoints.cs:106-142` |
| Forecast service | Last-30-day `AiUsageRecords` cost, daily call EMA, and negative AI credit ledger rows. | Forecast cost is likely zero if usage rows have zero cost; credit forecast depends on actual negative ledger entries. | `backend/src/OetLearner.Api/Services/Billing/UsageForecastService.cs:36-116` |

### 6.5 Provider routing and cost levers

| Lever | Current evidence | Evidence | Status |
| --- | --- | --- | --- |
| Feature-level provider/model routing | Resolver checks DB `AiFeatureRoutes`, then static speaking defaults for speaking-specific codes. | `backend/src/OetLearner.Api/Services/Rulebook/AiFeatureRouteResolver.cs:1-24`, `backend/src/OetLearner.Api/Services/Rulebook/AiFeatureRouteResolver.cs:173-203` | Verified |
| Static speaking AI defaults | `speaking.score.v2` -> Anthropic `claude-sonnet-4-6`; `speaking.patient.turn.v1` -> Anthropic `claude-haiku-4-5`; `card.draft.v1` -> Anthropic `claude-sonnet-4-6`; all mark prompt caching enabled. | `backend/src/OetLearner.Api/Services/Rulebook/AiFeatureRouteResolver.cs:42-73`, `backend/src/OetLearner.Api/Services/Rulebook/AiFeatureRouteResolver.cs:82-113` | Verified |
| Bulk Copilot routing candidates | Non-scoring features such as vocabulary, recalls, conversation opening/reply, writing coach, and summarise can be bulk-routed to Copilot. | `backend/src/OetLearner.Api/Services/Rulebook/AiFeatureRouteResolver.cs:150-164` | Verified |
| Provider admin CRUD | Admin AI endpoints expose provider rows including default model and rate-card fields; API keys are encrypted and only hints are returned. | `backend/src/OetLearner.Api/Endpoints/AiUsageAdminEndpoints.cs:404-520` | Verified |
| Generic OpenAI-compatible transport | Calls `POST chat/completions` with system+user messages, optional reasoning effort, no provider Batch API. | `backend/src/OetLearner.Api/Services/Rulebook/OpenAiCompatibleProvider.cs:40-79` | Verified |
| Registry-backed OpenAI-compatible transport | Calls `POST chat/completions` with system+user messages, optional reasoning effort, no provider Batch API. | `backend/src/OetLearner.Api/Services/Rulebook/AiProviderRegistry.cs:117-169` | Verified |
| Anthropic native transport | Calls `POST messages`; request has `system = request.SystemPrompt` as a flat string and one user message. | `backend/src/OetLearner.Api/Services/Rulebook/AiProviderRegistry.cs:203-253` | Verified |
| Prompt caching docs vs implementation | Rulebook README says Anthropic prompt caching requires `cache_control`; speaking helper builds such JSON, but provider transport sends flat `system` string. | `backend/src/OetLearner.Api/Services/Rulebook/README.md:73`, `backend/src/OetLearner.Api/Services/Rulebook/README.md:110-120`, `backend/src/OetLearner.Api/Hubs/ConversationHub.SpeakingRoleplay.cs:25-26`, `backend/src/OetLearner.Api/Services/Rulebook/AiProviderRegistry.cs:241` | Gap |
| Provider Batch API | Search found only internal app batch endpoints/jobs, not vendor batch API calls like `/v1/batches`. | `Not found in backend/src/OetLearner.Api/** for provider Batch API markers` | Gap |
| Embeddings/RAG | AI assistant indexing uses OpenAI-compatible `embeddings`, model `text-embedding-3-small`, dimension 1536, batch size 20, and hash fallback. | `backend/src/OetLearner.Api/Services/AiAssistant/Indexing/EmbeddingService.cs:9-29`, `backend/src/OetLearner.Api/Services/AiAssistant/Indexing/EmbeddingService.cs:48-80`, `backend/src/OetLearner.Api/Services/AiAssistant/Indexing/EmbeddingService.cs:84-120` | Verified |

### 6.6 Per-candidate cost estimate status

| Item | Result | Evidence |
| --- | --- | --- |
| Actual AI calls per paying candidate in last 30 days | Not available from repo-only audit. Requires sanitized production DB/log query. | `No DB/log query run` |
| Actual cost per paying candidate in last 30 days | Not available from repo-only audit. Current dashboard data may be zero because recorder cost is zero. | F-037, F-044 |
| Model-selection cost basis Claude can use now | Use call graph and feature criticality, not live dollar totals. Distinguish high-stakes scoring from drafts/hints/summaries/live-turn latency. | Sections 3, 5, 6 |
| Immediate accounting caveat | Before trusting current admin AI cost dashboards, reconcile `AiUsageRecords.CostEstimateUsd` vs `AiQuotaCounters.CostAccumulatedUsd`. | F-037, F-043, F-044 |

## 7. Gap Analysis

Status: first pass complete. These are flags only; no application code was changed.

| Gap | Why Claude should care | Evidence | Status |
| --- | --- | --- | --- |
| F-046: Next production build can pass with TypeScript and ESLint errors ignored. | Model-selection changes can ship despite type/lint regressions unless separate validation gates run. | `next.config.ts:23-24` | Gap |
| F-047: `Quick Check` was not found as a SKU/product code. | Claude cannot assign model cost/entitlement to that named package until product owner maps it to `review_credits`, `addon_purchase`, plan, or another SKU. | `backend/src/OetLearner.Api/Services/LearnerService.cs:3225-3240`; `Not found in codebase` for `Quick Check` search | Gap |
| F-048: AI usage dashboards aggregate cost from `AiUsageRecords`, but recorder writes zero cost. | Any model-selection strategy using current dashboard cost totals may be wrong. | `backend/src/OetLearner.Api/Services/Rulebook/AiUsageRecorder.cs:183-186`, `backend/src/OetLearner.Api/Services/Billing/AiUsageAnalyticsService.cs:65-93`, `backend/src/OetLearner.Api/Services/Billing/AiUsageAnalyticsService.cs:145-211` | Gap |
| F-049: Quota counters accumulate cost separately from usage rows. | Cost source of truth is split; reconcile before forecasting margins. | `backend/src/OetLearner.Api/Services/Rulebook/AiGatewayService.cs:528-552`, `backend/src/OetLearner.Api/Services/AiManagement/AiQuotaService.cs:293-302` | Gap |
| F-050: AI credit ledger has `UsageDebit`, but gateway debit path was not found. | Learner credit consumption, forecast credits, and wallet top-up suggestions may be disconnected from actual AI calls. | `backend/src/OetLearner.Api/Domain/AiCreditEntities.cs:6-20`, `backend/src/OetLearner.Api/Services/AiManagement/AiCreditService.cs:18-39`, `backend/src/OetLearner.Api/Services/Billing/UsageForecastService.cs:105-116` | Gap |
| F-051: Anthropic prompt caching is documented/flagged in speaking defaults, but provider transport sends flat string `system`. | Repeated rulebook/persona blocks may be paying full prompt cost unless cache-control serialization is implemented. | `backend/src/OetLearner.Api/Services/Rulebook/README.md:73`, `backend/src/OetLearner.Api/Services/Rulebook/README.md:110-120`, `backend/src/OetLearner.Api/Hubs/ConversationHub.SpeakingRoleplay.cs:25-26`, `backend/src/OetLearner.Api/Services/Rulebook/AiProviderRegistry.cs:241` | Gap |
| F-052: Vendor Batch API usage was not found. | Non-interactive grading/drafting/backfill jobs may be paying synchronous prices and missing batch discounts. | `Not found in backend/src/OetLearner.Api/** for provider Batch API markers` | Gap |
| F-053: Live speaking is not one model decision; it spans realtime/batch ASR, per-turn LLM, optional TTS, and post-session evaluation. | Claude should make separate tiering decisions by latency/stakes/modality. | `backend/src/OetLearner.Api/Hubs/ConversationHub.cs:751-890`, `backend/src/OetLearner.Api/Services/Conversation/ConversationAiOrchestrator.cs:40-120`, `backend/src/OetLearner.Api/Configuration/ConversationOptions.cs:7-60` | Gap |
| F-054: Full mock report aggregation path does not itself show an AI report-writing call; section internals need deeper tracing. | Claude should not assume `mock.full_grade` equals the final report aggregator. | `backend/src/OetLearner.Api/Services/Mocks/Results/MockReportAggregationService.cs:206-420`, `backend/src/OetLearner.Api/Services/MockService.cs:471`, `backend/src/OetLearner.Api/Services/MockService.cs:649` | Gap |
| F-055: AI Assistant gateway records usage but does not show quota enforcement/global-budget enforcement in its constructor/call path. | Assistant traffic may bypass `IAiQuotaService` protections that regular grounded gateway calls use. | `backend/src/OetLearner.Api/Services/AiAssistant/AiAssistantGateway.cs:21-27`, `backend/src/OetLearner.Api/Services/AiAssistant/AiAssistantGateway.cs:105-151` | Gap |
| F-056: External voice/OCR/storage provider costs are not unified in the `AiUsageRecord` accounting model in this pass. | A platform-wide AI model strategy should include STT/TTS/OCR/S3/PDF extraction costs, not only text LLM tokens. | `backend/src/OetLearner.Api/Domain/AiProviderEntities.cs:5-55`, `backend/src/OetLearner.Api/Configuration/ConversationOptions.cs:7-60`, `backend/src/OetLearner.Api/OetLearner.Api.csproj:10-11` | Gap |
| F-057: Reading legacy routes remain as shims returning gone/redirect helpers. | Model-selection docs should point to structured reading-paper routes, not legacy learner routes. | `backend/src/OetLearner.Api/Endpoints/LearnerEndpoints.cs:191-199` | Verified gap |

## 8. Risk And Reliability Notes

Status: first pass complete from repository evidence.

| Risk/reliability note | Evidence | Status |
| --- | --- | --- |
| F-058: Grounded gateway physically refuses missing/null/ungrounded prompts and records refusals. | `backend/src/OetLearner.Api/Services/Rulebook/AiGatewayService.cs:64-103` | Positive control |
| F-059: Production mock provider fallback is explicitly forbidden. | `backend/src/OetLearner.Api/Services/Rulebook/AiGatewayService.cs:190-226` | Positive control |
| F-060: Provider base URLs are checked for unsafe values before calls/admin provider writes. | `backend/src/OetLearner.Api/Services/Rulebook/OpenAiCompatibleProvider.cs:30-38`, `backend/src/OetLearner.Api/Services/Rulebook/AiProviderRegistry.cs:117-124`, `backend/src/OetLearner.Api/Endpoints/AiUsageAdminEndpoints.cs:436-441` | Positive control |
| F-061: Payment webhook flow persists provider payload hash/status and applies verified side effects; this is important for paid AI-credit workflows. | `backend/src/OetLearner.Api/Services/LearnerService.cs:8248-8390`, `backend/src/OetLearner.Api/Services/LearnerService.cs:8737-8805` | Positive control |
| F-062: Wallet ledger refuses negative balances and is idempotency-aware. | `backend/src/OetLearner.Api/Services/WalletService.cs:85-260` | Positive control |
| F-063: Usage recorder is fail-soft; accounting failures do not block the candidate response. | `backend/src/OetLearner.Api/Services/Rulebook/AiUsageRecorder.cs:8-14`, `backend/src/OetLearner.Api/Services/Rulebook/AiUsageRecorder.cs:195-204` | Reliability tradeoff |
| F-064: Quota commit after provider success is fail-soft; candidate gets the completion even if quota/cost accounting fails. | `backend/src/OetLearner.Api/Services/Rulebook/AiGatewayService.cs:528-559` | Reliability tradeoff |
| F-065: This audit did not run tests, typecheck, lint, build, Docker, migrations, production log queries, or production database queries. | Audit workflow constraint; no such commands executed | Limitation |

## 9. Open Questions For Claude

Status: ready for Claude to answer after reviewing this audit.

| Question | Why it matters | Evidence anchors |
| --- | --- | --- |
| Which model tiers should own high-stakes scoring versus low-stakes drafts, hints, summaries, and recall explanations? | Current feature codes already separate scoring-critical and non-scoring surfaces. | `backend/src/OetLearner.Api/Domain/AiEntities.cs:210-270` |
| Should Writing and Speaking scoring use dual-pass/high-tier models while drafts/coaching use cheaper or BYOK routes? | Writing pipeline is high-stakes, grounded, persisted, and fallback-aware; coach/draft paths are separate features. | `backend/src/OetLearner.Api/Services/Writing/WritingEvaluationPipeline.cs:136-270`, `backend/src/OetLearner.Api/Domain/AiEntities.cs:214-248` |
| What should live Speaking use for ASR, per-turn reply, TTS, and post-session evaluation? | These are different latency/cost/risk classes and should not share one blanket model rule. | `backend/src/OetLearner.Api/Hubs/ConversationHub.cs:751-890`, `backend/src/OetLearner.Api/Configuration/ConversationOptions.cs:7-60` |
| Should Anthropic prompt caching be implemented in provider serialization for rulebook/persona blocks? | Docs/defaults imply prompt caching, but transport currently sends a flat system string. | F-051 |
| Should vendor Batch APIs be introduced for non-interactive scoring/admin drafting/backfills? | No vendor Batch API use found; many jobs are async and could be candidates if SLA allows. | F-052 |
| Should cost source of truth be `AiUsageRecord`, `AiQuotaCounter`, or a new normalized cost ledger? | Today the rows that dashboards read and the counters that budgets update can diverge. | F-037, F-044, F-048, F-049 |
| Should AI Assistant traffic use the same quota/global-budget controls as grounded gateway traffic? | Assistant records usage but no quota service was found in its path. | F-055 |
| What should happen when paid candidate AI calls fail: retry, deterministic fallback, human-review queue, refund/credit, or visible delay? | Existing writing path has explicit failure states; product policy still needs model-by-model fallback choices. | `backend/src/OetLearner.Api/Services/Writing/WritingEvaluationPipeline.cs:167-213`, `backend/src/OetLearner.Api/Services/LearnerService.cs:1888-1980` |
| What exact SKU represents “Quick Check”? | This named offer was not found in code, so cost modelling cannot map it yet. | F-047 |

## 10. Quick Stats Appendix

Status: first pass complete from repository evidence and local read-only metadata counts.

| Statistic | Value | Evidence |
| --- | ---: | --- |
| Tracked files | 3,481 | `git ls-files` local read-only stats command |
| Tracked text LOC | 1,154,702 | Corrected local read-only LOC command |
| Backend DbSet declarations captured in first 282 lines | 100+ | `backend/src/OetLearner.Api/Data/LearnerDbContext.cs:8-282` |
| Unit/e2e counts claimed by repo instructions | 113 Vitest files, 675 tests, 13 Playwright projects | `AGENTS.md` project overview; not rerun during this audit |
| Writing seed samples | `writing-samples.v1=5`, `writing-samples.v2=24` | Local read-only JSON metadata count; `backend/src/OetLearner.Api/Data/Seeds/writing-samples.v1.json:4`, `backend/src/OetLearner.Api/Data/Seeds/writing-samples.v2.json` |
| Speaking role-play seed cards | 12 cards: 6 Nursing + 6 Medicine | `backend/src/OetLearner.Api/Services/Seeding/SpeakingRolePlayCardSeed.README.md:3-7`, `backend/src/OetLearner.Api/Services/Seeding/SpeakingRolePlayCardSeed.README.md:45-60` |
| Backend listening seed files | 2 JSON files | Local read-only metadata count; `backend/src/OetLearner.Api/Data/SeedData/listening/starter-mock-1.json`, `backend/src/OetLearner.Api/Data/SeedData/listening/starter-mock-2.json` |
| Real-content listening folder files | 13 files | Local read-only folder count for `Listening ( IMPORTANT NOTE =  Same for All Professions )/` |
| Real-content reading folder files | 13 files | Local read-only folder count for `Reading ( IMPORTANT NOTE = Same for All Professions )/` |
| Rulebook JSON files | 70 JSON files | `rulebooks/**/*.json` file search |
| Rulebook folder counts | writing 31, speaking 5, listening 4, reading 2, conversation 7, pronunciation 9, grammar 5, vocabulary 5 | Local read-only folder count |
| Recall seed files | 48 JSON files | Local read-only count for `backend/src/OetLearner.Api/Data/SeedData/recalls/` |
| Paying candidates in DB | Not available from repo-only audit | Requires sanitized DB query or export |
| AI calls last 30 days | Not available from repo-only audit | Requires sanitized DB/log query |
| Actual AI spend last 30 days | Not available from repo-only audit; current dashboard cost may underreport as zero | F-037, F-048 |

## Audit Completion Note

Total findings recorded: 65 (`F-001` through `F-065`).
