# OET Listening and Reading AI System v1.1 Design

**Status:** Implementation design derived from `C:\Users\Dr Faisal Maqsood PC\Downloads\OET_Listening_and_Reading_AI_System_Specification_v1.1.pdf`.

**Scope:** Website, computer-based Listening and Reading delivery only. Paper-based exam simulation is out of scope. Real-exam technical requirements such as ProProctor, 1920x1080 resolution, wired headsets, and VPN state are candidate-facing exam-day guidance, not enforced platform constraints.

## Goal

Bring the existing OET Listening and Reading modules into conformance with specification v1.1 across deterministic delivery, server-authoritative attempts and timers, strict marking, versioned scoring, candidate results, grounded post-submit AI, admin authoring, role boundaries, reliability, privacy, and release acceptance evidence.

## Current baseline and confirmed gaps

The repository already has relational Listening and Reading entities, learner-safe projections, server-side attempt services, Listening FSM transitions, Reading Part A and shared Part B/C timing, autosave endpoints, annotation persistence, authoring validation, review surfaces, and focused unit/backend/E2E tests.

The audit identified these conformance gaps that this design must close:

1. `backend/src/OetLearner.Api/Services/OetScoring.cs` and `lib/scoring.ts` contain a piecewise raw-to-500 formula. The specification forbids inventing or silently interpolating a conversion. A configured lookup table is required.
2. Listening and Reading result pages use accuracy gauges rather than a distinct score-band graph carrying the persistent label `AI Practice Score — not an official OET result.`.
3. The Listening result page does not render the stricter-than-examiner-discretion spelling disclosure required by Section 6.
4. Marking-policy defaults and admin-editable normalization options exist, but the active profile is not represented as an immutable, owner-approved version attached to every scored attempt.
5. Question explanations and evidence exist on authored questions, but the v1.1 contract requires an explicit approved-evidence boundary for every post-submit AI explanation and a deterministic fallback when no approved explanation exists.
6. Existing admin permission policies are granular for platform admins, but the v1.1 role matrix still needs explicit content-author, tutor, clinical-reviewer, language-assessor, and time-limited customer-support scopes for these surfaces.
7. Existing reliability controls are distributed across module services and require a single acceptance matrix proving interruption, timer drift, refresh, duplicate submission, audio fault, key defect, privacy, and AI-failure behavior.

## Non-negotiable invariants

- Deterministic marking is authoritative. No LLM, fuzzy matching, spelling correction, semantic similarity, embeddings, stemming, synonym inference, partial credit, or inferred equivalence can change a mark.
- Typed answers compare against the canonical answer plus explicitly authored accepted variants only. Leading/trailing-space handling and any other normalization are controlled by a versioned marking-policy profile.
- Listening and Reading full papers contain exactly 42 marks: Listening A=24, B=6, C=12; Reading A=20, B=6, C=16.
- Correct answers, rationale, transcript evidence, and model answers remain hidden until the relevant attempt is submitted or expires and is finalized.
- Attempt state, timer deadlines, section locks, finalization, and score calculation are server-authoritative. Refresh, reconnect, client clock changes, or local storage changes cannot grant time or unlock a section.
- Every answer is autosaved and the learner sees the latest confirmed save state. Offline recovery must be encrypted locally and reconciled against the server without overwriting newer authoritative data.
- An irreversible Listening boundary and final submission require explicit confirmation. Reading Part A locks at the authoritative 15-minute deadline; Reading Parts B and C share one authoritative 45-minute block.
- Every attempt stores the exact paper/test version, answer-key version, score-conversion version, marking-policy version, and relevant mode/policy snapshot.
- A score conversion table is mandatory for a scaled result. If no approved table covers a raw score, the system displays the auditable raw score and withholds scaled score, grade, and pass status; it never applies a formula fallback.
- Every AI call uses the existing grounded gateway or approved direct-call recorder, produces one usage record, and is post-submit only for this domain. AI failure cannot delay, modify, or prevent deterministic results.
- The platform never claims that an AI practice score is an official OET result.

## Architecture

### 1. Shared versioned assessment configuration

Add a shared configuration layer while retaining the existing `Listening*` and `Reading*` aggregates:

- `ScoreConversionTableVersion` and `ScoreConversionTableRow`: subtest, raw score, scaled score, grade band, effective dates, approval metadata, immutable-after-use state, and an optional pathway/regulator mapping.
- `MarkingPolicyVersion`: exact normalization settings, case behavior, accepted-variant rules, and policy provenance. The attempt stores the resolved version, not a live mutable policy reference.
- `AssessmentRationale`: approved source sentence/paragraph, author-approved explanation, error category, editable study recommendation, owner, reviewer, and audit timestamps.
- `AssessmentReMarkJob` and item-level re-mark audit records: original score, new score, reason, approver, key/policy version transition, affected candidates, and completion status.

The shared layer is consumed by both module graders and result projections. Existing entities remain the source of module-specific content, options, extracts, sections, answers, annotations, and attempt events.

### 2. Deterministic marking and score calculation

The Listening and Reading graders remain independent at the module boundary but share exact comparison helpers and version-resolution contracts.

- Listening Part A typed answers use canonical plus explicit accepted variants. MCQ answers match exactly one versioned option key. A strikethrough/elimination annotation is never a selected answer.
- Reading Part A uses strict matching for Q1–7 A/B/C/D, Q8–14 short answers, and Q15–20 single-word/number completion. Paraphrase, synonym, wrong singular/plural form, wrong unit, wrong number, wrong order, and extra words fail unless explicitly permitted by the keyed variant/profile.
- Reading Parts B and C use one exact selected key per MCQ, with no negative marking.
- Invalid data, including an MCQ with zero or multiple correct options, makes automated scoring invalid and routes the attempt to admin review rather than guessing.
- Each scored item records its outcome and exact error category. `incorrect form` is distinct from a generic wrong answer.
- Raw totals are reproducible from stored response rows, immutable question/key versions, marking-policy version, and explicit overrides. Any human override is separate, reasoned, assigned, and audited.

### 3. Server-authoritative delivery

Preserve and harden the current attempt services and Listening FSM rather than adding a second client-owned workflow.

Listening delivery covers preflight identity/eligibility, audio integrity and duration verification, sound check, device/network metadata, authoritative start, A1/A2, B1–B6, C1/C2 sequencing, one-play scored audio, forward-only navigation, unanswered warnings, boundary confirmation, event logging, and admin review on verified playback faults. Audio URLs must be capability-bound to the attempt or authorized media service and must not expose answer data.

Reading delivery covers four Part A texts, 20 questions, a hard 15-minute deadline, an optional break that cannot reopen Part A, one shared 45-minute B/C deadline, six Part B questions, two eight-question Part C texts, responsive split/stacked layouts, text highlights, configurable answer-choice elimination, zoom, keyboard focus, and timer visibility.

Both modules persist deadlines and section state, use optimistic concurrency for answer/submit races, reconcile offline autosave without trusting client time, and make finalization idempotent.

### 4. Results and grounded AI

Create a shared result contract and presentation primitives used by both modules:

- overall raw score out of 42, part totals and percentages, correct/incorrect/unanswered counts, time used per section and total, converted practice score when an approved table exists, conversion-table version, grade band, and configured pathway pass indicator;
- a platform-branded score-band graph using platform colors and typography, visually distinct from the official OET graphic, with the exact persistent label `AI Practice Score — not an official OET result.`;
- prioritized mistakes followed by complete item review; typed answer, correct answer, exact error category, source sentence/evidence, and approved rationale where available;
- MCQ candidate option, correct option, distractor category, and author-approved explanation;
- evidence-counted error patterns and editable next-step recommendations;
- the required Listening marking-strictness disclosure on all relevant Listening result screens.

AI may explain an already-marked item from stored approved evidence, classify observed patterns with evidence counts, produce an editable study plan, answer a result-screen question within available evidence, and summarize strengths/priorities. If evidence is absent or the AI call fails, the UI states that no approved explanation is available and retains the deterministic result.

### 5. Admin authoring, validation, and governance

Listening and Reading authoring must validate every published question for part, type, answer key, mark value, options, accepted variants, rationale/evidence, and status. Typed variants display author, timestamp, and reason. MCQs reject duplicate options and require exactly one correct option. Listening audio validates integrity, duration, and expected section timing.

Before publish, admins receive both a full candidate preview and a marking preview. Publish is blocked by validation errors. Corrected keys or variants use a controlled re-mark workflow and never silently overwrite historical scores. Conversion tables are date-effective, versioned, locked after use, and require formal reprocessing approval for changes.

### 6. Roles, privacy, and failure handling

Enforce the v1.1 matrix at the API and UI boundaries:

- Candidate: own attempts/results only.
- Tutor: assigned candidates only; comments/overrides are audited.
- Content Author: question/key/variant authoring without candidate PII.
- Clinical Reviewer and Language Assessor: explicit scoped access where configured, not required for deterministic L&R marking.
- Admin: full access with audit logging.
- Customer Support: ticket-linked, candidate-scoped, time-limited access.

Attempt/result data uses the existing storage and data-protection boundaries, with retention/deletion rules, export logging, restricted projections, and no answer-key leakage. Network interruption, audio buffering, refresh, duplicate submit, time drift, key defects, and AI failures have deterministic recovery states and acceptance tests.

## Delivery decomposition

1. **Configuration and deterministic scoring:** schema/migrations, approved table APIs, marking-policy versions, shared exact matcher, grader changes, raw reproducibility, and fail-closed scaled results.
2. **Listening delivery:** preflight, integrity-bound audio, authoritative playback state, section FSM, event logs, lock enforcement, and mobile/desktop player behavior.
3. **Reading delivery:** Part A lock, shared B/C timer, annotations, navigation, answer-choice elimination, responsive layout, and server/API lock enforcement.
4. **Results and post-submit AI:** common result DTOs, branded graph, disclosure, item/error review, approved evidence, grounded explanations, pattern analytics, and study links.
5. **Admin governance and roles:** candidate/marking previews, publish validation, variants, rationale library, conversion-table lifecycle, controlled re-mark, role scopes, and audit views.
6. **Reliability and release acceptance:** encrypted offline reconciliation, clock synchronization, duplicate-submit proofs, concurrency/load tests using the owner’s peak-attempt target, LR-01–LR-16 automated coverage, production deployment, and live health/route verification.

## Required owner-controlled release inputs

These values must be supplied or approved before the corresponding behavior can be released; the implementation must not guess them:

- complete raw 0–42 to scaled 0–500 lookup rows for Listening and Reading, table version, effective date, and approval;
- exact capitalisation and spacing normalization profile and version;
- practice-mode versus full-mock navigation/lock policy and learner-facing labeling;
- approved rationale/evidence library and editable recommendation catalog;
- pathway/regulator pass thresholds;
- confirmation that exam-day technical requirements remain guidance-only rather than an enforced rehearsal mode;
- final score-graph visual/legal approval;
- expected peak concurrent timed attempts for load testing.

Until these inputs exist, the affected production release gates remain closed. Raw deterministic marking and audit-safe authoring can be developed independently, but no unapproved scaled score or pass claim may be emitted.

## Acceptance evidence

The implementation plan must map and prove each PDF acceptance test LR-01 through LR-16. The evidence must include backend tests for authority, locking, marking, versioning, idempotency, and failure handling; frontend unit tests for disclosure/graph/navigation; Playwright checks for desktop/mobile clipping and pre-submit secrecy; and production health/route checks after deployment. A passing build alone is not completion evidence.
