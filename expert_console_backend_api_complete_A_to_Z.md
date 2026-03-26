# Expert Console Backend + API Supplement — Explicit Blueprint Requirements + Inherited Global Requirements + Under-Specified Decisions

## What this file is
This file is the **backend/API handoff supplement for the Expert Console only**.

It is written to sit beside the original **OET Platform — Frontend Developer Handoff** and make expert-console backend/API work implementation-ready without silently inventing product behavior.

This file covers three categories for the Expert Console backend/API:

1. **Explicitly defined in the blueprint**
2. **Globally defined elsewhere in the blueprint**
3. **Not specified enough yet**

It also provides **recommended backend/API defaults** to unblock implementation where the blueprint defines product/UX intent but does not fully define server contracts, workflow state models, or operational rules.

---

## Read this before coding

### Source-of-truth order
1. **Original blueprint**
2. **This backend/API supplement**
3. **Approved product / design / architecture decisions made after this supplement**

### Strict implementation rule
The backend developer must implement the Expert Console backend/API to support the **current platform design system, current UI primitives, and current domain components**.

The backend must **not invent a new product model, a new review workflow language, ad-hoc screen semantics, or screen-specific payload shapes** that force the frontend to build new one-off UI patterns.

That means:
- support the **existing route structure**
- support the **existing review workflows**
- support the **existing expert-facing domain language**
- return **stable structured data**, not random presentation blobs
- do **not** force the frontend to infer core review meaning from weak or ambiguous payloads

### Why this matters
The original blueprint is strong on:
- expert workflow intent
- route map
- operational layouts
- filters / columns for the queue
- workspace composition
- review-speed expectations
- calibration and reviewer operations

But it is intentionally lighter on:
- canonical expert resources
- exact request/response schemas
- review state transition rules
- assignment semantics
- claim/release/reassign behaviors
- SLA calculation rules
- learner-context permissions
- draft-review versioning
- audit trail expectations
- schedule / availability semantics
- metrics formulas and aggregation windows

This file closes that backend/API gap as far as possible **without pretending that undefined operational decisions are already finalized product truth**.

---

## How to use this file
For each Expert Console feature below, read the sections in this order:

1. **Explicitly defined in the blueprint**  
   These are direct requirements already present in the blueprint.

2. **Globally defined elsewhere in the blueprint**  
   These are project-wide or expert-wide constraints that still apply even when not repeated in the screen section.

3. **Not specified enough yet**  
   These are real gaps. Do **not** quietly invent final operational behavior without alignment.

4. **Recommended backend/API implementation default**  
   These are safe defaults to let engineering start and finish the backend/API while leaving room for later product refinement.

---

## Core implementation rules

### 1) This file is a supplement, not a replacement
Where the original blueprint is explicit, that blueprint remains the source of truth.

### 2) Do not invent a different expert product
The backend must reflect the same product described in the blueprint:
- OET-native
- criterion-first
- practice-first
- trust-first
- time-poor-user friendly
- professional in tone

For the Expert Console specifically, that translates to:
- operationally realistic review workflows
- criterion-based reviewer scoring and comments
- careful distinction between AI assistance and human judgment
- dense, efficient read/write flows for reviewers
- reliable handling of large submissions, audio, transcripts, and anchored comments

### 3) Do not invent new UI semantics in the API
The backend should provide **domain data** and **stateful workflow support**, not presentation hacks.

Examples:
- return rubric structures, not hard-coded widget copy
- return queue state and reason codes, not only display strings
- return SLA status with timestamps, not only color names
- return anchored comment metadata, not pre-rendered HTML overlays
- return AI assistance snapshots separately from human review fields

### 4) Support the current design system and current domain components
The backend should be shaped to support existing components already defined in the blueprint, including but not limited to:
- ReviewerRubricPanel
- CriterionBreakdownCard
- ContentMetadataPanel
- VersionHistoryDrawer
- Audio player
- Waveform viewer
- Transcript viewer
- Review comment anchor
- Status badge
- Confidence badge
- Table
- FilterBar
- Modal / Drawer

### 5) Do not hide open decisions
Where the blueprint is silent, mark the decision and implement a safe default.  
Do not treat guessed operational behavior as if it were already approved.

---

## Scope of this file

### In scope
Expert Console backend/API for:
- Review Queue
- Writing Review Workspace
- Speaking Review Workspace
- Assigned Learners
- Calibration Center
- Schedule / Availability
- Performance Metrics

### Cross-surface dependencies that this file must still acknowledge
The Expert Console depends on resources that are also touched by learner/admin flows, including:
- learner submissions and evaluations
- review requests created from learner flows
- AI-generated feedback snapshots
- profession/sub-test/criterion taxonomy
- content metadata
- reviewer assignment and operations policy
- admin-managed thresholds / confidence routing / feature flags

### Out of scope
- Learner App backend/API full detail
- Admin/CMS backend/API full detail
- Public marketing website
- Blog / SEO pages
- External landing pages
- Non-OET exams

---

## Backend/API implications of the product principles for the Expert Console

### OET-native, not generic ESL
Backend models must preserve:
- profession specificity
- sub-test separation
- OET Writing and Speaking criterion mapping
- role-card / case-note context
- medically relevant communication context where part of the task

The Expert Console is not a generic support ticket desk or generic essay-grading dashboard.

### Criterion-first
Backend output for expert review must preserve criterion structure.  
Do not collapse expert work into one free-text blob when the product expects rubric-driven review.

### Practice-first
The Expert Console is still part of the learner-practice system. The backend must support:
- quick triage of pending reviews
- fast workspace hydration
- direct submit/rework flows
- operational visibility on what blocks learner progress

### Trust-first
The backend must:
- preserve separation between AI estimate and human review
- expose confidence bands / routing metadata where relevant
- avoid presenting AI output as final expert truth
- support auditable review changes where the human expert overrides AI suggestions

### Time-poor user UX
For experts, this means the API must support:
- dense queue reads
- split-pane workspace hydration
- draft persistence
- low-latency retrieval of submission bundles
- safe resume of in-progress review work
- keyboard-first flows via stable resource identifiers and mutation endpoints

### Professional tone
Return structured educational and assessment metadata.  
Avoid generic LMS-style or support-tool payloads that ignore professional review context.

---

## Explicitly defined in the blueprint that directly impacts expert backend/API

### Expert capabilities
The expert/reviewer must be able to:
- access assigned review queue
- review Writing submissions
- review Speaking submissions
- use calibration tools
- view learner context needed for review

Backend implication: the Expert Console requires explicit permissioned APIs for queue access, review workspaces, calibration workflows, and learner-context reads.

### Expert routes that backend must support
The blueprint defines these Expert Console routes:
- `/expert/queue`
- `/expert/review/writing/:reviewRequestId`
- `/expert/review/speaking/:reviewRequestId`
- `/expert/learners/:learnerId`
- `/expert/calibration`
- `/expert/metrics`
- `/expert/schedule`

Backend implication: route-level data dependencies must be stable and permission-safe.

### Expert layout rules that impact backend/API
The blueprint states for the Expert Console:
- dense information layout
- split panes for submission vs rubric
- keyboard-first workflows for fast reviewing

Backend implication:
- workspace bundles should be hydration-friendly and avoid excessive waterfall loading
- review mutations should be granular enough to support keyboard-driven save/submit/comment operations
- list reads should be efficient for dense tables and filters

### Review Queue — explicit backend implications
The blueprint explicitly defines queue columns:
- review id
- learner
- profession
- sub-test
- AI confidence
- priority
- SLA due
- assigned reviewer
- status

The blueprint explicitly defines queue filters:
- Writing/Speaking
- profession
- priority
- overdue
- confidence band
- assigned/unassigned

Backend implication:
- queue items must expose all above fields in a filterable index/read model
- filters must be first-class server-side fields, not frontend-derived only

### Writing Review Workspace — explicit backend implications
The blueprint defines workspace layout/data needs:
- case notes
- learner response
- AI draft feedback
- rubric entry panel
- final comment composer
- send/rework controls

The blueprint explicitly requires:
- keyboard shortcuts
- anchored comment support
- save draft review
- SLA visibility

Backend implication:
- writing review bundle must include submission content, AI feedback snapshot, rubric schema, anchored-comment model, draft state, and SLA state
- save-draft and submit/rework mutations must exist

### Speaking Review Workspace — explicit backend implications
The blueprint defines workspace layout/data needs:
- role card
- audio player/waveform
- transcript
- AI flags
- rubric panel
- final response panel

The blueprint explicitly requires:
- timestamp anchoring
- playback speed controls
- side-by-side AI and human notes

Backend implication:
- speaking review bundle must include audio asset refs, transcript segments, flag anchors, rubric schema, draft notes, and timestamp-comment model
- review APIs must support timestamp-based annotations and final submission

### Calibration Center — explicit backend implications
The blueprint defines views:
- benchmark cases
- reviewer alignment scores
- disagreements
- notes/history

Backend implication:
- calibration APIs must expose benchmark cases, reviewer performance against those cases, disagreement artifacts, and history over time

### Required API-driven entities from the blueprint that still affect experts
The blueprint says the frontend should strongly type these entities:
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

Backend implication:
- Expert APIs will at minimum touch `user`, `profession`, `subtest`, `criterion`, `contentItem`, `attempt`, `evaluation`, `criterionScore`, `feedbackItem`, and `reviewRequest`
- Learner context reads may also touch `learnerGoal`, `readinessSnapshot`, and limited study-plan context if approved

### Async workflows explicitly listed in the blueprint that affect experts
The blueprint explicitly calls these flows async:
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

Backend implication:
- Expert APIs must surface async state cleanly and allow the workspace to differentiate pending source artifacts vs ready artifacts

### QA critical path explicitly relevant to expert backend/API
The blueprint explicitly requires that:
- reviewer can filter queue
- reviewer can complete writing review
- reviewer can complete speaking review
- reviewer can save draft without losing comments

Backend implication:
- these are not optional operational niceties; they are release-critical behaviors

### Release slicing explicitly relevant to experts
The blueprint places:
- expert console
- admin/CMS
- quality dashboards

inside **Slice 5**.

Backend implication:
- if the project follows the release slicing, the expert APIs may be shipped after learner surfaces, but their contract should still be designed now to avoid later rework

---

## Globally defined elsewhere in the blueprint that still apply to expert backend/API

### Product mission and frontend mission still apply
Even though the Expert Console is not the learner surface, the product mission still applies:
- trusted, profession-specific preparation
- assessment-grade practice flows
- criterion-based feedback
- AI speed
- human review trust

Backend implication:
- expert APIs must preserve the trust model and not flatten the distinction between machine assistance and human adjudication

### Product principles still apply
The globally defined product principles still affect expert APIs:
- OET-native
- criterion-first
- practice-first
- trust-first
- time-poor user UX
- professional tone

Backend implication:
- criteria, profession, and sub-test metadata cannot be treated as optional decoration
- queue and workspaces must prioritize fast actionability and clarity

### Design system requirements still affect backend/API shape
The blueprint defines shared primitives and domain-specific components.

Backend implication:
- APIs should return canonical, reusable fields that map well to shared components like `Table`, `FilterBar`, `Status badge`, `Confidence badge`, `ReviewerRubricPanel`, `CriterionBreakdownCard`, `VersionHistoryDrawer`
- avoid bespoke payloads per screen that break shared component reuse

### Component state support still applies
Every component must support:
- loading
- empty
- success
- partial data
- error
- permission denied where relevant
- stale data warning where relevant

Backend implication:
- expert APIs must return enough state metadata and failure metadata to support these states
- not every workspace bundle will be fully ready at first render; partial-data contracts must be explicit

### General integration rules still apply
The blueprint says:
- all core pages must tolerate partial data
- optimistic updates only where low risk
- explicit loading and stale states for evaluations, study plans, and reviews
- polling or subscription support for async evaluation states

Backend implication:
- review workspaces must load even when some source artifacts are still processing
- stale AI results or stale assignment state should be explicitly detectable
- review-draft mutations should be optimistic only where safe

### State layers still apply conceptually
The blueprint defines:
- server state: content, attempts, evaluations, plans, reviews
- local UI state: filters, drawers, editors, playback state
- persisted client state: draft recovery, onboarding progress where appropriate

Backend implication:
- server must remain source of truth for reviews, assignments, and workspace artifacts
- client draft buffering can exist, but review draft persistence requires backend support

### Validation rules still apply
The blueprint states:
- input validation should be immediate but calm
- scoring fields in review/admin must validate before submit
- required diagnostic/goal fields must show clear reason and fix path

Backend implication:
- expert rubric and final-review submission endpoints must validate required fields before final submission and return actionable error codes

### Empty-state rules still apply
The blueprint says every empty state should guide action.

Backend implication:
- queue APIs should distinguish truly empty queue vs no results for current filters vs permission-restricted results
- calibration APIs should distinguish no cases assigned vs no benchmark program configured

### Error-state rules still apply
The blueprint requires:
- retry
- save locally where possible
- contact support route only for true blockers
- preserving user work in long tasks

Critical errors explicitly listed project-wide include:
- writing draft save failure
- speaking audio upload failure
- evaluation timeout
- review purchase or entitlement mismatch

Expert backend implication:
- draft-save failure is directly relevant
- evaluation timeout and missing transcript/audio readiness are relevant blockers
- the system must preserve expert draft work and comments where possible

### Analytics instrumentation still applies
The blueprint minimum events include:
- evaluation viewed
- review requested
- subscription started/changed
and other learner events.

For expert backend/API, additional operational events are implied even if not explicitly listed by name.

Backend implication:
- expert mutations should emit events for queue open, workspace viewed, draft saved, review submitted, review reworked, calibration case opened, calibration submitted, schedule changed, and metrics viewed
- each event should include relevant identifiers where applicable

### Performance requirements still apply
The blueprint requires:
- route transitions feel immediate for local navigation
- transcript view handles long content without jank
- tables in expert/admin areas support virtualization or efficient pagination where needed
- waveform/transcript sync is a key performance focus area

Backend implication:
- queue APIs need efficient pagination/filtering
- speaking workspace APIs need performant transcript + waveform metadata contracts
- large writing responses must not force heavyweight recomputation on every draft save

### Security and privacy requirements still apply
The blueprint requires:
- role-based route protection
- avoid exposing hidden admin/expert routes in learner bundles where practical
- signed upload flows for audio
- no sensitive scoring config exposed client-side
- secure handling of tokens and session refresh
- explicit consent messaging for audio capture where required

Backend implication:
- expert endpoints must be strongly role-gated
- expert users should only access permitted review objects and learner context
- admin-only configuration should not leak through expert read models unless intentionally exposed

### Device coverage still matters
The blueprint explicitly tests:
- desktop latest Chrome/Safari/Edge
- tablet for writing and result review
- mobile for learner flows

Expert implication:
- expert console is primarily desktop-first, but server contracts should not assume one device only; schedule/metrics and certain reads may still appear on tablet

---

## Not specified enough yet at project-wide expert backend/API level

### 1) Review status taxonomy is not defined
The blueprint refers to `status`, but does not define the full lifecycle.

Open questions:
- Is there a distinction between `queued`, `assigned`, `claimed`, `in_review`, `draft_saved`, `submitted`, `rework_requested`, `cancelled`, `expired`, `sla_breached`?
- Does `completed` mean expert submitted, QA accepted, or learner delivered?
- Are Writing and Speaking review status models identical?

### 2) Assignment model is not defined
The blueprint lists `assigned reviewer` and `assigned/unassigned` filtering but does not define:
- who can assign reviews
- whether experts can self-claim work
- whether reassignment is allowed
- whether multiple reviewers can collaborate
- whether an item can be both assigned and visible to others

### 3) Queue visibility rules are not defined
Not defined:
- whether experts see only assigned items or also a shared pool
- whether certain experts are limited by profession/sub-test
- whether confidence band affects eligibility
- whether overdue items override assignment visibility

### 4) SLA model is not defined
The blueprint mentions `SLA due` and `SLA visibility`, but does not define:
- source of SLA deadline
- business calendar vs absolute timestamp
- warning thresholds
- escalation behavior
- pause/resume rules
- whether different turnaround products create different SLA policies

### 5) Rubric scoring model is not fully defined
The blueprint expects a rubric panel, but does not define:
- exact score entry scale
- whether reviewers must score every criterion numerically
- whether holistic comments can substitute missing rubric fields
- whether AI-suggested scores prefill reviewer forms
- whether reviewers can partially submit and return later

### 6) Writing anchored-comment model is not defined
The blueprint requires anchored comments, but does not define:
- anchor by character range, token index, paragraph ID, sentence ID, or rendered editor anchor key
- whether comments support threading
- whether comments support severity/type labels
- whether comments are reviewer-private until submit

### 7) Speaking timestamp-comment model is not defined
The blueprint requires timestamp anchoring, but does not define:
- whether markers are single timestamps or start/end ranges
- time unit precision
- whether markers attach to transcript segments, waveform regions, or both
- whether multiple comment types can attach to the same region

### 8) AI assistance contract is not fully defined
The blueprint mentions:
- AI draft feedback
- AI flags
- AI and human notes side-by-side
- AI confidence in queue

Not defined:
- what exact AI artifacts are frozen snapshots vs regenerable
- whether AI assistance can change after the expert opens the workspace
- whether expert edits should overwrite, version, or coexist with AI output
- whether AI confidence is model-level, task-level, or criterion-level

### 9) Learner context boundary is not defined
The blueprint says experts can view learner context needed for review, but does not define:
- exact allowed learner fields
- whether exam date/readiness/study-plan details are visible
- whether billing/credits visibility is allowed
- whether reviewers can see previous attempts or only current submission context
- whether reviewer access differs by review type

### 10) Calibration formula and workflow are not defined
The blueprint lists benchmark cases, alignment scores, disagreements, notes/history, but does not define:
- how alignment is computed
- whether calibration is scored against gold labels
- whether disagreements are pairwise reviewer comparisons or reviewer-vs-gold comparisons
- whether notes/history are personal, shared, or audit records

### 11) Schedule / Availability semantics are not defined
The blueprint includes schedule / availability in IA/routes but does not define:
- whether reviewers set working hours, blackout times, capacity caps, timezone, or auto-accept rules
- whether schedule impacts assignment eligibility
- whether this is a personal settings page or operations-controlled schedule surface

### 12) Performance Metrics formulas are not defined
The blueprint includes performance metrics in IA/routes but does not define:
- exact KPIs
- date-range filters
- how quality, turnaround, productivity, or calibration are scored
- whether metrics are reviewer-only, manager-visible, or both

### 13) Search model is not defined
The blueprint defines queue filters but does not define:
- global search by learner / review id / task title
- transcript/content keyword search
- saved filters
- default queue sort precedence

### 14) Notification model is not defined
Not defined:
- how experts learn about newly assigned reviews
- whether overdue warnings generate notifications
- whether calibration deadlines exist
- whether schedule conflicts produce alerts

### 15) Auditability expectations are not fully defined
The blueprint implies operational seriousness but does not explicitly define:
- immutable review submission snapshots
- edit history for draft notes/comments
- re-open after submit rules
- who can view audit history

### 16) Access-control granularity is not defined
Not defined:
- role variants such as reviewer vs senior reviewer vs QA lead vs manager
- ability to view other experts’ drafts
- ability to reopen completed reviews
- ability to see cross-profession workloads

### 17) Async freshness rules are not defined
The blueprint requires queued/processing/completed/failed states but does not define:
- polling interval
- push/subscription behavior
- timeout thresholds
- when stale AI data requires workspace refresh

### 18) Cross-surface change propagation is not fully defined
Not defined:
- what happens to learner-facing status immediately after expert submit
- whether study plans update synchronously or asynchronously after review completion
- whether readiness snapshots refresh on review completion
- whether admin-config changes affect already-open expert workspaces

---

## Recommended backend/API architecture default

### Service/module boundaries
Use a modular backend with at least the following responsibility groups:

1. **Identity + authorization**
   - expert auth
   - role/permission checks
   - profession/sub-test access policy

2. **Review queue read model**
   - queue item projection
   - server-side filtering
   - sorting/pagination
   - SLA snapshots
   - assignment visibility

3. **Review workflow service**
   - claim/release/assign transitions
   - draft save
   - submit/rework transitions
   - final review state changes

4. **Rubric + comment service**
   - criterion schema loading
   - expert rubric draft persistence
   - anchored/timestamp comment storage
   - final comment storage

5. **Submission artifact service**
   - writing content retrieval
   - speaking audio refs
   - transcript segments
   - AI flags / AI feedback snapshots
   - case notes / role cards / content metadata

6. **Calibration service**
   - benchmark case listing
   - gold/reference answers
   - reviewer submissions
   - alignment scoring
   - disagreement history

7. **Availability / capacity service**
   - reviewer availability
   - capacity rules
   - timezone preferences
   - blackout windows

8. **Metrics / analytics service**
   - reviewer performance snapshots
   - throughput / SLA summaries
   - calibration metrics
   - historical rollups

9. **Audit + event service**
   - immutable submission snapshots
   - draft edit history where needed
   - event emission for operational tracking

### Recommended persistence approach
- relational database for core normalized resources and workflow state
- object storage for audio assets and large transcript artifacts if needed
- event/audit log for immutable workflow history
- denormalized queue projections for performant list views
- optional search index if global expert search becomes large

### Recommended job/event model
Background jobs are recommended for:
- queue projection refresh
- SLA recalculation
- transcript availability refresh
- evaluation-status refresh
- post-review learner-status fanout
- calibration score recomputation
- metrics aggregation
- notification dispatch

---

## Recommended standard API conventions

### Versioning
Use explicit versioning, for example:
- `/v1/expert/...`

### Resource design
Prefer domain resources over screen-specific endpoints.

Good:
- `/v1/expert/reviews/{reviewRequestId}`
- `/v1/expert/queue`
- `/v1/expert/calibration/cases`
- `/v1/expert/availability`

Avoid:
- `/v1/expert/right-panel-data`
- `/v1/expert/screen-state`
- `/v1/expert/random-review-payload`

### Pagination
Queue and large calibration/history endpoints should support:
- cursor pagination preferred
- page size limits
- stable sort order

### Filtering
Server-side filtering should support canonical fields, not free-form frontend assumptions.

### Idempotency
Mutation endpoints that can be retried should support idempotency keys, especially:
- claim / release / assign
- draft save where autosave is used
- final submit
- schedule update if implemented as patch operations

### Concurrency control
Use optimistic concurrency/version fields on review drafts and final submit actions.

At minimum, draft-bearing resources should expose:
- `version`
- `updated_at`
- `updated_by`

### Error model
Return structured errors with:
- machine-readable code
- human-safe message
- retryability where relevant
- field-level errors for rubric validation
- permission codes for access denials

### Partial-data model
Workspace responses should explicitly flag partial readiness, for example:
- transcript pending
- AI feedback stale
- audio unavailable
- case-note artifact missing
- SLA snapshot stale

### Time model
All timestamps should be canonical and timezone-safe.  
Reviewer-visible schedule endpoints should support timezone-aware data.

---

## Canonical expert backend entities and minimum backend fields

### 1) `expertUser`
Minimum fields:
- `id`
- `display_name`
- `email`
- `roles`
- `permissions`
- `specialties` (profession/sub-test if relevant)
- `timezone`
- `is_active`

### 2) `reviewRequest`
Minimum fields:
- `id`
- `learner_id`
- `subtest`
- `profession`
- `attempt_id`
- `evaluation_id` nullable
- `requested_turnaround`
- `priority`
- `status`
- `created_at`
- `sla_due_at`
- `assigned_reviewer_id` nullable
- `assignment_state`
- `ai_confidence_band` nullable
- `source_type` (writing / speaking)

### 3) `reviewQueueItem`
A denormalized queue projection with at least:
- `review_request_id`
- `review_label` or human-readable code
- `learner_summary`
- `profession`
- `subtest`
- `ai_confidence_band`
- `priority`
- `sla_due_at`
- `sla_state`
- `assigned_reviewer_summary`
- `status`
- `created_at`
- `updated_at`

### 4) `reviewAssignment`
Minimum fields:
- `review_request_id`
- `assigned_reviewer_id`
- `assigned_by`
- `assigned_at`
- `claim_state`
- `released_at` nullable
- `reassigned_from` nullable
- `reason_code` nullable

### 5) `reviewDraft`
Minimum fields:
- `review_request_id`
- `reviewer_id`
- `version`
- `state`
- `rubric_entries`
- `anchored_comments`
- `timestamp_comments`
- `final_comment_draft`
- `draft_saved_at`
- `autosave_error_state` nullable

### 6) `rubricEntry`
Minimum fields:
- `criterion_id`
- `criterion_name`
- `score_value` nullable
- `score_band` nullable
- `comment`
- `required`
- `validation_state`

### 7) `anchoredComment`
For Writing at minimum:
- `id`
- `review_request_id`
- `reviewer_id`
- `anchor_type`
- `anchor_start`
- `anchor_end`
- `anchor_text_snapshot`
- `comment_text`
- `comment_type` nullable
- `visibility_state`
- `created_at`
- `updated_at`

### 8) `timestampComment`
For Speaking at minimum:
- `id`
- `review_request_id`
- `reviewer_id`
- `start_ms`
- `end_ms` nullable
- `transcript_segment_id` nullable
- `comment_text`
- `comment_type` nullable
- `created_at`
- `updated_at`

### 9) `aiAssistanceSnapshot`
Minimum fields:
- `source_evaluation_id`
- `generated_at`
- `model_label`
- `confidence_band`
- `criterion_suggestions`
- `flags`
- `summary`
- `is_stale`
- `version`

### 10) `writingReviewBundle`
Hydration bundle including:
- review request summary
- learner summary/context
- content metadata
- case notes
- learner response text
- AI draft feedback snapshot
- rubric schema
- existing draft
- SLA snapshot
- permissions/actions

### 11) `speakingReviewBundle`
Hydration bundle including:
- review request summary
- learner summary/context
- content metadata
- role card
- audio asset refs
- transcript segments
- AI flags
- rubric schema
- existing draft
- SLA snapshot
- permissions/actions

### 12) `learnerReviewContext`
Conservative minimum fields if approved:
- `learner_id`
- `display_name` or anonymized reviewer-safe display form
- `profession`
- `target_exam_date` nullable
- `goal_summary` nullable
- `prior_relevant_attempts` nullable
- `current_review_context_only` boolean

### 13) `calibrationCase`
Minimum fields:
- `id`
- `subtest`
- `profession`
- `benchmark_label`
- `case_artifacts`
- `reference_rubric`
- `reference_notes`
- `difficulty`
- `status`

### 14) `calibrationResult`
Minimum fields:
- `calibration_case_id`
- `reviewer_id`
- `submitted_rubric`
- `alignment_score`
- `disagreement_summary`
- `notes`
- `submitted_at`

### 15) `reviewerAvailability`
Minimum fields:
- `reviewer_id`
- `timezone`
- `working_windows`
- `blackouts`
- `capacity_limits`
- `effective_from`
- `effective_to` nullable

### 16) `reviewerMetricSnapshot`
Minimum fields:
- `reviewer_id`
- `window_start`
- `window_end`
- `completed_reviews`
- `draft_reviews`
- `avg_turnaround`
- `sla_hit_rate`
- `calibration_score`
- `rework_rate`
- `notes` nullable

### 17) `auditEvent`
Minimum fields:
- `id`
- `actor_id`
- `entity_type`
- `entity_id`
- `action`
- `before_snapshot` nullable
- `after_snapshot` nullable
- `created_at`

---

## Suggested endpoint inventory for the Expert Console backend/API

### Queue and assignment
- `GET /v1/expert/queue`
- `GET /v1/expert/queue/filters/metadata`
- `POST /v1/expert/reviews/{reviewRequestId}/claim`
- `POST /v1/expert/reviews/{reviewRequestId}/release`
- `POST /v1/expert/reviews/{reviewRequestId}/assign`
- `POST /v1/expert/reviews/{reviewRequestId}/reassign`

### Review workspace hydration
- `GET /v1/expert/reviews/{reviewRequestId}`
- `GET /v1/expert/reviews/writing/{reviewRequestId}`
- `GET /v1/expert/reviews/speaking/{reviewRequestId}`
- `GET /v1/expert/reviews/{reviewRequestId}/sla`
- `GET /v1/expert/reviews/{reviewRequestId}/history`

### Draft persistence and comments
- `PATCH /v1/expert/reviews/{reviewRequestId}/draft`
- `POST /v1/expert/reviews/{reviewRequestId}/anchored-comments`
- `PATCH /v1/expert/reviews/{reviewRequestId}/anchored-comments/{commentId}`
- `DELETE /v1/expert/reviews/{reviewRequestId}/anchored-comments/{commentId}`
- `POST /v1/expert/reviews/{reviewRequestId}/timestamp-comments`
- `PATCH /v1/expert/reviews/{reviewRequestId}/timestamp-comments/{commentId}`
- `DELETE /v1/expert/reviews/{reviewRequestId}/timestamp-comments/{commentId}`

### Review decisions
- `POST /v1/expert/reviews/{reviewRequestId}/submit`
- `POST /v1/expert/reviews/{reviewRequestId}/rework`
- `POST /v1/expert/reviews/{reviewRequestId}/cancel` (only if business rules allow)

### Learner context
- `GET /v1/expert/learners/{learnerId}`
- `GET /v1/expert/learners/{learnerId}/review-context`
- `GET /v1/expert/learners/{learnerId}/attempts` (permission-scoped)

### Calibration
- `GET /v1/expert/calibration/cases`
- `GET /v1/expert/calibration/cases/{caseId}`
- `POST /v1/expert/calibration/cases/{caseId}/draft`
- `POST /v1/expert/calibration/cases/{caseId}/submit`
- `GET /v1/expert/calibration/history`
- `GET /v1/expert/calibration/alignment`

### Schedule / availability
- `GET /v1/expert/availability`
- `PUT /v1/expert/availability`
- `PATCH /v1/expert/availability`
- `GET /v1/expert/availability/constraints`

### Metrics
- `GET /v1/expert/metrics`
- `GET /v1/expert/metrics/summary`
- `GET /v1/expert/metrics/history`
- `GET /v1/expert/metrics/calibration`

### Optional supporting endpoints
- `GET /v1/expert/me`
- `GET /v1/expert/notifications`
- `POST /v1/expert/notifications/{id}/read`

---

## Recommended canonical state machines

### 1) Review request lifecycle
Recommended default:
- `queued`
- `assigned`
- `claimed`
- `in_review`
- `draft_saved`
- `submitted`
- `rework_requested`
- `completed`
- `cancelled`

Notes:
- `submitted` can mean expert completed action but downstream learner delivery / reporting may still be processing
- `completed` should represent end-to-end operational completion if the system distinguishes that from submit

### 2) Assignment lifecycle
Recommended default:
- `unassigned`
- `assigned`
- `claimed`
- `released`
- `reassigned`

### 3) Workspace artifact readiness lifecycle
Recommended default for AI/transcript/evaluation artifacts:
- `queued`
- `processing`
- `completed`
- `failed`
- `stale`

### 4) Draft lifecycle
Recommended default:
- `empty`
- `editing`
- `saving`
- `saved`
- `conflicted`
- `submit_pending`
- `submitted`

### 5) SLA lifecycle
Recommended default:
- `on_track`
- `at_risk`
- `overdue`
- `completed_on_time`
- `completed_late`

---

## Recommended cross-resource relationships

- one learner can have many review requests
- one review request belongs to one attempt and one sub-test
- one review request can have zero or one current assignment but many assignment-history records
- one review request can have many draft save revisions
- one writing review can have many anchored comments
- one speaking review can have many timestamp comments
- one review request can reference one current AI assistance snapshot and a history of prior versions if needed
- one reviewer can have many calibration results
- one reviewer can have many availability windows
- one reviewer can have many metric snapshots across time windows

---

## Recommended server-side analytics / event emission strategy

### Minimum event families
Emit operational events for at least:
- queue viewed
- queue filters changed
- review claimed
- review released
- review assigned
- writing workspace viewed
- speaking workspace viewed
- review draft saved
- review submit attempted
- review submitted
- review rework requested
- calibration case viewed
- calibration submitted
- availability updated
- metrics viewed

### Event payload recommendations
Where relevant include:
- reviewer id
- learner id
- review request id
- attempt id
- evaluation id
- profession
- sub-test
- priority
- SLA state
- AI confidence band
- device type
- timestamp

### Important boundary
Analytics events should not replace the canonical audit trail.  
Use separate durable audit records for compliance/operations.

---

## Recommended backend delivery checklist before handing to frontend

The expert backend/API should not be considered ready until:
- queue filters work server-side
- queue sorting/pagination are stable
- queue visibility obeys permissions
- Writing workspace loads full required bundle
- Speaking workspace loads full required bundle
- anchored comments persist safely
- timestamp comments persist safely
- draft save supports conflict detection
- final submit validates rubric completeness
- SLA snapshots are available in queue + workspace
- partial artifact readiness is explicit
- calibration endpoints return benchmark/alignment/disagreement/history data
- availability endpoints are timezone-safe
- metrics endpoints are bounded and performant
- audit logging exists for core review mutations
- admin-only config does not leak through expert APIs unintentionally

---

## Feature 1: Review Queue

**Primary expert route:** `/expert/queue`

### Explicitly defined in the blueprint
- Required columns: review id, learner, profession, sub-test, AI confidence, priority, SLA due, assigned reviewer, status.
- Required filters: Writing/Speaking, profession, priority, overdue, confidence band, assigned/unassigned.
- Purpose implied by blueprint: triage and access review work.

### Globally defined elsewhere in the blueprint
- Dense information layout applies; queue reads must support operational efficiency rather than decorative cards.
- Keyboard-first workflows apply; stable row IDs and fast claim/open actions matter.
- Tables in expert/admin areas must support virtualization or efficient pagination where needed.
- Component states apply: loading, empty, partial, error, permission denied, stale.
- QA critical path requires reviewer can filter queue.

### Not specified enough yet
- Default sort order is not defined.
- Whether experts can see only assigned items or a shared pool is not defined.
- Whether row click opens full workspace directly or side preview exists is not defined.
- Claim/unclaim behavior is not defined.
- Whether batch actions exist is not defined.
- Human-readable review code vs raw ID is not defined.

### Recommended backend/API implementation default
- Implement `GET /v1/expert/queue` with cursor pagination and server-side filters for all blueprint fields.
- Default sort order: overdue first, then earliest SLA due, then highest priority, then oldest created.
- Return queue items with explicit `available_actions` based on permissions and assignment state.
- Include `visibility_scope` metadata so frontend can distinguish “assigned to me” vs “shared pool” if product later expands.
- Keep queue row payload compact and move heavy workspace data out of the list endpoint.

---

## Feature 2: Writing Review Workspace

**Primary expert route:** `/expert/review/writing/:reviewRequestId`

### Explicitly defined in the blueprint
- Layout/data needs: case notes, learner response, AI draft feedback, rubric entry panel, final comment composer, send/rework controls.
- Requirements: keyboard shortcuts, anchored comment support, save draft review, SLA visibility.

### Globally defined elsewhere in the blueprint
- Split panes for submission vs rubric apply directly.
- Criterion-first product principle applies; rubric and comments cannot be generic free text only.
- Reviews must tolerate partial data and expose explicit loading/stale states.
- Validation rules for scoring fields apply before submit.
- Error-state rule to preserve user work in long tasks applies directly to review drafts.

### Not specified enough yet
- Exact rubric scoring scale is not defined.
- Exact anchor model is not defined.
- Whether AI feedback is editable, replaceable, or snapshot-only is not defined.
- Whether send vs rework map to learner-visible statuses or internal workflow states is not defined.
- Whether reviewers may reopen their own submitted review is not defined.

### Recommended backend/API implementation default
- Expose `GET /v1/expert/reviews/writing/{reviewRequestId}` returning a single hydration bundle with request summary, learner context, content metadata, case notes, learner response, AI snapshot, rubric schema, existing draft, SLA snapshot, and allowed actions.
- Expose `PATCH /v1/expert/reviews/{reviewRequestId}/draft` for autosave and explicit save with optimistic concurrency `version` checks.
- Use anchor ranges over canonical submission text (start/end offsets plus text snapshot) as the first implementation default.
- Expose `POST /v1/expert/reviews/{reviewRequestId}/submit` and `POST /v1/expert/reviews/{reviewRequestId}/rework` with strict validation and audit logging.

---

## Feature 3: Speaking Review Workspace

**Primary expert route:** `/expert/review/speaking/:reviewRequestId`

### Explicitly defined in the blueprint
- Layout/data needs: role card, audio player/waveform, transcript, AI flags, rubric panel, final response panel.
- Requirements: timestamp anchoring, playback speed controls, side-by-side AI and human notes.

### Globally defined elsewhere in the blueprint
- Waveform/transcript sync is a stated key performance focus area.
- Async artifact states apply because transcript/evaluation may still be processing.
- Criterion-first and trust-first principles apply; AI flags must remain distinct from human review.
- Error-handling and partial-data support are mandatory.
- QA critical path requires reviewer can complete speaking review.

### Not specified enough yet
- Transcript segmentation model is not defined.
- Timestamp-comment precision and range model are not defined.
- How playback-speed options are configured is not defined.
- Whether AI flags can be accepted/rejected individually is not defined.
- Whether human notes can attach to waveform region, transcript segment, or both is not defined.

### Recommended backend/API implementation default
- Expose `GET /v1/expert/reviews/speaking/{reviewRequestId}` with role card, audio asset refs, waveform metadata, transcript segments, AI flags, rubric schema, draft notes/comments, SLA snapshot, and allowed actions.
- Use timestamp comments as `start_ms` + optional `end_ms` with optional transcript-segment reference.
- Return playback capability metadata separately from audio assets so frontend can render allowed speeds without hard-coded assumptions.
- Mark transcript/AI sections with explicit readiness states when upstream processing is incomplete.

---

## Feature 4: Assigned Learners

**Primary expert route:** `/expert/learners/:learnerId`

### Explicitly defined in the blueprint
- This area exists in the Expert Console IA and route map.
- Expert capability explicitly includes viewing learner context needed for review.

### Globally defined elsewhere in the blueprint
- Trust and privacy rules apply; only necessary learner context should be exposed.
- Strong typing of learnerGoal, readinessSnapshot, studyPlan, attempt, evaluation, reviewRequest may affect this screen depending on approved scope.
- Role-based route protection applies.

### Not specified enough yet
- The blueprint does not define the page layout, fields, actions, or limits for assigned learners.
- It is not defined whether this page is a learner summary profile, current-review context hub, history surface, or coaching-style overview.
- It is not defined whether experts may browse any learner or only learners tied to active assignments.

### Recommended backend/API implementation default
- Keep this page conservative in v1: expose reviewer-safe review context rather than a full learner CRM profile.
- Provide `GET /v1/expert/learners/{learnerId}/review-context` returning profession, target exam date if allowed, current assigned reviews, recent relevant attempts, and limited readiness/goal summary if approved.
- Require explicit permission checks that tie access to assigned or historically reviewed work unless product approves broader visibility.

---

## Feature 5: Calibration Center

**Primary expert route:** `/expert/calibration`

### Explicitly defined in the blueprint
- Views: benchmark cases, reviewer alignment scores, disagreements, notes/history.
- Expert capability explicitly includes use of calibration tools.

### Globally defined elsewhere in the blueprint
- Criterion-first and trust-first principles apply strongly here.
- Loading/empty/error/partial states still apply.
- Metrics/performance ideas elsewhere in the blueprint suggest calibration is not cosmetic; it should be operationally meaningful.

### Not specified enough yet
- Whether calibration is mandatory, periodic, scored against gold labels, or comparative only is not defined.
- Alignment formula is not defined.
- Whether notes/history are personal, shared, or manager-visible is not defined.
- Whether calibration affects queue eligibility is not defined.

### Recommended backend/API implementation default
- Expose `GET /v1/expert/calibration/cases` and `GET /v1/expert/calibration/alignment` separately.
- Represent benchmark cases as immutable artifacts with reference rubric/results attached.
- On submit, store reviewer rubric independently from the reference rubric and compute a transparent alignment score plus disagreement summary.
- Preserve calibration history as immutable snapshots.

---

## Feature 6: Schedule / Availability

**Primary expert route:** `/expert/schedule`

### Explicitly defined in the blueprint
- This area exists in the Expert Console IA and route map.

### Globally defined elsewhere in the blueprint
- Time-poor UX and operational realism still apply.
- Role-based access, timezone-safe timestamps, and strong validation remain relevant.
- This surface likely influences assignment operations even though the blueprint does not say exactly how.

### Not specified enough yet
- The blueprint does not define the data model, fields, or interactions.
- It is not defined whether this is personal availability, shift scheduling, capacity settings, blackout management, or all of the above.
- It is not defined whether changes are self-service or admin-approved.

### Recommended backend/API implementation default
- Support a conservative personal-availability model in v1: timezone, recurring working windows, temporary blackout windows, optional daily/weekly capacity.
- Expose `GET /v1/expert/availability` and `PUT/PATCH /v1/expert/availability`.
- Keep assignment-engine integration behind service boundaries so later business rules can evolve without breaking the API.

---

## Feature 7: Performance Metrics

**Primary expert route:** `/expert/metrics`

### Explicitly defined in the blueprint
- This area exists in the Expert Console IA and route map.

### Globally defined elsewhere in the blueprint
- The blueprint includes quality analytics and reviewer-oriented operational seriousness across the product.
- Analytics instrumentation, performance requirements, and QA expectations all suggest metrics must be trustworthy and bounded.

### Not specified enough yet
- Exact KPIs, formulas, and visibility rules are not defined.
- It is not defined whether metrics are self-view only, manager-view, or both.
- It is not defined whether metrics include quality, speed, SLA hit rate, calibration alignment, or productivity counts.
- It is not defined what date windows and comparison periods exist.

### Recommended backend/API implementation default
- Start with reviewer-safe summary metrics: completed reviews, draft reviews, average turnaround, SLA hit rate, calibration score, and rework rate over bounded date windows.
- Expose `GET /v1/expert/metrics/summary` and `GET /v1/expert/metrics/history`.
- Keep formulas documented and versioned so later metric changes do not silently rewrite historical meaning.

---

## Final implementation note
This file is intentionally strict.

It gives the backend developer three separate layers:
- what the blueprint already requires
- what project-wide rules still apply to the Expert Console
- what remains under-specified and must not be faked as final product truth

The backend developer should **build to the current platform design system, current domain components, and current workflow language**.

They should **not invent new expert workflows, new UI semantics, new scoring meaning, or new permission behavior** unless product/design explicitly approves them.

Where this file gives a recommended default, that default is there to unblock implementation safely — not to rewrite the source blueprint.
