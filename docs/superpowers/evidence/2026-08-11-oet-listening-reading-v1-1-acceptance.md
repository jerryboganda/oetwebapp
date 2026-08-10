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
| LR-03 | Misspelled Part A answer receives zero with no fuzzy/AI override | `backend/src/OetLearner.Api/Services/Listening/ListeningGradingService.cs`, `backend/tests/OetLearner.Api.Tests/Listening/ListeningPartASpellingTests.cs`; deterministic `IsCorrect` remains authoritative over AI metadata | Implemented; focused backend run pending |
| LR-04 | Explicit accepted variant receives credit and is named in audit | `ListeningGradingService` writes `listening.marking.accepted_variant_used` with the matched variant; `AssessmentGovernanceEndpoints` preserves key snapshots | Implemented; focused audit test pending |
| LR-05 | Strikethrough is not a selected MCQ answer | Candidate selection remains a server-validated option key and annotation metadata is separate in `ListeningLearnerService`; no dedicated strikethrough regression is currently evidenced | Pending dedicated acceptance test |
| LR-06 | Reading Part A locks at the authoritative 15-minute deadline | `backend/src/OetLearner.Api/Services/Reading/ReadingAttemptService.cs`, `backend/src/OetLearner.Api/Endpoints/ReadingLearnerEndpoints.cs`, `tests/e2e/reading/part-a-lock.spec.ts` | Implemented; deployed browser verification pending |
| LR-07 | Reading B+C share one authoritative 45-minute timer | `ReadingAttemptService`, `ReadingLearnerEndpoints`, `tests/e2e/reading/part-a-lock.spec.ts` | Implemented; deployed browser verification pending |
| LR-08 | Raw score is reproducible from stored response/key version | `ListeningAttempt.LastQuestionVersionMapJson`, `ListeningAnswer.QuestionVersionSnapshot`, `ReadingAttempt.PaperRevisionId`, `backend/tests/OetLearner.Api.Tests/Assessment/AssessmentScoreConversionServiceTests.cs` | Implemented fail-closed revision guard; focused reproducibility test pending |
| LR-09 | No answer/rationale is visible before final submission | `backend/src/OetLearner.Api/Endpoints/ReadingLearnerEndpoints.cs`, `backend/src/OetLearner.Api/Services/Listening/ListeningLearnerService.cs`, `tests/e2e/listening/listening-answer-key-not-exposed.spec.ts` | Implemented; deployed browser verification pending |
| LR-10 | Result has raw/part/converted/graph/review/disclosure contracts | `components/domain/results/score-conversion-evidence.tsx`, `app/listening/results/[id]/page.tsx`, `app/reading/paper/[paperId]/results/page.tsx`, `app/reading/paper/[paperId]/results/page.test.tsx` | Implemented; deployed responsive verification pending |
| LR-11 | Refresh/reconnect restores answers without extra time | `ReadingAttemptService`, `ListeningLearnerService`, server deadline fields and idempotent submit paths | Implemented in source; focused reconnect test pending |
| LR-12 | AI failure cannot delay/change deterministic result | `ListeningPartAAiScoringWorker`, `ReadingExplanationService`, grounded usage gateway; provider failure returns fallback/non-blocking review | Implemented; focused AI failure test pending |
| LR-13 | MCQ publication rejects zero/multiple correct options | `ListeningStructureService`, `ReadingStructureService`, existing authoring validation tests | Implemented; focused release test pending |
| LR-14 | Key change uses controlled auditable re-mark | `AssessmentGovernanceEndpoints`, `ReadingGradingService.RegradeSubmittedAsync`, `ListeningGradingService.RegradeWithKeyAsync`; original/updated result snapshots retained on the job | Implemented; focused re-mark test pending |
| LR-15 | Desktop/mobile timer, passage, and controls do not clip | Responsive result/player layouts and existing mobile/desktop route surfaces | Pending dedicated Playwright run |
| LR-16 | Exam technical requirements are guidance only | `AssessmentMarkingPolicyDocument.TechnicalRequirementsGuidanceOnly`; no resolution/headset/VPN hard gate added | Implemented; owner style/copy review pending |

## Release gates that cannot be guessed

- Complete approved 0..42 Listening and Reading score-conversion tables.
- Approved normalization/capitalization/spacing policy and practice/mock lock mode.
- Effective rationale/evidence library, pathway thresholds, and pass labels.
- Legal/style approval for the differentiated practice score graph.
- Peak concurrent timed-attempt target and corresponding load evidence.

## Deployment evidence

- Previous release `5141eac3d` completed Actions run `31437434454`, including
  API/web/backup images, production migration, and blue/green deployment.
- The changes represented by this document require a new commit SHA and a new
  completed build/migration/deploy run before they can be called live.
