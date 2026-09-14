# Release Ledger

One row per cut release. This ledger is the traceability record required by
`docs/releases/RELEASE-PARITY-CHECKLIST.md`: from any release a reader must be
able to reach its exact source commit, artifacts and live-feed state.

| Release ID | Date (UTC) | Surface(s) | Version(s) | Source commit | Tag(s) | CI run(s) | Artifact SHA-256 | Live-feed evidence | Operator |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| R-2026-09-13-PARITY | 2026-09-13 | Web app (single web deploy carrying all parity fixes to all remote-URL shells) | web `40f79b6e7` | `40f79b6e7` | — | Build & Deploy (see `.github/agent-state.local.md`) | — | `app.oetwithdrhesham.co.uk` health green 2026-09-13 | Codex session |
| R-2026-09-13-PARITY (Android follow-up) | 2026-09-13 | Android | 1.4.13 / versionCode 8 | `6432f713e` (defaults; build from `main` `31765d735`) | `v1.4.13-mobile-android` (defaults only; release dispatched) | mobile-release run 34780865897 (success, 2026-09-13 20:28 UTC) | APK feed digest `sha256:ab9f8803dfcd892418a703d0515025884cf35805e1d0a8c2abf29a93de4daf35` | Play internal+alpha 1.4.13/8; VPS android feed 1.4.13/8 | Codex session |
| R-2026-09-14-PARITY-2 | 2026-09-14 | Android + iOS + Windows desktop | Android 1.4.14/9 · iOS 1.4.14/9 · Desktop 0.7.7 | `bb3a02eeb` | `v1.4.14-mobile-android`, `v1.4.14-mobile-ios`, `v0.7.7-tauri-desktop` | **none — CI BLOCKED**: GitHub Actions billing failure blocks every run since 2026-09-14 08:42 UTC (annotation: "recent account payments have failed or your spending limit needs to be increased"). Runs to be dispatched after billing is restored — see `docs/releases/2026-09-14-parity-2-handover.md` §4. | not built (blocked) | not published (blocked); pre-cut live state: Play internal+alpha 1.4.13/8, VPS android 1.4.13/8, iOS feed empty, desktop 0.7.6 | AutoCoder (OpenClaw) session |
