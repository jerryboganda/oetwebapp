# Release Note — R-2026-09-14-PARITY-2 — 14 Sep 2026

**Release ID:** `R-2026-09-14-PARITY-2`
**Source commit:** `bb3a02eeb` on `main` (pushed from `feat/writing-owner-addendum-two`, fast-forward)
**Source requirement:** Owner order "cut new fresh app releases" (14 Sep 2026) per `docs/app-release-playbook.md` — all three pathways: Android, iOS, Windows desktop.
**Predecessor releases:** Android `1.4.13` / versionCode 8 · Desktop `0.7.6` · iOS feed empty (no prior release)

## Versions in this release

| Surface | Version | Tag | CI workflow |
| --- | --- | --- | --- |
| Android (Play internal + alpha/Closed Testing + VPS sideload) | 1.4.14 / versionCode 9 | `v1.4.14-mobile-android` | `mobile-release.yml` (dispatch, platform=android) |
| iOS (VPS sideload feed only) | 1.4.14 / build 9 | `v1.4.14-mobile-ios` | `mobile-release.yml` (dispatch, platform=ios) |
| Windows desktop (NSIS installer + updater feed) | 0.7.7 | `v0.7.7-tauri-desktop` | `tauri-desktop-release.yml` (tag push) |
| Web app | unchanged — `31765d735` already live (Build & Deploy run 34807150495, 2026-09-14 04:44 UTC, health green) | — | — |
| Public website | unchanged | — | — |

## Why native shells are re-cut while the web app is unchanged

All three shells load the production web bundle remotely, so the 13 Sep parity
fixes (Android Practice Spelling keyboard/nav overlap, full desktop nav labels,
always-visible Recalls / Course Materials / Videos) are already active in every
installed shell. This release re-cuts the shells so that:

1. every distribution channel (Play tracks, VPS sideload feeds, desktop updater)
   points at one synchronized release identifier under the new
   `docs/releases/RELEASE-PARITY-CHECKLIST.md` gate, and
2. the Android `adjustResize` native root fix (shipped in 1.4.13) is confirmed
   as the live baseline before further changes.

## Shell deltas vs previous release

- **Android 1.4.13 → 1.4.14:** none. The manifest `windowSoftInputMode="adjustResize"`
  root fix shipped in 1.4.13; no `android/` commits since. versionCode bump only.
- **Desktop 0.7.6 → 0.7.7:** Apple support-floor enforcement (`63db5fc15`,
  `src-tauri/` only). Web-side parity fixes arrive via the remote bundle.
- **iOS:** first tagged release of the new lineage (1.4.14 / build 9); feed was empty.

## Content carried by the shared web bundle (already live, verified 2026-09-14)

- Android Recalls > Practice Spelling: bottom nav never rises over the spelling
  card/input (plugin-authoritative keyboard state + viewport baseline + DOM-focus
  fallback; `android:windowSoftInputMode="adjustResize"`).
- Desktop/laptop sidebar full labels: Listening Practice / Reading Practice /
  Writing Practice / Speaking Practice / Recalls / Course Materials / Videos.
- Recalls, Course Materials, Videos always visible in learner navigation
  (fail-open entitlements; permissions still gate content inside each area).

## Deferred / explicitly not in this release

- `d494e661b` "writing: a past-dated appointment is not a follow-up instruction"
  was unpushed at release dispatch; it rides the next web deploy.
- TestFlight / App Store Connect upload (manual owner step; no automation exists).
- macOS dmg: best-effort leg of the desktop workflow (ships unsigned; absence is
  acceptable per playbook — Windows presence is mandatory).

## Filled after the runs (14 Sep 2026)

- **Android:** run `34855473661` success — build + cert-pin + VPS feed publish green; AAB sha256 `6c5d2ffc…e15c4` published to every live Play track (`internal` + `alpha`, both completed at 1.4.14/9); VPS android feed serves 1.4.14/9, APK digest `sha256:fc37c95b…8c94`, APK URL HTTP 200.
- **Desktop:** tag run `34854005712` rerun success — conformance gate, Windows NSIS, macOS dmg, updater-feed publish all green; `latest.json` serves 0.7.7 with Win installer sha256 `fa6734ca…2385` and dmg sha256 `29379e7d…9abe`; EXE URL HTTP 200.
- **iOS:** run `34857032510` failed at input validation — repo secrets `APPLE_TEAM_ID`, `IOS_DISTRIBUTION_CERT_BASE64`, `IOS_DISTRIBUTION_CERT_PASSWORD`, `IOS_PROVISIONING_PROFILE_BASE64` are not configured and the AASA file still holds placeholders. Owner-only credentials; iOS stays on the empty VPS feed until supplied. See handover §4.
- **Live health:** `/api/health` ok at 14:49 UTC.
- **Note:** the earlier "billing" wall was the private-repo paid-minutes limit; flipping the repo public (mandatory playbook step) unblocked Actions. Repo flipped private again immediately after the runs.
