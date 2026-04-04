# 06 - Feature Strategy and Blueprint

## Document intent

This blueprint separates:

- shared-core features that should serve OET and IELTS
- OET flagship features that justify premium pricing and trust
- IELTS-specific work that should be built next
- PTE work that should remain deferred until a dedicated engine is designed

## Shared-core feature blueprint

### Verified platform features to keep and strengthen

| Capability | Verified today | Next product move |
| --- | --- | --- |
| Onboarding and goals | learner onboarding, goals, registration profile, exam-family references | make copy, target validation, and goal guidance fully exam-family-aware |
| Diagnostics | per-skill diagnostics and dashboard visibility | connect diagnostics more tightly to readiness risk and next-action plans |
| Practice engine | writing, speaking, reading, listening, mocks | unify learner messaging and reporting across OET and IELTS-ready shared flows |
| AI evaluation | fast productive-skill scoring and summaries | make confidence, provenance, and escalation logic visible and consistent |
| Expert review | premium review routing, expert queues, admin ops | surface clearer SLA, turnaround, and QA signals to learners and operators |
| Billing and entitlements | plans, add-ons, coupons, wallet, invoices, subscriptions | make checkout, webhook handling, wallet top-ups, and gating operationally strict |
| Notifications | inbox, preferences, delivery infrastructure | focus on study reminders, review-state trust messages, and readiness digests |
| Content operations | admin editing, revisions, taxonomy, criteria | add provenance, stale-content review, rubric coverage, and performance loops |
| Analytics and audit | admin analytics and audit logs | add conversion, entitlement use, AI routing, SLA, and content-performance observability |

### Shared-core product slices

#### Slice A - Documentation truth reset

Scope:

- re-baseline all strategy docs against code and live sources
- clearly mark verified versus proposed
- stop relying on stale strategy assumptions

Success state:

- every major strategic claim is traceable to code or linked market sources

#### Slice B - Monetization hardening

Scope:

- production-grade Stripe and PayPal checkout flows
- idempotent webhook handling
- wallet top-up safety
- invoice and subscription transition reliability
- strict entitlement checks on premium actions

Success state:

- no premium path depends on a sandbox shortcut
- purchases and entitlements reconcile cleanly

#### Slice C - Exam-family abstraction cleanup

Scope:

- score validation by exam family
- readiness logic cleanup
- learner-facing copy cleanup
- frontend type and cache naming cleanup

Success state:

- OET remains premium, but shared-core logic no longer assumes every user is OET

#### Slice D - AI trust boundary hardening

Scope:

- confidence bands
- provenance labels
- escalation rules
- expert-routing visibility
- learner-safe "AI-assisted / not official score" messaging

Success state:

- learners know what the score means and when human review matters

#### Slice F - Content ops maturity

Scope:

- manual plus AI-drafted authoring support
- provenance tracking
- stale-content review
- rubric mapping visibility
- content performance analytics

Success state:

- the platform can scale content safely before broader IELTS or PTE authoring

## OET flagship blueprint

### Product thesis

OET should win on productive-skill depth, profession specificity, and review trust.

### OET features to deepen next

| Feature | Why it matters | Priority |
| --- | --- | --- |
| Writing revision coaching | OET learners need actionable second-pass improvement, not only one-shot scoring | Immediate |
| Profession-specific writing remediation | The product should coach nurses, doctors, pharmacists, and others differently where the communication patterns differ | Near-term |
| Speaking role-play transcript loop | Learners need transcript-backed phrasing feedback, not only generic fluency comments | Immediate |
| Compare-attempt analytics | Learners should see whether revisions are improving the right criteria over time | Near-term |
| Expert SLA and QA visibility | Premium review trust rises when turnaround and quality expectations are transparent | Near-term |
| Readiness blockers for productive skills | OET candidates need explicit blockers tied to writing and speaking evidence, not only generalized progress | Near-term |

### OET features to avoid

- generic gamification before evidence-based coaching improves
- shallow "all professions are the same" content reuse
- AI-only premium positioning without visible review trust controls

## IELTS blueprint

### IELTS should be the next operational exam family

The shared core can support IELTS next, but the IELTS layer must feel native.

### Required IELTS feature set

| Capability | Shared-core reuse | IELTS-specific work |
| --- | --- | --- |
| Onboarding and goals | registration, billing, notifications, analytics | Academic versus General selection |
| Writing tasks | editor, AI trust messaging, expert-review patterns | Task 1 versus Task 2 logic, task weighting, criteria-aware reporting |
| Speaking | speaking flow, summaries, trust boundary | IELTS band logic and speaking criteria framing |
| Reading and listening | diagnostics, scoring surfaces, dashboards | IELTS-specific content banks and score interpretation |
| Readiness | study plans, readiness shell | IELTS-native band and pathway reporting |

### IELTS feature strategy

1. Launch Academic and General as a first-class distinction.
2. Reuse the shared four-skill loop.
3. Build IELTS-native reporting and criteria handling.
4. Reuse billing, entitlements, notifications, and admin tooling.

## PTE blueprint

### Product stance

PTE is strategically attractive, but it should not be launched as a shallow exam-family flag.

### Required future PTE engine

| Capability | Why it is distinct |
| --- | --- |
| question-type bank | PTE is organized around many discrete item types rather than a small set of broad task families |
| integrated-skill orchestration | many PTE items blend reading, writing, listening, and speaking |
| rapid drill loop | PTE users often expect high-frequency, drill-oriented repetition |
| computer-based timing and simulation | timing and interface fidelity matter more than in a paper-derived model |
| AI-heavy scoring and remediation | fast speaking and writing scoring are central to PTE value expectations |

### PTE rule

Do not market PTE broadly until the dedicated question-type engine and analytics model exist.

## Admin and operations blueprint

| Area | Verified foundation | Next move |
| --- | --- | --- |
| AI config | admin AI controls already exist | add confidence-policy and escalation visibility |
| Review ops | queue and calibration already exist | add SLA and QA views tied to premium promises |
| Content ops | revisions and taxonomy exist | add provenance, stale review, and rubric coverage views |
| Billing ops | plans, add-ons, coupons, subscriptions, invoices already exist | add provider-state visibility and reconciliation ops |
| Product analytics | admin dashboards already exist | expand around conversion, entitlement use, and content performance |

## Out of scope for now

- major desktop-shell expansion
- broad mobile-only feature programs
- shallow PTE rollout
- community and cohort investments before core monetization and trust loops are stronger
