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
| LR-04 | Explicit accepted variant receives credit and is named in audit | `ListeningGradingService` writes `listening.marking.accepted_variant_used` with the matched variant; `ReadingGradingService` consumes only explicitly authored Part A variants when the attempt policy enables them; `AssessmentGovernanceEndpoints` preserves key snapshots | Implemented; focused audit test added, execution pending by request |
| LR-05 | Strikethrough is not a selected MCQ answer | Candidate selection remains a server-validated option key and annotation metadata is separate in `ListeningLearnerService`; `tests/unit/listening/BCQuestionRenderer.test.tsx` and `app/reading/paper/[paperId]/page.test.tsx` assert rule-out leaves the radio answer unchecked | Implemented; focused UI regression passed |
| LR-06 | Reading Part A locks at the authoritative 15-minute deadline | `backend/src/OetLearner.Api/Services/Reading/ReadingAttemptService.cs`, `backend/src/OetLearner.Api/Endpoints/ReadingLearnerEndpoints.cs`, `tests/e2e/reading/part-a-lock.spec.ts` | Implemented; deployed browser verification pending |
| LR-07 | Reading B+C share one authoritative 45-minute timer | `ReadingAttemptService`, `ReadingLearnerEndpoints`, `tests/e2e/reading/part-a-lock.spec.ts` | Implemented; deployed browser verification pending |
| LR-08 | Raw score is reproducible from stored response/key version | `ContentPaperService` assigns bounded Reading/Listening `PublishedRevisionId` values; `ListeningAttempt.LastQuestionVersionMapJson`, `ListeningAnswer.QuestionVersionSnapshot`, `ReadingAttempt.PaperRevisionId`, governed `MarkingPolicyVersionId`/snapshot guards, legacy `Attempt` capture in `LearnerService.CreateAttemptAsync`, attempt-start `AssessmentScoreConversionSnapshot`, migration `20260902090000_AddAssessmentScoreConversionAttemptSnapshots`, and `backend/tests/OetLearner.Api.Tests/Assessment/AssessmentScoreConversionServiceTests.cs` | Implemented fail-closed revision, policy-snapshot, and score-table selection guards; focused assessment governance run passed (11/11), broader revision/deployed verification pending |
| LR-09 | No answer/rationale is visible before final submission | `backend/src/OetLearner.Api/Endpoints/ReadingLearnerEndpoints.cs`, `backend/src/OetLearner.Api/Services/Listening/ListeningLearnerService.cs`, `tests/e2e/listening/listening-answer-key-not-exposed.spec.ts` | Implemented; deployed browser verification pending |
| LR-10 | Result has raw/part/converted/graph/review/disclosure contracts | `components/domain/results/score-conversion-evidence.tsx`, `components/domain/results/score-band-graph.tsx`, `components/domain/results/score-band-graph.test.tsx`, `app/listening/results/[id]/page.tsx`, `app/reading/paper/[paperId]/results/page.tsx`, `app/reading/paper/[paperId]/results/page.test.tsx` | Implemented; graph test passed; deployed responsive verification pending |
| LR-11 | Refresh/reconnect restores answers without extra time | `ReadingAttemptService`, `ListeningLearnerService`, server deadline fields and idempotent submit paths; `app/listening/player/[id]/page.tsx` and `lib/mobile/offline-sync.ts` now encrypt, queue, and server-wins reconcile transiently offline Listening answers | Implemented in source; focused reconnect test pending |
| LR-12 | AI failure cannot delay/change deterministic result | `backend/src/OetLearner.Api/Services/Reading/ReadingExplanationService.cs`, `backend/src/OetLearner.Api/Services/Listening/ListeningExplanationService.cs`, `backend/src/OetLearner.Api/Endpoints/ReadingLearnerEndpoints.cs`, `backend/src/OetLearner.Api/Endpoints/ListeningLearnerEndpoints.cs`, `components/domain/results/grounded-listening-ai-explanation.tsx`, grounded usage gateway; blank/unanswered responses fail closed and AI remains advisory-only | Implemented; deterministic Reading/Listening gateway-failure tests added; short execution pending |
| LR-13 | MCQ publication rejects duplicate options and zero/multiple correct options | `ListeningStructureService`, `ReadingStructureService`, `ContentPaperService`, explicit duplicate/zero/multiple-correct authoring regression tests | Implemented; shared paper publish now hard-blocks invalid Reading/Listening MCQ payloads; focused execution pending |
| LR-14 | Key change uses controlled auditable re-mark | `AssessmentGovernanceEndpoints` validates submitted-attempt ownership, question-revision ownership, and semantic key-snapshot shape before queueing; `ReadingGradingService.RegradeSubmittedAsync`, `ListeningGradingService.RegradeWithKeyAsync`; original/updated result snapshots retained on the job | Implemented; focused re-mark test pending |
| LR-15 | Desktop/mobile timer, passage, and controls do not clip | Responsive result/player layouts and existing mobile/desktop route surfaces; `tests/e2e/responsive/listening-reading-layout.spec.ts` checks the canonical Listening and Reading learner routes for document overflow and clipped elements across desktop/mobile learner projects | Pending dedicated Playwright run |
| LR-16 | Exam technical requirements are guidance only | `ListeningV2Endpoints.TechReadinessRequest` forwards device labels, screen dimensions, display scale, client shell/app version, parsed browser version, and Network Information observations to `ListeningSessionService.RecordTechReadinessAsync`; the service records them without rejecting; `TechReadinessDto.TechnicalRequirementsGuidanceOnly`; `lib/listening/tech-readiness-probe.ts`; candidate guidance in `ListeningIntroCard` and `app/exam-guide`; `ListeningV2AdvanceEndpointTests.Technical_guidance_signals_are_recorded_without_blocking_strict_readiness` | Source, focused frontend contract tests, and focused backend endpoint suite pass (12/12); deployed browser verification and owner style/copy review pending |

## Release gates that cannot be guessed

- Complete approved 0..42 Listening and Reading score-conversion tables.
- Approved normalization/capitalization/spacing policy and practice/mock lock mode.
- Effective rationale/evidence library, pathway thresholds, and pass labels.
- Legal/style approval for the differentiated practice score graph.
- Peak concurrent timed-attempt target and corresponding load evidence.

## Latest implementation slice

- Mock report aggregation now requires persisted Listening/Reading scaled
  score, conversion-table version, and explicit pass decision before emitting
  governed converted values. The versioned payload, client mapper, and
  practice Statement-of-Results adapter carry and enforce the same evidence;
  raw score fallback is no longer accepted for those two subtests. Focused
  adapter execution passed with release-copy directories excluded; backend
  execution remains pending.
- Reading/Listening paper publication now requires source provenance, every
  required asset role, successful validator execution, and zero error-level
  structural findings. Listening transcript evidence and distractor-authoring
  defects remain hard blockers; only non-essential pedagogical metadata stays
  advisory. Focused backend execution remains pending.
- The Reading paper result and legacy Listening mock result surfaces now require
  an explicit persisted score-conversion table key and pass decision before
  rendering a converted score or grade. Legacy Reading mock normalization also
  drops unproven scaled values back to raw-only evidence, and the Listening mock
  contract now carries the conversion decision for the same fail-closed client
  rule. Focused execution remains pending.
- The active legacy/diagnostic Listening player now uses the shared encrypted
  offline answer queue with deterministic per-attempt/question keys, server-wins
  reconciliation, conflict messaging, and reconnect auto-sync. Submission and
  timer state remain online/server-authoritative; focused reconnect execution is
  still pending.
- The same player transport now visibly disables playback and seeking while an
  audio-validity hold is active; a focused component regression covers the
  administrator-review state.
- The admin Listening export audit event now records the audio-review hold
  status, reason, and timestamp alongside the exported attempt evidence; a
  focused endpoint regression covers that audit boundary.
- Controlled re-mark intake now rejects unknown question revisions, attempts
  outside the requested paper, and key snapshots without an explicit answer or
  accepted-variant field before creating an audit job. Focused endpoint and
  execution tests remain pending.
- The learner-facing Listening test-rules disclosure no longer promises credit
  for misspellings or meaning-based plural/article substitutions; it now states
  strict platform marking and explicit-authorised-variant-only credit.
- The anonymous Listening rules policy no longer publishes legacy `30/42` or
  `350/500` constants. It derives pass anchors only from a complete effective
  owner table and otherwise returns an explicit unavailable state; the UI shows
  raw-score guidance without an unsupported scaled pass claim.
- Listening learner, admin, and teacher analytics now consume only persisted
  scaled results carrying an owner conversion-table version and explicit
  `ScoreConversionPassed` value. When no approved result exists, scaled
  aggregates and pass percentages are `null`, and the learner action plan/UI
  state that conversion is unavailable instead of inferring the shared 350
  threshold.
- Listening V2 and legacy Listening/Reading pathway projections now ignore
  scaled values without an owner conversion-table version. Full/exam pathway
  readiness requires the persisted owner `ScoreConversionPassed` flag, and
  pathway milestones use owner-approved conversion evidence rather than a
  formula or numeric pass fallback. Focused pathway execution remains pending.
- Reading/Listening mock completion now requires canonical evidence with an
  owner conversion-table version; mock result adapters, per-module readiness,
  booking advice, and Mock Center projections withhold formula-derived grade
  or pass labels when that metadata is absent. Focused mock execution remains
  pending.
- Reading cohort/paper analytics, privileged tutor review, and admin analytics
  now expose converted Reading scores only when the persisted owner table
  version and pass decision are present; missing metadata is null/unavailable,
  and scaled-score overrides fail closed without owner conversion evidence.
  Focused Reading analytics/tutor execution remains pending.
- Mock admin aggregate readiness and pass-prediction calculations now exclude
  reports containing Reading/Listening modules, so a mock-wide numeric average
  cannot become an unapproved assessment pass claim. Focused admin analytics
  execution remains pending.
- The legacy background mock-report builder is aligned with the same governed
  score gate, withholding Reading/Listening formula grades, mock-wide grade,
  and booking pass advice when owner conversion evidence is absent.
- Reading dashboard copy now labels predicted values as an AI Practice Score
  with the non-official-result disclosure; mock client readiness and color
  helpers exclude Reading/Listening-bearing reports from mock-wide pass logic
  and use persisted owner grades when available.
- Legacy learner dashboard evidence, progress/comparison trends, submissions,
  weak-area actions, and interleaved-practice prioritization now suppress
  Reading/Listening score ranges unless the evaluation has owner conversion
  metadata and an explicit pass decision. Focused legacy-surface execution
  remains pending.
- The score-estimator service now refuses Reading/Listening prediction inputs
  without persisted owner-approved scaled scores, refuses mixed conversion-table
  versions, and hides legacy snapshots without conversion provenance. The
  estimator UI carries the persistent `AI Practice Score — not an official OET
  result.` disclosure. Focused browser/API execution remains pending.
- Assessment score-conversion governance now has focused compiled-assembly
  evidence: `dotnet vstest bin\\Debug\\net10.0\\OetLearner.Api.Tests.dll
  --TestCaseFilter:"FullyQualifiedName~AssessmentScoreConversionServiceTests"`
  passed all 11 tests, covering exact 0..42 validation, no interpolation,
  attempt table pinning, fail-closed missing configuration, and lock-on-use
  behavior for tables and marking policies. Broader revision-path and deployed
  acceptance remain pending.
- Listening V2 readiness and navigation now have focused compiled-assembly
  evidence: `dotnet vstest bin\\Debug\\net10.0\\OetLearner.Api.Tests.dll
  --TestCaseFilter:"FullyQualifiedName~ListeningV2AdvanceEndpointTests"`
  passed all 12 tests, including strict readiness gating, confirm-token
  transitions, expired readiness rejection, and recording advisory device,
  browser, network, display, and audio-output signals without blocking on
  those advisory values. Deployed browser verification remains pending.
- Lightweight frontend contracts also pass: `pnpm exec vitest run
  tests/unit/listening/audio-integrity.test.ts
  components/domain/results/score-band-graph.test.tsx
  lib/mobile/offline-answer-reconciliation.test.ts --reporter=dot`
  passed 5 files and 14 tests. This covers client-side seek/replay blocking,
  branded practice-score graph rendering, and encrypted-queue reconciliation
  decision behavior; full browser reconnect and deployed acceptance remain
  separate gates.
- Listening and Reading pathway `bestScaledScore` values now also require an
  explicit persisted conversion decision, preventing pathway branching or
  milestone display from relying on table-key-only metadata. Focused pathway
  execution remains pending.
- Shared learner-facing Listening result and transcript-review projections now
  suppress scaled score, grade, pass, conversion-key, and score-display claims
  unless scaled score, owner table key, and explicit decision are all present.
  Focused Listening result/review execution remains pending.
- Reading grading persistence and learner attempt/result projections now apply
  the same three-field conversion gate, including idempotent existing-result
  responses and submit responses. Focused Reading result/review execution
  remains pending.
- Reading grading now writes `reading.marking.accepted_variant_used` with the
  matched explicit variant and attempt/question/policy provenance whenever that
  variant earns credit; the focused regression fixture covers both accepted and
  non-accepted answers.
- Remaining legacy Listening submit, expert re-mark, generic objective submit,
  mock-result, background-report, analytics-export, and client result paths now
  apply the same explicit conversion-decision gate and clear stale scaled values
  when conversion evidence is unavailable. Focused execution remains pending.
- Listening media `audio_error` now durably sets `RequiresAdminReview`,
  `AdminReviewReason`, and `AdminReviewFlaggedAt` on both relational and
  legacy attempts; the admin export includes the hold fields, server mutation
  and submit paths fail closed, and the player halts without automatic replay.
  Focused execution and authenticated deployed browser verification remain
  pending.
- `ddebd85be` adds encrypted offline autosave reconciliation with server-wins conflict handling; submission and timer state are never queued offline.
- `abe859e2f` adds grounded post-submit Listening and Reading explanations sourced from stored answers and authored rationale/transcript evidence, with usage attribution and advisory-only UI contracts.
- The Listening admin authoring flow now includes an answer-key-free
  `preview-structure` projection plus a separate marking preview console for
  the Section 12 candidate/marking review gate; focused UI/API execution is
  still pending.
- Listening authoring now validates processed uploaded-audio duration and
  authored per-section timing (including legacy JSON extracts), and the shared
  publish path hard-blocks missing audio, duration, cue-window, and section
  timing defects. Focused execution remains pending.
- Typed-answer authoring now exposes a least-privilege accepted-variant audit
  projection (actor, timestamp, reason) for Reading and Listening; Listening
  Part A bulk edits also require and display the reason before saving. Focused
  execution and deployed admin verification remain pending.

## Latest computer-only delivery checkpoint - 2026-08-12

- The canonical Listening paper-route sub-section audio now enforces the
  strict one-play contract at the media-event layer as well as by hiding
  native controls: unauthorized pause resumes, replay is halted, every
  non-programmatic seek snaps to the last known playhead, and playback-rate
  changes are reset to normal speed. The focused audio-integrity regression
  passed 9 tests; deployed browser acceptance remains separate.
- Reading paper presentation is now fail-closed at the policy service and
  database default. Existing persisted enablement is reset by the migration;
  the admin control and learner preview now report computer-based delivery
  only, while the legacy contract field remains false for compatibility.
- Listening paper simulation is rejected at the learner mode boundary,
  historical paper attempts are not resumed through the learner session or
  FSM policy paths, the paper pathway launch target is removed, and learner
  briefing/pathway copy no longer advertises paper mode.
- Bounded validation passed: `pnpm exec vitest run
  "app/reading/paper/[paperId]/page.test.tsx" --reporter=dot` (3 files,
  30 tests), plus `git diff --check`. Local backend compilation, deployment,
  and authenticated desktop/mobile acceptance remain pending.
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
- Listening readiness telemetry now also records a bounded client shell/device
  type, native app version when available, parsed browser name/version, and
  Network Information API quality observations (effective type, downlink, RTT,
  and save-data). Raw user-agent strings are not sent, and none of these
  guidance observations can block the audio-only strict launch gate. Focused
  frontend probe/API contract execution passed: 4 files, 26 tests.
- Legacy learner, mock, analytics, tutor, expert, and background LR surfaces
  now expose raw-only evidence when no owner-approved conversion row exists;
  no raw-to-scaled formula fallback remains in the audited LR paths.

## Final exact current checkpoint

- Commit `428f9b27583d5ea336d92710cc6e4e7a56932ec5` is on `main` and
  `origin/main`. The gap-closure workflow filter now includes the Listening
  Part A spelling, Listening grading, scoring-path, learner-leak, Reading
  grading, score-conversion, and Reading authoring regression classes.
- Build & Deploy run `31516273755` completed successfully for that exact SHA:
  web/API/backup images, off-box migration SQL generation and application, and
  blue/green deployment all passed. The VPS reported
  `AUTO_DEPLOY_DONE: live on green (previous slot blue kept for rollback)`;
  target-slot router and public web/API verification also passed.
- Independent checks at `2026-08-11T17:22:41Z` returned HTTP 200 for API live,
  API readiness, and web health; the app root, `/listening`, and `/reading`
  returned HTTP 307 to sign-in. Readiness at `2026-08-11T17:22:46Z` reported
  database, migrations, stuck_jobs, and storage all `ok`.
- Widened clean-runner gap-closure run `31516282588` passed frontend
  type-check, Vitest, and lint. Its backend scope compiled and ran 293 tests:
  211 passed and 82 failed. The failures are dominated by tests that start
  Reading/Listening attempts without an owner-effective marking policy, plus
  legacy test expectations for unconfigured score conversion/default
  normalization and one technical-guidance fixture. No owner score table,
  normalization profile, or policy was invented to force those tests green.
- The current implementation is therefore deployed and fail-closed, but the
  PDF acceptance is not fully closed until owner-controlled release inputs and
  authenticated desktop/mobile acceptance evidence are supplied.

## Documentation-only follow-up deployment

- Documentation commit `c88efccf33fccdac8ec9a76d4c06fe9e8579d8e7` is on
  `main` and `origin/main`. Build & Deploy run `31517340245` completed
  successfully for that exact SHA; image builds, off-box migration
  generation/application, and blue/green deployment passed. The VPS reported
  `AUTO_DEPLOY_DONE: live on blue (previous slot green kept for rollback)`.
- Independent checks at `2026-08-11T17:36:07Z` returned HTTP 200 for API live,
  API readiness, and web health; `/`, `/listening`, and `/reading` returned
  HTTP 307 to sign-in. Readiness at `2026-08-11T17:36:11Z` reported database,
  migrations, stuck_jobs, and storage all `ok`.

## Final evidence-correction deployment

- Evidence correction commit `b0705877bfa03afca0e03ae39134fd3b50cfcbd9`
  is on `main` and `origin/main`. Build & Deploy run `31518463827`
  completed successfully; image builds, off-box migration
  generation/application, and blue/green deployment passed. The VPS reported
  `AUTO_DEPLOY_DONE: live on green (previous slot blue kept for rollback)`.
- Independent checks at `2026-08-11T17:46:46Z` returned HTTP 200 for API live,
  API readiness, and web health; `/`, `/listening`, and `/reading` returned
  HTTP 307 to sign-in. Readiness at `2026-08-11T17:46:50Z` reported database,
  migrations, stuck_jobs, and storage all `ok`.

## Latest endpoint contract fix

- The Listening v2 technical-readiness endpoint previously deserialized only
  `audioOk` and `durationMs`, silently dropping the already-supported advisory
  device, resolution, and display-scale fields. The endpoint now forwards all
  seven request fields into the existing service command; the strict launch
  gate remains audio-only.
- `git diff --check` passed. The focused Windows
  `ListeningV2AdvanceEndpointTests` run timed out after 124 seconds before
  producing compiler/test output; hosted CI is required for execution evidence.

## Hosted LR-16 regression evidence

- Gap-closure run `31522316211` executed against exact SHA `8707607e7`.
  Frontend type-check, Vitest, and lint all passed. The backend scope compiled
  and ran 293 tests: 212 passed and 81 failed.
- All `ListeningV2AdvanceEndpointTests` passed, including
  `Technical_guidance_signals_are_recorded_without_blocking_strict_readiness`.
  The remaining backend failures are the known owner-approved marking-policy
  gate and legacy expectations for unavailable score conversion/default
  normalization; no owner values were invented to mask them.

- Listening authored questions now carry an explicit `validationStatus` and
  optional validation note in the admin JSON and relational structures. Both
  JSON and relational publish validation fail closed unless every question is
  marked `published`; promotion to `published` is restricted to content-publish
  or publisher-approval permissions. Canonical publish-ready fixtures now set
  the explicit status, and a missing-status regression is present; focused
  execution and deployed admin verification remain pending.

## Computer-only learner-surface closure

- The learner Listening client no longer exposes or renders the legacy paper
  booklet/all-parts review surface. Paper query and delivery values fail closed
  to the computer exam surface; the API client mode union and presentation
  styles no longer advertise paper delivery.
- The obsolete standalone Listening paper-simulation component, helper, and
  dedicated tests were removed after confirming no production imports remain.
- Direct Listening documentation and the rulebook citation map now describe
  supported computer-based modes only. Focused Windows evidence for this slice:
  `ListeningPlayerSkinShell.test.tsx` and `lib/listening-api.test.ts` passed
  (15 tests), and `cbla-fidelity.test.tsx` passed (20 tests). The untracked,
  user-owned `pdf-policy-release*` copies were excluded from these checks and
  were not modified.
- The retained legacy `?mode=paper` browser contract now asserts fail-closed
  normalization to `mode=exam`; it no longer treats paper free navigation as a
  supported delivery mode.

## Assessment-governance role boundary

- Score tables, marking-policy versions, approved rationales, and controlled
  re-mark jobs are isolated behind dedicated assessment-governance read, write,
  approve, and execute permissions. Content-author permissions alone do not
  grant access; `system_admin` is the only break-glass override.
- The admin scoring-system route uses the dedicated governance-read permission,
  matching the backend `/v1/admin/assessment-governance` policy boundary.
- Candidate-result Reading tutor/admin routes now use dedicated
  `assessment:results_read`/`assessment:results_write` permissions instead of
  generic content-write access. Content-author permissions cannot open
  non-redacted attempt review, feedback, score overrides, or assignments;
  expert routes retain assigned-candidate checks.
- Effective marking-policy documents now fail closed when any owner-controlled
  normalization, answer-form, audio-lock, replay, or technical-guidance field
  is omitted; no omitted field inherits a runtime default. A focused regression
  test covers incomplete policy JSON; backend execution remains bounded/pending.
- Score-table validation now also requires an explicit grade band and pass
  decision for every raw score row, so a converted number alone cannot become
  a governed result. The admin editor rejects incomplete rows before submit.
- The admin marking-policy editor no longer pre-populates owner-controlled
  normalization, replay, lock, or technical-guidance values. A policy draft
  now requires explicit owner-supplied JSON, and the backend remains the
  fail-closed completeness gate before approval.
- Added a dedicated responsive learner-surface Playwright check covering the
  canonical Listening player and Reading paper routes on desktop and mobile
  learner projects. It asserts route health and rejects document or element
  overflow beyond the viewport; execution remains pending by request.
- The canonical Reading results page now carries the same persistent
  stricter-than-examiner spelling disclosure as Listening, with a focused page
  regression assertion.

## Customer-support ticket boundary

- `CustomerSupportCase` and migration
  `20260904090000_AddCustomerSupportCases` provide a local ticket-linked grant
  with a mandatory candidate ID, expiry, open/closed lifecycle, and immutable
  ticket/candidate uniqueness boundary.
- `CustomerSupportCaseService` exposes only ticket-scoped case listing and a
  minimal candidate contact projection while the case is open and unexpired;
  closed or expired grants fail closed and never fall back to candidate search.
- `AdminCustomerSupportRead`/`AdminCustomerSupportWrite` and the built-in
  `customer_support` role isolate support access from content, learner-wide,
  and assessment-governance permissions.
- Case creation, candidate projection reads, and closure emit
  `support.case.created`, `support.case.candidate_read`, and
  `support.case.closed` audit events without assessment content. Focused
  service execution and deployed cross-role acceptance remain pending by the
  owner's bounded-validation instruction.
- Candidate, tutor/expert, content, assessment-governance, and customer-support
  boundaries represented by the repository are now enforced in their API paths;
  owner-provided release inputs and authenticated production acceptance remain
  separate unresolved gates.

## Per-section Listening audio start gate

- The strict server start gate now accepts a paper when it has either a
  combined scored audio asset or one or more non-empty per-section scored
  audio assets. A per-section-only regression fixture was added so valid
  `audioUrlByPart` papers are not rejected as missing audio. Backend execution
  of this regression remains pending by explicit request.

## Canonical Listening part-mark validation

- Publish validation now enforces both total raw marks of 42 and the canonical
  Listening part-mark breakdown A=24, B=6, C=12 for relational and legacy JSON
  papers. A paper cannot redistribute marks while retaining a total of 42.
- Focused relational and JSON regression fixtures cover this gate; backend
  execution remains pending by explicit user request.

## Canonical Reading part and section marks

- Reading publish validation now checks persisted part max-raw metadata against
  A=20, B=6, and C=16, and checks the canonical B1..B6/C1..C2 section rows
  against their one-mark scores. A paper cannot retain 42 question points while
  carrying inconsistent results-calculation metadata.
- Focused part and section mutation fixtures cover these gates; backend
  execution remains pending by explicit user request.

## Fail-closed unknown-question grading

- Reading grading no longer honors the legacy `grade_as_correct` fallback for
  an unknown or corrupt question type. Such a question always receives zero
  credit, preserving deterministic strict marking even if an old policy
  snapshot contains the permissive value.
- A regression test covers the permissive legacy snapshot path. The backend
  test was not executed in this bounded pass by explicit user request; the
  source change and focused test fixture are present for the owner's check.

## Listening scored-audio preflight

- Strict Listening readiness now verifies every distinct scored audio URL in
  the session before the first server-authoritative timer transition. Relative
  and `/v1/` media URLs are fetched through the authorized blob path; each
  asset must reach `canplaythrough`, and a failed asset keeps readiness blocked.
  The existing audible sound probe remains required as the candidate-facing
  output check.
- Section playback still loads its active source after the strict transition;
  later playback errors remain fail-stop and are surfaced for administrator
  review. Full browser execution was not run in this bounded pass.
