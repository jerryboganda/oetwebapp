# Production E2E Audit: 2026-04-28

## Scope

This audit records the production E2E readiness state for the OET Prep Platform on 28 April 2026.

The completed pass covered production health, unauthenticated browser QA, and local implementation of the accessibility fixes found on public auth/legal surfaces. Credentialed learner, tutor/expert, admin, sponsor, and expired-account E2E coverage could not be executed because dedicated production test credentials are not configured locally or on the VPS.

## Production Baseline

- Baseline production commit before this fix: `3465398`.
- Public web health: green.
- Public API health: green, including database `ok`.
- Credential scan: no `PROD_LEARNER_*`, `PROD_EXPERT_*`, `PROD_ADMIN_*`, `PROD_SPONSOR_*`, or `PROD_EXPIRED_*` keys found locally or in `/opt/oetwebapp/.env.production`.

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

## Blocked Credentialed Matrix

| Role | Status | Blocker |
| --- | --- | --- |
| Learner | Blocked | Missing dedicated production learner credentials. |
| Tutor/expert | Blocked | Missing dedicated production tutor/expert credentials. |
| Admin | Harness added, execution blocked | Missing dedicated production admin credentials. |
| Sponsor | Harness added, execution blocked | Missing dedicated production sponsor credentials. |
| Expired account | Harness added, execution blocked | Missing dedicated expired-account credentials. |

## Required Production Test Secrets

Configure these as local/VPS secrets before the credentialed production matrix runs. Do not commit them.

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

1. Provision dedicated low-risk production test accounts for learner, tutor/expert, admin, sponsor, and expired states.
2. Store credentials only in approved secret configuration, never in Git.
3. Run credentialed smoke E2E first, avoiding destructive billing, content deletion, account suspension, publishing, bulk import, or irreversible admin actions.
4. Expand to role-specific read-only and reversible workflows once smoke coverage is stable.
5. Re-check landmarks, accessible names, keyboard flow, touch targets, console errors, API 5xxs, and role-based route guards across authenticated shells.
6. Capture any production-only failures with commit, route, role, browser, expected/actual behavior, and reproduction notes.
7. Fix locally, run the normal quality gates, push after `git fetch`/rebase, then redeploy with a fresh production backup.

## Added Credential-Gated Harness

`tests/e2e/prod-smoke-privileged.spec.ts` now covers read-only smoke walks for admin, sponsor, and expired-account credentials. The normal Playwright matrix ignores this file; it runs only through `playwright.prod-privileged.config.ts`. The tests skip unless `RUN_PROD_PRIVILEGED_SMOKE=1` and the matching `PROD_*` secrets are present. They seed auth through the production API, visit only non-destructive surfaces, and fail on unexpected redirects, document access failures, API/non-API 5xxs, missing main landmarks, uncaught page errors, request failures, or console errors.

Run the privileged production harness explicitly:

```powershell
$env:RUN_PROD_PRIVILEGED_SMOKE = "1"
npm run test:e2e:prod-privileged -- --workers=1
```