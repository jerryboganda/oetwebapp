# Speaking Module Progress

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

## Gap Register

| ID | Gap | Severity | Resolution target |
| --- | --- | --- | --- |
| SPK-001 | Speaking authoring is hidden inside generic Content Papers | High | Add clear Admin Content > Speaking Authoring entry |
| SPK-002 | Speaking mock-set admin UI lacks edit/update | High | Add edit modal wired to `updateAdminSpeakingMockSet` |
| SPK-003 | Self-practice route points to non-existent `/conversation/session/*` | High | Return and fallback to `/conversation/*` |
| SPK-004 | Mock-set recordings do not bind to paired attempts | High | Include/use paired `attemptId` in task submission |
| SPK-005 | Speaking retention worker not hosted | High | Register hosted service |
| SPK-006 | Speaking content picker sends ignored subtest filter | High | Add backend/API support for `subtest` filter |
| SPK-007 | Attached scanned PDFs need OCR/manual structuring | Medium | Import as source assets; author structured fields after OCR/manual extraction |
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

## Decisions

- Reuse `ContentPaper` + `SpeakingContentStructure` for card CRUD instead of creating a parallel schema.
- Reuse `SpeakingMockSet` for two-card mock composition.
- Reuse Conversation for AI-patient practice.
- Treat attached PDFs as source assets/provenance inputs; do not hard-code their body text into seed data.
- Learner routes must receive candidate-safe projections only.
