# Maximum Performance Optimisation — Evidence

Date: 2026-08-07

## Delivered

- Browser performance budgets and timing/resource collection in `tests/performance/`.
- Isolated Playwright projects for unauthenticated, learner, admin, Pixel, and iPhone paths.
- Staging-only k6 critical-path coverage for auth/bootstrap, dashboard, subscription, entitlement, and optional admin reads.
- Scheduled/manual GitHub Actions performance workflow with production-host rejection, browser budgets, k6 thresholds, and artifact summaries.
- Mobile and Tauri workflow path filters so shared runtime/provider/media changes require native CI.
- Vitest exclusion for Playwright performance specs so the repository-wide jsdom runner does not collect them.

The browser budgets are LCP <= 2.5 s, FCP <= 1.8 s, INP <= 200 ms, and CLS <= 0.1. The k6 gate requires HTTP failure rate < 1%, critical-read P95 < 1 s, and P99 < 2 s. JavaScript/CSS transfer size is reported separately from decoded size.

## Release evidence

- Performance implementation commits are in `main`, including `573dfd305` (`test: isolate playwright performance specs from vitest`).
- Performance runtime release `31bd84b61` contains the implementation and was deployed successfully. The later documentation-only release `ece0c9c0d` was also deployed successfully without changing runtime behavior.
- Build & Deploy run `31192330082` completed successfully: web build, API build, blue/green health gates, router switch, and public verification all passed.
- Final documentation-release Build & Deploy run `31195289547` completed successfully with the same blue/green and public verification gates.
- Live images were deployed from `31bd84b61aab1e4e100b6c210a507a0deac65642`.
- Direct post-deploy checks returned HTTP 200:
  - `https://app.oetwithdrhesham.co.uk/api/health` — `{"status":"ok","service":"oet-web"}`
  - `https://api.oetwithdrhesham.co.uk/health/ready` — status ok; database, migrations, stuck jobs, and storage all ok.
- Mobile CI run `31189982498` completed successfully: unit tests, lint/typecheck, iOS build check, and Android debug APK build.
- Tauri Desktop CI run `31189990700` completed successfully: bridge contract conformance plus Windows Rust format, clippy, tests, and build.
- SBOM/SCA run `31192330220` completed successfully.

## Local focused validation

- `pnpm exec vitest run tests/performance/metrics.test.ts tests/static/frontend-heavy-imports.test.ts --reporter=dot` — 9/9 passed.
- `node --test tests/load/critical-paths.k6.test.js scripts/perf/summarize-browser-reports.test.mjs` — 4/4 passed.
- Targeted ESLint for the performance harness/config — passed.
- `pnpm exec tsc --noEmit --pretty false` — passed after removing stale generated `.next` dev types from the prior local server attempt.
- `pnpm exec playwright test -c playwright.performance.config.ts --list` — 7 isolated performance tests listed.
- Vitest discovery of `tests/performance/browser-performance.spec.ts` — zero files, confirming runner isolation.

## Explicit remaining boundaries

- The performance workflow is scheduled/manual and was not executed because no non-production performance target or `OET_PERF_*` credentials are configured. The documented staging DNS names were unresolved from this host. No production load test was run.
- A local browser baseline was not claimed: the local app runtime was not available on port 3000 and Docker is not installed on this host. The CI harness is ready to run when a valid staging target and credentials are supplied.
- Native CI proves build/contract compatibility only; physical Android, iOS, Windows, and macOS device measurements remain manual acceptance work.
- Broad QA Smoke run `31192332484` completed with failure and was not a performance gate. Its frontend failures were unrelated existing suites (18 files, 43 tests) and did not include the isolated Playwright performance spec after the fix; backend failures were device/version-gate and EF test-environment failures; E2E jobs failed local-stack readiness at `http://localhost:5198/health/ready`.

The implementation and production release are complete. The only unverified items are the external staging-load/browser measurement run and physical-device acceptance, which require infrastructure or devices outside this workspace.
