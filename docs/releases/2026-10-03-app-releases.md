# Release Note — R-2026-10-03-APP-RELEASES — 3 Oct 2026

**Release ID:** `R-2026-10-03-APP-RELEASES`
**Source commit:** `c8d7a570e` on `main` (both runs dispatched from it)
**Source requirement:** Owner order "cut latest releases for mobile + desktop apps" (4 Oct 2026 local) per `docs/app-release-playbook.md`. iOS pathway skipped by explicit owner decision (ASC API keys still 401-blocked since 17 Sep — Team Keys need re-download).
**Predecessor releases:** Android `1.4.17` / versionCode 12 (cut 24 Sep, no ledger row) · Desktop `0.7.10` (cut after 17 Sep, no ledger row) · iOS feed still empty/404.

## Versions in this release

| Surface | Version | Tag | CI workflow |
| --- | --- | --- | --- |
| Android (Play production + alpha/Closed Testing + internal + VPS sideload) | 1.4.18 / versionCode 13 | — (workflow_dispatch) | `mobile-release.yml` run `37146943639` SUCCESS |
| Windows desktop (NSIS installer + updater feed) | 0.7.11 | — (workflow_dispatch) | `tauri-desktop-release.yml` run `37146946812` SUCCESS |
| macOS (DMG) | 0.7.11 built, download + updater entries stripped (unsigned — unchanged policy since 17 Sep) | — | same desktop run |
| iOS | **not cut** (owner decision — ASC keys 401-blocked; TestFlight/App Store remain manual owner steps) | — | — |
| Web app | unchanged — production already serving current `main` (Deploy production run `37146777264` for `c8d7a570e` was green during the cut) | — | — |
| Public website | unchanged | — | — |

## Why the shells are re-cut while the web bundle is remote

All shells load the production web bundle over HTTPS, so learner-visible web
behavior arrives with the web deploy. This release re-cuts the shells to ship
real native-shell deltas and converge every channel on one identifier:

- **Android 1.4.17 → 1.4.18:** `b9f2add39` — fix(android): Speaking microphone
  was always denied inside the app (learner-facing shell fix); `8ddbdd110`
  (PR #269 repo-wide cleanup) touches `android/`. First Android release where
  the **production** track is live (1.4.17/12 had reached production), so
  `cut-android-release` synced production + alpha + internal in one edit.
- **Desktop 0.7.10 → 0.7.11:** `8ddbdd110` (PR #269) touches `src-tauri/`; no
  other `src-tauri/` commits since 0.7.10.

## Filled after the runs (3 Oct 2026 UTC)

- **Android:** run `37146943639` success (~19:16 UTC) — input validation, version
  stamp, Next build, cap sync, signed AAB+APK, pinned-upload-cert verify, VPS
  feed publish all green. AAB sha256 `d7778c45f10c543c39fcc6fe59b36ff8aeb0d9fd53c110cdcc51e4274e7a5507`.
  Play `cut-android-release` synced `production`, `alpha`, `internal` — all three
  completed at 1.4.18/13 (`list-tracks` verified; beta untouched/empty). VPS
  android feed serves 1.4.18/13, APK digest `sha256:a7acdc80b4cf700829e225160d4b215bcc1e676bb0adc85961cd912693d4f5f7`,
  downloadUrl HTTP 206.
- **Desktop:** run `37146946812` success (~19:31 UTC) — bridge conformance gate,
  Windows NSIS, macOS Universal dmg, updater-feed publish all green.
  `latest.json` serves 0.7.11 with a minisign `signature` on `windows-x86_64`,
  installer sha256 `a85c9cfbe23df0d52f43eb4fd66981300fe9d69307fe36db74d7feccc4432bcf`,
  EXE URL HTTP 206; no `darwin` entries / `downloads.mac` (unsigned dmg — correct).
- **Live health:** `/api/health` 200 at 19:35 UTC. One transient edge 502
  (openresty) during verification self-recovered within ~40 s; workflow's own
  post-publish verify had already passed.
- **Visibility:** repo was public for both runs (verified before dispatch),
  flipped private immediately after the desktop run concluded.

## Ledger gap (backfilled 2026-10-04)

Android 1.4.15–1.4.17 and desktop 0.7.9 → 0.7.10 were cut between 14 Sep and
24 Sep 2026 without ledger rows. Backfilled on 2026-10-04 as
`R-2026-09-16-SHELL-ROUND` and `R-2026-09-24-MOBILE-1417` in
`docs/releases/RELEASE-LEDGER.md`: run IDs, source commits and publish-job
outcomes reconstructed from the Actions history, and artifact SHA-256 values
recomputed from the retained artifacts (the 1.4.17 APK hash matches the live
feed digest captured before that feed was overwritten). The cutting sessions'
operator identities remain unknown.

## Parity checklist status

No learner-visible functional change rides in the shells themselves beyond the
Android mic fix above (web behavior arrives via the remote bundle), so the
device-parity battery in `docs/releases/RELEASE-PARITY-CHECKLIST.md` is N/A for
this cut; the live-feed verification above is the done-definition evidence.
The Android mic fix (`b9f2add39`) should get one on-device speaking check at the
next parity pass.
