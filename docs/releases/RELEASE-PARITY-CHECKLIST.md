# Release Parity Checklist — Template

Copy this file to `docs/releases/<YYYY-MM-DD>-<slug>.md` for each release, fill
**every** field, and attach it to the release. A release note without a
completed copy of this checklist is not a completed release.

**Release ID:** `R-<YYYY-MM-DD>-<SLUG>`  *(e.g. `R-2026-09-13-PARITY`)*
**Requested change(s) covered:** *(list the owner-requested items, one line each)*
**Web/app commit:** `<sha>` — *(the commit that Build & Deploy shipped; not `main` HEAD)*
**Author / date completed:** *(name, date)*

## 1. Surface version / deploy identifiers

Fill in the exact identifier per surface for *this* release. Native shells are
remote-URL WebView wrappers (`capacitor.config.ts` `server.url`, Tauri
`DEFAULT_WEB_URL`), so a web-only change normally needs **no** shell rebuild —
record that explicitly rather than leaving the row blank.

| Surface | Version / deploy identifier | Where the identifier comes from | Rebuilt for this release? | Verified live? |
| --- | --- | --- | --- | --- |
| Public website (marketing) | *(e.g. commit sha / FTP deploy stamp)* | `OET Project Website` repo deploy | Yes / No / N/A | ☐ |
| Web app — desktop + mobile browser | *(Build & Deploy run id + commit sha)* | `.github/workflows/deploy.yml` run for `<sha>`; live at `https://app.oetwithdrhesham.co.uk` | n/a (this is the web deploy) | ☐ |
| Android | *(versionName / versionCode, e.g. `1.4.13` / `8`)* — Play tracks: internal ☐ alpha ☐ VPS feed ☐ | `android/app/build.gradle`; Play tracks via `playstore.cli list-tracks`; feed `https://app.oetwithdrhesham.co.uk/api/releases/native?platform=android` | Yes / No | ☐ |
| iOS | *(MARKETING_VERSION / CURRENT_PROJECT_VERSION)* | `ios/App/App.xcodeproj/project.pbxproj`; VPS feed `/api/releases/native?platform=ios`; TestFlight is a manual step | Yes / No | ☐ |
| Windows (EXE) | *(Tauri version, e.g. `0.7.6`)* | `src-tauri/tauri.conf.json`; updater `https://app.oetwithdrhesham.co.uk/desktop/updates/latest.json` | Yes / No | ☐ |
| macOS (DMG) | *(Tauri version)* + notarization status | same Tauri build as Windows; see §4 | Yes / No | ☐ |

> **All-shell rule of thumb:** confirm whether the change lives in the shared web
> bundle (ships to every shell with the web deploy) or in native shell code
> (needs its own build). State it per row — never infer it silently.

## 2. Parity checklist

Tick only what was actually executed and observed. Record the account(s),
device/browser and width used.

- [ ] **Same requested change verified on every applicable surface.** Each item
      in the header list was checked on the public website (if it touches
      website copy), the web app at desktop + mobile widths, Android, iOS,
      Windows (and macOS when distributed). Findings: *(per surface, one line)*
- [ ] **Desktop web and Windows tested at normal laptop widths** — at minimum
      **1280 px** and **1440 px** viewport widths (the expanded desktop sidebar
      must render full labels: *Listening Practice, Reading Practice, Writing
      Practice, Speaking Practice, Recalls, Course Materials, Videos*). Widths
      tested: *(record exact px)*
- [ ] **Windows nav visibility tested with multiple learner accounts.** At least
      three: (a) a plan with an explicit module list, (b) a plan with an empty /
      no module list, (c) an account whose entitlement fetch fails or is slow
      (throttled network). *Recalls*, *Course Materials* and *Videos* must be
      present in the desktop navigation on **every** account — entitlements may
      gate what opens inside an area, never the nav item itself. Accounts used:
      *(record)*
- [ ] **Android keyboard regression test for Recalls > Practice Spelling.** On a
      physical Android device (not emulator-only): open Recalls > Practice
      Spelling, focus the spelling input, and confirm the bottom navigation
      never rises into or over the spelling card / input / actions — the nav
      must hide or stay docked at the true bottom while the keyboard is open,
      matching the mobile browser. Also confirm the focused input stays visible.
      Device + Android/WebView version: *(record)*
- [ ] **Native relaunch lands on Dashboard.** On each native shell released:
      fully close the app (swipe away / quit, not just background), reopen, sign
      in, and confirm the landing screen is **Dashboard** — not a stale route or
      a blank WebView. Shells tested: *(record)*
- [ ] **No stale cached bundle.** Native shells load the remote bundle; confirm
      the relaunch served the new bundle (deploy SHA stamp / changed UI visible),
      not the previous session's cache.
- [ ] **Automated regression coverage present** for each fixed defect
      (`components/layout/__tests__/feature-flag-nav.test.tsx`,
      `lib/__tests__/mobile-runtime.test.ts`,
      `lib/__tests__/keyboard-nav-css.test.ts`, or the equivalent suite for the
      area changed) and green in CI for this commit.

## 3. Release gate rule

> **No release is marked complete while any platform still shows old wording,
> missing navigation, stale UI, or a known unresolved regression.**

Concretely, a release may not be signed off if any of the following is true on
any applicable surface:

- old navigation wording (e.g. bare `Listening` / `Materials` where the agreed
  full label is required at a desktop breakpoint);
- a primary navigation area missing (Recalls / Course Materials / Videos);
- a stale UI or stale cached bundle from a previous release;
- a known regression that is unresolved, even if it is "only" on one platform.

If a platform cannot be verified (no device, no build environment), the release
stays **incomplete** and the gap is recorded in the note below — it is not
silently waived.

## 4. macOS distribution note

macOS notarization (Apple Developer ID signing, notarization and stapling;
secrets `APPLE_CERTIFICATE`, `APPLE_ID`, …) is the **final distribution step**
and waits on the Apple Developer account. Notarization must **not** block
implementation or the parity QA in §2: build, test and parity-verify the shared
web stack and the other surfaces now; notarize and distribute the DMG once the
account is available. Record the current status here:

- macOS notarization status this release: *(pending account / signed / notarized / stapled)*

## 5. Unverified / outstanding items

Anything not verifiable locally or in CI (e.g. physical-device passes, Windows
EXE rebuild, App Store review) must be listed here with an owner and a
follow-up. Do not leave this section empty for a release that has such gaps.

| Item | Surface | Owner | Follow-up |
| --- | --- | --- | --- |
| *(e.g. Android on-device keyboard pass)* | *(Android)* | *(name)* | *(link / next step)* |
