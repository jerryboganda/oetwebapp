# 01 - Business Requirement and Product Thesis

## Document intent

This file is a reset of the product thesis as of April 4, 2026.

- Verified truth comes from the codebase, route surfaces, domain entities, endpoint groups, and product-manual audit files.
- Market context comes from current official and competitor sources linked in [02_market_research_and_competitor_benchmark.md](./02_market_research_and_competitor_benchmark.md).
- Any item marked Proposed is not treated as already shipped.

## Source-of-truth hierarchy

1. Code in `app/`, `lib/`, `backend/src/OetLearner.Api/`, `electron/`, and test suites.
2. Product-manual audit files in `docs/product-manual/`.
3. Refreshed product-strategy docs in this folder.
4. Older strategy assumptions only where they still match code or current market sources.

## Verified product reality

The platform is already a serious exam-prep product, not a concept-stage build.

Verified in code today:

- Learner, expert, and admin web surfaces are live.
- The backend already contains exam-family references for `oet`, `ielts`, and `pte`.
- Productive-skill workflows already support AI evaluation plus premium expert review routing.
- Admin operations already include content management, revisions, taxonomy, criteria, AI configuration, feature flags, notifications, billing, review operations, and audit logs.
- Billing already includes plans, add-ons, coupons, subscriptions, invoices, wallet balances, wallet transactions, payment transactions, and payment webhook events.
- Electron and Capacitor are packaging layers around the shared product, not separate products.

## Product thesis

OET Prep should operate as an outcome-oriented exam-preparation platform for high-stakes English tests, with OET as the flagship premium product and multi-exam expansion as the scale path.

The product wins when it helps serious candidates do five things better than official-only or content-only alternatives:

1. Diagnose their likely score gap quickly.
2. Practice in formats that feel close to the real exam.
3. Understand exactly why a response is weak or strong.
4. Escalate high-stakes productive work into trusted human review when it matters.
5. See a believable path from current level to target score before test day.

## Strategic position

### OET first

OET remains the flagship because the platform already has the strongest domain fit there:

- profession-specific writing and speaking workflows
- productive-skill review operations
- healthcare-oriented content and copy
- readiness and remediation loops that make sense in a clinical communication context

### IELTS second

IELTS is the next operational exam family because it can reuse most of the existing shared core:

- four-skill product structure
- diagnostic and readiness flows
- practice engine and study-plan model
- billing and entitlement model
- AI-assisted productive-skill evaluation with clearer task-specific reporting

The main work is not building a new company. It is abstracting score logic, content models, writing/speaking criteria, and learner-facing copy away from OET-only assumptions.

### PTE later

PTE should not be forced into the current OET interaction model.

It needs a more distinct engine because:

- the exam is fully computer-based
- question-type granularity is much higher
- integrated-skill timing and interaction patterns matter more
- rapid AI scoring and remediation loops are a much larger part of the value proposition

PTE is therefore a strategic follow-on, not the next immediate release.

## Primary users and paying logic

### Primary user

Healthcare professionals preparing for OET.

This includes nurses, doctors, and allied health candidates who need profession-aware preparation and often value human feedback on Writing and Speaking.

### Secondary user

IELTS candidates who want stronger practice structure, clearer readiness feedback, and better guided improvement than generic practice libraries offer.

### Internal operators

- Experts who deliver review quality, turnaround speed, and trusted scoring support.
- Admin and content operators who control content quality, taxonomy, AI behavior, review operations, billing, and auditability.

### Why users pay

Users will pay for:

- better score confidence before booking or rebooking an exam
- faster improvement on Writing and Speaking
- mock and readiness loops that feel predictive rather than decorative
- premium human review at the point of risk, not everywhere by default
- trusted explanations of score movement and remediation priorities

## Business outcomes that matter

### Conversion

The product should convert free and trial users by proving value early through:

- diagnostic baseline
- score-gap clarity
- exam-accurate practice
- visible improvement opportunities
- credible upgrade paths into reviews, mocks, and premium planning

### Retention

Retention should come from active study loops, not passive content browsing:

- study plans
- attempt history and compare views
- readiness movement
- mock cycles
- follow-up drills and revision flows
- scheduled review turnaround and notifications

### Trust

Trust is a product requirement, not a branding layer.

The platform must clearly communicate:

- AI-assisted vs human-reviewed outputs
- not official score vs official exam result
- confidence or uncertainty level
- when escalation to expert review is recommended
- what evidence produced a recommendation or score estimate

### Operational feasibility

The business only scales if expert review is treated as a premium scarce resource.

That means:

- human review is entitlement-gated and/or credit-based
- AI handles large-volume first-pass evaluation and triage
- admin tools expose SLA, QA, reassignment, and failure visibility
- content and AI configuration changes are auditable

## Product principles

1. Shared core first, exam-specific depth second.
2. OET keeps the deepest premium experience.
3. Human review is high-value and selectively routed.
4. AI output must be confidence-labeled and clearly non-official.
5. Simulation quality matters more than content volume.
6. Every premium feature must improve either outcomes, trust, or operator leverage.
7. Packaging layers do not get priority over core learner loops and monetization.

## Non-goals for the current planning horizon

- Rewriting the entire platform.
- Treating all exam families as equal in the next release cycle.
- Making PTE a surface-level clone of OET or IELTS.
- Turning every submission into a human-reviewed workflow.
- Prioritizing desktop/mobile shell expansion ahead of core web learning loops and payment reliability.

## Thesis statement

The right next move is not a rebuild. It is a disciplined re-baseline:

- keep OET as the premium flagship
- operationalize IELTS on the shared core
- defer PTE until its engine is designed properly
- harden monetization, trust labeling, exam-family abstraction, expert operations, and content governance

That path creates a more credible product today and a more scalable exam-prep platform tomorrow.
