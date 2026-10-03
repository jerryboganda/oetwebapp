---
name: "CI Validation Ladder"
description: "Use when validating changes in the OET repo: builds, tests, installs, lint, type-checks, Playwright, dotnet, or scripts. Defines where validation runs (GitHub Actions only) and which workflow runs each check."
applyTo: "package.json,pnpm-lock.yaml,Dockerfile,docker-compose*.yml,backend/**,app/**,components/**,contexts/**,hooks/**,lib/**,tests/**,playwright*.config.ts,vitest.config.ts,scripts/**"
---

# CI Validation Ladder

Compute runs only on GitHub Actions — see `AGENTS.md` § "GITHUB ACTIONS IS THE ONLY AUTHORIZED
COMPUTE ENVIRONMENT". Do not run builds, tests, lint, type-checks, Playwright, dotnet or Docker on
the local machine or the production VPS.

## Local (the only allowed commands)

```powershell
pnpm run ship               # ONE command per ship (lock -> rebase -> gate -> visibility lease
                            # -> push -> watch Deploy production -> ax:record); multi-agent safe
pnpm run ship:gate          # the seconds-long static gate inside that flow
pnpm run ship:watch         # watch Deploy production for a SHA on its own (supersede-aware)
pnpm run ship:self-test     # ship wrapper self-test (also runs in ax-check.yml, linux + windows)
pnpm run pipeline:check     # CI/CD contract: single pull-only rollout, no automated browser lanes,
                            # path-filtered builds, rollout gates intact (runs in the guards job too)
pnpm run ax:check           # validate the state ledger (static; fails on a gate with no evidence)
pnpm run ax:status          # read the current run's goal, gates and next action
pnpm run ax:next            # pick the next ready task from TASKS.json
pnpm run ax:record          # after a green deploy: real run ids -> VERIFICATION.md (uses gh)
pnpm run ax:verify          # re-check recorded run ids against GitHub (uses gh)
```

`ax:check` is static, like `ship:gate`. `ax:record` and `ax:verify` make read-only `gh` calls;
like `ship:watch` they are local tooling, not compute. See `scripts/agent/README.md`.

## CI (push the branch or `gh workflow run qa-smoke.yml --ref <branch>`)

| Check | Workflow / job |
| --- | --- |
| `pnpm exec tsc --noEmit`, `pnpm run check:encoding` (report-only), `pnpm run lint`, `vitest run`, `pnpm run build` | `qa-smoke.yml` / `frontend-unit` |
| `dotnet test` (6 shards, Postgres/pgvector, NuGet-cached) | `qa-smoke.yml` / `backend-tests` |
| Placement entry contracts | `qa-smoke.yml` / `placement-entry` |
| ~~Playwright/e2e~~ | **Removed by owner directive 2026-10-03 (hard rule).** No e2e job runs on any trigger; `tests/e2e/**` is a manual tool. Bugs are reported by the owner and fixed on demand. |
| Pending EF model changes, gitleaks (path-filtered) | `speaking-ci.yml` / `migrations-check`, `secrets-scan` |
| Images → GHCR + migration SQL artifact. Runs only for pushes touching a build input; rebuilds only the changed component (the rest are retagged from `:latest`); parallel per SHA, no cross-SHA lock | `build-images.yml` (push to `main` only) |
| Migrations apply + blue/green rollout with health gate (serialized `production-deploy`); stands down when superseded or when the SHA has no images; dispatch with `sha` = rollback | `production-deploy.yml` |
| Android / iOS builds | `mobile-ci.yml` |
| Tauri desktop (fmt, clippy, cargo test) | `tauri-ci.yml` |

Ship-it default is `pnpm run ship` (which runs `ship:gate` internally). Never treat "pushed" as done.
A push touching no build input starts no build and no rollout at all; the watcher reports
`SHIP-WATCH_NOTHING_TO_DEPLOY` and exits 0 — production is legitimately unchanged.

## Scope & safety

- Choose validation by risk: docs-only changes need no CI run; behavior changes need the matching
  CI job to be green; broad refactors warrant the full `qa-smoke.yml` run.
- Never claim a check passed without a GitHub Actions run behind it. Report the workflow, run, job
  and step, what did not run, and any remaining risk.
- Never record a gate as `PASS` without evidence. `SESSION_STATE.md` accepts a run id, a workflow
  file, or `local:<command>`; `pnpm run ax:check` fails anything else. A check that was genuinely
  not run is recorded as `NOT RUN` — that is honest and passes.
- `VERIFICATION.md` is machine-written by `pnpm run ax:record`. Never hand-edit a result; re-check it
  with `pnpm run ax:verify`.
- The VPS is deploy-only. Storage persistence, protected volumes, and production container rules are a
  deployment/runtime invariant — see `deployment.instructions.md`.
- Get approval before destructive, networked, production, or credential-adjacent commands.
