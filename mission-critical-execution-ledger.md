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

---

## ElevenLabs TTS Integration â€” Listening Audio Backfill (2026-05-19)

**Branch/mode:** direct production VPS work (user-authorized: "DO EVERYTHING #LEAVE NOTHING BEHIND"). VPS `root@185.252.233.186`, compose project `oetwebsite`.

**Outcome:**

- ElevenLabs is now the PRIMARY TTS provider for the listening-audio backfill scripts (`scripts/admin/_lib.mjs` â†’ `aiTts()`). DigitalOcean Qwen3-TTS retained as automatic fallback when ElevenLabs is unavailable, returns non-200, or throttles.
- Active env: `ELEVENLABS__APIKEY=sk_7769â€¦` in `/opt/oetwebapp/.env.production` (UPPERCASE). Model `eleven_multilingual_v2`.
- Banner now prints: `TTS provider: ElevenLabs (PRIMARY) âœ“ key=sk_7769â€¦ model=eleven_multilingual_v2  fallback=DigitalOcean Qwen3`.
- `endRun()` now prints `TTS providers: ElevenLabs=N  DigitalOcean=N  fallbacks=N` so each run reports which provider produced audio.
- First end-to-end paper confirmed produces ElevenLabs MP3 (~4MB `audio/mpeg`) and attaches A1â€“C2 successfully.
- Paper `ace9585ac0974f52b27d453502352dc4` published with 5 distinct audio media assets (A1 legacy + A2/B/C1/C2 ElevenLabs).
- 4 previously archived listening papers unarchived â†’ 23 Draft listening papers now in sweep (`scripts/admin/_run_sweep.sh` launcher on VPS).

**Root causes resolved this wave:**

| # | RC | Resolution |
| --- | --- | --- |
| 1 | env case mismatch | Script reads both `Foo__Bar` and `FOO__BAR` forms |
| 2 | `parseFlags` only accepted space form (`--paper-id X`), not equals form | Documented; callers use space form |
| 3 | `JsonSerializerOptions.ReferenceHandler.IgnoreCycles` regression on POST body | Resolved upstream; script now uses numeric enum body |
| 4 | Build overlay caused feature branch CS0234 | Reverted; clean image green slot `oetwebsite-learner-api:local` sha256 `2d7c6d9c1fd95243e7253e9e785bfe0b11b7a8751ddd2a102bbd8e3fdd63eeb5` |
| 5 | Router `ACTIVE_SLOT=blue` pointed at stale slot | `/opt/oetwebapp/.deploy/active-slot.env` updated to `green` |
| 6 | 413 on chunked upload through nginx router | `_patch_413.sh` reapplied â€” note: reverts on router recreate |
| 7 | Asymmetric JSON enum binding in deployed image | **Permanent client-side workaround:** GET response serializes `PaperAssetRole` as string (`"Audio"`, `"AudioScript"`, etc.); POST refuses strings with 400-empty-body and requires numeric (`0`,`1`,`2`,`3`). Scripts now use string match for reads, numeric for writes. No backend rebuild planned. |
| 8 | ElevenLabs key was loaded but banner did not surface which provider was active | Banner + `endRun` patched to print provider counter |
| 9 | JSON object cycle exception on `GET /v1/admin/papers/{id}` returning HTTP 500 (`Possible object cycle detected... reference loop`) | **DEPLOYED 2026-05-19**: surgically patched `backend/src/OetLearner.Api/Program.cs` (anchor: `builder.Services.AddProblemDetails();`) to add global `ConfigureHttpJsonOptions` with `ReferenceHandler.IgnoreCycles`. Rebuild dance: required both compose files (`-f docker-compose.production.yml -f docker-compose.production.build.yml`) and target the SLOT service (`learner-api-green`), NOT the router (`learner-api` = nginx). First attempt hit phantom stale-cache CS0234; resolved by `docker builder prune` + `--no-cache` on the parallel rebuild. New image `oetwebsite-learner-api:local` deployed to active green slot. **Smoke verified**: GET on previously-500ing paper id `cd17cff04b9d40e486035bf2f90411ea` now returns HTTP 200 with clean JSON; no `JsonException`/cycle entries in fresh `oet-api-green` logs. Patcher script: `scripts/admin/_patch_program_cycle.py` (idempotent). |

**Asymmetric enum binding constants (for any future script):**

```js
const PAPER_AUDIO_ROLE = 'Audio';            // GET response form (string)
const PAPER_AUDIOSCRIPT_ROLE = 'AudioScript';
const PAPER_AUDIO_ROLE_NUM = 0;              // POST body form (numeric)
// Numeric enum: Audio=0, QuestionPaper=1, AudioScript=2, AnswerKey=3
// ContentPapers.Status: 0=Draft, 4=Published, 6=Archived
```

**PowerShell + ssh quoting pitfalls observed:**

- `\$!` inside a double-quoted `ssh "â€¦"` arg is consumed by PowerShell and reaches the remote shell as a literal `$!`. Solution: ship a small shell script (e.g., `scripts/admin/_run_sweep.sh`) via `scp` and invoke it with `ssh "bash â€¦"`.
- SQL with `\"` quoting fails in PS. Solution: `scp` a `.sql` file â†’ `docker cp` into the postgres container â†’ `docker exec psql -f /tmp/file.sql`.

**Operational reminders carried forward:**

- ElevenLabs master key `sk_7769e7e60686c6188b2ea38822d0156dbb9dcc6c2c0000a0` was shared in chat. **Rotate after this backfill ships.**
- Postgres user/db are both `oet_learner` (UNDERSCORE).
- `service learner-api` = nginx router. Code lives in `learner-api-blue` / `learner-api-green` slots.
- Sweep script uses CLI flag `--paper-id <id>` only; `ONLY_PAPER` env is ignored.
- `node --env-file=.env.production` requires UPPERCASE keys in that file.


---

## 2026-05-20 04:00Z — ElevenLabs backfill HALT (quota exhausted)

**Halt cause:** ElevenLabs key `sk_7769e7e60686c6188b2ea38822d0156dbb9dcc6c2c0000a0` returns `401 quota_exceeded` — has a 131,000-credit/month cap (was advertised in chat as `unlimited`; the API does not enforce that). Remaining at halt: **62 credits**.

**Sweep outcome (PID 1296987 killed cleanly):**
- 10 papers fully published this run on 100% ElevenLabs (50 ElevenLabs MP3 parts).
- Paper 11 `dabaf1c3067542168080c04587086a16` (nursing-set-026) hit quota during part 4: A1/A2/B attached (ElevenLabs), C1 attached as DO fallback (6.2 MB, audio/mpeg), C2 never attached. Sweep killed before publish call. **DO-fallback row + asset purged via `scripts/admin/_clean_paper11_c1.sql`**; paper now sits at Status=0 with 3 clean ElevenLabs parts (A1/A2/B).

**Honest DB inventory (post-halt, post-scrub):**
- 39 total listening papers; 26 Published, 13 Draft.
- Of 26 Published: 24 fully complete (5 audio parts), 1 missing Part B (`nursing-standard-set-002`), 1 with only Part A1 audio from a pre-ElevenLabs era (`speech-pathology-standard-set-011` — published with 4/5 audio missing; pre-existing bug, not caused by this sweep).
- Of 13 Draft: 11 zero-audio (untouched), 2 partial (`nursing-standard-set-026` 3/5, `dentistry-hard-set-003` 3/5).

**Credit math to finish:**
- 11 zero-audio × 5 parts + 2 partial × 2 parts + 1 broken-published × 4 parts = **~63 part-TTS calls ≈ 164,000 credits** at ~2,600 chars/part. Exceeds the 131,000/mo cap — cannot complete in a single billing cycle on this tier even with a quota reset.

**Resume conditions (any one unblocks):**
1. New ElevenLabs key on Pro/Scale/Business tier with ≥200k monthly credits, OR
2. Explicit owner authorization to use DO/Qwen fallback for remaining 14 papers (violates earlier `100% ElevenLabs` directive), OR
3. Wait for monthly reset on this key (still under-budget — could finish ~7 of 13 drafts in next cycle).

**Resume command (when key replaced in `/opt/oetwebapp/.env.production`):**

`ssh root@185.252.233.186 bash /opt/oetwebapp/scripts/admin/_run_sweep.sh`

Sweep script auto-detects missing parts per paper and only TTSes what's missing, so the partial drafts will only consume credits for their remaining parts.

**Outstanding manual decisions (owner-only):**
- `speech-pathology-standard-set-011` is Status=4 with only A1 audio. Either unpublish (4→0) for completion later, or accept as published-with-defect.
- `nursing-standard-set-002` is Status=4 missing Part B. Either backfill Part B once quota returns, or accept as published-with-defect.

**Key rotation:** `sk_7769e7e60686c6188b2ea38822d0156dbb9dcc6c2c0000a0` is shared in chat AND exhausted. Revoke regardless of quota plan — replace with a fresh key in `/opt/oetwebapp/.env.production` (key `ELEVENLABS__APIKEY`).
