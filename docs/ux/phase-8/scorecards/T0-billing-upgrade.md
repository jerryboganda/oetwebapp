# T0 Billing Upgrade Scorecard

## Metadata

- Route ID: T0-BILL-001
- Route name: Billing center and upgrade
- Paths: `/billing`, `/billing/upgrade`, `/billing/checkout`
- Portal: learner
- Stage: T0
- Primary persona: learner
- Owner: TBD
- Reviewer: TBD
- Related RW IDs: RW-002, RW-009, RW-013, RW-017, RW-022
- Last validation date: 2026-05-09 local evidence planning

## Recommendation

Decision: Monitor

Top risks:

1. Current Stripe/checkout provider evidence and webhook health evidence are pending.
2. Sponsor/institutional billing placeholders remain tracked under RW-013.
3. Payment failure and cancellation states need latest browser artifact links.

## Scorecard

For each dimension, record score 0-5, evidence link, and notes.

- Functional correctness: 4; billing smoke and unit tests cover center, upgrade, payment banners, freezes, and top-ups.
- API contract coverage: 3; billing backend tests exist; frontend/backend contract evidence pending.
- Role/authz correctness: 4; learner route and expired-user redirect coverage exists in production smoke specs.
- Commercial readiness: 3; learner billing is strong, sponsor billing remains open.
- Accessibility: 3; billing route is covered in mobile/prod accessibility suites, but current local a11y artifact pending.
- Responsive/visual quality: 4; billing mobile motion smoke exists.
- Performance/reliability: 3; production perf smoke includes billing; current artifact pending.
- Observability/alerts: 2; billing webhook/checkout alert ownership pending.
- Security/privacy: 3; payment secrets stay server-side; release evidence and webhook proof pending.

## Acceptance Checks

- [x] Happy path test exists.
- [x] Failure/empty/loading states have unit coverage.
- [ ] API shape or contract tested with explicit billing contract.
- [x] Role guard/expired-account route behavior has smoke coverage.
- [ ] No critical/serious accessibility violations in current run.
- [ ] Keyboard-only billing flow works with attached evidence.
- [ ] 360 px mobile and desktop screenshots attached.
- [ ] Billing webhook/error dashboard or smoke artifact attached.
- [ ] Security/privacy review notes attached for checkout/webhook flow.

## Evidence Attachments

- Playwright report: pending current run for `tests/e2e/billing.smoke.spec.ts` and `tests/e2e/billing.spec.ts`.
- Unit/integration tests: `app/billing/__tests__/*` and backend billing tests.
- Screenshots: pending.
- Accessibility report: pending current billing a11y run.
- API contract evidence: pending.
- Observability dashboard: pending.

## Follow-up Actions

- Action: Run billing smoke, billing unit tests, and attach checkout/webhook health evidence.
- Owner: TBD
- Priority: P0
- Target: before launch signoff
- Tracker: RW-009, RW-013, RW-017, RW-022

## Signoff

- Owner: TBD
- QA: TBD
- Product: TBD
- Security/privacy if required: required
