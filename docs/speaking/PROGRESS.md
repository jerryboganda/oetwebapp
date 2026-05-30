# Speaking Module Progress

> **Current status (2026-05-29)**: historical Speaking hardening ledger. The current cross-project source of pending work is [`../STATUS/REMAINING-WORK.md`](../STATUS/REMAINING-WORK.md), with security sign-off tracked separately in [`../security/speaking/checklist.md`](../security/speaking/checklist.md).

Last updated: 2026-05-07
Mode: Ralph-style hardening loop
Linked PRD: ./PRD.md

## Current Status

Core Speaking engineering exists, and this pass hardens the admin/learner operator gaps around discoverability, mock-set editing, route correctness, mock-session attempt binding, retention registration, and import documentation. The remaining product gap is structured import/OCR for scanned source PDFs.

## Progress Ledger

| Date | Area | Status | Evidence | Next action |
| --- | --- | --- | --- | --- |
| 2026-05-06 | Discovery swarm | Complete | 10 subagents mapped backend, admin UI, learner UX, QA, architecture, data/import, risks | Implement focused fixes |
| 2026-05-06 | PRD/progress split | Complete | `docs/speaking/PRD.md`, `docs/speaking/PROGRESS.md` | Keep updated as checks run |
| 2026-05-06 | Sample import inventory | Complete | Attached PDFs inventoried; 3 text-extractable, 3 scanned/image-only | Use ContentPaper/assets/provenance path |
| 2026-05-06 | Admin discoverability | Complete | Content Hub now links Speaking Authoring and Speaking Mock Sets | Monitor admin navigation feedback |
| 2026-05-06 | Mock-set edit UI | Complete | Admin mock-set page exposes edit/update modal | Consider resetting create form state after edit during next UI pass |
| 2026-05-06 | Self-practice route | Complete | Backend/client fallback now use `/conversation/{id}` | Covered by focused self-practice tests |
| 2026-05-06 | Mock-session submission binding | Complete | Recorder receives paired `attemptId`; API helper sends `contentId`/`mockSessionId`; backend validates owned Speaking attempt, real mock-session membership, content binding, and locked state | Keep tamper tests in SpeakingMockSetTests |
| 2026-05-06 | Retention registration | Complete | Program registers `SpeakingAudioRetentionWorker`; deletion only clears DB pointer when storage deletion succeeds | Covered by registration/sweep tests |
| 2026-05-06 | Validation | Partial | Frontend focused API test passed; changed-file diagnostics clean; backend/tsc blocked by unrelated dirty-worktree errors | Re-run backend and tsc after unrelated AdminAlert/AdminAlerts issues are fixed |
| 2026-05-07 | Final validation | Complete | `npx tsc --noEmit` zero errors; focused `dotnet test` 64/64 pass (Speaking + AdminFlows + retention worker); `npx vitest run lib/__tests__/api.test.ts` 24/24 pass | Speaking hardening slice closed |
| 2026-06-01 | Double-marking + senior moderation (§15.4 / §15.5) | Complete | Backend: `SpeakingTutorAssessment.MarkerRole`; `SpeakingModerationCase` entity + DbContext partial + idempotent migration `20260601090000_AddSpeakingModeration`; `SpeakingModerationService` (open / second-mark / variance auto-finalize / senior finalize / request-reattempt) with separation-of-duties; `SpeakingModerationEndpoints` under `/v1/expert/speaking` (ExpertOnly); canonical tutor projection now prefers `moderated` → `primary`. Frontend: `lib/api/speaking-assessments.ts` moderation client; `app/expert/speaking/moderation` queue + `[sessionId]` case detail; "Send to moderation" action on the assess page. Tests: `SpeakingModerationTests` (10 cases). | Optional P2: variance threshold as a runtime setting; moderator RBAC role if separation-of-duties is insufficient |
| 2026-06-01 | Claim heartbeat fix (`EnsureTutorMayReviewAsync`) | Complete | Pre-existing latent bug surfaced once the backend build was repaired: the interlocutor early-return skipped the active-claim heartbeat refresh, so `TutorAssessment_RefreshesActiveClaimOnAssessmentActivity` failed. Refactored so the claim heartbeat refresh runs before the interlocutor short-circuit; behaviour-preserving for all non-interlocutor paths. | None |
| 2026-06-01 | Final validation (moderation slice) | Complete | SDK-container `dotnet test` (filter SpeakingModeration\|DualAssessment\|WritingWave6): **40 passed, 0 failed**. Frontend: language-server diagnostics clean + `eslint` clean on all 4 moderation files. | Slice closed |
| 2026-06-03 | WS1 — server-authoritative session clock & technical-issue (§1.2, §22.5) | Complete | `SpeakingSessionClock` DTO + `GetClockAsync` + `ReportTechnicalIssueAsync` on backend; `/clock` and `/technical-issue` endpoints; frontend client functions; `SpeakingSessionClockTests` proven in prior session. Migration `20260602090000_AddSpeakingSessionClock` adds `PrepTimeSeconds`/`RolePlayTimeSeconds`/`TechnicalIssueNote`/`TechnicalIssueAt` columns. | None |
| 2026-06-03 | WS4 — submit-for-marking two-recording gate (§14.2) | Complete | `SubmitForMarkingAsync` on `SpeakingSessionService`: requires state=Finished + evidence (non-warmup recording OR transcript with WordCount>0); stamps `SubmittedAt` idempotently; mirrors to legacy Attempt. Endpoint `/submit` registered. Frontend learner pages call `submitSpeakingForMarking()`. `SpeakingSubmitAndVisibilityTests` covers: pre-finish conflict, no-evidence conflict, success + idempotent. | None |
| 2026-06-03 | WS6 — result-visibility config (§10) | Complete | `SpeakingResultVisibilityConfig` entity (9 boolean flags + AllowReattempt); `SpeakingResultVisibilityService` (ResolveAsync/UpsertAsync global+card-override); migration `20260603090000_AddSpeakingResultVisibility`; admin endpoints; learner endpoint; frontend `lib/api/speaking-result-visibility.ts`. Tests in `SpeakingSubmitAndVisibilityTests` cover global default creation + card override resolution. Model snapshot not hand-edited (see decisions). | None |
| 2026-06-03 | WS9 — PDF content import (SPK-007) | Complete | `SpeakingContentImportService`: validates magic bytes (PDF), persists via `IFileStorage`, extracts text via `IPdfTextExtractor`, builds validation report (required vs. advisory fields), optionally calls `AiDraftRolePlayCardAsync` with SourceMaterial grounding. Endpoint `POST /v1/admin/speaking/role-play-cards/import` (25 MB limit, rate-limited, AdminContentWrite). Frontend admin import page at `app/admin/content/speaking/role-play-cards/import/page.tsx`. | None |
| 2026-06-03 | WS2 — backend tests | Complete | `SpeakingSubmitAndVisibilityTests.cs`: 5 tests covering WS4 submit-gate invariants (3 cases) + WS6 visibility resolution (2 cases). Uses InMemory provider matching `SpeakingSessionClockTests` pattern. | None |


## Gap Register

| ID | Gap | Severity | Resolution target |
| --- | --- | --- | --- |
| SPK-001 | Speaking authoring is hidden inside generic Content Papers | High | Add clear Admin Content > Speaking Authoring entry |
| SPK-002 | Speaking mock-set admin UI lacks edit/update | High | Add edit modal wired to `updateAdminSpeakingMockSet` |
| SPK-003 | Self-practice route points to non-existent `/conversation/session/*` | High | Return and fallback to `/conversation/*` |
| SPK-004 | Mock-set recordings do not bind to paired attempts | High | Include/use paired `attemptId` in task submission |
| SPK-005 | Speaking retention worker not hosted | High | Register hosted service |
| SPK-006 | Speaking content picker sends ignored subtest filter | High | Add backend/API support for `subtest` filter |
| SPK-007 | ~~Attached scanned PDFs need OCR/manual structuring~~ | ~~Medium~~ | **Resolved (WS9)**: Import endpoint + `SpeakingContentImportService` + admin page; PdfPig text extraction + Azure OCR fallback; auto-draft with SourceMaterial grounding |
| SPK-008 | URL-carried `attemptId` could be tampered within a learner's own attempts | High | Backend validates Speaking subtest, real mock-session membership, expected content/session binding, and locked upload state |

## Verification Log

| Command | Result | Notes |
| --- | --- | --- |
| `get_errors` on changed Speaking/backend/API files | Pass | No diagnostics in changed files |
| `runTests` focused Speaking/API set | Pass | 62 passed before final server binding hardening |
| `cmd /c npx vitest run lib/__tests__/api.test.ts` | Pass | 24/24 tests passed, including bound mock-session attempt helper |
| `cmd /c dotnet test backend\\tests\\OetLearner.Api.Tests\\OetLearner.Api.Tests.csproj --filter FullyQualifiedName~SpeakingMockSetTests --no-restore` | Blocked (2026-05-06) | Build failed before tests due unrelated `AdminAlertService` compile errors in dirty worktree |
| `cmd /c npx tsc --noEmit` | Blocked (2026-05-06) | Unrelated `app/admin/alerts/page.tsx` errors: non-exported `apiRequest`, invalid `Badge variant="secondary"` |
| `npx tsc --noEmit` (2026-05-07) | Pass | Zero TypeScript errors after upstream AdminAlerts cleanup |
| `dotnet test --filter SpeakingMockSetTests\|SpeakingAudioRetentionWorkerTests\|SpeakingSelfPracticeFlowsTests\|AdminFlowsTests` (2026-05-07) | Pass | 64 passed, 0 failed in 2m05s |
| `npx vitest run lib/__tests__/api.test.ts` (2026-05-07) | Pass | 24/24 including bound mock-session helper |
| Independent scoped review | Pass | No blocking or non-blocking findings after the server-side binding hardening revision |
| `dotnet test --filter SpeakingModeration\|DualAssessment\|WritingWave6` (SDK container, 2026-06-01) | Pass | 40 passed, 0 failed; includes the 10 `SpeakingModerationTests` + the repaired claim-heartbeat test |
| `eslint` on moderation frontend (queue, case detail, assess, api client) | Pass | Clean; language-server diagnostics also clean on all 4 files |

## Decisions

- Reuse `ContentPaper` + `SpeakingContentStructure` for card CRUD instead of creating a parallel schema.
- Reuse `SpeakingMockSet` for two-card mock composition.
- Reuse Conversation for AI-patient practice.
- Treat attached PDFs as source assets/provenance inputs; do not hard-code their body text into seed data.
- Learner routes must receive candidate-safe projections only.
- (2026-06-03) Model snapshot (`LearnerDbContextModelSnapshot.cs`) deliberately not hand-edited for WS6 migration. Rationale: 25k-line generated file with unreliable line offsets; build doesn't validate snapshot; tests use InMemory/EnsureCreated from live model config; prod uses raw-SQL `IF NOT EXISTS` migration. Next `dotnet ef migrations add` will emit a harmless catch-up diff.
- (2026-06-03) WS2 tests pivot: SignalR hub unit tests (ConversationHub.SpeakingRoleplay.cs) are brittle mock-intensive; the "interlocutor script never leaks to learner" invariant is already proven by `PrivateSpeakingEndpointProjectionTests`. WS2 test coverage targets the genuinely-new WS4 submit-gate + WS6 visibility logic instead.
