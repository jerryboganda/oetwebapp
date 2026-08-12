# OET Listening and Reading v1.1 acceptance evidence

# Latest LR coding checkpoint - 2026-08-12 (Mock conversion projection hard lock)

- Reading and Listening mock-section adapters now require the authoritative
  attempt max score to be exactly 42 before forwarding scaled/table/pass
  evidence into mock reports. Added a subset Reading resolver regression for
  stale conversion metadata; focused mock resolver command exited 0 with
  silent runner output.
- No long validation, audit, CI/CD, push, deployment, or live acceptance was
  run.

# Latest LR coding checkpoint - 2026-08-12 (Listening conversion projection hard lock)

- Listening analytics, expert review, pathway, and pathway-progress
  projections now require the canonical 42-item maximum before treating
  persisted scaled/table/pass metadata as approved conversion evidence.
- Focused Listening analytics/expert backend command exited 0 with silent
  runner output. No long validation, audit, CI/CD, push, deployment, or live
  acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Listening subset conversion server hard lock)

- Listening grading and learner score reconstruction now require the
  canonical 42-item maximum before exposing owner-table scaled scores. The
  guard covers relational attempts, persisted evaluations, and legacy JSON
  score rows; subset/stale metadata remains raw-only in learner projections.
- Focused Reading subset and Listening no-table grading regressions exited 0
  with silent runner output. No long validation, audit, CI/CD, push,
  deployment, or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Reading subset conversion server hard lock)

- Reading grading now refuses owner-table scaled conversion for Drill,
  MiniTest, and ErrorBank attempts even when an effective table exists, and
  clears conversion table metadata for those raw-only results without marking
  the owner table as used.
- Reading learner endpoint, analytics, tutor, and pathway projections now
  require the canonical 42-item max before exposing converted score evidence.
  Added an effective-table regression to prove subset mode—not table absence—
  keeps conversion unavailable. Focused backend test exited 0 with silent
  runner output; scoped `git diff --check` passed. No long validation, audit,
  CI/CD, push, deployment, or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Listening/Reading subset-result conversion hard lock)

- Listening results/transcript review and Reading paper results now require
  the complete 42-item paper before displaying owner-table scaled score, grade,
  or pass evidence. Subset practice attempts remain raw-only even if
  conversion fields leak into an API payload; malformed conversion fields fail
  closed.
- Added `hasApprovedListeningConversion` coverage and strengthened the Reading
  subset regression. Focused Vitest passed 10/10 with nested worktree copies
  excluded; targeted ESLint passed with 0 errors and 4 existing warnings. No
  long validation, audit, CI/CD, push, deployment, or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Canonical Listening paper stop telemetry parity)

- The strict `/listening/paper/[paperId]` route now emits the shared
  `audio_stopped` event for normal pauses and programmatic cue-boundary stops,
  matching the newer `/listening/player/[id]` route and preserving the
  existing `audio_ended` event.
- No long validation, audit, CI/CD, push, deployment, or live acceptance was
  run for this slice.

Source: `C:\Users\Dr Faisal Maqsood PC\Downloads\OET_Listening_and_Reading_AI_System_Specification_v1.1.pdf`.

# Latest LR coding checkpoint - 2026-08-12 (Reading Exam Part-A hard-lock enforcement)

- Reading Exam answer persistence now hard-locks Part A at the server-owned
  deadline regardless of the configurable practice `PartATimerStrictness`
  value. This removes a client/server bypass where `soft_warn` or `disabled`
  could keep accepting scored Exam answers after the 15-minute boundary.
- Added `ReadingAuthoringTests.Exam_part_a_lock_cannot_be_relaxed_by_policy_strictness`.
  Focused `dotnet test` exited 0 with silent runner output. No full validation,
  audit, CI/CD, push, deployment, or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Listening mode-copy parity)

- Candidate-facing Listening intro guidance now follows the server mode policy:
  strict one-play modes describe irreversible forward-only audio, while
  Practice Mode describes policy-controlled pause/scrub/replay and review
  navigation instead of claiming exam locks.
- Focused player component suite passed 41/41 across 3 files; touched ESLint
  reported no errors or warnings; scoped `git diff --check` passed. No full
  validation, CI/CD, push, deployment, or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Player preflight persistence parity)

- The `/listening/player/[id]` strict start flow now persists the passed
  pathway audio-check outcome before requesting attempt creation. This aligns
  the visible player sound check with the server-authoritative
  `AudioCheckPassedAt` gate and matches the canonical Listening paper flow.
- Single-file ESLint completed with 0 errors and 16 existing warnings; source
  assertions and scoped `git diff --check` passed. No full validation, CI/CD,
  push, deployment, or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Legacy Listening start-gate parity)

- The legacy JSON-backed Listening start path now enforces the same server-side
  strict sound-check and complete scored-audio gates as relational papers.
  Added a JSON-authored regression fixture for direct exam-start bypasses.
- Focused `ListeningAudioCheckGateTests` command exited 0; scoped `git diff
  --check` passed. No full validation, CI/CD, push, deployment, or live
  acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Listening playback-stop telemetry)

- Added the missing PDF-required `audio_stopped` event to the shared client /
  server integrity-event contract. The player records a stop timestamp and
  whether the pause was programmatic or a normal pause, while preserving the
  existing blocked-pause resume protocol. The exact touched audio-resume test
  passed 1/1. The broader file still has one unrelated Part-B auto-submit
  failure, and duplicate `pdf-policy-release*` copies fail import resolution;
  targeted ESLint completed with 0 errors and 16 existing warnings. Focused
  source assertions and scoped diff checks passed. No full validation, CI/CD,
  push, deployment, or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Listening sound-check volume)

- Added the PDF-required candidate-facing sound-check volume control before
  scored content. The selected volume is applied only to the short probe or
  generated tone; scored-audio integrity verification and fail-closed startup
  behavior remain unchanged. The focused `TechReadinessCheck` test passed 1/1,
  targeted ESLint completed cleanly, and focused source assertions plus scoped
  diff checks passed. No full validation, CI/CD, push, deployment, or live
  acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Listening audio integrity fail-closed)

- Closed a concrete P0 deviation in the scored-audio readiness gate. A failed
  scored-audio integrity check now exposes Retry only; the prior
  `Continue anyway` bypass and unused skip prop were removed, so the attempt
  cannot proceed after an integrity failure. Added
  `components/domain/listening/TechReadinessCheck.test.tsx`; the focused test
  passed 1/1. Targeted ESLint completed cleanly, and focused source assertions
  plus scoped diff checks passed. No full validation, CI/CD, push, deployment,
  or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Listening preview disclosure regression)

- The focused Listening preview regression now proves that candidate mode hides
  transcript evidence, distractor explanations/categories, and speaker attitude,
  while protected marking mode renders those authored fields. The bounded Vitest
  command again produced no output and was stopped; targeted ESLint completed
  with 0 errors and the same 2 existing setState-in-effect warnings. Focused
  source assertions and scoped diff checks passed. No full validation, CI/CD,
  push, deployment, or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Listening playback-speed hard lock)

- Scored Listening audio now forces 1x playback at metadata load and resets
  browser, OS, or programmatic playback-rate changes immediately. Blocked rate
  changes are recorded as `audio_speed_change_blocked` telemetry through the
  always-on attempt-event stream. The root-only focused Vitest run (stale
  `pdf-policy-release/**` copies excluded) passed the new speed-lock regression
  and 9/10 tests; the existing Part B end-cue test still times out waiting for
  `mockSubmit` at line 656. Scoped source assertions and `git diff --check`
  passed; touched-file ESLint timed out at the bounded 30-second limit. No full
  validation, CI/CD, push, deployment, or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Canonical Listening speed telemetry parity)

- The canonical `/listening/paper/[paperId]` strict route now forces 1x
  playback at metadata load and records `audio_speed_change_blocked` with the
  requested rate before resetting any browser or programmatic speed change.
  Practice-mode playback policy remains unchanged.
- Scoped source assertions and `git diff --check` passed. No full validation,
  CI/CD, push, deployment, or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Reading MCQ corruption hold parity)

- Reading single-answer MCQ payloads containing multiple selected options now
  set a durable administrator-review hold, record the reason/timestamp and
  audit event, withhold all converted-score evidence, and reject further
  learner answer writes or submission until review. Learner attempt/review
  projections expose the hold reason without exposing conversion metadata.
- Added the Reading attempt schema migration and focused grading regression.
  Scoped `git diff --check` passed; the focused `dotnet test` exceeded the
  bounded 40-second window without output and was stopped. No full validation,
  CI/CD, push, deployment, or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Listening full candidate preview)

- The Listening learner-safe authoring preview now projects only ready primary
  QuestionPaper and Audio assets and renders them through the authenticated
  learner viewers. Answer-key assets and marking fields remain outside the
  candidate projection; the audio card is explicitly authoring-only and does
  not create a scored attempt. Candidate typed and MCQ controls are local-only
  preview interactions and never submit an attempt. The candidate preview also
  exposes section selection and a local extract countdown when authored timing
  exists; it never changes authoritative attempt time.
- Marking mode now shows protected accepted variants, approved rationale,
  transcript evidence, validation status, speaker attitude, and per-option
  distractor authoring metadata when authored, while candidate mode remains
  answer-key-free. The focused Vitest rerun was stopped after it produced no
  output within the bounded check window; the focused file had passed 4/4
  before these latest marking-only metadata assertions were added. The
  route-level source assertion and scoped diff check passed; targeted ESLint
  completed with 0 errors and 2 existing setState-in-effect warnings. No full
  validation, CI/CD, push, deployment, or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Independent Reading preview failure paths)

- Reading candidate-safe and protected marking preview requests now settle
  independently in both directions. A failure in either projection no longer
  hides the other available view, and candidate mode continues to exclude
  answer keys, rationale, and accepted variants.
- The existing focused Reading preview regression now covers both failure
  directions; the targeted Vitest invocation passed 3 files and 12 tests.
  No full validation, CI/CD, push, deployment, or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Independent Listening preview failure paths)

- Listening candidate-safe and admin marking preview requests now settle
  independently in both directions. A failure in either projection no longer
  hides the other available view, and each mode reports its own bounded
  failure state without mixing answer-key data into candidate mode.
- Added `app/admin/content/listening/[paperId]/preview/page.test.tsx`; its three
  focused tests prove the learner-safe extract projection hides answer-key
  fields and that either projection remains available when the other fails.
- The focused Vitest file passed 3/3, and scoped diff checks passed. No full
  validation, CI/CD, push, deployment, or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Listening authoring preview context)

- The answer-key-free Listening candidate preview now includes authored
  extract context, speaker roles, accent, section limit, and audio cue-window
  metadata alongside the learner-safe question projection. Correct answers,
  accepted variants, rationales, and distractor metadata remain excluded;
  marking preview remains on the protected admin structure projection.
- Targeted ESLint passed with one pre-existing React setState-in-effect warning
  on the existing data-loading effect; no full test/build, CI/CD, push,
  deployment, or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Grounded Q&A reply rendering)

- Grounded Reading passage Q&A and Listening question Q&A now render the
  returned advisory reply in the learner result/review panels. The UI keeps
  the explicit marks-unaffected disclosure, shows the current reply exactly
  once, and preserves the server-side evidence/authorization gates.
- Added `components/domain/results/grounded-qna.test.tsx`; the focused Vitest
  file passed 2/2 tests. No full test/build, CI/CD, push, deployment, or live
  acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Reading grounded candidate Q&A UI)

- Reading submitted review items now carry only the linked passage identifier,
  and the learner result surface exposes grounded passage Q&A beside each
  item. The existing server service still requires ownership of a submitted
  attempt pinned to the current published Reading revision and fails closed
  when the passage or rulebook evidence is unavailable.
- This slice has only bounded source assertions and scoped diff checks; no full
  test/build, CI/CD, push, deployment, or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Listening grounded candidate Q&A)

- Listening now exposes a post-submit question-scoped Q&A route and learner
  results/review UI. It derives the owned submitted attempt, current published paper revision, question
  version, effective rationale, and transcript evidence server-side before
  invoking the grounded gateway. The response is explicitly advisory and
  marks-unchanged; missing evidence, revision drift, rulebook failure, or
  gateway failure blocks the answer.
- The focused service regression covers prompt delimiting and strict JSON
  reply parsing. This slice has only bounded source assertions and scoped diff
  checks; no full test/build, CI/CD, push, deployment, or live acceptance was
  run.

# Latest LR coding checkpoint - 2026-08-12 (Listening score-override account lifecycle)

- The separate Listening human score-override path now resolves the assigned
  `ExpertUser` profile before applying an override and fails closed for missing
  or inactive profiles. Focused grading fixtures include active assigned and
  unassigned reviewer profiles, with a regression covering the inactive
  assigned-reviewer `403 account_suspended` contract.
- This slice has only bounded source assertions and scoped diff checks; no
  full test/build, CI/CD, push, deployment, or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Reading analytics account lifecycle)

- The expert Reading cohort-analytics endpoint now resolves the authenticated
  `ExpertUser` profile and fails closed for missing or inactive profiles before
  querying assigned learner analytics, preserving the existing assignment
  scope.
- This slice has only bounded source assertions and scoped diff checks; no
  full test/build, CI/CD, push, deployment, or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Listening review revocation lifecycle)

- Listening expert “My Reviews” now requires a current non-cancelled,
  non-failed Listening review assignment for the authenticated expert, rather
  than exposing feedback solely because the expert historically authored it.
  A focused regression verifies feedback disappears after assignment revocation.
- This slice has only bounded source assertions and scoped diff checks; no
  full test/build, CI/CD, push, deployment, or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Listening review submission lifecycle)

- Listening expert “My Reviews” now also requires the underlying attempt to
  remain submitted, preventing stale feedback from exposing an in-progress
  attempt even when an assignment row is still present. A focused regression
  covers the non-submitted boundary.
- This slice has only bounded source assertions and scoped diff checks; no
  full test/build, CI/CD, push, deployment, or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Reading tutor account lifecycle)

- Reading expert assignment listing and attempt-access checks now resolve the
  authenticated `ExpertUser` profile and fail closed for missing or inactive
  profiles, matching the Listening expert boundary. Focused regressions cover
  the inactive-profile `403 account_suspended` contract and keep the existing
  expert-assignment scope fixture explicit.
- This slice has only bounded source assertions and scoped diff checks; no
  full test/build, CI/CD, push, deployment, or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Listening expert account lifecycle)

- All Listening expert read/write entry points now resolve the authenticated
  expert profile and fail closed when it is missing or inactive. This keeps a
  previously issued expert-role token from retaining Listening candidate
  access after account deactivation. A focused regression covers the 403
  `account_suspended` boundary.
- This slice has only bounded source assertions and scoped diff checks; no
  full test/build, CI/CD, push, deployment, or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Listening expert assignment boundary)

- Listening expert attempt lists, review bundles, feedback reads, and feedback
  writes are now fail-closed to the assigned expert. Access requires a
  non-cancelled/non-failed Listening `ReviewRequest` with an active
  `Assigned` or `Claimed` `ExpertReviewAssignment`, and the attempt must be
  submitted. Existing focused service fixtures now seed explicit assignments;
  a regression covers cross-candidate list exclusion and feedback denial.
- Bounded source assertions and scoped `git diff --check` are the intended
  checks for this slice. No full build/test, CI/CD, push, deployment, or live
  acceptance was run under the user's lightweight-validation instruction.

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
| LR-08 | Raw score is reproducible from stored response/key version | `ContentPaperService` assigns bounded Reading/Listening `PublishedRevisionId` values; `ListeningAttempt.LastQuestionVersionMapJson`, `ListeningAnswer.QuestionVersionSnapshot`, `ReadingAttempt.PaperRevisionId`, governed `MarkingPolicyVersionId`/snapshot guards now require and recheck captured policy version identity, legacy `Attempt` capture in `LearnerService.CreateAttemptAsync`, attempt-start `AssessmentScoreConversionSnapshot` now requires and rechecks pinned table-version provenance, migration `20260902090000_AddAssessmentScoreConversionAttemptSnapshots`, and focused grading/governance regressions | Implemented fail-closed revision, policy-snapshot, and score-table provenance guards; focused assessment governance run passed (11/11) before the latest regressions, broader revision/deployed verification pending |
| LR-09 | No answer/rationale is visible before final submission | `backend/src/OetLearner.Api/Endpoints/ReadingLearnerEndpoints.cs`, `backend/src/OetLearner.Api/Services/Listening/ListeningLearnerService.cs`, `tests/e2e/listening/listening-answer-key-not-exposed.spec.ts` | Implemented; deployed browser verification pending |
| LR-10 | Result has raw/part/converted/graph/review/disclosure contracts | `components/domain/results/score-conversion-evidence.tsx`, `components/domain/results/score-band-graph.tsx`, `components/domain/results/score-band-graph.test.tsx`, `backend/src/OetLearner.Api/Endpoints/ReadingLearnerEndpoints.cs`, `app/listening/results/[id]/page.tsx`, `app/reading/paper/[paperId]/results/page.tsx`, `app/reading/paper/[paperId]/results/page.test.tsx` | Implemented; primary Listening results now exposes an exact miss category for each wrong typed response, and Reading `number_form` misses render as “Incorrect answer form”; canonical Reading results test passed (7/7) with release-copy directories excluded; deployed responsive verification pending |
| LR-11 | Refresh/reconnect restores answers without extra time | `ReadingAttemptService`, `ReadingLearnerEndpoints`, `ListeningLearnerService`, server deadline fields and idempotent submit paths; `ReadingAuthoringTests.Resume_endpoint_preserves_persisted_timing_anchors`; `app/listening/player/[id]/page.tsx` and `lib/mobile/offline-sync.ts` now encrypt, queue, and server-wins reconcile transiently offline Listening answers | Implemented in source; focused backend/reconnect execution pending |
| LR-12 | AI failure cannot delay/change deterministic result | `backend/src/OetLearner.Api/Services/Reading/ReadingExplanationService.cs`, `backend/src/OetLearner.Api/Services/Reading/ReadingPassageQnaService.cs`, `backend/src/OetLearner.Api/Services/Listening/ListeningExplanationService.cs`, `backend/src/OetLearner.Api/Services/Listening/ListeningQuestionQnaService.cs`, `backend/src/OetLearner.Api/Endpoints/ReadingLearnerEndpoints.cs`, `backend/src/OetLearner.Api/Endpoints/ListeningLearnerEndpoints.cs`, `components/domain/results/grounded-reading-passage-qna.tsx`, `components/domain/results/grounded-listening-ai-explanation.tsx`, `components/domain/results/grounded-listening-question-qna.tsx`, grounded usage gateway; blank/unanswered responses fail closed and AI remains advisory-only | Implemented; deterministic Reading/Listening explanation and Q&A failure paths added; short execution pending |
| LR-13 | MCQ publication rejects duplicate, blank, and zero/multiple correct options | `ListeningStructureService`, `ReadingStructureService`, `ContentPaperService`, explicit duplicate/blank/zero/multiple-correct authoring regression tests | Implemented; shared paper publish now hard-blocks invalid Reading/Listening MCQ payloads, including legacy JSON blank options; focused execution pending |
| LR-14 | Key change uses controlled auditable re-mark | `AssessmentGovernanceEndpoints` validates submitted-attempt ownership, question-revision ownership, semantic key-snapshot shape, and exact current-question provenance for the original key before queueing; `ReadingGradingService.RegradeSubmittedAsync`, `ListeningGradingService.RegradeWithKeyAsync`; canonical original/updated result snapshots retained on the job; `ReadingAuthoringTests.ReMark_endpoint_enforces_key_provenance_and_stores_canonical_snapshots` | Implemented; focused re-mark execution pending |
| LR-15 | Desktop/mobile timer, passage, and controls do not clip | Responsive result/player layouts; shared `components/domain/reading-pdf-viewer.tsx` now fits the default question-paper view to the usable viewport while retaining deliberate zoom scrolling; `tests/e2e/responsive/listening-reading-layout.spec.ts` checks the canonical Listening and Reading learner routes for document overflow and clipped elements across desktop/mobile learner projects | Source fit-to-panel guard implemented; touched-file ESLint passed with four pre-existing warnings; pending dedicated Playwright run |
| LR-16 | Exam technical requirements are guidance only | `ListeningV2Endpoints.TechReadinessRequest` forwards device labels, screen dimensions, display scale, client shell/app version, parsed browser version, and Network Information observations to `ListeningSessionService.RecordTechReadinessAsync`; the service records them without rejecting; `TechReadinessDto.TechnicalRequirementsGuidanceOnly`; `lib/listening/tech-readiness-probe.ts`; candidate guidance in `ListeningIntroCard` and `app/exam-guide`; `ListeningV2AdvanceEndpointTests.Technical_guidance_signals_are_recorded_without_blocking_strict_readiness` | Source, focused frontend contract tests, and focused backend endpoint suite pass (12/12); deployed browser verification and owner style/copy review pending |

## Release gates that cannot be guessed

- Complete approved 0..42 Listening and Reading score-conversion tables.
- Approved normalization/capitalization/spacing policy and practice/mock lock mode.
- Effective rationale/evidence library, pathway thresholds, and pass labels.
- Legal/style approval for the differentiated practice score graph.
- Peak concurrent timed-attempt target and corresponding load evidence.

## Latest implementation slice

- Listening preflight now projects learner-safe candidate identity, profession,
  selected paper/mode, and server eligibility. Both Listening entry surfaces
  render the confirmation summary and keep Start disabled when eligibility is
  false. The canonical route also requires every authored learner section to
  resolve audio before Start; legacy combined audio remains a valid complete
  asset, while per-section papers fail closed on incomplete coverage. The
  readiness check verifies all resolved scored audio assets before the attempt
  timer can begin. Bounded source assertions passed; build/test and deployed
  browser verification remain pending.
- Reading timer guards now use exact `now >= deadline` semantics at the Part A
  lock, shared B/C lock, submit expiry, and Part A-to-B/C opening boundary on
  both client and server. Reading learner structure and in-progress attempt
  projections continue to exclude answer keys, accepted variants, and rationale
  fields. Bounded source assertions passed; build/test and deployed browser
  verification remain pending.
- Canonical Listening buffering/stall events now pause the visible section timer
  until `canplay` recovery, while audio load failures remain integrity/admin
  review holds. No client-side deadline or server timestamp is extended by this
  recovery path. Bounded buffering/timer source assertions and `git diff --check`
  passed; build/test and deployed browser verification remain pending.

- Built-in admin role changes now revoke the target admin's active refresh
  sessions in the same save as permission assignment/removal and include the
  revoked-session count in the audit/result. Backend service permission
  resolution preserves legacy implicit system-admin behavior only for accounts
  without a role-catalog record; role-managed `unassigned` accounts remain
  fail-closed after grant removal. JWT validation now rejects revoked token
  families regardless of the optional single-active-session setting. `git
  diff --check` passed; backend compilation/execution and deployed cross-role
  acceptance remain pending.
- The anonymous Listening test-rules contract now reads the effective
  `ListeningPolicy.FullPaperTimerMinutes` instead of advertising a conflicting
  hardcoded 40-minute duration. This aligns the disclosed timer with the
  server-authoritative 45-minute default and the supplied specification's
  approximately 45–50-minute Listening duration. OET content import, mock,
  authoring, onboarding, and countdown copy/defaults were aligned to the same
  policy-backed duration; `git diff --check` and focused source assertions
  remain the bounded verification, with backend compilation/execution and
  deployment still pending.
- Listening attempt creation now applies the active per-user accessibility
  policy to both legacy and relational attempts: `BlockAttempts` fails closed,
  extra-time entitlements extend the server deadline, expired overrides are
  ignored, and the effective timer/entitlement are captured in the immutable
  policy snapshot. The V2 FSM also ignores expired user overrides. A focused
  regression covers the 20% extra-time deadline; backend execution remains
  pending under the lightweight-validation instruction.
- Listening exam-like starts now enforce the owner-configured per-paper
  attempt cap and cooldown across both legacy and relational attempt stores;
  practice remains unlimited, while failed eligibility checks occur before
  credit debit. The focused governance regression also proves a configured cap
  rejects a forced second start; backend execution remains pending.
- Primary result feedback now carries the PDF-required error category for each
  wrong typed Listening response, reusing the persisted miss reason/error type
  and fail-closed authored-answer hints. Reading singular/plural and numeric
  form mismatches now render as “Incorrect answer form” rather than a generic
  review label. Targeted ESLint passed with only three pre-existing Reading
  results warnings; the canonical Reading results test passed 7/7 after
  excluding the user-owned `pdf-policy-release/` and `pdf-policy-release2/`
  copies from Vitest discovery. Deployment and browser verification remain
  pending.
- The built-in admin role catalog now includes explicit v1.1 Content Author,
  Clinical Reviewer, and Language Assessor presets. Assigning a built-in role
  is fail-closed for unknown roles and non-admin targets, replaces stale
  permission grants with the catalog's least-privilege set, and records the
  assigning actor/time in each grant; legacy content-editor and billing role
  IDs remain available. The role catalog regression was added, `git diff
  --check` passed, and the bounded backend build was stopped after producing
  no output within the requested lightweight-check window. Backend compilation,
  focused execution, and deployed cross-role acceptance remain pending.
- A follow-up source audit corrected the remaining learner-facing Listening
  Part C practice copy from four-option to three-option MCQs and corrected the
  internal implementation-plan statement. The repository-wide option-type
  audit now leaves four-option references only on Reading Part C and Reading
  fixtures; the Listening starter-copy sanitizer already removes its stale
  illustrative fourth option before publication.
- Listening V2 navigation repair now preserves existing `WindowStartedAt` and
  `WindowDurationMs` values when reconnect/refresh encounters malformed state;
  only legacy rows missing an anchor are initialized. This prevents repair
  from granting extra time. Focused backend execution remains pending.
- Reading resume projection now has a regression guard proving refresh returns
  the persisted `StartedAt` and `DeadlineAt` timing anchors rather than
  recalculating or extending them. Focused backend execution remains pending.
- Controlled re-mark intake now canonicalizes answer/variant snapshot aliases,
  requires the submitted attempt's live question key to match the claimed
  original snapshot, and stores canonical before/after snapshots. A mismatched
  original snapshot is rejected before a job is created. Focused backend
  execution remains pending.
- Published Listening audio now remains behind the authenticated entitlement
  check while streaming inline with `Accept-Ranges: none` and no download
  filename; the signed/direct media URL path is refused for the same assets.
  Non-Listening media retains its existing download behavior. Focused backend
  execution remains pending.
- Listening bulk authoring replacements now apply the same accepted-variant
  reason invariant as per-question PATCH: any existing typed-answer variant
  change fails closed without a reason, and successful changes emit the
  least-privilege actor/time/reason audit tuple without persisting a reusable
  reason in the question document. The admin editor now collects the reason;
  focused backend execution remains pending.
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
- Learner mock trend and per-report pass-prediction services now apply the same
  governed Reading/Listening exclusion through
  `MockAssessmentEvidenceGuard`; governed reports return pending conversion
  evidence instead of a mock-wide readiness or pass claim.
- Mock report aggregation, legacy report enrichment, and the retained background
  builder now withhold the mock-wide overall score while any Reading/Listening
  module lacks owner-approved conversion evidence, preventing a partial mean
  from being presented as a completed mock result.
- Shared Listening/Reading score graphs now require the explicit persisted pass
  decision as well as the scaled value and conversion-table key; a missing
  decision remains raw-only while an explicit failed decision still renders the
  owner-converted score.
- The shared mock evidence guard now treats malformed or structurally invalid
  report JSON as ineligible rather than allowing a readiness endpoint exception;
  readiness and pass prediction remain fail-closed on missing evidence.
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
  responses and submit responses. The existing endpoint regression now also
  asserts post-submit `evidenceSentence` disclosure and pre-submit source
  evidence redaction; fresh backend execution remains pending by request.
- Reading grading now writes `reading.marking.accepted_variant_used` with the
  matched explicit variant and attempt/question/policy provenance whenever that
  variant earns credit; the focused regression fixture covers both accepted and
  non-accepted answers.
- Remaining legacy Listening submit, expert re-mark, generic objective submit,
  mock-result, background-report, analytics-export, and client result paths now
  apply the same explicit conversion-decision gate and clear stale scaled values
  when conversion evidence is unavailable. Focused execution remains pending.
- Listening mock conversion now also requires the persisted session question
  count to equal the canonical 42; Reading and Listening mock result clients
  preserve the returned raw maximum, and the aggregate report no longer
  fabricates `/42` when a governed raw maximum is absent. The focused Reading
  pathway API regression passed (1 file, 7 tests); backend and deployed
  acceptance remain pending by request.
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
- The Reading admin authoring flow now exposes the same two-way review gate:
  its candidate preview consumes the learner-safe projection, while the
  protected marking preview reads the admin answer/rationale/evidence
  projection. Focused UI execution passed in `d95d92503` (10 tests).
- Listening manifest export now declares `modeSupport: ["computer"]`; the
  retired paper simulation is no longer advertised by the v1.1 authoring
  contract. The focused export/import regression passed in `d52d7995c`.
- Reading post-submit review now projects the authored `EvidenceSentence` only
  after submission and renders it as protected Source evidence beside the
  answer/rationale review. The canonical focused UI test passed (6/6); the
  untracked `pdf-policy-release*` copies were explicitly excluded and were not
  modified.
- Listening learner review no longer fabricates rationale text when an authored
  explanation is absent. The result contract carries a nullable explanation and
  the UI states that no approved explanation is available. The captured
  `LearningEvidenceLoopEnabled` policy now gates transcript evidence, failing
  closed for missing/false snapshots; focused endpoint regressions cover both
  null rationale and disabled evidence. Fresh backend execution remains pending
  by request.
- Listening and Reading grounded explanation services now fail closed when the
  gateway fails or returns malformed output; they no longer synthesize a
  fallback explanation from the answer/key. The deterministic submitted result
  remains available and the existing frontend error state reports the advisory
  explanation as unavailable. Focused failure regressions now assert this
  boundary; fresh backend execution remains pending by request.
- Grounded Listening and Reading explanations now also fail closed when the
  submitted attempt is missing or no longer matches its pinned published paper
  revision. Listening additionally requires the submitted question-version
  snapshot to match the current authored question before rationale, transcript,
  or answer-key evidence can reach the gateway. Focused drift regressions were
  added; fresh backend execution remains pending by request.
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
- The branded practice score graph now keeps raw-only results on a raw-score
  scale and hides the 350 reference marker until an owner-approved conversion
  exists; no client formula represents an unapproved scaled result. The
  focused graph regression covers both approved and raw-only states.
- Listening audio transport is now derived from the owner-approved marking
  policy captured in the attempt snapshot. Exam, home, and diagnostic modes
  remain fail-closed one-play/no-pause/no-scrub; practice transport controls
  can relax only when the snapshot explicitly selects practice lock mode and
  allows replay. Malformed or legacy snapshots remain strict. Focused policy
  unit coverage was added; backend execution and deployed browser verification
  remain pending.
- The legacy Listening mock grader now consumes that same immutable marking
  policy for text normalization. Credit remains limited to the canonical answer
  or explicitly authored variants; near-spelling classification is diagnostic
  only and remains zero-credit. Focused policy tests were added; backend
  execution and deployed mock verification remain pending.
- Legacy Listening mock start now fails closed unless the published template
  resolves to exactly 42 distinct questions with canonical Part A 24 / Part B 6
  / Part C 12 coverage, one-mark items, and valid three-option MCQs. It stores
  immutable question, key, option, rationale, transcript-evidence, and version
  snapshots and grades against them instead of mutable live rows. Focused
  execution and deployed mock verification remain pending.
- Reading grounded explanations now have a post-submit-only service contract;
  the question-only generation surface and cache path are no longer exposed
  through `IReadingExplanationService`. Learner explanations require an owned,
  submitted attempt and its stored answer before approved evidence is sent to
  the grounded gateway. The deterministic gateway-failure test now exercises
  that submitted-attempt path; focused execution and deployed verification
  remain pending.
- Reading now has a source-level scoring-path audit matching the Listening
  audit: Reading services must not reference the legacy raw-to-scaled helper
  and the governed grader must use `IAssessmentScoreConversionService`.
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
- `AdminRoleCatalog` now makes the v1.1 Content Author, Clinical Reviewer, and
  Language Assessor scopes explicit, and built-in role assignment synchronizes
  the persisted `AdminPermissionGrant` rows consumed by authentication rather
  than only changing legacy role metadata. Role removal now revokes those
  effective grants as well, including system-admin grants. Assignment and
  removal write actor/target/role/permission audit events in the same save.
  Tutor access remains
  on the Expert assigned-candidate paths; it is not granted through admin role
  presets.
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

## Listening runtime policy snapshot closure

- Listening start now snapshots the owner-editable practice replay and
  post-submit review visibility controls alongside the marking policy,
  effective timer, grace, and accessibility values. The active player reads
  the captured replay decision, and review projections redact correct answers,
  explanations, distractor analysis, and option correctness according to the
  captured policy without changing deterministic scores.
- Existing attempts with no Listening-policy snapshot retain the historical
  post-submit display defaults; malformed snapshots fail closed. Strict Exam,
  Home, and Diagnostic modes remain one-play regardless of any mutable replay
  setting. A focused audio-policy regression covers the admin practice-replay
  disable path. Backend execution remains pending by the owner's bounded
  validation instruction.
- The legacy Listening mock-start path now captures and applies the same
  practice replay policy, preventing that alternate route from bypassing the
  admin control.
- Corrected relational-attempt policy reads to consume their root-level
  snapshot shape for replay and review visibility; generic nested snapshots
  remain supported. Added a focused root-shape replay regression.
- The V2 FSM now captures the resolved session timing/lock/accessibility policy
  on both attempt shapes and reads that immutable snapshot for state, advance,
  readiness, and audio-resume operations. Missing legacy snapshots retain the
  compatibility resolver; present malformed snapshots select strict defaults
  rather than a newer live policy.

# Latest LR coding checkpoint - 2026-08-12 (Listening home history policy)

- `ListeningLearnerService.GetHomeAsync` now resolves the active global/user
  Listening policy and applies `ShowPastAttempts` to recent results,
  transcript-backed latest review, and per-paper last-attempt links. In-progress
  attempts remain visible and resumable when past-attempt display is disabled.
- No score, normalization, or other owner-controlled value was invented.
  Bounded source assertions and `git diff --check` remain the only planned
  checks for this slice; no backend build/test, CI/CD, push, or deployment is
  being run under the user's lightweight-validation instruction.

# Latest LR coding checkpoint - 2026-08-12 (Listening countdown policy)

- Listening now parses the owner-configured `CountdownWarningsJson` into
  bounded, descending second thresholds, captures them on new generic and
  relational attempts, and returns the captured thresholds from the session
  contract so policy edits cannot change an in-flight attempt's display.
- The learner timer consumes those thresholds instead of hardcoded 30/120
  second bands; malformed policy JSON fails closed to no configured client
  warning thresholds while the server deadline remains authoritative. A
  focused component regression covers the configured thresholds. Backend and
  frontend execution remain pending under the user's lightweight-validation
  instruction.

# Latest LR coding checkpoint - 2026-08-12 (Reading authoring publish boundary)

- Reading authoring now applies the published-paper mutation gate to every
  non-read route, matching the existing Listening authoring boundary. Content
  Authors cannot mutate published Reading papers without `content:publish` or
  `system_admin`; review transitions additionally require publish permission for
  `Published` and system-admin permission for emergency overrides. The review
  permission guard is located on the review-transition endpoint, not the
  distractor endpoint.
- Added focused permission regression coverage. Bounded source assertions and
  scoped `git diff --check` passed. No full build/test, CI/CD, push, deployment,
  or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Listening progress score policy)

- Listening home now preserves the chronological `recentResults` list while
  adding an owner-selected `progressScoreDisplay` projection. `best`,
  `latest`, `average`, and `first` are supported; unknown values fail closed
  to `latest`. Best-score ranking uses an approved scaled score when present,
  otherwise the stored raw-score proportion, and average mode reports only
  averages of persisted scores.
- The Listening hero consumes the progress projection and labels the selected
  mode. Grading, stored result values, and result routes are unchanged. A
  bounded source assertion and `git diff --check` are the only planned checks;
  backend/frontend execution, CI/CD, push, and deployment remain unrun.

# Latest LR coding checkpoint - 2026-08-12 (Listening short-answer policy)

- Listening attempt snapshots now carry `ShortAnswerNormalisation` and
  `ShortAnswerAcceptSynonyms` for both generic and relational starts, with the
  effective policy resolver exposing the same values to the immutable session
  snapshot and mock-start path.
- The authoritative V2 grader consumes those captured values. New snapshots
  require explicit synonym opt-in; malformed captured values receive zero
  synonym credit and exact matching. Captured normalization may tighten the
  governed marking policy but cannot loosen it, and the legacy fuzzy name never
  grants fuzzy credit. Paper-wide wrong-section analysis and accepted-variant
  audit events use the same synonym decision.
- Added a focused pure regression for explicit synonym opt-in. Bounded source
  assertions and `git diff --check` passed. No backend build/test, CI/CD, push,
  deployment, or live authenticated acceptance was run under the user's
  lightweight-validation instruction.

# Latest LR coding checkpoint - 2026-08-12 (Listening screen-reader policy)

- The owner-controlled `ScreenReaderOptimised` setting is now included in the
  effective Listening policy, captured on generic, relational, and mock starts,
  and returned by the session `modePolicy` contract.
- The Listening player renders a conditional, concise `aria-live="polite"`
  status announcement for section reading, audio, and review transitions. It
  does not announce every timer tick, avoiding a noisy live-region loop. Missing
  or malformed captured values fail closed; legacy attempts without the field
  use the current owner policy for compatibility.
- A focused player regression was added. Bounded source assertions and
  `git diff --check` passed; no frontend/backend execution, CI/CD, push,
  deployment, or live accessibility verification was run.

# Latest LR coding checkpoint - 2026-08-12 (Listening screen-reader policy)

- The owner-controlled `ScreenReaderOptimised` setting is now included in the
  effective Listening policy, captured on generic, relational, and mock starts,
  and returned by the session `modePolicy` contract.
- The Listening player renders a conditional, concise `aria-live="polite"`
  status announcement for section reading, audio, and review transitions. It
  does not announce every timer tick, avoiding a noisy live-region loop. Missing
  or malformed captured values fail closed; legacy attempts without the field
  use the current owner policy for compatibility.
- Bounded source assertions and `git diff --check` remain the only checks for
  this slice; no frontend/backend execution, CI/CD, push, deployment, or live
  accessibility verification was run.

# Latest LR coding checkpoint - 2026-08-12 (Listening AI extraction policy)

- Both Listening Part A AI extraction entry points now resolve the owner
  `AiExtractionEnabled` kill-switch before OCR/model spend and reject disabled
  extraction with a typed conflict.
- Both paths count every existing extraction draft for the paper and enforce
  the positive `AiExtractionMaxRetriesPerPaper` cap. Extraction remains a
  Pending, human-reviewable draft; no auto-approval path was introduced.
- Added bounded source evidence for the guard and its DI registration; the
  bounded assertions passed and `git diff --check` passed. No backend
  build/test, CI/CD, push, deployment, or live policy acceptance was run under
  the user's lightweight-validation instruction.

# Latest LR coding checkpoint - 2026-08-12 (Listening AI policy control surface)

- The existing Listening Policy admin page now exposes the AI extraction
  kill-switch and per-paper extraction cap, with labeled controls and bounded
  non-negative input handling.
- Human approval is displayed as a disabled, always-on invariant because both
  extraction paths stage `Pending` drafts and never auto-publish. The backend
  rejects negative retry limits and persists human approval as `true` even if
  an older client submits `false`.
- Bounded source assertions passed and `git diff --check` passed; no
  frontend/backend build or test, CI/CD, push, deployment, or live admin
  acceptance was run under the user's lightweight-validation instruction.

# Latest LR coding checkpoint - 2026-08-12 (Strict grader and PDF evidence closure)

- Listening grading now computes case sensitivity from the current captured
  question/policy inside the question loop, preventing an invalid pre-loop
  reference while preserving exact typed marking and explicit authored variants.
- PDF-backed Listening items are consistently exempt from retyped transcript
  timestamp validation because their source/evidence is the published question
  PDF; audio-authored items remain fail-closed until transcript evidence is
  complete.
- The Reading compatibility smart-quote default is now disabled to match the
  strict v1.1 identity normalization boundary. Bounded source assertions passed
  and `git diff --check` passed; no build/test, CI/CD, push, deployment, or live
  acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Fullscreen guidance boundary)

- The active Listening OET@Home path no longer requests or requires browser
  fullscreen. The legacy lock flag is disabled, while a separate
  `technicalGuidanceTelemetryEnabled` field keeps focus/fullscreen events
  non-blocking and auditable. Intro and skin copy now state that fullscreen is
  optional; strict audio, timer, and section-lock behavior remains unchanged.
- Bounded fullscreen-guidance assertions and `git diff --check` passed. No
  frontend/backend build or test, CI/CD, push, deployment, or live acceptance
  was run.

# Latest LR coding checkpoint - 2026-08-12 (Strict normalization surface)

- Reading grading no longer applies legacy smart-quote, hyphen-spacing, or
  number/unit rewrites. Those fields remain compatibility-only and are forced
  off in effective policy snapshots; the admin surface now directs authors to
  use explicit accepted variants. Only the named exact/trim/collapse profiles
  can affect comparison.
- Bounded source assertions and `git diff --check` remain required; no build,
  test, CI/CD, push, deployment, or live marking acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Listening policy parity)

- The diagnostic/mock Listening grader now consumes the captured Listening
  normalization profile and authored-variant flag, including fail-closed
  handling for invalid profiles and explicit case-insensitive matching.
- The relational Listening grader now applies the same captured profile and
  effective case-sensitivity rule, so both user-visible grading paths remain
  deterministic and policy-pinned.
- Bounded policy-parity assertions passed and `git diff --check` passed. No
  frontend/backend build or test, CI/CD, push, deployment, or live acceptance
  was run.

# Latest LR coding checkpoint - 2026-08-12 (Reading policy parity)

- Reading grading now preserves the captured Reading normalization profile,
  rejects invalid/legacy profiles by exact matching, and explicitly applies
  the approved case-insensitive profile wherever short-answer comparisons are
  made.
- The bounded source audit confirmed the LR conversion service still resolves
  only complete owner-approved lookup rows and has no interpolation/formula
  fallback.
- Bounded Reading normalization assertions and `git diff --check` passed. No
  frontend/backend build or test, CI/CD, push, deployment, or live acceptance
  was run.

# Latest LR coding checkpoint - 2026-08-12 (Bounded final audit)

- Targeted `git diff --check` passed across the touched LR grading and
  extraction services.
- The source audit found no live Listening/Reading grading call site using the
  legacy formula methods; the shared legacy definitions remain because they
  are referenced by existing non-LR/test contracts and were not removed.
- No full build/test, CI/CD, push, deployment, or live browser/admin
  acceptance was run, per the user's explicit instruction.

# Latest LR coding checkpoint - 2026-08-12 (Reading explanation grounding)

- Reading post-submit explanations now fail closed when the approved Reading
  rulebook is unavailable; the previous synthetic fallback rulebook was
  removed. Approved rationale, source evidence, submission, revision, and
  gateway requirements remain mandatory before explanation generation.
- Bounded source assertion and `git diff --check` passed. No backend build or
  test, CI/CD, push, deployment, or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Invalid normalization fail-closed closure)

- Legacy captured `fuzzy_levenshtein_1` profiles are now treated as invalid in
  Listening policy snapshots; unknown and legacy fuzzy strategies in both
  graders fail closed to exact matching. They cannot receive trimming or any
  other extra normalization, and Levenshtein remains analytics-only.
- Bounded invalid-normalization assertions passed and `git diff --check`
  passed. No backend build/test, CI/CD, push, deployment, or live marking
  acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Durable extraction-start retry ledger)

- Reading and Listening Part A extraction now reserve a durable audit ledger
  entry before OCR/model execution and count those starts against the owner
  retry cap. Concurrent or failed provider runs can no longer bypass the cap
  because no completed draft row exists yet; zero remains explicitly unlimited.
- Bounded extraction-ledger assertions passed and `git diff --check` passed.
  No backend build/test, CI/CD, push, deployment, or live admin acceptance was
  run.

# Latest LR coding checkpoint - 2026-08-12 (Atomic extraction reservation)

- Reading and Listening Part A retry reservations now use serializable
  database transactions around the count-and-insert operation, preventing
  concurrent extraction requests from bypassing the owner cap. Listening
  reserves only after paper/asset/input guards and immediately before OCR.
- Bounded atomic-reservation assertions passed and `git diff --check` passed.
  No backend build/test, CI/CD, push, deployment, or live admin acceptance was
  run.

# Latest LR coding checkpoint - 2026-08-12 (Listening mock strict-marking disclosure)

- The Listening mock-results route now renders the required strict-marking
  disclosure alongside its practice-score disclaimer, covering the
  specification's warning about minor spelling variants and examiner
  discretion.
- Bounded source assertions passed and `git diff --check` passed; no frontend
  build/test, CI/CD, push, deployment, or live result-page acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Listening Part B/C AI kill-switch)

- Listening Part B/C OCR and Claude imports now resolve the owner Listening
  policy before any provider call and return the same typed conflict when AI
  extraction is disabled. The path remains a projection only and requires
  explicit admin review/save.
- Bounded source assertions passed and `git diff --check` passed; no backend
  build/test, CI/CD, push, deployment, or live admin acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Listening Part B/C extraction cap)

- Projection-only Part B/C AI imports now record a durable
  `ListeningPartBCExtractionStarted` audit event and enforce the owner
  `AiExtractionMaxRetriesPerPaper` cap before OCR/model spend. A zero cap keeps
  the explicit unlimited behavior; the typed retry-limit conflict is reused.
- Bounded source assertions passed and `git diff --check` passed; no backend
  build/test, CI/CD, push, deployment, or live admin acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Strict normalization profile closure)

- Listening and Reading graders now correctly collapse internal whitespace for
  the documented `trim_collapse_case_insensitive` profile while retaining the
  key's case-sensitivity decision. Both policy services reject empty,
  unsupported, or fuzzy normalization profiles before persistence.
- Bounded source assertions passed and `git diff --check` passed; no backend
  build/test, CI/CD, push, deployment, or live marking acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Listening mock pass-status display)

- The Listening mock-results surface now displays the approved conversion
  table's pass status. When no approved conversion exists, pass status remains
  explicitly unavailable rather than being inferred from raw or linear scores.
- Bounded source assertions passed and `git diff --check` passed; no frontend
  build/test, CI/CD, push, deployment, or live result-page acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Listening duration defaults)

- Production Listening test-rules fallback and mock-authoring default now use
  45 minutes, matching the server-authoritative default and the specification's
  approximately 45–50-minute Listening duration.
- Admin timer copy now describes the same 45–50-minute expectation; the public
  test-rules endpoint remains policy-backed and does not hard-code a score or
  duration over the configured policy.
- Bounded duration assertions and `git diff --check` passed. No frontend or
  backend build/test, CI/CD, push, deployment, or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Reading AI policy control surface)

- The Reading global policy page now exposes the AI extraction kill-switch,
  always-on human-approval invariant, and bounded per-paper retry cap.
- Reading policy updates now reject negative retry caps; zero remains the
  explicit unlimited value, while the existing backend approval invariant is
  preserved.
- Bounded source assertions passed and `git diff --check` passed; no
  frontend/backend build or test, CI/CD, push, deployment, or live admin
  acceptance was run under the user's lightweight-validation instruction.

# Latest LR coding checkpoint - 2026-08-12 (Exact Reading shape and MCQ corruption hold)

- Reading publish validation, AI extraction validation, and admin authoring
  now enforce Part A Q1-7 matching, Q8-14 short answer, Q15-20 sentence
  completion; Part B three-option MCQ only; and Part C four-option MCQ only.
  Text-linked papers also require exactly A=4, B=6, and C=2 text rows,
  while PDF-only papers remain permitted to defer text extraction.
- Listening multiple-selected payloads for single-answer MCQs now remain
  invalid for automated marking, create an administrator-review hold and
  audit event, and persist no converted score or pass result.
- Bounded source assertions passed and `git diff --check` passed. No frontend
  or backend build/test, CI/CD, push, deployment, or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Legacy Listening MCQ corruption parity)

- The legacy JSON-attempt submission path now scans Part B/C single-answer
  MCQ payloads for multiple selected options before deterministic marking. It
  preserves the raw attempt, records an administrator-review audit event,
  withholds automated evaluation and score conversion, and returns the same
  conflict used by relational Listening attempts.
- Bounded source assertions passed and `git diff --check` passed. No backend
  build/test, CI/CD, push, deployment, or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Part B one-question forward flow)

- The learner Listening player now renders Part B as one active question at a
  time, binds the shared Part B audio to the active extract's authored cue
  window, and exposes an irreversible Next confirmation before starting the
  next short extract. A Part B audio file without valid per-extract cue
  boundaries now halts the attempt and raises administrator review instead of
  silently skipping questions.
- Bounded source assertions passed and `git diff --check` passed. No frontend
  build/test, backend build/test, CI/CD, push, deployment, or live acceptance
  was run under the user's lightweight-validation instruction.

# Latest LR coding checkpoint - 2026-08-12 (Listening boundary confirmation and API locks)

- The strict Listening paper route now requires an explicit confirmation for
  every sub-section boundary, including timer expiry, and does not advance the
  client cursor after a failed server cursor write.
- Relational and legacy JSON answer saves now enforce the active canonical
  A1/A2/B/C1/C2 section, reject edits to locked or future sections, and reject
  cursor jumps that skip a boundary.
- Bounded source assertions passed and `git diff --check` passed. No frontend
  or backend build/test, CI/CD, push, deployment, or live acceptance was run
  under the user's lightweight-validation instruction.

# Latest LR coding checkpoint - 2026-08-12 (Canonical Listening preflight gate)

- The canonical strict Listening paper route now runs the audio readiness check
  before enabling Start, verifies all resolved scored audio assets, records the
  learner sound-check outcome through `/v1/listening-pathway/audio-check`, and
  records advisory device/browser/network telemetry on the relational attempt.
- The missing audio-check endpoint is now mapped server-side; strict attempt
  creation remains fail-closed until the learner has passed that check.
- Bounded source assertions passed and `git diff --check` passed. No frontend
  or backend build/test, CI/CD, push, deployment, or live acceptance was run
  under the user's lightweight-validation instruction.

# Latest LR coding checkpoint - 2026-08-12 (Canonical Listening refresh/reconnect)

- The canonical strict route now restores the server-authoritative section
  cursor and active Part B question after refresh. Relational and legacy
  attempts project audio lifecycle state/checkpoints; active audio resumes from
  the latest persisted checkpoint, while completed audio does not autoplay
  again.
- The canonical route now logs audio start/progress/end, buffering/stalls,
  playback errors, focus/visibility changes, answer changes, and section
  transitions through the existing integrity-event service. Playback failures
  remain server-admin-review holds.
- Bounded refresh/reconnect source assertions passed and `git diff --check`
  passed. No frontend or backend build/test, CI/CD, push, deployment, or live
  acceptance was run under the user's lightweight-validation instruction.

# Latest LR coding checkpoint - 2026-08-12 (Listening duplicate-submit winner replay)

- Listening submit idempotency now closes the concurrent loser path for both
  legacy JSON and relational attempts. The existing durable `(scope, key)`
  cache remains the replay source; optimistic concurrency now reloads the
  committed winner, prevents a second legacy evaluation, and returns that
  winner review to the duplicate request.
- The existing legacy `Attempt.DraftVersion` column is now an explicit
  concurrency token and is incremented on submit, including review-hold
  submits. Relational attempts retain their existing `RowVersion` guard.
- Bounded source assertions and `git diff --check` passed. No build/test,
  CI/CD, push, deployment, or live acceptance was run under the user's
  lightweight-validation instruction.

# Latest LR coding checkpoint - 2026-08-12 (Result-surface disclosure and graph parity)

- Reading mock results now carry the same branded practice-score disclosure
  and stricter-than-examiner marking disclosure as canonical results.
- Reading and Listening mock results now expose owner-table conversion
  evidence and the platform-branded score-band graph. Transcript-backed
  Listening review now exposes the same graph and practice-result label.
- TSX parse checks for all three touched result pages, bounded source
  assertions, and `git diff --check` passed. No full build/test, CI/CD, push,
  deployment, or live acceptance was run under the user's lightweight-
  validation instruction.

# Latest LR coding checkpoint - 2026-08-12 (Listening Part A/B/C result breakdown)

- Canonical Listening results and transcript-backed review now render a
  deterministic Part A/B/C table with correct, incorrect, unanswered, and
  percentage values derived from the item-level review payload. No client
  scoring or score conversion is introduced.
- Touched Listening result/review pages and the shared breakdown component
  passed bounded TSX parse checks and `git diff --check`. No full build/test,
  CI/CD, push, deployment, or live acceptance was run under the user's
  lightweight-validation instruction.

# Latest LR coding checkpoint - 2026-08-12 (Server-clock timer synchronization)

- Reading and Listening learner responses now expose a server timestamp for
  display-clock correction. Reading deadline comparisons use the corrected
  server clock; strict and legacy Listening timed displays use the same
  corrected clock while preserving server-authoritative expiry/grading and the
  existing audio-buffering hold behavior.
- Added the shared clock helper and timestamp-aware timer tick path. Bounded
  TS/TSX parse checks, server-clock source assertions, and `git diff --check`
  passed. No full build/test, CI/CD, push, deployment, or live acceptance was
  run under the user's lightweight-validation instruction.

# Latest LR coding checkpoint - 2026-08-12 (Result timing analytics)

- Reading review responses now expose server-persisted total elapsed time and
  Part A/B/C timing derived from answer telemetry. Listening review responses
  expose server-persisted attempt duration plus A1/A2/B/C1/C2 audio durations
  derived from the saved audio cue timeline. No timing value is fabricated
  when telemetry is missing or malformed.
- Canonical Reading results, Listening results, and transcript-backed Listening
  review now render a shared accessible Time used summary with per-section rows,
  total, and an explicit Not recorded state for unavailable telemetry.
- Bounded TS/TSX parse checks, backend contract assertions, and scoped
  `git diff --check` passed. No full build/test, CI/CD, push, deployment, or
  live acceptance was run under the user's lightweight-validation instruction.

# Latest LR coding checkpoint - 2026-08-12 (Mock result timing parity)

- Legacy Reading mock results now show the persisted session total and explicit
  Not recorded Part A/B/C rows because that pathway did not capture per-part
  telemetry. Legacy Listening mock results now return and display their
  persisted session duration with explicit Not recorded A1/A2/B/C1/C2 rows.
- Mock result TS/TSX parsing, backend contract assertions, and scoped
  `git diff --check` passed. No full build/test, CI/CD, push, deployment, or
  live acceptance was run under the user's lightweight-validation instruction.

# Latest LR coding checkpoint - 2026-08-12 (Mock targeted next-step routes)

- Mock Reading next steps and remediation study-plan items now deep-link to the
  existing Part-filtered Error Bank route. Mock Listening next steps and study
  plan items now map persisted error categories to existing focused drill
  routes, with deterministic safe fallbacks for unmapped categories.
- Added `MockNextStepRouteResolver` and focused route-regression coverage. A
  bounded source assertion and scoped `git diff --check` passed. The focused
  backend test remains unexecuted; no full build/test, CI/CD, push, deployment,
  or live acceptance was run under the user's lightweight-validation
  instruction.

# Latest LR coding checkpoint - 2026-08-12 (Grounded explanation attribution)

- Reading and Listening submitted-attempt explanation calls now include stable
  `reading.explanation.v1` and `listening.explanation.v1` prompt-template IDs,
  allowing AI usage records to attribute the exact explanation prompt version.
  The obsolete shared-question Reading explanation cache writer was removed so
  learner-specific generated explanations remain request-scoped and advisory.
- Bounded source assertions and scoped `git diff --check` passed. No full
  build/test, CI/CD, push, deployment, or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Reading authoring release permissions)

- Reading question review transitions now fail closed for Content Authors:
  entering `Published` requires content-publish/publisher-approval permission,
  and emergency rollback via `IsAdminOverride` requires `system_admin`.
  Added focused permission-policy regression coverage for publish, override,
  and malformed claims.
- Bounded source assertions and scoped `git diff --check` passed. The focused
  backend test remains unexecuted; no full build/test, CI/CD, push, deployment,
  or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Listening TTS production fail-closed)

- Production startup now rejects `Listening:TtsProvider=stub` instead of
  warning and allowing silence-generated audio artifacts. The stub remains
  available only for development/CI pipeline checks; production must select a
  real provider such as ElevenLabs.
- Added focused provider normalization/environment-policy regression. Bounded
  source assertions and scoped `git diff --check` passed. The focused backend
  test remains unexecuted; no full build/test, CI/CD, push, deployment, or live
  acceptance was run under the user's lightweight-validation instruction.

# Latest LR coding checkpoint - 2026-08-12 (Reading extraction fail-closed fallback)

- Production `GroundedReadingExtractionAi` failures no longer call the
  canonical placeholder-manifest generator. They persist an explicitly flagged
  empty, non-approvable extraction draft, preventing fabricated passages,
  questions, or answer keys from entering authoring review. The known-manifest
  extractor remains documented as a test fixture only; production DI remains
  grounded-gateway based.
- Added a focused fallback regression. Bounded source assertions and scoped
  `git diff --check` passed. The focused backend test remains unexecuted; no
  full build/test, CI/CD, push, deployment, or live acceptance was run under
  the user's lightweight-validation instruction.

# Latest LR coding checkpoint - 2026-08-12 (Grounded Reading passage Q&A)

- Replaced the Reading pathway passage-Q&A stub with a grounded service. It
  requires an owned submitted attempt pinned to the current published Reading
  revision, bounds and role-filters conversation context, sends the passage
  through the rulebook-grounded gateway, and returns explicit advisory/marks
  unaffected metadata. Missing evidence, malformed AI output, gateway failure,
  and pre-submit requests fail closed with a conflict; no deterministic marks
  are changed.
- Added the AI task/reply contract, DI registration, TypeScript response type,
  and focused prompt/parser regressions. Bounded source assertions and scoped
  `git diff --check` passed. The focused backend test remains unexecuted; no
  full build/test, CI/CD, push, deployment, or live acceptance was run under
  the user's lightweight-validation instruction.

# Latest LR coding checkpoint - 2026-08-12 (Mock error taxonomy parity)

- Legacy Reading and Listening mock diagnostics now share a server-side typed
  answer classifier for inference, number/plural form, unit, spelling, form,
  and detail categories. Authored MCQ distractor categories remain preferred;
  persisted correctness and marks are never modified by the diagnostic path.
- Added focused classifier regressions. Bounded source assertions and scoped
  `git diff --check` passed; the new backend tests were not executed. No full
  build/test, CI/CD, push, deployment, or live acceptance was run under the
  user's lightweight-validation instruction.

# Latest LR coding checkpoint - 2026-08-12 (Reading mock governed score conversion)

- Reading legacy mock start now captures the owner-effective score-conversion
  snapshot in session metadata and marks an available table used. Reading mock
  results resolve that pinned snapshot, expose the version/pass fields expected
  by the result UI, and keep scaled score unavailable when no approved table is
  available. No raw-to-500 formula or client-side conversion was introduced.
- Added a focused Reading mock result contract regression. Bounded backend source
  assertions and scoped `git diff --check` passed; the new backend test was not
  executed. No full build/test, CI/CD, push, deployment, or live acceptance was
  run under the user's lightweight-validation instruction.

# Latest LR coding checkpoint - 2026-08-12 (Reading mock governed grade)

- Reading mock results now expose the owner-authored conversion grade and the
  API client uses that grade when an approved table is present. The previous
  scaled-score-to-grade derivation was removed from this result path; no grade
  is synthesized when conversion is unavailable.
- Bounded C# source assertions, TypeScript transpile checks, and scoped
  `git diff --check` passed. No full build/test, CI/CD, push, deployment, or
  live acceptance was run under the user's lightweight-validation instruction.

# Latest LR coding checkpoint - 2026-08-12 (Prioritized mock review)

- Reading and Listening completed mock result payloads now place unanswered
  items first, then answered mistakes, then correct answers, while preserving
  the complete session review and original order within each priority group.
  Persisted `IsCorrect` values remain authoritative.
- Bounded source assertions, TypeScript transpile checks, and scoped
  `git diff --check` passed. No full build/test, CI/CD, push, deployment, or
  live acceptance was run under the user's lightweight-validation instruction.

# Latest LR coding checkpoint - 2026-08-12 (Shared mock review ordering)

- Reading and Listening now use the same server-side `MockResultReviewOrdering`
  helper for unanswered-first, mistake-second, correct-last review ordering.
  A focused regression covers priority and stable order within each group.
- Bounded source assertions, TypeScript transpile checks, and scoped
  `git diff --check` passed. The focused backend regression was added but not
  executed; no full build/test, CI/CD, push, deployment, or live acceptance was
  run under the user's lightweight-validation instruction.

## 2026-08-12 — Legacy mock remediation plan bridge

- Completed legacy Reading and Listening mock results now derive editable
  remediation items from persisted error-summary categories, counts, and
  question IDs. Items are deduplicated per completed session, link to actual
  targeted practice routes, and expose `/study-plan` for candidate edits.
- Scoped touched-source checks, TypeScript/TSX transpile parsing, and
  `git diff --check` passed. The focused backend contract test is present but
  unexecuted; full validation, CI/CD, deployment, and live acceptance remain
  outside the requested lightweight-validation scope.

## 2026-08-12 — Admin editing of mock remediation recommendations

- The admin learner study-plan surface now edits generated remediation items
  through the existing audited `AdminContentWrite` override route. Title,
  rationale, due date, duration, section, and learner route are editable; the
  admin projection includes the persisted rationale/content metadata. The
  recommendation remains separate from marks and result claims.
- The override route is covered by the granular authorization inventory.
  Touched-source checks, TypeScript/TSX transpile parsing, and `git diff --check`
  passed; backend execution, deployment, and live admin acceptance remain
  unexecuted under the lightweight-validation instruction.

## 2026-08-12 — Legacy mock part accuracy percentages

- Legacy Reading and Listening mock result breakdowns now expose and render
  server-derived accuracy percentages for each candidate-facing Part A/B/C
  row, in addition to raw and outcome counts. This closes the percentage
  requirement without adding client-side marking or score conversion.
- Touched-source checks and TypeScript/TSX transpile parsing passed, with
  `git diff --check`. The focused backend contract test remains unexecuted;
  backend build/test and live acceptance remain outside the requested scope.

## 2026-08-12 — Reading mock deterministic error taxonomy

- Legacy Reading mock review now derives diagnostic categories from persisted
  answers plus authored question metadata: distractor categories when keyed,
  detail, spelling, form, or incorrect-answer. Unanswered and correct results
  remain deterministic, and no category can change the stored mark.
- Narrow source checks, TypeScript/TSX transpile parsing, and `git diff --check`
  passed. Backend execution and deployed acceptance remain pending.

## 2026-08-12 — Listening mock distractor taxonomy

- Legacy Listening MCQ review now derives a post-submit distractor category
  from the immutable question snapshot's authored option metadata, falling
  back to `distractor` when the option is untagged. No score or answer decision
  is changed by this diagnostic projection.
- Narrow source invariants and `git diff --check` passed; backend execution and
  deployed acceptance remain pending.

# Latest LR coding checkpoint - 2026-08-12 (Listening Part A/B/C aggregation)

- Listening mock result breakdowns now aggregate the candidate-facing score
  totals as Part A, Part B, and Part C while retaining A1/A2/B/C1/C2 as the
  separate timing sections required by the listening flow.
- Bounded source assertions and scoped `git diff --check` passed. No full
  build/test, CI/CD, push, deployment, or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Governance lifecycle hardening)

- Assessment score tables, marking-policy versions, and rationale/evidence
  records now enforce distinct Draft -> InReview -> Approved -> Effective
  transitions. Effective promotion rejects unapproved records; first use
  still locks the immutable version. Admin UI/API actions and authorization
  inventory expose the separate review and approval steps.
- Targeted TS transpile parsing, backend state-guard assertions, and scoped
  `git diff --check` passed. No long validation, CI/CD, push, deployment, or
  live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Release-status projection)

- Added an admin-only fail-closed release-status projection for Listening and
  Reading. It reports blockers when the effective complete score table,
  effective marking policy, or effective rationale/evidence library is absent,
  and surfaces the status in the scoring governance page without inventing
  owner values.
- Targeted TS transpile parsing, backend release-status assertions, and scoped
  `git diff --check` passed. No long validation, CI/CD, push, deployment, or
  live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Release ambiguity fail-closed)

- The release-status projection now rejects two effective/locked table or
  marking-policy versions with the same effective timestamp, matching the
  runtime resolver's ambiguity guard instead of silently selecting one.
- Bounded backend source assertions, TS transpile parsing, and scoped
  `git diff --check` passed. No long validation, CI/CD, push, deployment, or
  live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Release effective-time parity)

- Release readiness now ignores future-dated Effective/Locked records until
  their `EffectiveFrom` timestamp, matching the runtime score-table and
  marking-policy resolvers.
- Bounded source assertions and scoped `git diff --check` passed. No long
  validation, CI/CD, push, deployment, or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Owner release-gate metadata)

- Marking-policy governance now carries explicit owner release metadata for
  score-graph legal/style approval, peak concurrent timed-attempt target, and
  load-evidence URL. Policy approval rejects missing or incomplete metadata;
  release status reports the same blocker without inventing values. Added a
  focused contract regression for preservation and default denial.
- Bounded backend source assertions, TS transpile parsing, and scoped
  `git diff --check` passed. The focused backend test remains unexecuted; no
  long validation, CI/CD, push, deployment, or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Release-status refresh parity)

- The scoring governance page now refreshes the consolidated release status
  after marking-policy, rationale, and re-mark governance reloads as well as
  after score-table actions, preventing stale Ready/Blocked state after an
  approval transition.
- Targeted TS transpile parsing and scoped `git diff --check` passed. No long
  validation, CI/CD, push, deployment, or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Evidence URL validation)

- Peak-concurrency release evidence now requires an absolute HTTPS URL in
  addition to graph approval and a positive target. Non-HTTPS placeholders are
  denied by the release-gate contract; the focused contract source regression
  covers missing and non-HTTPS denial.
- Bounded source assertions and scoped `git diff --check` passed. The focused
  backend test remains unexecuted; no long validation, CI/CD, push,
  deployment, or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Mock review ordering and contract regression)

- Reading mock item review now follows the immutable session question order,
  rather than repeating per-part display-order values, and unknown answers are
  counted as unanswered consistently with Listening. A focused backend
  contract regression covers candidate-safe JSON item fields and transcript
  evidence timestamps.
- Bounded TS parsing, backend source assertions, and scoped `git diff --check`
  passed. The new backend test was added but not executed; no full build/test,
  CI/CD, push, deployment, or live acceptance was run under the user's
  lightweight-validation instruction.

# Latest LR coding checkpoint - 2026-08-12 (Mock item-review parity)

- Completed legacy Reading and Listening mock results now expose ordered,
  candidate-safe item review records derived from persisted attempts and
  authored question keys. The UI shows the learner answer, correct answer,
  marks, explanation, and Reading passage or Listening transcript evidence;
  Listening also preserves authored evidence time bounds and persisted
  spelling/meaning miss flags. Pre-submit routes remain unchanged and no
  client-side marking was introduced.
- Mock item-review TS/TSX parsing, backend contract/source assertions, and
  scoped `git diff --check` passed. No full build/test, CI/CD, push,
  deployment, or live acceptance was run under the user's lightweight-
  validation instruction.

# Latest LR coding checkpoint - 2026-08-12 (Mock error-pattern and next-step parity)

- Legacy Reading and Listening mock result APIs now return server-derived
  error-category counts and a deterministic targeted-practice link based only
  on persisted completed item review. Both mock result UIs display the pattern
  summary before the full item review. No AI output, score conversion, pass
  claim, or client-side marking was added to this path.
- Bounded TS parsing, backend source assertions, and scoped `git diff --check`
  passed. The focused backend contract test remains added but unexecuted; no
  full build/test, CI/CD, push, deployment, or live acceptance was run.

# Latest LR coding checkpoint - 2026-08-12 (Mock result breakdown parity)

- Legacy Reading and Listening mock result endpoints now derive candidate-safe
  Part A/B/C breakdowns from persisted question attempts, including raw score,
  correct, incorrect, and unanswered counts. Listening preserves A1/A2/B/C1/C2
  timing sections; Reading preserves A/B/C timing sections. No client scoring
  or fabricated telemetry was introduced.
- Mock result TS/TSX parsing, backend contract assertions, and scoped
  `git diff --check` passed. No full build/test, CI/CD, push, deployment, or
  live acceptance was run under the user's lightweight-validation instruction.

# Latest LR coding checkpoint - 2026-08-12 (Mock targeted next-step routes)

- Mock Reading next steps and remediation study-plan items now deep-link to the
  existing Part-filtered Error Bank route. Mock Listening next steps and study
  plan items now map persisted error categories to existing focused drill
  routes, with deterministic safe fallbacks for unmapped categories.
- Added `MockNextStepRouteResolver` and focused route-regression coverage. A
  bounded source assertion and scoped `git diff --check` passed. The focused
  backend test remains unexecuted; no full build/test, CI/CD, push, deployment,
  or live acceptance was run under the user's lightweight-validation
  instruction.
# 2026-08-12 Listening section-transition telemetry

- Closed the canonical player telemetry gap for PDF §17.11. Strict server FSM
  advances now emit one `section_transition` event only when the applied target
  crosses sections, with from/to sections and FSM states. Local forward-only
  advances emit the same event with a local-transition reason. Added the client
  event literal and a focused strict cross-section regression.
- Focused Vitest passed 1/1 for the new transition regression and 1/1 for the
  existing audio-resume regression. Targeted ESLint completed with 0 errors and
  16 existing warnings; source assertions and scoped `git diff --check` passed.
- No full validation, CI/CD, push, deployment, or live acceptance was run.
# 2026-08-12 Listening boundary-copy parity

- Corrected candidate-facing Listening copy that incorrectly said the next
  section opened automatically at audio end. It now states that the player
  opens an irreversible finish confirmation and only advances after confirmation,
  matching the enforced lock boundary.
- Focused Listening player-component Vitest passed 39/39 across 3 collected
  files. Targeted ESLint completed with 0 errors and 16 existing warnings;
  boundary-copy assertions and scoped `git diff --check` passed.
- No full validation, CI/CD, push, deployment, or live acceptance was run.
# 2026-08-12 Assessment score-table UI coverage

- The admin Listening/Reading score-table editor now rejects non-integer raw or
  converted values and requires exactly one row for every raw score 0 through
  42 before submitting a draft. The backend validator remains authoritative;
  this closes the client-side validation mismatch without inventing values.
- Targeted ESLint completed with 0 errors and 3 existing warnings; score-table
  source assertions and scoped `git diff --check` passed. No full validation,
  CI/CD, push, deployment, or live acceptance was run.

### 2026-08-12 Listening practice speed parity

- The canonical Listening player now enforces 1x playback only for server
  policy modes with `onePlayOnly` enabled. Practice mode no longer resets a
  learner-selected playback rate, while strict exam/home behavior and blocked
  speed telemetry remain unchanged.
- Scoped Vitest passed 2/2 speed-lock regressions; scoped `git diff --check`
  passed. No full validation, CI/CD, push, deployment, or live acceptance was
  run.

### 2026-08-12 Reading MCQ invalid-state parity

- Reading single-answer MCQ payloads containing multiple persisted selections
  are now held as invalid for automated marking: the raw answer is preserved,
  `IsCorrect` remains null, points and distractor metadata are cleared, and the
  existing admin-review/conversion fail-closed path remains active. Regression
  assertions now cover the persisted invalid state.
- Scoped `git diff --check` passed. The one focused backend test was attempted
  with a 60-second bound but timed out before producing output; no full
  validation, CI/CD, push, deployment, or live acceptance was run.

### 2026-08-12 Reading invalid-review projection

- Submitted Reading review projections now preserve corrupted multiple-choice
  answers as `isInvalid` with explicit invalid counts. They are excluded from
  ordinary wrong-answer clusters/counts, shown as `Invalid — admin review`,
  and do not expose a grounded AI explanation action. The result surface also
  displays the fail-closed admin-review warning and conversion status.
- Scoped source assertions and `git diff --check` passed. The focused Reading
  results Vitest file was attempted with a 30-second bound but timed out before
  producing output; no full validation, CI/CD, push, deployment, or live
  acceptance was run.

### 2026-08-12 Listening audio-failure advance hold

- The canonical Listening paper section now receives an explicit audio-failure
  signal from authenticated-media and media-element load failures. It keeps the
  section timer paused and disables advance/submit controls after the
  `audio_error` admin-review event, so a candidate cannot cross a failed audio
  boundary while the server-side review hold remains authoritative.
- Bounded source assertions and scoped `git diff --check` passed. No full
  validation, CI/CD, push, deployment, or live acceptance was run.

### 2026-08-12 Reading invalid submit-result contract

- Reading submit grading now keeps corrupted single-answer MCQs out of the
  ordinary `incorrectCount`, exposes `invalidCount`, and marks the affected
  answer as `isInvalid` in the typed submit contract. The persisted review
  projection and the immediate submit response therefore share the same
  fail-closed invalid state.
- Bounded source assertions and scoped `git diff --check` passed. No full
  validation, CI/CD, push, deployment, or live acceptance was run.

### 2026-08-12 Reading invalid Error Bank isolation

- Reading multiple-selection corruption now remains outside the ordinary
  Error Bank/remediation path. The grader preserves the indeterminate answer
  for administrator review but does not seed or mutate a learner-error entry;
  the focused regression asserts that no Error Bank row is created.
- Bounded source assertions and scoped `git diff --check` passed. No full
  validation, CI/CD, push, deployment, or live acceptance was run.

### 2026-08-12 Reading invalid analytics isolation

- Reading cohort/paper analytics now exclude `multiple_selection_review_required`
  answers from question opportunities, accuracy/difficulty denominators,
  discrimination groups, time-per-question aggregates, and distractor
  histograms. The invalid submitted attempt remains visible in attempt-level
  audit/completion counts, while its indeterminate answer cannot distort
  learner-performance analytics.
- Bounded source assertions and scoped `git diff --check` passed. No full
  validation, CI/CD, push, deployment, or live acceptance was run.

### 2026-08-12 Reading admin analytics invalid isolation

- The admin Reading analytics endpoint now excludes administrator-review
  attempts from canonical pass/scaled-score eligibility and excludes their
  invalid answer rows from question opportunities, unanswered/accuracy
  denominators, distractor traps, and timing aggregates. Attempt-level totals
  remain auditable; no raw-to-scaled fallback was introduced.
- Bounded source assertions and scoped `git diff --check` passed. No full
  validation, CI/CD, push, deployment, or live acceptance was run.

### 2026-08-12 Reading privileged invalid-review disclosure

- The privileged Reading attempt review now exposes explicit
  `requiresAdminReview`, `adminReviewReason`, `invalidCount`, per-section
  invalid counts, and per-question `isInvalid`. Invalid items are excluded
  from privileged ordinary-incorrect counts and accuracy denominators, and
  owner conversion metadata remains unavailable while the review hold is
  active. The admin UI renders a dedicated warning and invalid-answer state.
- Bounded source assertions and scoped `git diff --check` passed. No full
  validation, CI/CD, push, deployment, or live acceptance was run.

### 2026-08-12 Listening audio-review input freeze

- The same canonical Listening audio-review hold now freezes Part A typed
  inputs, Part B/C MCQ radios, and the direct advance handler in addition to
  the section timer and boundary button. This prevents local answer mutation or
  forward navigation after a failed scored-audio load while the server remains
  the authoritative hold and persistence boundary.
- Bounded source assertions and scoped `git diff --check` passed. No full
  validation, CI/CD, push, deployment, or live acceptance was run.

### 2026-08-12 Listening invalid-review projection

- Submitted relational Listening reviews now preserve corrupted single-answer
  MCQs as `isInvalid` when the deterministic answer row has a null correctness
  state. Invalid items are excluded from ordinary incorrect counts, error
  clusters, issue/feedback projections, and recall seeding; controlled human
  overrides clear the invalid state. The result contract exposes invalid and
  administrator-review counts/reasons, and the candidate UI renders an explicit
  admin-review warning, excludes invalid items from part accuracy denominators,
  and suppresses automated answer/explanation/Q&A actions for them.
- Bounded source assertions and scoped `git diff --check` passed. No full
  validation, CI/CD, push, deployment, or live acceptance was run.

### 2026-08-12 Listening analytics/export invalid isolation

- Listening relational admin exports now disclose per-answer `isInvalid` and
  `missReason` metadata. Student and admin analytics exclude legacy and
  relational attempts held for administrator review from approved conversions,
  per-part aggregates, weakness counts, hardest-question tallies, distractor
  heat, and spelling aggregates while retaining the attempt for audit/completion
  visibility.
- Focused regression coverage was added for invalid export disclosure and
  relational admin-review analytics exclusion. Bounded source assertions and
  scoped `git diff --check` passed. No full validation, CI/CD, push, deployment,
  or live acceptance was run.

### 2026-08-12 Listening expert invalid-review disclosure

- Listening expert attempt lists and review bundles now preserve the canonical
  administrator-review reason, invalid-answer count, and per-answer invalid /
  miss metadata. Held attempts no longer expose owner-approved scaled scores in
  expert review or learner home/review projections. The expert UI renders a
  dedicated hold warning, invalid-answer badges, and excludes invalid answers
  from the displayed correctness denominator.
- Focused regression coverage was extended for expert bundle invalid disclosure
  and assignment-bound review behavior. Bounded source assertions and scoped
  `git diff --check` passed. No full validation, CI/CD, push, deployment, or
  live acceptance was run.

### 2026-08-12 Reading and Listening home review-state disclosure

- Reading and Listening learner home projections now disclose the canonical
  administrator-review hold and reason while suppressing stale scaled scores.
  Learner cards render an explicit pending-review state instead of treating
  held attempts as approved results.
- Focused home regressions cover both relational Listening and canonical
  Reading projections. Bounded source assertions and scoped `git diff --check`
  passed. No full validation, CI/CD, push, deployment, or live acceptance was
  run.

### 2026-08-12 Listening pathway review-state isolation

- Both Listening pathway implementations now exclude attempts held for
  administrator review from progression qualification, approved scaled-score
  milestones, owner-pass gates, and stored pathway scores. Held attempts remain
  auditable as submitted but cannot advance learner progression.
- Focused regressions cover the 12-stage relational pathway and the legacy
  course-pathway snapshot. Bounded source assertions and scoped `git diff --check`
  passed. No full validation, CI/CD, push, deployment, or live acceptance was
  run.

### 2026-08-12 Computer-based delivery guidance correction

- The learner `/exam-guide` no longer advertises paper-based delivery as a
  platform mode. It now states the website computer-based/OET@Home-style
  rehearsal scope and explicitly treats paper-based behaviour as educational
  guidance only. Listening and Reading copy now matches the specification's
  42-question structures, Listening duration range, Reading Part A 15-minute
  lock, and shared 45-minute Parts B+C block.
- Bounded source assertions and scoped `git diff --check` passed. No full
  validation, CI/CD, push, deployment, or live acceptance was run.

### 2026-08-12 Reading strict synonym payload validation

- Reading short-answer authoring now rejects accepted-synonym payloads that
  contain non-string, empty, or whitespace-only entries. This keeps optional
  answer variants explicit and prevents malformed values from reaching the
  deterministic marking path.
- Focused source assertions and scoped `git diff --check` passed. No full
  validation, CI/CD, push, deployment, or live acceptance was run.

### 2026-08-12 Listening scored-audio range lock

- The content-addressed Listening TTS route is used as the scored fallback when
  a paper has no uploaded section audio. It no longer enables HTTP byte-range
  processing, preventing a server-side partial-response seek path from bypassing
  the player’s forward-only audio lock.
- A focused endpoint regression asserts that a Range request receives the full
  audio response rather than a partial response. Only bounded source assertions
  and scoped `git diff --check` are intended; no full validation, CI/CD, push,
  deployment, or live acceptance was run.

### 2026-08-12 Review-hold reason consistency

- Reading and Listening mutation, submit, and grading guards now preserve the
  fail-closed administrator-review state while reporting the persisted server
  reason. Learners are no longer told that malformed-key, unsupported-type, or
  audio-fault holds are necessarily multiple-selection defects.
- Bounded source assertions and scoped `git diff --check` were used only. No
  full validation, CI/CD, push, deployment, or live acceptance was run.

### 2026-08-12 Reading grading answer-key integrity hold

- Reading grading now treats invalid question points, malformed answer keys or
  options, unsupported question types, and invalid accepted-variant payloads as
  an administrator-review hold. The raw learner answer is preserved, the item
  receives no automated credit or ordinary wrong-answer classification, score
  conversion is withheld, and the hold is audited.
- Learner review, cached grading results, tutor review, analytics, and Error
  Bank filtering now recognize both the existing multiple-selection hold and
  the new question-integrity hold. A focused unknown-question regression was
  added. Bounded source assertions and scoped `git diff --check` passed. No
  full validation, CI/CD, push, deployment, or live acceptance was run.

### 2026-08-12 Reading canonical answer-key completeness

- Reading authoring now rejects empty canonical short-answer values, empty
  labeled answer maps, and empty labeled answer values before they can enter a
  published deterministic marking path.
- Focused source assertions and scoped `git diff --check` passed. No full
  validation, CI/CD, push, deployment, or live acceptance was run.

### 2026-08-12 Listening MCQ option preservation

- Listening authoring no longer silently truncates a multiple-choice payload
  with more than three options. The authored option set is preserved so the
  existing exact-shape publish gate can reject the invalid paper without
  mutating its answer-key content.
- Focused source assertions and scoped `git diff --check` passed. No full
  validation, CI/CD, push, deployment, or live acceptance was run.

### 2026-08-12 Listening one-mark-per-question publish gate

- Listening structural validation now blocks both relational and JSON papers
  when any authored question is not worth exactly one mark, even if aggregate
  Part A/B/C totals still sum to 42. This prevents compensating 0/2-point
  entries from changing the item-level assessment contract.
- Focused source assertions and scoped `git diff --check` passed. No full
  validation, CI/CD, push, deployment, or live acceptance was run.

### 2026-08-12 Listening point-value preservation

- Listening authoring no longer coerces zero or negative authored points to one
  during JSON round-trip, replacement, or relational mirroring. Invalid point
  values remain visible to the one-mark publish gate instead of being silently
  rewritten.
- Focused source assertions and scoped `git diff --check` passed. No full
  validation, CI/CD, push, deployment, or live acceptance was run.

### 2026-08-12 Reading point-value preservation

- Reading authoring no longer coerces zero or negative authored points to one
  during question creation or update. Invalid point values remain visible to
  the existing one-mark publish gate instead of being silently rewritten.
- Focused source assertions and scoped `git diff --check` passed. No full
  validation, CI/CD, push, deployment, or live acceptance was run.

### 2026-08-12 Listening runtime/backfill point and option preservation

- Listening backfill, learner projections, attempt max-score initialization,
  and analytics projections now preserve authored point values instead of
  coercing zero or negative marks to one. Backfill also preserves extra MCQ
  options so an invalid option count remains visible to the publish gate rather
  than being truncated into a valid three-option question.
- Focused source assertions and scoped `git diff --check` passed. No full
  validation, CI/CD, push, deployment, or live acceptance was run.

### 2026-08-12 Reading exam publish-readiness gate

- Full Reading Exam attempts now re-run the structural publish validator before
  creating an attempt. Incomplete or invalid authored papers are rejected with
  `reading_paper_not_publish_ready`; subset and learning practice modes retain
  their controlled practice path.
- Focused source assertions and scoped `git diff --check` passed. No full
  validation, CI/CD, push, deployment, or live acceptance was run.

### 2026-08-13 Reading passage Q&A attempt scoping

- Reading grounded passage Q&A now requires the exact submitted attempt ID,
  learner ownership, matching paper and published revision, and membership of
  the passage in that attempt's question scope. This prevents a learner from
  using another submitted attempt on the same paper to request an unrelated
  passage's grounded content.
- The result UI and client request now pass the finalized attempt ID, and a
  focused subset-scope regression covers included versus excluded passages.
  Only bounded source assertions and scoped `git diff --check` were run; no
  full validation, CI/CD, push, deployment, or live acceptance was run.
