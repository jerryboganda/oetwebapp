# Changelog

All notable changes to this repo are documented here. Format inspired by
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Module-scoped
changelogs live alongside their modules (e.g. `docs/speaking/changelog.md`).

## [Unreleased]

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
