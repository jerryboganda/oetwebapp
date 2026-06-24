# TEST_PLAN.md — OET Prep Learner (Mobile) Production-Readiness

**App:** OET Prep Learner · **Package:** `com.oetprep.learner` · **Stack:** Capacitor 7 (remote-URL hybrid) over Next.js 16 / React 19, .NET 10 API
**Branch:** `qa/production-readiness` (worktree `D:/Projects/oet-qa-prod-readiness`) · **Owner:** QA engagement · **Status:** living document

> **Architecture note that shapes the whole plan.** This is a **remote-URL Capacitor app**: `capacitor.config.ts` sets `server.url` to a live web URL and the native app loads that site in a WebView (`webDir: capacitor-web` is a one-line redirect stub — no bundled SPA). Therefore "testing the web app" ≈ testing whatever URL the build targets, and the *mobile-specific* surface under test is the **native shell**: runtime permissions, deep links / App Links, push, biometric unlock, secure token storage, audio recording, app lifecycle, and how the live site renders/behaves inside the WebView (CSP intersection, keyboard, safe-area, back button).

## 1. Scope & platforms
- **Android** — full scope, **delivered to testers** this round (signed APK sideload + AAB).
- **iOS** — hardened and verified **up to the signing gate** only (no paid Apple Developer account yet). Signed IPA / TestFlight are **out of scope** until the $99/yr account exists. Unsigned `xcodebuild` build-check, Info.plist/Privacy-Manifest hardening, and a release-workflow skeleton are in scope.

## 2. Risk register (ranked)
| # | Risk | Sev | Mitigation / test |
|---|------|-----|-------------------|
| R1 | Push is fully coded but **dormant** (no `google-services.json` / `GoogleService-Info.plist`) | High* | Ship dormant by decision; verify graceful no-op; document Firebase/APNs provisioning to enable. *High only if push is required for launch. |
| R5 | Remote-URL app → testers' actions hit whatever URL the build targets; **no offline fallback** | Med | Target a **staging URL** for tester builds (decision pending); parameterize `CAPACITOR_APP_URL`; test airplane/offline behavior + error UX. |
| R4 | No real-device E2E (Playwright emulation only) | Med | Add Maestro smoke flows on an Android emulator; manual matrix on real/emulated devices. |
| R3 | iOS `UIRequiredDeviceCapabilities=armv7` (legacy 32-bit) | Med | Change to `arm64`; rebuild-check. |
| R2 | `next build` ignores type errors (`typescript.ignoreBuildErrors:true`); type safety gated only by CI `tsc` | Med | Keep CI `tsc` mandatory; evaluate removing the flag; document. |
| R6 | No coverage measurement/gate | Low-Med | Add Vitest coverage; report numbers; gate critical-path modules. |
| R7 | Manual icons/splash; `versionCode/Name` still 1/1.0 | Low | Adopt `@capacitor/assets`; set real version before build. |
| R8 | `@aparajita/capacitor-secure-storage` pinned v6 vs v7 elsewhere; stray `types/*.d.ts` shim | Low | Confirm runtime OK; plan upgrade; remove stray shim if unused. |
| R9 | `style-src 'unsafe-inline'` (Tailwind) | Low (accepted) | Documented tradeoff; revisit with CSS-modules later. |

## 3. Test strategy (by dimension → phase)
- **Static analysis & hygiene (Ph2):** `tsc --noEmit`, ESLint, Vitest, `next build`, `pnpm audit`, `./gradlew lint`; secret scan; `.gitignore` audit.
- **Security (Ph3):** permission least-privilege; CSP/ATS/Privacy-Manifest; release minify + WebView debug off; deep-link verification files.
- **Automated tests (Ph4):** unit gaps on mobile-critical modules; Maestro smoke on emulator.
- **Manual/exploratory (Ph5):** full device matrix; permissions grant/deny/permanently-denied; connectivity transitions; lifecycle/low-memory/restore; deep-link cold start; Android back; push (if provisioned).
- **Production readiness (Ph6):** crash reporting (Sentry) on native shell; error UX; perf (cold start, jank, memory, artifact size); versioning; assets; update strategy.
- **CI/CD (Ph7):** audit existing workflows; close gaps; coverage; staging URL param.
- **Signing & delivery (Ph8–9):** keystore + signed APK/AAB; install verification; sizes; links.

## 4. Device / OS matrix
**Android (primary):**
| Class | Example | API/OS | Why |
|-------|---------|--------|-----|
| Small phone | ≤5.5" | API 24–26 | min-ish SDK, small viewport, safe-area |
| Modern large | Pixel-class | API 34/35 | current target, gestures, POST_NOTIFICATIONS |
| Budget/mid | mid-tier | API 29–31 | memory pressure, slower CPU |
| Tablet (opt.) | 10" | API 33+ | layout scaling |
| Foldable (opt.) | — | API 33+ | configChange resilience |

**iOS (verification only, no on-device tester install):** iOS 15/16 (older supported), iOS 17/18 (current), iPad — exercised in Simulator / unsigned build-check only.

**Conditions per device:** portrait↔landscape; notch/safe-area insets; keyboard overlap; permission grant + deny + permanently-denied (mic, camera, notifications); offline / airplane / wifi↔cellular mid-action; background→foreground, resume-after-suspend, low-memory kill + state restore; deep-link cold start (`https://…` App Link + `oet-prep://open`); Android hardware back; push delivery (only if FCM/APNs provisioned).

## 5. Critical user flows (must never break)
Auth/login incl. biometric unlock · Dashboard · Speaking (mic record + AI mark) · Listening (per-section audio) · Reading (PDF papers) · Writing (submission + feedback) · Mock exams · Billing/checkout (Stripe + PayPal) + subscription/freeze · Vocab/recall · Deep-link + device pairing (`oet-prep://open`, `/pair?code=`) · Push routing (if provisioned).

## 6. Entry / exit criteria
- **Entry:** clean `qa/production-readiness` off `main`; deps installed; this plan approved.
- **Exit (go for internal testing):** CI green (tsc+lint+tests+Android build); all Critical/High bugs fixed (rest logged with severity); least-privilege + CSP/ATS/Privacy-Manifest verified; no secrets in repo/bundle; release minified + WebView debug off; signed APK/AAB build reproducibly and install on a fresh device; tester docs + links complete.

## 7. Open inputs / decisions
- **Staging URL** for tester builds — required before Phase 9 (else explicit production fallback with billing guardrails).
- **Push**: ship **dormant** (default) unless Firebase/APNs credentials are provided.
- **Distribution channel**: direct signed APK (default) ± Firebase App Distribution (free, optional managed channel).
