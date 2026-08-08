# Maximum Performance Optimisation - Evidence

Date: 2026-08-08

## Delivered

- Added measurement-gated browser performance budgets and authoritative timing,
  Web Vitals, resource-size, error, and overflow collection for public,
  unauthenticated, learner, and admin routes.
- Added an isolated disposable staging-like GitHub Actions stack with HTTPS
  proxying, readiness gates, browser budgets, k6 critical-read load, artifact
  upload, and production-host refusal for external mode.
- Stabilised the learner first paint: critical dashboard queries are limited to
  auth/profile/home data, lower dashboard details hydrate below the fold,
  navigation prefetch is disabled for primary shell links, and initial route/
  state layout projection is disabled during the loading-to-dashboard swap.
- Aligned the auth-loading learner shell with the real learner shell, including
  fixed viewport geometry, branded header controls, sidebar width, and mobile
  bottom navigation. This removed the remaining Pixel CLS regression without
  weakening auth, CSP, storage, or native bridge contracts.
- Added responsive browser coverage for Chromium, Firefox, WebKit, Pixel,
  iPhone, public routes, and admin routes, plus Android/iOS and Windows/macOS
  native CI gates.

The enforced browser budgets are LCP <= 2.5 s, FCP <= 1.8 s, INP <= 200 ms
when a qualifying interaction exists, CLS <= 0.1, zero page/request/HTTP
response errors, and no horizontal overflow. The k6 gate enforces HTTP failure
rate < 1%, critical-read P95 < 1 s, and P99 < 2 s. JavaScript/CSS encoded
transfer size is reported separately from decoded size.

## Shipped revision and deployment

- Runtime implementation revision: `0caccbabe3d5622cc56d5ebd601fe2d30f5e896f`
  (`perf: align learner loading shell geometry`).
- The Android runtime smoke harness was then corrected on
  `60e9a60a0` (direct activity launch and portable process loop) and
  `584177a75` (portable crash-signature assertion).
- Build & Deploy run [31234129215](https://github.com/jerryboganda/oetwebapp/actions/runs/31234129215)
  completed successfully for the implementation revision: web, API, and
  backup images, production migration, and deploy.
- Follow-up Build & Deploy run [31236473400](https://github.com/jerryboganda/oetwebapp/actions/runs/31236473400)
  completed successfully after the native smoke harness corrections.
- SBOM/SCA run [31234129218](https://github.com/jerryboganda/oetwebapp/actions/runs/31234129218)
  completed successfully.
- Direct post-deploy checks after the final deployment returned HTTP 200:
  - `https://app.oetwithdrhesham.co.uk/` - web document served successfully.
  - `https://api.oetwithdrhesham.co.uk/health` - API/database status `ok`.
  - `https://api.oetwithdrhesham.co.uk/health/ready` - database, migrations,
    stuck jobs, and storage all `ok`.

## Authoritative browser gate

Performance run [31234134695](https://github.com/jerryboganda/oetwebapp/actions/runs/31234134695)
passed on the runtime revision. It built the isolated stack, verified
readiness, ran through the HTTPS proxy, and completed the browser, k6, and
summary gates. All 10 projects had zero browser page/request/HTTP response
errors, zero horizontal overflow, and zero budget violations.

| Project | Route | LCP | FCP | CLS | JS encoded / decoded | CSS encoded / decoded |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Admin Chromium | `/admin` | 980 ms | 336 ms | 0 | 710,716 / 2,472,639 B | 58,276 / 425,042 B |
| Admin Pixel | `/admin` | 900 ms | 324 ms | 0 | 710,716 / 2,472,639 B | 58,276 / 425,042 B |
| Learner Chromium | `/` | 548 ms | 548 ms | 0 | 674,303 / 2,352,138 B | 58,276 / 425,042 B |
| Learner Firefox | `/` | 353 ms | 353 ms | 0 | 674,303 / 2,352,138 B | 58,276 / 425,042 B |
| Learner iPhone | `/` | 1,852 ms | 232 ms | 0 | 688,913 / 2,394,583 B | 58,366 / 425,042 B |
| Learner Pixel | `/` | 324 ms | 324 ms | 0 | 687,964 / 2,394,583 B | 58,276 / 425,042 B |
| Learner WebKit | `/` | 1,185 ms | 1,185 ms | 0 | 690,015 / 2,395,948 B | 58,366 / 425,042 B |
| Public Chromium | `/get-app` | 328 ms | 328 ms | 0.0228 | 415,853 / 1,419,019 B | 56,287 / 418,413 B |
| Public Pixel | `/get-app` | 324 ms | 324 ms | 0 | 415,853 / 1,419,019 B | 56,287 / 418,413 B |
| Unauthenticated Chromium | `/sign-in` | 484 ms | 456 ms | 0.0244 | 475,049 / 1,627,830 B | 61,797 / 449,825 B |

INP was `n/a` because the measurement flow produced no qualifying interaction;
the configured missing-INP policy permits that. The previous learner Pixel
failure (CLS `0.14749`) was reproduced from trace evidence and fixed by making
the auth-loading shell geometry match the hydrated learner shell; the final
Pixel run recorded CLS `0`.

## Authoritative k6 gate

The same performance run completed k6 successfully:

- 89,933 HTTP requests; 89,932 checks succeeded and 1 check failed.
- HTTP failure rate was 1/89,933 (0.0011%, displayed as `0.00%`), below the
  enforced `<1%` threshold.
- Critical-read P95 was 216.51 ms and P99 was 280.8 ms; both stayed below the
  1,000 ms / 2,000 ms thresholds.
- The single failed check was one dashboard response under the 100-VU load.
  The isolated stack log attributes it to PostgreSQL reaching its connection
  cap (`too many clients already`), not a browser error or a sustained HTTP
  failure. This is recorded as the remaining server-capacity observation; no
  production load was generated.

## Native CI

- Mobile CI [31236473393](https://github.com/jerryboganda/oetwebapp/actions/runs/31236473393)
  passed lint/typecheck, mobile unit tests, iOS build and simulator launch,
  Android debug build, and the Android emulator runtime smoke. The Android
  artifact records a 915,883 ms hosted-emulator boot, successful APK install,
  live process `3250`, the expected `MainActivity`, and no AndroidRuntime crash
  signature. `adb shell am start -W` reported `Status: timeout` with
  `LaunchState: UNKNOWN` after 42.75 s, while the subsequent process/activity
  assertions passed; this is retained as a hosted-emulator launcher timing
  observation, not a native LCP measurement.
- Tauri Desktop CI [31234597842](https://github.com/jerryboganda/oetwebapp/actions/runs/31234597842)
  passed Windows Rust format/clippy/tests/build, macOS desktop launch smoke,
  and `desktopBridge` contract conformance.

These are CI build/simulator/emulator/contract gates, not physical-device
measurements.

## Local focused validation

- `pnpm exec tsc --noEmit --pretty false` - passed.
- `pnpm exec eslint components/auth/auth-guard.tsx` - passed.
- `pnpm exec vitest run app/page.test.tsx components/layout/__tests__/app-shell.test.tsx --reporter=dot` - 9/9 passed.
- `git diff --check` - passed.
- Local Docker was unavailable, so the isolated performance stack ran in CI.

## Remaining boundaries

- No external non-production staging URL or `OET_PERF_*` credentials were
  configured; the validated staging-like target was the disposable isolated CI
  stack. The workflow refuses production hosts in external mode.
- Physical Android, iOS, Windows, and macOS device measurements and manual
  low-bandwidth interaction acceptance remain owner-side boundaries. Native CI
  proves hosted emulator/simulator build, process/activity launch assertions,
  and bridge compatibility; it does not replace physical-device measurements
  or native LCP profiling.
- QA Smoke [31234129214](https://github.com/jerryboganda/oetwebapp/actions/runs/31234129214)
  and Speaking Module CI [31234129174](https://github.com/jerryboganda/oetwebapp/actions/runs/31234129174)
  are separate broad repository workflows, not performance acceptance gates;
  their failing unrelated suites remain visible and were not hidden.
