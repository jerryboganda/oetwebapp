# OET Listening and Reading v1.1 acceptance evidence

Source: `C:\Users\Dr Faisal Maqsood PC\Downloads\OET_Listening_and_Reading_AI_System_Specification_v1.1.pdf`.

This matrix is deliberately evidence-led. `Implemented` means the source path
contains the guard or UI contract; `Pending verification` means the check still
needs to run against the new release SHA. `Owner gate` means the PDF requires
an owner-controlled value that must not be invented in code.

| ID | Acceptance statement | Implementation/evidence | Status |
| --- | --- | --- | --- |
| LR-01 | Listening audio cannot pause, replay, or restart on refresh | `backend/src/OetLearner.Api/Endpoints/ListeningAudioEndpoints.cs`, `components/domain/listening/player/ListeningAudioTransport.tsx`, `tests/unit/listening/audio-integrity.test.ts`, `tests/e2e/listening/exam-mode-locks.spec.ts` | Implemented; deployed browser verification pending |
| LR-02 | Section boundary confirmation is irreversible and locks prior answers | `backend/src/OetLearner.Api/Endpoints/ListeningV2Endpoints.cs`, `backend/tests/OetLearner.Api.Tests/Listening/ListeningV2AdvanceEndpointTests.cs`, `tests/e2e/listening/exam-mode-locks.spec.ts` | Implemented; deployed browser verification pending |
| LR-03 | Misspelled Part A answer receives zero with no fuzzy/AI override | `backend/src/OetLearner.Api/Services/Listening/ListeningGradingService.cs`, `backend/src/OetLearner.Api/Services/Reading/ReadingGradingService.cs`, `backend/tests/OetLearner.Api.Tests/Listening/ListeningPartASpellingTests.cs`, `backend/tests/OetLearner.Api.Tests/Reading/ReadingGradingServiceV11Tests.cs`; deterministic `IsCorrect` remains authoritative over AI metadata | Implemented; focused backend run pending |
| LR-04 | Explicit accepted variant receives credit and is named in audit | `ListeningGradingService` writes `listening.marking.accepted_variant_used` with the matched variant; `ReadingGradingService` consumes only explicitly authored Part A variants when the attempt policy enables them; `AssessmentGovernanceEndpoints` preserves key snapshots | Implemented; focused audit test pending |
| LR-05 | Strikethrough is not a selected MCQ answer | Candidate selection remains a server-validated option key and annotation metadata is separate in `ListeningLearnerService`; `tests/unit/listening/BCQuestionRenderer.test.tsx` and `app/reading/paper/[paperId]/page.test.tsx` assert rule-out leaves the radio answer unchecked | Implemented; focused UI regression passed |
| LR-06 | Reading Part A locks at the authoritative 15-minute deadline | `backend/src/OetLearner.Api/Services/Reading/ReadingAttemptService.cs`, `backend/src/OetLearner.Api/Endpoints/ReadingLearnerEndpoints.cs`, `tests/e2e/reading/part-a-lock.spec.ts` | Implemented; deployed browser verification pending |
| LR-07 | Reading B+C share one authoritative 45-minute timer | `ReadingAttemptService`, `ReadingLearnerEndpoints`, `tests/e2e/reading/part-a-lock.spec.ts` | Implemented; deployed browser verification pending |
| LR-08 | Raw score is reproducible from stored response/key version | `ListeningAttempt.LastQuestionVersionMapJson`, `ListeningAnswer.QuestionVersionSnapshot`, `ReadingAttempt.PaperRevisionId`, governed `MarkingPolicyVersionId`/snapshot guards, legacy `Attempt` capture in `LearnerService.CreateAttemptAsync`, attempt-start `AssessmentScoreConversionSnapshot`, migration `20260902090000_AddAssessmentScoreConversionAttemptSnapshots`, and `backend/tests/OetLearner.Api.Tests/Assessment/AssessmentScoreConversionServiceTests.cs` | Implemented fail-closed revision, policy-snapshot, and score-table selection guards; focused backend run pending |
| LR-09 | No answer/rationale is visible before final submission | `backend/src/OetLearner.Api/Endpoints/ReadingLearnerEndpoints.cs`, `backend/src/OetLearner.Api/Services/Listening/ListeningLearnerService.cs`, `tests/e2e/listening/listening-answer-key-not-exposed.spec.ts` | Implemented; deployed browser verification pending |
| LR-10 | Result has raw/part/converted/graph/review/disclosure contracts | `components/domain/results/score-conversion-evidence.tsx`, `components/domain/results/score-band-graph.tsx`, `components/domain/results/score-band-graph.test.tsx`, `app/listening/results/[id]/page.tsx`, `app/reading/paper/[paperId]/results/page.tsx`, `app/reading/paper/[paperId]/results/page.test.tsx` | Implemented; graph test passed; deployed responsive verification pending |
| LR-11 | Refresh/reconnect restores answers without extra time | `ReadingAttemptService`, `ListeningLearnerService`, server deadline fields and idempotent submit paths | Implemented in source; focused reconnect test pending |
| LR-12 | AI failure cannot delay/change deterministic result | `backend/src/OetLearner.Api/Services/Reading/ReadingExplanationService.cs`, `backend/src/OetLearner.Api/Services/Listening/ListeningExplanationService.cs`, `backend/src/OetLearner.Api/Endpoints/ReadingLearnerEndpoints.cs`, `backend/src/OetLearner.Api/Endpoints/ListeningLearnerEndpoints.cs`, `components/domain/results/grounded-listening-ai-explanation.tsx`, grounded usage gateway; blank/unanswered responses fail closed and AI remains advisory-only | Implemented; focused AI failure test pending |
| LR-13 | MCQ publication rejects zero/multiple correct options | `ListeningStructureService`, `ReadingStructureService`, existing authoring validation tests | Implemented; focused release test pending |
| LR-14 | Key change uses controlled auditable re-mark | `AssessmentGovernanceEndpoints`, `ReadingGradingService.RegradeSubmittedAsync`, `ListeningGradingService.RegradeWithKeyAsync`; original/updated result snapshots retained on the job | Implemented; focused re-mark test pending |
| LR-15 | Desktop/mobile timer, passage, and controls do not clip | Responsive result/player layouts and existing mobile/desktop route surfaces | Pending dedicated Playwright run |
| LR-16 | Exam technical requirements are guidance only | `ListeningSessionService.RecordTechReadinessAsync` records Bluetooth, resolution, and scale signals without rejecting; `TechReadinessDto.TechnicalRequirementsGuidanceOnly`; candidate guidance in `ListeningIntroCard` and `app/exam-guide`; `ListeningV2AdvanceEndpointTests.Technical_guidance_signals_are_recorded_without_blocking_strict_readiness` | Implemented; focused backend run stalled locally; owner style/copy review pending |

## Release gates that cannot be guessed

- Complete approved 0..42 Listening and Reading score-conversion tables.
- Approved normalization/capitalization/spacing policy and practice/mock lock mode.
- Effective rationale/evidence library, pathway thresholds, and pass labels.
- Legal/style approval for the differentiated practice score graph.
- Peak concurrent timed-attempt target and corresponding load evidence.

## Latest implementation slice

- `ddebd85be` adds encrypted offline autosave reconciliation with server-wins conflict handling; submission and timer state are never queued offline.
- `abe859e2f` adds grounded post-submit Listening and Reading explanations sourced from stored answers and authored rationale/transcript evidence, with usage attribution and advisory-only UI contracts.
- `0a5867199` adds the branded practice score-band graph with approved-table-only conversion, raw-only fallback, the 350 reference marker, and the persistent non-official-result disclosure.
- `4bdffbc97` normalizes the score-graph source file ending.
- `831da6795` rejects blank/unanswered stored responses before grounded AI explanation generation.
- `d992d198e` and `20d19043c` repair the pre-existing Speaking baseline compilation blockers without staging the user’s remaining Speaking work; the hosted API publish gate now passes.

## Deployment evidence

- Commit `c4ed2e55b9b47aeedccb601b16c0b4580630ad48` is on `main` and
  `origin/main`.
- Actions run `31487883204` completed successfully for that exact SHA,
  including web/API/backup image builds, off-box migration SQL generation and
  production application, and blue/green deployment. The VPS reported
  `AUTO_DEPLOY_DONE: live on blue` with the previous green slot retained for
  rollback.
- Post-deploy checks at `2026-08-11T11:52:26Z` returned HTTP 200 for
  `https://api.oetwithdrhesham.co.uk/health/live` and `/health/ready`.
  Readiness reported database, migrations, stuck jobs, and storage all `ok`.
  The app root, `/listening`, and `/reading` returned HTTP 307 redirects to
  their sign-in routes on `app.oetwithdrhesham.co.uk`.

- Commit `7c677c486044be9dc9955d1183e5aeb5cfcfec08` is on `main` and
  `origin/main`.
- Actions run `31453183628` completed successfully for that exact SHA,
  including API/web/backup images, production migration, and blue/green
  deployment.
- Post-deploy public checks returned HTTP 200 for API live/readiness. API
  readiness reported database, migrations, stuck jobs, and storage all `ok`.
  `https://app.oetwithdrhesham.co.uk/`, `/listening`, and `/reading` returned
  HTTP 307 redirects to the sign-in route with the requested `next` path.

## Current release attempt

- `20d19043cf369c707b6766f001383561a1535eb7` is on `main` and `origin/main`.
- Actions run `31509360292` completed successfully for the exact SHA, including
  web/API/backup image builds, off-box migration SQL generation and production
  application, and blue/green deployment. The VPS reported
  `AUTO_DEPLOY_DONE: live on green (previous slot blue kept for rollback)`.
- The deploy log verified target-slot API and web health, router health, and
  public API/web verification. Independent checks at `2026-08-11T16:02:07Z`
  returned HTTP 200 for `/health/live` and `/health/ready`; readiness reported
  database, migrations, stuck jobs, and storage all `ok`. The app root and
  `/listening` returned HTTP 307 to sign-in with the expected `next` paths.

## Latest conformance hardening

## Latest release attempt

- Commit `428eda72cff3980739f0e904ad09a1fc69f2414d` is on `main` and
  `origin/main`. It exposes deterministic score-conversion error evidence on
  Listening score overrides, preserves the Listening V2 answer-request
  contract, and keeps raw-only mock results type-safe.
- Build & Deploy run `31514162141` completed successfully after one transient
  web-build rerun. The exact SHA passed API/web/backup images, off-box
  migration SQL generation/application, and blue/green deployment. The VPS
  reported `AUTO_DEPLOY_DONE: live on blue` with green retained for rollback.
- Independent checks at `2026-08-11T17:09:55Z` returned HTTP 200 for API live,
  API readiness, and web health. Readiness reported database, migrations,
  stuck_jobs, and storage all `ok`; `/`, `/listening`, and `/reading` returned
  HTTP 307 to sign-in with their requested `next` paths.
- Clean-runner gap-closure run `31514179563` passed the complete frontend
  type-check, Vitest, and lint ladder. Its backend scope compiled and passed
  110 tests; 21 tests failed closed at the missing owner-approved marking
  policy gate, so no owner score table or policy was invented to make them
  green. The filter now includes the LR spelling, explicit-variant, score
  snapshot, leak, Reading grading, and authoring classes for the next run.
- Speaking CI run `31514162202` passed its full frontend job; its backend job
  had 363 passes and one unrelated owner-gate failure.

- Policy versions are locked only after a Listening/Reading attempt is
  durably created; failed starts no longer consume an owner policy version.
- Governed graders reject missing or malformed marking-policy snapshots rather
  than resolving mutable current settings.
- Reading Part A grading now accepts only the canonical key or explicitly
  authored variants when the attempt's immutable policy enables them; inferred
  synonyms and fuzzy rescue remain unavailable.
- Full Listening/Reading and legacy Listening attempts now capture the score
  conversion table selection (including a raw-only unavailable reason) at
  attempt start, so a later table cannot change an in-flight result.
- Real-exam device requirements are now explicitly advisory for Listening:
  the audio sound check remains a strict preflight, while Bluetooth/wireless
  labels, resolution, display scale, VPN, and similar signals are recorded for
  guidance/audit and cannot block an AI practice launch.
- Legacy learner, mock, analytics, tutor, expert, and background LR surfaces
  now expose raw-only evidence when no owner-approved conversion row exists;
  no raw-to-scaled formula fallback remains in the audited LR paths.
