# Changelog

All notable changes to this repo are documented here. Format inspired by
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Module-scoped
changelogs live alongside their modules (e.g. `docs/speaking/changelog.md`).



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

## [Unreleased]

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
