# SESSION STATE

Session: ax-session-state-ledger
Goal: Externalize agent working memory into a tracked, machine-checked ledger (SESSION_STATE.md + TASKS.json + VERIFICATION.md)
Mode: execute
Updated: 2026-10-01T18:59:39Z
Branch: main
HEAD: e7d547e5d

<!--
The current run's working memory. This is layer 2 of three:
  1. AGENTS.md / .github/instructions/**  permanent rules
  2. SESSION_STATE.md + TASKS.json         this run        <- you are here
  3. VERIFICATION.md / git / Actions runs  objective truth

Rules
- Header keys are required, and `Mode` must be plan | execute | verify | blocked | done.
- The seven H2 sections below are required and the order is load-bearing
  (`pnpm run ax:check` enforces it).
- Never tick a gate without evidence. A `PASS` row needs a run id, a workflow
  file, or `local:<command>`. Record real runs with `pnpm run ax:record`.
- Keep it short. It is working memory, not a history file.
- Two sessions writing this at once? Take the newer `Updated:` block wholesale —
  do not hand-merge. The durable, merge-safe ledger is PROGRESS.md.
-->

## Objective

Build the missing middle layer of the repo's agent-state model. `.github/agent-state.local.md`
was named as the current-task handoff in 21 files but did not exist and is gitignored, so every
"read the handoff" gate pointed at nothing. Replaced it with a tracked `SESSION_STATE.md` ledger,
a `TASKS.json` queue and a machine-written `VERIFICATION.md` evidence index, and retired the
stale local-Docker validation rule that the agent/skill surfaces still carried.

## Acceptance criteria

- [x] AC-1 `pnpm run ax:self-test` and `pnpm run ax:check` pass.
- [x] AC-2 `ship:gate --self-test` still prints `ship-gate self-test OK`; `--ci` behaviour unchanged.
- [x] AC-3 No dangling `agent-state.local.md` pointer remains in any instruction surface.
- [x] AC-4 `PROGRESS.md` is compact, and stale compute-rule contradictions are removed.
- [ ] AC-5 Build & Deploy for this SHA is green with live health confirmed (Actions only).

## Decisions (do not revisit)

- D-1 Tracked `SESSION_STATE.md` is the canonical ledger; `.github/agent-state.local.md`
  becomes the machine-written, gitignored raw evidence journal. Reversing costs the
  21-file repoint.
- D-2 `ship:gate` only ever **warns** about state; the hard failure lives in `ax:check`.
  Reversing risks `deploy.yml`'s `syntax-gate`, which runs `--self-test` then `--ci`.
- D-3 `VERIFICATION.md` is machine-written only — `ax:record` from real `gh` data. A
  hand-edited evidence row defeats the point of the file.
- D-4 No merge driver on the ledger files: `merge=ours` needs a one-time global `git config`
  on every machine and fails silently without it.
- D-5 Historical records citing the old path (`docs/releases/**`) are left untouched; the
  file still exists, so the citations remain true.
- D-6 The agent/skill surfaces now point at the one canonical Actions-only validation ladder
  instead of restating it — `docker exec` and host `pnpm` copies had drifted apart and both
  contradicted `AGENTS.md`.

## Touched files

| Path | Change |
| --- | --- |
| `scripts/agent/state.mjs` | new |
| `scripts/agent/session-state.template.md` | new |
| `scripts/agent/README.md` | new |
| `SESSION_STATE.md` | new |
| `TASKS.json` | new |
| `VERIFICATION.md` | new |
| `docs/PROGRESS-ARCHIVE-2026.md` | new |
| `AGENTS.md` | edit |
| `PROGRESS.md` | edit (compacted) |
| `PRD.md` | edit (superseded banner) |
| `package.json` | edit (ax:* scripts) |
| `scripts/ship/pre-push-gate.mjs` | edit (local-only advisory + tripwire) |
| `.github/copilot-instructions.md` | edit |
| `.github/instructions/{agentic-workflow,validation,deployment}.instructions.md` | edit |
| `.github/prompts/*.prompt.md` | edit (6) |
| `.github/agents/*.agent.md` | edit (19) |
| `.codex/skills/*/SKILL.md` | edit (9) |
| `docs/README.md`, `docs/dev/lessons-learned.md`, `docs/READING-UPLOAD-AGENT-HANDOFF.md` | edit |
| `scripts/README.md`, `.github/ISSUE_TEMPLATE/copilot-task.md` | edit |

## Verification gates

| Gate | Command / workflow | Evidence | Result |
| --- | --- | --- | --- |
| ax-self-test | pnpm run ax:self-test | local:ax:self-test | PASS |
| ax-check | pnpm run ax:check | local:ax:check | PASS |
| ship-gate | pnpm run ship:gate | local:ship:gate | PASS |
| ship-gate-self-test | node scripts/ship/pre-push-gate.mjs --self-test | local:ship-gate-self-test | PASS |
| ship-gate-ci-unchanged | SHIP_GATE_CI=1 node scripts/ship/pre-push-gate.mjs --ci | local:ship-gate-ci | PASS |
| deploy | deploy.yml | NOT RUN | NOT RUN |

## Blockers

- None.

## Next action

1. Push `main`. The repo is **already public** (another session is working the same window), so
   leave visibility alone and flip to private only once no other run still needs it.
2. `pnpm run ship:watch` until Build & Deploy for this SHA is green, then `pnpm run ax:record`
   followed by `pnpm run ax:verify`.
