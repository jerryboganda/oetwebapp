# OET Writing AI Assessment v1.1 Design

## Goal

Implement the supplied `OET_Writing_AI_Assessment_Specification_v1.1.pdf` as the
authoritative website/computer-based Writing assessment contract. The existing
Writing V2 submission route remains the entry point, but it must produce a
versioned, grounded, auditable assessment report or a fail-closed review state.

## Decisions and release posture

- Paper-based exam simulation remains out of scope, exactly as the PDF states.
- Medicine is the only profession pack that may be candidate-facing initially.
  Nursing, Pharmacy, and any other profession are blocked until an owner-approved
  profession pack exists; the Medicine pack is never silently reused.
- Transfer and referral-to-GP remain blocked for candidate-facing scoring until
  owner-approved detailed packs exist. The system may classify them, but it must
  return `requires_review` instead of inventing rules.
- Candidate-facing numeric scores are blocked until the calibration release gate
  has an owner-approved benchmark, tolerance, reviewer set, and criterion-priority
  result. Scores may be retained for restricted calibration/admin review only.
- Missing task text, recipient/request evidence, diagnosis/plan evidence, or any
  unreadable critical case-note page blocks scoring. The original submission is
  immutable and reprocessable.
- The score graph uses platform-owned colours and typography and always carries
  the exact persistent label: `AI Estimated Practice Score — not an official OET result`.
  Confidence is displayed as both a label and likely range until the owner changes
  that governed presentation setting.

## Architecture

1. `WritingAssessmentPreflightService` snapshots the submitted task, ordered case
   note pages, profession, letter text, timing, rule-pack versions, and model
   versions. It performs no inference when a required input is absent.
2. `WritingTaskUnderstandingService` parses the written task, classifies the
   letter type with evidence, identifies recipient/urgency/purpose/request, and
   builds a fact map from the case notes. It returns `requires_review` on conflict.
3. `WritingV11RuleEngine` composes the existing deterministic Rulebook engine with
   the exact v1.1 hard exceptions, discharge exclusion list, Re-line age/DOB rule,
   punctuation house style, and primary-criterion map. Each error has one scoring
   criterion even if the report references it pedagogically under another.
4. `WritingGroundedAssessmentService` calls the existing grounded AI gateway only
   after preflight and deterministic checks. It validates the complete JSON
   contract, removes ungrounded claims, requires six in-range criterion scores with
   at least two evidence observations where possible, and never converts six marks
   linearly to 0–500.
5. `WritingCalibrationReleaseService` owns benchmark letters, two human ratings,
   matched content/language pairs, MAE/band/pass/fairness metrics, criterion
   priority correlation, model/version approval, and release blocking.
6. `WritingModelAnswerService` generates a separate model answer only after
   scoring. Every clinical sentence must map to a case-note fact; failed grounding
   holds the answer for review rather than displaying it.
7. `WritingAssessmentReportService` stores the complete report and projects it to
   the existing learner/tutor/admin DTOs. The learner report contains header,
   score/graph/confidence, six criteria, top five priorities, complete grouped
   error table, fact map, strengths, model answer/rationale, study plan, and all
   version labels.

## Persistence and access

The report is append-only and linked to the immutable `WritingSubmission`. New
tables store preflight snapshots, task classification, fact evidence, material
errors, criterion evidence, calibration releases, and model-answer grounding.
Tutor overrides create a new reviewed projection and audit event; the original AI
report is never overwritten. Existing `LearnerOnly`, tutor-assignment, content
author, clinical-reviewer, language-assessor, admin, and ticket-scoped support
policies are reused, with every export/review action audited.

## Approaches considered

1. **Replace the existing Writing V2 pipeline.** This gives a clean contract but
   risks breaking established tutor, appeal, mock, and learner result flows.
2. **Add a parallel report API and leave the old pipeline untouched.** This lowers
   regression risk but leaves bypasses through the existing submission/result
   route, which violates the PDF's end-to-end requirement.
3. **Wrap the existing route with a v1.1 preflight/report pipeline (selected).**
   This preserves compatible DTOs and existing role boundaries while making
   preconditions, deterministic rules, grounding, calibration, and model-answer
   checks mandatory before the existing grade/result projection can succeed.

## Failure handling

- Missing/unreadable input: `blocked_missing_input` with exact missing fields.
- Conflicting classifier evidence: `requires_review` with evidence excerpts.
- Unsupported or unapproved pack: `blocked_release_gate`.
- AI/provider/malformed/ungrounded response: `awaiting_reprocess`; no grade or
  model answer is exposed.
- Model-answer grounding failure: score may remain restricted, but the answer is
  `held_for_review`; the original letter and case notes remain intact.
- Any release/configuration version change reruns calibration before numeric
  candidate output can be enabled.

## Acceptance coverage

The implementation will add backend and UI tests for W-01 through W-19, including
the smoking/alcohol and atopic-allergy hard exceptions, semicolon-before-
`however` authority, discharge exclusions, double-count suppression, calibration
priority correlation, brand label, unsupported pack blocking, and model-answer
fact traceability. A production verification checklist will prove the deployed
commit SHA, health endpoints, route availability, release-gate defaults, and the
absence of candidate-facing score output before approvals exist.
