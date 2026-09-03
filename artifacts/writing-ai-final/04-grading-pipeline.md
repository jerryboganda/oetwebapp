# 04 — Candidate Grading Pipeline (Runtime)

## Runtime inputs (every fresh grade)

1. Canonical case notes (`CaseNotesSnapshot` from structured sentences).
2. Exact Writing Task (`TaskPromptMarkdown`).
3. Candidate letter (any length, including empty).
4. Official OET Writing criteria (six criteria via grounded prompt +
   `rulebooks/writing/common/assessment-criteria.json`: Purpose 0–3, others 0–7).
5. Profession-specific rulebook (v1.0.1) + shared rules, injected by
   `IAiGatewayService.BuildGroundedPrompt({Kind: Writing, Profession,
   LetterType: genre token, Task: Score})`; AI citations filtered to
   `AppliedRuleIds`.

Explicitly NOT inputs: Model Answer text/similarity/embeddings (proven by
`EvaluateAsync_GradingInput_NeverContainsModelAnswerText` canary test);
Speaking rulebooks (RuleKind.Writing only); full rulebook PDFs (cached JSON).

## Prompt shape (`BuildRubricInput`)

`Scenario / Profession / Letter type / Task prompt / Case notes (source of
truth; do not invent facts) / Word count / Candidate letter` + instruction to
reply with the single grounded JSON contract (`findings, criteriaScores,
estimatedScaledScore, estimatedGrade, …`). Temperature 0.2,
`FeatureCode: WritingGrade`, `PromptTemplateId: writing.score.v1`.

## Structured result

`ParseRubric` requires the complete six-criterion contract with in-range
scores and scaled score in `[ScaledMin, ScaledMax]`; anything else fails loud
(`writing_rubric_failed`, retryable) — never a fabricated grade. Persisted as
`WritingGrade` (C1 0–3, C2–C6 0–7, raw total, band) + deterministic
`WritingAssessmentReportV11` (rule-engine findings, fact map, six criteria) +
per-submission `WritingAssessmentModelAnswer` (copy of the saved pregen for
display, or legacy live fallback). Credit reservation committed once.

## Blank / near-blank path (new)

Whitespace-only letters skip preflight letter checks and take
`GradeBlankSubmissionAsync`: zeros on all six criteria, band E, honest
"no assessable content" per-criterion feedback, zero provider calls, zero
credit hold. Shape matches normal grading so candidate surfaces render
unchanged. Covered by `EvaluateAsync_BlankLetter_GradesZeroWithNoProviderCall`.

## Persistence & recovery

Submission row is written BEFORE grading (letter survives provider failure,
refresh, network loss). Failed attempts keep the letter + hashes +
reservation reference + any persisted provider result; `POST …/retry-grade`
resumes the same logical attempt (claim reset → `EvaluateAsync`, idempotent
reservation, provider-result resume). Grading page shows a failed-state card
with Retry (same submission) + Back-to-library.
