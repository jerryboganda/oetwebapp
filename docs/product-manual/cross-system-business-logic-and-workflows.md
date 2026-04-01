# Cross-System Business Logic and Workflows

This document explains how the learner, expert, and admin surfaces operate as one OET preparation system.

Related documents:

- [Master Product Manual](./master-product-manual.md)
- [Learner App Manual](./learner-app-manual.md)
- [Expert Console Manual](./expert-console-manual.md)
- [Admin Dashboard and CMS Manual](./admin-dashboard-cms-manual.md)
- [_Audit Fact Base](./_audit-fact-base.md)

## 1. Business Logic Breakdown

### 1.1 OET-specific preparation logic

The platform assumes that OET preparation is not one uniform workflow. The current system distinguishes between:

- objective sub-tests
  - Reading
  - Listening
- productive sub-tests
  - Writing
  - Speaking

That distinction shapes the product:

- objective sub-tests are answer- and explanation-driven
- productive sub-tests are evaluation-, feedback-, revision-, and review-driven

### 1.2 Criterion-first feedback logic

The platform does not stop at summary scoring for productive skills. Its current model is:

1. generate or retrieve evaluation output
2. break the result into criterion-level evidence
3. convert the evidence into revision-ready feedback
4. allow escalation to expert review

This is clearest in Writing, and it also appears in Speaking through transcript, phrasing, and expert-review flows.

### 1.3 Practice-first workflow logic

The learner product is organized around repeated practice, not static content consumption. The sequence is:

- define targets
- establish a diagnostic baseline
- execute planned practice
- inspect performance evidence
- revise or escalate where needed
- validate through mocks

### 1.4 Expert review trust logic

Expert review is treated as an operational trust layer, especially for Writing and Speaking. The system supports this through:

- explicit review-request surfaces
- a queue with claim and release logic
- review workspaces with rubric scoring
- calibration and metrics
- admin review operations for assignment and intervention

### 1.5 Readiness estimation logic

Readiness is not presented as a single course-completion metric. The current implementation indicates readiness is based on:

- diagnostic evidence
- task performance
- trend data
- mock evidence
- expert-review evidence
- blockers and weak-link identification

### 1.6 Content and admin control logic

Admins do not only manage text assets. They manage the structural rules of the platform:

- content definitions
- profession taxonomy
- criteria
- AI evaluation configuration
- review operations
- notification policy
- billing and entitlement logic
- feature flags
- audit trails

## 2. Dashboard Interdependency Map

| Source surface | Action | Dependent surface | Operational effect |
| --- | --- | --- | --- |
| Admin | publish content | Learner | practice content becomes available |
| Admin | edit taxonomy | Learner / Expert | profession mapping changes across goals, content, and queue filters |
| Admin | edit criteria | Learner / Expert | feedback structure and rubric logic change |
| Admin | activate AI config | Learner / Expert | evaluation routing or model behavior can change |
| Learner | request Writing review | Expert | creates Writing review work |
| Learner | request Speaking review | Expert | creates Speaking review work |
| Expert | submit review | Learner | feedback evidence returns to learner-visible history and readiness |
| Admin | assign/reopen/cancel review | Expert / Learner | changes review throughput and expected delivery |
| Admin | edit billing objects | Learner | changes plan, add-on, coupon, and review entitlement behavior |
| Admin | edit notification policy | Learner / Expert / Admin | changes how events are communicated |

## 3. Feature Dependency Graph

### 3.1 Learner planning loop

`Onboarding -> Goals -> Diagnostics -> Study Plan -> Practice -> Progress/Readiness`

### 3.2 Writing improvement loop

`Writing Library/Home -> Writing Player -> Writing Result -> Writing Feedback -> Writing Revision -> Submissions/Compare`

Optional escalation path:

`Writing Result or History -> Writing Expert Review Request -> Expert Queue -> Expert Writing Review -> Learner Evidence`

### 3.3 Speaking improvement loop

`Speaking Home -> Task Selection -> Device Check -> Role Card -> Speaking Task -> Speaking Result -> Transcript/Phrasing`

Optional escalation path:

`Speaking Result -> Speaking Expert Review Request -> Expert Queue -> Expert Speaking Review -> Learner Evidence`

### 3.4 Objective-skill loop

`Reading or Listening Home -> Player -> Results -> Progress/Readiness`

Listening extends this with:

`Results -> Transcript-backed Review -> Drills`

### 3.5 Mock loop

`Mock Setup -> Mock Player -> Mock Report -> Study Plan / Readiness / Progress`

### 3.6 Admin governance loop

`Content/Criteria/AI Config/Flags/Notifications/Billing -> Learner and Expert behavior -> Quality Analytics / Audit Logs / Review Ops`

## 4. Operational Lifecycle Examples

### 4.1 Diagnostic to study-plan lifecycle

1. Learner completes onboarding and goals.
2. Learner starts diagnostics.
3. The four sub-test diagnostics create baseline evidence.
4. Diagnostic summary identifies weak areas and a starting readiness picture.
5. Study plan becomes the primary operational layer for ongoing preparation.

### 4.2 Writing submission lifecycle

1. Learner opens a Writing task and submits a response.
2. The system creates or retrieves evaluation output.
3. The learner sees a result summary.
4. The learner inspects criterion-level detailed feedback.
5. The learner revises or requests expert review.
6. Submitted and reviewed work becomes part of history, progress, and readiness evidence.

### 4.3 Speaking submission lifecycle

1. Learner chooses a speaking task and passes device check.
2. Learner records and submits.
3. The system produces result assets such as score-range output and transcript-linked evidence.
4. The learner uses transcript review and phrasing drills for self-improvement.
5. The learner can escalate into expert review.
6. Expert feedback becomes part of the learner evidence loop.

### 4.4 Review request lifecycle

1. Learner requests expert review.
2. The request enters review operations.
3. Expert queue exposes the request with SLA, priority, and metadata.
4. An expert claims the work and uses the relevant workspace.
5. The expert saves a draft or submits the review.
6. Admin can intervene through review ops if the pipeline stalls or fails.
7. The review outcome returns to learner evidence.

### 4.5 Content lifecycle

1. Admin creates or edits a content item.
2. Admin maps profession, sub-test, and criteria focus.
3. Admin publishes the item.
4. Learners can access the item through the relevant module.
5. If needed, admin restores a prior revision.

### 4.6 Mock-to-readiness lifecycle

1. Learner configures a full or partial mock.
2. Mock player coordinates section access.
3. Learner completes and submits the mock.
4. Mock report summarizes overall score, sub-test breakdown, and weakest criterion.
5. The report pushes the learner back into targeted study planning and readiness interpretation.

## 5. Business-to-Feature Mapping

| Business need | Implementing features |
| --- | --- |
| Establish learner baseline | onboarding, goals, diagnostics, diagnostic results |
| Personalize preparation | study plan, weak-skill focus, readiness, progress |
| Support productive-skill improvement | Writing feedback, Writing revision, Speaking transcript, Speaking phrasing drills |
| Add trusted human review | Writing expert review request, Speaking expert review request, expert queue, expert workspaces |
| Validate exam readiness | mocks, readiness, progress trends |
| Keep content domain-specific | admin content editor, taxonomy, criteria |
| Govern AI behavior | admin AI config, quality analytics |
| Protect review operations | expert schedule, expert calibration, admin review ops |
| Control communications | learner settings, admin notification governance |
| Control entitlements and monetization | learner billing, admin billing ops |

## 6. Risks, Gaps, and Fragile Areas

### 6.1 Transitional routing and content IDs

Some learner flows still depend on fixed task IDs. This is most visible in:

- diagnostic module routing
- mock player section launches

This creates a fragility risk when content becomes more dynamic.

### 6.2 Unavailable AI-live speaking mode

Speaking route logic exposes an AI mode, but the live experience is not functionally available. This creates a gap between route design and actual capability.

### 6.3 Mixed transitional data signals

Frontend code still includes mock-data and transitional wording. Even though the backend is substantial, that mixed state increases the risk of documentation drift and incomplete migration.

### 6.4 Operational dependencies

The platform's strongest value comes from cross-surface coordination. That means failures in any one layer can propagate:

- learner review requests depend on review ops
- expert work depends on upstream artifacts such as transcript or AI-support assets
- quality analytics depend on reliable downstream event and evaluation capture

### 6.5 Copy and presentation quality issues

Encoding artifacts in UI copy reduce polish and can create confusion in critical flows such as reports and task titles.

## 7. What to Validate End to End

The following scenarios should be treated as cross-system regression paths:

- onboarding -> goals -> diagnostics -> study plan
- Writing submission -> result -> feedback -> revision -> review request -> expert review
- Speaking selection -> device check -> task -> transcript -> review request -> expert review
- content publish -> learner availability
- review assignment -> expert completion -> learner visibility
- mock completion -> report -> study-plan update path
- notification policy update -> visible communication behavior
- billing entitlement update -> review-request eligibility

## 8. Cross-System Conclusion

The system already behaves like an integrated OET operations platform rather than three disconnected dashboards. Its strongest design pattern is that each surface exists to complete a different part of the same preparation loop:

- learner surfaces create evidence
- expert surfaces validate and enrich evidence
- admin surfaces define, govern, and stabilize the system that produces the evidence

The main weaknesses are transitional implementation details, not a missing product model. The overall business logic is already coherent and clearly expressed in the current routes, workspaces, and operational control surfaces.

