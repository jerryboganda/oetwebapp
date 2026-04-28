# Production E2E Audit: 2026-04-28

## Scope

This audit records the production E2E readiness state for the OET Prep Platform on 28 April 2026.

The completed pass covered production health, unauthenticated browser QA, credentialed learner, tutor/expert, admin, sponsor, and expired-account smoke coverage. Dedicated synthetic production accounts are now configured for sponsor and expired-account scans, with secrets stored only outside Git.

## Production Baseline

- Earlier baseline production commit before the first audit fix: `3465398`.
- Final deployed production commit after credentialed role completion: `a6a8812`.
- Public web health: green.
- Public API health: green, including database `ok`.
- Production smoke secrets are stored on the VPS at `/root/oet-prod-e2e.env` with `600` permissions. Local copied secrets were deleted after the run.
- Fresh database backup before sponsor/expired provisioning: `/root/backups/pre-prod-e2e-sponsor-expired-20260428-043253-7b8fd74.dump`.

## Completed Unauthenticated Findings And Fixes

| Area | Finding | Fix |
| --- | --- | --- |
| Auth/legal pages | Public pages did not expose a `main` landmark. | `AuthScreenShell` now renders the primary content wrapper as `<main>`. |
| Register form | Country-code selector did not expose a distinct accessible name. | `CountryCodeSelect` now provides an overridable `aria-label`, defaulting to `Country calling code`. |
| Auth interactions | Some auth links and the remember-me checkbox had small touch targets. | Auth shell link and checkbox hit areas were increased without changing form semantics. |

## Verification

Local validation passed after the fixes:

- Focused auth/accessibility tests: `4` files, `5` tests.
- `npx tsc --noEmit`.
- `npm run lint`.
- Full Vitest suite: `120` files, `690` tests.
- `npm run build` with only the known Prisma/Sentry/OpenTelemetry warning.
- Independent reviewer pass: no blockers or non-blocking issues found.

Additional validation after the credentialed production fix:

- Focused readiness/dashboard regression tests: `app/page.test.tsx` and `lib/__tests__/api.test.ts`, `23` tests passed.
- `npm run build` on the final dashboard/readiness fix.
- Temporary EF/Core sponsor/expired provisioner build succeeded; it writes its transient env output with owner-only permissions.
- Production deploy/rebuild completed from `/opt/oetwebapp` at `11efe00`, then the VPS checkout was fast-forwarded to `a6a8812`.
- Final production health: API `/health` returned `200` with database `ok`; web returned `307` redirect from the root host.

## Credentialed Matrix

| Role | Status | Coverage |
| --- | --- | --- |
| Learner | Passed | `tests/e2e/prod-smoke.spec.ts` walked dashboard, study plan, progress, readiness, reading, listening, writing, speaking, mocks, billing, exam guide, and feedback guide. |
| Tutor/expert | Passed | `tests/e2e/prod-smoke-expert.spec.ts` walked expert console, onboarding, and safe drill-down routes. Empty queue/calibration/learner lists were detected and skipped safely. |
| Admin | Passed | `tests/e2e/prod-smoke-privileged.spec.ts` walked `/admin`, `/admin/content`, `/admin/users`, `/admin/billing`, and `/admin/analytics/quality` with mutation guard enabled. |
| Sponsor | Passed | `tests/e2e/prod-smoke-privileged.spec.ts` walked `/sponsor`, `/sponsor/learners`, and `/sponsor/billing` with mutation guard enabled. |
| Expired account | Passed | `tests/e2e/prod-smoke-privileged.spec.ts` walked `/dashboard`, `/billing`, and `/settings`, allowing expected billing redirects only, with mutation guard enabled. |

Final production command results:

- Learner/expert smoke: `4` tests passed in `1.5m`.
- Privileged smoke: `3` tests passed in `29.4s`.

## Production Defects Found And Fixed

| Area | Finding | Fix |
| --- | --- | --- |
| Expired dashboard | Sparse readiness snapshots without `evidence` caused the learner dashboard route error boundary for the expired account. | `fetchReadiness()` now normalizes missing/malformed evidence, blockers, and subtests; the dashboard also guards empty readiness averages and missing trend text. |
| Production privileged smoke | Analytics events through the app proxy appeared as unexpected mutations. | The read-only mutation guard now mocks both direct API analytics and `/api/backend/v1/analytics/events`. |
| Sponsor/expired test state | Sponsor and expired-account smoke credentials/state were missing. | A temporary EF/Core provisioner created controlled synthetic accounts, revoked old sessions, upserted support rows, and refreshed secret storage outside Git. |

## Required Production Test Secrets

Configure these as local/VPS secrets before rerunning the credentialed production matrix. Do not commit them.

```text
RUN_PROD_PRIVILEGED_SMOKE=1

PROD_LEARNER_EMAIL=
PROD_LEARNER_PASSWORD=

PROD_EXPERT_EMAIL=
PROD_EXPERT_PASSWORD=

PROD_ADMIN_EMAIL=
PROD_ADMIN_PASSWORD=

PROD_SPONSOR_EMAIL=
PROD_SPONSOR_PASSWORD=

PROD_EXPIRED_EMAIL=
PROD_EXPIRED_PASSWORD=
```

## Safe Next Wave

1. Keep all production smoke credentials in approved secret configuration, never in Git.
2. Avoid destructive billing, content deletion, account suspension, publishing, bulk import, or irreversible admin actions in production E2E.
3. Expand to role-specific read-only and reversible workflows once smoke coverage is stable.
4. Re-check landmarks, accessible names, keyboard flow, touch targets, console errors, API 5xxs, and role-based route guards across authenticated shells.
5. Capture any production-only failures with commit, route, role, browser, expected/actual behavior, and reproduction notes.
6. Fix locally, run the normal quality gates, push after `git fetch`/rebase, then redeploy with a fresh production backup when data changes are required.

## Added Credential-Gated Harness

`tests/e2e/prod-smoke-privileged.spec.ts` covers read-only smoke walks for admin, sponsor, and expired-account credentials. The normal Playwright matrix ignores this file; it runs only through `playwright.prod-privileged.config.ts`. The tests skip unless `RUN_PROD_PRIVILEGED_SMOKE=1` and the matching `PROD_*` secrets are present. They seed auth through the production API, visit only non-destructive surfaces, block unexpected mutations after auth, mock expected analytics beacons, and fail on unexpected redirects, document access failures, API/non-API 5xxs, missing main landmarks, uncaught page errors, request failures, console errors, or unexpected client-side mutations.

Run the privileged production harness explicitly:

```powershell
$env:RUN_PROD_PRIVILEGED_SMOKE = "1"
npm run test:e2e:prod-privileged -- --workers=1
```
