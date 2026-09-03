# Mobile Performance Hardening — Root Cause Analysis

Date: 2026-09-03
Branch: `perf/mobile-hardening-2026-09-03`
Scope: Header/safe-area (Issue 01), global navigation responsiveness (Issue 02), Dashboard performance (Issue 03) for the Capacitor Android shell.

## Architecture confirmed before any change

This is **not** a native Android or React Native app. It is the same Next.js 16 App Router web app (`app/`, `components/`, `lib/`) used at `app.oetwithdrhesham.co.uk`, wrapped by **Capacitor 6** for Android (`android/`) and iOS (`ios/`). `capacitor.config.ts` points the Android `WebView` at the **remote production URL** — there is no bundled/offline copy of the web app inside the APK. This changes what "native performance work" means here:

- The header/safe-area problem is a **native Android inset ↔ WebView CSS** integration problem (`MainActivity.java`, `styles.xml`, `AndroidManifest.xml`, `app/globals.css`), not a native layout XML problem.
- Global navigation and Dashboard performance are **the same React/Next.js rendering, routing, and TanStack Query concerns** that govern the web app, running inside a WebView instead of a browser tab. There is no separate "mobile" render path to fix independently.

## Issue 01 — Header / safe-area

**Symptom:** header collides with / crowds the system status bar on some large-screen Android devices (reported on Galaxy S24 Ultra-class hardware), while a normal phone looks correct.

**Actual root cause — two independent bugs, both required for the fix:**

1. **Native config was internally inconsistent.** `android/variables.gradle` targeted/compiled API 35 when this bug was first introduced (now bumped to API 36 by PR #181, merged after this investigation started — the same forced edge-to-edge behavior applies, and strengthens, at 36), which **force-enables edge-to-edge** on Android 15+ regardless of Capacitor's `StatusBar.overlaysWebView` setting (confirmed by this repo's own prior fix commit `beb4f2d43`: *"Android 15 (targetSdk 35) force-enables edge-to-edge regardless of the Capacitor StatusBar overlaysWebView:false setting, so the WebView draws under the status bar... while env(safe-area-inset-*) resolves to 0"*). The follow-up commit `2587b3544` "fixed" this by setting `android:windowOptOutEdgeToEdgeEnforcement="true"` on every theme — i.e. it told the WebView it was **not** edge-to-edge, while the OS could still render it edge-to-edge inconsistently across OEM WebView builds (this is the known, and now Google-deprecated, opt-out escape hatch — it is not guaranteed to be honored uniformly, especially on Samsung One UI). The result: on some devices the WebView draws under the status bar (real edge-to-edge) while `env(safe-area-inset-top)` still resolves to `0` (because the opt-out flag told the platform "no inset to report"), producing the exact "header collides with system chrome, no top gap" symptom — and it is device/OEM-dependent, matching "looks fine on a normal phone, wrong on an S24 Ultra-class device."
2. **The header's own CSS combined a fixed height with the same inset's padding on one box.** `components/layout/top-nav.tsx`'s `<motion.header>` carried both a fixed height (`h-14 lg:h-24` / `h-11 lg:h-12`) **and** `padding-top: env(safe-area-inset-top)` (via the `.safe-area-inset-top` utility) on the *same* element. Any non-zero inset there eats into the fixed height instead of adding to it, squeezing the icon row (36–44px tall) into whatever room is left below the padding — pushing icons off-center or up against the boundary. This bug exists independently of bug (1): it would misalign the header on **any** device the moment the safe-area inset is genuinely non-zero (which is the correct, intended state everywhere once edge-to-edge is embraced properly).

**Code locations:** `android/app/src/main/AndroidManifest.xml`, `android/app/src/main/res/values/styles.xml`, `android/app/src/main/java/com/oetprep/learner/MainActivity.java`, `capacitor.config.ts`, `lib/mobile/runtime.ts` (`syncNativeChrome`), `app/globals.css` (`:root`, `.safe-area-inset-*` utilities), `components/layout/top-nav.tsx`.

See `02-header-safe-area-fix.md` for the fix and `06-device-test-matrix.md` for the verification procedure (a physical-device boundary — see `07-release-verification.md`).

## Issue 02 — Global navigation responsiveness

**Symptom:** navigation feels like full page reloads instead of native transitions; side nav feels delayed; UI appears to wait for data.

**Actual root cause (ranked by impact), confirmed by direct code inspection:**

1. **No persistent shell layout for the learner surface.** `components/layout/app-shell.tsx` / `LearnerDashboardShell` is instantiated **inside each of ~202 individual `page.tsx` files**, not in a shared Next.js `layout.tsx`. Admin (`app/admin/layout.tsx`) and expert/tutor (`app/expert/layout.tsx`, `app/tutor/layout.tsx`) already do this correctly — they render their shell once in a `layout.tsx` and don't have this problem. For learners (the large majority of routes and users), every navigation between top-level sections **fully unmounts and remounts** `AuthGuard`, `Sidebar`, `TopNav`, and `BottomNav`, which is what makes navigation read as "reload-like" rather than a native transition. This is the single largest lever for Issue 02, and it is **not fixed in this pass** — see `03-global-performance-analysis.md` and `docs/mobile-performance/04-dashboard-performance-analysis.md` for why a blind, whole-tree migration was judged too high-risk to execute without device/browser verification, and for the scoped follow-up plan.
2. **An uncached fetch effect sat directly on the remount path.** `LearnerStreakBadges` (rendered inside `TopNav`, which remounts on every navigation per (1)) called `fetchStreak()`/`fetchXP()` in a bare `useEffect`, with no caching — two fresh network requests fired on **every single tap**, and the badges visibly disappeared/reappeared each time. **Fixed** in this pass (moved onto the shared TanStack Query client).
3. **Duplicate entitlement fetching.** `hooks/use-enabled-modules.ts` (used by `Sidebar`, `BottomNav`, and the skill switcher) had its own hand-rolled, module-level single-flight cache calling `/v1/me/entitlement-snapshot` directly, **independent of** the Dashboard's own React Query cache for the exact same endpoint — two uncoordinated caches hitting the same data, and a purchase-success invalidation on one didn't refresh the other. **Fixed** in this pass (both now share one TanStack Query cache key).
4. **`prefetch={false}` on every primary nav `Link`** (`Sidebar`, `BottomNav`, `TopNav`) removes the opportunity for Next.js to warm a destination route ahead of a tap. **This was investigated and deliberately left as-is** — `docs/performance/2026-08-07-performance-evidence.md` records that disabling this exact prefetching was part of a **measured, CI-budget-gated fix** for a first-paint (LCP/FCP) regression on the initial learner Dashboard load. Reverting it without being able to reproduce that same browser-budget CI gate in this session would risk silently reintroducing an already-fixed regression on a different, already-hardened metric. See `03-global-performance-analysis.md` for the full reasoning and the recommended (but not executed) follow-up: intent-based (hover/idle) prefetch instead of blanket prefetch.

## Issue 03 — Dashboard performance

**Symptom:** Dashboard is slow to open, leave, and return to.

**Actual root cause, confirmed by direct code inspection:**

1. `/dashboard` is a literal re-export of `/` (`app/dashboard/page.tsx: export { default } from '../page'`) — there is one Dashboard implementation, `app/page.tsx`.
2. The Dashboard already uses TanStack Query correctly for its own 9 direct queries (parallel, not sequential) — this part of the architecture was **not** a bottleneck.
3. The actual duplicate-request and staleness issues traced to the **same** two causes as Issue 02 (#2 and #3 above): the entitlement snapshot was fetched twice (once by the Dashboard's own `entitlementQuery`, once by the nav chrome's separate cache), and several slow-changing, mutation-invalidated queries (entitlement, subscription, ai-package-credits, scoring policy) carried short (30–60s) `staleTime`s that triggered an avoidable full background-refetch burst on every "leave Dashboard, do something, come back" cycle even though nothing had actually changed. **Fixed** in this pass.
4. The Dashboard's perceived remount cost when leaving/returning is the **same architectural cause as Issue 02 #1** (no persistent shell) — leaving and returning to `/` remounts the whole shell, not just the Dashboard content. Not fixed in this pass for the reasons above; documented as the primary follow-up.

## Summary table

| Root cause | Issue(s) | Status |
|---|---|---|
| `windowOptOutEdgeToEdgeEnforcement` opt-out + inconsistent WebView inset reporting | 01 | **Fixed** |
| Header combines fixed height with safe-area padding on one box | 01 | **Fixed** |
| No persistent learner shell layout (202 `page.tsx` instantiate their own shell) | 02, 03 | Root-caused, **not executed** — see follow-up plan |
| Uncached streak/XP fetch on every remount | 02 | **Fixed** |
| Duplicate entitlement-snapshot cache (nav vs. dashboard) | 02, 03 | **Fixed** |
| Short staleTime on mutation-invalidated dashboard queries | 03 | **Fixed** |
| `prefetch={false}` on primary nav links | 02 | Investigated, **deliberately left unchanged** (prior measured fix) |
| AuthGuard blocks the whole shell behind session-restore | 02, 03 | Investigated, **left unchanged** — narrow real impact (cold start only; `AuthProvider` is root-mounted and does not re-run per navigation) and security-sensitive to touch without device verification |
