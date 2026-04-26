# Sprint 3 Status

**Created:** 2026-04-26  
**Source:** `docs/product-strategy/08_implementation_roadmap.md`  
**Status:** Proposed - awaiting user sign-off before code work.

## Scoring method

Candidate roadmap items are scored as **impact x effort fit** where:

- Impact: 1 low, 5 mission-critical user/revenue/trust impact.
- Effort fit: 1 too large/uncertain for one sprint, 5 highly sprintable.
- Priority score: impact x effort fit.

## Candidate scoring

| Rank | Candidate | Impact | Effort fit | Score | Rationale |
| --- | --- | ---: | ---: | ---: | --- |
| 1 | Strict entitlement enforcement | 5 | 4 | 20 | Protects premium value and closes UI-only access assumptions. |
| 2 | Payment production hardening | 5 | 3 | 15 | High revenue impact; scope must stay focused on webhooks, checkout, and regression tests. |
| 3 | AI trust boundary hardening | 5 | 3 | 15 | Keeps fast AI feedback clearly advisory and escalation-aware. |
| 4 | OET writing revision coaching | 4 | 3 | 12 | High learner value in a premium/anxiety-heavy module. |
| 5 | Expert SLA and QA visibility | 4 | 3 | 12 | Makes premium review promises operationally visible to staff and learners. |
| 6 | Content provenance and QA analytics | 4 | 3 | 12 | Supports safe content scaling and admin confidence. |
| 7 | OET speaking transcript and phrasing loop | 4 | 2 | 8 | Valuable but overlaps with conversation/speaking work; keep tightly scoped. |
| 8 | Readiness blocker improvements | 3 | 3 | 9 | Improves next-action guidance but should not outrank monetization/trust. |
| 9 | Exam-family abstraction cleanup | 4 | 2 | 8 | Important platform work, but risks broad churn in a sprint. |
| 10 | Billing UX cleanup | 3 | 3 | 9 | Useful after payment/entitlement reliability is locked. |

## Selected Sprint 3 scope

### S3-H1 - Strict entitlement enforcement
- **Acceptance criteria**
  - Premium learner routes and backend endpoints enforce entitlement server-side.
  - Free/trial limits are covered by unit or backend tests.
  - User-facing blocked states explain the required upgrade path.
  - No pass/fail or score-gating logic bypasses canonical scoring services.

### S3-H2 - Payment production hardening
- **Acceptance criteria**
  - Stripe checkout, cancellation, webhook idempotency, and wallet/review-credit mutations have regression coverage.
  - Webhook verification rejects missing/invalid signatures.
  - Billing state reconciliation is observable in admin/support views.
  - Sandbox fallbacks remain disabled in production configuration.

### S3-H3 - AI trust boundary hardening
- **Acceptance criteria**
  - Learner AI feedback/result surfaces show advisory/non-official framing.
  - Scoring/evaluation AI calls remain platform-only where policy requires it.
  - Admin AI configuration exposes provider health without leaking secrets.
  - Tests cover at least one refusal/guardrail path per productive skill family touched.

### S3-H4 - OET writing revision coaching
- **Acceptance criteria**
  - Learners can compare a revised writing attempt against the previous attempt.
  - Suggestions cite rulebook/scoring-grounded reasons.
  - Accepted/dismissed suggestion events are tracked through the typed analytics registry.
  - Existing writing grading flows remain server-authoritative.

### S3-H5 - Expert SLA and QA visibility
- **Acceptance criteria**
  - Expert queue and admin ops surfaces expose SLA age, priority, and rework risk.
  - Overdue or high-risk reviews are filterable.
  - QA metrics are backed by API contracts and tests.
  - Learner status messaging remains clear and non-internal.

### S3-H6 - Content provenance and QA analytics
- **Acceptance criteria**
  - Admin content lists show provenance completeness and QA status.
  - Publish gate failures surface actionable asset/provenance issues.
  - Background QA metrics are queryable without raw file reads outside `IFileStorage`.
  - CSV/export paths use existing export utilities and respect permissions.

### S3-H7 - Speaking transcript and phrasing loop
- **Acceptance criteria**
  - Speaking results surface transcript excerpts tied to phrasing feedback.
  - Learners can drill at least one suggested phrasing replacement.
  - Any ASR/transcript data uses existing storage/retention patterns.
  - Expert escalation remains available for uncertain automated feedback.

## Sign-off gate

No Sprint 3 code should start until the user accepts or edits this scope.
