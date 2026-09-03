# Issue 01 — Header / Safe-Area Fix

## What changed and why (root cause, not a device-model workaround)

### 1. Native: stop opting out of edge-to-edge; bridge real insets instead

- `android/app/src/main/res/values/styles.xml`, `android/app/src/main/AndroidManifest.xml`: removed `android:windowOptOutEdgeToEdgeEnforcement`. This flag is Google's temporary, inconsistently-honored escape hatch from Android 15's enforced edge-to-edge — it does not reliably prevent the WebView from drawing under the status bar on every OEM/WebView build, and Google has signalled it will not exist indefinitely. Fighting the platform default here was the actual source of Issue 01's device-dependent inconsistency.
- `android/app/src/main/java/com/oetprep/learner/MainActivity.java`: now explicitly calls `WindowCompat.setDecorFitsSystemWindows(getWindow(), false)` (making edge-to-edge intentional rather than ambient/inconsistent) and installs a `ViewCompat.setOnApplyWindowInsetsListener` directly on the Capacitor `WebView` (`getBridge().getWebView()`). On every inset change the platform reports (initial layout, rotation, display-cutout mode change, multi-window resize) it reads `WindowInsetsCompat.Type.statusBars() | navigationBars() | displayCutout()`, converts Android px → CSS px (`÷ density`), and pushes the four values into the WebView via `evaluateJavascript` as CSS custom properties: `--safe-area-inset-{top,right,bottom,left}`. **There is no device model check, no hardcoded pixel value, and no breakpoint anywhere in this code** — it reads whatever the OS reports for the device it's running on, live.
- `capacitor.config.ts` (`StatusBar.overlaysWebView`) and `lib/mobile/runtime.ts` (`syncNativeChrome`'s `StatusBar.setOverlaysWebView`) were flipped from `false` to `true` to stop fighting the same edge-to-edge state from the JS/plugin side — previously `overlaysWebView:false` was already a fiction on Android 15 (per the bug this repo's own commit `beb4f2d43` documented), and leaving it `false` risked the plugin re-toggling `setDecorFitsSystemWindows` back on every resume/theme change, undoing MainActivity's setup.

### 2. Web: `env(safe-area-inset-*)` → bridgeable CSS variables, with `env()` fallback

`app/globals.css` `:root` now defines:

```css
--safe-area-inset-top: env(safe-area-inset-top);
--safe-area-inset-right: env(safe-area-inset-right);
--safe-area-inset-bottom: env(safe-area-inset-bottom);
--safe-area-inset-left: env(safe-area-inset-left);
```

MainActivity's bridge overrides these same variable names via an inline style on `<html>`, which wins the CSS cascade over the stylesheet default regardless of specificity. Every platform this app runs on is covered:

- **Android** (this fix's target): gets the live, native-bridged value.
- **iOS / web browsers**: untouched — they already compute `env(safe-area-inset-*)` correctly themselves (Apple's WKWebView safe-area support is reliable), so the `:root` default keeps working exactly as before.

`.safe-area-inset-top/bottom/left/right`, `.overlay-safe-area`, `.keyboard-safe-bottom`, `.keyboard-safe-floating-bottom` (`app/globals.css`) were updated to reference `var(--safe-area-inset-*)` instead of the raw `env()` function — this is the single point of truth every consumer of these utility classes benefits from without per-file changes. `components/layout/app-shell.tsx` and `components/auth/auth-guard.tsx`'s bottom-nav content padding, and `top-nav.tsx`'s mobile-menu dropdown positioning, were updated to the same `var()` form for consistency within the files this fix already touches.

The ~28 other files in the repo that call `env(safe-area-inset-*)` directly (modals, drawers, overlays — not part of the reported header/nav-bar issue) were **not** touched. Making the native side genuinely, consistently edge-to-edge (item 1 above) is expected to make the WebView's own native `env()` computation correct for them too, since the root cause of `env()` reading `0` was the platform being told it *wasn't* edge-to-edge while sometimes rendering as if it were — a consistent edge-to-edge state fixes `env()` at the source for every consumer, not just the ones migrated to the bridged variable. This is called out as a residual, low-priority verification item in `07-release-verification.md`.

### 3. Header box model: split the safe-area padding from the fixed-height icon row

`components/layout/top-nav.tsx`'s `<motion.header>` used to declare **both** a fixed height (`h-14 lg:h-24` / `h-11 lg:h-12`) and `padding-top: var(--safe-area-inset-top)` (via the `safe-area-inset-top` utility class) on the same element. Restructured to:

```
<motion.header class="... safe-area-inset-top ...">      ← no fixed height; grows to (inset + content)
  <div class="flex items-center gap-3 ... h-14 lg:h-24">  ← fixed height; always has full room
    (hamburger / logo / search / actions / avatar)
  </div>
</motion.header>
```

The outer element keeps the border/background/sticky-positioning/safe-area padding (so the header's background color still extends up behind the status bar, and the divider still sits directly under the icon row). The inner row keeps the fixed content height, so the hamburger, logo, notification bell, theme toggle, and avatar are **always** vertically centered within their full declared height, independent of how tall the safe-area inset is on that particular device. This is what actually guarantees "no abnormal upward displacement" and "icons remain vertically centered" regardless of device — the previous structure would have misaligned the header on **any** device with a genuinely non-zero, correctly-reported inset, not just the S24 Ultra.

## Files affected

- `android/app/src/main/AndroidManifest.xml`
- `android/app/src/main/res/values/styles.xml`
- `android/app/src/main/java/com/oetprep/learner/MainActivity.java`
- `capacitor.config.ts`
- `lib/mobile/runtime.ts`
- `app/globals.css`
- `components/layout/top-nav.tsx`
- `components/layout/app-shell.tsx`, `components/auth/auth-guard.tsx` (bottom-nav content padding, same var migration)
- `components/layout/__tests__/top-nav.test.tsx` (new — regression guard, see below)

## Device-specific hacks used

**None.** No `Build.MODEL`/device-name check, no hardcoded top-offset constant, no per-breakpoint safe-area value anywhere in this change. Every value flows from live `WindowInsetsCompat` (Android) or `env()` (everywhere else).

## Regression protection

`components/layout/__tests__/top-nav.test.tsx` asserts the header structure directly: the safe-area-padded element carries no fixed-height class, and the fixed-height content row is a distinct element from it. This test was verified to **fail** against the pre-fix header structure (see `05-before-after-performance.md`) and pass against the fix, so it will catch a regression to the old fixed-height + inset-padding-on-one-box pattern if it's ever reintroduced.

Physical-device verification (S24 Ultra-class, normal phone, tablet) is a real boundary of this session — see `06-device-test-matrix.md` for the exact reproducible procedure and `07-release-verification.md` for what remains outstanding.
