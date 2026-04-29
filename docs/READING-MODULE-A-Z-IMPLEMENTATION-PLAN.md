# Reading Module A-Z Implementation Plan

**Status:** active implementation plan.  
**Source brief:** OET Reading exam structure, simulator, authoring, analytics, pathway, compliance, and content-scale requirements supplied on 2026-04-29.  
**Current baseline:** the codebase already has relational Reading authoring, policy configuration, learner-safe endpoints, attempt lifecycle, grading, result review, admin paper editor integration, and canonical scoring anchored at `30/42 = 350/500`.

## Non-Negotiable Product Rules

1. Reading papers stay canonical: Part A = 20 items, Part B = 6 items, Part C = 16 items, total = 42.
2. Part A is a strict 15-minute section. Learners cannot return to it after the window ends in exam mode.
3. Parts B and C share the remaining 45-minute section.
4. Learner endpoints never expose correct answers, accepted variants, or explanations before submit.
5. Every score conversion routes through `OetScoring` or `lib/scoring.ts`; no inline pass/fail thresholds.
6. Part A short answers are exact-answer by default; synonym acceptance is explicitly non-standard mode.
7. All content must be original, licensed, or properly permissioned; leaked or recalled OET content is forbidden.
8. Published content requires academic, medical, language, and provenance quality gates.

## Phase 0 - Baseline Mapping and Gap Lock

**Goal:** prevent duplicate systems and identify the safest extension points.

Delivered baseline:

1. Backend entities: `ReadingPart`, `ReadingText`, `ReadingQuestion`, `ReadingAttempt`, `ReadingAnswer`, `ReadingPolicy`.
2. Backend services: structure validation, attempt lifecycle, exact grading, policy resolution, expiry worker.
3. Admin endpoints and editor for structured Reading papers.
4. Learner home, paper player, and results review pages.
5. Tests covering canonical counts, grading, policy, and the 30/42 scoring anchor.

Exit criteria:

1. This plan is checked in.
2. New work extends the existing Reading subsystem rather than creating a parallel module.

## Phase 1 - Exam-Mode Fidelity

**Goal:** make the simulator match the operational exam flow.

Tasks:

1. Lock Part A at the policy Part A deadline.
2. Auto-move the learner to Part B/C when Part A expires.
3. Prevent navigation back to Part A in exam mode after lock.
4. Auto-submit the attempt when the B/C shared window ends.
5. Preserve autosaved answers and use the server as the source of truth.
6. Add unanswered warnings before manual submit.
7. Add desktop-first warning for small screens.

Acceptance criteria:

1. The player no longer lets learners answer or revisit Part A after the Part A timer.
2. The player submits automatically at the end of the 60-minute exam window.
3. Backend hard locks still enforce the same rules if the browser is bypassed.

## Phase 2 - Authoring Workflow Completion

**Goal:** make teachers/admins able to create high-quality papers without developer help.

Tasks:

1. Improve the Reading structure editor for faster bulk entry and validation.
2. Add JSON import/export for Reading structures.
3. Add review states: draft, academic review, medical review, language review, pilot, publish, retire.
4. Add evidence sentence, distractor category, difficulty, estimated time, and text type fields.
5. Add authoring validation for four Part A texts, six Part B extracts, and two Part C texts.
6. Add provenance enforcement and copyright review notes.

Acceptance criteria:

1. Admins can produce a complete 42-item paper from the UI.
2. Publish remains blocked until structure, assets, provenance, and review checks pass.

## Phase 3 - Learner Practice Modes

**Goal:** support both exam simulation and teaching practice.

Tasks:

1. Add learning mode with pause, retry, explanations, and optional hints.
2. Add Part A standalone drills.
3. Add Part B workplace text drills.
4. Add Part C paragraph, attitude, inference, vocabulary, and reference drills.
5. Add timed mini-tests of 5, 10, and 15 minutes.
6. Add an error bank that retests missed questions or skills.

Acceptance criteria:

1. Learners can choose strict exam mode or instructional practice mode.
2. Practice mode is clearly marked as non-standard when it changes OET-faithful rules.

## Phase 4 - Results, Analytics, and Diagnosis

**Goal:** turn attempts into actionable feedback.

Tasks:

1. Show raw score, estimated scaled score, grade, and practice disclaimer.
2. Add part breakdown: A /20, B /6, C /16.
3. Add skill breakdown: scanning, purpose, detail, inference, vocabulary, reference, attitude.
4. Add timing breakdown by part and question.
5. Diagnose Part A spelling, incomplete phrases, over-answering, and wrong-text errors.
6. Diagnose B/C distractors: opposite meaning, too broad, too narrow, not stated, wrong speaker.

Acceptance criteria:

1. A learner report can recommend the next drill based on lost marks.
2. Teachers can see common error patterns without inspecting every response manually.

## Phase 5 - Teacher and Admin Dashboards

**Goal:** make Reading performance manageable at class and cohort level.

Tasks:

1. Add class average by part and skill.
2. Add hardest-question and common-distractor reports.
3. Add student risk labels: red, amber, green.
4. Add homework assignment and completion tracking.
5. Add paper-quality analytics for item discrimination and poor-performing questions.

Acceptance criteria:

1. Teachers can assign targeted work from dashboard evidence.
2. Admins can retire or revise weak content based on attempt data.

## Phase 6 - Course Pathway Integration

**Goal:** connect Reading tests to a structured learning journey.

Tasks:

1. Add diagnostic Reading placement.
2. Sequence Part A strategy lessons, drills, Part B lessons, Part C lessons, mixed practice, full mocks, and readiness report.
3. Feed weak skills into study plans and next actions.
4. Add readiness report text using raw, scaled estimate, part score, weak skills, and recommended drills.

Acceptance criteria:

1. Reading outcomes automatically influence the learner pathway.
2. The next action is specific, not generic.

## Phase 7 - AI-Assisted Extraction With Human Approval

**Goal:** reduce admin data-entry time without trusting AI blindly.

Tasks:

1. Extract PDF text server-side.
2. Route all AI prompts through the grounded AI gateway.
3. Require strict JSON schema for the Reading structure manifest.
4. Stage AI output as draft only.
5. Require human approval before live structure changes.
6. Record AI usage and extraction warnings.

Acceptance criteria:

1. AI extraction never auto-publishes a Reading paper.
2. Invalid or uncertain extraction output is visible to admins and safely rejected.

## Phase 8 - Content Scale and Quality Operations

**Goal:** support a serious Reading bank safely.

Tasks:

1. Build 20-30 full Reading mocks.
2. Build 50+ Part A standalone tests.
3. Build 300+ Part B extracts.
4. Build 100+ Part C long texts.
5. Build 200+ skill drills and 50 healthcare vocabulary sets.
6. Add import templates and reviewer checklists.

Acceptance criteria:

1. Content production can scale without lowering quality gates.
2. Every item has provenance, answer key, explanation, skill tag, and review status.

## Phase 9 - Compliance, QA, and Launch Readiness

**Goal:** make the module safe for commercial academy use.

Tasks:

1. Add academic integrity policy and confidential-content warnings.
2. Add accessibility checks for keyboard, screen reader, contrast, and font scaling.
3. Add unit, backend, E2E, and browser-smoke coverage for exam flow.
4. Add operational runbook for publishing, rollback, content retirement, and policy changes.
5. Add production monitoring for submit failures, timer issues, and grading anomalies.

Acceptance criteria:

1. Typecheck, lint, focused unit tests, backend tests, and browser smoke pass for release candidates.
2. The academy can explain that scores are practice estimates unless officially licensed scoring is available.

## Implementation Start

This turn starts Phase 1 and Phase 4:

1. Phase 1: enforce frontend Part A no-return and auto-submit at the B/C deadline.
2. Phase 4: add part and skill breakdowns to the learner review payload and results UI.
