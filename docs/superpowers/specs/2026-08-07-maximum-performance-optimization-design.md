# Maximum Cross-Platform Performance Optimization Design

**Date:** 2026-08-07
**Status:** Approved for implementation
**Scope:** Web, mobile web, Android, iOS, Windows desktop, macOS desktop, API staging load, and delivery evidence.

## Goal

Make the OET platform measurably fast, responsive, stable, and recoverable across public web, authenticated learner/admin web, mobile browser, Capacitor Android/iOS shells, and Tauri desktop shells. Preserve the existing measured startup/runtime changes in `e87b5f051` while adding repeatable browser budgets, safe staging load coverage, and native CI gates.

## Design decisions

### 1. CI-first performance proof with bounded local feedback

GitHub Actions is the authoritative environment for heavy Next.js, Android, iOS, and Tauri builds. Local Windows checks provide fast, focused feedback only. The production VPS is never used for load testing or source builds.

The performance wave uses the existing `mobile-ci.yml`, `tauri-ci.yml`, `speaking-load.yml` patterns and adds a general performance harness where existing workflows do not provide coverage. Native workflow path filters will include shared runtime/provider surfaces that can affect a shell even when `android/`, `ios/`, or `src-tauri/` are unchanged.

### 2. User-centric metrics with honest resource accounting

The browser harness measures representative routes in desktop Chromium, Pixel 7 Chromium, iPhone 14 WebKit, Firefox, and Safari/WebKit where the runner supports them. It records navigation timing, Core Web Vitals, long tasks, route errors, horizontal overflow, and resource timing.

JavaScript and CSS are reported using both `transferSize`/encoded bytes and `decodedBodySize`/decoded bytes. A transfer-size result is never presented as a decoded bundle-size result.

Acceptance targets are:

- LCP at or below 2.5 seconds.
- FCP at or below 1.8 seconds.
- INP at or below 200 milliseconds when interaction samples are available.
- CLS at or below 0.1.
- No uncaught page errors, failed critical resources, horizontal overflow, clipped primary content, or browser-reload recovery on the tested routes.
- Critical staging API workloads at P95 below 1 second, P99 below 2 seconds, and error rate below 1 percent.

The first run records a fresh baseline for the current `main` state. Historical `/sign-in` measurements are treated as comparison context, not as current proof; the harness must remeasure them before claiming an improvement.

### 3. Safe, staging-only load testing

The k6 suite targets `STAGING_API_URL` only and uses dedicated test credentials supplied through GitHub Actions secrets. It exercises read-heavy critical journeys first: authentication/bootstrap, learner profile/subscription/dashboard data, and one representative admin read path. Any mutating scenario must use isolated staging fixtures and idempotent cleanup.

The workflow fails its performance gate when k6 thresholds fail, while still uploading JSON summaries for diagnosis. It must not use production URLs, customer credentials, real customer identifiers, or a workflow pattern that converts threshold failures into a green result.

### 4. Native coverage is build and smoke evidence, not an invented device claim

The native lanes will provide:

- Android debug APK compilation after a production-URL Next.js build and Capacitor sync.
- iOS simulator compilation on `macos-latest` after the same web build and Capacitor sync.
- Windows Tauri format, clippy, tests, and compile smoke through the existing Tauri CI lane.
- macOS Tauri build evidence through the existing release/build lane when its runner and signing constraints permit it.

If no physical device or desktop installation is available to the agent, the final report will explicitly mark physical touch, scroll, audio/video, background/resume, battery, and network-transition behavior as unverified. CI compilation and browser emulation will not be presented as physical-device acceptance.

## Architecture

### Browser measurement harness

Create a focused Playwright performance harness under `tests/performance/` with reusable route definitions and a metric collector. The collector will:

1. Navigate to a route with a clean page context.
2. Wait for the route’s stable readiness signal without arbitrary long sleeps.
3. Read navigation/resource timing and `PerformanceObserver` entries from the page.
4. Capture uncaught exceptions, failed requests, layout overflow, and primary-content visibility.
5. Emit a versioned JSON report and a concise human-readable summary.

The harness will support unauthenticated `/sign-in` and authenticated learner/admin routes using the repository’s existing Playwright storage-state setup. It will not log cookies, authorization headers, response bodies, or user data.

### API load harness

Create a general k6 scenario under `tests/load/` that reuses the existing auth helper and tags each endpoint. Thresholds will be declared in the script so local and CI runs have identical acceptance rules. The workflow will accept only an explicit staging target and dedicated test identity variables.

### CI integration

Create a manual/scheduled performance workflow that:

- Installs dependencies and browser tooling.
- Runs the browser performance harness against the configured staging/web target.
- Runs the staging k6 scenario.
- Uploads JSON/HTML/summary artifacts.
- Exits non-zero on threshold violations.

Update shared-path filters for Mobile CI and Tauri CI so changes to root providers, runtime bridges, media-preference behavior, accessibility startup behavior, and shell gates cannot bypass native compilation gates.

### Existing application optimizations

The implementation must preserve and measure the existing changes in `e87b5f051`, including deferred native startup, authenticated workspace provider boundaries, bounded promotional media, media preload behavior, query-key reuse, and in-place retry recovery. Any additional optimization must be justified by a measurement or a failing budget.

## Error handling and rollback

- Missing staging URL or test credentials: fail before sending requests and explain the missing configuration without printing secret values.
- Browser route failure, failed critical resource, or metric collection failure: fail the browser job and upload the sanitized report.
- k6 threshold failure: fail the load job and retain summaries; do not suppress the failure.
- Native build failure: fail the corresponding platform job and preserve build logs/artifacts.
- Performance regressions after deployment: stop further rollout, retain the previous healthy image/release, and use the existing blue/green deployment rollback path.
- No production load generation, secret file reads, or direct VPS builds.

## Validation and delivery

Validation proceeds in this order:

1. Run the new browser harness against the current state and save the baseline evidence.
2. Add the smallest measurement-backed code or workflow changes needed to satisfy the design.
3. Run focused TypeScript/lint/unit checks for the changed harness and workflow-adjacent code.
4. Run the staging k6 workflow with dedicated test configuration.
5. Run Mobile CI and Tauri CI, including Android/iOS build evidence and desktop compile/test evidence.
6. Push the approved implementation to `main`, allow the existing blue/green deployment, and verify web `/api/health` plus API `/health/ready`.
7. Report exact metrics, encoded versus decoded resource sizes, CI run identifiers, native platform status, and every remaining external boundary.

The final claim will distinguish source/configuration changes, CI build health, production deployment, production health, and physical-device acceptance.

## Non-goals

- No production load or stress testing.
- No third-party APM/vendor rollout without an existing configured account and explicit scope.
- No broad rewrite of application architecture or domain behavior.
- No change to authentication, CSP nonces, native attestation, media security, scoring, rulebooks, storage, or entitlement contracts except where a measured performance change is proven safe.
- No signed mobile/desktop release claim without the required signing material and release workflow evidence.
