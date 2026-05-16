# PRD — Listening Module V2 (Rulebook + Academy Platform)

> **Status**: Active · **Owner**: agency-all-rounder orchestration · **Started**: 2026-05-11
>
> Source documents:
>
> 1. `deepseek_markdown_20260511_dfdc38.md` — OET Listening Platform Rulebook (academy product spec — dashboards, pathway, content tooling)
> 2. `deepseek_markdown_20260510_d2e079 ---- LISTENING.md` — OET Listening Comprehensive Rulebook (R-coded canonical rules R01–R10)
>
> User decisions (from clarification popup, 2026-05-11, refreshed after attached rulebook review):
>
> - **Scope**: Finish all remaining Listening V2 gaps end-to-end; update `PRD-LISTENING-V2.md` / `PROGRESS-LISTENING-V2.md` first, then implement.
> - **Canonical sources**: Both attached markdown documents are authoritative for the new wave. Existing `docs/LISTENING.md`, rulebook JSON, and this PRD must be reconciled to them.
> - **Modes**: CBT/computer strict mock, Learning mode with replay/transcript, Paper simulation, OET@Home fullscreen simulation, Diagnostic placement, and Part A/B/C trainers are all in scope.
> - **Navigation locks**: Strict — all R06 rules + confirm dialogs + unanswered warnings
> - **Timing source**: Rulebook defaults with admin-overridable per-paper policy.
> - **Audio integrity**: Client lock + server FSM/audio-resume checks; network recovery resumes from last server-known playhead with grace. No backend audio proxy for this wave.
> - **Marking**: Part A uses strict normalized exact matching plus admin-authored accepted variants. No partial credit. AI may suggest variants, but admins approve them. Human overrides are post-submission only and require an audit reason.
> - **Authoring workflow**: Draft → Review → Publish with required lint.
> - **Publish gate**: 42 items with 24/6/12 split, audio extract/cue timings, transcript evidence timestamps, skill tag for every question, distractor category for every wrong MCQ option, difficulty rating, and source-provenance/legal attestation.
>   Wave 5 implementation requires `SourceProvenance` to include an exact positive `legal=<allowed>` attestation token until a dedicated structured attestation field is introduced.
> - **AI/OCR**: PDF/audio-script extraction, transcript time-coding, skill-tag, distractor-category, and accepted-variant suggestions are allowed through platform-only AI keys; never learner/admin BYOK.
> - **Highlighting**: Highlights/strikethrough are ephemeral during the attempt. B/C get R08 tools; Part A uses no highlight tools in strict mode.
> - **Accessibility**: Browser/system zoom must remain allowed for accessibility; do not globally block it as the only exam behavior.
> - **OET@Home integrity**: Leaving fullscreen warns the learner and records telemetry; the attempt continues.
> - **Scoring**: Existing `OetScoring` (30/42 ≡ 350) + per-Part + per-skill-tag breakdown + estimate disclaimer
> - **Dashboards**: Learner + single-owner Teacher/class + Admin per-question deep-dive + 12-stage pathway. First KPIs: raw/scaled estimate + disclaimer, A/B/C breakdown, skill weaknesses, Part A error types, distractor heatmaps, timing/preview issues, readiness RAG, class averages/hardest questions, admin question deep-dive.
> - **Authoring extras**: skill tags, distractor categories, transcript evidence timestamps, accent+speaker metadata, difficulty rating, time-coded transcripts
> - **Pathway**: Full 12-stage pathway with rule-based thresholds from attempts.
> - **Entitlements**: Whole paper gated by plan/tier. If entitlement changes mid-attempt, the current attempt can finish; new starts are blocked.
> - **Content legality**: Admin original/legal-content attestation + audit log on every publish are required.
> - **Retention / exports**: Configurable retention days, teacher CSV/Excel export, admin-only full attempt JSON export, audio assets retained until paper retirement.
> - **Ops**: Production uses local VPS volume + manual Listening V2 backfill after deploy.
> - **Audio**: Hard-block pause/seek in exam mode; auto-advance with timing windows; replay only in Learning mode
> - **Subagents**: 10 in waves (researcher, architect, planner, critic, implementer×2, designer, reviewer, debugger, devops, docs)
> - **Breaking changes**: Additive migrations only, backfill defaults, no destructive drops
> - **Validation bar**: TypeScript check, lint, Vitest, backend build/tests, Next production build, focused Listening Playwright E2E, full Playwright matrix, docs/rulebook citation checklist.

---

## 1. Goals

- Make the Listening exam engine **rulebook-faithful** to R01–R10 (every R-code mapped to code + test).
- Ship **5 delivery modes** sharing one canonical state machine: CBT, Paper, OET@Home, Learning, Diagnostic.
- Deliver **academy platform** features: learner/teacher/admin dashboards, 12-stage course pathway, per-question deep-dive, transcript-evidence review, skill-tag analytics.
- Extend authoring with skill tags, distractor categories, per-answer evidence timestamps, accent/speaker metadata, time-coded transcripts.
- Maintain mission-critical invariants from `AGENTS.md`: scoring through `OetScoring` (30/42 ≡ 350), AI through grounded gateway, content via `ContentPaper`/`MediaAsset`/`IFileStorage`, additive migrations only.

## 2. Non-Goals

- New AI provider implementations (use existing `IAiGatewayService`).
- Re-architecting `ContentPaper`/`MediaAsset` storage.
- Reading/Writing/Speaking module changes (Listening only).
- Live proctoring infrastructure (OET@Home simulates the UX only).

## 3. Workstreams

| ID | Stream | Lead subagent | Files (primary) |
| ---- | -------- | --------------- | ----------------- |
| WS-A | Schema + migrations + policy defaults | agency-implementer (backend) | `backend/src/OetLearner.Api/Domain/ListeningEntities.cs`, `Data/Migrations/*`, `Services/Listening/ListeningPolicyDefaults.cs` |
| WS-B | Navigation state machine + grading + dashboards APIs | agency-implementer (backend) | `Services/Listening/ListeningSessionService.cs`, `Services/Listening/ListeningGradingService.cs`, `Endpoints/Listening/*` |
| WS-C | Active player route (R06 locks, R08 tools, timing) | agency-implementer (frontend) | `app/listening/player/[id]/page.tsx`, `components/domain/listening/player/*`, `lib/listening/transitions.ts` |
| WS-D | Paper / OET@Home / Learning / Diagnostic modes + dashboards UI | agency-implementer (frontend) | `app/listening/**`, `components/domain/listening/*`, `app/expert/listening/**`, `app/admin/listening/**` |
| WS-E | Authoring extras (skill tags, distractor categories, transcript evidence, time-coded transcript) | agency-implementer (backend+frontend) | `ListeningAuthoringService.cs`, `ListeningStructureEditor.tsx` |
| WS-F | 12-stage Course Pathway | agency-implementer (frontend) | `app/listening/pathway/**`, `Services/Listening/ListeningPathwayService.cs` |
| WS-G | Tests + R-code citation checklist | agency-reviewer + agency-debugger | `tests/`, `backend/src/OetLearner.Api.Tests/Listening/*`, `docs/LISTENING-RULEBOOK-CITATIONS.md` |
| WS-H | Docs + runbook | agency-docs | `docs/LISTENING.md` (canonical), `docs/LISTENING-RULEBOOK-CITATIONS.md` |

## 4. R-code → Code mapping (target)

Every R-code from Doc 2 must be cited by at least one test. The citation table lives in `docs/LISTENING-RULEBOOK-CITATIONS.md` (created in WS-H). High-stakes mapping:

| R-code | Enforcement site | Test |
| -------- | ------------------ | ------ |
| R01.7 (audio plays once) | Active player `modePolicy.onePlayOnly` / `canScrub=false`; backend `ListeningSessionService.ResumeAsync` blocks audio restart | Playwright E2E + unit |
| R02.4 (any spelling error = full wrong) | `ListeningGradingService.GradePartA` strict-eq path | xunit |
| R02.7 (no clipping for medicine) | Already in `OetScoring`; re-cite | existing |
| R03.4–R05.10 (timing windows) | `ListeningPolicyDefaults` constants + per-paper overrides | xunit |
| R06.1–R06.7 (one-way section locks) | Active player server-FSM bridge in `app/listening/player/[id]/page.tsx` + server-side `ListeningSessionService.AdvanceSectionAsync` | Playwright + xunit |
| R06.10 (confirm dialog) | Active player next/submit confirmation modal plus V2 confirm-token retry | Vitest |
| R06.11 (unanswered warning) | Active player lock/submit copy includes exact unanswered question numbers before section lock or final submission | Vitest |
| R07.2–R07.3 (paper free-nav + final 2-min all-parts review) | Active player paper-mode policy path renders all section question groups in final review and keeps free-navigation controls available | Vitest |
| R08.1 (no highlight in Part A) | `PartARenderer.tsx` `highlightingDisabled` | Vitest |
| R08.3–R08.5 (yellow stem highlight, right-click strikethrough on options) | `BCQuestionRenderer.tsx` | Vitest |
| R08.7 (in-app +/− zoom; browser/system zoom remains allowed for accessibility) | `<ZoomControls/>` | Vitest |
| R10.1–R10.3 (screen res, scale, wired headset) | `<TechReadinessCheck/>` active strict-start gate + backend `/tech-readiness` snapshot/enforcement in `ListeningSessionService` | xunit + active-route/Vitest API-client; full V2 section routing pending |

(Full table maintained in `docs/LISTENING-RULEBOOK-CITATIONS.md`.)

## 5. Migration Plan — RECONCILED (post Wave 1 review)

**Wave 1 critic + researcher findings**: Most fields proposed in the original PRD §5 already exist in [`ListeningEntities.cs`](backend/src/OetLearner.Api/Domain/ListeningEntities.cs). True delta vs current schema:

### 5.1 KEEP-AS-IS (already shipped, do NOT re-add)

| Originally proposed | Already shipped | Action |
| --- | --- | --- |
| `ListeningQuestion.SkillTag` enum | exists as `string? SkillTag` | Keep as `string?` with **validation set** (`ListeningSkillTags` static): `"purpose"`, `"gist"`, `"detail"`, `"opinion"`, `"warning"`, `"attitude"`, `"note_completion"`, `"other"`. No type change. |
| `ListeningOption.DistractorCategory` (7 values) | exists as enum `ListeningDistractorCategory`; Wave 3a added `OutOfScope` and Wave 5 exposed it in admin authoring | Keep additive enum extension. Drop `correct` from PRD (correct option uses `IsCorrect=true`, not a distractor category). `ReusedKeyword` covers `keyword_match`. |
| Transcript evidence on `ListeningAnswer` | exists on `ListeningQuestion` (`TranscriptEvidenceStartMs/EndMs/Text`) | Keep on `ListeningQuestion` (correct entity — evidence is authored, not learner-submitted). |
| `ListeningExtract.AccentCode/SpeakersJson/TimeCodedTranscriptJson` | all exist (`AccentCode`, `SpeakersJson`, `TranscriptSegmentsJson`) | Reuse existing. |
| `ListeningAttempt.Mode` | exists as `ListeningAttemptMode { Exam, Learning, Drill, MiniTest, ErrorBank, Home, Paper }` | **Add `Diagnostic`**; **deprecate `Drill/MiniTest/ErrorBank`** (mapped to `Learning` for legacy attempts via backfill; remove from new code paths). Map `Exam`→CBT internally. |

### 5.2 TRUE ADDITIONS (additive nullable columns + new tables)

| Field/table | Type | Default | Rationale |
| --- | --- | --- | --- |
| `ListeningQuestion.Version` | `int` | `1` | Version-pin for marking integrity (critic CRITICAL #3) |
| `ListeningOption.Version` | `int` | `1` | Same |
| `ListeningAnswer.QuestionVersionSnapshot` | `int?` | `null` | Freeze on first save; grade against snapshot |
| `ListeningAnswer.OptionVersionSnapshot` | `int?` | `null` | Same |
| `ListeningExtract.TopicCsv` | `string?` (max 256) | `null` | Anti-pattern noted — keep CSV for v1 to avoid join-table blast, lint warns on un-vocab values; revisit when ListeningExtractTopic join table needed |
| `ListeningExtract.DifficultyRating` | `int?` (1–5) | `null` | Admin-rated; lint warns at publish if null |
| `ListeningQuestion.DifficultyLevel` | `int?` (1–5) | `null` | Same scale as extract; either-axis usage allowed |
| `ListeningAttempt.NavigationStateJson` | `string?` (`jsonb` Postgres / `TEXT` SQLite via `HasColumnType` conditional) | `null` | Server-authoritative FSM snapshot. **No LINQ-into** (critic CRITICAL #2) |
| `ListeningAttempt.WindowStartedAt` | `DateTimeOffset?` | `null` | Server clock for current state window |
| `ListeningAttempt.WindowDurationMs` | `int?` | `null` | Pairs with `WindowStartedAt` |
| `ListeningAttempt.AudioCueTimelineJson` | `string?` (jsonb Postgres) | `null` | Emitted on entering `*_audio` state |
| `ListeningAttempt.TechReadinessJson` | `string?` (jsonb Postgres) | `null` | R10 snapshot |
| `ListeningAttempt.AnnotationsJson` | `string?` (jsonb Postgres) | `null` | Persists only when `ListeningPolicy.AnnotationsPersistOnAdvance=true` |
| `ListeningAttempt.HumanScoreOverridesJson` | `string?` (jsonb Postgres) | `null` | Per-question score overrides by assigned expert reviewer; reason required for audit, learner review gets only generic override metadata |
| `ListeningAttempt.LastQuestionVersionMapJson` | `string?` (jsonb Postgres) | `null` | Map `questionId → version` snapshots for grade-time pinning |
| `ListeningPolicy.PreviewWindowMsA1` `..A2` `..C1` `..C2` | `int` | 30000 (A), 90000 (C1), 60000 (C2) | R03–R05 |
| `ListeningPolicy.ReviewWindowMsA1` `..A2` `..C1` `..C2FinalCbt` `..C2FinalPaper` | `int` | 75000/75000/30000/120000/120000 | R03–R07 |
| `ListeningPolicy.BetweenSectionTransitionMs` | `int` | 40000 | R04.8 etc. |
| `ListeningPolicy.PartBQuestionWindowMs` | `int` | 15000 | R04.3 preview |
| `ListeningPolicy.OneWayLocksEnabled` | `bool` | `true` | R06 |
| `ListeningPolicy.ConfirmDialogRequired` | `bool` | `true` | R06.10 |
| `ListeningPolicy.UnansweredWarningRequired` | `bool` | `true` | R06.11 |
| `ListeningPolicy.HighlightingEnabledPartA` | `bool` | `false` | R08.1 |
| `ListeningPolicy.HighlightingEnabledPartBC` | `bool` | `true` | R08.3 |
| `ListeningPolicy.OptionStrikethroughEnabled` | `bool` | `true` | R08.5 |
| `ListeningPolicy.InAppZoomEnabled` | `bool` | `true` | R08.7 |
| `ListeningPolicy.BrowserZoomAllowed` | `bool` | `true` | R08.7 accessibility invariant |
| `ListeningPolicy.TechReadinessRequired` | `bool` | `true` | R10.1–R10.3 |
| `ListeningPolicy.TechReadinessTtlMs` | `int` | 900000 | 15 min (architect §3) |
| `ListeningPolicy.AnnotationsPersistOnAdvance` | `bool` | `false` | Paper mode flips to true via override |
| `ListeningPolicy.FinalReviewAllPartsMsPaper` | `int?` | `120000` | R07.3 |
| `ListeningPolicy.ConfirmTokenTtlMs` | `int` | `30000` | Two-step confirm (architect §1) |
| **New table** `ListeningPathwayProgress` | (Id, UserId, StageCode, Status, StartedAt, CompletedAt, ScaledScore?, AttemptId?, UnlockOverrideBy?) | unique idx `(UserId, StageCode)` | 12-stage tracker |
| **New table** `TeacherClass` (NOT `ListeningTeacherClass` — cross-skill from day one) | (Id, OwnerUserId, Name, CreatedAt) | | Teacher dashboards |
| **New table** `TeacherClassMember` | (Id, TeacherClassId, UserId, AddedAt) | unique idx `(TeacherClassId, UserId)` | |
| **New table** `ListeningAttemptNote` (Learning mode only) | (Id, AttemptId, ExtractId, TranscriptMs?, Text, CreatedAt) | | Architect §3 |
| **New static** `AiFeatureCodes.AdminListeningSkillTag`, `AdminListeningTranscriptSegment` | const string | | Mandate gateway routing (critic HIGH #4) |
| **New file** `rulebooks/listening/<profession>/rulebook.v1.json` | | | Required by AI gateway |
| **New file** `Services/Listening/ListeningPolicyDefaults.cs` | static constants | | Single source of truth for default windows |
| **New file** `Services/Listening/ListeningModePolicy.cs` + 5 impls | `IListeningModePolicy` interface (architect §2) | | |
| **New file** `Services/Listening/ListeningSessionService.cs` | FSM advancement + confirm-token + audio-resume | | Extracted from `ListeningLearnerService.cs` |
| **New file** `Services/Listening/ListeningGradingService.cs` | grading + per-skill breakdown | | Same |

All migrations: additive nullable columns + backfill defaults via background `ListeningV2BackfillService` (idempotent; idempotency key per `(attemptId, version)`). Legacy `Drill/MiniTest/ErrorBank` mode values map to `Learning` on read; new code never writes them.

Wave 8 implementation note: `/v1/listening/v2/me/pathway` recomputes the 12-stage rows before projection, initializes new users with `diagnostic` unlocked, returns only the canonical stage order, and carries qualifying attempt/score metadata for `InProgress` and `Completed` stages. V2 learner grading is user-bound before it triggers pathway recompute.

Wave 10 implementation note: `/v1/listening/v2/teacher/classes/{classId}/analytics` returns owner-scoped class Listening analytics through `TeacherClass`/`TeacherClassMember`. The service looks up classes by `(classId, ownerUserId)` so non-owned class ids follow the not-found path, filters analytics to current roster members, and redacts teacher-facing `CommonMisspellings` so raw Part A wrong-answer strings remain admin-only. Relational Listening V2 `ListeningAttempt.ScaledScore` is the score source of truth when a legacy `Evaluation` row also exists for the same attempt id.

Wave 11 implementation note: `/listening/classes` is the teacher-class analytics frontend workspace. `teacherClassApi.analytics()` maps backend analytics into a teacher-safe DTO: `commonMisspellings` is dropped and distractor `WrongAnswerHistogram` is reduced to `wrongAnswerCount` before page code receives it. The page links from Listening home, supports class creation and adding learners by user id, and displays class KPIs, part accuracy, hardest questions, and aggregate distractor miss counts without rendering raw wrong-answer strings.

Wave 12 implementation note: `/listening/pathway` now renders inside the learner dashboard shell with a hero, accessible progressbar, canonical 12-stage metadata, and action-aware tiles. In-progress and unlocked stages link to the best available Listening route for their stage family; locked and completed stages stay non-actionable. Future backend launch-target fields should replace the coarse frontend route mapping once available.

Wave 13 implementation note: the R06.10 strict-mode confirm-token contract now returns a JSON `AdvanceResultDto` body with HTTP 412 on the first advance request. `listeningV2Api.advance()` explicitly accepts that 412 status through `apiClient.postWithAcceptedStatuses(...)`, preserving normal API error behavior elsewhere while allowing the UI to receive and echo the confirm token on the second request.

Wave 14 implementation note: teacher class analytics now uses a backend teacher-safe DTO. The teacher endpoint sends aggregate `wrongAnswerCount` values and omits admin-only `CommonMisspellings` and raw `WrongAnswerHistogram` data entirely; an HTTP-level privacy regression test asserts the serialized JSON does not contain raw learner-entered wrong answers.

Wave 15 implementation note: teacher class analytics now scopes legacy `Attempt` and normalized `ListeningAttempt` rows through a `TeacherClassMembers` join instead of a materialized roster `Contains` filter. Member counts remain distinct, admin analytics remains unscoped, teacher DTO privacy from Wave 14 is preserved, and SQLite provider runs use a client-side submitted-at cutoff after the server-side class join to avoid nullable `DateTimeOffset` translation failures.

Wave 16 implementation note: the teacher class analytics HTTP route now has endpoint-level non-owner coverage. A request for another teacher's class id returns HTTP 404 and the response body is asserted not to contain the class id or class name, complementing the service-level owner-scope test.

Wave 17 implementation note: `/listening/classes` now includes a privacy-safe CSV export for the selected class window. The export rows are built from explicit teacher-safe fields only, exclude raw wrong-answer histograms/common misspellings, and are disabled unless the current analytics request has succeeded so stale data cannot be exported after a failed refresh.

Wave 18 implementation note: `/v1/admin/listening/attempts/{attemptId}/export` now provides the admin-only full Listening attempt JSON export promised by the retention/export scope. The route is gated by `AdminContentRead`, supports normalized Listening V2 attempts and legacy `Attempt` rows, returns raw answer/state JSON only to authorized admins, returns 404 for missing Listening attempts, and writes a metadata-only `AuditEvent` (`ListeningAttemptExported`) after successful exports without storing raw answer payloads in the audit details.

Wave 19 implementation note: Listening V2 backend hardening now requires `TeachingStaffOnly` for `/v1/listening/v2/teacher/*`, owner-scopes class CRUD/member lookups so non-owned class ids do not reveal existence, validates member ids before roster insert, rejects unknown FSM destination states before free-navigation persistence, repairs malformed `NavigationStateJson` without reopening submitted attempts, and filters admin attempt export evaluations to `SubtestCode == "listening"`.

Wave 20 implementation note: backend/API-client R10 tech-readiness enforcement is now in place. Learners can record readiness through `POST /v1/listening/v2/attempts/{attemptId}/tech-readiness`; strict-mode attempts reject `intro -> a1_preview` with structured 422 `AdvanceResultDto` payloads when readiness is missing or expired; fresh `audioOk=true` readiness preserves the existing confirm-token flow; non-owned learner attempt ids return 404 across state, advance, tech-readiness, and audio-resume. The active routed player and frontend readiness gate still need to be wired before R10 is product-complete.

Wave 21 implementation note: the active learner route now bridges strict starts into the V2 R10 contract. Exam/Home launches and strict mock Listening launches require `TechReadinessCheck`, persist readiness via `listeningV2Api.recordTechReadiness(...)`, replay the `intro -> a1_preview` confirm-token transition through `listeningV2Api.advance(...)`, and keep strict audio unmounted until readiness/start or verified V2 resume. Returned `session.modePolicy` can force strict behavior even when the URL lacks `mode=exam`. This remains a start/readiness bridge only; answer save, submit, and section-by-section navigation are still legacy/local-state pending a later V2 player migration.

Wave 22 implementation note: pathway stage progression is now attempt-scoped instead of mode-only. `ListeningPathwayProgressService` consumes each submitted attempt at most once per recompute, parses relational `ListeningAttempt.ScopeJson` through a shared `ListeningAttemptScope` helper, and requires explicit `pathwayStage`/legacy `stageCode`/`stage` scopes to match the exact stage. `POST /v1/listening-papers/papers/{paperId}/attempts` accepts optional `pathwayStage`, validates it against the canonical 12 stages, stores it in relational `ScopeJson`, and scopes in-progress reuse by the same stage. The active player and typed client forward/preserve `pathwayStage` through scoped starts and resumes.

Wave 23 implementation note: `/v1/listening/v2/me/pathway` now returns nullable backend-authored `actionHref` values for actionable rows. `ListeningPathwayLaunchTargets` maps each canonical pathway stage to the exact internal player route, escapes paper/mode/stage values, and suppresses locked/completed/unanchored rows. The endpoint selects only published objective-ready Listening papers that pass `IContentEntitlementService.AllowAccessAsync(...)`, so denied or structurally incomplete papers do not produce launch links. `/listening/pathway` consumes these backend URLs directly, `PathwayBoard` renders stage-specific accessible action labels, and the active player/session APIs preserve `pathwayStage` through session lookup, start, and URL replacement. The diagnostic pathway stage now maps to relational `ListeningAttemptMode.Diagnostic` instead of being collapsed into broad practice.

Wave 24 implementation note: the active learner player now exposes R08 tools without reintroducing removed per-mode shells. Part A continues through `PartARenderer` with no highlight/strikethrough controls, while Part B/C multiple-choice questions render through `BCQuestionRenderer` with an explicit stem-highlight toggle, context-menu option strikethrough, and keyboard-accessible strike buttons. `ZoomControls` provides bounded in-app question zoom for the active question surface while preserving browser/system zoom for accessibility. The route-level player test and focused component tests pin the R08 behavior.

Wave 25 implementation note: the active learner player now hydrates strict Exam/OET-Home resumes from the V2 FSM state and uses the existing confirm-token `/v1/listening/v2/attempts/{attemptId}/advance` protocol for strict preview -> audio, audio -> review, and review -> next-section transitions. Shared TS helpers in `lib/listening/transitions.ts` map backend FSM states to the active player's section/phase model, and route-level tests prove strict resume into later sections plus preview-to-audio server advance. This is still not the full player migration: answer save, submit/review DTO handoff, paper free-navigation, and all-parts final-review parity remain later work.

Wave 26 implementation note: Phase 9's learner/admin surface is now implemented on the active route. The live player still uses `app/listening/player/[id]/page.tsx` as the orchestration surface, but the visible player chrome is extracted into `ListeningIntroCard`, `ListeningAudioTransport`, `ListeningPhaseBanner`, and `ListeningSectionStepper`, with focused component tests and opt-in Storybook coverage. Admin hardest-question rows now link to `/admin/analytics/listening/question/{paperId}/{number}` for a filtered per-question deep dive. The strict exam and paper-mode Listening Playwright specs are live learner-page contract tests rather than `test.fixme` placeholders. Review hardening closed two strict-mode gaps: audio-resume now pauses while validation is pending and remains paused with a warning on validation failure, and Part B review gating uses the full multi-extract cue window. Wave 27 closed the local V2 save/submit and paper final-review deferrals; seeded multi-part free-navigation E2E coverage still needs a deterministic complete-paper fixture and running stack.

Wave 27 implementation note: the active learner player now persists answers through `PUT /v1/listening/v2/attempts/{attemptId}/answers/{questionId}` and submits through `POST /v1/listening/v2/attempts/{attemptId}/submit`. These endpoints are facade routes over `ListeningLearnerService.SaveAnswerAsync()` / `SubmitAsync()` so ownership, deterministic grading, full review DTO shape, and relational Listening behavior remain canonical. Paper-mode sessions project `freeNavigation`, `unansweredWarningRequired`, and `finalReviewAllPartsSeconds`; the route renders all section question groups during paper final review and keeps stepper/jump-list navigation available without seeking to section cue points. R06.11 warnings now list exact unanswered question numbers for section locks and final submit.

### 5.3 DEFERRED to v2.1 (do not implement now)

- True CAT (item response theory) — Diagnostic is **fixed-form placement** only (critic HIGH #5)
- Mobile/Capacitor parity for R08.7 in-app zoom — web-only this pass (critic 2.3)
- AI auto-tagging of skill tags / distractor categories — manual tagging this pass
- AI auto-segmentation of transcripts — manual evidence timestamps this pass
- Real proctoring (camera/screen-share) for OET@Home — UI shell only, document as known limitation
- `ListeningExtractTopic` join table (replace `TopicCsv`)
- Paper-mode physical answer-sheet OMR — on-screen-only + browser print CSS only

## 5.4 Pre-flight: WS-E0 reconciliation step

Before any WS-A migration is written, an implementer runs WS-E0:

1. Diff `PRD-LISTENING-V2.md §5.2` against `ListeningEntities.cs` line-by-line.
2. Confirm `LearnerQuestionDto` projection at `ListeningLearnerService.cs:1862` does NOT leak `IsCorrect` / `WhyWrongMarkdown` via `q.Options` (researcher §C flag). Write a regression test.
3. Confirm `OetScoring.OetRawToScaled` is the only raw→scaled path; grep for `* 350` / `/ 42` / `* 500` in `Services/Listening/*` and fail CI if found outside `OetScoring`.
4. Pre-create `AiFeatureCodes.AdminListeningSkillTag` + `AdminListeningTranscriptSegment` constants and add to `AiCredentialResolver.PlatformOnlyFeatures` (critic HIGH #4).

## 6. Risks & Mitigations — updated post Wave 1

| Risk | Mitigation |
| ------ | ------------ |
| Existing in-flight attempts break on schema bump | Additive nullable only; backfill worker; legacy attempts default `Mode=Exam→cbt-mapped`, default policy windows |
| Single 33–53 MB MP3 + cue-point seek + hard-block pause race conditions | Server emits authoritative `audio_timeline[]` of cue events; client renders but cannot override; idempotent `/advance` with client nonce |
| Confirm-dialog R06.10 double-tap race (critic 1.3) | Two-step confirm-token protocol: first `/advance` returns 412 with signed `confirmToken` (TTL `ConfirmTokenTtlMs`); retry must echo token (architect §1, §8 R3) |
| jsonb columns on SQLite test DB (critic CRITICAL #2) | `HasColumnType("jsonb")` only on Npgsql; SQLite stores as `TEXT`; never LINQ-into them — pull row + parse C# |
| Question/option edits silently invalidate in-flight attempts (critic CRITICAL #3) | `Version` int on Question/Option; freeze `QuestionVersionSnapshot`/`OptionVersionSnapshot` on first answer save; grade against snapshot |
| Paywall mid-attempt → opaque error (critic HIGH #7) | New `paywalled` FSM state; autosave on existing in-progress attempt always allowed; only new-attempt POST returns 402 |
| Teacher dashboard OWASP A01 scope leak (critic HIGH #6) | Wave 10 class analytics looks up by `(classId, ownerUserId)`, filters by class membership, returns not-found for non-owned classes, and redacts teacher-facing raw misspelling strings; authz Playwright/HTTP-level tests still required |
| Subagent edits collide on `ListeningEntities.cs` | Serialize: WS-E0 reconcile → WS-A migration → (WS-B ∥ WS-C-skeleton) → (WS-D ∥ WS-F) → WS-G → WS-H |
| R08.1 (no highlight Part A) vs WCAG / screen-readers (critic 2.4) | `ListeningUserPolicyOverride.AccessibilityModeEnabled` opt-out flag; documented carve-out; a11y test |
| Audio replay on refresh / network drop / mobile WebView range-requests (critic 2.1) | `ListeningSessionService.ResumeAsync` contract: server stores `audioPlayheadMs` per autosave tick; on resume, jump to last-seen + grace; if grace exceeded, force-advance to `*_review` with audit |
| AI gateway bypass on skill-tag / transcript work (critic HIGH #4) | New `AiFeatureCodes.AdminListeningSkillTag`, `AdminListeningTranscriptSegment` constants + `AiCredentialResolver.PlatformOnlyFeatures` registration; `RuleKind.Listening` + new task modes; rulebook file present before any AI call |
| EF SQLite translation of nullable DateTimeOffset / nullable int comparisons (user memory) | All such predicates filtered client-side after server-translatable narrow; pattern follows `ai-gateway-byok-quota.md` memory |
| OET@Home mode proliferation with no real UX delta (critic 3.2) | Implement as CBT + `OetHomeFullscreenMode=true` flag + tab-focus-loss telemetry only this pass; no separate `PaperModePolicy.fullscreen` class |

## 7. Definition of Done

- `npx tsc --noEmit` clean
- `npm run lint` 0 errors/warnings
- `npm test` all green (existing 675 + new)
- `npm run build` compiles all routes
- `dotnet test backend/OetLearner.sln` all green (existing 601+ + new)
- New Playwright E2E specs cover: R06 CBT lock sequence, R07 paper free-nav, R08 highlight/strikethrough/zoom, R10 readiness gate
- Storybook entries for active-route player chrome (`ListeningIntroCard`, `ListeningAudioTransport`, `ListeningPhaseBanner`, `ListeningSectionStepper`) plus dashboard/pathway/admin surfaces as they become reusable components; removed per-mode shells such as `CbtPlayer`, `PaperModePlayer`, and `LearningModePlayer` are no longer required artifacts.
- `docs/LISTENING-RULEBOOK-CITATIONS.md` lists every R01.x–R10.x with at least one source-of-truth file + test
- `docs/LISTENING.md` updated as canonical spec (supersedes V1)
- All migrations applied cleanly on a fresh DB AND on existing dev seed without data loss

## 8. Out of scope / follow-ups

- Real proctoring integration (camera, screen-share) — UI shell only this pass
- Mobile native player parity (Capacitor) — web-only this pass; Capacitor wrapper follows
- AI auto-tagging of skill tags / distractor categories on existing seeded data — manual admin tagging this pass
