# Changelog

All notable changes to this repo are documented here. Format inspired by
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Module-scoped
changelogs live alongside their modules (e.g. `docs/speaking/changelog.md`).


## [R-2026-10-03-APP-RELEASES] - 2026-10-03

Native shell release cut from `main` `c8d7a570e` (workflow_dispatch, no tags) per `docs/app-release-playbook.md`. The shells load the production web bundle remotely, so learner-visible web behavior arrives with the web deploy; this cut ships the native deltas and converges every channel on one identifier. Full evidence: `docs/releases/2026-10-03-app-releases.md`.

### Android — 1.4.18 (versionCode 13)

- `mobile-release.yml` run `37146943639` success: signed AAB + APK, pinned upload cert verified, VPS sideload feed published (APK `sha256:a7acdc80…`).
- Play: `cut-android-release` landed 13/1.4.18 on **production, alpha (Closed Testing) and internal** in one atomic edit — all three were live at 1.4.17/12. Beta stays empty/untouched.
- Shell delta: Speaking microphone was always denied inside the app (`b9f2add39`) + PR #269 cleanup.
- Committed `android/app/build.gradle` defaults synced to 13/1.4.18 (were stale at 10/1.4.15).

### Windows desktop — 0.7.11

- `tauri-desktop-release.yml` run `37146946812` success: bridge conformance, Windows NSIS, macOS Universal dmg, updater-feed publish all green.
- Updater feed serves 0.7.11 with a minisign-signed `windows-x86_64` entry (installer `sha256:a85c9cfb…`). macOS downloads stay stripped (unsigned dmg — policy unchanged since 17 Sep).
- Shell delta: PR #269 repo-wide cleanup touches `src-tauri/`.

### iOS — not cut (owner decision)

- Skipped this round: ASC API keys still 401-blocked (Apple Team Keys need re-download); iOS VPS feed never published. TestFlight/App Store remain manual owner steps.

## [Unreleased] - Free Mocks (2026-09-22)

- **Free Listening / Reading sample:** a paper tagged `free-sample` opens the content gate and skips the per-paper credit debit; a FREE SAMPLE card sits above the practice cards on both hubs (outside the four-card grid). Reading uses Atlas 02, Listening Atlas ST3; both stay in their libraries.
- **Free Writing / Speaking sample:** one free AI-graded attempt per learner per subtest, on the designated (or auto-picked lowest-order live) item of a profession the learner chooses. New `FreeSampleDesignations` / `FreeSampleClaims` tables, `GET /v1/free-samples/{subtest}`, admin `PUT/DELETE /v1/admin/free-samples/{subtest}/{profession}`. Dark-launched behind the `free_samples_enabled` feature flag (absent = OFF). See Master Catalogue Rule F.
- **Speaking hub:** criteria → intro questions → "Open Practice Library" text link → Free Speaking Mock → Full AI Speaking Mock → Book a Tutor (visible, gated by the entitlement snapshot).

## [R-2026-09-14-PARITY-2] - 2026-09-14

Cross-platform app release cut from `main` `bb3a02eeb` (branch `feat/writing-owner-addendum-two`, fast-forward merged state).
Web app itself is unchanged on production (`Build & Deploy` run 34807150495, SHA `31765d735`, already live and healthy) — this release re-cuts the **native shells** so their pinned/installed copies converge on the same parity state:

### Android — 1.4.14 (versionCode 9)

- Tag `v1.4.14-mobile-android`; carries the same web bundle as 1.4.13 (the Recalls > Practice Spelling keyboard/nav fix, full desktop nav labels, always-visible core nav).
- Shell delta vs 1.4.13: none — the manifest `adjustResize` root fix already shipped in 1.4.13. This release exists so every channel converges on one synchronized release under the new parity checklist.

### iOS — 1.4.14 (build 9)

- Tag `v1.4.14-mobile-ios`; publishes to the VPS sideload feed only. TestFlight/App Store Connect upload remains a manual owner step (no automation exists).

### Desktop (Windows) — 0.7.7

- Tag `v0.7.7-tauri-desktop`; updater feed + installer on the VPS.
- Shell delta vs 0.7.6: Apple support-floor enforcement (`63db5fc15`, `src-tauri` only); all parity fixes reach the shell via the shared remote web bundle.

### Web app

- No production web change in this release (last deploy `31765d735` remains live).

### Unreleased work (NOT in this release)

- `d494e661b` writing case-note marker fix (unpushed at release time; ships in the next web deploy).

## [History before R-2026-09-13]

### Desktop (Tauri 2) production-readiness

Hardening pass on the `src-tauri/` desktop shell ahead of Windows internal testing.

#### Added
- Rust unit tests (17) for IPC + sidecar logic; `rustfmt.toml` / `clippy.toml`.
- `tauri-ci.yml` PR gate (fmt + clippy `-D warnings` + cargo test + build + bridge conformance).
- Per-session sidecar log capture (`<app_data>/logs/`) + Rust panic hook.
- Self-signed Authenticode signing wiring + production minisign updater key; GitHub Release +
  `latest.json` publishing in the release workflow.
- Living QA docs under `docs/qa/` (TEST_PLAN, QA_REPORT, BUGLOG, TESTER_SETUP).

#### Changed
- Hardened CSP for the bundled splash; updater endpoint moved to the prod HTTPS feed.
- Resolved all `cargo clippy -D warnings` findings; canonical `cargo fmt`.

#### Security
- `.gitignore` now excludes code-signing / updater key material; verified no secrets committed.

### Speaking module v2

Implementation of `~/.claude/plans/1-oet-speaking-module-sequential-candy.md`.

#### Added
- Profession-aware learner gate (`P1.2`) — `/speaking/select-profession`.
- `activeProfessionId` + `activeProfessionLabel` on `CurrentUser` (`P1.1`).
- AI feature routes registered for `speaking.score.v2`, `speaking.patient.turn.v1`, `card.draft.v1` (`P1.3`).
- Warm-up conversation flow (`P3`) with profession-specific seeded questions.
- AI patient turn loop with prompt caching + time-up cues + avatar component (`P4`).
- Mock orchestrator with Bridge state + aggregated readiness band (`P5`).
- LiveKit Cloud gateway, webhook HMAC verification, S3 egress (`P6`).
- Tutor assessment validation + calibration drift report (`P7`).
- Drill bank + course pathway page (`P8`).
- Admin Speaking + Mocks analytics dashboards (`P9`).
- Learner recording self-management + admin audit viewer (`P10`).
- Full content library: hand seeds + AI draft + batch authoring + originality guard (`P11`).
- Playwright E2E suite, xUnit integration suite, runbook, feature flag (`P12`).

#### Tooling
- Storybook stories for Speaking components.
- k6 load test scripts with documented SLOs.
- A11y axe-core Playwright specs covering every Speaking surface.
- Mobile (Capacitor) + desktop (Electron) audio bridges.
- Architecture docs + Mermaid diagrams.
- Analytics events catalog + Grafana dashboard JSON.
- GitHub Actions CI/CD pipeline (PR, nightly E2E, a11y, weekly load, content batch).
- Threat model + security checklist + key rotation runbook.

#### Governance
- Speaking-specific PR template, CODEOWNERS, governance docs (changelog, contributing, release checklist, incident runbook, SLA).

[Unreleased]: https://github.com/<org>/oet-web-app/compare/main...HEAD
