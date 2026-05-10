# T0 Auth Sign In And Register Scorecard

## Metadata

- Route ID: T0-AUTH-001
- Route name: Sign in and registration entry
- Paths: `/sign-in`, `/register`, `/mfa/*`, `/forgot-password`
- Portal: public
- Stage: T0
- Primary persona: learner
- Owner: TBD
- Reviewer: TBD
- Related RW IDs: RW-002, RW-003, RW-009, RW-017
- Last validation date: 2026-05-09 local evidence planning

## Recommendation

Decision: Monitor

Top risks:

1. Production smoke and browser matrix evidence still need current run links.
2. MFA/recovery and middleware cookie set-clear flows need explicit route-level evidence.
3. Manual screen-reader validation is not complete.

## Scorecard

For each dimension, record score 0-5, evidence link, and notes.

- Functional correctness: 4; `tests/e2e/auth/auth.spec.ts`, auth unit tests; needs latest CI link.
- API contract coverage: 4; auth backend tests cover sign-in/session; contract report pending.
- Role/authz correctness: 4; protected route redirects covered by `tests/e2e/shared/role-guards.spec.ts`; production debug-header guard added.
- Commercial readiness: 3; signup flow exists; final prod smoke evidence pending.
- Accessibility: 4; `tests/e2e/shared/accessibility.spec.ts` covers sign-in keyboard and axe smoke; manual assistive-tech evidence pending.
- Responsive/visual quality: 3; mobile motion/smoke tests exist; screenshot evidence pending.
- Performance/reliability: 3; production smoke/perf surfaces exist; current run artifact pending.
- Observability/alerts: 2; auth SLO row defined, dashboard link pending.
- Security/privacy: 4; CSRF proxy and production dev-auth protections are tracked; final RBAC/auth lifecycle evidence pending.

## Acceptance Checks

- [x] Happy path test exists.
- [x] Failure/redirect state test exists.
- [x] API shape or contract has backend coverage.
- [x] Role guard test exists.
- [x] Automated critical/serious accessibility scan exists.
- [ ] Keyboard-only flow has current artifact.
- [ ] 360 px mobile and desktop screenshots attached.
- [ ] Error/latency dashboard or smoke artifact attached.
- [ ] Security/privacy review notes attached for MFA/recovery/cookie lifecycle.

## Evidence Attachments

- Playwright report: pending current run.
- Unit/integration tests: `AuthFlowsTests`, `DevelopmentAuthHandlerTests`, `ProductionReadinessTests`.
- Screenshots: pending.
- Accessibility report: `tests/e2e/shared/accessibility.spec.ts`.
- API contract evidence: pending.
- Observability dashboard: pending.

## Follow-up Actions

- Action: Run auth E2E and attach report.
- Owner: TBD
- Priority: P0
- Target: before launch signoff
- Tracker: RW-002, RW-003, RW-017

## Signoff

- Owner: TBD
- QA: TBD
- Product: TBD
- Security/privacy if required: required
