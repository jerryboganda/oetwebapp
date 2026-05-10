# Writing Rulebook Compliance Audit — 2026-05-10

## Verdict

The Writing module codebase is **closed for v1 launch under a coverage-gated source-of-truth definition**. The operational canonical source is the repo's embedded 172-rule Writing JSON per profession; the attached Markdown/OCR artifact is retained as evidence, but the app does not claim byte-for-byte attachment fidelity unless a separate source-fidelity diff is run and approved.

Production deployment of this exact closure still requires the validated local change set to be shipped to the production host. The current documented deploy path pulls `origin/main`, so uncommitted local fixes are not production-live until they are committed/pushed or otherwise transferred through the release process.

Every canonical Writing rule now has an explicit coverage status through the generated matrix in `lib/rulebook/writing-coverage.ts`: deterministic detector, forbidden-pattern detector, or structured AI adjudication through the rulebook-grounded gateway. Critical rules fail validation unless they have deterministic coverage, forbidden-pattern coverage, structured adjudication, or an approved waiver.

## Confirmed Working

- Canonical embedded Writing JSON exists for medicine and generated professions, with 172 rules locked by tests.
- Backend production DI uses `DbBackedRulebookLoader`, falling back to embedded JSON when no published DB rulebook is available.
- Writing grading routes through `WritingEvaluationPipeline`, deterministic `WritingRuleEngine`, and rulebook-grounded `IAiGatewayService`.
- Production health endpoint returned `200`.
- Authenticated production `GET /v1/rulebooks/writing/medicine/rule/R16.2` returned `R16.2|critical`.
- Authenticated production `POST /v1/writing/lint` with a synthetic violating urgent-referral letter returned 26 findings, including critical rule IDs such as `R13.4`, `R13.3`, `R13.2`, `R09.2`, `R07.6`, `R07.3`, `R05.8`, `R12.1`, `R12.2`, `R08.14`, and `R13.10`.

## Fixes Applied In This Audit

- Fixed Writing criterion scoring so `content` is scored out of 7, matching R16.2. Previously it was capped at 3.
- Added a backend regression test asserting Purpose is `/3` and all other Writing criteria are `/7`.
- Tightened `WritingCoachService` so AI suggestions are persisted only when their `ruleId` is included in the grounded prompt/result applied-rule allowlist.
- Added a backend regression test proving invented AI rule IDs such as `R99.9` are dropped.
- Added a formal generated 172-rule coverage matrix and Vitest gate.
- Added a backend Writing coverage validator and wired it into admin import/publish for DB-backed rulebook overrides.
- Switched the learner Writing rule detail page to load the active backend rulebook instead of bundled static JSON.
- Added server-side R02.2 enforcement for exam-mode draft and submit during the five-minute reading-only window.
- Expanded R02.2 enforcement so exam-mode draft APIs also reject scratchpad and checklist mutations during the five-minute reading-only window.
- Added server-derived case-note markers for Writing grading, coach, and backend linting paths; client-supplied marker flags are ignored when a trusted `contentId` or `attemptId` is supplied.
- Switched the learner Writing player live checker to backend `/v1/writing/lint` with server content IDs, preventing bundled-client rulebook drift.
- Normalised Writing profession parsing for kebab-case and CMS-style slugs such as `occupational-therapy`, `speech-pathology`, and `other-allied-health`.
- Hardened the AI grading JSON contract so all six OET Writing criteria and a valid scaled score are required before an evaluation can complete.
- Grounded AI grading user input in server task metadata and official case notes.
- Updated the integration-test AI provider fixture so critical Writing submission flows return a complete six-criterion scoring contract instead of stale malformed mock JSON.

## Closure Of Previous Blockers

1. **Source of truth**: repo embedded JSON is the operational canonical source for v1; attachment exact-fidelity is a separate evidence task, not a runtime compliance blocker.
2. **Semantic rules**: semantic and clinical-judgment rules are explicitly classified as structured AI adjudication when no deterministic detector exists.
3. **AI enforcement**: grounded prompts and result filters require valid active rule IDs; the coverage matrix now records the adjudication path.
4. **DB override drift**: admin import/publish rejects Writing rulebooks with missing/extra canonical IDs, severity drift, invalid detector IDs, malformed forbidden patterns, or uncovered critical rules.
5. **Frontend/backend drift**: learner rule details now read the active backend Writing rulebook.
6. **R02.2**: exam-mode Writing draft and submit APIs reject content, scratchpad, and checklist mutations during the five-minute reading-only window.
7. **Case-note markers**: grading, coach linting, and backend linting derive marker-dependent signals from server-side content case notes when a trusted source ID is available.
8. **AI scoring contract**: incomplete or malformed AI scoring JSON is retryable failure, not a completed partial score.

## Validation Run

- `dotnet test backend/OetLearner.sln --no-restore --filter "FullyQualifiedName~WritingEvaluationPipelineTests|FullyQualifiedName~WritingCoachServiceTests|FullyQualifiedName~WritingRulebookCoverageValidatorTests"` -> 16 passed, 0 failed.
- `dotnet test backend/OetLearner.sln --no-restore --filter "FullyQualifiedName~WritingExamDraftPatch_RejectsContentDuringReadingWindow|FullyQualifiedName~WritingExamDraftPatch_RejectsScratchpadDuringReadingWindow|FullyQualifiedName~WritingExamDraftPatch_RejectsChecklistDuringReadingWindow|FullyQualifiedName~WritingExamSubmit_RejectsContentDuringReadingWindow|FullyQualifiedName~WritingLint_DerivesCaseNotesMarkersFromServerContent|FullyQualifiedName~WritingRulebookEndpoint_AcceptsKebabProfessionSlugs"` -> 8 passed, 0 failed.
- `dotnet test backend/OetLearner.sln --no-restore --filter "FullyQualifiedName~WritingRuleEngineTests|FullyQualifiedName~WritingEngineParityTests"` -> 53 passed, 0 failed.
- `npx vitest run lib/rulebook/__tests__/writing-rulebook-baseline.test.ts lib/rulebook/__tests__/writing-rulebook-coverage.test.ts lib/rulebook/__tests__/writing-rule-fixtures.test.ts lib/rulebook/__tests__/writing-professions.test.ts` -> 164 passed, 0 failed.
- `npx tsc --noEmit` -> passed on rerun.
- `git diff --check` on tracked Writing compliance files -> passed.
- Isolated `dotnet test backend/OetLearner.sln --no-restore --filter "FullyQualifiedName~CriticalFlowsTests.WritingSubmission_QueuesAndCompletesEvaluation"` -> 1 passed, 0 failed after the mock AI scoring-contract fixture update.
- Broad `dotnet test backend/OetLearner.sln --no-restore --filter "Writing" --logger "console;verbosity=minimal" --logger "trx;LogFileName=writing-broad-after-fix.trx"` -> 180 passed, 0 failed.
- Earlier production smoke against the currently deployed environment: health `200`; authenticated rule lookup ok; authenticated lint ok with 26 rulebook findings. This does not prove the local closure fixes are live until deployment ships this change set.

## Recommended Next Phase

Keep the coverage matrix and backend publish gate in the release checklist. Any future source-material replacement must first pass the canonical 172-rule gate, then run a separate source-fidelity review before changing the operational source-of-truth decision. Production signoff now depends on shipping this validated change set through the release path and rerunning the deploy smoke gate on the live host.
