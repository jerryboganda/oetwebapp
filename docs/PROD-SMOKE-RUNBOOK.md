# Production Smoke — How to run

> Spec: `tests/e2e/prod-smoke.spec.ts` was deleted on 2026-10-08 with the rest of the test code (last copy at tag `last-commit-with-tests`).
> Target: `https://app.oetwithdrhesham.co.uk`

## 1. Set credentials (NEVER commit these)

PowerShell (temporary — current shell only):

```powershell
$env:PROD_LEARNER_EMAIL = "your-learner@example.com"
$env:PROD_LEARNER_PASSWORD = "your-password"
```

Or add to a local-only `.env.local.prod` (already covered by `.gitignore`).

## 2. Install Playwright browsers (once)

```powershell
# Removed 2026-10-08: the Playwright suite was deleted, so there are no browsers to install.
```

## 3. Run the smoke

```powershell
# Removed 2026-10-08: tests/e2e/prod-smoke.spec.ts was deleted, so there is no smoke run to start.
```

> The Playwright projects this note described (`chromium-unauth`, `chromium-learner`) and the saved auth state in `tests/e2e/setup/` were deleted on 2026-10-08 with the rest of the test code.

## 4. Read the report

```powershell
# Removed 2026-10-08: no Playwright report is produced any more.
```

Screenshots of each learner surface were written to `playwright-report-prod/` by the spec, which was deleted on 2026-10-08.

## AI worker + acceptance journeys

After web/API health is green, confirm:

```powershell
docker compose --env-file .env.production -f docker-compose.production.yml ps oet-ai-worker
docker compose --env-file .env.production -f docker-compose.production.yml exec oet-api-blue wget -qO- http://127.0.0.1:8080/health/live
```

Walk one synthetic flow per class with an internal admin/test account:

| Class | Journey |
|---|---|
| Scoring-critical | Reading attempt, Writing grade, Speaking AI assessment, Listening Part A advisory |
| Interactive learning | Reading/Listening explanation or passage Q&A |
| Admin batch | One extraction or draft job |

Do not treat a 7-day wait as a deploy blocker; run the scaled-down probes in
`scripts/ops` (ledger recon, duplicate markers) on the same SHA.

## What the spec asserts

- Sign-in with the provided credentials succeeds
- At least one auth cookie is set
- Every learner surface (`/`, `/study-plan`, `/progress`, `/readiness`, `/reading`,
  `/listening`, `/writing`, `/speaking`, `/mocks`, `/billing`, `/exam-guide`,
  `/feedback-guide`) returns `< 400` for the main document
- No JS console errors (favicon/beacon/cancel noise filtered)
- No `/v1/*` API responses with status `>= 500`
- Sign-out works when the shell exposes it

## If it fails

1. Open `playwright-report-prod/` screenshots to identify which surface broke.
2. Incident/deploy operators may check live server logs on the VPS with read-only commands only. Do not run builds, tests, installs, or validation suites on the VPS.
   ```bash
   docker compose --env-file .env.production -f docker-compose.production.yml logs --tail=300 web learner-api
   ```
3. If the issue is a deployment regression, follow the rollback procedure in
   [ops/deploy-gate.md](ops/deploy-gate.md#rollback-procedure) using the latest
   approved previous-good SHA and image digest record.
