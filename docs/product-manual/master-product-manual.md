# OET Prep Platform Master Product Manual

This documentation package explains the implemented product across all three operational surfaces:

- [Learner App Manual](./learner-app-manual.md)
- [Expert Console Manual](./expert-console-manual.md)
- [Admin Dashboard and CMS Manual](./admin-dashboard-cms-manual.md)
- [Cross-System Business Logic and Workflows](./cross-system-business-logic-and-workflows.md)
- Working evidence base: [_Audit Fact Base](./_audit-fact-base.md)

## 1. Executive Summary

The OET Prep Platform is a role-based OET preparation system built around three connected surfaces:

- a learner-facing preparation app
- an expert review console
- an admin and CMS control plane

Its purpose is to help healthcare professionals prepare for the OET exam through structured diagnostics, study planning, sub-test practice, mock testing, criterion-based feedback, expert review, and operational oversight.

This is not a generic learning platform. The current implementation is organized around the realities of OET:

- four sub-tests with different preparation needs
- profession-aware practice and goal setting
- objective scoring for Reading and Listening
- criterion-based evaluation and expert intervention for Writing and Speaking
- readiness tracking based on actual evidence, not only content completion

## 2. Product Purpose and Business Logic

### Business problem solved

OET candidates do not only need content. They need a preparation system that can:

- establish a starting level
- translate gaps into a practical study plan
- separate objective sub-test practice from productive-skill review
- provide criterion-level feedback that supports revision
- bring experts into the loop where human judgment matters
- give learners and operators a reliable signal of exam readiness

### Why this platform exists

The platform exists to turn OET preparation into an operationally managed workflow instead of a loose collection of lessons and practice tasks. The system connects:

- learner intent and target scores
- diagnostic baselining
- task-level practice
- automated or AI-assisted evaluation
- expert review operations
- admin-controlled content and quality governance

### How business logic is expressed in the product

The business logic is implemented through five core product ideas:

1. Diagnose before prescribing.
   - Onboarding and goals feed into diagnostic flows and study-plan generation.
2. Treat sub-tests differently.
   - Writing and Speaking use richer evaluation and review workflows than Reading and Listening.
3. Make feedback revision-oriented.
   - Writing and Speaking both include mechanisms that turn evaluation into next actions.
4. Use expert review as a trust and quality layer.
   - Human review is operationalized through request, queue, rubric, SLA, and submission workflows.
5. Govern the system centrally.
   - Content, taxonomy, criteria, AI configuration, review operations, notifications, billing, and auditability are all admin-managed.

## 3. Core Product Model

### Surface 1: Learner App

The learner app is where candidates:

- define goals
- complete diagnostics
- follow a study plan
- practice each OET sub-test
- request expert reviews for Writing and Speaking
- take mocks
- monitor readiness and progress

### Surface 2: Expert Console

The expert console is where reviewers:

- receive review work
- inspect learner context
- assess Writing and Speaking using rubric-driven workspaces
- complete calibration
- manage availability
- track review performance

### Surface 3: Admin Dashboard and CMS

The admin surface is where operators:

- manage content and revisions
- define profession taxonomy
- define rubric criteria
- configure AI evaluation policies
- monitor review operations
- govern notifications
- manage users and billing logic
- control flags and auditability

### Platform operating model

The system runs as a managed loop:

1. Admin defines what the platform can deliver.
2. Learner consumes the preparation experience.
3. Productive-skill submissions generate expert work when needed.
4. Expert feedback re-enters learner progress and readiness evidence.
5. Admin monitors quality, failures, operational load, and policy outcomes.

## 4. Role Matrix

| Role | Primary objective | Main permissions |
| --- | --- | --- |
| Learner | Prepare for OET and reach target score | goals, diagnostics, study plan, sub-test practice, mocks, review requests, readiness, progress, settings, billing |
| Expert | Review productive-skill work accurately and on time | queue, claim/release, Writing review, Speaking review, learner context, calibration, schedule, metrics |
| Admin | Operate and govern the platform | content, taxonomy, criteria, AI config, review ops, notifications, analytics, users, billing ops, flags, audit logs |

What each role does not do:

- learners do not directly manage review assignment or content configuration
- experts do not publish content or control billing rules
- admins do not participate as normal learner users within the documented surfaces

## 5. End-to-End Platform Flow

### Content and control flow

1. Admin creates or updates content, profession mappings, criteria, and evaluation rules.
2. Those definitions become available to learner-facing practice, planning, and evaluation surfaces.
3. Learners complete tasks and produce practice evidence.
4. Writing and Speaking can escalate into expert-review requests.
5. Experts process those requests and submit structured review outputs.
6. Review outcomes feed learner-visible history, progress, and readiness context.
7. Admin monitors the resulting quality, throughput, failures, and policy behavior.

### Learner evidence flow

The current implementation uses multiple evidence sources:

- onboarding and goals
- diagnostic attempts
- study-plan activity
- sub-test attempts and evaluations
- expert reviews
- mock reports
- progress trends
- readiness summaries

## 6. OET Domain Mapping

### Reading

Implemented as objective task practice with:

- dedicated home surface
- timed player
- answer submission
- results review

### Listening

Implemented as objective task practice with richer review support:

- timed player
- answer submission
- results
- transcript-backed review
- targeted drills

### Writing

Implemented as the most complete productive-skill workflow:

- task library
- timed writing workspace
- AI result summary
- detailed criterion feedback
- revision mode
- model answer explainer
- expert review request

### Speaking

Implemented as a productive-skill performance workflow:

- task selection
- device check
- role card preview
- recording session
- results
- transcript review
- phrasing drills
- expert review request

### Mocks

Implemented as an exam-readiness layer:

- full or single-subtest setup
- mock orchestration
- report surface
- follow-through into study planning

### Planning, review, and readiness

The platform connects preparation management to OET-specific outcome logic through:

- goals
- diagnostics
- study plan
- productive-skill feedback and expert review
- mock evidence
- readiness scoring and blockers

## 7. System-Wide Functional Inventory

### Learner-facing areas

- onboarding
- goals
- diagnostics
- home dashboard
- study plan
- Writing
- Speaking
- Reading
- Listening
- mocks
- readiness
- progress
- submissions
- billing
- settings

### Expert-facing areas

- dashboard
- queue
- Writing review
- Speaking review
- learners
- calibration
- metrics
- schedule

### Admin-facing areas

- operations dashboard
- content library and editor
- content revisions
- taxonomy
- criteria
- AI config
- review ops
- notifications
- quality analytics
- users
- billing ops
- feature flags
- audit logs

## 8. Key Business Workflows

### Onboarding to study plan

1. Learner completes onboarding.
2. Learner defines goals and target outcomes.
3. Learner starts diagnostics.
4. Diagnostic evidence feeds a study-plan view with actionable tasks.

### Writing attempt to revision

1. Learner opens a Writing task.
2. Learner drafts and submits.
3. The system produces a Writing result summary.
4. Learner reviews detailed criterion feedback.
5. Learner enters revision mode or requests expert review.

### Speaking attempt to expert review

1. Learner selects a speaking task.
2. Learner completes device check and recording flow.
3. The system produces a score-range result and transcript assets.
4. Learner uses transcript and phrasing drill pages for self-improvement.
5. Learner requests expert review when needed.

### Content creation to learner availability

1. Admin creates or edits a content item.
2. Admin maps profession, sub-test, criteria focus, and model-answer metadata.
3. Admin publishes the item.
4. The item becomes available to learner-facing practice surfaces and related operational logic.

### Review request to feedback return

1. Learner requests expert review for Writing or Speaking.
2. The request enters expert review operations.
3. An expert claims and completes the review.
4. The review becomes part of learner evidence and history.

## 9. Operational Logic

The platform is built as an operational SaaS product, not only a learning interface.

Key operational controls include:

- review backlog visibility
- assignment and rework controls
- calibration and alignment monitoring
- content version restoration
- impact previews for archive actions
- feature flag governance
- notification policy governance
- coupon, credit, and entitlement management
- audit visibility on privileged changes

## 10. Known Gaps and Partial Implementations

### Confirmed partial areas

- Speaking AI live mode is not active in the current implementation.
- The mock player is operational but still launches some sections through fixed content identifiers.
- Some diagnostic and practice flows still rely on fixed IDs instead of fully dynamic content selection.

### Transitional signals

- Frontend code still contains transitional mock-data language.
- Several screens show character-encoding artifacts in UI copy.

### Areas kept explicit instead of overclaimed

- The documentation confirms billing and notification governance surfaces exist.
- It does not overstate the exact external-provider behavior where the audited evidence was not exhaustive.

## 11. Conclusion

The OET Prep Platform is a multi-surface preparation and operations system designed around the actual structure of OET preparation. Its strongest implemented themes are:

- OET-specific workflow design rather than generic learning content
- clear separation between objective and productive skill handling
- strong Writing and Speaking review paths
- admin-operated content, evaluation, review, and governance surfaces
- readiness and planning built from accumulated evidence

The remaining weaknesses are not in product direction, but in a few transitional implementation areas where the platform has not yet fully removed fixed IDs, mock-data remnants, or unavailable mode fallbacks.

