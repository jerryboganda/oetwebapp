# Device Test Matrix & Reproducible Verification Procedure

No physical device, emulator, or browser was available in this session (see `07-release-verification.md`). This document is the exact, reproducible procedure to run the header/safe-area and navigation acceptance tests once a device or CI runner is available — it is written so anyone (owner, CI, or a future agent session) can execute it without re-deriving anything.

## Required devices/profiles

| Device | Why |
|---|---|
| **A — Normal Android phone** (e.g. Pixel 7/8, ~6.1", standard punch-hole) | Reference: must remain correct (no regression). |
| **B — Galaxy S24 Ultra-class** (6.8", tall aspect ratio, centered punch-hole; or any large/tall Samsung One UI device) | The originally-reported device class. |
| **C — Small/tall phone** if available (e.g. compact Android phone) | Confirms the fix isn't tuned to one screen size. |
| **D — Android tablet**, if the app is tested there | Confirms large-screen non-phone behavior. |

## Build to test

```bash
pnpm run mobile:build      # builds the web bundle capacitor-web consumes for local dev/testing
pnpm run mobile:sync       # capacitor sync — copies web assets, updates native deps
cd android && ./gradlew assembleDebug   # debug APK; use bundleRelease for a release-mode check
```

Per `AGENTS.md`/`CLAUDE.md`, Android release builds run via CI (`Mobile CI` / `Mobile Release` workflows) — this branch (`perf/mobile-hardening-2026-09-03`) should be pushed and that pipeline run before relying on a local build result.

## Test 1 — Normal phone header (Device A)

**Expected:** header unchanged from the existing correct reference — hamburger, logo, notification bell, theme toggle, avatar all on one centerline; no gap regression from before this fix.
**Procedure:** launch app → observe header on `/` (Dashboard) and one other route (e.g. `/listening`) → compare against a pre-fix build or screenshot if available.

## Test 2 — Galaxy S24 Ultra-class header (Device B)

**Expected:** status bar respected, no collision, no upward displacement, icons centered, no excessive empty space above the header.
**Procedure:** same as Test 1, on device B. Additionally rotate the device (if the app supports landscape) and confirm the header re-centers without a stale/frozen inset (this exercises `MainActivity`'s `OnApplyWindowInsetsListener`, which re-fires on every inset change including rotation).

## Test 3 — Different safe-area height (any device with a notch/cutout, or Device C)

**Expected:** header adapts automatically; no code change required per device.
**Procedure:** same as Test 1. Confirms `--safe-area-inset-top` (bridged in `MainActivity.java`) tracks the live inset rather than a fixed value — there is no per-device branch to inspect in the code, so this test is really confirming the *result*, not a code path.

## Test 4 — Open side navigation while Dashboard data is loading

**Expected:** immediate visual response opening/closing the hamburger menu; no freeze while the 12 Dashboard queries (see `04-dashboard-performance-analysis.md`) are in flight.
**Procedure:** cold-launch into `/`, open the mobile menu (hamburger) within the first second, before Dashboard queries resolve.

## Test 5 — Switch navigation destination

**Expected:** destination shell appears promptly; per `03-global-performance-analysis.md` §C this is **not yet fully instant** (shell remount is a documented, unexecuted follow-up) — record actual behavior rather than assuming pass.
**Procedure:** from `/`, tap "Listening" in the bottom nav or sidebar; observe whether the header/sidebar visibly flash/rebuild vs. persist.

## Test 6 — Open Dashboard cold

**Expected:** shell/hero visible immediately; widgets populate progressively (not one blocking spinner for everything).
**Procedure:** cold-launch into `/`.

## Test 7 — Return to Dashboard warm

**Expected:** with this session's staleTime fix, entitlement/subscription/ai-credits/scoring-policy should **not** visibly re-fetch (no flash of new data) within ~2–5 minutes of the previous visit; study-plan/readiness/dashboard-home may still refresh quietly in the background (unchanged, intentional — see `04-dashboard-performance-analysis.md` §3).
**Procedure:** visit `/`, navigate away, wait 30s, return; use a network inspector (`chrome://inspect` on the WebView, or Android Studio's Network Profiler) to confirm which of the 12 Dashboard-related requests actually re-fire.

## Test 8 — Dashboard API request count

**Expected:** 12 requests on cold open (down from 13 — see `05-before-after-performance.md`), with `/v1/me/entitlement-snapshot` appearing exactly once.
**Procedure:** same network inspector as Test 7, on a cold `/` load.

## Test 9 — Rapid route switching

**Expected:** no uncontrolled duplicate-request storm, no state corruption. Not specifically hardened in this pass beyond the dedup work above — record actual behavior.

## Test 10 — Network slowdown

**Expected:** shell/nav remain usable; Dashboard's existing `AsyncStateWrapper` skeleton gates only the two critical queries (tasks/profile), not all 12 — verify this holds under throttling.
**Procedure:** enable network throttling in the WebView inspector (or Android's Developer Options network speed limiter) and repeat Test 6.

## Test 11 — Failed request

**Expected:** app remains responsive; `useEnabledModules`'s fail-open gate (`isModuleEnabled` treats an unresolved/failed entitlement query as "show everything" rather than hiding nav items) should hold — this behavior was preserved, not changed, in this session's rewrite of `hooks/use-enabled-modules.ts`.
**Procedure:** block `/v1/me/entitlement-snapshot` in the network inspector and confirm nav items remain visible rather than disappearing.

## Test 12 — Background/foreground

**Expected:** no unnecessary full reload. `lib/mobile/runtime.ts`'s `appStateChange` handler (unchanged in this pass beyond the `overlaysWebView` value) re-syncs status bar/viewport metrics on resume, not a full reload.
**Procedure:** background the app, wait, foreground it, confirm header/Dashboard state persists.

## What this session could verify without a device

Everything upstream of "run the built APK on hardware": the exact native/CSS code paths involved (`02-header-safe-area-fix.md`), that the change compiles conceptually correct Java (manually reviewed line-by-line — no local JDK to compile-check; see `07-release-verification.md`), that no TypeScript/lint/unit-test regression was introduced (`05-before-after-performance.md`), and that the new regression test fails against the old structure and passes against the new one.
