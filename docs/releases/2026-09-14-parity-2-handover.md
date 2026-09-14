# Release Handover — R-2026-09-14-PARITY-2

**Audience:** whoever finishes or verifies this release (expects no prior context).
**Prepared:** 2026-09-14 by the AutoCoder (OpenClaw) session.
**Status at handover: RELEASED on Android + Windows desktop; iOS prepared but blocked on owner-only Apple signing credentials.**

---

## 1. What this release is

A fresh cross-platform app release cut under the owner's standing order
("cut new fresh app releases" / "cut app releases") and the procedure in
`docs/app-release-playbook.md` — all three pathways:

| Surface | Version | Tag | Distribution |
| --- | --- | --- | --- |
| Android | 1.4.14 / versionCode 9 | `v1.4.14-mobile-android` | Play internal + alpha/Closed Testing + VPS sideload feed |
| iOS | 1.4.14 / build 9 | `v1.4.14-mobile-ios` | VPS sideload feed only (TestFlight = manual owner step) |
| Windows desktop | 0.7.7 | `v0.7.7-tauri-desktop` | VPS updater feed + NSIS installer |
| Web app | unchanged (`31765d735`) | — | already live and healthy |
| Public website | unchanged | — | — |

All three shells load the production web bundle over the network, so the 13 Sep
parity fixes were already active; this release re-cuts the shells so every
channel converges on one synchronized release identifier under
`docs/releases/RELEASE-PARITY-CHECKLIST.md`.

## 2. What is DONE and verified

| Item | Evidence |
| --- | --- |
| Version bump committed | Commit `bb3a02eeb`, 1 file changed: `android/app/build.gradle` → `versionCode 9` / `versionName "1.4.14"`. Desktop follows repo convention: version files stay at baseline `0.5.1`; `tauri-desktop-release.yml` stamps the tag version at build time. |
| Changelog | `CHANGELOG.md` → new `## [R-2026-09-14-PARITY-2] - 2026-09-14` section (previous release referenced; all changes since accounted for or explicitly deferred). |
| Release note | `docs/releases/2026-09-14-parity-2-release.md` |
| Release ledger | `docs/releases/RELEASE-LEDGER.md` (row added; run IDs/hashes to be filled post-run) |
| Parity checklist | `docs/releases/RELEASE-PARITY-CHECKLIST.md` (pre-existing, created 14 Sep) |
| Local type gate | `node node_modules/typescript/bin/tsc --noEmit` → **exit 0**, 0 diagnostics |
| Release-relevant tests | `node node_modules/vitest/vitest.mjs run` on `feature-flag-nav`, `top-nav`, `mobile-runtime`, `keyboard-nav-css` → **4 files / 30 tests passed** |

Local gate commands used (pnpm `.cmd` shims hang in this shell — call the tools directly):

```powershell
cd "D:\Projects\OET with Dr Hesham\OET Project Web App"
node node_modules\typescript\bin\tsc --noEmit
node node_modules\vitest\vitest.mjs run components/layout/__tests__/feature-flag-nav.test.tsx components/layout/__tests__/top-nav.test.tsx lib/__tests__/mobile-runtime.test.ts lib/__tests__/keyboard-nav-css.test.ts
```

## 3. RESOLVED — the "GitHub Actions billing" wall was the private-repo paid-minutes limit

Every Actions job dispatched while the repo was **private** failed within ~2
seconds with zero steps executed and no runner assigned:

> The job was not started because recent account payments have failed or your
> spending limit needs to be increased. Please check the 'Billing & plans'
> section in your settings

The last green run (`Build & Deploy`, `34807150495`) had run while the repo was
public. Flipping the repo public — a mandatory step of every release in this
playbook anyway — immediately unblocked Actions; the same desktop run then
started and succeeded without any account changes. Conclusion: the private
repo's billable Actions minutes were exhausted; public-repo Actions are free.
The repo must be flipped **private** again immediately after the release runs.

## 4. How to finish the release (after billing is restored)

Follow `docs/app-release-playbook.md` exactly. Short form:

1. **Merge/push the release commit to `main`** so CI builds it (CI builds
   `origin/main`, never the working tree). ✅ DONE — `main` is at `2ec46cd64`
   (pushed 2026-09-14 ~14:10 UTC) and the three tags `v1.4.14-mobile-android`,
   `v1.4.14-mobile-ios`, `v0.7.7-tauri-desktop` are on the remote.
2. Repo must be **public** for the whole duration of every run, then **private**
   immediately after: `gh repo edit jerryboganda/oetwebapp --visibility public --accept-visibility-change-consequences`.
3. **Desktop — DONE.** Tag run `34854005712` rerun → all jobs green; live feed
   `desktop/updates/latest.json` serves 0.7.7 (Win NSIS sha256 `fa6734ca…2385`,
   macOS dmg sha256 `29379e7d…9abe`). ✅
4. **Android — DONE.** Dispatch `34855473661` (version=1.4.14, version_code=9) →
   build + VPS feed publish green; AAB published to **every live track** via
   `cut-android-release` (Play internal + alpha both completed at 1.4.14/9;
   production/beta untouched — they were already empty). VPS android feed
   serves 1.4.14/9 (APK digest `sha256:fc37c95b…8c94`). ✅
5. **iOS — BLOCKED on owner-only credentials.** Run `34857032510` failed at
   "Validate iOS release inputs": `APPLE_TEAM_ID`,
   `IOS_DISTRIBUTION_CERT_BASE64`, `IOS_DISTRIBUTION_CERT_PASSWORD`,
   `IOS_PROVISIONING_PROFILE_BASE64` are not set in repo secrets, and
   `public/.well-known/apple-app-site-association` still contains
   launch-blocking placeholders. No automation can proceed without the owner
   supplying these; TestFlight upload remains a manual owner step regardless.
6. Record run IDs, artifact SHA-256 values and live-feed JSON in
   `docs/releases/RELEASE-LEDGER.md` — done for Android + desktop; iOS row
   records the exact missing secrets. Parity checklist: Android + desktop
   verified to serve the release; device-pass items remain for the owner
   (see §6).

Billing-wall root cause (for the record): the "recent account payments have
failed or your spending limit needs to be increased" annotation appeared on
every run while the repo was **private** (private-repo Actions minutes are
billable). The last green run had been public; flipping the repo public —
already a mandatory step of every release in the playbook — immediately
unblocked Actions. The repo must still be flipped **private** again the
moment the release runs finish.

Failed-on-billing run IDs for this SHA (2026-09-14 14:11 UTC): Build & Deploy
`34853974318`, Mobile CI `34853974303`, QA Smoke `34853974432`, SBOM/SCA
`34853974359`, Speaking CI `34853974358`, Tauri Desktop Release (tag)
`34854005712`, UBAG e2e `34854005795` / `34854006021` / `34854006613`.
None executed a single step. After billing is restored, re-run what matters:
the desktop tag run (rerun), the two mobile dispatches, and Build & Deploy if
the docs commits should be reflected on the site (they are docs-only, so
optional for the release itself).

## 5. Rollback

Nothing in this release deletes or overwrites a previous release; the previous
state is intact and is the rollback target.

| Surface | Rollback action | Previous good state |
| --- | --- | --- |
| Android (VPS feed) | `gh workflow run publish-existing-mobile-to-vps.yml -f platform=android -f version=1.4.13 -f tag=v1.4.13-mobile-android -f version_code=8` | 1.4.13 / code 8 (APK sha256 `ab9f8803…daf35`) |
| Android (Play tracks) | `.venv\Scripts\python.exe -m playstore.cli assign-track 8 --track internal` (and `--track alpha`) — repoints the track at the existing code 8 with no upload | 1.4.13 / code 8 on internal + alpha |
| iOS | `publish-existing-mobile-to-vps.yml` with `platform=ios`; the feed was **empty** before this release, so rollback = remove the published feed entry | empty feed `{}` |
| Windows desktop | Re-publish the previous installer: `gh workflow run publish-existing-desktop-to-vps.yml` | 0.7.6 (installer sha256 `3d8fe23e…5ccfc`) |
| Web app | Not changed by this release; if a web rollback is ever needed, redeploy the previous image tag (`ghcr.io/jerryboganda/oetwebapp-web:31765d735`) | `31765d735` |

Rollback notes:

- **Never** lower a `versionCode` — Play and the VPS feed both reject
  non-increasing codes. Roll the feed/track pointer, not the artifact number.
- Uninstalling is only needed if a user somehow installed a differently signed
  build; Play-signed and sideloaded copies upgrade on their own channels only.

## 6. Verification still owed (cannot be done in this session)

- Android device pass: Recalls > Practice Spelling with the keyboard open — bottom
  nav must never cover the spelling card.
- Windows EXE pass at normal laptop width with **≥3 learner accounts**: Recalls,
  Course Materials and Videos present.
- Desktop web pass in Chrome/Edge at laptop widths: full sidebar labels.
- Native relaunch: full close/reopen/sign-in lands on Dashboard.
- Post-run: artifact hashes and feed JSON recorded in the ledger.
