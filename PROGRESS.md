# PROGRESS — Durable Checkpoint Ledger

Last updated: 2026-10-04

## How this file works

- Compact durable checkpoints only, newest first. One entry ≤ 10 lines, ideally with the real
  GitHub Actions run id that proved it.
- **This is not the current run's memory.** That is `SESSION_STATE.md` + `TASKS.json`.
  Verified evidence is `VERIFICATION.md` (machine-written — never hand-edit it).
- Do not paste historical ledgers here. Verbatim history lives in
  `docs/PROGRESS-ARCHIVE-2026.md`; older still is `git log -- PROGRESS.md`.

## Active checkpoint - Deployment acceleration, measured free-runner target misses (2026-10-04)

- Wrote immutable component reuse, same-publish SQL/Writing references, persisted caches, lean contexts and health/config-aware service reuse; bound paired router recovery/serving identity remain enforced.
- Added inclusive ship timing, actual-promotion/no-op proof and offline provenance/driver/watcher/registry regressions.
- Existing owner authentication is verified process-scoped; shared CLI defaults/credentials remain unchanged.
- SHA 66458ca821: Build images 37170176749 passed 31 protocol checks and 26 real Writing tests; production 37170535406 served exact images in green.
- Inclusive push-to-verified-live was 744.784 seconds, not 300; cold build-only benchmark 37170735127 passed in about 322 seconds without promotion.
- Follow-up f13b93bd4: build 37172887015 passed 37 protocol tests, native cache persistence and complete runtime bytes; deploy 37173143817 served blue at 569.457 seconds inclusive (driver 543.109).
- AX 37174084749 passed actual-source cases on native Windows 5.1/Linux 7.6.6; forced-Writing benchmark 37173946904 passed 3/666 evaluated sources and 26 tests in 310 seconds build-only.
- Genuine changed-web/reused-API e96: build 37174218390 passed, deploy 37174402463 verified green at 582.880 seconds inclusive (driver 528.521); Next compile 5.7s, image pull 91.524s, native readiness 94.649s.
- Corrected web-layer release 0689edfd0: build 37176154885 passed 38 tests and actual byte/owner assertions; deploy 37176403559 verified blue at 529.552s inclusive (driver 491.713s). Cold Next 89s, pull 54.659s, reused-API readiness 14.482s.
- Frontend QA 37176154904 passed; preserved backend fixtures still need one forced evidence run. Target unmet and full cold/Writing-live unmeasured; private restoration faces the inconsistent empty May queue.

## Previous checkpoint — AX: externalized agent working memory (2026-10-01)

- `.github/agent-state.local.md` was named as the current-task handoff in 21 files but did not
  exist and is gitignored, so every "read the handoff" gate pointed at nothing.
- Replaced with a tracked `SESSION_STATE.md` ledger + `TASKS.json` queue + machine-written
  `VERIFICATION.md` evidence index, driven by `scripts/agent/state.mjs` (`pnpm run ax:*`).
  The old path is now the gitignored raw evidence journal.
- `pnpm run ax:check` fails a `PASS` gate with no run id, workflow file or `local:<command>`,
  so a gate can no longer be ticked without evidence.
- Retired the stale local-Docker validation rule from the `.github/agents/**` and
  `.codex/skills/**` surfaces; they now point at the single Actions-only ladder instead of
  restating it, which removes the drift class.
- `PROGRESS.md` compacted; the retrospective checkpoints moved verbatim to
  `docs/PROGRESS-ARCHIVE-2026.md`.

## Previous checkpoint — Writing grading failure recovery

- Fixed misleading exemplar progress and failure visibility without refresh; focused UI/backend Actions checks passed.
- Diagnosed Codex HTTP 404: seeded root BaseUrl posts `/chat/completions`, while the sidecar accepted only `/v1/chat/completions`. The shared handler accepts both routes; red/green protocol evidence is in runs `36785645083` / `36785869736`.
- Fixed the large-prompt Codex argv transport with the existing stdin path; offline red/green runs `36814911854` / `36815163321`, including actual-image CLI checks with no network or inference. Sidecars at `2be1986a5385b83c4702e1869e5d1811c9ace5c1` are Healthy; credential volumes preserved.
- Preserved concurrent Speaking PR #305. Main deploy `36824151971` succeeded at `ed834765d4c6548e778f3cc52ad3a50503800e35` with exact web/API image tags and public health gates.
- Real Codex-only recovery passed in `36825878639`, job `110251344304`: same QA submission `8af5d137-6f43-4e28-b278-af936d6bf165`, saved grade `b9cd9019-e6e3-43f7-a34f-0009fd6bea62`, visible six-criterion report `4e1b550c-1e1d-41f3-bb00-122a1226d4e2`. Already-graded retry reused the grade with no new provider call; prior `auto` mode restored. Scoped frontend/backend/compile/canonical gates passed.
- User authorized one subscription-only QA assessment and incident-specific Jev waiver; paid Writing APIs remain forbidden. Original user submission untouched.

## Older checkpoints

Verbatim, newest first: `docs/PROGRESS-ARCHIVE-2026.md`
(UBAG provider board → FINAL Speaking brief → AI Packages conformance → Master Catalogue wave 1 →
Antigravity integration → Firebase OTP → answer-key reports → Atlas/Reading waves → billing checkout
→ AI packages → the 2026 PR-#38 portfolio work).
Older still: `git log -- PROGRESS.md`.

## Next-Step Protocol For New Agent Runs

1. Read `AGENTS.md`, `.github/copilot-instructions.md`, `SESSION_STATE.md`, `TASKS.json`, this file, and the domain doc for the surface you touch.
2. Non-trivial work: `pnpm run ax:status`, then continue from `SESSION_STATE.md` when its Goal matches the newest request; otherwise re-goal it with `pnpm run ax:init`.
3. Pick work with `pnpm run ax:next`.
4. Compute (build / test / lint / typecheck) runs on GitHub Actions only. `pnpm run ship:gate` and `pnpm run ax:check` are the only local checks — see `.github/instructions/validation.instructions.md`.
5. Production deploy uses GitHub Actions + GHCR images; never build on the VPS.
6. After this SHA's deploy is green: `pnpm run ax:record`, then `pnpm run ax:verify`, then update `SESSION_STATE.md` with validation, blockers and the next concrete step.

## Active Risks

- `SESSION_STATE.md` and `TASKS.json` are tracked and rewritten per task, so two parallel agent sessions can conflict. Take the newer `Updated:` block wholesale rather than hand-merging; `PROGRESS.md` remains the merge-safe durable ledger.
- State enforcement is warn-only inside `ship:gate` by design, so a session can still push a stale ledger. `pnpm run ax:verify` is what makes a false evidence claim detectable after the fact.
- Never stage unrelated untracked paths; `git add` explicit paths only.
