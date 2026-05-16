# Listening V2 — Rulebook → Code Citation Map

**Status:** Authoritative. Every R-code below is mapped to the file that
implements it (production code) and the test that pins the behavior.
Any change to the rulebook MUST be reflected here.

Source rulebooks: the Listening V2 source documents captured in
[`PRD-LISTENING-V2.md`](../PRD-LISTENING-V2.md) plus `Project Real Content/Listening/`
academy content.

|R-Code|Topic|Implementation|Pinned by test|
|---|---|---|---|
|R01|42 raw item canonical max|[`ListeningStructureService.cs`](../backend/src/OetLearner.Api/Services/Listening/ListeningStructureService.cs#L55)|`OetScoringTests.IsListeningReadingPassByRaw_Respects_30_Of_42_Threshold`|
|R02|30/42 ≡ 350/500 pass anchor|[`OetScoring.cs`](../backend/src/OetLearner.Api/Services/OetScoring.cs#L162)|`ListeningGradingServiceTests.GradeAsync_routes_raw_to_scaled_via_OetScoring` + `ListeningScoringPathAuditTest`|
|R03|Five attempt modes (Exam / OET-Home / Paper / Learning / Diagnostic)|[`ListeningModePolicy.cs`](../backend/src/OetLearner.Api/Services/Listening/ListeningModePolicy.cs)|`ListeningV2PathwayLaunchTargetEndpointTests` + mode-policy endpoint coverage|
|R04|Server-authoritative FSM for strict start/resume/phase advances and fail-closed audio resume, including pending validation|[`ListeningSessionService.cs`](../backend/src/OetLearner.Api/Services/Listening/ListeningSessionService.cs), [`ListeningFsmTransitions.cs`](../backend/src/OetLearner.Api/Services/Listening/ListeningFsmTransitions.cs), [`lib/listening/transitions.ts`](../lib/listening/transitions.ts), [`app/listening/player/[id]/page.tsx`](../app/listening/player/[id]/page.tsx)|`tests/unit/listening/transitions.parity.test.ts`, `cbla-fidelity.test.tsx`, `audio-resume.test.tsx`, `ListeningV2AdvanceEndpointTests`|
|R05|Wall-clock window anchors and section cue-window enforcement|`ListeningAttempt.WindowStartedAt` + `WindowDurationMs` ([`ListeningEntities.cs`](../backend/src/OetLearner.Api/Domain/ListeningEntities.cs#L391)) plus active player extract `audioStartMs`/`audioEndMs` enforcement|`cbla-fidelity.test.tsx` strict resume hydration, `audio-resume.test.tsx` multi-extract Part B gate, and backend session tests|
|R06.10|Two-step confirm-token on advance|[`ListeningConfirmTokenService.cs`](../backend/src/OetLearner.Api/Services/Listening/ListeningConfirmTokenService.cs)|`ListeningV2AdvanceEndpointTests.Advance_returns_confirm_payload_on_first_strict_mode_request_and_applies_echoed_token` + `cbla-fidelity.test.tsx` strict transition tests|
|R06.11|Unanswered-questions banner|[`app/listening/player/[id]/page.tsx`](../app/listening/player/[id]/page.tsx) lists exact unanswered question numbers before section lock or final submit|`cbla-fidelity.test.tsx` exact unanswered-number tests|
|R07.3|Paper-mode final-review banner (last 2 min)|[`app/listening/player/[id]/page.tsx`](../app/listening/player/[id]/page.tsx) renders all section question groups and free-navigation controls from paper mode policy|`cbla-fidelity.test.tsx` all-parts paper review test + `listening-player-components.test.tsx` free-navigation stepper test|
|R08|Part A no annotation tools; B/C stem highlight, option strikethrough, and in-app zoom|[`PartARenderer.tsx`](../components/domain/listening/PartARenderer.tsx), [`BCQuestionRenderer.tsx`](../components/domain/listening/BCQuestionRenderer.tsx), [`ZoomControls.tsx`](../components/domain/listening/ZoomControls.tsx), wired by [`app/listening/player/[id]/page.tsx`](../app/listening/player/[id]/page.tsx)|`PartARenderer.test.tsx`, `BCQuestionRenderer.test.tsx`, `ZoomControls.test.tsx`, `cbla-fidelity.test.tsx`|
|R09|Version-pinned grading|[`ListeningGradingService.cs`](../backend/src/OetLearner.Api/Services/Listening/ListeningGradingService.cs) reads `LastQuestionVersionMapJson`|`ListeningGradingServiceTests`|
|R10|Pre-attempt tech-readiness probe|[`TechReadinessCheck.tsx`](../components/domain/listening/TechReadinessCheck.tsx)|`cbla-fidelity.test.tsx` + Playwright `listening-r10-readiness-gate.spec.ts`|
|R11|12-stage learner pathway|[`ListeningPathwayProgressService.cs`](../backend/src/OetLearner.Api/Services/Listening/ListeningPathwayProgressService.cs) + [`PathwayBoard.tsx`](../components/domain/listening/PathwayBoard.tsx)|`tests/unit/listening/PathwayBoard.test.tsx`|
|R12|Cross-skill teacher classes (OWASP A01)|[`TeacherClassService.cs`](../backend/src/OetLearner.Api/Services/Listening/TeacherClassService.cs)|`TeacherClassServiceTests`|
|R13|Pre-submit DTO leak shield|(audit)|`ListeningLearnerLeakAuditTest`|
|R14|Inline-math forbidden in service tree|(audit)|`ListeningScoringPathAuditTest`|

## Mode policy matrix (R03)

|Mode|Free nav|One-way locks|Audio replay|Timer enforced|Confirm token required|
|---|---:|---|---|---|---|
|Exam (CBT)|❌|✅|❌|✅|✅|
|OET-Home|❌|✅|❌|✅|✅|
|Paper|✅|❌|❌|✅|❌|
|Learning|✅|❌|✅|❌|❌|
|Diagnostic|✅|❌|❌|❌|❌|

## 12-stage pathway order (R11)

1. `diagnostic`
2. `foundation_partA`
3. `foundation_partB`
4. `foundation_partC`
5. `drill_partA`
6. `drill_partB`
7. `drill_partC`
8. `minitest_partA`
9. `minitest_partBC`
10. `fullpaper_paper`
11. `fullpaper_cbt`
12. `exam_simulation`

Pass thresholds: stages 10–12 require scaledScore ≥ 350 (30/42); stages
2–9 require ≥ 300; stage 1 has no threshold.

## Mission-critical invariants

1. `OetScoring.OetRawToScaled` is the **only** raw→scaled path.
   `ListeningScoringPathAuditTest` source-scans the entire
   `Services/Listening/` tree for inline math (`* 350`, `/ 42`,
   `* 8.33`, etc.) and fails the build if any is found.
2. Every grading event MUST snapshot `LastQuestionVersionMapJson` so
   admin question edits never silently invalidate in-flight attempts.
3. Every teacher-class read/write MUST filter by `OwnerUserId ==
   currentUserId`. `TeacherClassServiceTests` pin all four refusal paths.
4. The TS FSM table in `lib/listening/transitions.ts` MUST stay
   identical to `ListeningFsmTransitions.cs`.
   `tests/unit/listening/transitions.parity.test.ts` enforces parity.
