# 03 - Current Project Audit

## Document intent

This audit reflects verified repository reality on April 4, 2026.

Code is the authoritative system of record. Existing strategy docs and product-manual files were used only to accelerate verification.

## Verified stack

### Frontend and packaging

- Next.js 15 App Router
- React 19
- TypeScript 5.9
- Tailwind 4
- Motion 12
- Vitest and Playwright
- Electron shell for desktop
- Capacitor shell for mobile

### Backend

- ASP.NET Core on `net10.0`
- Entity Framework Core 10
- Npgsql PostgreSQL provider
- JWT auth plus development-auth shortcuts
- xUnit integration and API test coverage

## Verified surface areas

### Learner web app

Verified route families include:

- onboarding and goals
- diagnostic hub and per-skill diagnostics
- study plan
- writing workflows
- speaking workflows
- reading workflows
- listening workflows
- mocks and mock reports
- progress and readiness
- submissions and compare
- billing and wallet views
- settings and auth/account flows

### Expert console

Verified route families include:

- dashboard
- queue and filter metadata
- writing review workspace
- speaking review workspace with audio access
- learner context views
- calibration center
- metrics
- schedule

### Admin surface

Verified route families include:

- dashboard
- content management and publish/archive actions
- content revision history and restore
- taxonomy management
- criteria management
- AI configuration management and activation
- feature flags
- notifications governance and delivery health
- review operations
- quality analytics
- user administration
- billing administration
- audit logs

## Backend API groups

### Auth

Verified endpoint group: `/v1/auth`

Capabilities include:

- register, sign-in, refresh, sign-out
- external auth start/callback/exchange
- email verification OTP
- forgot/reset password
- MFA setup and challenge
- current-user profile lookup

### Learner

Verified endpoint groups and endpoints include:

- `/v1/learner/bootstrap`, `/me`, `/onboarding`, `/goals`
- `/v1/diagnostic/*`
- `/v1/study-plan/*`
- `/v1/writing/*`
- `/v1/speaking/*`
- `/v1/reading/*`
- `/v1/listening/*`
- `/v1/mocks`, `/v1/mock-attempts`, `/v1/mock-reports`
- `/v1/reviews/*`
- `/v1/billing/*`
- `/v1/payment/webhooks/stripe`
- `/v1/payment/webhooks/paypal`
- `/v1/reference/exam-families`
- `/v1/learner/engagement/*`
- `/v1/learner/readiness/risk`

### Expert

Verified endpoint group: `/v1/expert`

Capabilities include:

- dashboard and queue
- claim and release workflow
- writing and speaking review detail
- speaking review audio
- submit and rework review flows
- learner roster and learner detail
- calibration, metrics, schedule, and review history

### Admin and notifications

Verified endpoint groups: `/v1/admin`, `/v1/notifications`, `/v1/admin/notifications`, `/v1/analytics`

Capabilities include:

- content CRUD-adjacent workflows and revisions
- taxonomy and criteria management
- AI config lifecycle
- feature flags lifecycle
- review-ops assignment/cancel/reopen/failure inspection
- billing plan, add-on, coupon, subscription, redemption, and invoice views
- notification preferences, delivery health, and test-email flows
- analytics event ingestion

## Verified domain foundations

### Shared learner domain

The shared learner domain already includes exam-family fields in multiple key entities. This means the repo is not OET-only at the schema level, even if some UX and logic still are.

Verified patterns in the domain model:

- `ExamFamilyCode` is already present in multiple learner/content/evaluation entities.
- review requests are first-class entities with state machines.
- wallets exist as first-class learner assets.
- readiness, study-plan, attempts, and evaluations are already modeled.

### Billing and monetization domain

Verified entities and records already exist for:

- billing plans
- add-ons
- coupons
- coupon redemptions
- subscriptions
- invoices
- wallet balances
- wallet transaction ledger
- payment transactions
- payment webhook events

This is why the older strategy proposals to add wallet/payment foundations were stale.

### Admin and governance domain

Verified entities already exist for:

- content revisions
- feature flags
- notification events and policies
- admin audit trails
- expert review artifacts

## Verified operational strengths

### Strength 1: three-sided operating model already exists

The product is not just learner-facing. It already has the operator surfaces needed to run premium review and governance loops.

### Strength 2: billing is a real domain, not placeholder pricing copy

The platform already models plans, add-ons, invoices, coupons, subscriptions, wallet credits, and payment records. This makes monetization hardening an extension task, not a greenfield task.

### Strength 3: OET productive-skill depth is real

Writing and Speaking already have the deepest learner loops and the strongest review operations. This is the right flagship advantage.

### Strength 4: exam-family expansion is structurally possible

Exam-family codes already exist in the backend. The real work now is alignment of copy, scoring validation, readiness logic, reporting, and content operations.

## Current limitations and caveats

### Incomplete payment hardening before this session

The billing domain existed, but provider-grade hosted checkout behavior, webhook normalization, and stricter entitlement alignment still needed tightening.

### OET-biased learner logic still exists

Even with exam-family fields in the backend, parts of the learner experience still assumed OET score scales, OET copy, or OET task framing.

### AI trust surfacing was under-explained

The platform had AI configuration and evaluation flows, but learner-facing confidence and provenance cues were not consistently exposed.

### Content governance is ahead of content performance governance

Revision history exists, but broader provenance, rubric-mapping discipline, stale-content review, and performance analytics still need maturity.

### Shell layers exceed current commercial priority

Electron and Capacitor are valuable distribution layers, but the business-critical work remains core web learning loops, monetization, and trust.

## Audit conclusion

The current project should be treated as a mature, extensible OET-first platform with real learner, expert, admin, billing, and governance foundations already in place.

The next stage is not architectural invention. It is focused evolution in five areas:

1. monetization reliability
2. exam-family abstraction cleanup
3. AI trust boundary hardening
4. OET flagship deepening
5. content-operations maturity

## Verified code anchors

- `app/`
- `lib/api.ts`
- `backend/src/OetLearner.Api/Endpoints/`
- `backend/src/OetLearner.Api/Domain/Entities.cs`
- `backend/src/OetLearner.Api/Domain/BillingEntities.cs`
- `backend/src/OetLearner.Api/Domain/AdminEntities.cs`
- `docs/product-manual/_audit-fact-base.md`
- `docs/product-manual/cross-system-business-logic-and-workflows.md`
