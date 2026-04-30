# Speaking Module — Gap Analysis and Implementation Plan

> **Implementation status.** ✅ All seven waves shipped on
> `codex/mission-critical-a-z-cleanup` lineage. Final validation gate
> (2026-04-30): `npx tsc --noEmit` 0 errors, `npm run lint` 0 warnings,
> `dotnet test` 813/813 passed (TRX `<Counters total="813" passed="813"
> failed="0" error="0" />`). See `mission-critical-execution-ledger.md`
> and repo memory `/memories/repo/speaking-module-plan-complete.md` for
> the file-level implementation map.
>
> **Scope.** This document maps the OET Speaking specification (the "Speaking
> markdown" supplied by the project owner, sections 1–23) to what already
> exists in this repository and lays out a wave-based plan to close the gaps
> without breaking the mission-critical contracts in `AGENTS.md`
> (`OetScoring`, `IAiGatewayService`, `Services/Rulebook`, `IFileStorage`,
> `ContentPaper` publish gate, `Services/Conversation`, free-tier caps,
> notification fan-out, `IAiUsageRecorder`).
>
> **Status legend.**
>
> - ✅ Implemented — already present and on the canonical contract.
> - 🟡 Partial — present but does not yet match the spec one-for-one.
> - ⚠️ Missing — needs to be built.
>
> **Date.** 2026-04-30. Reviewed against current `main`.

---

## 0. Mission-critical invariants (must not be violated)

These come from `AGENTS.md` and the existing speaking/conversation specs. Any
wave below must keep them green:

| Invariant | Source |
| --- | --- |
| Speaking score anchor 350/500; never compare `score >= 350` inline | `lib/scoring.ts`, `OetLearner.Api.Services.OetScoring`, `docs/SCORING.md` |
| All AI calls go through `IAiGatewayService.BuildGroundedPrompt` + `CompleteAsync`; ungrounded prompts throw `PromptNotGroundedException` | `docs/AI-USAGE-POLICY.md` |
| Every AI call writes one `AiUsageRecord` via `IAiUsageRecorder` | same |
| Speaking rule enforcement goes through `Services/Rulebook/SpeakingRuleEngine` only | `docs/RULEBOOKS.md` |
| Role-play cards live as `ContentPaper` (`SubtestCode = "speaking"`, `CardType` set) → `ContentPaperAsset` → `MediaAsset`, with publish gate enforcing required roles + `SourceProvenance` | `docs/CONTENT-UPLOAD-PLAN.md` |
| Audio/video I/O always via `IFileStorage` (SHA-256 content-addressed); retention swept by a worker | `docs/CONVERSATION.md`, `docs/PRONUNCIATION.md` |
| Conversation module already owns `oet-roleplay` AI patient simulation; new work extends it, never duplicates it | `backend/src/OetLearner.Api/Domain/ConversationEntities.cs`, `docs/CONVERSATION.md` |
| `ApiClient`/`lib/api.ts` is the only HTTP path from app code; raw `fetch` only in documented exceptions | `AGENTS.md` |
| Free-tier caps + notification fan-out are part of the user contract | `Domain/FreeTierEntities.cs`, `Domain/NotificationEntities.cs` |

---

## 1. What already exists in the repo

This is not a greenfield build. Substantial Speaking infrastructure is
already in place. The plan therefore extends, never duplicates.

### Backend (`backend/src/OetLearner.Api/`)

- **Rulebooks** — `rulebooks/speaking/{nursing,medicine}/rulebook.v1.json`
  with the Grade B / 350 anchor, profession-specific rules, plain-English
  jargon detector, empathy-before-instruction rule, check-understanding rule,
  summarise-key-plan rule. Loaded by `Services/Rulebook/SpeakingRuleEngine`.
- **Evaluation pipeline** — `Services/SpeakingEvaluationPipeline.cs` runs
  ASR/transcript ingestion, calls `SpeakingRuleEngine.Audit`, and routes
  scoring through `IAiGatewayService.BuildGroundedPrompt(Kind=Speaking,
  Task=Score, CardType=…)`. Stores transcript JSON on the attempt.
- **Speaking endpoints** — `/v1/speaking/home`, `/v1/speaking/tasks`,
  `/v1/speaking/tasks/{id}`, `/v1/speaking/device-checks`,
  `/v1/speaking/audit`, `/v1/speaking/attempts/{id}/audio/upload-session`,
  `/v1/speaking/attempts/{id}/audio/complete`, `/v1/speaking/attempts/{id}/submit`,
  `/v1/speaking/evaluations/{id}/summary`, `/v1/speaking/evaluations/{id}/review`.
- **Private (1:1) speaking sessions** — `Endpoints/PrivateSpeakingEndpoints.cs`
  + admin/expert routes (`/v1/private-speaking/*`,
  `/v1/expert/private-speaking/*`, `/v1/admin/private-speaking/*`) covering
  config, tutor profiles, availability, slots, bookings, Zoom integration,
  cancellation, rating, stats. Notification flows for booked / 24h / 30m /
  tutor joined / no-show / recording ready / feedback ready already exist
  (`Domain/NotificationEntities.cs`).
- **Conversation module** — `Domain/ConversationEntities.cs` with
  `TaskTypeCode = "oet-roleplay"`, ASR/TTS provider selectors, audio retention
  worker, gateway-grounded opening / reply / evaluation; this is what powers
  the AI patient simulator.
- **Content papers** — `Domain/ContentPaperEntities.cs` already supports
  `PaperType.RolePlay`, `WarmUpQuestions = 8`, `CardType` field on
  `ContentPaper` matching `AiGroundingContext.CardType`. The publish gate
  (`ContentPaperService.RequiredRolesFor`) and `SourceProvenance` already
  enforce content integrity.
- **Free-tier cap** — `MaxSpeakingAttempts = 3` on `FreeTierEntities`.
- **AI feature codes** — `AiFeatureCodes.SpeakingGrade`, plus
  `BackgroundJobType.SpeakingTranscription` and `SpeakingEvaluation`.

### Frontend (`app/speaking/`)

Routes already shipped:

- `app/speaking/page.tsx` — speaking home (resume attempt, recommended role-play, past attempts).
- `app/speaking/selection/page.tsx` — task list.
- `app/speaking/check/page.tsx` — device check.
- `app/speaking/task/[id]/page.tsx` — pre-roleplay landing.
- `app/speaking/roleplay/[id]/page.tsx` — recording / role-play screen.
- `app/speaking/transcript/[id]/page.tsx` — timestamped transcript viewer.
- `app/speaking/fluency-timeline/[id]/page.tsx` — fluency visualisation.
- `app/speaking/results/[id]/page.tsx` — scored results.
- `app/speaking/expert-review/[id]/page.tsx` — expert review viewer.
- `app/speaking/phrasing/[id]/page.tsx` — phrasing micro-feedback.
- `app/speaking/rulebook/page.tsx` — rulebook viewer.

Admin already has `app/admin/private-speaking/` for the live tutor business.

### What is unambiguously good and should not be touched

- The dual-criteria scoring shape (linguistic 4 + clinical communication 5).
  The pipeline already feeds `SpeakingRuleEngine` findings + grounded AI
  output into one `AttemptEvaluation` with criterion fields.
- The `oet-roleplay` AI patient simulator (Conversation module). The spec's
  "Self-practice speaking room" maps directly onto this; do not build a
  parallel system.
- `PrivateSpeakingEndpoints.cs` for the human-tutor business flow.

---

## 2. Section-by-section gap audit

Below, every numbered section of the spec is mapped to its repo state.

### §1–4 Format / interlocutor / warm-up

| Spec item | State | Notes |
| --- | --- | --- |
| Profession-specific role-play, candidate plays own profession | ✅ | `ContentItem.ProfessionId`, profession selector exists. |
| Two role-plays per full mock | 🟡 | Single-task flow is solid. No `SpeakingMockSet` entity that pairs two tasks + a single readiness band yet. |
| 3-min prep before each role-play | ⚠️ | `app/speaking/task/[id]/page.tsx` is a static landing, no enforced 3-min countdown with notes box / emotion / purpose / lay-language helpers. |
| ~5-min role-play timer | 🟡 | `roleplay/[id]/page.tsx` records but does not enforce a hard 5-min cap with auto-stop and auto-submit. |
| Warm-up simulator (not assessed) | ⚠️ | `WarmUpQuestions` content type exists in the schema but no learner UI surface. |

### §5–6 Role-play card + 3-min prep

| Spec item | State | Notes |
| --- | --- | --- |
| Candidate card: setting, role, patient role, background, tasks, emotional cue, clinical content | 🟡 | `ContentItem.DetailJson` already carries this; field shape isn't standardised across the bank. |
| **Interlocutor card** (hidden from learner): patient profile, prompts, hidden info, resistance level, closing cue | ⚠️ | No first-class entity. AI roleplay uses `ConversationTemplate.PatientContext` but it isn't exposed for human-tutor sessions. |
| 3-min prep screen with countdown + highlighting + notes + emotion + purpose + lay-language simplifier + Start button | ⚠️ | None of these UI affordances exist on `task/[id]`. |

### §7–8 The two role-plays / candidate behaviours

✅ Profession tagging, setting, communication goal, patient emotion,
difficulty, criteria focus, time target — all expressible via `ContentItem`
fields and `CardType` enum. The 9-stage candidate behaviour map (opening →
purpose → relationship → information gathering → patient perspective →
explanation → checking → shared plan → closing) is partly enforced by
`SpeakingRuleEngine`'s `turnStage` rules (greeting / empathy / feedback /
recap) but stage **coverage** is not surfaced as a visible criterion-by-stage
matrix on the results screen. 🟡

### §9 Assessment criteria (4 linguistic + 5 clinical)

🟡 The pipeline does compute per-criterion scores (rulebook + grounded AI
output). What is missing is the **public, criterion-keyed feedback**
contract on `/v1/speaking/evaluations/{id}/summary` — today criteria are
embedded inside an opaque blob. Need stable JSON keys: `intelligibility`,
`fluency`, `appropriateness`, `grammarExpression`, `relationshipBuilding`,
`patientPerspective`, `structure`, `informationGathering`,
`informationGiving`, each `{ score, max, descriptor, exemplarPhrases[] }`,
plus a top-level `estimatedScaledScore` projected via a single new
`OetScoring.SpeakingProjectedScaled()` helper that anchors the 4×6 + 5×3
rubric to 350.

### §10 Scoring + readiness bands

🟡 Scaled score is reported. The 5-band readiness ladder
(`not_ready | developing | borderline | exam_ready | strong`) is not
formally typed; it lives as free text. Add a `SpeakingReadinessBand` enum
and project it server-side, mirroring how Pronunciation does this.

### §11 Rules learners must know

✅ Rulebook covers integrity, regulations, "estimated score, not official".
Missing: a single learner-facing disclaimer banner component on every
results page. ⚠️ small UI fix.

### §12 Platform modules

| Module | State |
| --- | --- |
| A. Full Speaking mock simulator (two role-plays, 3+5 timers, recording, criterion feedback) | 🟡 Need `SpeakingMockSet` + simulator orchestrator route. |
| B. Live role-play classroom (video room, tutor dashboard, hidden interlocutor card, cue buttons, private notes) | 🟡 Private speaking exists; tutor dashboard does not yet show interlocutor card or cue buttons. |
| C. Self-practice speaking room (AI patient, voice recording, transcript, fluency / jargon / empathy detection, replay, retake) | 🟡 Conversation module covers AI patient + transcript. Empathy detection is rule-driven. Jargon detector exists as a rule check. UI affordances for retake / model comparison are missing. |
| D. Speaking feedback dashboard | 🟡 Exists but criterion-keyed shape needs to be made stable (see §9). |
| E. Role-play card builder (admin) | ⚠️ No dedicated admin builder UI for two-card role-play (candidate + interlocutor) on top of `ContentPaper`. |
| F. Interlocutor training module | ⚠️ Not present. New (small) module under `app/admin/private-speaking/training/` and an `InterlocutorCalibrationSample` entity. |

### §13 Data model

⚠️ Spec lists 11 tables. Current model can express the same data with **far
fewer new tables** by reusing `ContentPaper` / `Attempt` / `AttemptEvaluation` /
`ConversationSession`. Net new tables required:

1. `SpeakingMockSet` — pairs two `ContentItem` IDs as one mock; tracks the
   learner's combined readiness band.
2. `InterlocutorCard` — 1:1 with a speaking `ContentItem`, hidden from the
   learner projection layer.
3. `TutorCalibrationScore` — tutor's score for a sample recording, used to
   compute drift vs. the gold sample.
4. `SpeakingFeedbackComment` — tutor's timestamped inline comment on a
   transcript line (uses existing `Attempt`/`Transcript`).

Everything else maps onto existing tables.

### §14–15 Student / teacher analytics

🟡 Backend can already compute trends from `AttemptEvaluation` rows. The
`/v1/speaking/home` endpoint and learner dashboard are partial. Teacher
dashboards exist for private speaking but do not yet aggregate criterion
trends across the class.

### §16 Course pathway (16 stages)

⚠️ No formal `learning_paths` entry tying these 16 stages to assets. The
`learning-paths` route exists at the top level — needs a curated speaking
pathway seeded.

### §17 Skill drills

🟡 Drill content can be carried by existing `ContentItem` rows tagged with
`CardType` extensions / `TagsCsv` (e.g. `drill:empathy`, `drill:ice`,
`drill:lay-language`). No drill bank seeded yet.

### §18–19 Content + modes

⚠️ Volume targets (50–100 mock sets, 300–600 cards, 300+ drills) are content
work, not engineering. The engineering deliverable is just the admin
ergonomics for bulk authoring (already covered by §12-E).

### §20 Compliance

🟡 Consent gating, RBAC on recordings, retention sweep, "estimated score"
disclaimer, and academic-honesty banner exist piecewise (Conversation
retention worker, RBAC middleware) but need a single
`SpeakingComplianceOptions` config and a dedicated audit event class
(`AuditEventType.SpeakingRecordingAccessed`).

### §21 Technical design

See §13. Plan reuses existing tables — no `recordings` / `transcripts` /
`assessments` duplicates.

### §22–23 Excellence / business

These are validation criteria for the plan; not separate work items.

---

## 3. Wave-based implementation plan

Each wave is independently deployable, ships behind a feature flag, and
must leave `npx tsc --noEmit`, `npm run lint`, `npm test`, and
`npm run backend:test` green. No wave is allowed to break any invariant
in §0.

### Wave 1 — Stable criterion-keyed feedback contract (foundation) ✅ DONE

**Goal.** Make the scoring contract machine-readable before any new UI is
built on top of it.

**Backend.**

- Add `OetScoring.SpeakingProjectedScaled(SpeakingCriterionScores scores)`
  anchored at "B grade ≡ 350". Tests for boundary conditions.
- Promote `SpeakingReadinessBand` to a real enum
  (`NotReady | Developing | Borderline | ExamReady | Strong`) and project
  it server-side.
- Stabilise the JSON shape returned by
  `/v1/speaking/evaluations/{id}/summary` to include all 9 criteria with
  `{ score, max, descriptor, exemplarPhrases[] }` and the band.
- Update `SpeakingEvaluationPipeline` to write the structured criteria to
  `AttemptEvaluation` instead of opaque JSON.

**Frontend.**

- Update `lib/api.ts` `SpeakingEvaluationSummary` type to the new contract.
- Update `app/speaking/results/[id]/page.tsx` to render the criterion-keyed
  card (read-only — no UI behaviour change beyond labels).

**Acceptance.** Existing E2E speaking smoke tests still pass; new unit
tests cover both `OetScoring.SpeakingProjectedScaled` and the projection
layer.

**Rollback.** Feature flag `SpeakingScoringV2`; old shape kept on the
endpoint behind an `Accept-Version` header for one release.

### Wave 2 — Interlocutor card + 3-minute prep + 5-minute timer ✅ DONE

**Goal.** Make role-play exam-realistic.

**Backend.**

- New entity `InterlocutorCard` with FK to `ContentItem`. Fields per spec
  §13. Migration on PostgreSQL + SQLite.
- Projection layer: learner endpoints (`/v1/speaking/tasks/{id}` etc.)
  **never** include `InterlocutorCard` (mirroring how reading hides
  `CorrectAnswerJson`). Tutor and admin endpoints expose it.
- Admin endpoint to CRUD `InterlocutorCard` under
  `/v1/admin/papers/{paperId}/interlocutor-card`.

**Frontend.**

- Replace `app/speaking/task/[id]/page.tsx` static landing with a 3-min prep
  screen: countdown, candidate-card highlighting, notes box, emotion
  selector, purpose selector, lay-language simplifier, Start button.
- `app/speaking/roleplay/[id]/page.tsx`: enforce 5-min cap with audible
  warning at 4:30 and auto-stop + auto-submit at 5:00.
- New tutor view `app/expert/private-speaking/session/[id]/interlocutor-card.tsx`
  shows the hidden card with cue buttons.

**Acceptance.** Playwright E2E "speaking-prep-timer" runs the 3-min prep
flow under a faked clock. Backend test asserts learner endpoints never
serialise `InterlocutorCard`.

**Rollback.** Flag `SpeakingExamRealism`; existing legacy `task` page kept
behind the flag.

### Wave 3 — Speaking mock set (two role-plays as one mock) ✅ DONE

**Goal.** Match the §1 "two role-plays" requirement end-to-end.

**Backend.**

- New entity `SpeakingMockSet { Id, ProfessionId, RolePlay1ContentId,
  RolePlay2ContentId, Status, Difficulty, CriteriaFocus, Tags }`.
- Endpoint `/v1/speaking/mock-sets/{id}/start` creates two paired
  `Attempt`s and returns a single `mockSessionId`.
- Endpoint `/v1/speaking/mock-sessions/{id}` returns combined criterion
  averages + a single readiness band.
- Admin CRUD under `/v1/admin/speaking/mock-sets`.

**Frontend.**

- `app/speaking/mocks/[id]/page.tsx` orchestrates: prep1 → roleplay1 → prep2
  → roleplay2 → combined results.
- `app/admin/content/speaking/mock-sets/page.tsx` admin list + builder.

**Acceptance.** New unit tests for the combined-band projection. Smoke
test that the orchestrator advances stages and submits both attempts.

**Rollback.** Flag `SpeakingMockSets`. Existing single-task flow untouched.

### Wave 4 — Tutor calibration + inline transcript comments ✅ DONE

**Goal.** Quality control across human tutors.

**Backend.**

- `TutorCalibrationSample` (gold-marked recording reference) and
  `TutorCalibrationScore` (one row per tutor per sample).
- `SpeakingFeedbackComment` keyed by `(attemptId, transcriptLineIndex,
  criterion)`.
- Endpoints: tutor submits calibration scores, admin sees drift report,
  expert posts inline comments on a learner's attempt.

**Frontend.**

- `app/expert/calibration/speaking/page.tsx` — tutor calibration mode.
- `app/admin/private-speaking/calibration/page.tsx` — drift dashboard.
- `app/speaking/transcript/[id]/page.tsx` — render inline comments under
  the line they target.

**Acceptance.** Backend unit tests for drift maths. Permission tests:
calibration is gated by an existing admin permission
(`ManageExperts` / `ReviewMocks`).

**Rollback.** Flag `SpeakingTutorCalibration`.

### Wave 5 — Self-practice (AI patient) deep-link from Speaking module ✅ DONE

**Goal.** Reuse the Conversation module's AI patient instead of building a
parallel system.

**Backend.**

- Add a deep-link endpoint
  `POST /v1/speaking/tasks/{id}/self-practice` that creates a
  `ConversationSession` seeded with the candidate card's scenario + role
  via `IConversationService`. Subject to free-tier caps already on
  Conversation.

**Frontend.**

- Button on `app/speaking/task/[id]` and `app/speaking/results/[id]`:
  "Practise this scenario with the AI patient" → routes to
  `/conversation/session/[sessionId]`.

**Acceptance.** No new AI provider. The deep-link prompt is built only via
`IAiGatewayService.BuildGroundedPrompt(Kind=Conversation,
Task=GenerateConversationOpening)` — same path as the Conversation module.
A test asserts no direct provider call is added.

**Rollback.** Flag `SpeakingSelfPractice`.

### Wave 6 — Drills bank + course pathway seed ✅ DONE

**Goal.** Surface the §17 micro-drills and §16 16-stage pathway.

**Backend.**

- Drill content is just `ContentItem` rows with
  `Tags = "drill:<kind>"` (no schema change).
- Seed a `LearningPath` entry "Speaking Foundations" with 16 stages mapped
  to existing learner pages.

**Frontend.**

- `app/speaking/drills/page.tsx` — drill catalogue filterable by drill
  kind, profession, criterion focus.
- Wire the speaking pathway entry into `app/learning-paths/page.tsx`.

**Acceptance.** Lint + tsc clean; smoke test asserts the pathway shows the
correct first stage for a new learner.

**Rollback.** Flag `SpeakingDrills`.

### Wave 7 — Compliance polish ✅ DONE

**Goal.** Close the §20 audit/consent items.

- New `SpeakingComplianceOptions` (retention days, consent text, score
  disclaimer copy).
- Audit event `AuditEventType.SpeakingRecordingAccessed` written every
  time a non-owner downloads or streams a recording.
- Standard "Estimated score, not official OET" banner component
  (`components/domain/SpeakingScoreDisclaimer.tsx`) used on all results
  pages.
- Retention worker reuses the Conversation pattern.

**Acceptance.** Backend test: any non-owner GET on a recording URL emits
exactly one audit row. UI snapshot test: every speaking results page
renders the disclaimer.

**Rollback.** Compliance work is additive — no flag needed; can be reverted
file-by-file.

---

## 4. Out-of-scope (explicitly)

- Changes to the OET scoring anchor itself.
- Replacing the AI patient with a new provider.
- Authoring the 50–100 mock sets / 300–600 cards / 300+ drills (this is
  content work, scheduled separately by the academy team).
- Mobile-only deltas (Capacitor offline mode) — already covered by the
  capacitor mobile plan.

---

## 5. Verification matrix

For every wave:

- [ ] `npx tsc --noEmit` — 0 errors.
- [ ] `npm run lint` — 0 errors / 0 warnings.
- [ ] `npm test` — all unit tests pass.
- [ ] `npm run backend:test` — all .NET tests pass.
- [ ] `npm run build` — production build compiles.
- [ ] Targeted Playwright smoke test for the wave passes locally.
- [ ] No new direct `fetch()` outside `lib/api.ts` exceptions.
- [ ] No new AI provider call outside `IAiGatewayService`.
- [ ] No new `File.*` / `Path.*` outside `IFileStorage`.
- [ ] `OetScoring` / `lib/scoring.ts` is the only place numeric
      raw↔scaled conversion happens.

---

## 6. Decisions (locked 2026-04-30)

1. **Free-tier cap for mock sets.** Separate cap
   `MaxSpeakingMockSets = 1` per rolling 7 days, in addition to the
   existing `MaxSpeakingAttempts = 3`. Implemented in Wave 3 alongside
   `SpeakingMockSet`.
2. **Tutor calibration drift policy.** Tiered soft-block. Per criterion
   on a 100-point projection: ≤ 20 green; 20 < drift ≤ 40 yellow banner
   urging re-calibration; > 40 red — blocks **assignment of new**
   private-speaking bookings only (in-flight bookings remain markable).
   Admin can override, recorded as `AuditEventType.TutorCalibrationOverride`.
   Implemented in Wave 4.
3. **AI scoring failure fallback.** Fail loudly. On
   `PromptNotGroundedException` or any provider error during speaking
   evaluation: set `AttemptEvaluation.Status = Failed`, surface a "could
   not grade — please retry" message, do **not** consume the free-tier
   counter, and write `AiUsageRecord` with `Outcome = ProviderError`
   and `RefundedQuota = true`. Same contract as Pronunciation.
   Implemented in Wave 1 (since Wave 1 owns the evaluation contract).
