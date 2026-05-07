# Mocks Module Progress

> Last updated: 2026-05-08  
> Source of truth: `docs/mocks/PRD.md`

## Status Summary

The Mocks module has a real backend model, learner flow, report flow, admin list/create/publish surface, item analysis, bookings, remediation, and diagnostic entitlement. The current closure work is hardening and completion: align admin UI with existing backend CRUD, make learner/report flows safer, and add regression coverage for the gaps discovered during the May 2026 audit.

## Progress Ledger

| Date | Area | Status | Evidence | Next action |
| --- | --- | --- | --- | --- |
| 2026-05-07 | Codebase research | Done | Backend, learner, admin, UX, architecture, planning, risk, and review specialists mapped mocks surfaces. | Convert findings into focused fixes. |
| 2026-05-07 | PRD | Done | `docs/mocks/PRD.md` created. | Keep updated as contracts change. |
| 2026-05-07 | Backend hardening | Done | Partial-submit, booking-status, remediation JSON, report metadata, and canonical subtest id fixes implemented with endpoint tests. | Continue with server-resolved section result adapters. |
| 2026-05-07 | Admin CRUD | Done | API helpers, edit modal, published-paper picker, and section reorder controls added. | Add richer admin preview/analysis coverage in a follow-up wave. |
| 2026-05-07 | Learner report | Done | SoR rendering now requires all four final numeric subtest scores and tolerates legacy report ids. | Add browser smoke when dev server is available. |
| 2026-05-07 | Validation | Done | Focused Vitest, backend endpoint tests, TypeScript check, and lint passed. | Broaden to full suite before release. |
| 2026-05-08 | Mocks Authoring Wizard | Done | New `/admin/content/mocks/wizard/[bundleId]/{bundle,listening,reading,writing,speaking,review}` flow scaffolds papers per subtest, attaches assets via `IFileStorage`, sets per-skill structure (Listening 24/6/12, Reading 20/6/16, Writing case-notes + model answer, Speaking roleplay), and runs the publish gate paper-by-paper before bundle release. Driven by new `lib/mock-wizard/{api,state,upload}.ts` + `components/domain/mock-wizard/*`. | Add E2E smoke when dev env is available. |
| 2026-05-08 | Sample mock seeder | Done | New `MockSampleSeeder` (double-gated: `IsDevelopment()` + `Enabled` flag) seeds 3 idempotent Draft bundles from `Project Real Content/` via `IFileStorage` with SHA-256 dedup. Registered in `Program.cs`; opt-in config in `appsettings.Development.json`. | Flip `Enabled=false` default before any staging copy. |
| 2026-05-08 | Subtest player wiring | Done | Listening, Reading, Writing, Speaking player pages now read `mockAttemptId`/`mockSectionId` from the launch contract and POST `completeMockSection` with rawScore/maxRawScore/scaledScore/grade/evidence after grading, then navigate to `/mocks/player/{mockAttemptId}`. Speaking page bug fixed (was reading wrong `attemptId`/`mockSession` params). Closes MOCK-GAP-008. | — |
| 2026-05-08 | Wizard reviewer pass | Done | Independent `agency-reviewer` returned **APPROVED WITH FIXES**: 5 High + 9 Medium. All 5 Highs and 3 Mediums applied (reviewEligible derivation, Paper-status checklist row, Speaking interlocutorCard split, Writing country/wordCount handling, StepReview retry refresh, Listening canonical-shape gate, WizardShell aria-label). | 6 Medium follow-ups left (Reading time-limit per Part B vs C, bodyHtml sanitization, seeder transaction tightening, Enabled-default flip, accepted-synonyms case sensitivity, unused setReadingPart export). |
| 2026-05-08 | V2 Wave 5 — Remediation | Done | New `RemediationCatalog` resolves 18 weakness tags → drill IDs with day offsets; `RemediationPlanService` rebuilt with constructor DI on `(LearnerDbContext, IAiGatewayService, ILogger)` and `private const EnableAiPersonalisation = false` flag (locked by reviewer comment because current `RuleKind.Grammar` grounding is a structural workaround, not semantic — must be replaced with `RuleKind.Remediation/Mock` before flipping). New `AiFeatureCodes.MockRemediationDraft` (BYOK eligible). 5 catalog tests, all green. | Add dedicated `RuleKind.Remediation` + rulebook directory before any AI personalisation rollout. |
| 2026-05-08 | V2 Wave 6 — Speaking room + chunked recording | Done | Learner page `/mocks/speaking-room/[bookingId]` rewritten as audio-only state machine (`pre → rp1-prep 3min → rp1-speak 5min → rp2-prep 3min → rp2-speak 5min → submitting → done`) using `MediaRecorder.start(7000)` and sequential per-part upload via `inflightRef`. New `MockBookingRecordingService` enforces 8 MiB/chunk, 240 chunks/booking, 200 MiB/booking caps; SHA-256 hashing; `IFileStorage` write at `mock-bookings/{id}/chunks/part-{n:000}-{sha}.{ext}`; **idempotent retry handling** (same part+sha = no-op success; same part+different sha = `part_already_uploaded` reject). Manifest column widened to Postgres `text` (was VARCHAR(8000) — exceeded at ~38 chunks). New endpoints: learner `POST /v1/mock-bookings/{id}/recording-chunk?part=N` (`PerUserWrite` rate-limit + `RequestSizeLimit`), learner `POST /v1/mock-bookings/{id}/recording/finalize`, learner `GET /v1/mock-bookings/{id}` (interlocutor card stripped), expert `GET /v1/expert/mocks/bookings/{bookingId}` under `ExpertOnly` policy enforcing `AssignedTutorId == actorId`, admin `GET /v1/admin/mock-bookings/{id}`. New expert page `/expert/speaking-room/[bookingId]` exposes interlocutor cue cards. Hand-written migration uses Postgres `IF NOT EXISTS` + `ALTER TYPE` for safe partial-deploy widen. 3 booking-DTO tests covering leak guard / expert exposure / unassigned-403. | Add E2E smoke for chunked-upload retries; add per-day rate-limit for chunk POSTs above the global `PerUserWrite` (Medium). |
| 2026-05-08 | V2 Wave 7 — Diagnostic learner UI | Done | New `/mocks/diagnostic` Client Component (`getHeaders→ensureFreshAccessToken` is browser-only; mirrors sibling `app/mocks/*` pages) renders entitlement check via `fetchMockDiagnosticEntitlement` + diagnostic mock chooser via `fetchMockOptions` filtered by `mockType === 'diagnostic'` + study path via `fetchMockDiagnosticStudyPath`. Result rendered via `OetStatementOfResultsCard` only when `isMockReportStatementOfResultsReady(report)` returns true. | — |
| 2026-05-08 | V2 Wave 8 — Admin leak-report queue + randomisation helper | Done | New admin route `/admin/content/mocks/leak-reports` (status chips, table, action modal). New `MockService.ListLeakReportsAsync` (`AsNoTracking`, paged) + `UpdateLeakReportAsync` (status validation, terminal-state lock, `AuditEvent` on every mutation). PII guard in `ToLeakReportSummary` — only `displayName`, no email. New endpoints: `GET /v1/admin/mocks/leak-reports?status=&limit=` (`AdminContentRead`), `PATCH /v1/admin/mocks/leak-reports/{id}` (`AdminContentWrite`). Mocks list page got `<LeakReportsLink>` chip with cheap open-count badge. Separate deterministic `RandomisationHelper.SeededShuffle<T>(items, seed, saltKey)` (Fisher-Yates + FNV-1a salt) with 9 unit tests — **NOT yet wired** into Reading/Listening DTOs because current option storage is positional-array + letter-keyed answers; structural payload migration to option IDs required before grading-safe shuffling can be enabled. 4 leak-report endpoint tests, all green. | Wire structural option-ID migration in a separate wave; then enable shuffle on grading-side projection. |
| 2026-05-08 | V2 reviewer pass | Done | Independent `agency-reviewer` returned **APPROVED WITH FIXES**: 1 Critical + 2 High + 6 Medium/Low. All 3 blockers applied: (1) `RecordingManifestJson` widened to Postgres `text` (`MockEntities.cs`, snapshot, migration `ALTER TABLE ... TYPE text`); (2) `MockBookingRecordingService.AppendChunkAsync` now idempotent on `part` (same sha = no-op success, different sha = `part_already_uploaded` rejection); (3) `RemediationPlanService.EnableAiPersonalisation` demoted from `public const` to `private const` with explicit reviewer comment that flipping requires real `RuleKind.Remediation` rulebook. Also applied chunk endpoint Medium: `RequireRateLimiting("PerUserWrite")` + `RequestSizeLimit(MaxChunkBytes + 64KiB)` at boundary. | 6 Medium/Low items remain (test coverage for chunk dedup retries; admin leak-report list UI accessibility audit; W7 diagnostic UI Suspense boundary; W8 randomisation helper docs; remediation drill ID lookup vs canonical drill table; option-ID migration plan). |
| 2026-05-08 | V2 final validation | Done | `dotnet build backend/OetLearner.sln` (0 errors, pre-existing warnings only); `dotnet test --filter "MockBookingLearnerDto|MockLeakReportAdmin|RemediationCatalog|RandomisationHelper|MockV2EndpointTests"` → **26/26 pass**; `npx tsc --noEmit` → EXIT=0; scoped `npx eslint` on all 35+ touched files → EXIT=0. | Full `npm test` + `npm run lint` + `npm run test:e2e:smoke` before release. |

## Gap Register

| ID | Gap | Severity | Resolution |
| --- | --- | --- | --- |
| MOCK-GAP-001 | Admin mock UI cannot edit bundle metadata although backend supports it. | High | Resolved: typed API helper and edit modal added. |
| MOCK-GAP-002 | Admin mock UI cannot reorder sections although backend supports it. | High | Resolved: reorder helper and move controls added. |
| MOCK-GAP-003 | Section builder uses raw ContentPaper IDs. | Medium | Resolved: searchable published paper picker added while preserving manual ID fallback. |
| MOCK-GAP-004 | Backend can submit a multi-section mock after only one section. | Critical | Resolved: all required sections must be completed; optional sections no longer block submission. |
| MOCK-GAP-005 | Generic learner booking PATCH can mutate terminal status. | Critical | Resolved: learner-supplied `status` is rejected; reschedule/cancel remain dedicated endpoints. |
| MOCK-GAP-006 | Remediation generator expects stale `subtestScores` and hard-codes `350`. | High | Resolved: parses current `subTests` payload and uses `OetScoring` constants. |
| MOCK-GAP-007 | Pending/non-numeric subtests map to zero in Statement of Results adapter. | High | Resolved: readiness guard and provisional warning added before rendering SoR. |
| MOCK-GAP-008 | Launch/completion contract remains transitional and can still rely on client evidence. | Critical | **Resolved 2026-05-08:** All 4 subtest players (Listening, Reading, Writing, Speaking) now read `mockAttemptId`/`mockSectionId` from `MockService.BuildLaunchRoute` query params and POST `completeMockSection` with rawScore/maxRawScore/scaledScore/grade after grading. Speaking auto-marked path uses `awaitingTutorReview: true`; report aggregation can now trust per-section evidence instead of falling back to client-only state. |
| MOCK-GAP-009 | Speaking live-room transition ownership is still incomplete end-to-end. | High | **Resolved 2026-05-08:** Learner audio-only state machine + chunked recording + expert read-only room landed in V2 Wave 6. |
| MOCK-GAP-010 | Full diagnostic learner journey is not clearly surfaced. | Medium | **Resolved 2026-05-08:** New `/mocks/diagnostic` page wires entitlement → chooser → study path → SoR card (V2 Wave 7). |

## Validation Log

| Date | Check | Result | Notes |
| --- | --- | --- | --- |
| 2026-05-07 | Editor diagnostics | Passed | `get_errors` clean for touched TS/C# files. |
| 2026-05-07 | Focused frontend tests | Passed | `npx vitest run lib/adapters/oet-sor-adapter.test.ts lib/__tests__/api.test.ts` — 34 tests passed. |
| 2026-05-07 | Focused backend tests | Passed | `dotnet test backend/OetLearner.sln --filter FullyQualifiedName~MockV2EndpointTests` — 5 tests passed; existing warnings only. |
| 2026-05-07 | TypeScript | Passed | `npx tsc --noEmit`. |
| 2026-05-07 | Lint | Passed | `npm run lint`. |
| 2026-05-08 | TypeScript | Passed | `npx tsc --noEmit` after wizard + seeder + player wiring + reviewer fixes. |
| 2026-05-08 | Lint (scoped) | Passed | `npx eslint` over `app/admin/content/mocks/wizard/**`, `components/domain/mock-wizard/**`, `lib/mock-wizard/**`, and 4 player pages with `--max-warnings=0`. |
| 2026-05-08 | Vitest (scoped) | Passed | 10/10 in player suites (`app/listening/player`, `app/writing/player`). Covers `completeMockSection` mock + grading pipeline. |
| 2026-05-08 | Backend build | Passed | `dotnet build backend/OetLearner.sln` — 0 errors, 5 pre-existing warnings (unrelated to mocks). |

## Decisions

- Use `docs/mocks/PRD.md` and `docs/mocks/PROGRESS.md` instead of overwriting root `PRD.md` or `PROGRESS.md`.
- Treat existing code as the implementation source of truth where old V2 plan items are stale.
- Prioritize root-cause hardening and admin CRUD completion before larger diagnostic/live-room expansions.
- Keep mock reports practice-labelled and provisional until all required scoring/review evidence is final.

## Follow-Up Waves

1. Server-resolved section result adapters for Reading/Listening first, then Writing/Speaking review-aware aggregation.
2. Tutor/admin-owned live-room controls with learner read-only state transitions.
3. Dedicated diagnostic mock learner route and study-path handoff.
4. Expanded admin item analysis beyond Reading where reliable item-level data exists.
5. Booking reminders with notification caps and idempotent background jobs.
