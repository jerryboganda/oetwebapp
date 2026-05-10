# T0 Learner Dashboard Scorecard

## Metadata

- Route ID: T0-LRN-001
- Route name: Learner dashboard
- Paths: `/dashboard`, `/`
- Portal: learner
- Stage: T0
- Primary persona: learner
- Owner: TBD
- Reviewer: TBD
- Related RW IDs: RW-002, RW-009, RW-017, RW-022
- Last validation date: 2026-05-09 local evidence planning

## Recommendation

Decision: Monitor

Top risks:

1. Current Playwright dashboard evidence must be rerun and attached.
2. Manual screen-reader and keyboard traversal beyond top-level cards is pending.
3. Route-level latency/error dashboard link is pending.

## Scorecard

For each dimension, record score 0-5, evidence link, and notes.

- Functional correctness: 4; learner smoke tests cover load and reload survival.
- API contract coverage: 3; dashboard data contract evidence pending.
- Role/authz correctness: 4; role guard tests cover learner access and privileged redirects.
- Commercial readiness: 4; dashboard is the primary learner landing route and has route coverage.
- Accessibility: 4; axe dashboard smoke exists; manual keyboard/screen-reader evidence pending.
- Responsive/visual quality: 4; mobile smoke test covers 360 px layout; current screenshots pending.
- Performance/reliability: 3; production perf smoke includes dashboard; current artifact pending.
- Observability/alerts: 2; route SLO row pending dashboard link.
- Security/privacy: 3; route is auth-gated; data minimization review pending.

## Acceptance Checks

- [x] Happy path test exists.
- [x] Failure/empty/loading states have partial coverage.
- [ ] API shape or contract tested with explicit dashboard contract.
- [x] Role guard tested.
- [x] Automated critical/serious accessibility scan exists.
- [ ] Keyboard-only flow works with attached evidence.
- [ ] 360 px mobile and desktop screenshots attached.
- [ ] Error/latency dashboard or smoke artifact attached.
- [ ] Security/privacy review notes attached.

## Evidence Attachments

- Playwright report: pending current run for `tests/e2e/learner/learner-smoke.spec.ts`, `tests/e2e/shared/accessibility.spec.ts`, and `tests/e2e/shared/mobile-smoke.spec.ts`.
- Unit/integration tests: dashboard-related route tests pending inventory.
- Screenshots: pending.
- Accessibility report: `tests/e2e/shared/accessibility.spec.ts`.
- API contract evidence: pending.
- Observability dashboard: pending.

## Follow-up Actions

- Action: Run learner dashboard smoke, accessibility, and mobile smoke; attach report links.
- Owner: TBD
- Priority: P0
- Target: before launch signoff
- Tracker: RW-009, RW-017, RW-022

## Signoff

- Owner: TBD
- QA: TBD
- Product: TBD
- Security/privacy if required: TBD
