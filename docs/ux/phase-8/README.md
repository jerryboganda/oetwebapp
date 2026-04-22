# Phase 8 — Remediation Backlog, Rollout, Validation, Governance

## Artifacts
- `backlog.md` — every open gap as a ticket: JTBD link, acceptance, severity, estimate, owner.
- `rollout-plan.md` — batched releases grouped by risk (auth/billing isolated), with dependencies.
- `validation-plan.md` — moderated usability tests (n ≥ 5 per persona on top 8 JTBDs) + pre/post metrics.
- `governance-playbook.md` — ongoing cadence:
  - Weekly UX triage (new gaps, re-score regressed routes).
  - Monthly design-review of any new route before it ships.
  - **PR template checklist**: H7 Consistency + H8 A11y + dev-copy scan pass.
  - Axe CI gate on smoke routes.
  - Quarterly persona validation interviews.

## Success metrics (vs. Phase 1 baseline)
| Metric | Target |
|--------|--------|
| 🔴 Critical gaps open | 0 |
| Mean heuristic score across T0/T1 | ≥ 24/30 |
| axe serious/critical violations (T0) | 0 |
| Task success rate (top 8 JTBDs, n≥5) | ≥ 90% |
| Horizontal-scroll issues at 360 px (top 50 routes) | 0 |
| Top 5 support-ticket themes | ↓ ≥ 40% in 90 days |
| Funnel: diagnostic → upgrade | report delta, target ↑ |

## Exit criteria
- Zero open 🔴 gaps.
- Governance playbook merged into repo + PR template updated.
- Validation study run and summary published.
