# Maximum Performance Optimisation — Evidence

Date: 2026-08-08

## Delivered

- Added browser performance budgets and authoritative timing/resource collection for unauthenticated, learner, admin, Pixel, and iPhone routes.
- Added an isolated disposable staging-like GitHub Actions stack with HTTPS proxying, readiness gates, browser budgets, k6 critical-read load, artifact upload, and production-host refusal for external mode.
- Preserved auth/CSP/native bridges while reducing startup work: web auth-storage hydration no longer probes native Preferences, navigation prefetch is disabled for the primary shell links, initial state/layout projection was removed from the critical loading-to-dashboard swap, and the optional app promotion is below dashboard content.
- Code-split the learner dashboard's lower readiness, pronunciation, scoring, streak, and add-on widget tree so mobile hydration can paint the primary dashboard/action surface without parsing the complete secondary dashboard.
- Corrected the k6 critical-read threshold selector to match the emitted `endpoint-class` tag and added a regression test for that contract.
- Kept native Mobile and Tauri CI coverage available for shared runtime/provider/performance changes.

The enforced browser budgets are LCP <= 2.5 s, FCP <= 1.8 s, INP <= 200 ms when a qualifying interaction exists, CLS <= 0.1, zero page/request/HTTP response errors, and no horizontal overflow. The k6 gate enforces HTTP failure rate < 1%, critical-read P95 < 1 s, and P99 < 2 s. JavaScript/CSS encoded transfer size is reported separately from decoded size.

## Shipped revision and deployment

- Runtime commit: `117a1fbfe` — `perf: code split learner dashboard details`.
- Main was pushed successfully; the working checkout remains on `main`.
- Build & Deploy run [31224563676](https://github.com/jerryboganda/oetwebapp/actions/runs/31224563676) completed successfully: web, API, and backup images; production migration; deploy.
- SBOM/SCA run [31224563636](https://github.com/jerryboganda/oetwebapp/actions/runs/31224563636) completed successfully.
- Direct post-deploy checks returned HTTP 200:
  - `https://app.oetwithdrhesham.co.uk/` — web document served successfully.
  - `https://api.oetwithdrhesham.co.uk/health` — API/database status `ok`.
  - `https://api.oetwithdrhesham.co.uk/health/ready` — database, migrations, stuck jobs, and storage all `ok`.

## Authoritative browser gate

Performance run [31224597913](https://github.com/jerryboganda/oetwebapp/actions/runs/31224597913) passed on `117a1fbfe`. It built the isolated stack, verified readiness, ran through the HTTPS proxy, and completed the browser, k6, and summary gates. All five projects had zero page/request/HTTP response errors and no budget violations.

| Project | Route | LCP | FCP | CLS | JS encoded / decoded | CSS encoded / decoded |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Admin Chromium | `/admin` | 956 ms | 320 ms | 0 | 696,403 / 2,415,624 B | 58,263 / 424,974 B |
| Learner Chromium | `/` | 980 ms | 376 ms | 0.009958 | 660,924 / 2,300,862 B | 58,263 / 424,974 B |
| Learner iPhone | `/` | 1,224 ms | 1,224 ms | 0 | 675,494 / 2,343,286 B | 58,353 / 424,974 B |
| Learner Pixel | `/` | 748 ms | 356 ms | 0 | 674,570 / 2,343,286 B | 58,263 / 424,974 B |
| Unauthenticated Chromium | `/sign-in` | 548 ms | 548 ms | 0 | 473,529 / 1,619,499 B | 61,784 / 449,757 B |

INP was `n/a` because the measurement flow produced no qualifying interaction; the configured missing-INP policy explicitly permits that. No browser project reported overflow or violations.

## Authoritative k6 gate

The same performance run completed k6 successfully:

- 88,857 HTTP requests; HTTP failure rate 0.00% (0 failures).
- 88,857 checks passed; 0 checks failed.
- Critical-read aggregate P95: 221.68 ms.
- Critical-read maximum: 769.38 ms.
- The script enforced critical-read P95 < 1,000 ms and P99 < 2,000 ms; the k6 process and workflow gate passed. The default k6 console/summary export did not emit an exact P99 value, so no invented percentile is reported; the observed maximum is already below the P99 ceiling.
- The run used the disposable isolated stack. No production load was generated.

## Native CI

- Mobile CI [31224597896](https://github.com/jerryboganda/oetwebapp/actions/runs/31224597896) passed: unit tests, lint/typecheck, Android debug build, and iOS build check.
- Tauri Desktop CI [31224598260](https://github.com/jerryboganda/oetwebapp/actions/runs/31224598260) passed: desktop bridge conformance plus Windows Rust format, clippy, tests, and build.

These are CI build/contract gates, not physical-device measurements.

## Local focused validation

- `pnpm exec tsc --noEmit --pretty false` — passed.
- `pnpm exec eslint app/page.tsx components/learner/learner-dashboard-details.tsx` — passed.
- `pnpm exec vitest run tests/performance/metrics.test.ts lib/auth-storage.test.ts --reporter=dot` — 10/10 passed.
- `node --test tests/load/critical-paths.k6.test.js` — 2/2 passed.
- `git diff --check` — passed before commit.
- Local Docker was unavailable, so the isolated stack ran in CI. The targeted local .NET AuthFlows test exceeded the local 120-second command window and was not claimed as passed; CI deployment/backend gates remained the authoritative host build boundary.

## Remaining boundaries

- No external non-production staging URL or `OET_PERF_*` credentials were configured, so the validated staging-like target was the disposable isolated CI stack. The workflow refuses production hosts in external mode.
- Physical Android, iOS, Windows, and macOS device/browser measurements remain manual acceptance work; native CI proves build and bridge compatibility.
- QA Smoke [31224563639](https://github.com/jerryboganda/oetwebapp/actions/runs/31224563639) and Speaking Module CI [31224563634](https://github.com/jerryboganda/oetwebapp/actions/runs/31224563634) are separate broad repository workflows and were not performance acceptance gates. The focused performance gate, native gates, production deploy, and live health checks above are green.
