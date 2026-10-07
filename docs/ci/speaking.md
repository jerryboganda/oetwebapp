# Speaking Module CI Runbook

## Workflows

| Workflow | Trigger | Owner |
|----------|---------|-------|
| `speaking-ci.yml` | PR + push to main (path filter on Speaking) | Speaking team |
| `speaking-e2e.yml` | Nightly 03:00 UTC + manual | Speaking team |
| `speaking-a11y.yml` | Nightly 03:30 UTC + manual | Speaking + a11y team |
| *(none)* | There is no Speaking load workflow: `speaking-load.yml` was deleted with all CI QA (owner directive 2026-10-06). The k6 scripts under `tests/load/` are manual tools the owner runs from a self-provisioned load generator (`docs/ops/LOAD-TESTING.md`). | Owner |

## Required secrets

- `STAGING_BASE_URL` — Speaking E2E target (https://staging.example.com)
- `GITLEAKS_LICENSE` (optional) — paid plan

## Composite action

`.github/actions/setup-oet-stack` installs .NET + Node, restores caches, optionally waits for the Postgres service container.

## Failure investigation

1. **Backend job fails** → there are no backend tests to fail (test code was deleted 2026-10-08, commit `f1b855bcc`); open the failing step's log in the Actions run.
2. **Migrations-check fails** → run `dotnet ef migrations add <Name>` locally and commit.
3. **E2E nightly red** → check `speaking-e2e-report` HTML artifact for failing trace + screenshot.
4. **A11y nightly red** → axe HTML report lists exact selector + WCAG rule. Triage as P1 if serious/critical, P2 otherwise.
5. **Load test SLO breach** → the owner runs the k6 scripts by hand (`docs/ops/LOAD-TESTING.md`); see `docs/load-testing/speaking-budgets.md` for the budget reference.

## Adding a new spec to a workflow

- Playwright spec: not applicable. `tests/e2e/` was deleted 2026-10-08 with all test code (commit `f1b855bcc`).
- Axe spec: not applicable. `tests/a11y/` was deleted 2026-10-08 with all test code.
- k6 script: not applicable. `tests/load/` was deleted 2026-10-08 with all test code.
