# Speaking Module CI Runbook

## Workflows

| Workflow | Trigger | Owner |
|----------|---------|-------|
| `speaking-ci.yml` | PR + push to main (path filter on Speaking) | Speaking team |
| `speaking-e2e.yml` | Nightly 03:00 UTC + manual | Speaking team |
| `speaking-a11y.yml` | Nightly 03:30 UTC + manual | Speaking + a11y team |
| `speaking-load.yml` | Weekly Mon 04:00 UTC + manual | Speaking + ops |
| `speaking-content-batch.yml` | Manual only | Content team |

## Required secrets

- `STAGING_BASE_URL` — Speaking E2E target (https://staging.example.com)
- `STAGING_API_URL` — Speaking load + content batch (https://staging-api.example.com)
- `STAGING_ADMIN_TOKEN` — admin JWT for content batch
- `OET_LOAD_LEARNER_EMAIL` / `OET_LOAD_LEARNER_PASSWORD` — load-test learner creds
- `GITLEAKS_LICENSE` (optional) — paid plan

## Composite action

`.github/actions/setup-oet-stack` installs .NET + Node, restores caches, optionally waits for the Postgres service container.

## Failure investigation

1. **Backend job fails on tests** → download `speaking-test-results` artifact, open the `.trx` file in Visual Studio or `dotnet test --logger "console;verbosity=detailed"` locally.
2. **Migrations-check fails** → run `dotnet ef migrations add <Name>` locally and commit.
3. **E2E nightly red** → check `speaking-e2e-report` HTML artifact for failing trace + screenshot.
4. **A11y nightly red** → axe HTML report lists exact selector + WCAG rule. Triage as P1 if serious/critical, P2 otherwise.
5. **Load test SLO breach** → see `docs/load-testing/speaking-budgets.md` for the budget reference; open an incident if any p95 metric is over budget for two consecutive runs.

## Adding a new spec to a workflow

- Playwright spec → drop at `tests/e2e/speaking-<name>.spec.ts`; picked up automatically by the glob.
- Axe spec → drop at `tests/a11y/<surface>.a11y.spec.ts`.
- k6 script → drop at `tests/load/speaking-<scenario>.k6.js`.
