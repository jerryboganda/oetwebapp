# Listening Module — Canonical Spec (V2)

> **Status:** authoritative. Supersedes any earlier draft notes about
> Listening V2. Cross-cutting invariants (scoring, content model, AI
> gateway, file storage) are documented in `AGENTS.md` and **must be
> respected** by every Listening change.

This document is the single source of truth for the Listening module.
For the granular rulebook → file:line → test mapping see
[`docs/LISTENING-RULEBOOK-CITATIONS.md`](LISTENING-RULEBOOK-CITATIONS.md).

---

## 1. Mission-critical invariants

1. **Raw → scaled is OetScoring only.** All raw → scaled conversions for
   Listening MUST route through `OetLearner.Api.Services.OetScoring.OetRawToScaled`
   (TS mirror: `lib/scoring.ts`). Anchor: **30/42 ≡ 350/500**. Inline
   math like `* 350` / `/ 42` / `* 8.33` is **forbidden** anywhere in
   `backend/src/OetLearner.Api/Services/Listening/**` and is enforced by
   the source-scanning audit test
   [`ListeningScoringPathAuditTest`](../backend/tests/OetLearner.Api.Tests/Listening/ListeningScoringPathAuditTest.cs).
2. **Server-authoritative FSM.** Canonical section navigation is held in
  `ListeningAttempt.NavigationStateJson`. The client carries a mirror in
  [`lib/listening/transitions.ts`](../lib/listening/transitions.ts) that is
  parity-tested against the C# table
  [`ListeningFsmTransitions.cs`](../backend/src/OetLearner.Api/Services/Listening/ListeningFsmTransitions.cs)
  by [`tests/unit/listening/transitions.parity.test.ts`](../tests/unit/listening/transitions.parity.test.ts).
  The active player now uses this V2 FSM for strict start, strict resume
  hydration, strict preview/audio/review forward advances, and audio-resume
  enforcement. Answer autosave, submit/review DTO handoff, and paper-mode
  free navigation still run through the legacy active-player runtime until
  their explicit migration waves land.
3. **Two-step confirm-token (R06.10).** Strict modes (CBT, OET-Home)
   require a second POST to `/v1/listening/v2/attempts/{id}/advance`
   echoing the HMAC token returned by the first call. TTL is 30 s by
   default. Implementation:
   [`ListeningConfirmTokenService.cs`](../backend/src/OetLearner.Api/Services/Listening/ListeningConfirmTokenService.cs).
4. **Version-pinned grading.** Every attempt snapshots
   `LastQuestionVersionMapJson` at first navigation. Grading reads
   that snapshot so admin question edits during in-flight attempts
   never silently invalidate the candidate's answer.
   Implementation:
   [`ListeningGradingService.cs`](../backend/src/OetLearner.Api/Services/Listening/ListeningGradingService.cs).
5. **No answer-key leak in pre-submit DTOs.** Every learner-facing
   Listening DTO is source-scanned by
   [`ListeningLearnerLeakAuditTest`](../backend/tests/OetLearner.Api.Tests/Listening/ListeningLearnerLeakAuditTest.cs)
   for forbidden fields (`IsCorrect`, `CorrectAnswer*`, `AcceptedSynonyms*`,
   `Explanation*`, `WhyWrong*`, `TranscriptEvidence*`,
   `DistractorCategory`).
6. **OWASP A01 on teacher classes.** Every read/write path in
   [`TeacherClassService.cs`](../backend/src/OetLearner.Api/Services/Listening/TeacherClassService.cs)
   filters by `OwnerUserId == currentUserId`. Pinned by
   [`TeacherClassServiceTests`](../backend/tests/OetLearner.Api.Tests/Listening/TeacherClassServiceTests.cs).

---

## 2. Five attempt modes (R03)

|Mode|Free nav|One-way locks|Replay|Timer|Confirm-token|
|---|---|---|---|---|---|
|**Exam (CBT)**|❌|✅|❌|✅|✅|
|**OET-Home**|❌|✅|❌|✅|✅|
|**Paper**|✅|❌|❌|✅|❌|
|**Learning**|✅|❌|✅|❌|❌|
|**Diagnostic**|✅|❌|❌|❌|❌|

All five modes are orchestrated today by the active learner route
`app/listening/player/[id]/page.tsx`, which branches on the attempt's
`mode` value returned by the backend while delegating reusable player
chrome to `components/domain/listening/player/*`. Per-mode policy is
resolved server-side by
[`ListeningModePolicyResolver`](../backend/src/OetLearner.Api/Services/Listening/ListeningModePolicy.cs)
which maps every value of `ListeningAttemptMode` (Exam, OetHome, Paper,
Learning, Diagnostic, Drill, MiniTest, ErrorBank — the last three all
fall through to LearningModePolicy per planner Wave 2 §1f).

---

## 3. FSM transitions (R04)

Forward path (CBT / OET-Home strict order):

```text
intro → a1_preview → a1_audio → a1_review
      → a2_preview → a2_audio → a2_review
      → b_intro    → b_audio
      → c1_preview → c1_audio → c1_review
      → c2_preview → c2_audio → c2_review → c2_final_review
      → submitted
```

Plus the orthogonal terminal state `paywalled`.

In strict modes every advance must equal `Next(currentState)`. Free
modes (Paper, Learning, Diagnostic) accept any FSM state in any order
as long as the destination is in `LISTENING_FSM_STATES`.

---

## 4. 12-stage learner pathway (R11)

```text
1.  diagnostic
2.  foundation_partA
3.  foundation_partB
4.  foundation_partC
5.  drill_partA
6.  drill_partB
7.  drill_partC
8.  minitest_partA
9.  minitest_partBC
10. fullpaper_paper
11. fullpaper_cbt
12. exam_simulation
```

Pass thresholds (scaledScore via `OetScoring`):

- Stages 10–12: **≥ 350** (the official pass anchor)
- Stages 2–9: **≥ 300** (foundation/drill/mini-test gate)
- Stage 1 (diagnostic): no threshold; just needs to be submitted

Recompute is idempotent, runs on every grade, and is back-filled at
startup by [`ListeningV2BackfillService`](../backend/src/OetLearner.Api/Services/Listening/ListeningV2BackfillService.cs).

---

## 5. Endpoints

All under `/v1/listening/v2/...`, all
`.RequireAuthorization("LearnerOnly").RequireRateLimiting("PerUser")`,
writes `+ "PerUserWrite"`. Mounted by
[`ListeningV2Endpoints`](../backend/src/OetLearner.Api/Endpoints/ListeningV2Endpoints.cs)
alongside the legacy `/v1/listening-papers` group.

|Verb|Path|Purpose|
|---|---|---|
|GET|`/attempts/{id}/state`|Current FSM snapshot (lazy-seeds Intro).|
|POST|`/attempts/{id}/advance`|Two-step advance. Returns 412 with confirm-token on first call in strict modes.|
|POST|`/attempts/{id}/audio-resume`|5-second grace window for cue-point resume.|
|POST|`/attempts/{id}/grade`|Version-pinned grade + auto-recompute pathway.|
|GET|`/me/pathway`|12-stage snapshot.|
|GET/POST/DELETE|`/teacher/classes`|CRUD with owner-only filtering.|
|POST/DELETE|`/teacher/classes/{id}/members`|Roster management.|

---

## 6. Data model touchpoints

`ListeningAttempt` carries the V2 additive columns
(`NavigationStateJson`, `WindowStartedAt`, `WindowDurationMs`,
`AudioCueTimelineJson`, `TechReadinessJson`, `AnnotationsJson`,
`HumanScoreOverridesJson`, `LastQuestionVersionMapJson`). All are
nullable jsonb on Postgres (plain TEXT on SQLite via the conditional
mapping in `LearnerDbContext.OnModelCreating`) and **never LINQ-queried**
— see repo memory `ef-core-sqlite-translation.md` for why.

---

## 7. Frontend surface

- Learner player route:
  [`app/listening/player/[id]/page.tsx`](../app/listening/player/[id]/page.tsx)
  — active route that branches per-mode on the server-returned attempt `mode`
  and composes extracted player chrome from
  [`components/domain/listening/player/`](../components/domain/listening/player/).
  Consumes `listeningV2Api`, `PartARenderer`, and `TechReadinessCheck`. The
  R06.10 two-step confirm-token retry is now used for strict start/resume and
  strict forward phase advances. Strict audio-resume validation pauses playback
  while the server verdict is pending, resumes only after approval, and fails
  closed with a paused player warning on validation failure. Section cue
  enforcement uses the full authored extract window for multi-extract sections
  such as Part B. The route
  has lock/submit confirmation copy, but the full R06.11 unanswered-number
  banner and R07.3 all-parts paper final-review parity remain later player
  migration work. R08 Part B/C annotation behavior is delegated to
  [`BCQuestionRenderer.tsx`](../components/domain/listening/BCQuestionRenderer.tsx)
  and in-app zoom is delegated to
  [`ZoomControls.tsx`](../components/domain/listening/ZoomControls.tsx).
- Wire client: [`lib/listening/v2-api.ts`](../lib/listening/v2-api.ts)
  — typed wrappers around `apiClient`; exposes `AdvanceResult` for the
  confirm-token retry.
- Audio integrity helpers:
  [`lib/listening/audio-integrity.ts`](../lib/listening/audio-integrity.ts).
- Reusable Part A renderer:
  [`components/domain/listening/PartARenderer.tsx`](../components/domain/listening/PartARenderer.tsx).
- Component workbench: opt-in Storybook config and Listening V2 player stories
  live in [`.storybook/main.ts`](../.storybook/main.ts),
  [`docs/STORYBOOK.md`](STORYBOOK.md), and
  [`components/domain/listening/player/__stories__/`](../components/domain/listening/player/__stories__/).

## 8. Admin analytics surfaces

- Admin aggregate analytics: [`app/admin/analytics/listening/page.tsx`](../app/admin/analytics/listening/page.tsx).
- Admin per-question deep dive:
  [`app/admin/analytics/listening/question/[paperId]/[number]/page.tsx`](../app/admin/analytics/listening/question/[paperId]/[number]/page.tsx)
  — filters the class-wide Listening analytics DTO to a single `(paperId,
  questionNumber)` tuple and surfaces accuracy, distractor heat, and contextual
  misspelling signal.
- Reusable Part B/C renderer with stem highlight and option
  strikethrough:
  [`components/domain/listening/BCQuestionRenderer.tsx`](../components/domain/listening/BCQuestionRenderer.tsx).
- In-app question zoom controls:
  [`components/domain/listening/ZoomControls.tsx`](../components/domain/listening/ZoomControls.tsx).
- Tech-readiness probe (R10):
  [`components/domain/listening/TechReadinessCheck.tsx`](../components/domain/listening/TechReadinessCheck.tsx).
- Learner pathway page:
  [`app/listening/pathway/page.tsx`](../app/listening/pathway/page.tsx),
  which renders
  [`components/domain/listening/PathwayBoard.tsx`](../components/domain/listening/PathwayBoard.tsx)
  (R11).
- Teacher classes page:
  [`app/listening/classes/page.tsx`](../app/listening/classes/page.tsx).
- FSM mirror (parity-tested against the C# table):
  [`lib/listening/transitions.ts`](../lib/listening/transitions.ts).

> The earlier per-mode shells (`CbtPlayer`, `PaperModePlayer`,
> `LearningModePlayer`, `DiagnosticPlayer`) and their helpers
> (`SectionAdvanceConfirm`, `UnansweredQuestionsBanner`,
> `useListeningNavigation`) were removed in the V2 cleanup pass — the
> production learner route does not use per-mode player shells.

---

## 8. Validation

Run before merging any Listening change:

```powershell
# Frontend
npx tsc --noEmit          # MUST be 0 errors
npm run lint              # MUST be 0 errors/warnings
npm test                  # MUST be green

# Backend
cd backend
dotnet build OetLearner.sln                           # MUST be 0 errors
dotnet test OetLearner.sln --filter "FullyQualifiedName~Listening"
dotnet test OetLearner.sln                            # full backend regression
```

---

## 9. Per-profession rulebooks

Listening rulebooks live in `rulebooks/listening/<profession>/rulebook.v1.json`
(`kind: "listening"`). All four professions present:

- `rulebooks/listening/medicine/rulebook.v1.json`
- `rulebooks/listening/nursing/rulebook.v1.json`
- `rulebooks/listening/dentistry/rulebook.v1.json`
- `rulebooks/listening/physiotherapy/rulebook.v1.json`
