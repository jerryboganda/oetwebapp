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
pnpm run ship -- --sha <sha> # verified recovery under the same lease/gate/evidence rules
pnpm run ax:check           # validate the state ledger (static; fails on a gate with no evidence)
pnpm run ax:status          # read the current run's goal, gates and next action
pnpm run ax:next            # pick the next ready task from TASKS.json
pnpm run ax:record          # after a green deploy: real run ids -> VERIFICATION.md (uses gh)
pnpm run ax:verify          # re-check recorded run ids against GitHub (uses gh)
```

`ax:check` is static, like `ship:gate`. `ax:record` and `ax:verify` make read-only `gh` calls;
like `ship:watch` they are local tooling, not compute. See `scripts/agent/README.md`.

## CI (no automated QA - owner directive 2026-10-06)

The owner tests the live product by hand and reports bugs; the agent fixes them on demand. No workflow runs unit, integration, e2e, smoke, accessibility, visual, performance, load or conformance checks, and the contract checker (`pnpm run pipeline:check`, run by `ship:gate` and the `guards` job) fails if one is added. Never claim a test, lint or typecheck passed.

| Check | Workflow / job |
| --- | --- |
| Pending EF model changes, gitleaks (path-filtered) | `speaking-ci.yml` / `migrations-check`, `secrets-scan` |
| Immutable images → GHCR. Filtered main build/deployment inputs; compare a successful ancestor manifest, compile only changed components and reuse verified digests. API publish generates SQL/references without a second compile | `build-images.yml` |
| Deployment protocol regressions, shell syntax, PowerShell parser and single-pipeline/compute contracts | `build-images.yml` / `guards` |
| Guarded cold-cache build measurement (`benchmark=true`, `rebuild_all=true`): no image push or deploy. Force real required Writing tests with `writing_gate=true` | `build-images.yml` / manual dispatch |
| Verify original-source SQL artifact, apply when API is not proven deployed, then bound prepare/promote with durable router reload/public serving proof. Only successful descendant main builds supersede; dispatch a proven deployed `sha` for rollback | `production-deploy.yml` |

Ship-it default is `pnpm run ship` (which runs `ship:gate` internally). Never treat "pushed" as done.
A release cannot skip the watcher, physical serving proof, evidence or guarded
visibility cleanup. The standalone PowerShell watcher is an implementation detail
of the wrapper, not an alternate production path. Follow `AGENTS.md`'s mandatory
accelerated baseline; preserve actual applicable Writing checks and independent QA.
A push touching no build input starts no build and no rollout at all; the watcher reports
`SHIP-WATCH_NOTHING_TO_DEPLOY` and exits 0 — production is legitimately unchanged.

Report inclusive before-first-push-attempt to verified-live elapsed separately from
workflow creation proxies and the VPS `DEPLOY_LIVE` timestamp. A successful
stand-down is not a deployment. Cold/queue/Writing time and unmeasured categories
remain visible; never declare the 300-second target met from an unverified health response.

## Scope & safety

- There is no CI validation to choose: the build compiles, the owner tests manually and reports bugs.
- Never claim a test, lint or typecheck passed (none run anywhere); claim a build only with a GitHub Actions run behind it. Report the workflow, run, job
  and step, what did not run, and any remaining risk.
- Never record a gate as `PASS` without evidence. `SESSION_STATE.md` accepts a run id, a workflow
  file, or `local:<command>`; `pnpm run ax:check` fails anything else. A check that was genuinely
  not run is recorded as `NOT RUN` — that is honest and passes.
- `VERIFICATION.md` is machine-written by `pnpm run ax:record`. Never hand-edit a result; re-check it
  with `pnpm run ax:verify`.
- The VPS is deploy-only. Storage persistence, protected volumes, and production container rules are a
  deployment/runtime invariant — see `deployment.instructions.md`.
- Get approval before destructive, networked, production, or credential-adjacent commands.
