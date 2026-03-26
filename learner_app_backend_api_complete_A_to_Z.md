
# Learner App Backend + API Supplement — Explicit Blueprint Requirements + Inherited Global Requirements + Under-Specified Decisions

## What this file is
This file is the **backend/API handoff supplement for the Learner App only**.

It is written to sit beside the original **OET Platform — Frontend Developer Handoff** and make backend/API work implementation-ready without silently inventing product behavior.

This file covers three categories for the learner app backend/API:

1. **Explicitly defined in the blueprint**
2. **Globally defined elsewhere in the blueprint**
3. **Not specified enough yet**

It also provides **recommended backend/API defaults** to unblock implementation where the blueprint intentionally stops at product/UX level and does not fully define the server contract.

---

## Read this before coding

### Source-of-truth order
1. **Original blueprint**
2. **This backend/API supplement**
3. **Approved product / design / architecture decisions made after this supplement**

### Strict implementation rule
The backend developer must implement the Learner App backend/API to support the **current platform design system, current UI primitives, and current domain components**.  
The backend must **not invent a new product model, a new UI language, ad-hoc screen semantics, or screen-specific payload shapes** that force the frontend to create new bespoke widgets.

That means:
- support the **existing AppShell / route structure**
- support the **existing domain components**
- support the **existing learner workflows**
- return **stable structured data**, not random view-specific blobs
- do **not** force the frontend to invent missing semantics from weak API responses

### Why this matters
The original blueprint is strong on:
- learner workflows
- surface IA
- route map
- UI states
- domain entities
- trust / product principles

But it is intentionally lighter on:
- canonical resource boundaries
- exact request/response schemas
- background job contracts
- state transition rules
- versioning / idempotency / conflict handling
- catalog filtering defaults
- scoring/readiness calculation exposure
- billing / entitlement server rules

This file closes that backend/API gap as far as possible **without pretending that undefined product decisions are already finalized**.

---

## How to use this file
For each learner feature below, read the sections in this order:

1. **Explicitly defined in the blueprint**  
   These are direct requirements already present in the blueprint.

2. **Globally defined elsewhere in the blueprint**  
   These are project-wide or learner-wide constraints that still apply to the feature even when not repeated in its screen section.

3. **Not specified enough yet**  
   These are real gaps. Do **not** quietly invent final product behavior without alignment.

4. **Recommended backend/API implementation default**  
   These are safe defaults to let engineering start and finish the backend/API while still leaving room for later product refinement.

---

## Core implementation rules

### 1) This file is a supplement, not a replacement
Where the original blueprint is explicit, that blueprint remains the source of truth.

### 2) Do not invent a different product
The backend must reflect the same product described in the blueprint:
- OET-native
- criterion-first
- practice-first
- trust-first
- time-poor-user friendly
- professional in tone

### 3) Do not invent new UI semantics in the API
The backend should provide **domain data** and **stateful workflow support**, not presentation hacks.

Examples:
- return criterion objects, not hard-coded card copy
- return review eligibility and reason codes, not only `true/false`
- return async status and retry hints, not only a spinner assumption
- return transcript markers with timing/anchor data, not only decorated HTML

### 4) Support the current design system and current domain components
The backend should be shaped to support existing components already defined in the blueprint, including but not limited to:
- ProfessionSelector
- SubtestSwitcher
- CriterionBreakdownCard
- ReadinessMeter
- WeakestLinkCard
- StudyPlanItem
- WritingCaseNotesPanel
- WritingEditor
- WritingIssueList
- RevisionDiffViewer
- SpeakingRoleCard
- MicCheckPanel
- TranscriptFlagList
- BetterPhraseCard
- MockReportSummary
- ReviewRequestDrawer

### 5) Do not hide open decisions
Where the blueprint is silent, mark the decision and implement a safe default.  
Do not treat guessed behavior as if it were already approved product truth.

---

## Scope of this file

### In scope
Learner App backend/API for:
- onboarding
- goals
- diagnostic
- dashboard
- study plan
- Writing
- Speaking
- Reading
- Listening
- mocks
- readiness
- progress
- submission history
- billing
- settings

### Out of scope
- Expert Console backend/API details
- Admin/CMS backend/API details
- Public marketing website
- Blog / SEO pages
- External landing pages
- Non-OET exams

---

## Backend/API implications of the product principles

### OET-native, not generic ESL
Backend models must preserve:
- profession specificity
- sub-test separation
- Writing and Speaking profession-awareness
- criterion mapping
- readiness by sub-test
- task content metadata that reflects OET workflows rather than generic course content

### Criterion-first
Backend output for evaluations and feedback must preserve criterion structure.  
Do not collapse feedback into unstructured summary text when the blueprint expects criterion-based views.

### Practice-first
The API should support:
- fast task discovery
- resumable attempts
- revision
- mock entry
- review request
- actionable dashboard cards
- study-plan direct actions

### Trust-first
The API must:
- represent scores as estimated ranges where appropriate
- support confidence labels/bands
- clearly distinguish AI estimate vs human review
- avoid returning product language that feels official when it is not official

### Time-poor user UX
The API must support:
- dashboard aggregation
- study-plan rationale per item
- resumable progress
- low-latency reads for task entry
- strong state restoration after interruptions

### Professional tone
Return structured educational metadata and explanation fields.  
Avoid making the frontend infer clinical/educational meaning from generic LMS payloads.

---


## Explicitly defined in the blueprint that directly impacts backend/API

### Learner capabilities
The learner must be able to:
- complete onboarding
- set goals and exam date
- take diagnostics
- use study plan
- do Writing, Speaking, Reading, Listening tasks
- take mocks
- review feedback
- request expert reviews
- view readiness and progress
- manage billing and settings

Backend implication: these are not optional feature flags for v1 of the full learner scope unless the release slice explicitly defers them.

### Learner routes that backend must support
The blueprint defines these learner routes:
- `/app/dashboard`
- `/app/onboarding`
- `/app/goals`
- `/app/diagnostic`
- `/app/diagnostic/results`
- `/app/study-plan`
- `/app/writing`
- `/app/writing/tasks`
- `/app/writing/tasks/:id`
- `/app/writing/attempt/:attemptId`
- `/app/writing/result/:evaluationId`
- `/app/writing/revision/:attemptId`
- `/app/writing/model-answer/:contentId`
- `/app/speaking`
- `/app/speaking/tasks`
- `/app/speaking/mic-check`
- `/app/speaking/task/:id`
- `/app/speaking/attempt/:attemptId`
- `/app/speaking/result/:evaluationId`
- `/app/speaking/review/:attemptId`
- `/app/reading`
- `/app/reading/task/:id`
- `/app/listening`
- `/app/listening/task/:id`
- `/app/mocks`
- `/app/mocks/:id`
- `/app/readiness`
- `/app/progress`
- `/app/reviews`
- `/app/history`
- `/app/billing`
- `/app/settings`

Backend implication: route coverage must map to stable resource coverage; do not create server gaps that force mock data for real screens.

### Required API-driven entities
The blueprint explicitly says the frontend should strongly type these entities:
- user
- learnerGoal
- profession
- subtest
- criterion
- contentItem
- attempt
- evaluation
- criterionScore
- feedbackItem
- readinessSnapshot
- studyPlan
- reviewRequest
- subscription
- wallet/credits

Backend implication: all of these need canonical server-side contracts and lifecycle definitions.

### Async workflows that require backend support
The blueprint explicitly identifies these async flows:
- speaking transcription
- speaking evaluation
- writing evaluation
- human review completion
- study plan regeneration
- report generation

Required UI states:
- queued
- processing
- completed
- failed with retry guidance

Backend implication: every one of these needs explicit status enums, polling/subscription support, retry policy, failure reason handling, and timestamps.

### General integration rules from the blueprint
- all core pages must tolerate partial data
- optimistic updates only where low risk
- explicit loading and stale states for evaluations, study plans, and reviews
- polling or subscription support for async evaluation states

Backend implication:
- endpoints should be composable and partially successful where possible
- composite reads should not fail hard because one child card failed
- `stale_at` / `computed_at` / `last_updated_at` style metadata is recommended

### Validation / empty-state / error requirements
The blueprint requires:
- immediate but calm validation
- every empty state should guide action
- retry
- save locally where possible
- preserve user work in long tasks

Critical error states:
- writing draft save failure
- speaking audio upload failure
- evaluation timeout
- review purchase or entitlement mismatch

Backend implication:
- use structured validation errors with field-level metadata
- return specific reason codes for critical blockers
- support resumable or idempotent write flows

### Analytics instrumentation explicitly required
Track at minimum:
- onboarding started/completed
- goals saved
- diagnostic started/completed
- task started/submitted
- evaluation viewed
- revision started/submitted
- mock started/completed
- review requested
- readiness viewed
- plan item completed/skipped/rescheduled
- subscription started/changed

Include where relevant:
- user id
- profession
- sub-test
- content id
- attempt id
- evaluation id
- mode
- device type
- timestamp

Backend implication:
- emit these events server-side where authoritative
- do not rely only on client analytics for critical lifecycle events

### Security and privacy requirements
The blueprint explicitly requires:
- role-based route protection
- avoid exposing hidden admin/expert routes in learner bundles where practical
- signed upload flows for audio
- no sensitive scoring config exposed client-side
- secure handling of tokens and session refresh
- explicit consent messaging for audio capture where required

Backend implication:
- strong RBAC/ABAC at the API layer
- signed media upload/download strategy
- segregated learner-safe evaluation payloads vs internal scoring configuration

### QA critical path that backend must support
Learner critical path:
- onboarding works
- diagnostic works end to end
- writing draft saves and restores
- speaking recording uploads and evaluates
- study plan updates after evaluated task
- review request flow works
- billing flow works

Backend implication: these are release-blocking backend flows.

### Release slicing with backend impact
The blueprint suggests:
- Slice 1: app shell, auth, onboarding/goals, dashboard skeleton, study plan read-only, taxonomy integration
- Slice 2: Writing library/player/result/revision/expert review request
- Slice 3: Speaking home/mic check/task flow/transcript review/better phrasing
- Slice 4: Reading/Listening flows, mock center, readiness center, progress/history
- Slice 5: expert/admin/quality dashboards

Backend implication:
- incremental delivery plan is already implied
- learner backend can be phased, but within each slice the core contracts should be future-safe

### Open decisions already acknowledged in the blueprint that affect backend
The blueprint itself names these early decisions:
1. Rich text editor strategy for Writing
2. Waveform library choice for Speaking review
3. Real-time vs polling for evaluation state changes
4. Dark mode from v1 or later
5. Mobile authoring experience scope for long Writing tasks

Backend implication:
- at minimum #1, #2, #3 materially affect server contract, data format, and event/job orchestration
- do not ignore them during backend design

---


## Globally defined elsewhere in the blueprint that still apply to learner backend/API

### Learner app layout and workflow implications
The frontend uses:
- top app bar with context-aware title/actions
- desktop sidebar + mobile bottom nav
- persistent next recommended action strip on dashboard/study pages
- distraction-free task views where applicable

Backend implication:
- routes/screens need stable route-entry data
- dashboard and study surfaces need recommendation/rationale data
- task resources need enough metadata to hydrate focused task views without extra round-trips

### Visual tone and trust implications
The learner experience must be:
- clinical
- clean
- high-trust
- conservative with status colors
- never alarmist without reason

Backend implication:
- use explicit `confidence_band`, `risk_level`, `review_status`, `evaluation_state` style semantics
- avoid exposing raw experimental internals directly to learner payloads

### Mobile strategy implications
The product is web-first but must work well on mobile:
- responsive navigation
- sticky CTA on task/result screens
- readable transcript and feedback cards
- writing editor optimized for tablet/mobile landscape where possible
- reliable audio upload from mobile networks

Backend implication:
- support resumable/retryable writes
- media upload flows must survive unstable networks
- payload sizes should be sensible; long transcript responses may need chunking/virtualization support

### Accessibility implications that affect backend
Required from first release:
- keyboard navigation for core paths
- visible focus states
- accessible form labels and validation
- high contrast
- screen-reader support
- captions/transcript access where relevant
- scalable typography
- reduced-motion preference support

Backend implication:
- return structured labels/errors and semantic states
- return transcript/caption data in accessible text form
- avoid media-only content without textual alternatives where the product expects transcript access

### Design-system state model implications
Every component must support:
- loading
- empty
- success
- partial data
- error
- permission denied where relevant
- stale data warning where relevant

Backend implication:
- endpoints should expose enough metadata to differentiate these states
- use stable status codes and payload-level status objects
- do not force the frontend to interpret every failure as a generic empty state

### Performance implications
Backend/API must support:
- dashboard interactive within acceptable standards
- immediate-feeling route transitions for local navigation
- low writing editor latency
- transcript view handling long content without jank
- efficient handling of data-heavy areas where needed

Backend implication:
- aggregate dashboard carefully
- paginate long histories/catalogs
- chunk/stream long transcript structures if needed
- minimize hot-path waterfall requests

### State management implications
The blueprint distinguishes:
- server state
- local UI state
- persisted client state

Backend implication:
- be clear what is authoritative on the server vs safely local-only
- support draft recovery and resumability where the blueprint expects it
- support versioning/conflict detection for long-lived writes

### Empty-state guidance implications
Examples in the blueprint:
- no attempts yet → start diagnostic or recommended task
- no reviews → explain expert review value and CTA
- no progress data → complete first evaluated task

Backend implication:
- endpoints should return enough status + recommendation metadata to drive guided empty states without frontend guesswork

### Error design implications
The blueprint says:
- retry
- save locally where possible
- contact support only for true blockers
- preserve user work in long tasks

Backend implication:
- errors need actionability
- provide retryable reason codes, not only opaque error strings
- preserve server drafts/partial attempts where possible

---


## Not specified enough yet at project-wide backend/API level

These are cross-cutting backend decisions the blueprint does **not** fully resolve, but the project cannot safely finish without them.

### 1) Canonical API style
Not specified:
- REST vs RPC vs GraphQL vs hybrid
- naming conventions
- versioning strategy
- idempotency-key policy
- cursor vs offset pagination
- error envelope standard

Recommended default:
- versioned REST JSON API under `/v1`
- cursor pagination for catalogs/history
- standard error envelope with `code`, `message`, `field_errors`, `retryable`, `support_hint`
- require `Idempotency-Key` on purchase/review/attempt-submit endpoints

### 2) Canonical auth/session approach
The blueprint requires secure token/session handling but does not specify:
- cookie session vs token auth
- refresh strategy
- device/session management
- MFA or re-auth for billing-sensitive actions

Recommended default:
- secure HTTP-only session cookies for web if platform allows, otherwise short-lived access + refresh with server rotation
- strict learner role scoping on all learner endpoints

### 3) Canonical entity lifecycle/state machines
The blueprint names entities but does not fully define state machines for:
- attempt
- evaluation
- review request
- study plan regeneration
- report generation
- subscription/credits

Recommended default:
- document explicit state machines in code and in API docs before implementation starts

### 4) Content/versioning rules
The blueprint references content items, tasks, revisions, model answers, rubrics, and AI configs, but learner-safe version exposure is not fully specified.

Recommended default:
- every learner-facing content object should expose a stable `content_id`, a published revision ID/version, and learner-safe effective metadata
- never expose internal draft/admin-only revision objects to learner APIs

### 5) Evaluation explainability boundary
Not specified:
- how much raw scoring logic is exposed to learners
- whether model version is visible to learners
- whether confidence computation is visible

Recommended default:
- expose learner-safe explanation and confidence band only
- keep raw model thresholds/config internal

### 6) Async orchestration model
The blueprint says polling or subscription support but does not finalize:
- queue tech
- event bus
- websocket/SSE policy
- retry/backoff behavior
- cancellation semantics

Recommended default:
- queue-backed async jobs
- polling first for learner flows, with optional SSE later
- explicit job resources/status embedded in attempt/report resources

### 7) Media storage and lifecycle
Not specified:
- storage provider
- signed URL expiry policy
- retention policy
- reprocessing rules
- transcript regeneration rules

Recommended default:
- signed upload + signed download/stream URLs
- retention and redaction policy documented before launch
- immutable original media plus derived artifacts (transcript, waveform peaks, analysis markers)

### 8) Billing/credits/entitlements model
Not specified:
- exact plan types
- credit ledger semantics
- invoice schema
- refund/cancellation handling
- entitlement checks for review products and extras

Recommended default:
- keep a dedicated entitlement layer separate from UI catalog rendering
- expose learner-safe summary endpoints only; payment processor detail stays server-side

### 9) Recommendation engines
Not specified:
- how next task, next mock, drills, and study intensity are computed
- how much can be rule-based vs ML-based

Recommended default:
- start with transparent rules-based recommendations with explanation strings
- store recommendation provenance for debugging

### 10) Progress/readiness computation cadence
Not specified:
- real-time vs nightly batch vs on-demand
- snapshot versioning
- invalidation rules after new evaluation/review

Recommended default:
- compute on material updates and cache snapshots
- expose `computed_at` and `snapshot_version`

### 11) Comparison and lineage model
Not specified:
- how revisions, retries, retakes, and comparisons relate across attempts
- whether diagnostic attempts coexist with normal attempts in shared history

Recommended default:
- model lineage explicitly: `parent_attempt_id`, `source_context`, `comparison_group_id`

### 12) Search/filter taxonomies
The blueprint names many filters but does not define:
- exact enum values
- sort orders
- default filter behavior
- taxonomy ownership (CMS vs code)

Recommended default:
- taxonomy and filter options come from the backend/CMS, not hard-coded in the frontend

---


## Recommended backend/API architecture default

### Service / module breakdown for the Learner App
A practical modular breakdown:

1. **Identity / Auth / Session**
   - current learner profile
   - role enforcement
   - session refresh / device context

2. **Taxonomy / Reference Data**
   - professions
   - sub-tests
   - criteria
   - scenario types
   - difficulty levels
   - focus area vocabularies
   - turnaround options where learner-facing

3. **Learner Profile / Goals / Settings**
   - onboarding state
   - learner goals
   - profile
   - study preferences
   - accessibility/audio/low-bandwidth preferences
   - notifications/privacy

4. **Content Catalog**
   - writing tasks
   - speaking tasks
   - reading tasks
   - listening tasks
   - model answers
   - drills
   - mock bundles
   - content metadata

5. **Attempt Management**
   - writing attempts
   - speaking attempts
   - reading attempts
   - listening attempts
   - revision lineage
   - answer persistence
   - timer heartbeat
   - draft save/recovery

6. **Media / Upload**
   - signed upload session creation
   - upload completion confirmation
   - media processing state
   - waveform/transcript artifact references

7. **Evaluation / Feedback**
   - writing evaluations
   - speaking evaluations
   - reading results
   - listening results
   - criterion scores
   - anchored comments
   - phrasing suggestions
   - diagnostic results

8. **Study Plan / Recommendation / Readiness**
   - dashboard cards
   - study plan generation
   - plan-item actions
   - readiness snapshots
   - recommendation provenance

9. **Review Requests / Human Review**
   - review eligibility
   - review request create/read/status
   - review deliverables visibility for learner

10. **Mock Orchestration**
    - mock setup
    - mock attempt bundle
    - mock report generation
    - purchased mock reviews

11. **Progress / History**
    - submission history
    - comparison views
    - progress trends
    - usage summaries

12. **Billing / Subscription / Credits**
    - plan summary
    - next renewal
    - invoice list
    - credit wallet/ledger summary
    - review product options
    - upgrade/downgrade/extras entry points

### Recommended persistence concepts
- relational store for canonical entities, entitlements, state machines, and auditable history
- object storage for audio/media and large derived artifacts
- queue/job system for async evaluation, transcription, plan regeneration, report generation
- cache layer for dashboard/readiness/progress composites
- analytics/event pipeline for operational and product events

---


## Recommended standard API conventions

### Resource versioning
Use `/v1/...` from day one.

### Standard response metadata
Recommended fields where relevant:
- `id`
- `state`
- `created_at`
- `updated_at`
- `computed_at`
- `stale_at`
- `version`
- `links`
- `actions`
- `warnings`
- `errors`

### Standard async status model
Use normalized states:
- `queued`
- `processing`
- `completed`
- `failed`

Recommended companion fields:
- `status_reason_code`
- `status_message`
- `retryable`
- `retry_after_ms`
- `last_transition_at`

### Standard write safety
Use:
- idempotency keys for create/submit/purchase/review request actions
- optimistic concurrency version fields or ETags for long-lived drafts
- resumable upload/session IDs for audio

### Standard validation envelope
Recommended structure:
- `code`
- `message`
- `field_errors[]`
  - `field`
  - `reason_code`
  - `message`
- `retryable`
- `support_hint`

### Standard pagination
Use cursor pagination for:
- task libraries
- submission history
- invoices
- reviews list if learner-facing
- attempts history lists

### Standard filtering
Backend-owned filter metadata should be returned from the API for:
- profession
- difficulty
- criteria
- mode
- scenario type
- review status
- mock type

### Standard permissions
Return permission/eligibility info explicitly where relevant:
- `can_request_review`
- `can_start_task`
- `can_resume_attempt`
- `can_view_transcript`
- `can_purchase_extras`
- `eligibility_reason_codes`

### Standard learner-safe explainability
For learner-facing evaluations:
- ranges instead of falsely precise certainty when blueprint expects estimates
- confidence band enum + learner-safe explanation
- criterion-based structure for Writing/Speaking
- evidence list for readiness where required

### Standard auditability
Every important state change should be auditable:
- attempt created/submitted
- evaluation queued/completed/failed
- review requested/completed
- plan regenerated
- billing changed
- settings changed

---


## Canonical learner backend entities and minimum backend fields

This section is not a rigid schema, but it is the minimum server-side modeling depth needed to support the blueprint.

### User
Recommended minimum fields:
- `user_id`
- `role`
- `display_name`
- `email`
- `timezone`
- `locale`
- `created_at`
- `last_active_at`
- `current_plan_id`
- `active_profession_id` if applicable

### LearnerGoal
Recommended minimum fields:
- `goal_id`
- `user_id`
- `profession_id`
- `target_exam_date`
- `overall_goal` optional
- `target_scores_by_subtest`
- `previous_attempt_summary`
- `weak_subtest_self_report`
- `study_hours_per_week`
- `target_country`
- `target_organization`
- `draft_state`
- `submitted_at`
- `updated_at`

### Profession
- `profession_id`
- `code`
- `label`
- `status`
- `sort_order`

### Subtest
- `subtest_id`
- `code` (`writing`, `speaking`, `reading`, `listening`)
- `label`
- `supports_profession_specific_content`

### Criterion
Recommended minimum:
- `criterion_id`
- `subtest`
- `code`
- `label`
- `description`
- `sort_order`

### ContentItem
Recommended minimum:
- `content_id`
- `content_type`
- `subtest`
- `profession_id` nullable where not profession-specific
- `title`
- `difficulty`
- `estimated_duration_minutes`
- `criteria_focus[]`
- `scenario_type`
- `mode_support[]`
- `published_revision_id`
- `status`

### Attempt
Recommended minimum:
- `attempt_id`
- `user_id`
- `content_id`
- `subtest`
- `context` (`diagnostic`, `practice`, `mock`, `revision`, etc.)
- `mode`
- `state`
- `started_at`
- `submitted_at`
- `completed_at`
- `elapsed_seconds`
- `draft_version`
- `parent_attempt_id` nullable
- `comparison_group_id` nullable
- `device_type`
- `last_client_sync_at`

### Evaluation
Recommended minimum:
- `evaluation_id`
- `attempt_id`
- `subtest`
- `state`
- `score_range`
- `grade_range` where applicable
- `confidence_band`
- `strengths[]`
- `issues[]`
- `criterion_scores[]`
- `generated_at`
- `model_explanation_safe`
- `learner_disclaimer`

### CriterionScore
Recommended minimum:
- `criterion_code`
- `score_range` or learner-safe band
- `confidence_band`
- `explanation`

### FeedbackItem
Recommended minimum:
- `feedback_item_id`
- `evaluation_id`
- `criterion_code`
- `type`
- `anchor`
- `message`
- `severity`
- `suggested_fix`

### ReadinessSnapshot
Recommended minimum:
- `snapshot_id`
- `user_id`
- `computed_at`
- `readiness_by_subtest`
- `weakest_link`
- `target_date_risk`
- `recommended_study_remaining`
- `evidence[]`
- `blockers[]`
- `version`

### StudyPlan
Recommended minimum:
- `plan_id`
- `user_id`
- `version`
- `generated_at`
- `state`
- `items[]`
- `checkpoint`
- `weak_skill_focus`
- `retake_rescue_mode` nullable

### ReviewRequest
Recommended minimum:
- `review_request_id`
- `attempt_id`
- `subtest`
- `state`
- `turnaround_option`
- `focus_areas[]`
- `learner_notes`
- `payment_source`
- `price_snapshot`
- `created_at`
- `completed_at`
- `eligibility_snapshot`

### Subscription
Recommended minimum:
- `subscription_id`
- `plan_id`
- `status`
- `next_renewal_at`
- `started_at`
- `changed_at`

### Wallet / Credits
Recommended minimum:
- `wallet_id`
- `user_id`
- `credit_balance`
- `ledger_summary`
- `last_updated_at`

---


## Suggested endpoint inventory for the Learner App backend/API

This is a **recommended** endpoint map to make the learner app buildable end-to-end.  
It is not a claim that the blueprint explicitly defined these exact routes.

### Identity / profile / bootstrap
- `GET /v1/me`
- `GET /v1/me/bootstrap`
- `GET /v1/reference/professions`
- `GET /v1/reference/subtests`
- `GET /v1/reference/criteria`
- `GET /v1/reference/filters/{surface}`

### Onboarding / goals / settings
- `GET /v1/learner/onboarding/state`
- `POST /v1/learner/onboarding/start`
- `POST /v1/learner/onboarding/complete`
- `GET /v1/learner/goals`
- `PATCH /v1/learner/goals`
- `POST /v1/learner/goals/submit`
- `GET /v1/settings`
- `PATCH /v1/settings/profile`
- `PATCH /v1/settings/goals`
- `PATCH /v1/settings/notifications`
- `PATCH /v1/settings/privacy`
- `PATCH /v1/settings/accessibility`
- `PATCH /v1/settings/audio`
- `PATCH /v1/settings/study`

### Diagnostic
- `GET /v1/diagnostic/overview`
- `POST /v1/diagnostic/attempts`
- `GET /v1/diagnostic/attempts/{diagnosticId}`
- `GET /v1/diagnostic/attempts/{diagnosticId}/hub`
- `GET /v1/diagnostic/attempts/{diagnosticId}/results`

### Dashboard / study plan / readiness
- `GET /v1/learner/dashboard`
- `GET /v1/study-plan`
- `POST /v1/study-plan/regenerate`
- `POST /v1/study-plan/items/{itemId}/complete`
- `POST /v1/study-plan/items/{itemId}/skip`
- `POST /v1/study-plan/items/{itemId}/reschedule`
- `POST /v1/study-plan/items/{itemId}/swap`
- `GET /v1/readiness`
- `GET /v1/progress`
- `GET /v1/submissions`
- `GET /v1/submissions/compare`

### Writing
- `GET /v1/writing/home`
- `GET /v1/writing/tasks`
- `GET /v1/writing/tasks/{contentId}`
- `POST /v1/writing/attempts`
- `GET /v1/writing/attempts/{attemptId}`
- `PATCH /v1/writing/attempts/{attemptId}/draft`
- `PATCH /v1/writing/attempts/{attemptId}/heartbeat`
- `POST /v1/writing/attempts/{attemptId}/submit`
- `GET /v1/writing/evaluations/{evaluationId}/summary`
- `GET /v1/writing/evaluations/{evaluationId}/feedback`
- `GET /v1/writing/revisions/{attemptId}`
- `POST /v1/writing/revisions/{attemptId}/submit`
- `GET /v1/writing/content/{contentId}/model-answer`

### Speaking
- `GET /v1/speaking/home`
- `GET /v1/speaking/tasks`
- `GET /v1/speaking/tasks/{contentId}`
- `POST /v1/speaking/attempts`
- `GET /v1/speaking/attempts/{attemptId}`
- `POST /v1/speaking/attempts/{attemptId}/audio/upload-session`
- `POST /v1/speaking/attempts/{attemptId}/audio/complete`
- `PATCH /v1/speaking/attempts/{attemptId}/heartbeat`
- `POST /v1/speaking/attempts/{attemptId}/submit`
- `GET /v1/speaking/attempts/{attemptId}/processing`
- `GET /v1/speaking/evaluations/{evaluationId}/summary`
- `GET /v1/speaking/evaluations/{evaluationId}/review`
- `POST /v1/speaking/device-checks`

### Reading
- `GET /v1/reading/home`
- `GET /v1/reading/tasks/{contentId}`
- `POST /v1/reading/attempts`
- `GET /v1/reading/attempts/{attemptId}`
- `PATCH /v1/reading/attempts/{attemptId}/answers`
- `PATCH /v1/reading/attempts/{attemptId}/heartbeat`
- `POST /v1/reading/attempts/{attemptId}/submit`
- `GET /v1/reading/evaluations/{evaluationId}`

### Listening
- `GET /v1/listening/home`
- `GET /v1/listening/tasks/{contentId}`
- `POST /v1/listening/attempts`
- `GET /v1/listening/attempts/{attemptId}`
- `PATCH /v1/listening/attempts/{attemptId}/answers`
- `PATCH /v1/listening/attempts/{attemptId}/heartbeat`
- `POST /v1/listening/attempts/{attemptId}/submit`
- `GET /v1/listening/evaluations/{evaluationId}`

### Mocks
- `GET /v1/mocks`
- `GET /v1/mocks/options`
- `POST /v1/mock-attempts`
- `GET /v1/mock-attempts/{mockAttemptId}`
- `POST /v1/mock-attempts/{mockAttemptId}/submit`
- `GET /v1/mock-reports/{reportId}`

### Reviews / billing
- `GET /v1/reviews`
- `GET /v1/reviews/eligibility`
- `POST /v1/reviews/requests`
- `GET /v1/reviews/requests/{reviewRequestId}`
- `GET /v1/billing/summary`
- `GET /v1/billing/invoices`
- `GET /v1/billing/review-options`
- `GET /v1/billing/extras`
- `POST /v1/billing/checkout-sessions`

---


## Recommended canonical state machines

### Attempt state machine
Recommended learner-facing attempt states:
- `not_started`
- `in_progress`
- `paused` if needed
- `submitted`
- `evaluating` for async evaluated flows
- `completed`
- `failed`
- `abandoned` optional internal/admin support state

Notes:
- Writing draft save failure should not move the attempt to failed automatically.
- Speaking audio upload failure should be modeled separately from final attempt failure where possible.
- Reading/Listening may move more directly from `in_progress` → `submitted` → `completed`.

### Evaluation state machine
Recommended states:
- `queued`
- `processing`
- `completed`
- `failed`

Notes:
- `completed` should not imply official score.
- `failed` should return retry guidance only where the product allows retry.

### ReviewRequest state machine
Recommended states:
- `draft` optional if UI drafts requests
- `submitted`
- `awaiting_payment` or `awaiting_credit_confirmation` if needed
- `queued`
- `in_review`
- `completed`
- `failed`
- `cancelled` if business permits

### StudyPlan regeneration state machine
Recommended states:
- `idle`
- `queued`
- `processing`
- `completed`
- `failed`

### MockReport generation state machine
Recommended states:
- `queued`
- `processing`
- `completed`
- `failed`

---
## Recommended cross-resource relationships

### Core learner relationships
- one `user` has one active learner profile context
- one `user` can have one active `learnerGoal` draft/submitted record, plus version history if needed
- one `contentItem` can produce many `attempt`s
- one `attempt` can have zero or one primary `evaluation` per evaluation pipeline run, plus internal rerun history if needed
- one `attempt` can have zero or many `reviewRequest`s over time depending on business rules
- one `attempt` can reference one `parent_attempt_id` for revision or retake lineage
- one `readinessSnapshot` belongs to one `user` at one computation time
- one `studyPlan` belongs to one `user` and should be versioned
- one `subscription` and one `wallet/credits` summary belong to one `user`

### Recommended lineage fields
To avoid backend ambiguity later, use:
- `source_context` (`diagnostic`, `practice`, `mock`, `revision`, `review_followup`)
- `parent_attempt_id`
- `origin_content_id`
- `comparison_group_id`
- `plan_item_id` nullable when an attempt originates from the study plan

---
## Recommended server-side analytics / event emission strategy

The blueprint defines what must be tracked.  
For backend reliability, emit server-side events for authoritative milestones:

### Onboarding / goals
- `onboarding_started`
- `onboarding_completed`
- `goals_saved`

### Attempt lifecycle
- `task_started`
- `task_submitted`
- `writing_draft_saved_failed` where useful operationally
- `speaking_audio_upload_failed`
- `evaluation_completed`
- `evaluation_failed`

### Learning workflow
- `revision_started`
- `revision_submitted`
- `mock_started`
- `mock_completed`
- `review_requested`
- `readiness_viewed`
- `study_plan_item_completed`
- `study_plan_item_skipped`
- `study_plan_item_rescheduled`

### Commercial lifecycle
- `subscription_started`
- `subscription_changed`
- `credits_consumed`
- `credits_purchased`

Recommended event payload minimum:
- `user_id`
- `profession_id`
- `subtest`
- `content_id`
- `attempt_id`
- `evaluation_id`
- `mode`
- `device_type`
- `timestamp`

---
## Recommended backend delivery checklist before handing to frontend

A learner backend/API increment should not be called complete until it has:

- stable typed contracts for all resources touched by the routes in scope
- explicit state enums documented
- idempotent create/submit actions where money or irreversible state is involved
- retryable upload/draft flows for long tasks
- learner-safe evaluation payloads that do not expose hidden scoring config
- test coverage for permission denied, partial data, empty state, queued/processing/failed async flows
- audit/event emission for required analytics milestones
- seeded taxonomy/reference data for professions / subtests / criteria / difficulty / scenario types
- clear eligibility rules for reviews, transcript access, mock options, and billing actions

---

# Feature-by-feature learner backend/API supplement

Each feature below is broken into the same four sections requested:
- **Explicitly defined in the blueprint**
- **Globally defined elsewhere in the blueprint**
- **Not specified enough yet**
- **Recommended backend/API implementation default**

---

## Feature 1: Onboarding Entry

**Primary learner route:** `/app/onboarding`

### Explicitly defined in the blueprint
- Purpose: welcome learner, explain setup value, and set expectation of diagnostic + personalized study path.
- UI defined in blueprint: headline, progress stepper, short explainer cards, CTA to begin.
- Acceptance criteria: learner can start onboarding in one action; step count is clear.

### Globally defined elsewhere in the blueprint
- Must live inside learner AppShell, use responsive navigation, and support keyboard/focus/accessible labels where interactive.
- Empty/error/loading states still apply even if the screen is simple; route must tolerate partial profile/taxonomy data.
- Analytics still apply: onboarding started, onboarding completed.

### Not specified enough yet
- Exact onboarding step count and whether it is static or conditional are not specified.
- Whether onboarding can be skipped entirely, deferred, or resumed from the dashboard is not fully defined.
- Whether explainer cards are CMS-driven or hardcoded is not defined.

### Recommended backend/API implementation default
- Expose `GET /v1/learner/onboarding/state` to return completion status, current step, resumable checkpoint, and whether the user may skip.
- Expose `POST /v1/learner/onboarding/start` and `POST /v1/learner/onboarding/complete` for explicit analytics-safe transitions.
- If cards are configurable, expose them from a content/config endpoint rather than embedding presentation text in code.

---

## Feature 2: Goal Setup

**Primary learner route:** `/app/goals`

### Explicitly defined in the blueprint
- Fields: profession, target exam date, target score by sub-test, previous attempts, weak sub-test self-report, study hours per week, target country/organization optional.
- Validation: profession required; sub-test targets optional but encouraged; exam date cannot be in the past.
- Must save partial progress, allow return later, and feed study-plan generation.
- Edge cases: no exam date yet; user only knows overall goal, not sub-test targets.

### Globally defined elsewhere in the blueprint
- Immediate but calm validation is required; form errors must give clear reason and fix path.
- Persisted client state is allowed, but server remains source of truth for saved goal progress.
- Analytics: goals saved; user/profession/sub-test/timestamp relevant where applicable.

### Not specified enough yet
- Exact input model for target scores is not defined: raw score, grade band, both, or nullable per sub-test.
- Shape of `previous attempts` is not defined: count only, historic dates, prior scores, uploaded evidence, or free text.
- Weak sub-test self-report model is not defined: single choice, multi-select ranking, or confidence-weighted input.
- Study hours per week bounds and granularity are not defined.
- Target country/organization canonical taxonomy is not defined.

### Recommended backend/API implementation default
- Use a versioned learner-goal resource: `GET/PUT /v1/learner/goals`.
- Represent targets per sub-test with nullable fields and an optional overall goal field; do not force all four targets.
- Allow draft save via partial `PATCH /v1/learner/goals` and separate `POST /v1/learner/goals/submit` only if business logic requires a formal completion step.
- Emit an async `study_plan_regeneration_requested` job whenever materially relevant fields change.

---

## Feature 3: Diagnostic Intro

**Primary learner route:** `/app/diagnostic`

### Explicitly defined in the blueprint
- Must show diagnostic components, estimated duration, notice that results are training estimate not official score, and a CTA to start.

### Globally defined elsewhere in the blueprint
- Trust-first rule applies: backend must never label diagnostic results as official.
- The learner app is OET-native: diagnostic metadata must map to the four sub-tests and profession-specific Writing/Speaking.
- Analytics: diagnostic started/completed.

### Not specified enough yet
- How estimated duration is calculated is not specified: static, content-driven, or personalized based on mode/profession/device.
- Whether the learner can choose full diagnostic vs partial diagnostic from intro is not defined.
- Whether previous incomplete diagnostic attempts are resumed automatically is not defined.

### Recommended backend/API implementation default
- Expose `GET /v1/diagnostic/overview` returning subtests, estimated durations, training-estimate disclaimer, resumable attempt status, and eligibility.
- Expose `POST /v1/diagnostic/attempts` to create or resume a diagnostic session bundle.

---

## Feature 4: Diagnostic Hub

**Primary learner route:** `/app/diagnostic`

### Explicitly defined in the blueprint
- Must show four diagnostic cards: Writing, Speaking, Reading, Listening.
- Must show progress indicator and support resume later.

### Globally defined elsewhere in the blueprint
- Partial data tolerance matters: one sub-test may be complete while others are pending or failed.
- Async state model applies because Writing/Speaking evaluation can remain queued/processing after task submission.

### Not specified enough yet
- Progress calculation model is not specified: equal weighting by sub-test, by completion state, or by required components within each sub-test.
- Resume behavior is not specified when multiple attempts exist or when an evaluation is still processing.
- Whether the hub allows retake/reset per sub-test is not defined.

### Recommended backend/API implementation default
- Expose `GET /v1/diagnostic/attempts/{id}/hub` returning per-subtest state: not_started, in_progress, submitted, evaluating, completed, failed.
- Return resumable route hints for each card instead of hard-coding frontend decision logic.

---

## Feature 5: Writing Diagnostic Task

**Primary learner route:** `/app/diagnostic` or dedicated attempt route mapped to writing attempt

### Explicitly defined in the blueprint
- Layout requires case notes, editor, timer/checklist.
- Requirements: auto-save draft, confirm before exit, practice vs timed mode support.

### Globally defined elsewhere in the blueprint
- Writing editor state must preserve unsaved content protection, local recovery snapshot, server draft sync state, and timer state.
- Critical error handling: writing draft save failure must preserve work and offer retry.
- Mobile/tablet expectations still apply even if authoring is desktop-optimized.

### Not specified enough yet
- Whether diagnostic writing uses the same attempt model as normal Writing tasks is not explicitly defined.
- The exact autosave interval, save conflict behavior, and authoritative server-draft merge strategy are not defined.
- What constitutes checklist completion is not defined.

### Recommended backend/API implementation default
- Use the same canonical writing-attempt resource across diagnostic and regular writing flows with a `context=diagnostic` flag.
- Endpoints: `POST /v1/writing/attempts`, `GET /v1/writing/attempts/{attemptId}`, `PATCH /v1/writing/attempts/{attemptId}/draft`, `POST /v1/writing/attempts/{attemptId}/submit`.
- Persist timer state server-side at regular heartbeat intervals so session recovery is possible across devices/browser restarts.

---

## Feature 6: Speaking Diagnostic Task

**Primary learner route:** `/app/diagnostic` or speaking attempt route

### Explicitly defined in the blueprint
- Must support mic permission flow, prep timer, record/upload flow, transcript preview after upload when ready.

### Globally defined elsewhere in the blueprint
- Speaking task state must preserve audio upload state, transcript availability state, and recording interruption state.
- Critical error handling: speaking audio upload failure must preserve learner work and support retry.
- Explicit consent messaging for audio capture is required where relevant.

### Not specified enough yet
- Whether browser-recorded and externally uploaded audio share the same pipeline is not defined.
- Allowed file formats, duration limits, bitrate policy, and resumable upload behavior are not defined.
- Whether transcript preview is partial/streaming or only posted after full transcription completes is not defined.

### Recommended backend/API implementation default
- Use signed upload flow: `POST /v1/speaking/attempts` -> upload session -> storage PUT -> `POST /v1/speaking/attempts/{attemptId}/audio/complete` -> enqueue transcription/evaluation.
- Expose job state separately: `GET /v1/speaking/attempts/{attemptId}/processing` with queued/processing/completed/failed.
- Store capture method metadata (browser record vs upload) for support/debugging.

---

## Feature 7: Reading Diagnostic

**Primary learner route:** `/app/reading/task/:id` or diagnostic-specific reading route

### Explicitly defined in the blueprint
- Requirements: time tracking, answer persistence, fast transition between items.

### Globally defined elsewhere in the blueprint
- Low-risk optimistic updates are acceptable for answer persistence, but authoritative attempt state must survive refresh/network loss.
- Validation/error states still apply if content load fails or answer save fails.

### Not specified enough yet
- Whether answers are saved per item, batched, or only on navigation is not defined.
- Navigation rules in diagnostic mode are not defined beyond fast transitions.
- Whether timing is per task, per section, or per part is not defined.

### Recommended backend/API implementation default
- Use canonical reading-attempt resource with `PATCH /answers` supporting incremental updates and `PATCH /heartbeat` for elapsed-time sync.
- Return navigation metadata (current index, total items, section boundaries if any) from the API rather than hard-coding.

---

## Feature 8: Listening Diagnostic

**Primary learner route:** `/app/listening/task/:id` or diagnostic-specific listening route

### Explicitly defined in the blueprint
- Requirements: stable audio controls, answer persistence, mobile-safe playback behavior.

### Globally defined elsewhere in the blueprint
- Audio stability and safe mobile playback handling are system-level requirements.
- Partial data and retry states still apply to media delivery failures.

### Not specified enough yet
- Whether audio is streamed, progressively downloaded, or pre-signed via CDN is not defined.
- Replay restrictions, scrubbing rules, and browser fallback behavior are not defined.
- Whether transcript access exists during diagnostic is not defined and should default to no unless explicitly allowed.

### Recommended backend/API implementation default
- Serve audio via short-lived signed URLs/CDN references returned from `GET /v1/listening/attempts/{attemptId}`.
- Persist answers independently of playback state to reduce data loss on mobile interruptions.

---

## Feature 9: Diagnostic Results

**Primary learner route:** `/app/diagnostic/results`

### Explicitly defined in the blueprint
- Sections: readiness by sub-test, likely blockers, top weak criteria, recommended intensity, first study week suggestion, upgrade prompt if relevant.
- Acceptance: user sees actionable first plan; results feel specific and personalized; score estimates clearly marked as training estimates.

### Globally defined elsewhere in the blueprint
- Trust-first rule: use ranges/confidence-based guidance, never overconfident official-feeling scores.
- Study plan regeneration is an async flow and must expose queued/processing/completed/failed where applicable.

### Not specified enough yet
- Scoring methodology, confidence label thresholds, and readiness computation are not defined in the blueprint.
- How recommended intensity is derived is not defined.
- Upgrade prompt trigger logic is not defined.

### Recommended backend/API implementation default
- Return results from `GET /v1/diagnostic/attempts/{id}/results` with explicit disclaimer fields, ranges, blockers, weak criteria, and plan recommendation payload.
- Separate raw scoring internals from API output; expose only learner-safe explanation fields.
- If study-plan generation is asynchronous, return a plan status object and optional `next_poll_after_ms`.

---

## Feature 10: Dashboard Home

**Primary learner route:** `/app/dashboard`

### Explicitly defined in the blueprint
- Required cards: readiness snapshot, next exam date, today’s tasks, latest evaluated submission, weak criteria, streak/completion momentum, next mock recommendation, pending expert reviews.
- Primary CTAs: resume study plan, start next task, view latest feedback.
- Acceptance: within 5 seconds user knows what to do next; personalized; useful with low activity via strong empty states.

### Globally defined elsewhere in the blueprint
- Persistent next recommended action pattern applies.
- Strong empty states are mandatory.
- Dashboard must be performant and interactive within acceptable modern app standards.

### Not specified enough yet
- Priority order among cards is not defined.
- How 'today’s tasks' is computed from the study plan is not fully defined.
- Definition of streak/completion momentum is not specified.
- Fallback content for low-activity users is not fully specified.

### Recommended backend/API implementation default
- Provide a composite dashboard endpoint: `GET /v1/learner/dashboard` returning all required cards in one response to reduce waterfall latency.
- Each card should include `state`, `last_updated_at`, and actionable route hints/IDs.
- Allow partial card failure without failing the whole dashboard.

---

## Feature 11: Study Plan

**Primary learner route:** `/app/study-plan`

### Explicitly defined in the blueprint
- Sections: today, this week, next checkpoint, weak-skill focus, retake rescue mode banner if applicable.
- Per task item: title, sub-test, duration, why recommended, due date, status.
- Actions: start now, reschedule, swap, mark complete.
- Acceptance: act without multiple pages; plan updates after completion or skip.

### Globally defined elsewhere in the blueprint
- Study-plan regeneration is async and must support queued/processing/completed/failed.
- Optimistic updates only where low risk; server remains source of truth for scheduling decisions.

### Not specified enough yet
- Study plan generation logic and update triggers are not specified.
- Swap semantics are not defined: swap with same sub-test, same duration, same criterion focus, or any equivalent task.
- Retake rescue mode criteria are not defined.

### Recommended backend/API implementation default
- Canonical resource: `GET /v1/study-plan`, `POST /v1/study-plan/items/{id}/complete`, `POST /v1/study-plan/items/{id}/reschedule`, `POST /v1/study-plan/items/{id}/swap`, `POST /v1/study-plan/regenerate`.
- Return rationale fields per item rather than requiring frontend composition from many sources.
- Track plan versioning so UI can detect stale local plan state.

---

## Feature 12: Writing Home

**Primary learner route:** `/app/writing`

### Explicitly defined in the blueprint
- Sections: recommended Writing task, practice library, criterion drill library, past submissions, expert review credits, full mock entry.
- Filters: profession, difficulty, criteria, mode.

### Globally defined elsewhere in the blueprint
- OET-native and criterion-first rules apply: task metadata must preserve profession and criteria mapping.
- Use current domain components and current design system; backend should return stable data fields that fit them.

### Not specified enough yet
- Filter vocabulary and exact option sets are not fully defined.
- Recommended task algorithm is not defined.
- Whether criterion drill items are separate content items or filtered Writing tasks is not defined.

### Recommended backend/API implementation default
- Expose a hub endpoint or separate endpoints for cards/lists: `GET /v1/writing/home`, `GET /v1/writing/tasks`.
- Return canonical filter metadata from the API to keep frontend and backend aligned.

---

## Feature 13: Writing Task Library

**Primary learner route:** `/app/writing/tasks`

### Explicitly defined in the blueprint
- Card data: title, difficulty, profession, time, criteria focus, scenario type.

### Globally defined elsewhere in the blueprint
- Partial data state support matters for catalog screens.
- Taxonomy integration from release slice 1 still applies.

### Not specified enough yet
- Sorting, pagination, search, and filter defaulting are not specified.
- Scenario type taxonomy is not defined.
- Whether library is personalized or generic is not defined.

### Recommended backend/API implementation default
- Endpoint: `GET /v1/writing/tasks?profession=&difficulty=&criteria=&mode=&cursor=`.
- Use cursor pagination with returned `total_estimate` only if needed; do not force exact counts if expensive.

---

## Feature 14: Writing Player

**Primary learner route:** `/app/writing/attempt/:attemptId` or `/app/writing/tasks/:id` before attempt created

### Explicitly defined in the blueprint
- Layout: case notes panel, response editor, timer, checklist, scratchpad.
- Functional requirements: auto-save every few seconds, visible save status, warning on accidental navigation away, distraction-free mode, font size controls, dark mode if platform-wide.

### Globally defined elsewhere in the blueprint
- Critical save-failure handling and draft recovery are mandatory.
- Performance requirement: low input latency during long essays.
- Security requirement: preserve role-based access; no hidden config exposure client-side.

### Not specified enough yet
- Rich text editor strategy is explicitly listed as an open decision in the blueprint.
- Checklist content, scratchpad persistence policy, and font-size preference persistence are not defined.
- Timer enforcement model in strict/timed modes is not defined.

### Recommended backend/API implementation default
- Model scratchpad as optional separate draft field under the same attempt resource.
- Use ETag/version or monotonic draft revision numbers to prevent stale overwrites.
- Provide `save_state` metadata from the server so the UI can show authoritative 'saved / saving / failed' states.

---

## Feature 15: Writing Result Summary

**Primary learner route:** `/app/writing/result/:evaluationId`

### Explicitly defined in the blueprint
- Show estimated score range, grade range, confidence label, top strengths, top issues, CTAs to detailed feedback, revise, request expert review.

### Globally defined elsewhere in the blueprint
- Estimated results must never overpromise.
- Evaluation viewed analytics event applies.

### Not specified enough yet
- Grade range model is not defined.
- Confidence label vocabulary and thresholds are not defined.
- Strength/issue count and ranking logic are not defined.

### Recommended backend/API implementation default
- Expose `GET /v1/writing/evaluations/{evaluationId}/summary` with separate machine fields (ranges, confidence band enum) and learner-facing explanation strings.
- Include links/IDs for the attempt, full feedback, revision source, and review eligibility.

---

## Feature 16: Writing Detailed Feedback

**Primary learner route:** `/app/writing/result/:evaluationId` or nested feedback view

### Explicitly defined in the blueprint
- Must include all six Writing criteria: Purpose, Content, Conciseness & Clarity, Genre & Style, Organisation & Layout, Language.
- UI blocks: criterion score card, explanation, anchored comments, omissions, unnecessary details, revision suggestions.

### Globally defined elsewhere in the blueprint
- Criterion-first rule is non-negotiable.
- Review comment anchor domain component exists and should shape API anchoring data.

### Not specified enough yet
- Anchoring model is not defined: character offsets, text spans, paragraph IDs, sentence IDs, or hybrid.
- Whether omissions and unnecessary details are also anchored is not defined.
- How criterion scores are scaled and rounded is not defined.

### Recommended backend/API implementation default
- Return structured feedback by criterion with explicit `criterion_code`, `score_range`, `comments`, `anchors`, `omissions`, `unnecessary_details`, `revision_suggestions`.
- Prefer stable text-span anchoring with paragraph + offset fallback rather than raw absolute offsets only.

---

## Feature 17: Writing Revision Mode

**Primary learner route:** `/app/writing/revision/:attemptId`

### Explicitly defined in the blueprint
- Requirements: split view original vs revision, highlighted diffs, criterion delta summary, unresolved issue list.

### Globally defined elsewhere in the blueprint
- Revision started/submitted analytics apply.
- Partial data support matters if original evaluation exists but delta evaluation is pending.

### Not specified enough yet
- Whether revision creates a new attempt, child attempt, or version under the same attempt is not defined.
- Diff granularity and unresolved-issue matching logic are not defined.
- Criterion delta timing is not defined: pre-submit estimated delta or post-submit evaluated delta only.

### Recommended backend/API implementation default
- Model revision as a new attempt with `parent_attempt_id` and lineage fields.
- Expose `GET /v1/writing/revisions/{attemptId}` for original content, revision draft, diff blocks, unresolved issues, and latest delta summary if available.

---

## Feature 18: Model Answer Explainer

**Primary learner route:** `/app/writing/model-answer/:contentId`

### Explicitly defined in the blueprint
- Must show annotated model answer, paragraph-level rationale, include/exclude note logic, criterion mapping, profession-specific language notes; not raw sample text only.

### Globally defined elsewhere in the blueprint
- Content is OET-native and profession-specific.
- Use existing current design system and components; backend should provide structured explanatory content rather than HTML blobs only.

### Not specified enough yet
- Whether model-answer content is versioned alongside tasks is not explicitly defined.
- Whether explanations are static authored content or generated content is not defined.
- Localization/version fallback is not defined.

### Recommended backend/API implementation default
- Treat model answer explainer as CMS-authored versioned content linked to the writing task/content item.
- Endpoint: `GET /v1/writing/content/{contentId}/model-answer` returning structured sections rather than one large rich-text blob.

---

## Feature 19: Writing Expert Review Request

**Primary learner route:** `/app/reviews` or action from writing result/revision

### Explicitly defined in the blueprint
- Inputs: turnaround speed, focus areas, notes for reviewer, credit/payment selector.

### Globally defined elsewhere in the blueprint
- Review request is a strongly typed entity in the blueprint.
- Critical error handling includes review purchase or entitlement mismatch.
- Review requested analytics apply.

### Not specified enough yet
- Turnaround option catalogue, pricing, entitlement rules, and focus area taxonomy are not defined.
- Whether multiple review products exist per submission is not defined.
- Idempotency rules for duplicate request submission are not defined.

### Recommended backend/API implementation default
- Endpoints: `POST /v1/reviews/requests`, `GET /v1/reviews/eligibility?attempt_id=` and `GET /v1/billing/review-options?subtest=writing`.
- Require idempotency key on create-review request to avoid double charging.

---

## Feature 20: Speaking Home

**Primary learner route:** `/app/speaking`

### Explicitly defined in the blueprint
- Sections: recommended role play, common issues to improve, pronunciation drills, empathy/clarification drills, past attempts, expert review credits.

### Globally defined elsewhere in the blueprint
- OET-native and criterion-first rules apply.
- Audio/transcript-related domain components already exist and should be supported by API metadata.

### Not specified enough yet
- How 'common issues to improve' is computed is not defined.
- Drill taxonomy is not defined.
- Recommended role play selection logic is not defined.

### Recommended backend/API implementation default
- Expose `GET /v1/speaking/home` with cards for recommendation, issues, drill groups, attempts, and credits.

---

## Feature 21: Mic and Environment Check

**Primary learner route:** `/app/speaking/mic-check`

### Explicitly defined in the blueprint
- Must verify microphone permission, recording works, playback works, noise warning, and device compatibility warning if needed.

### Globally defined elsewhere in the blueprint
- Consent messaging for audio capture still applies.
- Device/mobile reliability requirements apply strongly here.

### Not specified enough yet
- Whether any server-side checks are required beyond client-side capture tests is not specified.
- Noise-threshold detection model is not defined.
- Which devices/browsers count as 'compatibility warning' cases is not defined.

### Recommended backend/API implementation default
- Primarily client-side flow, but optionally post diagnostics to `POST /v1/speaking/device-checks` for support telemetry and gating.
- Return a server-side compatibility ruleset/config if product wants centralized gating.

---

## Feature 22: Speaking Task Selection

**Primary learner route:** `/app/speaking/tasks`

### Explicitly defined in the blueprint
- Show scenario type, difficulty, profession, criteria focus, duration.

### Globally defined elsewhere in the blueprint
- Taxonomy integration and OET-native profession mapping apply.
- Catalog screens still need loading/empty/error states.

### Not specified enough yet
- Sorting, pagination, search, and personalization are not specified.
- Scenario type taxonomy is not defined.

### Recommended backend/API implementation default
- Endpoint: `GET /v1/speaking/tasks?profession=&difficulty=&criteria=&cursor=` with returned filter metadata.

---

## Feature 23: Role Card Preview

**Primary learner route:** `/app/speaking/task/:id` pre-live state

### Explicitly defined in the blueprint
- Show role card, prep timer, notes area, start task CTA.

### Globally defined elsewhere in the blueprint
- Professional, exam-focused tone applies; API should deliver role card content exactly as authored for OET context.
- Attempt session state should survive refresh/back navigation when reasonable.

### Not specified enough yet
- Notes persistence policy is not defined.
- Prep timer enforcement is not defined: informational vs hard gate.
- Whether starting the task locks the role card/notes snapshot is not defined.

### Recommended backend/API implementation default
- Treat role-card preview as the first state of a speaking attempt; `POST /v1/speaking/attempts` returns role card, prep window, notes draft, and start token.

---

## Feature 24: Live Speaking Task

**Primary learner route:** `/app/speaking/attempt/:attemptId`

### Explicitly defined in the blueprint
- Modes: AI interlocutor, self-practice, exam simulation.
- Requirements: clear live recording state, robust reconnect/retry behavior if supported, visible elapsed time, safe stop/submit controls.

### Globally defined elsewhere in the blueprint
- Async transcription/evaluation state handling applies immediately after submission.
- Audio upload state and interruption state preservation are mandatory.

### Not specified enough yet
- AI interlocutor protocol is not defined: streaming speech, text, turn-based prompts, or prerecorded prompts.
- Reconnect semantics are not defined.
- What happens on partial audio corruption or mid-task disconnect is not fully defined.

### Recommended backend/API implementation default
- Separate live-session state from final attempt asset state. Use session endpoints for control messages and upload endpoints for media.
- Capture granular lifecycle events: started, paused/interrupted, resumed, stopped, submitted, upload_completed.

---

## Feature 25: Speaking Result Summary

**Primary learner route:** `/app/speaking/result/:evaluationId`

### Explicitly defined in the blueprint
- Show estimated score range, confidence label, strengths, top improvement areas, next recommended drill, CTA to transcript review, CTA to request expert review.

### Globally defined elsewhere in the blueprint
- Trust-first and non-official estimate framing apply.
- Evaluation viewed analytics apply.

### Not specified enough yet
- Confidence label vocabulary, improvement area ranking, and drill recommendation logic are not defined.

### Recommended backend/API implementation default
- Expose `GET /v1/speaking/evaluations/{evaluationId}/summary` with recommendation IDs for drill deep-linking.

---

## Feature 26: Transcript + Audio Review

**Primary learner route:** `/app/speaking/review/:attemptId`

### Explicitly defined in the blueprint
- Layout: transcript pane, waveform/audio pane, inline markers.
- Marker types: unclear phrase, long pause, empathy miss, weak explanation, abrupt register, structure miss.

### Globally defined elsewhere in the blueprint
- Waveform viewer and transcript viewer are existing design-system/domain components.
- Transcript readability on small screens is required.

### Not specified enough yet
- Marker anchoring model is not defined: time ranges, token spans, utterance IDs, or all three.
- Whether transcript text is editable/correctable is not defined.
- Waveform library choice is explicitly listed as an early open decision.

### Recommended backend/API implementation default
- Return transcript as utterance/time-block structure plus marker objects containing start/end times, optional token references, severity, and category.

---

## Feature 27: Better Phrasing View

**Primary learner route:** `/app/speaking/review/:attemptId` or dedicated subview

### Explicitly defined in the blueprint
- Per flagged segment show original phrase, issue explanation, stronger alternative, repeat drill prompt.

### Globally defined elsewhere in the blueprint
- Criterion-first and improvement-oriented guidance apply.
- Small-screen readability still matters.

### Not specified enough yet
- Whether stronger alternatives are static, generated, or review-authored is not defined.
- How segments are selected/ranked is not defined.
- Whether repeat drill prompt triggers a new mini-attempt is not defined.

### Recommended backend/API implementation default
- Expose phrasing cards as structured objects under the speaking evaluation resource with stable IDs so learners can revisit them later.

---

## Feature 28: Speaking Expert Review Request

**Primary learner route:** `/app/reviews` or action from speaking result

### Explicitly defined in the blueprint
- Inputs: focus areas, reviewer notes, priority/turnaround, credit/payment selector.

### Globally defined elsewhere in the blueprint
- Expert review handoff must preserve role card, transcript, audio, and AI findings.
- Review purchase/entitlement mismatch error handling applies.

### Not specified enough yet
- Focus area taxonomy and turnaround pricing are not defined.
- Whether the user may request review before transcription/evaluation completes is not defined.

### Recommended backend/API implementation default
- Reuse generic review-request resource with speaking-specific attachment bundle references.
- Server should enforce prerequisite state rules and return clear eligibility reasons.

---

## Feature 29: Reading Home

**Primary learner route:** `/app/reading`

### Explicitly defined in the blueprint
- Sections: Part A/B/C entry points, speed drills, accuracy drills, explanations, mock sets.

### Globally defined elsewhere in the blueprint
- OET-native taxonomy and content metadata still apply.

### Not specified enough yet
- Part taxonomy content model is not defined in the blueprint.
- Whether explanations are separate content items or part of results/resources is not defined.

### Recommended backend/API implementation default
- Expose `GET /v1/reading/home` with grouped entry points and drill collections.

---

## Feature 30: Reading Player

**Primary learner route:** `/app/reading/task/:id`

### Explicitly defined in the blueprint
- Requirements: timer, easy question navigation, answer persistence, practice vs exam mode behaviors.

### Globally defined elsewhere in the blueprint
- Strong state handling, partial data tolerance, and mobile responsiveness apply.
- Task started/submitted analytics apply.

### Not specified enough yet
- Exam-mode restrictions are not defined.
- Question navigation model and answer autosave cadence are not defined.

### Recommended backend/API implementation default
- Model mode-specific rules in the task payload rather than hard-coding in frontend.

---

## Feature 31: Reading Results

**Primary learner route:** `/app/reading/task/:id` results state or dedicated result route

### Explicitly defined in the blueprint
- Show score, item-by-item review, explanation for errors, error-type clustering, recommended next drill.

### Globally defined elsewhere in the blueprint
- Results should remain actionable and high-trust.
- Progress/history analytics depend on storing attempt/evaluation relationships.

### Not specified enough yet
- Score scale and drill recommendation logic are not defined.
- Error-type taxonomy is not defined.

### Recommended backend/API implementation default
- Return item-level review objects and a normalized error taxonomy to support clustering.

---

## Feature 32: Listening Home

**Primary learner route:** `/app/listening`

### Explicitly defined in the blueprint
- Sections: part-based practice, transcript-backed review, distractor drills, mock sets.

### Globally defined elsewhere in the blueprint
- Audio stability and mobile playback constraints influence all listening content delivery.

### Not specified enough yet
- Listening part taxonomy and distractor taxonomy are not defined.

### Recommended backend/API implementation default
- Expose `GET /v1/listening/home` with grouped collections and access-policy hints (e.g., transcript reveal conditions).

---

## Feature 33: Listening Player

**Primary learner route:** `/app/listening/task/:id`

### Explicitly defined in the blueprint
- Requirements: audio stability, answer persistence, practice vs exam behaviors, safe mobile playback handling.

### Globally defined elsewhere in the blueprint
- Media delivery must tolerate unstable mobile networks.
- Task started/submitted analytics apply.

### Not specified enough yet
- Exam-mode playback restrictions are not defined.
- Answer-save cadence and resume semantics after interruption are not defined.

### Recommended backend/API implementation default
- Use resumable attempt state and detached media URLs so answer persistence survives media reloads.

---

## Feature 34: Listening Results

**Primary learner route:** `/app/listening/task/:id` results state or dedicated result route

### Explicitly defined in the blueprint
- Show correctness, transcript reveal when allowed, distractor explanation, recommended next drill.

### Globally defined elsewhere in the blueprint
- Access control matters for transcript reveal; backend must enforce rather than frontend-hide only.

### Not specified enough yet
- Transcript reveal policy is not defined.
- Distractor explanation authoring model is not defined.
- Recommendation logic is not defined.

### Recommended backend/API implementation default
- Return a `transcript_access` object with allowed/denied/deferred reasons.

---

## Feature 35: Mock Center

**Primary learner route:** `/app/mocks`

### Explicitly defined in the blueprint
- Sections: sub-test mocks, full mocks, purchased mock reviews, previous mock reports, recommended next mock.

### Globally defined elsewhere in the blueprint
- Mock flows cross multiple sub-tests and need stable orchestration across attempts/evaluations/reviews.

### Not specified enough yet
- Whether sub-test mocks reuse normal task content or dedicated mock bundles is not defined.
- Purchased mock review product model is not defined.

### Recommended backend/API implementation default
- Expose `GET /v1/mocks` with collections, prior reports, purchased review entitlements, and recommendation payload.

---

## Feature 36: Mock Setup

**Primary learner route:** `/app/mocks/:id` or create flow under `/app/mocks`

### Explicitly defined in the blueprint
- Options: sub-test or full mock, mode, profession for Writing/Speaking, include expert review, strict timer on/off depending on mode.

### Globally defined elsewhere in the blueprint
- Validation must remain calm and mode-aware.
- Review purchase/entitlement and billing links may apply if expert review is included.

### Not specified enough yet
- Mock mode taxonomy is not defined.
- Strict timer semantics are not defined.
- Profession selection behavior for a full mock is not fully defined.

### Recommended backend/API implementation default
- Expose configurable options from the backend so the frontend does not hard-code allowed combinations.

---

## Feature 37: Mock Report

**Primary learner route:** `/app/mocks/:id` report state

### Explicitly defined in the blueprint
- Show overall summary, sub-test breakdown, weakest criterion, comparison to prior mock, study plan update CTA.

### Globally defined elsewhere in the blueprint
- Report generation is async per blueprint and needs queued/processing/completed/failed state support.

### Not specified enough yet
- Comparison logic when no exact prior equivalent exists is not defined.
- Weakest criterion across full mock vs within sub-test is not explicitly defined.

### Recommended backend/API implementation default
- Treat mock report as a separate generated resource: `GET /v1/mock-reports/{reportId}` with generation status.

---

## Feature 38: Readiness Center

**Primary learner route:** `/app/readiness`

### Explicitly defined in the blueprint
- Show readiness by sub-test, weakest-link indicator, target-date risk, recommended study remaining, evidence behind the estimate, key blockers.

### Globally defined elsewhere in the blueprint
- Trust-first and evidence-backed presentation rules apply; backend must expose explainability fields.
- Readiness viewed analytics apply.

### Not specified enough yet
- Readiness algorithm, weakest-link calculation, target-date risk model, and evidence selection are not defined.
- How frequently readiness is recomputed is not defined.

### Recommended backend/API implementation default
- Expose readiness snapshot as a versioned resource with `computed_at`, evidence list, blockers, and explanation strings safe for learner view.

---

## Feature 39: Progress Dashboard

**Primary learner route:** `/app/progress`

### Explicitly defined in the blueprint
- Charts: sub-test trend, criterion trend, completion trend, submission volume, review turnaround/usage if relevant.

### Globally defined elsewhere in the blueprint
- Progress and history depend on consistent attempt/evaluation/review timestamps and immutable event capture.
- Tables/charts must remain performant.

### Not specified enough yet
- Time windows, aggregation intervals, and chart interaction/query parameters are not defined.
- Definition of review turnaround and usage relevance is not specified.

### Recommended backend/API implementation default
- Use analytics-ready summary endpoints instead of forcing frontend to aggregate raw attempts client-side.

---

## Feature 40: Submission History

**Primary learner route:** `/app/history`

### Explicitly defined in the blueprint
- List items: task name, sub-test, attempt date, score estimate, review status.
- Actions: reopen feedback, compare attempts, request review.

### Globally defined elsewhere in the blueprint
- Pagination/filtering may be needed for performance even though the blueprint does not specify the exact UI controls.
- Attempt and evaluation lineage must be stable.

### Not specified enough yet
- How attempts are grouped for comparison is not defined.
- Sorting and filter defaults are not defined.
- Score estimate display rules across unevaluated/evaluating attempts are not fully defined.

### Recommended backend/API implementation default
- Expose `GET /v1/submissions?cursor=&subtest=&status=` plus compare endpoint `GET /v1/submissions/compare?attempt_ids=`.

---

## Feature 41: Billing

**Primary learner route:** `/app/billing`

### Explicitly defined in the blueprint
- Show current plan, next renewal, review credits, invoices, and upgrade/downgrade/purchase extras.

### Globally defined elsewhere in the blueprint
- Billing flow is a learner critical path in QA.
- Security/privacy are important; do not expose sensitive payment configuration client-side.

### Not specified enough yet
- Plan catalogue, invoice detail level, credit ledger model, and extras taxonomy are not defined.
- Whether payment processing is embedded or redirect-based is not defined.

### Recommended backend/API implementation default
- Expose read endpoints for subscription summary, credits, invoice list, and purchasable review extras; keep payment processor details server-side.

---

## Feature 42: Settings

**Primary learner route:** `/app/settings`

### Explicitly defined in the blueprint
- Sections: profile, goals, notifications, privacy, accessibility, low-bandwidth mode, audio preferences, exam date and study preferences.

### Globally defined elsewhere in the blueprint
- Accessibility settings and low-bandwidth/audio preferences directly affect other learner modules.
- Privacy/security rules apply strongly here.

### Not specified enough yet
- Exact schema for notifications, privacy, audio preferences, and low-bandwidth mode is not defined.
- Whether settings are split into one resource or multiple sub-resources is not defined.

### Recommended backend/API implementation default
- Use a settings aggregate with modular sub-resources if needed: `/v1/settings/profile`, `/v1/settings/notifications`, `/v1/settings/accessibility`, etc., while still offering `GET /v1/settings` for initial hydrate.

---

## Final implementation note

This document is intended to let the backend developer start and finish the **Learner App backend/API** without inventing a second product.

Non-negotiable:
- support the **current learner routes**
- support the **current design system and existing domain components**
- keep **AI estimates clearly non-official**
- keep **criterion-first structure** for Writing/Speaking feedback
- preserve **long-task reliability** for writing drafts and speaking uploads
- expose **clear async states** for evaluation, review, plan regeneration, and report generation
- do **not** expose hidden admin/scoring configuration to learner payloads

If product/design later closes an open decision, update the contract and schema documentation explicitly rather than silently changing runtime behavior.