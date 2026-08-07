# Maximum Cross-Platform Performance Optimization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task with checkpoints.

**Goal:** Add reproducible browser performance budgets, staging-only critical API load coverage, and native CI trigger coverage while preserving the existing cross-platform startup/retry optimizations.

**Architecture:** A small Playwright performance project collects page and resource timing for public, learner, and admin routes using isolated auth state. A dedicated k6 scenario exercises only safe staging API reads with explicit thresholds. GitHub Actions stores sanitized performance artifacts and routes shared provider/runtime changes through Android, iOS, and Tauri gates.

**Tech Stack:** Next.js 16, TypeScript, Playwright, Vitest, k6, GitHub Actions, Capacitor Android/iOS, Tauri 2.

## Global Constraints

- Keep `e87b5f051` startup, provider-boundary, media, and retry behavior intact unless a measurement proves a focused correction is required.
- Browser targets: LCP <= 2.5s, FCP <= 1.8s, INP <= 200ms when available, CLS <= 0.1.
- Staging load targets: P95 < 1s, P99 < 2s, HTTP error rate < 1%.
- k6 must fail on threshold violations; it must not convert failures into green results.
- Never send load traffic to production, read `.env*`/secret files, print tokens, or store customer data in artifacts.
- Heavy Next.js, Android, iOS, and Tauri builds run in GitHub Actions; local checks remain focused.
- Preserve CSP nonces, auth/session initialization, native bridges, media security, scoring, rulebooks, storage, entitlements, and route behavior.
- Stage explicit paths only and preserve pre-existing untracked `.codex/config.toml` and `.superpowers/`.

---

### Task 1: Define the browser metric model and pure budget logic

**Files:**
- Create: `tests/performance/metrics.ts`
- Create: `tests/performance/metrics.test.ts`

**Interfaces:**
- `BrowserPerformanceReport` contains `route`, `project`, `capturedAt`, `navigation`, `webVitals`, `resources`, `errors`, and `layout` fields.
- `PerformanceBudgets` contains `lcpMs`, `fcpMs`, `inpMs`, `cls`, `maxPageErrors`, `maxRequestFailures`, and `allowMissingInp`.
- `evaluatePerformanceBudget(report, budgets): string[]` returns stable human-readable violations; an empty array means the report passes.
- `summarizeResourceTotals(entries): ResourceTotals` returns encoded and decoded JavaScript/CSS byte totals separately.

- [ ] **Step 1: Write failing unit tests**

```ts
it('reports only metrics that exceed the configured budgets', () => {
  const violations = evaluatePerformanceBudget(
    makeReport({ lcpMs: 2_501, fcpMs: 1_700, inpMs: 210, cls: 0.11 }),
    DEFAULT_PERFORMANCE_BUDGETS,
  );

  expect(violations).toEqual([
    'LCP 2501ms exceeds 2500ms',
    'INP 210ms exceeds 200ms',
    'CLS 0.11 exceeds 0.1',
  ]);
});

it('keeps encoded and decoded JavaScript/CSS totals distinct', () => {
  expect(summarizeResourceTotals([
    { name: '/app.js', initiatorType: 'script', transferSize: 100, decodedBodySize: 300 },
    { name: '/app.css', initiatorType: 'link', transferSize: 50, decodedBodySize: 150 },
  ])).toEqual({
    javascript: { encodedBytes: 100, decodedBytes: 300 },
    css: { encodedBytes: 50, decodedBytes: 150 },
  });
});
```

- [ ] **Step 2: Run the focused test and verify it fails**

Run: `pnpm exec vitest run tests/performance/metrics.test.ts --reporter=dot`  
Expected: FAIL because the metric model and budget functions do not exist.

- [ ] **Step 3: Implement the typed model and pure functions**

Use nullable numeric fields for unavailable browser metrics. Treat missing INP as valid only when `allowMissingInp` is true. Count only JavaScript and CSS resources, use `transferSize` for encoded bytes, and use `decodedBodySize` for decoded bytes.

- [ ] **Step 4: Run the focused test and verify it passes**

Run: `pnpm exec vitest run tests/performance/metrics.test.ts --reporter=dot`  
Expected: PASS with no secret or customer-data output.

- [ ] **Step 5: Commit the self-contained model**

```powershell
git add -- tests/performance/metrics.ts tests/performance/metrics.test.ts
git commit -m "test: define browser performance budgets"
```

### Task 2: Add isolated performance Playwright configuration and auth setup

**Files:**
- Create: `tests/performance/auth.setup.ts`
- Create: `playwright.performance.config.ts`

**Interfaces:**
- The `perf-setup` project writes only `playwright/.auth/perf-learner.json` and `playwright/.auth/perf-admin.json`.
- Browser projects are named `perf-unauth-chromium`, `perf-learner-chromium`, `perf-learner-pixel`, `perf-learner-iphone`, and `perf-admin-chromium`.
- `PLAYWRIGHT_BASE_URL` selects the web target; `NEXT_PUBLIC_API_BASE_URL` selects the API used by the existing auth bootstrap.

- [ ] **Step 1: Add a focused auth setup using existing fixtures**

Reuse `bootstrapSessionForRole` and `persistSessionToStorageState` from `tests/e2e/fixtures/auth-bootstrap.ts` with `useDiskCache: false` and `isolateSession: true`. Do not duplicate credentials or print the session response.

- [ ] **Step 2: Add the dedicated Playwright projects**

Set `testDir: '.'`, `testMatch: /tests\\/performance\\/.*\\.spec\\.ts/`, `fullyParallel: false`, and a performance-specific report directory. Add a `perf-setup` project matching only `tests/performance/auth.setup.ts`; make the learner/admin projects depend on it. Use Playwright’s existing `Desktop Chrome`, `Pixel 7`, and `iPhone 14` device descriptors.

- [ ] **Step 3: Run config discovery**

Run: `pnpm exec playwright test -c playwright.performance.config.ts --list`  
Expected: the setup plus five performance projects are listed without starting a browser or exposing credentials.

- [ ] **Step 4: Commit the isolated runner**

```powershell
git add -- tests/performance/auth.setup.ts playwright.performance.config.ts
git commit -m "test: add isolated performance browser projects"
```

### Task 3: Implement browser route measurements and budget enforcement

**Files:**
- Create: `tests/performance/browser-performance.spec.ts`
- Modify: `tests/performance/metrics.ts`

**Interfaces:**
- `collectBrowserPerformance(page, route, projectName): Promise<BrowserPerformanceReport>` installs observers before navigation and returns a sanitized report.
- Route matrix: unauthenticated `/sign-in`, learner `/`, and admin `/admin`.
- Reports are written to `output/performance/browser/<project>-<route>.json` and attached to the Playwright report.

- [ ] **Step 1: Add route/project tests with report-only defaults**

Install `PerformanceObserver` for `largest-contentful-paint`, `layout-shift`, `event`, and `longtask` before `page.goto`. Capture `pageerror` and `requestfailed` messages without URLs containing query strings or response bodies. Wait for a route-specific visible heading/main landmark and `document.fonts.ready`; do not add arbitrary sleep delays.

- [ ] **Step 2: Capture navigation, web vitals, resources, overflow, and visibility**

Read `performance.getEntriesByType('navigation')`, `performance.getEntriesByType('resource')`, and the observer buffers. Record TTFB, DOM/content load, LCP, FCP, INP when available, CLS, long-task total, JS/CSS encoded/decoded totals, failed resources, page errors, `scrollWidth`, `clientWidth`, and primary-content visibility.

- [ ] **Step 3: Enforce budgets only when requested**

Use `PERF_ENFORCE_BUDGETS=1` for CI gates. Always write the report; when enforcement is enabled, fail with the exact violation list from `evaluatePerformanceBudget`. Keep report-only mode available for the baseline run.

- [ ] **Step 4: Run the focused performance spec against an available local target**

Run: `PERF_ENFORCE_BUDGETS=0 pnpm exec playwright test -c playwright.performance.config.ts --project=perf-unauth-chromium tests/performance/browser-performance.spec.ts`  
Expected: a sanitized JSON report is produced when the local app is available; if no local stack is running, the command must fail with the normal readiness/navigation error rather than claiming a baseline.

- [ ] **Step 5: Commit the browser harness**

```powershell
git add -- tests/performance/metrics.ts tests/performance/browser-performance.spec.ts
git commit -m "perf: add browser performance measurements"
```

### Task 4: Add staging-only critical API k6 coverage

**Files:**
- Create: `tests/load/critical-paths.k6.js`
- Create: `tests/load/critical-paths.k6.test.js`

**Interfaces:**
- The k6 script reads `K6_TARGET_URL`, `OET_TEST_LEARNER_EMAIL`, and `OET_TEST_LEARNER_PASSWORD` through the existing `tests/load/lib/auth-helper.js` contract.
- The scenario tags `bootstrap`, `dashboard`, `subscription`, `entitlement-snapshot`, and `admin-dashboard` separately.
- Thresholds are `http_req_failed rate<0.01`, critical read P95 < 1000ms, and critical read P99 < 2000ms.

- [ ] **Step 1: Add a static test for safe-target and threshold declarations**

Use Node’s built-in test runner to assert the script contains no production hostname, declares `http_req_failed`, and does not contain `|| true`, `continue-on-error`, or an unconditional success exit.

- [ ] **Step 2: Implement the read-only k6 scenario**

Call `getToken()` once per VU, then issue the following authenticated GET requests in one iteration: `/v1/me/bootstrap`, `/v1/learner/dashboard`, `/v1/subscriptions/me`, and `/v1/me/entitlement-snapshot`. If `K6_INCLUDE_ADMIN=1`, require a separate admin identity contract before calling `/v1/admin/dashboard`; otherwise do not send admin traffic with learner credentials. Use stages of 10 VUs for 30 seconds, 50 VUs for 60 seconds, 100 VUs for 120 seconds, and ramp-down for 30 seconds.

- [ ] **Step 3: Run the static safety test**

Run: `node --test tests/load/critical-paths.k6.test.js`  
Expected: PASS without contacting any target URL.

- [ ] **Step 4: Commit the load scenario**

```powershell
git add -- tests/load/critical-paths.k6.js tests/load/critical-paths.k6.test.js
git commit -m "perf: add staging critical path load coverage"
```

### Task 5: Add the performance GitHub Actions workflow and artifact summary

**Files:**
- Create: `.github/workflows/performance.yml`
- Create: `scripts/perf/summarize-browser-reports.mjs`
- Create: `scripts/perf/summarize-browser-reports.test.mjs`

**Interfaces:**
- `workflow_dispatch` inputs: `target_url` and `api_url`, both required and intended for staging.
- Workflow secrets: `OET_PERF_LEARNER_EMAIL`, `OET_PERF_LEARNER_PASSWORD`; no secret values are written to summaries.
- The summary script reads JSON files from `output/performance/browser` and emits a Markdown table with route, project, LCP, FCP, INP, CLS, encoded JS/CSS, decoded JS/CSS, errors, and violations.

- [ ] **Step 1: Write summary-script tests using fixture JSON**

Create a temporary in-memory fixture in the Node test and assert the summary contains metric values and separate encoded/decoded columns, while omitting cookies, authorization values, and raw URLs.

- [ ] **Step 2: Implement the summary script**

Use `node:fs/promises` and `node:path`, sort reports by project and route, format missing metrics as `n/a`, and exit non-zero when any report contains violations and `PERF_ENFORCE_BUDGETS=1`.

- [ ] **Step 3: Define the workflow**

Use `ubuntu-latest`, Node 22, pnpm 10.33.0, `pnpm install --frozen-lockfile`, Playwright browser installation, `PLAYWRIGHT_BASE_URL`, `NEXT_PUBLIC_API_BASE_URL`, and `PERF_ENFORCE_BUDGETS=1`. Run the browser project and k6 script against the explicitly supplied staging target. Upload browser JSON/HTML and k6 JSON artifacts. Run the summary step even after a test failure, then fail the job if the performance gate failed.

- [ ] **Step 4: Add a production-target guard**

Fail before test execution when `target_url` or `api_url` contains `app.oetwithdrhesham.co.uk`, `api.oetwithdrhesham.co.uk`, or the production VPS address. The guard must print only the rejected hostname, never credentials.

- [ ] **Step 5: Run workflow YAML and script syntax checks**

Run: `node --check scripts/perf/summarize-browser-reports.mjs` and `git diff --check -- .github/workflows/performance.yml scripts/perf/summarize-browser-reports.mjs`  
Expected: both pass.

- [ ] **Step 6: Commit the performance workflow**

```powershell
git add -- .github/workflows/performance.yml scripts/perf/summarize-browser-reports.mjs
git commit -m "ci: add staging performance gates"
```

### Task 6: Route shared runtime changes through native CI

**Files:**
- Modify: `.github/workflows/mobile-ci.yml`
- Modify: `.github/workflows/tauri-ci.yml`

**Interfaces:**
- Mobile CI path filters include `app/providers.tsx`, `app/providers/**`, `components/mobile/**`, `components/shell/**`, `contexts/accessibility-context.tsx`, `hooks/use-media-preferences.ts`, `lib/query/**`, and the shared performance harness/config paths.
- Tauri CI path filters include `app/providers.tsx`, `components/shell/**`, `components/mobile/**`, `hooks/use-media-preferences.ts`, and the desktop-facing shared runtime paths.
- Existing build jobs and secrets remain unchanged.

- [ ] **Step 1: Add only shared-runtime paths to the mobile trigger**

Keep existing Android/iOS/auth paths and add the exact shared paths above. Do not broaden the workflow to every frontend file.

- [ ] **Step 2: Add only shell/runtime paths to the Tauri trigger**

Keep the Rust and bridge conformance jobs unchanged. Add `workflow_dispatch` so the native gate can be run explicitly for a performance release without a Rust-file edit.

- [ ] **Step 3: Validate YAML path and trigger changes**

Run: `git diff --check -- .github/workflows/mobile-ci.yml .github/workflows/tauri-ci.yml` and `rg -n 'app/providers|components/shell|components/mobile|workflow_dispatch' .github/workflows/mobile-ci.yml .github/workflows/tauri-ci.yml`  
Expected: the intended paths and manual Tauri trigger are present with no whitespace errors.

- [ ] **Step 4: Commit native trigger coverage**

```powershell
git add -- .github/workflows/mobile-ci.yml .github/workflows/tauri-ci.yml
git commit -m "ci: gate shared runtime changes on native builds"
```

### Task 7: Run focused validation and produce the delivery evidence

**Files:**
- Modify: `.github/agent-state.local.md`
- Create: `docs/performance/2026-08-07-performance-evidence.md`

**Interfaces:**
- The evidence document records exact commands, CI run IDs, web/API health responses, browser metric tables, k6 thresholds, native build statuses, and unverified physical-device boundaries.
- `.github/agent-state.local.md` records the latest goal, changed files, validation, blockers, measurements, and next step without secrets.

- [ ] **Step 1: Run the smallest local TypeScript and test checks**

Run: `pnpm exec vitest run tests/performance/metrics.test.ts tests/static/frontend-heavy-imports.test.ts --reporter=dot` and `pnpm exec tsc --noEmit --pretty false`  
Expected: focused tests pass; typecheck either passes or reports only an explicitly documented pre-existing failure.

- [ ] **Step 2: Run scoped lint and diff checks**

Run: `pnpm exec eslint tests/performance/metrics.ts tests/performance/metrics.test.ts tests/performance/auth.setup.ts tests/performance/browser-performance.spec.ts playwright.performance.config.ts scripts/perf/summarize-browser-reports.mjs` and `git diff --check`  
Expected: no new errors and no whitespace errors.

- [ ] **Step 3: Run the browser baseline locally when the stack is available**

Run: `PERF_ENFORCE_BUDGETS=0 pnpm exec playwright test -c playwright.performance.config.ts --project=perf-unauth-chromium --project=perf-learner-chromium tests/performance/browser-performance.spec.ts`  
Expected: reports are generated; unavailable local infrastructure is recorded as unverified rather than fabricated as a pass.

- [ ] **Step 4: Commit the complete implementation**

Stage only the explicit implementation, test, workflow, evidence, and state paths. Do not stage `.codex/config.toml`, `.superpowers/`, `.env*`, or generated auth/Playwright artifacts.

- [ ] **Step 5: Push `main` and monitor required workflows**

Run: `git push origin main`; then inspect `gh run list --branch main --limit 20` and the commit-specific `deploy`, `Mobile CI`, `Tauri Desktop CI`, `QA Smoke`, and performance workflow runs. Do not infer deployment from the push alone.

- [ ] **Step 6: Verify production health after blue/green deployment**

Run safe HTTP checks for web `/api/health` and API `/health/ready` using the repository’s approved production health endpoints. Record status and sanitized readiness fields only; do not run load or source-build commands on the VPS.

- [ ] **Step 7: Update evidence and final state**

Record the measured values, encoded versus decoded sizes, CI run IDs, deployment SHA, health results, and any native/physical-device limitation. Only claim complete delivery when each requirement has authoritative evidence.
