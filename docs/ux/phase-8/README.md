# Phase 8 - Remediation Backlog, Rollout, Validation, Governance

## Artifacts

- `templates/T0-T1-route-scorecard-template.md` - route scorecard template.
- `route-inventory-starter.csv` - current starter inventory for T0/T1 route readiness tracking.
- `accessibility-evidence-checklist.md` - manual and automated accessibility evidence checklist.
- `scorecards/T0-auth-signin-register.md` - starter scorecard for public auth entry.
- `scorecards/T0-learner-dashboard.md` - starter scorecard for learner dashboard.
- `scorecards/T0-billing-upgrade.md` - starter scorecard for learner billing and upgrade.
- Planned next files: `backlog.md`, `rollout-plan.md`, `validation-plan.md`, and `governance-playbook.md`.
- Governance cadence to formalize:
  - Weekly UX triage (new gaps, re-score regressed routes).
  - Monthly design-review of any new route before it ships.
  - PR template checklist: H7 Consistency + H8 A11y + dev-copy scan pass.
  - Axe CI gate on smoke routes.
  - Quarterly persona validation interviews.

## Success metrics (vs. Phase 1 baseline)

| Metric | Target |
| ------ | ------ |
| Critical gaps open | 0 |
| Mean heuristic score across T0/T1 | >= 24/30 |
| axe serious/critical violations (T0) | 0 |
| Horizontal-scroll issues at 360 px (top 50 routes) | 0 |
| Task success rate (top 8 JTBDs, n>=5) | >= 90% |
| Top 5 support-ticket themes | decrease >= 40% in 90 days |
| Funnel: diagnostic to upgrade | report delta, target increase |

## Exit criteria

- Zero open critical gaps.
- Governance playbook merged into repo + PR template updated.
- Validation study run and summary published.
