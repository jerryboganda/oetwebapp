# Mission-Critical Execution Ledger

Date: 2026-04-26  
Branch: `codex/mission-critical-a-z-cleanup`  
Mode: local/GitHub-only. No production VPS deployment, no production Docker commands, no Nginx Proxy Manager changes.

## Safety Baseline

- Preserved pre-existing dirty worktree on branch `wip/pre-mission-critical-2026-04-26`.
- Preservation commit: `9e5a76e chore(wip): preserve pre mission-critical dirty tree`.
- Clean implementation branch created from `main`: `codex/mission-critical-a-z-cleanup` at `cce5ab2`.
- All pre-existing stashes were converted to `wip/stash-*` branches or dropped if empty; `git stash list` now reports no stashes.

## Baseline / Tool Outputs

- Initial `npx ts-prune -p tsconfig.json | node scripts/ts-prune-filter.mjs`: 409 actionable / 1117 reported.
- Post-filter-tuning `npx ts-prune -p tsconfig.json | node scripts/ts-prune-filter.mjs`: still actionable; root Next framework files are now filtered, but broad product-source cleanup remains too large for a safe single commit.
- Initial `npx --yes knip --reporter compact`: reports unused files, dependencies, exports, exported types, and duplicate exports; many are tooling/native/build artifacts and require config triage rather than blind deletion.

## Task Status

| # | Task | Status | Notes |
| --- | --- | --- | --- |
| 1 | Tech-debt cleanup wave 1b | Partially complete | Captured tool output; tuned ts-prune filter for root framework files. Large remaining source-export cleanup remains queued for separate safe batches. |
| 2 | Barrel file consolidation | Policy documented | `AGENTS.md` now requires direct imports by default and no new re-export-only barrels. Existing broad barrel codemod is deferred to avoid unsafe churn in this combined branch. |
| 3 | Replace ad-hoc fetch | Substantially complete | Added `apiClient.get/post/put/patch/delete/postForm`; migrated backend API fetch callsites found in app/components/lib/hook scan; documented exceptions. |
| 4 | Motion presets | Partially complete | Loaded motion-system; replaced admin marketplace inline item/collapse motion with shared primitives; reduced-motion helpers already covered by `lib/motion.test.ts`. |
| 5 | Backend service-layer audit | Deferred/bounded | No Copilot landed split source was present on this branch. Conversation work was kept bounded; full >400 LOC split should be a separate backend-only series. |
| 6 | Auth audit M3 | Blocked | No repo-backed title/repro found. Not invented. |
| 7 | Auth audit M5 | Blocked | No repo-backed title/repro found. Not invented. |
| 8 | Auth audit L2 | Blocked | No repo-backed title/repro found. Not invented. |
| 9-14 | Sprint-2 H1/H4/H5/H6/H7/H14 | Blocked | No canonical title/acceptance criteria found in `docs/SPRINT-STATUS.md`; not invented. |
| 15 | Conversation module phase-2 | Implemented | Added resume token entity/migration, canonical + compatibility resume endpoints, txt/pdf transcript export through `IFileStorage`, ASR diarization contracts/provider flags, UI resume/export, and E2E spec. |
| 16 | Sprint-3 planning | Complete pending sign-off | Added `docs/SPRINT-3-STATUS.md` with roadmap scoring and acceptance criteria. |
| 17 | Sprint-4 planning | Placeholder complete | Added `docs/SPRINT-4-STATUS.md`; real planning waits until Sprint 3 ships. |
| 18 | Staging environment stand-up | Local artifacts complete | Added `docker-compose.staging.yml`, `.env.staging.example`, guarded workflow skeleton, and `docs/STAGING-LOCAL-GITHUB-PLAN.md`. |
| 19-21 | PR triage #2/#3/#4 | Complete locally | Added `docs/PR-TRIAGE-2026-04-26.md` with request-changes decisions. |
| 22 | Stash prune | Complete | Non-empty stashes preserved as `wip/stash-*` branches; empty stash dropped; zero stashes remain. |
| 23 | AGENTS.md doc-sync | Complete | Updated routes/test/backend endpoint/admin permission counts plus direct-import/API/staging rules. |
| 24 | SoR card lock check | Complete | No card diff; SoR tests passed. |
| 25 | DigitalOcean key rotation | User-only | Documented as user-only; no secret rotation attempted. |
| 26 | OpenCode Desktop restart | User/local-interactive | Not automated; desktop restart/provider validation remains user-owned. |

## Verification Log

- `npx tsc --noEmit` - passed.
- `cmd /c npm test -- lib/__tests__/api.test.ts app/conversation/page.test.tsx` - 2 files / 23 tests passed.
- `cmd /c npm test -- components/domain/OetStatementOfResultsCard.test.tsx lib/adapters/oet-sor-adapter.test.ts` - 2 files / 29 tests passed.
- `cmd /c npm test` - 113 files / 675 tests passed.
- `cmd /c npm run backend:test` - 601 backend tests passed.

## Speaking Module Plan (docs/SPEAKING-MODULE-PLAN.md) — 2026-04-30 closeout

All seven waves complete. Final gate evidence (TRX `<Counters/>`):
`total="815" executed="815" passed="815" failed="0" error="0"`.

| Wave | Headline artefacts |
| --- | --- |
| 1 — Stable criterion-keyed feedback contract | `OetScoring.SpeakingProjectedScaled`, `SpeakingReadinessBand` enum, 9-criterion summary projection. |
| 2 — Interlocutor card + 3-min prep + 5-min timer | `InterlocutorCard` entity, learner-side projection that strips it, prep/roleplay timer enforcement. |
| 3 — Speaking mock set | `SpeakingMockSet` entity, paired-attempt orchestrator routes, admin CRUD page. |
| 4 — Tutor calibration + transcript comments | `SpeakingCalibrationSample`/`Score`, `SpeakingFeedbackComment`, drift dashboard, expert calibration page, inline-comments UI. |
| 5 — AI patient deep-link | `LearnerService.SpeakingSelfPractice.cs` partial, `POST /v1/speaking/tasks/{id}/self-practice` (delegates to `ConversationService.CreateSessionAsync`, **no new AI provider**), shared `SpeakingSelfPracticeButton` on task + results pages. |
| 6 — Drills bank + pathway | 6 seeded `speaking_drill` `ContentItem`s (schema-free encoding via `ContentType`+`ScenarioType`), `LearnerService.SpeakingDrills.cs`, `GET /v1/speaking/drills?kind=&profession=&criterion=`, `app/speaking/drills/page.tsx`, "Speaking Foundations" callout in Learning Paths. |
| 7 — Compliance polish | `SpeakingComplianceOptions` (consent + disclaimer + 365-day retention bound to `Speaking:Compliance`), `speaking_recording_accessed` `AuditEvent` on every tutor audio stream, `GET /v1/speaking/compliance`, always-on `SpeakingScoreDisclaimer` banner on results, `SpeakingAudioRetentionWorker` `BackgroundService` mirroring `ConversationAudioRetentionWorker`. |

Verification commands run from repo root and `backend/`:

- `npx tsc --noEmit` → 0 errors.
- `npm run lint` → 0 errors / 0 warnings.
- `dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj` → 815/815 passed (TRX `<Counters/>` confirms 0 failed / 0 error).

## Final Count Evidence

- Routes: 241 (`app/**/page.tsx`).
- Vitest unit test result: 113 files / 675 tests.
- E2E spec files: 33 (`tests/e2e/**/*.spec.ts`).
- Backend endpoint map calls: 686 (`MapGet/MapPost/MapPut/MapDelete` in backend endpoint files).
- Admin permissions: 16 (`AdminPermissions.All`).

## Writing Rule Enforcement Update — 2026-05-07

Two previously-unenforced OET Writing rules are now active. Both reverse prior "no enforcement" stances without changing AI scoring behaviour.

### R03.8 — Word count 180–200 (ADVISORY)

- **Status:** advisory only (soft warning); does not block learner submission.
- **Source of truth:** per-profession `rulebooks/writing/<profession>/rulebook.v1.json`, rule `R03.8`, `params.min` / `params.max`.
- **Lint detector:** `lib/rulebook/writing-rules.ts` `letter_body_length` now reads `rule.params` and emits a finding when the body word count falls outside `[min, max]`. Reverses the prior "intentional no length judgment" decision recorded in that file.
- **UI:** `app/writing/player/page.tsx` shows a live word-count `Badge` driven by the same min/max from the active rulebook.
- **AI scoring:** unchanged — no length penalty is applied inside the AI gateway scoring path. Length is surfaced as a rulebook lint advisory, not a scaled-score deduction.

### Profession ↔ letter-type matching (PUBLISH GATE)

- **Status:** hard gate. Publish is rejected when `paper.LetterType` is not in the allowed set for `paper.ProfessionId`.
- **Enforcement point:** `WritingContentStructure.Validate(paper)` in the backend rulebook engine.
- **Data:** `AllowedLetterTypesByProfession` dictionary (data-driven matrix). Default-permissive: unknown profession ids are not blocked, so seeding new professions does not break publish until they are added to the matrix.
- **Scope:** publish gate only. Existing already-published papers are not retroactively invalidated; AI scoring is unaffected.
