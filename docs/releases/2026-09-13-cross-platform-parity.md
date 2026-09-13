# Release Note — Cross-Platform Parity — 13 Sep 2026

**Release ID:** `R-2026-09-13-PARITY`
**Web/App commit:** `40f79b6e7` (fix(parity): Android spelling keyboard nav overlap + full desktop nav labels + always-visible core nav)
**Source requirement:** *OET with Dr Ahmed Hesham — Development Addendum, 13 Sep 2026 (Cross-Platform Parity & Remaining UI Defects)*

## Versions deployed by this release

| Surface | Version after this release | How the release reaches it |
| --- | --- | --- |
| Public website | unchanged (no public-facing wording change in scope) | n/a — verified nothing in this release alters website copy |
| Web app (desktop + mobile) | `40f79b6e7` | Build & Deploy → `https://app.oetwithdrhesham.co.uk` |
| Android APK (1.4.12 / versionCode 7) | shell unchanged — runs the web release above | Capacitor `server.url` loads the production web app; fix ships inside the app with no rebuild |
| iOS | shell unchanged — runs the web release above | Same remote-URL WebView model |
| Windows EXE (0.7.6) | shell unchanged — runs the web release above | Tauri `DEFAULT_WEB_URL` loads the production web app |

All three native shells execute the same production web bundle, so this single
deploy is the synchronized cross-platform release. No native shell rebuild was
required because every defect is fixed in the shared web bundle the shells load.
Android versionName/versionCode and the Windows FileVersion are shell packaging
identifiers and are intentionally not bumped when only the remote bundle changes.

## What this release fixes

1. **PRIORITY 1 — Android Recalls > Practice Spelling keyboard overlap.**
   The Android shell can open the IME in two modes and the old derivation read
   "no keyboard" in both. (a) Native WebView resize (`resizeOnFullScreen`):
   `innerHeight` and `visualViewport.height` shrink together, so the offset
   test read 0 — fixed by comparing the viewport against a keyboard-free
   baseline height (focus-guarded; baseline resets on rotation). (b) adjustPan
   (the default under edge-to-edge, observed live on device 13 Sep): the
   window pans and NO viewport metric changes — only the Capacitor Keyboard
   plugin sees the keyboard, and every resize-triggered metrics pass was
   clobbering the plugin's flag, resurrecting the bottom nav mid-screen over
   the spelling input while mobile web behaved fine. The plugin's keyboard
   state is now authoritative in `lib/mobile/runtime.ts`: metrics may raise
   the keyboard flag (missed plugin events, plugin-less browsers, resize
   mode) but can never clear it while the plugin says the IME is open. The
   nav hides while the keyboard is open, exactly like the mobile browser;
   the spelling card, input and actions stay visible and usable.
2. **Desktop web navigation naming.** The learner desktop rail rendered the
   short labels (Listening / Reading / Writing / Speaking / Materials). The rail
   (`NavRail`) now renders `sidebarLabel`, so desktop/laptop widths show the
   agreed full names — Listening Practice, Reading Practice, Writing Practice,
   Speaking Practice, Recalls, Course Materials, Videos — while the compact
   mobile bottom tabs keep the short labels. The /materials hero and learner
   breadcrumbs now read **Course Materials**.
3. **Windows / core navigation visibility.** Recalls, Course Materials and
   Videos no longer carry module/feature-flag gating in the learner navigation,
   so entitlement hydration failures, failed flag fetches, empty plan module
   lists, cache state or platform can never silently remove these primary
   areas from the nav (Windows EXE, desktop web, Android, iOS alike).
   `use-enabled-modules` now treats an empty module list as fail-open per its
   documented contract. Permissions still gate content inside each area.

## Parity checklist (per addendum §5 release gate)

- [x] Same product logic and labels across responsive breakpoints — full labels
      render on every desktop breakpoint (rail + expanded sidebar + mobile
      hamburger drawer); only the compact bottom tabs use short labels (allowed).
- [x] Core learner navigation (Recalls, Course Materials, Videos) cannot
      disappear for subsets of accounts — nav links render unconditionally;
      verified by tests covering empty module list, explicit omission, and
      hydration failure (`components/layout/__tests__/feature-flag-nav.test.tsx`).
- [x] Android keyboard regression coverage for Recalls > Practice Spelling —
      native-resize race, no-focus shrink false-positive, and rotation
      re-baseline all pinned in `lib/__tests__/mobile-runtime.test.ts`; the CSS
      contract remains pinned in `lib/__tests__/keyboard-nav-css.test.ts`.
- [x] Native-app relaunch behavior unchanged (Dashboard landing; not touched by
      this release).
- [x] No platform keeps old wording from this change list — the fix ships to
      every shell through the single web release above.

## Live verification (filled after deploy)

- Build & Deploy run for `40f79b6e7`: see `.github/agent-state.local.md` entry
  for run ID, active slot and health-gate results recorded the same day.
- Manual device pass still required per addendum: Android APK focus the
  Practice Spelling input (nav must never rise over the card); Windows EXE at
  laptop width with multiple learner accounts (Recalls / Course Materials /
  Videos present).
