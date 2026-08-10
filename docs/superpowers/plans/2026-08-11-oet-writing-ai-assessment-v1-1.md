# OET Writing AI Assessment v1.1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the existing Writing submission route implement every candidate-facing and governance requirement in `OET_Writing_AI_Assessment_Specification_v1.1.pdf`, with fail-closed release gates and auditable production evidence.

**Architecture:** Add a versioned Writing v1.1 report pipeline around the existing `WritingSubmission` entry point. Deterministic task/fact/rule services run before grounded AI; calibration and profession/letter-pack approval are enforced server-side; the existing learner/tutor/admin surfaces project the new report without exposing unapproved scores or ungrounded model answers.

**Tech Stack:** ASP.NET Core Minimal API, EF Core/PostgreSQL, existing `IRulebookLoader`/`WritingRuleEngine`, existing grounded `IAiGatewayService`, Next.js App Router/React/TypeScript, Vitest/xUnit, GitHub Actions blue/green deployment.

## Global Constraints

- Website/computer-based delivery only; paper-based exam simulation is out of scope.
- Never infer missing written-task, recipient, request, diagnosis/plan, or unreadable case-note input.
- Never silently apply Medicine rules to Nursing, Pharmacy, or another profession.
- Transfer and referral-to-GP require owner-approved detailed packs; no invented special rules.
- The punctuation house style in R12.9-R12.17 overrides generic grammar defaults.
- Smoking/alcohol and atopic-allergy checks are deterministic critical rules.
- Every material error has exactly one primary scoring criterion.
- Candidate-facing 0–500 output requires an approved human calibration release and is labelled `AI Estimated Practice Score — not an official OET result`.
- All AI calls use grounded gateway helpers and record usage; no direct provider call is allowed.
- Original submissions and original AI reports are immutable; reviewed overrides are append-only and audited.
- Do not stage or overwrite unrelated dirty worktree files, `.env*`, credentials, `.codex/config.toml`, or `.superpowers/`.

---

### Task 1: Persist the v1.1 report and governed release state

**Files:**
- Create: `backend/src/OetLearner.Api/Domain/WritingAssessmentV11Entities.cs`
- Create: `backend/src/OetLearner.Api/Data/LearnerDbContext.WritingAssessmentV11.cs`
- Create: `backend/src/OetLearner.Api/Data/Migrations/20260811120000_AddWritingAssessmentV11.cs`
- Modify: `backend/src/OetLearner.Api/Data/LearnerDbContext.cs`
- Modify: `backend/src/OetLearner.Api/Data/Migrations/LearnerDbContextModelSnapshot.cs`
- Test: `backend/tests/OetLearner.Api.Tests/Writing/WritingAssessmentV11PersistenceTests.cs`

**Interfaces:**
- Produce `WritingAssessmentReportV11`, `WritingAssessmentFactEvidence`, `WritingAssessmentError`, `WritingAssessmentCriterionEvidence`, `WritingAssessmentReleaseGate`, `WritingAssessmentPackVersion`, and `WritingAssessmentModelAnswer` entities.
- Produce append-only status/version fields that later services can query without parsing learner-facing JSON.

- [ ] **Step 1: Write failing persistence tests** for immutable original report linkage, release-gate default blocked state, one primary criterion per error, and model-answer held-for-review state.
- [ ] **Step 2: Run the focused xUnit class** and confirm the new entities/context are not yet available.
- [ ] **Step 3: Add the entities and partial EF model** with indexes on submission, profession/letter type/version, release status, and audit actor.
- [ ] **Step 4: Add the migration and snapshot update** using the project’s EF migration conventions; do not modify unrelated migrations.
- [ ] **Step 5: Run the focused persistence tests** and `git diff --check`.

### Task 2: Enforce input preconditions, profession packs, and task classification

**Files:**
- Create: `backend/src/OetLearner.Api/Services/Writing/WritingAssessmentPreflightService.cs`
- Create: `backend/src/OetLearner.Api/Services/Writing/WritingTaskUnderstandingService.cs`
- Create: `backend/src/OetLearner.Api/Services/Writing/WritingProfessionPackService.cs`
- Modify: `backend/src/OetLearner.Api/Services/Writing/WritingSubmissionService.cs`
- Modify: `backend/src/OetLearner.Api/Services/Writing/WritingScenarioService.cs`
- Test: `backend/tests/OetLearner.Api.Tests/Writing/WritingAssessmentPreflightTests.cs`
- Test: `backend/tests/OetLearner.Api.Tests/Writing/WritingTaskUnderstandingTests.cs`

**Interfaces:**
- `Task<WritingAssessmentPreflightResult> ValidateAsync(WritingSubmission submission, WritingScenario scenario, CancellationToken ct)`.
- `WritingTaskUnderstandingResult Understand(WritingAssessmentInput input)`.
- `Task<WritingProfessionPackResolution> ResolveAsync(string profession, string letterType, CancellationToken ct)`.

- [ ] **Step 1: Add tests** for missing task/page, unreadable page, absent profession, conflicting urgent/routine evidence, Medicine-only release, Nursing/Pharmacy block, and Transfer/GP pack block.
- [ ] **Step 2: Run the focused tests** to establish the failing contract.
- [ ] **Step 3: Implement snapshot-based preflight** using only scenario/task fields and ordered stored assets; return exact missing-field codes.
- [ ] **Step 4: Implement classification** for routine referral, urgent referral, discharge, non-medical, transfer, and GP referral with evidence excerpts and `requires_review` conflict state.
- [ ] **Step 5: Wire the service into submission evaluation before any AI call** and verify unsupported professions never enter the Medicine rule path.
- [ ] **Step 6: Run the focused tests and source-scan for fail-open profession fallback.**

### Task 3: Complete deterministic v1.1 rule enforcement and error ownership

**Files:**
- Create: `backend/src/OetLearner.Api/Services/Writing/WritingAssessmentV11RuleEngine.cs`
- Modify: `backend/src/OetLearner.Api/Services/Rulebook/WritingRuleEngine.cs`
- Modify: `backend/src/OetLearner.Api/Services/Rulebook/WritingCaseNotesMarkerExtractor.cs`
- Test: `backend/tests/OetLearner.Api.Tests/Writing/WritingAssessmentV11RuleEngineTests.cs`
- Test: `backend/tests/OetLearner.Api.Tests/Rulebook/WritingEngineParityTests.cs`

**Interfaces:**
- `WritingAssessmentRuleResult Evaluate(WritingAssessmentInput input, WritingTaskUnderstandingResult task, WritingFactMap facts)`.
- `PrimaryCriterionFor(string errorCategory)` returns exactly one of `purpose`, `content`, `conciseness_clarity`, `genre_style`, `organisation_layout`, `language`.

- [ ] **Step 1: Add W-03, W-04, W-05, W-06, W-07, W-16, W-17, and W-18 tests** with exact snippets, severity, rule ID, and primary criterion assertions.
- [ ] **Step 2: Run focused tests** and confirm missing detectors/ownership fail.
- [ ] **Step 3: Implement exact Re-line age/DOB boundary**, including child first-name behavior, adult title/last-name behavior, and rejection of `Master`.
- [ ] **Step 4: Implement discharge’s exact five-item exclusion list, urgent rules, smoking/alcohol and atopic-allergy exceptions, and authoritative punctuation patterns.
- [ ] **Step 5: Normalize every error category to one primary criterion** while retaining secondary pedagogical references only in report metadata.
- [ ] **Step 6: Run parity tests and the new focused tests.**

### Task 4: Build grounded fact mapping, language analysis, and complete error report

**Files:**
- Create: `backend/src/OetLearner.Api/Services/Writing/WritingFactMapService.cs`
- Create: `backend/src/OetLearner.Api/Services/Writing/WritingLanguageAnalysisService.cs`
- Create: `backend/src/OetLearner.Api/Services/Writing/WritingAssessmentReportBuilder.cs`
- Modify: `backend/src/OetLearner.Api/Services/Writing/WritingEvaluationPipeline.cs`
- Modify: `backend/src/OetLearner.Api/Services/Writing/WritingSubmissionEvaluationPipeline.cs`
- Test: `backend/tests/OetLearner.Api.Tests/Writing/WritingFactMapServiceTests.cs`
- Test: `backend/tests/OetLearner.Api.Tests/Writing/WritingAssessmentReportBuilderTests.cs`

**Interfaces:**
- `WritingFactMap Build(WritingAssessmentInput input)` with source page/line evidence for every fact.
- `IReadOnlyList<WritingAssessmentError> Analyze(...)` with exact candidate wording, correction, rule/source, severity, confidence, offsets, and primary criterion.
- `WritingAssessmentReportV11 Build(...)` requiring all six criteria and complete evidence.

- [ ] **Step 1: Add tests** for required/semi-relevant/irrelevant/inaccurate/invented facts, body-only word count, duplicate grouping, top-five ordering, and at least two criterion observations.
- [ ] **Step 2: Run focused tests** and confirm the report contract fails without implementations.
- [ ] **Step 3: Implement fact extraction from structured case-note sentences and task markdown**, preserving source references and never generating facts from candidate text.
- [ ] **Step 4: Implement language analysis with the Rulebook house style as authoritative**, including spelling, grammar, tense, punctuation, vocabulary, register, and sentence control.
- [ ] **Step 5: Implement report materialization** for header, classification evidence, fact map, complete grouped errors, six criteria, strengths, top five, study plan, and transparency metadata.
- [ ] **Step 6: Run focused tests and inspect persisted JSON for absence of ungrounded clinical claims.**

### Task 5: Replace unsafe score fallback with calibration-gated scoring

**Files:**
- Create: `backend/src/OetLearner.Api/Services/Writing/WritingCalibrationReleaseService.cs`
- Create: `backend/src/OetLearner.Api/Services/Writing/WritingScoreCalibrationService.cs`
- Modify: `backend/src/OetLearner.Api/Services/Writing/WritingEvaluationPipeline.cs`
- Modify: `backend/src/OetLearner.Api/Services/Writing/WritingCalibrationService.cs`
- Modify: `backend/src/OetLearner.Api/Domain/WritingSubmissionEntities.cs`
- Test: `backend/tests/OetLearner.Api.Tests/Writing/WritingCalibrationReleaseTests.cs`
- Test: `backend/tests/OetLearner.Api.Tests/Writing/WritingEvaluationPipelineTests.cs`

**Interfaces:**
- `Task<WritingCalibrationReleaseDecision> EvaluateReleaseAsync(string modelVersion, string calibrationSetVersion, CancellationToken ct)`.
- `Task<WritingCalibratedScore> CalibrateAsync(WritingFeatureRecord features, CancellationToken ct)`; no `raw * constant` or interpolation path.

- [ ] **Step 1: Add tests** for no release gate, malformed AI response, low confidence, matched content/language pairs, criterion-priority correlation, and duplicate-error score suppression.
- [ ] **Step 2: Run focused tests** and capture current unsafe fallback behavior as the regression baseline.
- [ ] **Step 3: Implement benchmark approval requirements**: two qualified human ratings, tolerance, MAE, band/pass agreement, criterion agreement, hallucination rate, profession/letter/document fairness, and Content+Conciseness correlation stronger than Language.
- [ ] **Step 4: Make every model/version change invalidate candidate numeric output** until a new release is approved.
- [ ] **Step 5: Remove rule-engine-only and arbitrary linear score fallbacks from candidate-facing paths**; persist retry/review status instead.
- [ ] **Step 6: Run focused tests and source-scan for `estimatedScaledScore` fallback or linear conversion in Writing.**

### Task 6: Ground model answers and expose the complete learner report

**Files:**
- Create: `backend/src/OetLearner.Api/Services/Writing/WritingModelAnswerService.cs`
- Modify: `backend/src/OetLearner.Api/Services/Writing/WritingResultFeedbackService.cs`
- Modify: `backend/src/OetLearner.Api/Endpoints/WritingSubmissionEndpoints.cs`
- Modify: `app/writing/submissions/[id]/results/page.tsx`
- Create: `components/domain/writing/writing-assessment-report.tsx`
- Create: `components/domain/writing/writing-ai-score-graph.tsx`
- Test: `backend/tests/OetLearner.Api.Tests/Writing/WritingModelAnswerGroundingTests.cs`
- Test: `app/writing/submissions/[id]/results/page.test.tsx`

**Interfaces:**
- `Task<WritingModelAnswerResult> GenerateAsync(WritingAssessmentReportV11 report, CancellationToken ct)`.
- `WritingAssessmentReportDto` exposes only approved, visibility-gated fields and the exact practice-score label.

- [ ] **Step 1: Add tests** for post-scoring-only generation, case-note traceability, held answer on unmapped sentence, five priorities, full error expansion, score graph label, and unsupported/blocked states.
- [ ] **Step 2: Run focused tests** to establish missing UI/API fields.
- [ ] **Step 3: Implement grounded model-answer prompt and sentence-to-fact verifier**, with separate corrected-letter output and rationale map.
- [ ] **Step 4: Project the v1.1 report into the existing result endpoint**, preserving tutor visibility and appeal behavior.
- [ ] **Step 5: Render the six criteria, content map, errors, strengths, study plan, confidence/range, and brand-differentiated graph without official OET styling.
- [ ] **Step 6: Run focused Vitest and backend tests.**

### Task 7: Add owner governance APIs/admin surfaces and shared authorization audit

**Files:**
- Create: `backend/src/OetLearner.Api/Endpoints/WritingAssessmentGovernanceEndpoints.cs`
- Modify: `backend/src/OetLearner.Api/Endpoints/WritingRouteBuilderExtensions.cs`
- Create: `app/admin/writing/assessment-governance/page.tsx`
- Modify: `app/admin/writing/page.tsx`
- Modify: `app/tutor/writing/calibration/page.tsx`
- Test: `backend/tests/OetLearner.Api.Tests/Writing/WritingAssessmentGovernanceEndpointTests.cs`
- Test: `app/admin/writing/assessment-governance/page.test.tsx`

**Interfaces:**
- Admin APIs create/publish/retire profession packs, letter-type packs, calibration sets/releases, graph approval, confidence presentation, and reviewer authorization.
- Tutor/language-assessor APIs are assignment-scoped and cannot access cross-candidate PII.

- [ ] **Step 1: Add authorization tests** for candidate, assigned tutor, content author, clinical reviewer, language assessor, admin, and ticket-scoped support.
- [ ] **Step 2: Implement governed draft/effective/locked lifecycle** with audit events on every mutation/export/review.
- [ ] **Step 3: Implement admin UI** that visibly reports every release blocker and prevents effective status without required approvals.
- [ ] **Step 4: Verify shared identity/policy names match Listening/Reading/Speaking authorization paths.**
- [ ] **Step 5: Run focused API/UI tests and an endpoint inventory source scan.**

### Task 8: Production delivery and requirement-by-requirement verification

**Files:**
- Modify: `.github/agent-state.local.md`
- Modify: `docs/STATUS/writing-ai-assessment-v1-1-acceptance.md`
- Modify: `.github/workflows/deploy.yml` only if the existing workflow lacks the required migration/release gate wiring.

- [ ] **Step 1: Run one lightweight touched-area check after each implementation slice, then the focused backend/frontend checks required by the repository ladder.**
- [ ] **Step 2: Run the full W-01–W-19 acceptance matrix against the current source and test outputs; mark every item proven, incomplete, or unverified.
- [ ] **Step 3: Stage only explicit Writing v1.1 files, preserving all unrelated dirty files; commit and push `main` per AGENTS.md.**
- [ ] **Step 4: Verify GitHub Actions completed for the pushed SHA, then verify production web/API health and deployed SHA on the VPS without reading or exposing credentials.
- [ ] **Step 5: Exercise safe public/unauthenticated release-gate checks and authenticated checks only if already available through the configured verification path; do not claim protected candidate behavior without a real authorized session.
- [ ] **Step 6: Update the acceptance report with exact evidence and leave the goal active until every required boundary is proved.
