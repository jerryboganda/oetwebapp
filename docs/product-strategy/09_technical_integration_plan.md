# 09 - Technical Integration Plan

## Document intent

This plan is anchored in the current service layout and domain model. It avoids proposing foundation work that already exists.

## Integration principles

1. Prefer additive changes to existing services over new platform layers.
2. Keep provider-specific logic inside payment adapters and normalization services.
3. Keep score interpretation, entitlement checks, and AI trust messaging backend-owned.
4. Keep frontend changes contract-driven through `lib/api.ts` and typed view models.
5. Add persistence only where it improves reliability, auditability, or analytics.

## Service-centered implementation plan

### `LearnerService`

Current verified role:

- learner dashboard and readiness orchestration
- diagnostics, writing, speaking, reading, listening, mocks
- billing actions and wallet actions
- productive-skill summary shaping

Technical changes:

| Focus area | Required work |
| --- | --- |
| exam-family validation | validate target scores and score ranges by exam family |
| shared-core copy and reporting | stop leaking OET-only assumptions into summaries, readiness, and learner flows |
| payment orchestration | create hosted checkout sessions, persist pending payment state, handle webhook completion safely |
| wallet handling | top-up creation, completion, and balance-safe crediting |
| AI trust surface | return confidence labels, provenance labels, escalation recommendations, and learner-safe disclaimers |
| compare-attempt insights | extend writing and speaking reporting with progression views where not yet available |

### `ExpertService`

Current verified role:

- queue operations
- claim, release, submit, and rework flows
- drafts and calibration
- expert metrics

Technical changes:

| Focus area | Required work |
| --- | --- |
| escalation context | show why an attempt was routed to human review |
| SLA visibility | make turnaround and queue risk visible to experts and operators |
| QA reporting | expose quality and calibration views that support premium review promises |
| exam-family support | allow future IELTS review logic without breaking OET review operations |

### `AdminService`

Current verified role:

- content operations and revisions
- taxonomy and criteria admin
- AI configuration
- billing admin
- review operations
- feature flags and audit logs

Technical changes:

| Focus area | Required work |
| --- | --- |
| AI policy visibility | surface confidence thresholds, escalation rules, and audit trails more clearly |
| content provenance | track manual versus AI-drafted lineage and freshness review state |
| content QA analytics | show rubric coverage, stale content, and performance indicators |
| billing oversight | expose provider-state visibility and reconciliation outcomes for operations |
| experiment control | use existing feature flags and AI config to stage trust and exam-family changes safely |

### `PaymentGatewayService`

Current verified role:

- gateway selection
- checkout creation
- webhook verification and normalization

Technical changes:

| Focus area | Required work |
| --- | --- |
| production credentials | complete live Stripe and PayPal credential rollout and environment separation |
| hosted checkout | keep provider-grade checkout URLs as the default flow |
| webhook idempotency | normalize, persist, and safely ignore duplicates |
| reconciliation hooks | expose enough normalized state for background reconciliation and admin visibility |
| provider health telemetry | log failures and processing outcomes for operations |

### `BackgroundJobProcessor`

Current verified role:

- async evaluation and notification processing

Technical changes:

| Focus area | Required work |
| --- | --- |
| payment reconciliation | verify late or failed provider state transitions |
| AI confidence follow-up | trigger escalation or review routing when confidence or policy requires it |
| content QA refresh | compute freshness and performance snapshots where needed |
| engagement loops | generate readiness digests and study reminder jobs |

## API contract changes

### Learner API

Keep or extend:

- checkout session creation
- wallet top-up creation
- Stripe and PayPal webhook endpoints
- evaluation summaries that include exam-family-aware trust fields
- reference data for exam families

Add or expand where needed:

- compare-attempt analytics payloads
- readiness blocker payloads
- entitlement-aware premium action responses

### Expert API

Add or expand where needed:

- queue filters for escalation reasons and SLA risk
- review-detail trust context
- QA and calibration summaries

### Admin API

Add or expand where needed:

- AI confidence and escalation policy read models
- content provenance and freshness reporting
- payment reconciliation and provider-event visibility

## Frontend integration points

### Shared frontend contract layer

Primary file:

- `lib/api.ts`

Key responsibilities:

- normalize backend trust fields into stable frontend types
- keep exam-family mapping centralized
- avoid page-level ad hoc score interpretation logic

### Learner UI surfaces

Primary files already touched or likely to keep evolving:

- goals
- diagnostics
- writing results
- speaking results
- billing
- readiness and dashboard views

Required behavior:

- exam-family-aware copy
- learner-safe AI trust labels
- hosted checkout handling
- premium action gating

### Expert and admin UI surfaces

Primary evolution areas:

- expert queue and review detail
- admin AI config
- admin content tooling
- admin billing and review ops

Required behavior:

- visibility into escalation reason, SLA, QA, provenance, and provider state

## Data-model strategy

The data model should stay additive and minimal.

Reuse current entities wherever possible:

- exam-family references
- billing entities
- review entities
- content revisions
- AI config

Add only missing fields that directly support:

- payment reliability
- AI auditability
- content provenance and QA
- analytics segmentation by exam family, profession, and task type

## Observability plan

Track these as first-class metrics and events:

- conversion by plan, add-on, and exam family
- entitlement consumption and premium action attempts
- expert SLA and turnaround
- AI confidence distribution and escalation rate
- mock-to-score improvement
- readiness drift over time
- content performance by exam family, profession, and task type

## Test plan

Required validation remains:

- frontend: `npm run lint`, `npm test`, `npm run build`
- backend: `npm run backend:test`, `npm run backend:build`
- smoke E2E: `npm run test:e2e:smoke`
- targeted regression for touched learner, expert, and admin flows
- automated coverage for payment webhooks, entitlement gating, expert routing, AI escalation, and exam-family-aware score validation

## Rollout order

1. ship payment and entitlement hardening behind operationally safe configuration
2. finish exam-family cleanup in shared learner and reporting flows
3. deepen OET trust and revision loops
4. mature content ops analytics
5. operationalize IELTS on the shared core
