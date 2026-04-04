# 08 - Implementation Roadmap

## Document intent

This roadmap removes stale foundation work that is already present in the codebase and replaces it with the real next steps.

## Immediate critical

These items directly protect revenue, trust, and the integrity of the current OET product.

| Workstream | Why now | Primary surfaces |
| --- | --- | --- |
| Documentation truth reset | strategy must match code and live market reality before more investment decisions are made | `docs/product-strategy/*` |
| Payment production hardening | billing is already central to the product and must behave like a production system | `PaymentGatewayService`, `LearnerService`, billing UI, webhook endpoints |
| Strict entitlement enforcement | premium access cannot rely on loose UI assumptions | learner premium routes, billing state, checkout completion, review purchase paths |
| Exam-family abstraction cleanup | OET-only assumptions still block clean shared-core reuse | `LearnerService`, frontend copy, score validation, readiness summaries |
| AI trust boundary hardening | fast AI feedback must remain explicitly non-official and escalation-aware | learner result views, admin AI config, expert routing |

## Near-term high value

These items deepen the flagship product and improve conversion and retention.

| Workstream | Why next | Primary surfaces |
| --- | --- | --- |
| OET writing revision coaching | writing is a premium-value and high-anxiety area | writing flows, evaluation summaries, compare-attempt reporting |
| OET speaking transcript and phrasing loop | speaking trust improves when learners can see what to fix | speaking results, transcript analysis, review escalation |
| Expert SLA and QA visibility | premium review promise must be operationally visible | expert workspace, admin review ops, learner status views |
| Content provenance and QA analytics | content scale needs operational discipline before more authoring volume | admin content tooling, revision analytics, background QA jobs |
| Readiness blocker improvements | learners need sharper next-action guidance | readiness views, dashboard, notifications |
| Billing UX cleanup | conversions improve when plans, add-ons, and wallet use are obvious | billing page, entitlements messaging, checkout feedback |

## Mid-term strategic

These items open the next layer of scale once the flagship and monetization loops are stronger.

| Workstream | Outcome | Notes |
| --- | --- | --- |
| IELTS operational rollout | second exam family on the shared core | must include Academic versus General handling and IELTS-native reporting |
| Shared engagement loops | better reactivation and consistency | readiness digests, study reminders, improvement snapshots |
| Content-performance-driven authoring | smarter backlog and quality decisions | prioritize by exam family, task type, rubric gap, and performance data |
| Advanced compare-attempt analytics | stronger retention and outcome confidence | especially valuable for productive skills |

## Later-phase expansion

These items are strategically valuable but should wait until the shared core and flagship economics are healthier.

| Workstream | Why later |
| --- | --- |
| PTE engine and launch | requires a dedicated question-type and simulation model |
| broader mobile-specific learning programs | should follow stronger core learning loops and monetization |
| deeper desktop-shell investments | desktop is not the primary product risk right now |
| large community or cohort features | secondary to product trust and paid outcome value |

## Avoid or defer

| Item | Reason |
| --- | --- |
| redoing the entire architecture | unnecessary; the current codebase is already substantial and reusable |
| treating PTE as a simple exam toggle | product model is too distinct |
| shipping more shell polish before payment and trust reliability | wrong order of operations |
| adding shallow multi-exam marketing pages before real IELTS capability | risks credibility loss |

## Recommended sequencing

1. Finish truth-reset docs and keep them current.
2. Complete payment reliability and entitlement strictness.
3. Finish the shared-core exam-family cleanup.
4. Deepen OET trust, revision, and expert-ops value.
5. Mature content ops and analytics.
6. Operationalize IELTS next.
7. Start PTE only when its dedicated engine is funded and scoped.

## Release gates

- OET regressions remain green before any IELTS rollout flag is enabled.
- Payment webhooks, entitlement gating, and wallet flows have dedicated automated coverage.
- AI-assisted summaries always carry non-official trust framing.
- Strategy docs continue to separate verified from proposed.
