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
pnpm run ship:gate          # REQUIRED before every main push (seconds, static checks only)
pnpm run ship:watch         # REQUIRED after every main push (watches Build & Deploy only)
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
| `dotnet test` (6 shards, Postgres/pgvector) | `qa-smoke.yml` / `backend-tests` |
| Playwright smoke (one job per project) | `qa-smoke.yml` / `e2e-smoke` |
| Placement entry contracts | `qa-smoke.yml` / `placement-entry` |
| Pending EF model changes, gitleaks | `speaking-ci.yml` / `migrations-check`, `secrets-scan` |
| Web + API images, migrations, blue/green deploy | `deploy.yml` (push to `main` only) |
| Android / iOS builds | `mobile-ci.yml` |
| Tauri desktop (fmt, clippy, cargo test) | `tauri-ci.yml` |

Ship-it default is `ship:gate` only. Never treat "pushed" as done.

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
