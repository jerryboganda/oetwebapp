# Production Smoke — How to run

> Spec: [tests/e2e/prod-smoke.spec.ts](../tests/e2e/prod-smoke.spec.ts)
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
npm run test:e2e:install
```

## 3. Run the smoke

```powershell
npx playwright test tests/e2e/prod-smoke.spec.ts --project=chromium-unauth --workers=1
```

> The `chromium-unauth` project has no saved auth state, which matches what the
> spec expects (it signs in itself with the env credentials). Avoid
> `chromium-learner` etc. because those projects preload a saved learner auth
> state from `tests/e2e/setup/` which would conflict.

## 4. Read the report

```powershell
npx playwright show-report
```

Screenshots of each learner surface will be written to `playwright-report-prod/`.

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
2. Check live server logs on the VPS:
   ```bash
   docker compose --env-file .env.production -f docker-compose.production.yml logs --tail=300 web learner-api
   ```
3. If the issue is a deployment regression, follow the rollback procedure in
   [ops/deploy-gate.md](ops/deploy-gate.md#rollback-procedure) using the latest
   approved previous-good SHA from release evidence.
