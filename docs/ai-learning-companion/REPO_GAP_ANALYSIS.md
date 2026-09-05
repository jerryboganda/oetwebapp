# Repository Gap Analysis - AI Learning Companion (F-001 ... F-184)

> Audited 2026-09-06 against `oetwebapp` on branch `writing/final-production-release`.
> Method: direct inspection of domain entities, services, endpoints, routes, migrations and seed data.
> Every one of the 184 source features appears exactly once. No feature is omitted, including those
> deliberately deferred or blocked on an external decision.

## Status summary

| Status | Count | Meaning |
|---|---:|---|
| `EXISTS` | 42 | A shipping implementation satisfies the requirement; the companion consumes it. |
| `PARTIAL` | 83 | Real implementation exists but does not yet satisfy the requirement in full. |
| `MISSING` | 53 | No implementation. Genuine new work. |
| `BLOCKED` | 3 | Cannot proceed without an external product, commercial or legal decision. |
| `DEFERRED_BY_SOURCE` | 3 | The source specification itself defers this; kept in backlog with an owner. |
| **Total** | **184** | |

### How to read this

The high `EXISTS`/`PARTIAL` share is the central finding: this repository already ships deterministic
planning, readiness, spaced repetition, adaptive difficulty, rulebook grounding, a credit ledger, an
entitlement resolver, per-call AI telemetry and a streaming chat stack. The companion is therefore an
**orchestration, grounding and governance layer** over existing engines - not a second product.

The concentration of `MISSING` is equally informative. It clusters in four places:

1. **Retrieval** (F-013..F-029) - no knowledge index over OET content exists at all.
2. **Authority and safety** (F-026, F-029, F-152, F-154) - no source-authority class, no conflict
   detection, no entitlement-before-retrieval. These are zero-tolerance requirements.
3. **Context and actions** (F-091..F-094, F-098..F-104) - no context envelope and no typed action registry.
4. **Durable learning memory** (F-042..F-044, F-047) - evidence exists but is not compacted, attributed,
   or learner-controllable.

Everything else is either already shipping or a bounded extension of something that is.

## Cross-cutting requirements without an F-ID

| Requirement | Repository position |
|---|---|
| Provider abstraction | **Satisfied.** `IAiModelProvider` + `IAiProviderRegistry` + `IAiFeatureRouteResolver`; Anthropic, Gemini, Cloudflare, Copilot and OpenAI-compatible adapters; multi-account failover. |
| One usage row per provider call | **Satisfied.** `IAiUsageRecorder` writes exactly one `AiUsageRecord` per physical call; `IDirectAiCallRecorder` covers non-gateway calls (OCR/STT/embeddings). |
| Cost attribution | **Satisfied.** `AiPricingResolver` over `AiModelPrice`, `AiBudgetPeriod`, budget alerts. |
| Kill switches | **Partial.** `Ai:Coordination:Enabled`, circuit breakers, `AiBudgetOverrideService` and provider deactivation exist. Companion needs its own independent switches. |
| Caching boundaries | **Partial.** `AiResultCacheService` / `AiExplanationCacheService` exist. Companion cache keys must include entitlement class, profession, exam version and locale, and must never cross a user boundary. |
| Memory compaction | **Missing.** Threads persist in full; no compaction budget. |
| Incident response and rollback | **Partial.** `docs/ROLLBACK.md`, blue/green deploy, provider failover. Knowledge-release rollback independent of app deploy does not exist. |
| Prompt-injection boundary | **Partial.** Grounding is enforced structurally; retrieved content is not yet separated from policy because retrieval does not exist yet. |
| Content exfiltration defence | **Missing.** No verbatim-span cap, retrieval-volume cap, reconstruction detection or canary monitoring. Required before any paid corpus is indexed. |
| Evaluation harness | **Missing.** No golden-set runner for retrieval, authority or hallucination. |
| Unit economics | **Partial.** Cost telemetry is strong; contribution modelling including support, refunds and human cost is not assembled. Depends on blocked tier decisions. |

## Decision records raised by this audit

### DR-001 - "One AI Credits wallet" maps onto the existing candidate ledger

**Source requirement:** exactly one user-facing premium currency called AI Credits.

**Repository reality:** three deliberately non-fungible ledgers - `AiPackageCredit*` (candidate-facing credits and attempts), `AiCreditLedger` (platform provider tokens and USD, admin-only) and `Wallet` (money-like prepaid balance for live classes and tutor minutes). `docs/OET_2026_MASTER_CATALOGUE_AI_CREDITS_ACCESS.md` states that provider tokens and candidate credits "are different ledgers and must never be numerically equated".

**Decision:** the source's "AI Credits" is `IAiPackageCreditService` and nothing else. No fourth wallet is created; `AiCreditLedger` and `Wallet` are never shown as candidate credit. The source ledger invariants are already met - immutable transaction history, atomic `Serializable` updates, idempotency via the unique `(ReferenceId, Reason)` index, reversal through `RefundAsync`, and reserve/commit/release through `IAiCreditReservationService` so a failed technical attempt never consumes credit.

**Affected:** F-139, F-140, F-141, F-134. **Risk if ignored:** a duplicate balance truth, which the source explicitly forbids.

### DR-002 - Plus/Pro/Ultimate tiers are blocked on a commercial decision

**Source requirement:** Free / Plus GBP 7.99 / Pro GBP 14.99 / Ultimate GBP 26.99 subscription tiers.

**Repository reality:** tiers are `anonymous|free|trial|paid`, and monetisation runs through a 47-product catalogue seeded from `Data/Seeds/oet-2026-catalog.json` with add-ons, eligibility rules and a hard 180-day access cap. Introducing parallel subscription tiers is a pricing and packaging decision, not an engineering one.

**Decision:** implement the *behaviour* the tiers are meant to produce - message allowance, feature power, memory depth, model priority - as `AiQuotaPlan`-backed capability policy, which already exists. Do not create new sellable products without explicit owner sign-off. Source prices remain unapproved placeholders (TV-018).

**Affected:** F-136, F-137, F-138, F-144, and the per-tier cost ceilings in F-151.

### DR-003 - Voice role-play already ships ahead of the source stage

The source places Speaking voice role-play at Stage 3 behind a feasibility gate. This repository already ships it: `Services/Conversation/**` with four ASR providers, TTS, a SignalR hub, consent versioning, audio retention and rubric evaluation. **Decision:** F-065/F-067/F-088 are recorded as shipped. The companion must not duplicate or absorb the conversation module; the Stage 3 gate still applies to *new* companion-driven live voice.

## Feature-by-feature mapping

### Identity & onboarding (F-001 ... F-012)

| ID | Requirement | Status | Repository evidence | Gap / work required | Stage | Risk |
|---|---|---|---|---|---|---|
| F-001 | Anonymous demo mode | `PARTIAL` | `EffectiveEntitlementSnapshot.Tier == "anonymous"`; `proxy.ts` PUBLIC_PATHS allow unauthenticated routes. | No anonymous companion demo, no ~3-interaction cap, no consented lead capture. | Stage 1 | Medium |
| F-002 | Registered Free mode | `EXISTS` | Tier `free` in `EffectiveEntitlementSnapshot`; `AiQuotaPlans` + `IAiQuotaService` meter per feature code. | Needs a companion-specific quota plan row; no change to the tier model itself. | Stage 1 | Low |
| F-003 | Profession selection | `EXISTS` | `LearnerUser.ActiveProfessionId`, `LearnerGoal.ProfessionId`, `Services/Professions/**`; 13 professions per `CONTEXT.md`. | None. Companion reads this as a core retrieval dimension. | Stage 1 | Low |
| F-004 | Exam/version selection | `PARTIAL` | `LearnerGoal.ExamFamilyCode`/`ExamTypeCode`/`TargetExamMode`, `LearnerUser.ActiveExamTypeCode`. | No effective-dated exam *version* with current/retired state, so exam-date to version resolution is impossible. | Stage 2 | Medium |
| F-005 | Exam date and target | `EXISTS` | `LearnerGoal.TargetExamDate` + per-subtest `Target*Score` + `OverallGoal`; `app/goals`; `hooks/use-exam-date-gate.ts` (AuthGuard redirects to `/goals?required=examDate`). | None for capture. Replanning on change is F-036. | Stage 1 | Low |
| F-006 | Country/regulator goal | `EXISTS` | `LearnerGoal.TargetCountry`/`TargetOrganization`, `Services/TargetCountryOptions.cs`; Writing country-aware pass already depends on it. | None. | Stage 1 | Low |
| F-007 | Previous result capture | `PARTIAL` | `LearnerGoal.PreviousAttempts`, `WeakSubtestsJson`, `ConfidenceLevel`; `LearnerExamOutcome` records admin-verified outcomes. | No learner-entered prior official per-subtest scores as a baseline record. | Stage 2 | Low |
| F-008 | Screenshot score import | `MISSING` | OCR substrate exists: `Services/Ai/OcrService.cs`, `MistralOcrClient.cs`, `GeminiNativeProvider` multimodal. | No score-report import flow and no confirm-before-save step (source requires explicit confirmation). | Stage 2 | Medium |
| F-009 | Study availability | `PARTIAL` | `LearnerGoal.StudyHoursPerWeek`, `LearnerSettings.StudyJson`, `app/goals/study-commitment`, `setStudyCommitment`. | No shift pattern, days off, preferred times or travel/leave model. Blocks F-037/F-038. | Stage 2 | Medium |
| F-010 | Language/code-switch preference | `PARTIAL` | `LearnerUser.Locale`; `i18n.ts` SUPPORTED_LOCALES `[en, ar]`; `app/layout.tsx` sets `<html dir>`. | No per-learner or per-message code-switch preference for the companion. | Stage 1 | Low |
| F-011 | Teaching-style preference | `MISSING` | No teaching-style or explanation-depth field anywhere in `Domain/**`. | Add to companion learning memory as an explicit, user-editable preference. | Stage 1 | Low |
| F-012 | Multiple historical exam journeys | `MISSING` | One `LearnerGoal` row per user; no journey table and no resit linkage. | Journey-scoped schema needed so plans/memory/mastery survive a resit without losing history. | Stage 2 | Medium |

### Knowledge & grounding (F-013 ... F-029)

| ID | Requirement | Status | Repository evidence | Gap / work required | Stage | Risk |
|---|---|---|---|---|---|---|
| F-013 | All approved Rule Books | `PARTIAL` | 115 files under `rulebooks/{kind}/{profession}/rulebook.v1.json`; `DbBackedRulebookLoader` prefers admin-managed `RulebookVersions`/`RulebookRuleRows` over embedded JSON (60s cache). | Authored and versioned but NOT retrievable - no knowledge index exists over them. Core Stage 1 corpus. | Stage 1 | High |
| F-014 | All relevant session transcripts | `PARTIAL` | `LiveClassRecordingProcessingService` transcribes, summarises and embeds; `ClassRecordingEmbedding.EmbeddingJson` uses in-process cosine, not pgvector. | Not in a shared companion index; embeddings stored as JSON text rather than `vector(1536)`. | Stage 2 | Medium |
| F-015 | Writing workshops | `MISSING` | No workshop or correction-session entity in `Domain/**`. Source media exists on disk under `OET Materials & Videos Data/` (379 PDFs, 118 MP4, 115 MP3). | Requires the Stage 2 content-ops pipeline: register, extract, tag, approve, release. | Stage 2 | Medium |
| F-016 | Speaking workshops | `MISSING` | No workshop or correction-session entity in `Domain/**`. Source media exists on disk under `OET Materials & Videos Data/` (379 PDFs, 118 MP4, 115 MP3). | Requires the Stage 2 content-ops pipeline: register, extract, tag, approve, release. | Stage 2 | Medium |
| F-017 | Correction sessions | `MISSING` | No workshop or correction-session entity in `Domain/**`. Source media exists on disk under `OET Materials & Videos Data/` (379 PDFs, 118 MP4, 115 MP3). | Requires the Stage 2 content-ops pipeline: register, extract, tag, approve, release. | Stage 2 | Medium |
| F-018 | Reading/Listening materials | `PARTIAL` | `ContentPaper`/`ContentPaperAsset`/`MediaAsset` with the `CandidateVisible` publish gate; entitlement-gated by `IContentEntitlementService`. | Served to learners but not chunked or retrievable by the companion. | Stage 2 | Medium |
| F-019 | Tutor Book | `PARTIAL` | `Domain/TutorBookEntities.cs`, `Services/TutorBook/**`, 180-day fulfilment cap. | Not retrievable; recalls and updates are not indexed. | Stage 2 | Medium |
| F-020 | Dictionaries/common-word lists | `PARTIAL` | `SeedData.VocabularyBank*.cs`, `VocabularyService`, `VocabularyGlossService`, `app/vocabulary`. | Not retrievable as companion evidence. | Stage 2 | Low |
| F-021 | Recalls/practice banks | `PARTIAL` | `Services/Recalls/**`, `RecallSetCodes.cs`, `RecallSetTagEntity.cs`; a `search_recall_set` learner tool already exists in `Services/AiTools/Tools/BuiltInTools.cs`. | Tool exists but is granted to no companion feature and has no grounded citation path. | Stage 2 | Medium |
| F-022 | Video timestamp index | `PARTIAL` | `LibraryVideo`, `VideoCaptionTrack`, `VideoCategory` in `Domain/VideoLibraryEntities.cs`; live-class transcripts carry timestamps. | No unified transcript-segment index keyed by video id + timestamp. Blocks F-093/F-094/F-099. | Stage 2 | Medium |
| F-023 | Platform navigation map | `MISSING` | `PlatformLinkService.BuildWebUrl/BuildApiUrl/BuildCheckoutUrl` builds URLs, but no structured destination registry exists. | Navigator needs route id, title, required entitlement, deep-link resolver and retired/renamed state. Blocks F-098..F-101. | Stage 1 | High |
| F-024 | Support/FAQ knowledge | `MISSING` | `CustomerSupportCaseService` handles cases; no support or FAQ knowledge articles exist. | A minimal support corpus is needed for Stage 1 deflection. | Stage 1 | Medium |
| F-025 | Official current exam sources | `MISSING` | No official-facts store; no `OfficialFact`/`ExamPolicy` entity anywhere. | Needs effective-dated official source records with a review cadence. External sourcing required. | Stage 2 | High |
| F-026 | Source hierarchy | `MISSING` | No authority-class concept anywhere in `Domain/**`. All current grounding is methodology (rulebooks). | Baseline official-vs-methodology separation is required in Stage 1 per DEC-003; full precedence in Stage 2. | Stage 1 | High |
| F-027 | Content versioning | `PARTIAL` | `RulebookVersions` + `RulebookSectionRows`/`RulebookRuleRows`; `ContentPaper` publish + `CandidateVisible`. | No knowledge-release object binding source versions, index, evaluation report and rollback target. | Stage 1 | Medium |
| F-028 | Teacher override | `PARTIAL` | `Services/Rulebooks/RulebookAdminService.cs` supports admin edit/approve/publish with audit. | No companion-scoped override carrying approver, effective dates and change reason. | Stage 2 | Medium |
| F-029 | Conflict detection | `MISSING` | Nothing detects disagreement between sources. | Companion must surface conflicts, never blend them. Baseline required in Stage 1 retrieval. | Stage 1 | High |

### Planning & memory (F-030 ... F-047)

| ID | Requirement | Status | Repository evidence | Gap / work required | Stage | Risk |
|---|---|---|---|---|---|---|
| F-030 | Initial diagnostic | `PARTIAL` | Listening/Reading/Writing pathway generators; `AdaptiveDifficultyService` (Elo, K=32, seed 1500) establishes a starting skill level. | No companion-led diagnostic conversation that seeds the plan. | Stage 2 | Medium |
| F-031 | Daily plan | `EXISTS` | `IStudyPlanGenerator`/`StudyPlanGenerator` with `ContentPicker`, `RationaleBuilder`, `ReviewItemInjector`, `StudyPlanEntitlementResolver`, `GenerationInputsHasher`; `StudyPlanTemplateWeek`/`Days`; `app/study-plan`. | None. Companion exposes this as a read tool rather than re-implementing planning. | Stage 1 | Low |
| F-032 | Weekly plan | `EXISTS` | `IStudyPlanGenerator`/`StudyPlanGenerator` with `ContentPicker`, `RationaleBuilder`, `ReviewItemInjector`, `StudyPlanEntitlementResolver`, `GenerationInputsHasher`; `StudyPlanTemplateWeek`/`Days`; `app/study-plan`. | None. Companion exposes this as a read tool rather than re-implementing planning. | Stage 1 | Low |
| F-033 | 30/60/90-day plans | `PARTIAL` | `StudyPlanTemplateSeeder` ships `free-8wk-standard` (6-10wk), `premium-12wk-targeted` (10-16wk) and `premium-4wk-retake` (2-5wk); `StudyPlanTemplateSelector` matches on week range. | The 8/12-week templates cover ~60d and ~90d; a distinct 30-day horizon is not templated. | Stage 2 | Low |
| F-034 | 14/7/3-day plans | `MISSING` | The shortest seeded template floor is `MinWeeks = 2`. | Nothing below 2 weeks exists, so 14/7/3-day intensive plans cannot be selected. | Stage 2 | Medium |
| F-035 | Exam-eve plan | `MISSING` | No exam-eve template or logistics checklist in the seeder. | Needs a rehearsal/logistics/confidence plan type that avoids new-content overload. | Stage 2 | Low |
| F-036 | Missed-day replanning | `PARTIAL` | `app/study-plan/drift` surface exists; `StudyPlanGenerator` + `GenerationInputsHasher` make regeneration reproducible from stored inputs. | Verify drift triggers a real replan that explains dropped or delayed items instead of carrying all overdue work. | Stage 2 | Medium |
| F-037 | Shift-worker planning | `MISSING` | Availability is a single `StudyHoursPerWeek` integer. | Blocked on F-009: no shift, day-off or travel model to plan around. | Stage 2 | Medium |
| F-038 | Travel-aware replanning | `MISSING` | Availability is a single `StudyHoursPerWeek` integer. | Blocked on F-009: no shift, day-off or travel model to plan around. | Stage 2 | Medium |
| F-039 | What should I do now? | `EXISTS` | `LearnerActionsService.GetNextActionsAsync` / `GetReadinessBlockersAsync`; `app/next-actions`; `ContentPicker` + `RationaleBuilder` supply the eligible target and a stored rationale. | None for the engine. Companion surfaces it conversationally and adds the one-click action (F-100). | Stage 1 | Low |
| F-040 | Next-best-action engine | `EXISTS` | `LearnerActionsService.GetNextActionsAsync` / `GetReadinessBlockersAsync`; `app/next-actions`; `ContentPicker` + `RationaleBuilder` supply the eligible target and a stored rationale. | None for the engine. Companion surfaces it conversationally and adds the one-click action (F-100). | Stage 1 | Low |
| F-041 | Conversation memory | `EXISTS` | `AiAssistantThread`/`AiAssistantMessage` persist server-side; `ArchiveThreadAsync` clears; SignalR rehydrates across devices. | No compaction budget, so long threads grow token cost without bound. | Stage 1 | Medium |
| F-042 | Learning memory | `PARTIAL` | Evidence is spread across attempts, evaluations, `LearnerSkillProfile`, `ReviewItem` and tutor feedback. | No structured learning memory with provenance, confidence and a user correction path. | Stage 1 | High |
| F-043 | Journey memory | `MISSING` | No journey-scoped longitudinal record (blocked on F-012). | Needed for "what has worked for this learner over time". | Stage 2 | Medium |
| F-044 | Error DNA | `MISSING` | Substrate exists: `ReviewItem`, `SpacedRepetitionEntities`, escalations, rule ids on rulebook findings. | No error taxonomy, occurrence/trend counts, mark-impact evidence or remediation state. | Stage 2 | High |
| F-045 | Learning Fingerprint | `MISSING` | Nothing derives explainable learner traits. | Stage 3 per source; requires F-042 and F-044 first. | Stage 3 | Low |
| F-046 | Spaced reinforcement | `EXISTS` | `SpacedRepetitionService`, `Sm2Scheduler`, `ReviewItem`, `ReviewItemInjector`, `app/review`, vocabulary flashcards. | None. Companion schedules through this, never a parallel scheduler. | Stage 1 | Low |
| F-047 | Memory controls/export/delete | `MISSING` | `LearnerSettings.PrivacyJson` and `DataRetentionWorker` exist; `app/(auth)/account-deletion` exists. | No view/edit/delete/reset for companion learning memory specifically. Privacy-critical. | Stage 1 | High |

### Tutoring (F-048 ... F-071)

| ID | Requirement | Status | Repository evidence | Gap / work required | Stage | Risk |
|---|---|---|---|---|---|---|
| F-048 | Quick answer | `PARTIAL` | `AiAssistantOrchestrator.RunTurnAsync` streams answers through `IAiAssistantGateway`; `LearnerSystemPrompt` is a generic tutor persona. | No mode selection, no rulebook grounding, no citations. This is the S1.1 slice. | Stage 1 | High |
| F-049 | Detailed tutor | `PARTIAL` | `AiAssistantOrchestrator.RunTurnAsync` streams answers through `IAiAssistantGateway`; `LearnerSystemPrompt` is a generic tutor persona. | No mode selection, no rulebook grounding, no citations. This is the S1.1 slice. | Stage 1 | High |
| F-050 | Socratic tutor | `MISSING` | No teaching-mode concept in `SystemPromptProvider`. | Socratic and Coach modes are prompt/policy work on top of S1.1. | Stage 2 | Low |
| F-051 | Examiner mode | `PARTIAL` | Examiner behaviour exists in grading: `WritingRuleEngine`, `SpeakingRuleEngine`, `SpeakingEvaluationPipeline`, `_exam-mode` rulebooks. | Not exposed as a conversational examiner mode. | Stage 2 | Low |
| F-052 | Coach mode | `MISSING` | No teaching-mode concept in `SystemPromptProvider`. | Socratic and Coach modes are prompt/policy work on top of S1.1. | Stage 2 | Low |
| F-053 | Dr Hesham mode | `PARTIAL` | The 115 rulebooks are the Dr Hesham method and already drive `RulebookPromptBuilder` grounding for grading features. | Not selectable as a companion mode, and not applied to companion chat at all today. | Stage 1 | High |
| F-054 | Arabic explanation | `PARTIAL` | Arabic course materials exist under `OET Materials & Videos Data/*/Arabic`; `ar` locale and RTL are wired at the document root. | No Arabic explanation mode that preserves English clinical and exam terminology. | Stage 2 | Medium |
| F-055 | English-only mode | `MISSING` | No immersion or English-only mode. | Prompt/policy work on top of S1.1. | Stage 2 | Low |
| F-056 | Adaptive drill generator | `PARTIAL` | `WritingDrill`, `SpeakingDrillItem`, `RemediationPlanService`, `RemediationCatalog`, `AdaptiveDifficultyService`. | Drills are catalogue-driven; no companion-generated drill targeting a specific weakness. | Stage 2 | Medium |
| F-057 | Micro-lessons | `DEFERRED_BY_SOURCE` | Grammar lessons exist (`app/grammar/[lessonId]`, `Services/Grammar/**`). | Appendix places micro-lessons at Stage 4-5 (DEC-004). Kept in backlog, not dropped. | Stage 4-5 | Low |
| F-058 | Grammar tutor | `EXISTS` | `Services/Grammar/{GrammarRulebookService,GrammarDraftService,GrammarEntitlementService,GrammarPublishGateService}`; `app/grammar`; grammar rulebooks for 7 professions; `SeedData.GrammarSpecs1-4`. | Not yet reachable from the companion as a tool. | Stage 1 | Low |
| F-059 | Vocabulary brain | `EXISTS` | `VocabularyService`, `VocabularyGlossService`, `app/vocabulary/{browse,flashcards,quiz}`, SM-2 scheduling, `lookup_vocabulary_term` tool. | Not yet granted to a companion feature code. | Stage 1 | Low |
| F-060 | Writing guided mode | `PARTIAL` | `WritingCoachService` (597 lines) + `WritingCoachHub` stream inline suggestions; feature codes `writing.coach.suggest` and `writing.coach.explain`. | Guided, hint and compare modes are not distinct behaviours and are not companion-addressable. | Stage 2 | Medium |
| F-061 | Writing hint mode | `PARTIAL` | `WritingCoachService` (597 lines) + `WritingCoachHub` stream inline suggestions; feature codes `writing.coach.suggest` and `writing.coach.explain`. | Guided, hint and compare modes are not distinct behaviours and are not companion-addressable. | Stage 2 | Medium |
| F-062 | Writing correction | `EXISTS` | `WritingSubmissionEvaluationPipeline`, `WritingRuleEngine`, `WritingAssessmentPreflightService`; rule-cited corrections. | None. Companion links to it rather than re-grading. | Stage 1 | Low |
| F-063 | Writing compare/rewrite | `PARTIAL` | `WritingCoachService` (597 lines) + `WritingCoachHub` stream inline suggestions; feature codes `writing.coach.suggest` and `writing.coach.explain`. | Guided, hint and compare modes are not distinct behaviours and are not companion-addressable. | Stage 2 | Medium |
| F-064 | Writing scoring/criteria feedback | `EXISTS` | Criterion feedback plus scaled score via `OetScoring` (anchor 30/42 == 350/500); Writing country-aware pass. | The existing graded product is unaffected. The calibration gate (TV-006/TV-007) applies to *companion-asserted* bands only. | Stage 1 | Medium |
| F-065 | Speaking voice role play | `EXISTS` | `Services/Conversation/**` full voice role-play: ASR (Azure/Deepgram/Whisper/ElevenLabs), TTS (ElevenLabs), `ConversationHub`, `ConversationAiOrchestrator`, `docs/CONVERSATION.md`. | Source places this at Stage 3; this repo already ships it. Companion must not duplicate or absorb it. | Shipped | Low |
| F-066 | Speaking difficult-patient modes | `PARTIAL` | `Services/Speaking/SpeakingSimulationV11PersonaService.cs`, `ConversationTemplate` scenarios, interlocutor scripts. | Difficulty personalities are not a documented, selectable matrix. | Stage 2 | Low |
| F-067 | Speaking pronunciation/fluency analysis | `EXISTS` | `PronunciationService`, `Services/Pronunciation/**`, Azure phoneme + Gemini native-audio scoring, `rulebooks/pronunciation/**` for 10 professions. | Scope must stay within the validated pronunciation feature set (TV-011). | Shipped | Low |
| F-068 | Reading Part A/B/C coach | `PARTIAL` | `Services/Reading/ReadingExplanationService.cs` (`reading.explanation.v1`, `reading.passage_qna.v1`), reading pathway analytics, `_exam-mode` rulebooks. | No per-part A/B/C coaching thread and no distractor-susceptibility analysis. | Stage 2 | Medium |
| F-069 | Listening Part A/B/C coach | `PARTIAL` | Listening explanations, `ListeningPathwayAnalyticsService`, `ListeningPathwayGenerator`, dictation surface. | No spelling/accent/prediction-failure taxonomy feeding Error DNA. | Stage 2 | Medium |
| F-070 | Confidence-vs-accuracy analysis | `PARTIAL` | `LearnerGoal.ConfidenceLevel`; `ConfidenceBadge` UI primitive. | No per-question confidence capture compared against accuracy. | Stage 2 | Low |
| F-071 | Why did my score change? | `MISSING` | `LearnerActionsService.GetProgressTrendAsync` and `app/progress` show the trend. | No evidence-based causal explanation of a score change. | Stage 2 | Medium |

### Exam & analytics (F-072 ... F-085)

| ID | Requirement | Status | Repository evidence | Gap / work required | Stage | Risk |
|---|---|---|---|---|---|---|
| F-072 | Full mock mode | `EXISTS` | `MockService`, `MockItemAnalysisService`, `Services/Mocks/**`, `app/mocks`, `components/domain/mock-player`, `MockEntitlementService`; Full Mock Attempts never consume AI credits. | None. | Shipped | Low |
| F-073 | Practice vs Exam mode separation | `EXISTS` | Exam mode enforced server-side in `ListeningLearnerService`, `ReadingAttemptService` and `MockService`; `rulebooks/*/_exam-mode/rulebook.v1.json`. | The companion must honour it (F-155) and does not today. | Stage 1 | High |
| F-074 | Computer-based rehearsal | `EXISTS` | `MockService`, `MockItemAnalysisService`, `Services/Mocks/**`, `app/mocks`, `components/domain/mock-player`, `MockEntitlementService`; Full Mock Attempts never consume AI credits. | None. | Shipped | Low |
| F-075 | Test-day rehearsal | `PARTIAL` | `app/test-day` surface exists. | No logistics checklist or rehearsal driven by the companion. | Stage 2 | Low |
| F-076 | Final 24-hours mode | `MISSING` | No final-24-hours mode. | Depends on short-horizon plans (F-034/F-035). | Stage 2 | Low |
| F-077 | Result-day assistant | `MISSING` | `LearnerExamOutcome` records an admin-verified pass or fail and expires the package. | No result-day assistant creating a success or resit path. | Stage 2 | Medium |
| F-078 | Resit recovery plan | `PARTIAL` | `premium-4wk-retake` "Retake Rescue" template exists. | Not triggered from a recorded outcome; no resit journey link (F-012). | Stage 2 | Medium |
| F-079 | Readiness score | `EXISTS` | `Services/Readiness/{ReadinessComputationService,ReadinessBlockerRules,ReadinessForecastCalculator}`; `app/readiness`; `GetReadinessBlockersAsync`. | Must never be presented as a pass probability (F-153). | Stage 1 | Low |
| F-080 | Mastery map | `PARTIAL` | `LearnerSkillProfile` (`Domain/AdaptiveEntities.cs`) holds an Elo rating per skill. | No named mastery states, decay/review schedule or subskill map. | Stage 2 | Medium |
| F-081 | Trend analytics | `EXISTS` | `GetProgressTrendAsync`, `app/progress`, `app/progress/comparative`, `PredictionService`, `app/predictions`. | None. | Shipped | Low |
| F-082 | Timing analytics | `PARTIAL` | Attempt timing is captured across subtest services; `useTimer`, `components/ui/timer`. | No consolidated timing analytics view. | Stage 2 | Low |
| F-083 | Personal bests | `MISSING` | Streak fields exist on `LearnerUser`; `app/achievements`, `app/leaderboard`. | No personal-best or milestone record. | Stage 2 | Low |
| F-084 | Anonymous cohort benchmarking | `DEFERRED_BY_SOURCE` | `app/progress/comparative` exists. | Source lists cohort benchmarking as a 12-month non-goal until sample size, privacy and interpretation are responsible. | Deferred | Medium |
| F-085 | Weekly progress report | `MISSING` | Substrate exists: `NotificationCampaign`, `NotificationTemplate`, email and push dispatchers. | No weekly wins/risks/priorities report. | Stage 2 | Low |

### Multimodal & context (F-086 ... F-097)

| ID | Requirement | Status | Repository evidence | Gap / work required | Stage | Risk |
|---|---|---|---|---|---|---|
| F-086 | Image/screenshot understanding | `PARTIAL` | `OcrService`, `MistralOcrClient`, `GeminiNativeProvider` multimodal. | Not reachable from the companion; no tiered file metering. | Stage 2 | Medium |
| F-087 | PDF understanding | `PARTIAL` | OCR is used by the listening and reading authoring extraction pipelines. | No learner-facing PDF understanding with cost caps. | Stage 2 | Medium |
| F-088 | Audio analysis | `EXISTS` | ASR providers, `PronunciationService`, and the conversation audio pipeline with a retention worker. | Companion-side audio analysis is Stage 3 (voice gate). | Shipped | Low |
| F-089 | Handwritten note understanding | `MISSING` | No handwriting path. | Provider-dependent; Stage 2 at the earliest. | Stage 2 | Low |
| F-090 | Score report extraction | `MISSING` | Same substrate as F-008. | Requires explicit confirmation before saving extracted scores. | Stage 2 | Medium |
| F-091 | Current-page awareness | `MISSING` | No context envelope exists. The SignalR `StartTurn` contract carries no surface, resource or timestamp context. | Needs a bounded, server-resolved context envelope. Screen context must never widen entitlement. | Stage 2 | High |
| F-092 | Current-question awareness | `MISSING` | No context envelope exists. The SignalR `StartTurn` contract carries no surface, resource or timestamp context. | Needs a bounded, server-resolved context envelope. Screen context must never widen entitlement. | Stage 2 | High |
| F-093 | Current-video awareness | `MISSING` | No context envelope exists. The SignalR `StartTurn` contract carries no surface, resource or timestamp context. | Needs a bounded, server-resolved context envelope. Screen context must never widen entitlement. | Stage 2 | High |
| F-094 | Timestamp-aware help | `MISSING` | No context envelope exists. The SignalR `StartTurn` contract carries no surface, resource or timestamp context. | Needs a bounded, server-resolved context envelope. Screen context must never widen entitlement. | Stage 2 | High |
| F-095 | Writing selection awareness | `PARTIAL` | `WritingCoachHub` operates on a submission and streams suggestions. | No explicit selection range passed as bounded context. | Stage 2 | Medium |
| F-096 | Speaking-session context | `PARTIAL` | `ConversationHub` holds session state, turns and hashed resume tokens. | Not exposed to the companion as context. | Stage 3 | Low |
| F-097 | Cross-device conversation continuity | `EXISTS` | Threads persist server-side and rehydrate over SignalR; conversation sessions carry hashed resume tokens. | None. | Stage 1 | Low |

### Actions & navigation (F-098 ... F-112)

| ID | Requirement | Status | Repository evidence | Gap / work required | Stage | Risk |
|---|---|---|---|---|---|---|
| F-098 | Deep-link resource open | `PARTIAL` | `PlatformLinkService.BuildWebUrl`; `IEffectiveEntitlementResolver` + `IContentEntitlementService` can authorise a target. | No typed action with server-side target resolution. Model-generated URLs must never be followed. | Stage 1 | High |
| F-099 | Open exact timestamp | `MISSING` | Video timestamps exist per caption track and transcript. | Blocked on the F-022 timestamp index. | Stage 2 | Medium |
| F-100 | Start recommended practice | `PARTIAL` | `GetNextActionsAsync` returns eligible targets; `app/next-actions` links to them. | Not executable as a confirmed action from the conversation. | Stage 1 | Medium |
| F-101 | Continue last activity | `PARTIAL` | `GetNextActionsAsync` returns eligible targets; `app/next-actions` links to them. | Not executable as a confirmed action from the conversation. | Stage 1 | Medium |
| F-102 | Save note | `PARTIAL` | A `save_user_note` tool already exists in `Services/AiTools/Tools/BuiltInTools.cs`. | Granted to no companion feature code; no provenance into learning memory. | Stage 1 | Low |
| F-103 | Save vocabulary | `PARTIAL` | A `bookmark_recall_term` tool exists; vocabulary and SM-2 scheduling exist. | Granted to no companion feature code. | Stage 1 | Low |
| F-104 | Add to plan | `MISSING` | `StudyPlanItem` exists and is generated, but is never appended to conversationally. | Needs a typed, authorised add-plan-item action. | Stage 1 | Medium |
| F-105 | Set reminder | `PARTIAL` | `StudyPlanReminderWorker`, `NotificationScheduling`, `NotificationPreference` quiet hours. | Not reachable as a companion action. | Stage 2 | Low |
| F-106 | Open workshop | `MISSING` | Live classes and private speaking bookings exist; no workshop entity (F-015/F-016). | Blocked on the workshop model. | Stage 2 | Low |
| F-107 | Create support request | `PARTIAL` | `CustomerSupportCaseService`, `app/support`. | No contextual case creation carrying the conversation. | Stage 2 | Low |
| F-108 | Tutor handoff summary | `MISSING` | Expert and tutor surfaces exist (`app/expert`, `app/tutor`); no handoff summary. | Needs score trend, errors, adherence and recommended focus. | Stage 2 | Medium |
| F-109 | Report wrong answer | `PARTIAL` | Feedback entities across 18 domain files; `ReviewEscalations` + `AIEscalationStatsService`. | No companion-originated content-issue report. | Stage 2 | Low |
| F-110 | Show allowances/credits | `EXISTS` | `AiPackageCreditEndpoints`, `AiMeEndpoints`, `app/ai-packages`, `app/ai-usage`, `AiQuotaCounters`. | Companion needs to read it, not rebuild it. | Stage 1 | Low |
| F-111 | Contextual upgrade checkout | `PARTIAL` | `PlatformLinkService.BuildCheckoutUrl`; `BillingCheckoutEndpoints`; `ApiException.PaymentRequired` with `content_locked`. | Paywall is a generic error, not a contextual upgrade card with tier, price and benefits. | Stage 1 | Medium |
| F-112 | Resume chat after purchase | `MISSING` | Threads persist, so the conversation itself survives; entitlement is re-resolved per request. | No pending-action resume after checkout completes. | Stage 1 | Medium |

### Proactive & engagement (F-113 ... F-122)

| ID | Requirement | Status | Repository evidence | Gap / work required | Stage | Risk |
|---|---|---|---|---|---|---|
| F-113 | Exam countdown | `PARTIAL` | Exam date plus dashboard surfaces. | No countdown-driven plan adaptation. | Stage 3 | Low |
| F-114 | Inactivity risk nudges | `PARTIAL` | `EngagementService`, `NotificationRuleEngine`, `NotificationPolicyOverride`. | No inactivity-risk model. | Stage 3 | Low |
| F-115 | Weak-subtest reminder | `PARTIAL` | `NotificationRuleEngine` plus readiness blockers can identify a weak subtest. | Not wired into a reminder rule. | Stage 3 | Low |
| F-116 | New relevant content alert | `PARTIAL` | `NotificationCampaign` + `NotificationCampaignRecipient` with entitlement filtering. | Not driven by relevance to the individual learner. | Stage 3 | Low |
| F-117 | Calendar integration | `PARTIAL` | `app/study-plan/calendar`, `PrivateSpeakingCalendarService`, `ZoomMeetingService`. | No external calendar awareness of shifts or leave. | Stage 3 | Low |
| F-118 | Push/email/app progress summaries | `PARTIAL` | `MobilePushDispatcher`, `WebPushDispatcher`, `SoketiPushDispatcher`, `BrevoEmailSender`, `NotificationConsent`. | No progress-summary content. | Stage 3 | Low |
| F-119 | Quiet hours | `EXISTS` | Quiet hours on `NotificationPreference`, honoured by `NotificationScheduling`. | None. | Shipped | Low |
| F-120 | Streaks/milestones | `PARTIAL` | `LearnerUser.CurrentStreak`/`LongestStreak`/`WeeklyActivityJson`; `useStreak`; `app/achievements`, `app/leaderboard`. | No milestone recognition tied to demonstrated learning value. | Stage 3 | Low |
| F-121 | Adaptive gamification | `DEFERRED_BY_SOURCE` | Streaks and leaderboard exist. | Source defers gamification without demonstrated learning or conversion value. | Deferred | Low |
| F-122 | Referral/review prompt at positive moments | `PARTIAL` | `ReferralService`, `app/referral`, `app/reviews`. | Not gated to genuine positive moments; source forbids prompting during frustration. | Stage 3 | Medium |

### Admin/tutor (F-123 ... F-134)

| ID | Requirement | Status | Repository evidence | Gap / work required | Stage | Risk |
|---|---|---|---|---|---|---|
| F-123 | Tutor pre-session brief | `MISSING` | Expert console surfaces exist under `app/expert/**`. | No pre-session brief with trend, errors and adherence. | Stage 2 | Medium |
| F-124 | Tutor post-session notes | `PARTIAL` | Speaking moderation and assessment notes; `ExpertMessagingService`. | Tutor notes do not feed learner memory with provenance. | Stage 2 | Medium |
| F-125 | Admin aggregate AI queries | `PARTIAL` | `AiUsageAnalyticsService`, `app/admin/ai-analytics`, `AiUsageRecord` (feature, provider, model, cost). | No aggregation of learner *questions* by profession or subtest. | Stage 2 | Medium |
| F-126 | Quality dashboard | `PARTIAL` | `app/admin/ai-analytics`, `app/admin/escalations`, `AIEscalationStatsService`. | No AI-quality dashboard covering thumbs-down, low confidence and source conflicts. | Stage 2 | Medium |
| F-127 | Hallucination/low-confidence queue | `MISSING` | Escalation plumbing exists but is oriented to human review of gradings. | A hallucination queue and content/teaching-gap dashboards need companion telemetry first. | Stage 2 | Medium |
| F-128 | Content-gap dashboard | `MISSING` | Escalation plumbing exists but is oriented to human review of gradings. | A hallucination queue and content/teaching-gap dashboards need companion telemetry first. | Stage 2 | Medium |
| F-129 | Teaching-gap dashboard | `MISSING` | Escalation plumbing exists but is oriented to human review of gradings. | A hallucination queue and content/teaching-gap dashboards need companion telemetry first. | Stage 2 | Medium |
| F-130 | Rule approval/version/rollback | `PARTIAL` | `RulebookAdminService`, `RulebookVersions`, `AuditEvent` on admin writes. | No release/rollback object independent of an application deploy. | Stage 2 | Medium |
| F-131 | Content Studio extraction proposals | `PARTIAL` | `ContentGenerationService` job queue, `VocabularyDraftService`, `GrammarDraftService`, `GrammarPublishGateService`. | Drafts exist per module; no unified companion extraction-proposal queue. | Stage 2 | Medium |
| F-132 | Revenue/cost dashboard | `PARTIAL` | `AiBudgetService`, `AiBudgetAlertService`, `AiBudgetOverrideService`, `AiPricingResolver`, `AiLedgerReconciliationService`, admin billing pages. | No unified revenue-versus-AI-cost contribution view including support and refund cost. | Stage 2 | Medium |
| F-133 | Entitlement management | `EXISTS` | `UserAccessAllocationService` (grant/remove/suspend/restore/dates/addons/scopes), `app/admin/users/[id]`, fully audited. | None. | Shipped | Low |
| F-134 | Promotional AI Credits | `EXISTS` | `AiPackageCreditService.AdjustAsync(userId, request, adminId, ct)` with `AiPackageCreditReason.AdminAdjustment` and provenance. | Expiry and source on promotional grants should be checked against the source rules. | Stage 1 | Low |

### Commercial (F-135 ... F-151)

| ID | Requirement | Status | Repository evidence | Gap / work required | Stage | Risk |
|---|---|---|---|---|---|---|
| F-135 | Free message cap | `PARTIAL` | `IAiQuotaService.TryReserveAsync/CommitAsync`, `AiQuotaPlans`, `AiQuotaCounters`, `AiUserQuotaOverrides`; `QuotaPeriod` already supports `rolling_30d`. | No companion quota plan row; the free cap is not configured. | Stage 1 | Medium |
| F-136 | Plus tier | `BLOCKED` | Repo tiers are `anonymous\|free\|trial\|paid` plus a 47-product catalogue seeded from `Data/Seeds/oet-2026-catalog.json`. | Creating new sellable subscription tiers is a commercial decision (DR-002). Tier *behaviour* ships as quota and capability policy instead. | Blocked | High |
| F-137 | Pro tier | `BLOCKED` | Repo tiers are `anonymous\|free\|trial\|paid` plus a 47-product catalogue seeded from `Data/Seeds/oet-2026-catalog.json`. | Creating new sellable subscription tiers is a commercial decision (DR-002). Tier *behaviour* ships as quota and capability policy instead. | Blocked | High |
| F-138 | Ultimate tier | `BLOCKED` | As F-136/F-137, plus DEC-002: Ultimate must not be marketed before the Stage 3 voice and economics gates. | Schema and pricebook may exist early; public activation stays gated. | Blocked | High |
| F-139 | AI Credits | `EXISTS` | `IAiPackageCreditService` with `AiPackageCreditAccount`/`Lot`/`Transaction`; unique `(ReferenceId, Reason)` index; `Serializable` transactions; `IAiCreditReservationService` reserve/commit/release. | DR-001 maps the source "AI Credits" onto this ledger. No fourth wallet is created. | Stage 1 | Low |
| F-140 | Top-up packs | `EXISTS` | `pkg_*` credit packages in the catalogue; `AddonGrantProcessor`; `WalletTopUpTierConfig` for the separate money wallet. | Source price points remain placeholders pending TV-018. | Stage 1 | Low |
| F-141 | Usage counter | `EXISTS` | `app/ai-usage`, `AiQuotaCounters`, `AiPackageCreditSnapshot`, `CreditUsageInfoCard`. | Companion needs a visible in-chat counter. | Stage 1 | Low |
| F-142 | Contextual paywall | `PARTIAL` | `ApiException.PaymentRequired`; `mapErrorCodeToUserMessage` handles `no_ai_package_credits`, `ai_credits_insufficient` and `ai_package_expired`. | A dead error, not a personalised preview with a single CTA. | Stage 1 | Medium |
| F-143 | Course × AI entitlement matrix | `EXISTS` | `EffectiveEntitlementSnapshot` separates content scope from allowances; `PackageContentRules`, `CourseFamilyPolicy`, `PackageScopePolicy`. | None - this already is the two-axis model the source asks for. | Shipped | Low |
| F-144 | Standalone AI subscription | `PARTIAL` | `StandaloneAddonSubscriptions` sentinel; `pkg_*` AI packages are sold standalone. | Not a recurring AI-only subscription. | Blocked | Medium |
| F-145 | Course AI add-on | `EXISTS` | `BillingAddOn` + `AddonEligibilityService` (parent-enrolment rules) + `AddonGrantProcessor.ApplyAsync/ReverseAsync`. | None. | Shipped | Low |
| F-146 | Monthly billing | `EXISTS` | Stripe subscriptions; `FulfillRenewalAsync` idempotent on `(stripeSubscriptionId, currentPeriodStart)`; `SubscriptionStateMachine`; `SubscriptionExpiryWorker`. | None. | Shipped | Low |
| F-147 | Optional annual billing | `PARTIAL` | Billing periods exist on plan versions; regional gateways are registered. | Verify annual SKUs in `oet-2026-catalog.json` before claiming support. | Stage 2 | Low |
| F-148 | Upgrade/downgrade/cancel | `EXISTS` | `BillingSubscriptionEndpoints`, `SubscriptionStateMachine`, `RefundService`, `DisputeService`, dunning and retry. | None. | Shipped | Low |
| F-149 | Fair-use/rate limits | `EXISTS` | Rate-limit policies `PerUser`, `PerUserWrite`, `AiInteractive`, `AiInteractiveDay`, `AiScoring`, `AiLiveSpeaking`, `HubConnect`; `DailySafetyCapPct`. | Companion endpoints must adopt `AiInteractive`. | Stage 1 | Low |
| F-150 | Regional currency display | `PARTIAL` | `components/ui/price`, multi-gateway support (e.g. `easykash`), billing-country handling. | PPP bands and regional eligibility remain TV-023. | Stage 2 | Medium |
| F-151 | Cost ceiling alerts | `EXISTS` | `AiBudgetService` + `AiBudgetAlertService` with `AiBudgetPeriod`/`AiBudgetAlert`; ceilings are configurable. | Source per-tier ceilings depend on the new tiers (F-136..F-138), which are blocked. | Stage 1 | Medium |

### Trust & platform (F-152 ... F-167)

| ID | Requirement | Status | Repository evidence | Gap / work required | Stage | Risk |
|---|---|---|---|---|---|---|
| F-152 | Official-vs-methodology separation | `MISSING` | All grounding today is methodology (rulebooks). There is no official-fact authority class. | Baseline separation required in Stage 1 per DEC-003. | Stage 1 | High |
| F-153 | Anti-hallucination behavior | `PARTIAL` | `PromptNotGroundedException` forces a non-blank grounded system prompt; `AiFeaturePolicyRegistry` refuses disallowed features. | No unknown-answer contract and no unsupported-claim detection for the companion. | Stage 1 | High |
| F-154 | Entitlement-safe retrieval | `MISSING` | The gates exist (`IEffectiveEntitlementResolver`, `IContentEntitlementService.RequireAccessAsync`) but there is no retrieval to gate yet. | The entitlement prefilter must run BEFORE retrieval, with a recheck before any protected deep link. Zero-tolerance requirement. | Stage 1 | High |
| F-155 | Academic integrity controls | `PARTIAL` | Exam mode is enforced server-side in the listening, reading and mock services and in `_exam-mode` rulebooks. | The companion is not exam-mode aware and could leak hints. Must be gated before launch. | Stage 1 | High |
| F-156 | Sensitive upload warning | `MISSING` | `SecretScanner` exists for the dev assistant only. | No patient-identifier detection and no warn/redact path on uploads. | Stage 2 | High |
| F-157 | Privacy controls | `PARTIAL` | `LearnerSettings.PrivacyJson`, `NotificationConsent`, `DataRetentionWorker`, `AuthDataRetentionWorker`, `ConversationAudioRetentionWorker`. | No companion memory controls (see F-047). | Stage 1 | High |
| F-158 | Data export/delete | `PARTIAL` | `app/(auth)/account-deletion`, retention workers, `ConversationTranscriptExportService`. | No companion-scoped export or delete covering derived and indexed data. | Stage 2 | High |
| F-159 | Audit logs | `EXISTS` | `AuditEvent` written by ~79 services; `SecurityEventLogger`; one `AiToolInvocation` plus an audit row per tool call; admin write endpoints audited. | Companion actions and credit movements must join this. | Stage 1 | Low |
| F-160 | Accessibility - WCAG 2.2 AA across learner-facing web, web-app and native-app surfaces | `PARTIAL` | `AccessibilityProvider` (large text, high contrast, reduce motion), `tests/a11y/**`, `tests/e2e/shared/accessibility.spec.ts`, semantic UI primitives. | The new companion surface needs its own WCAG 2.2 AA pass; a11y defects in core flow are release defects. | Stage 1 | Medium |
| F-161 | Arabic RTL support | `PARTIAL` | `i18n.ts` `ar` locale, `app/layout.tsx` `dir` switch, `messages/ar/writing.json`. | Only Writing is internationalised (`MESSAGE_MODULES = [writing]`); the companion needs its own bundle. | Stage 1 | Medium |
| F-162 | iOS | `EXISTS` | Capacitor `ios/`, fastlane, VPS update feed. | Companion inherits the web app shell. | Shipped | Low |
| F-163 | Android | `EXISTS` | Capacitor `android/`, Play Console automation, `docs/app-release-playbook.md`. | Store AI-content reporting may be required (TV-032). | Shipped | Medium |
| F-164 | Windows | `EXISTS` | Tauri 2 desktop thin client (`src-tauri/`), Windows updater feed. | None. | Shipped | Low |
| F-165 | macOS | `PARTIAL` | Tauri supports macOS; the live update feed is windows-x86_64 only. | Source lists dedicated desktop apps as a non-goal where a PWA suffices. | Deferred | Low |
| F-166 | Website | `EXISTS` | Separate Astro site (`OET Project Website`), deployed independently. | The public companion demo (F-001) would live here. | Stage 1 | Medium |
| F-167 | Web app | `EXISTS` | Next.js 16 App Router web app - this repository. | None. | Shipped | Low |

### Expansion & B2B (F-168 ... F-184)

| ID | Requirement | Status | Repository evidence | Gap / work required | Stage | Risk |
|---|---|---|---|---|---|---|
| F-168 | IELTS pack | `PARTIAL` | `Services/IeltsMockEngine.cs`, `app/ielts-guide`, and the `ExamFamilyCode`/`ExamTypeCode` axis. | Not a full versioned knowledge and assessment pack. | Stage 4-5 | Low |
| F-169 | PTE pack | `PARTIAL` | `Services/PteScoring.cs`. | Scoring only; no content or assessment pack. | Stage 4-5 | Low |
| F-170 | TOEFL pack | `MISSING` | Nothing TOEFL-specific exists. | Stage 4 backlog. | Stage 4-5 | Low |
| F-171 | Future exam packs | `PARTIAL` | `ExamFamilyCode`/`ExamTypeCode`/`ActiveExamTypeCode` give an exam axis; rulebooks are keyed by kind x profession. | Global services still assume OET constants in places. | Stage 4-5 | Medium |
| F-172 | Exam-version engine | `MISSING` | No exam version, effective-date or retirement engine (see F-004). | Stage 4 backlog; F-004 is the Stage 2 precursor. | Stage 4-5 | Medium |
| F-173 | Cross-exam skill transfer | `MISSING` | No cross-exam skill transfer model. | Stage 4 backlog. | Stage 4-5 | Low |
| F-174 | B2B multi-tenancy | `MISSING` | `AiUsageRecord.TenantId` exists as a forward-compatible seam; nothing else is tenant-aware. | Avoid schema choices that would block isolation later. | Stage 4-5 | Medium |
| F-175 | White-label persona | `MISSING` | Persona is being introduced as configuration (`Companion:PersonaName`), which is white-label friendly. | Stage 5 backlog. | Stage 4-5 | Low |
| F-176 | Institution knowledge upload | `MISSING` | Persona is being introduced as configuration (`Companion:PersonaName`), which is white-label friendly. | Stage 5 backlog. | Stage 4-5 | Low |
| F-177 | Institution roles | `PARTIAL` | A `sponsor` role exists in `ApplicationUserRoles`; `app/sponsor/**`; `SponsorService`. | Sponsor is not a tenant; there is no institution role hierarchy. | Stage 4-5 | Low |
| F-178 | Seat/usage budgets | `PARTIAL` | `SponsorSeatPackService`, seat packs, `app/sponsor`. | No per-institution AI budget or active-user cap. | Stage 4-5 | Low |
| F-179 | Institution analytics | `PARTIAL` | Sponsor portal reporting. | No mastery, engagement, at-risk or content-gap analytics per institution. | Stage 4-5 | Low |
| F-180 | SSO | `MISSING` | `ExternalAuthService`/`ExternalIdentityProviderClient` are social login, not enterprise SSO. | Stage 5 backlog. | Stage 4-5 | Low |
| F-181 | API/webhooks | `PARTIAL` | Inbound webhooks (Stripe, PayPal, Brevo, Zoom, LiveKit, Bunny) and admin APIs exist. | No partner-facing API or outbound webhook contract. | Stage 4-5 | Low |
| F-182 | LMS integration | `MISSING` | No LMS integration. | Stage 5 backlog. | Stage 4-5 | Low |
| F-183 | Custom retention | `PARTIAL` | `DataRetentionWorker`, `AuthDataRetentionWorker`, `ConversationOptions.AudioRetentionDays`. | Retention is global, not per-contract. | Stage 4-5 | Low |
| F-184 | Enterprise audit/support | `PARTIAL` | `AuditEvent`, `SecurityEventLogger`, `CustomerSupportCaseService`. | No enterprise audit export or contracted support tier. | Stage 4-5 | Low |

## Open TO VERIFY gates that block production behaviour

These are recorded, not resolved. Engineering builds the configuration, schema and flags; the decision stays external. See `TO_VERIFY_AND_DECISION_REGISTER.md` for all 46.

| Gate | Blocks | Engineering preparation in this program |
|---|---|---|
| TV-002 first OET profession for beta | F-013 corpus scope | Profession-scoped rollout flag on the index |
| TV-006 / TV-007 Writing / Speaking calibration | Companion-asserted numeric bands | `companion.score_display.enabled` flag, default OFF; criterion feedback only |
| TV-018 net-revenue-per-credit multiple | F-140 top-up pricing | Pricebook stays configuration |
| TV-023 regional PPP bands | F-150 | Region pricebook + billing-country controls |
| TV-027 UK GDPR / DPIA map | F-157, F-158 | Data-flow inventory and retention controls |
| TV-030 Jana / Sami clearance | Persona naming | `Companion:PersonaName` config key, no hard-coded literal |
| TV-032 store AI-content reporting | F-163 | In-app report path for AI output |
| TV-035 kill-switch owner | Emergency controls | Role config and audit; switches exist regardless |
| TV-036 learner-distress escalation owner | Safety runbook | Escalation route wired, owner named externally |

## Immediate consequences for the build

1. **F-154 is the gating requirement.** Entitlement filtering must happen before retrieval, not after. No corpus may be indexed until the prefilter and the pre-deep-link recheck exist.
2. **F-023 blocks the navigator.** Without a structured destination registry, F-098..F-101 cannot be built safely, because the alternative is following model-generated URLs.
3. **F-155 must land with the first release.** The companion is not exam-mode aware today; shipping it unguarded would let a learner obtain hints during a protected attempt.
4. **F-047 is privacy-critical.** Durable learning memory cannot ship before the learner can see, correct and delete it.
5. **The action registry already exists.** `IAiToolRegistry` + `AiToolInvoker` + `AiFeatureToolGrant` provide deny-by-default grants, JSON-schema validation, one `AiToolInvocation` row and one `AuditEvent` per call. Companion actions are grants against a new feature code, not a new subsystem.
