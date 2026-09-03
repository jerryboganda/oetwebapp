# Release Verification

## What ran in this session and what it proves

| Check | Command | Result |
|---|---|---|
| TypeScript | `pnpm exec tsc --noEmit` (full repo) | 3 pre-existing errors (`components/admin/listening/part-bc-source-recovery-panel.tsx`, `lib/types/expert.ts` — both were `git status` **modified before this session started**, unrelated to this work). **0 errors in any file this session touched.** |
| Lint | `pnpm run lint` (full repo) | 0 errors, 465 pre-existing warnings, none in files this session touched. |
| Unit tests — touched areas | `vitest run` on `app-shell`, `learner-dashboard-shell`, `sidebar-route-matching`, `feature-flag-nav`, `top-nav` (new), `expert-dashboard-shell`, `notification-center`, `use-dashboard-home`, `mobile-runtime`, `lib/query/hooks` | 33/33 passing. |
| Unit tests — broad regression sweep | `vitest run` on 59 files (217 tests) using the shared `renderWithRouter` test helper this session modified | 203/217 passing; the 14 failures (2 files) reproduced identically against the pristine pre-session baseline (via `git stash`) — confirmed pre-existing, not introduced. |
| New regression test | `components/layout/__tests__/top-nav.test.tsx` | Written to encode the Issue-01 root cause; verified to fail against the pre-fix header structure and pass against the fix. |
| Android/native compile | — | **Not run.** No JDK, no `ANDROID_HOME`, no Android SDK available in this environment (`java: command not found`, `ANDROID_HOME` unset — confirmed directly). Per this repo's own `AGENTS.md`/`CLAUDE.md`, Android builds run on CI (`Mobile CI` GitHub Actions workflow), not locally. |
| Release-mode / production-build behavior | — | **Not run**, for the same reason. |
| Browser-budget / device performance gate | — | **Not run.** This repo already has the right infrastructure for this (`docs/performance/2026-08-07-performance-evidence.md`'s isolated staging-like GitHub Actions stack with Playwright LCP/FCP/CLS budgets and k6 load testing) but it requires Docker/CI, unavailable interactively in this session. |

## MainActivity.java — manual review in lieu of a compiler

Since Java could not be compiled locally, `android/app/src/main/java/com/oetprep/learner/MainActivity.java` was manually re-reviewed line by line after writing it, and one real bug was caught and fixed during that review: the `OnApplyWindowInsetsListener` lambda's `view` parameter is typed as the generic `android.view.View` (the interface's own signature), not `WebView` — the first draft incorrectly passed it to a method expecting `WebView`, which would have failed to compile. Fixed by reusing the already-`WebView`-typed `webView` local the listener was attached to (captured via lambda closure) instead. The API surface used (`getBridge().getWebView()`, `WindowCompat.setDecorFitsSystemWindows`, `ViewCompat.setOnApplyWindowInsetsListener`, `WindowInsetsCompat.Type.statusBars()/navigationBars()/displayCutout()`, `WebView.evaluateJavascript`) is stable, well-documented AndroidX/Capacitor public API already available at this project's `androidxCoreVersion` (1.12.0) and `@capacitor/android` version — no new dependency was added.

**This file must still be compiled by `Mobile CI` (or a local Android Studio/`gradlew assembleDebug` run with a JDK) before merging.** That is the actual acceptance gate for this specific file; static review reduces but does not eliminate the risk of a compile error CI would catch immediately.

## What remains a real, stated risk

1. **Physical-device header verification** (`06-device-test-matrix.md`) — the actual accountability for Issue 01 is "does the header look right on an S24 Ultra," which this session cannot observe. The fix is architecturally sound (root-caused against this repo's own prior bug commits, no device-specific branches), but "looks right" is a visual claim only a device or screenshot can settle.
2. **The shell-remount fix for Issues 02/03 was root-caused but not executed** (`03-global-performance-analysis.md`, `04-dashboard-performance-analysis.md`) — this is the single largest remaining lever for "navigation feels like a reload" and "dashboard return isn't instant," deliberately deferred because a correct fix touches ~200 route files on a live production app with no way to visually verify the result here.
3. **Intent-based prefetch was recommended, not implemented** (`03-global-performance-analysis.md` §E) — reverting the existing `prefetch={false}` blindly risked reintroducing a previously-measured, CI-gated LCP/FCP regression; a safer alternative (hover/touch-intent prefetch) is recommended but needs the same `docs/performance/`-style CI gate to validate before shipping.
4. **iOS was not touched.** This work is scoped to the Android edge-to-edge bug specifically (the reported symptom, and the platform where `windowOptOutEdgeToEdgeEnforcement` and Android 15's forced edge-to-edge apply). `ios/` was not modified. iOS's WKWebView safe-area handling was not reported as broken and nothing in this change should affect it (the `:root` CSS default is untouched `env()`), but it was not re-verified.

## Recommendation before merging to `main`

1. Push `perf/mobile-hardening-2026-09-03` and let `Mobile CI` run (lint/typecheck/mobile unit tests, iOS build+simulator, Android debug build, Android emulator smoke) — this is the actual compile gate for the native changes.
2. Run (or ask the owner to run) the device test matrix in `06-device-test-matrix.md` on at minimum Device A and Device B before considering Issue 01 closed.
3. If pursuing the deferred persistent-shell fix (`03-global-performance-analysis.md`), do it as its own reviewable PR per route-subtree, not folded into this one.
4. This repo's `AGENTS.md`/`CLAUDE.md` "ship-it" directive (push to `main`, make the repo public, deploy) is calibrated for small/routine changes verified by a quick local check. This change set is larger (native Android code + a repo-wide shared test-utility change + a cross-cutting caching change) and cannot be visually verified in this session — it was **not** auto-pushed/deployed. It sits on the feature branch above, committed, ready for the owner's review and the CI gates in this document.
