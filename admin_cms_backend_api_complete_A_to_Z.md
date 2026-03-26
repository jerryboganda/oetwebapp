# Admin / CMS Backend + API Supplement — Explicit Blueprint Requirements + Inherited Global Requirements + Under-Specified Decisions

## What this file is
This file is the **backend/API handoff supplement for the Admin / CMS surface only**.

It is written to sit beside the original **OET Platform — Frontend Developer Handoff** and make Admin / CMS backend/API work implementation-ready **without silently inventing product behavior**.

This file covers three categories for the Admin / CMS backend/API:

1. **Explicitly defined in the blueprint**
2. **Globally defined elsewhere in the blueprint**
3. **Not specified enough yet**

It also provides **recommended backend/API defaults** to unblock implementation where the blueprint defines product/UX intent but does not fully define:
- canonical admin resources
- request/response schemas
- workflow state models
- permissions and separation of duties
- audit obligations
- approval requirements
- change propagation rules
- cross-system operational contracts

---

## Read this before coding

### Source-of-truth order
1. **Original blueprint**
2. **This backend/API supplement**
3. **Approved product / design / architecture decisions made after this supplement**

### Strict implementation rule
The backend developer must implement the Admin / CMS backend/API to support the **current platform design system, current UI primitives, and current domain components**.

The backend must **not invent a new product model, a new admin workflow language, ad-hoc screen semantics, or screen-specific payload shapes** that force the frontend to build one-off admin UI patterns unrelated to the current system.

That means:
- support the **existing route structure**
- support the **existing admin operational workflows**
- support the **existing learner / expert / admin domain language**
- return **stable structured data**, not random presentation blobs
- do **not** force the frontend to infer core admin meaning from weak or ambiguous payloads
- do **not** force the frontend to invent new visual/state semantics because the API omitted operational state

### Why this matters
The original blueprint is strong on:
- application surfaces and IA
- admin responsibilities
- content/versioning intent
- AI configuration intent
- review operations oversight
- analytics areas
- project-wide validation, state, performance, security, and QA rules

But it is intentionally lighter on:
- exact admin resource boundaries
- edit vs read permissions
- multi-role separation of duties
- workflow state transitions
- publish/version/restore semantics
- reference integrity rules
- audit-event requirements
- staging/approval/promotion flows
- environment-aware config management
- bulk-operation safety rules
- financial/support operations boundaries
- feature-flag hierarchy and rollout semantics

This file closes that backend/API gap as far as possible **without pretending undefined operational behavior is already approved product truth**.

---

## How to use this file
For each Admin / CMS feature below, read the sections in this order:

1. **Explicitly defined in the blueprint**  
   These are direct requirements already present in the blueprint.

2. **Globally defined elsewhere in the blueprint**  
   These are project-wide or admin-wide constraints that still apply even when not repeated in the screen section.

3. **Not specified enough yet**  
   These are real gaps. Do **not** quietly invent final operational behavior without alignment.

4. **Recommended backend/API implementation default**  
   These are safe defaults to let engineering start and finish the backend/API while leaving room for later product refinement.

---

## Core implementation rules

### 1) This file is a supplement, not a replacement
Where the original blueprint is explicit, that blueprint remains the source of truth.

### 2) Do not invent a different admin product
The backend must reflect the same product described in the blueprint:
- OET-native
- criterion-first
- practice-first
- trust-first
- time-poor-user friendly
- professional in tone

For Admin / CMS specifically, that translates to:
- operationally realistic control surfaces
- safe management of production content and scoring-related configuration
- explicit auditability for sensitive changes
- dense, efficient read/write flows for operators
- strong protection against accidental destructive changes
- careful separation between content management, review operations, billing operations, and sensitive model/config management

### 3) Do not invent new UI semantics in the API
The backend should provide **domain data** and **stateful workflow support**, not presentation hacks.

Examples:
- return content lifecycle state and allowed actions, not only badge text
- return revision metadata and diff references, not only “has_revisions: true”
- return usage/impact counts before archive/delete, not only block messages
- return routing-rule objects, not hard-coded UI strings
- return audit events as structured actor/action/resource records, not only a free-text changelog
- return feature-flag scope and rule metadata, not only on/off booleans

### 4) Support the current design system and current domain components
The backend should be shaped to support existing components already defined in the blueprint, including but not limited to:
- ContentMetadataPanel
- VersionHistoryDrawer
- ReviewerRubricPanel
- CriterionBreakdownCard
- ProfessionSelector
- SubtestSwitcher
- Table
- FilterBar
- Modal / Drawer
- Status badge
- Confidence badge
- Task card
- Submission card

The backend must **not** force the frontend to invent unrelated admin-specific widgets because core data is malformed, missing, or overly presentation-specific.

### 5) Do not hide open decisions
Where the blueprint is silent, mark the decision and implement a safe default.  
Do not treat guessed operational behavior as if it were already approved.

### 6) Protect historical meaning
Admin changes must not silently rewrite historical meaning unless product explicitly approves reprocessing or retroactive reinterpretation.

This applies especially to:
- criteria mappings
- rubric definitions
- AI thresholds
- confidence routing rules
- feature flags affecting runtime behavior
- content versions tied to learner attempts and expert reviews

### 7) Prefer explicit impact modeling
Before high-risk changes, the backend should expose impact information where possible:
- linked content count
- linked attempts/evaluations count
- active learner usage
- reviewer dependency count
- experiment/flag dependency count
- environment reach

---

## Scope of this file

### In scope
Admin / CMS backend/API for:
- Content Library
- Task Builder
- Content Revisions
- Profession Taxonomy
- Rubrics / Criteria Mapping
- AI Evaluation Config
- Review Ops Dashboard
- Quality Analytics
- User Ops
- Billing Ops
- Feature Flags
- Audit Logs

### Also in scope because Admin / CMS is project control surface
Operational backend/API support for:
- content lifecycle management
- metadata governance
- criteria/rubric governance
- AI evaluation governance
- review operations oversight and interventions
- support/admin access to users and subscriptions
- rollout controls and kill switches
- admin-side auditability and traceability
- permission gating and separation of duties

### Out of scope
Unless separately approved, this file does **not** define:
- public marketing website backend
- third-party billing-provider implementation details beyond required app-facing contracts
- secret management UI
- infrastructure/IaC design
- data warehouse implementation specifics
- raw provider credentials exposure in the frontend

---

## Backend/API implications of the product principles for Admin / CMS

### OET-native, not generic ESL
Admin resources must preserve OET-specific structures:
- Writing criteria and task structures
- Speaking criteria and role-card structures
- profession-specific content behavior
- sub-test separation
- assessment-grade content metadata

Backend implication:
- avoid generic “lesson” or “quiz” abstractions as primary API language where they erase OET semantics
- content schemas should distinguish Writing / Speaking / Reading / Listening / Mock artifacts clearly

### Criterion-first
Admin must control the criterion definitions and mappings that downstream learner and expert experiences rely on.

Backend implication:
- criterion objects must be first-class resources
- mappings and rubric versions must be traceable
- evaluation configs should reference explicit rubric/criterion versions where possible

### Practice-first
Admin content and ops must support direct learner activity.

Backend implication:
- content publish/archive/version changes must be safe for live learner workflows
- content availability rules must be explicit
- archive/deprecate semantics must not strand learner routes unexpectedly

### Trust-first
Admin changes affect user trust.

Backend implication:
- high-risk changes need audit logging
- model/config/routing changes need versioning and revertability
- evaluation-sensitive changes should carry change notes and effective timestamps

### Time-poor user UX
Even though Admin is internal, the admin backend still affects learner/expert speed and clarity.

Backend implication:
- admin APIs should support efficient table filters, bulk reads, and compact summary payloads
- avoid requiring many chained calls for routine ops surfaces

### Professional tone
Admin APIs should preserve conservative, operationally credible semantics.

Backend implication:
- use clear reason codes, status models, and audit/event structures
- avoid “magic” state changes with no traceability

---

## Explicitly defined in the blueprint that directly impacts admin backend/API

### Admin / Content Manager capabilities
The blueprint explicitly states that admin/content manager can:
- manage content and metadata
- create and version tasks
- configure AI evaluation settings
- inspect quality dashboards
- manage feature flags and operational states

Backend implication:
- there must be first-class APIs/resources for each of those capabilities

### Admin / CMS IA explicitly defined
The blueprint explicitly includes:
- Content Library
- Task Builder
- Content Revisions
- Profession Taxonomy
- Rubrics / Criteria Mapping
- AI Evaluation Config
- Review Ops Dashboard
- Quality Analytics
- User Ops
- Billing Ops
- Feature Flags
- Audit Logs

Backend implication:
- each of these must have resource coverage; they cannot all be hidden behind one generic “admin data” endpoint

### Admin routes explicitly defined
- `/admin/content`
- `/admin/content/new`
- `/admin/content/:id`
- `/admin/content/:id/revisions`
- `/admin/taxonomy`
- `/admin/criteria`
- `/admin/ai-config`
- `/admin/review-ops`
- `/admin/analytics/quality`
- `/admin/users`
- `/admin/billing`
- `/admin/flags`
- `/admin/audit-logs`

Backend implication:
- route-aligned API families and permission checks should exist for each admin surface

### Admin UX foundation explicitly applicable
The blueprint explicitly states for Admin / CMS:
- data-table heavy
- filters visible by default
- revision and version history always accessible

Backend implication:
- list endpoints must support server-side filtering/sorting/pagination efficiently
- revision/version data must be fetchable without bespoke hacks
- filters must be first-class query parameters, not frontend-only approximation

### Content Library explicitly defined
Must support:
- data table view
- card view optional
- saved filters
- bulk actions
- publish/archive states
- revision indicators

Backend implication:
- content list endpoint must support filter presets, lifecycle state, bulk action eligibility, and revision summary metadata

### Task Builder explicitly defined
For each content type support:
- metadata entry
- profession selection
- criteria mapping
- difficulty
- estimated duration
- model answer / rubric notes
- versioning

Backend implication:
- content detail/create/update endpoints must support these fields and version-aware persistence

### AI Evaluation Config explicitly defined
Must show:
- active model version
- thresholds
- confidence routing rules
- experiment flags
- prompt/config labels

Backend implication:
- config resources must expose versioned configuration, active selection, and routing-rule metadata

### Quality Analytics explicitly defined
Must show:
- AI-human disagreement
- content performance
- review SLA
- feature adoption
- risk cases

Backend implication:
- analytics backend must expose these metric families and enough dimensions to make the admin screen meaningful

### Review ops, users, billing, flags, and audit logs are explicit surfaces
Even where the blueprint does not define exact fields, the surface itself is explicitly included in IA/routes.

Backend implication:
- these are not optional; backend/API must include first-class support for them

### Security/privacy requirements explicitly relevant to admin
The blueprint explicitly requires:
- role-based route protection
- avoid exposing hidden admin/expert routes in learner bundles where practical
- no sensitive scoring config exposed client-side
- secure handling of tokens and session refresh

Backend implication:
- admin authorization must be strict
- learner/expert APIs must not leak admin-only config data
- high-risk config endpoints must return only what the authorized admin role may see

### QA checklist explicitly relevant to admin
The blueprint explicitly requires:
- content can be created
- revision history visible
- content can be published/unpublished
- AI config page loads and is permission-gated

Backend implication:
- these are non-negotiable backend acceptance conditions

---

## Globally defined elsewhere in the blueprint that still apply to admin backend/API

### General integration rules
The blueprint explicitly says:
- all core pages must tolerate partial data
- optimistic updates only where low risk
- explicit loading and stale states for evaluations, study plans, and reviews
- polling or subscription support for async evaluation states

Admin backend implication:
- admin endpoints should return explicit data completeness and freshness signals where relevant
- destructive/high-risk admin writes should avoid optimistic assumptions
- review ops and analytics endpoints should expose refresh/freshness metadata where delayed

### Required API-driven entities
The blueprint explicitly lists strongly typed entities:
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

Admin backend implication:
- admin APIs will touch most or all of these directly or indirectly
- admin resource schemas should align with these canonical domain entities rather than invent parallel duplicates

### Async workflow handling
The blueprint says these flows are async and require states:
- speaking transcription
- speaking evaluation
- writing evaluation
- human review completion
- study plan regeneration
- report generation

Required states:
- queued
- processing
- completed
- failed with retry guidance

Admin backend implication:
- admin review ops, analytics freshness, and AI config rollout safety must understand these states
- admin should be able to observe or diagnose stuck/failed workflow states where relevant

### State architecture
The blueprint recommends:
- React + TypeScript frontend
- query library for server state
- lightweight client store for session/task-local state
- form library with schema validation
- design system as shared module

Admin backend implication:
- APIs should be cache-friendly and schema-stable
- resource boundaries should support query invalidation and partial hydration well

### Validation rules
The blueprint explicitly requires:
- input validation should be immediate but calm
- scoring fields in review/admin must validate before submit
- required diagnostic/goal fields must show clear reason and fix path

Admin backend implication:
- admin writes should return field-level validation errors and reason codes
- scoring/config fields need server-side validation, not only frontend checks

### Empty states
The blueprint explicitly states every empty state should guide action.

Admin backend implication:
- list endpoints should distinguish `empty` from `filtered_empty` where possible
- analytics endpoints should distinguish `no_data_yet` from `zero_value`

### Error states
The blueprint explicitly requires:
- retry
- save locally where possible
- contact support route only for true blockers
- preserving user work in long tasks

Critical learner errors listed:
- writing draft save failure
- speaking audio upload failure
- evaluation timeout
- review purchase or entitlement mismatch

Admin backend implication:
- admin endpoints should return retryable vs non-retryable error semantics
- content/task builder drafts and config edits should use version-aware saves to preserve work
- review ops and billing ops must surface reason-coded failures clearly

### Analytics instrumentation
The blueprint requires tracking at minimum events such as:
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

Admin backend implication:
- quality analytics and feature adoption rely on these server-side or data-pipeline events being consistent
- admin analytics APIs should not invent alternate definitions disconnected from tracked operational events

### Performance requirements
The blueprint explicitly highlights performance focus on:
- data-heavy admin tables
- diagnostic results and mock reports
- writing editor
- waveform/transcript sync

Admin backend implication:
- admin list/search/filter endpoints must scale well
- analytics endpoints should support bounded windows and summarized results
- do not force the admin frontend to download huge unpaginated datasets

### Security and privacy requirements
The blueprint explicitly requires:
- role-based route protection
- no sensitive scoring config exposed client-side
- secure token/session handling

Admin backend implication:
- admin endpoints must be RBAC-protected
- configuration data should be role-filtered
- secrets/provider credentials should never be returned to browser clients

### Release slicing
The blueprint release slicing places:
- expert console
- admin/CMS
- quality dashboards
in **Slice 5**

Admin backend implication:
- admin backend may depend on learner and expert domain resources already existing
- contracts should still be designed now to avoid rework later

### Open frontend decisions that still affect admin backend
The blueprint explicitly lists open decisions that can spill into admin/backend, including:
- real-time vs polling for evaluation state changes
- dark mode from v1 or later
- editor strategy for Writing
- waveform library choice
- mobile authoring scope

Admin backend implication:
- admin APIs should not hard-code assumptions that block either polling or later live updates
- admin should not depend on raw frontend implementation specifics

---

## Not specified enough yet at project-wide admin backend/API level

These are real gaps that the blueprint does not fully resolve.

### 1) Admin role model and separation of duties
Not defined:
- whether there is one admin role or multiple admin sub-roles
- whether content managers can edit AI config
- whether billing ops can view user content
- whether support staff can adjust credits but not refunds
- whether audit logs are visible to all admins or only privileged operators

### 2) Environment model
Not defined:
- whether admin operates across dev/staging/prod
- whether AI config / flags / content have environment-aware promotion
- whether production changes require approvals

### 3) Approval workflow
Not defined:
- whether publish, config changes, or feature-flag changes require maker/checker approval
- whether some actions are self-serve and others require second approver

### 4) Versioning semantics
Not defined:
- what creates a revision vs a draft update
- whether all admin-managed objects are versioned or only some
- whether restore creates a new latest version or mutates history
- whether unpublished drafts belong to revision history

### 5) Change propagation
Not defined:
- when content, taxonomy, rubric, or AI config changes become effective
- whether changes apply only to future attempts or also trigger reprocessing
- whether historical evaluations are immutable snapshots

### 6) Search model across admin
Not defined:
- whether search is full-text, fielded, or hybrid
- whether filters are personal-only or shareable
- whether saved filters are entity-specific or global

### 7) Bulk-action safety rules
Not defined:
- max bulk sizes
- dry-run/impact preview requirements
- partial success handling
- rollback behavior
- lock/conflict behavior

### 8) Audit-event coverage
Not defined:
- exact event taxonomy
- retention duration
- redaction rules
- whether before/after diffs are stored for all object types
- whether read access events are tracked for sensitive resources

### 9) Cross-resource referential integrity rules
Not defined:
- what blocks archive/delete of content in active use
- whether taxonomy items can be archived while referenced
- whether criteria versions can be retired while active in model routing
- whether flags can be removed while experiments depend on them

### 10) User ops operational boundaries
Not defined:
- whether impersonation exists
- whether auth/account recovery support is in scope
- whether learner/expert/internal users share one model
- what support notes or case-history model exists

### 11) Billing ops operational boundaries
Not defined:
- whether admin can refund, grant credits, change plans, extend renewals, or only view records
- whether wallet/credit adjustments are internal ledger entries or provider-linked actions
- whether finance and support roles are separate

### 12) Quality analytics source-of-truth model
Not defined:
- whether admin analytics are warehouse-backed or online-aggregated
- freshness SLA
- backfill/recomputation strategy
- metric definition governance

### 13) Review ops intervention model
Not defined:
- whether admin can reassign reviews
- whether admin can override queue state
- whether admin can reopen or cancel reviews
- how SLA breach escalation works

### 14) Feature-flag model
Not defined:
- scope hierarchy
- rule language
- rollout percentages
- scheduled rollouts
- dependency/compatibility model
- kill-switch semantics

### 15) AI config governance model
Not defined:
- whether prompts are editable in-browser
- what fields are protected or masked
- whether test/simulate endpoints exist
- rollback and approval policies

---

## Recommended backend/API architecture default

### Resource families
Use distinct admin resource families rather than one generic admin endpoint bucket:
- `/v1/admin/content/*`
- `/v1/admin/taxonomy/*`
- `/v1/admin/criteria/*`
- `/v1/admin/ai-config/*`
- `/v1/admin/review-ops/*`
- `/v1/admin/analytics/*`
- `/v1/admin/users/*`
- `/v1/admin/billing/*`
- `/v1/admin/flags/*`
- `/v1/admin/audit/*`

### Write-safety defaults
- Use optimistic concurrency with `version` or `etag` style checks for editable resources.
- Require explicit reason/comment for high-risk writes.
- Return `impact_summary` for archive/retire/flag/route changes where feasible.
- Separate `draft` from `published` where lifecycle matters.
- Prefer immutable revision snapshots for historical views.

### Read-shape defaults
For admin detail endpoints, return:
- primary entity
- revision summary
- allowed actions
- permissions summary
- linked usage counts
- freshness metadata if data is eventually consistent
- audit summary refs where sensitive changes exist

### Async/admin job defaults
For admin-triggered async jobs, use a shared job model:
- `queued`
- `processing`
- `completed`
- `failed`
- `cancelled` if applicable

Include:
- `job_type`
- `requested_by`
- `requested_at`
- `started_at`
- `completed_at`
- `error_code`
- `error_message_safe`
- `retryable`
- `result_ref`

### Permission model default
Use coarse-to-fine RBAC with optional capability flags:
- `admin_super`
- `admin_content`
- `admin_quality`
- `admin_review_ops`
- `admin_support`
- `admin_billing`
- `admin_flags`
- `admin_audit`

Even if the product later simplifies roles, design APIs so least-privilege can be enforced.

### Historical snapshot default
When content/evaluation-sensitive config changes, preserve references such as:
- `content_version_id`
- `rubric_version_id`
- `criteria_mapping_version_id`
- `ai_config_version_id`
- `flag_snapshot_id` where relevant

This avoids silent historical rewrites.

---

## Recommended standard API conventions

### List endpoints should support
- cursor pagination by default
- explicit sort field and sort direction
- filter objects or field query params
- search query
- `include_archived` where applicable
- `include_counts` for usage summaries where useful
- saved filter references where product approves

### Detail endpoints should support
- `include=` style expansion for heavy optional sections
- stable IDs for all linked entities
- explicit `permissions` / `allowed_actions`

### Bulk endpoints should support
- dry-run mode where action is high-risk
- partial success reporting
- per-item failure reasons
- idempotency key for repeat-safe submissions where relevant

### Writes should return
- updated entity snapshot or accepted job reference
- validation errors with field paths
- conflict/version errors with latest version reference when safe
- audit/reference IDs for sensitive changes when useful

### Error model
Use structured errors with:
- `code`
- `message_safe`
- `field_errors[]`
- `retryable`
- `conflict_ref` where relevant
- `support_ref` or `trace_id`

### Time model
- store server timestamps in UTC
- return ISO-8601 timestamps
- allow admin clients to render locale-specific display time
- include effective-at timestamps for config/flag/content state changes when relevant

---

## Canonical admin backend entities and minimum backend fields

### 1) AdminActor
Minimum fields:
- `id`
- `email`
- `display_name`
- `role_keys[]`
- `capabilities[]`
- `status`
- `last_login_at`

### 2) ContentItemAdmin
Minimum fields:
- `id`
- `content_type`
- `subtest`
- `title`
- `profession_ids[]`
- `difficulty`
- `estimated_duration_minutes`
- `status` (draft/published/archived at minimum)
- `metadata`
- `criteria_mapping_ref`
- `model_answer_ref`
- `rubric_notes_ref`
- `current_revision_id`
- `published_revision_id`
- `usage_counts`
- `created_by`
- `updated_by`
- `created_at`
- `updated_at`
- `published_at`
- `archived_at`

### 3) ContentRevision
Minimum fields:
- `id`
- `content_item_id`
- `revision_number`
- `state`
- `change_note`
- `snapshot_ref`
- `created_by`
- `created_at`
- `restored_from_revision_id`
- `compare_hash`

### 4) ProfessionTaxonomyNode
Minimum fields:
- `id`
- `code`
- `label`
- `description`
- `status`
- `display_order`
- `parent_id` if hierarchy exists
- `aliases[]`
- `usage_counts`
- `created_at`
- `updated_at`

### 5) CriterionDefinition
Minimum fields:
- `id`
- `subtest`
- `official_label`
- `internal_label`
- `description`
- `status`
- `version_id`
- `order_index`
- `created_at`
- `updated_at`

### 6) CriteriaMappingVersion
Minimum fields:
- `id`
- `scope_type` (global/subtest/profession/content-type etc.)
- `scope_ref`
- `criterion_refs[]`
- `weights_or_bands` if product later approves
- `status`
- `effective_at`
- `supersedes_version_id`
- `created_by`
- `created_at`

### 7) AIConfigVersion
Minimum fields:
- `id`
- `surface_scope` (writing/speaking/etc.)
- `model_version_label`
- `thresholds`
- `confidence_routing_rules`
- `experiment_flags[]`
- `prompt_or_config_labels[]`
- `status`
- `effective_at`
- `change_note`
- `created_by`
- `created_at`
- `approved_by` if approval exists

### 8) ReviewOpsSnapshot
Minimum fields:
- `generated_at`
- `queue_counts_by_status`
- `overdue_count`
- `sla_risk_count`
- `avg_turnaround`
- `stuck_workflow_count`
- `assignment_pressure_by_subtest`
- `failure_counts_by_reason`

### 9) QualityMetricSnapshot
Minimum fields:
- `metric_family`
- `time_window`
- `dimension_filters`
- `values`
- `freshness_at`
- `source_version`

### 10) UserAdminProfile
Minimum fields:
- `id`
- `role_type`
- `email`
- `display_name`
- `profession`
- `subscription_status`
- `wallet_or_credit_balance`
- `account_status`
- `created_at`
- `last_active_at`
- `privacy_flags_summary`
- `support_notes_summary` if approved

### 11) BillingAdminRecord
Minimum fields:
- `id`
- `user_id`
- `subscription_id`
- `plan`
- `renewal_at`
- `invoice_refs[]`
- `wallet_credit_balance`
- `payment_status`
- `provider_ref_masked`
- `updated_at`

### 12) FeatureFlag
Minimum fields:
- `id`
- `key`
- `name`
- `description`
- `flag_type` (product/experiment/kill-switch/ops)
- `status`
- `scope`
- `targeting_rules`
- `environment`
- `owner`
- `effective_at`
- `expires_at`
- `created_at`
- `updated_at`

### 13) AuditEvent
Minimum fields:
- `id`
- `occurred_at`
- `actor_type`
- `actor_id`
- `action`
- `resource_type`
- `resource_id`
- `environment`
- `reason_note`
- `before_ref`
- `after_ref`
- `trace_id`
- `ip_or_session_ref_masked`

### 14) AdminJob
Minimum fields:
- `id`
- `job_type`
- `status`
- `requested_by`
- `requested_at`
- `started_at`
- `completed_at`
- `retryable`
- `error_code`
- `result_ref`

---

## Suggested endpoint inventory for the Admin / CMS backend/API

### Content and revisions
- `GET /v1/admin/content`
- `POST /v1/admin/content`
- `GET /v1/admin/content/{contentId}`
- `PATCH /v1/admin/content/{contentId}`
- `POST /v1/admin/content/{contentId}/publish`
- `POST /v1/admin/content/{contentId}/archive`
- `POST /v1/admin/content/bulk-action`
- `GET /v1/admin/content/{contentId}/revisions`
- `GET /v1/admin/content/{contentId}/revisions/{revisionId}`
- `POST /v1/admin/content/{contentId}/revisions/{revisionId}/restore`
- `GET /v1/admin/content/{contentId}/impact-summary`

### Taxonomy
- `GET /v1/admin/taxonomy/professions`
- `POST /v1/admin/taxonomy/professions`
- `PATCH /v1/admin/taxonomy/professions/{professionId}`
- `POST /v1/admin/taxonomy/professions/{professionId}/archive`
- `GET /v1/admin/taxonomy/professions/{professionId}/impact-summary`

### Criteria / rubric mapping
- `GET /v1/admin/criteria`
- `GET /v1/admin/criteria/{criterionId}`
- `PATCH /v1/admin/criteria/{criterionId}`
- `GET /v1/admin/criteria/mappings`
- `POST /v1/admin/criteria/mappings`
- `PATCH /v1/admin/criteria/mappings/{mappingVersionId}`
- `POST /v1/admin/criteria/mappings/{mappingVersionId}/activate`
- `GET /v1/admin/criteria/mappings/{mappingVersionId}/impact-summary`

### AI config
- `GET /v1/admin/ai-config`
- `GET /v1/admin/ai-config/versions`
- `GET /v1/admin/ai-config/{configVersionId}`
- `POST /v1/admin/ai-config`
- `PATCH /v1/admin/ai-config/{configVersionId}`
- `POST /v1/admin/ai-config/{configVersionId}/activate`
- `POST /v1/admin/ai-config/{configVersionId}/simulate` (recommended)
- `GET /v1/admin/ai-config/{configVersionId}/impact-summary`

### Review ops
- `GET /v1/admin/review-ops/summary`
- `GET /v1/admin/review-ops/queue`
- `GET /v1/admin/review-ops/reviews/{reviewRequestId}`
- `POST /v1/admin/review-ops/reviews/{reviewRequestId}/reassign`
- `POST /v1/admin/review-ops/reviews/{reviewRequestId}/cancel`
- `POST /v1/admin/review-ops/reviews/{reviewRequestId}/reopen`
- `GET /v1/admin/review-ops/failures`

### Quality analytics
- `GET /v1/admin/analytics/quality/summary`
- `GET /v1/admin/analytics/quality/ai-human-disagreement`
- `GET /v1/admin/analytics/quality/content-performance`
- `GET /v1/admin/analytics/quality/review-sla`
- `GET /v1/admin/analytics/quality/feature-adoption`
- `GET /v1/admin/analytics/quality/risk-cases`

### User ops
- `GET /v1/admin/users`
- `GET /v1/admin/users/{userId}`
- `PATCH /v1/admin/users/{userId}`
- `POST /v1/admin/users/{userId}/status`
- `POST /v1/admin/users/{userId}/credits-adjustment`
- `GET /v1/admin/users/{userId}/activity-summary`

### Billing ops
- `GET /v1/admin/billing/subscriptions`
- `GET /v1/admin/billing/subscriptions/{subscriptionId}`
- `GET /v1/admin/billing/invoices`
- `POST /v1/admin/billing/credits-adjustment`
- `POST /v1/admin/billing/subscriptions/{subscriptionId}/plan-change` (if approved)
- `POST /v1/admin/billing/subscriptions/{subscriptionId}/refund` (if approved)

### Feature flags
- `GET /v1/admin/flags`
- `POST /v1/admin/flags`
- `GET /v1/admin/flags/{flagId}`
- `PATCH /v1/admin/flags/{flagId}`
- `POST /v1/admin/flags/{flagId}/activate`
- `POST /v1/admin/flags/{flagId}/deactivate`
- `GET /v1/admin/flags/{flagId}/impact-summary`

### Audit and jobs
- `GET /v1/admin/audit/events`
- `GET /v1/admin/audit/events/{eventId}`
- `GET /v1/admin/jobs/{jobId}`

---

## Recommended canonical state machines

### Content lifecycle
Recommended default:
- `draft`
- `published`
- `archived`

Optional later states if approved:
- `scheduled`
- `deprecated`
- `under_review`

Rules:
- publish should require validation pass
- archive should preserve historical references
- restore should create a new revision instead of mutating history

### Revision lifecycle
Recommended default:
- `draft_working_copy`
- `saved_revision`
- `published_revision`
- `restored_revision`

### Taxonomy lifecycle
Recommended default:
- `active`
- `archived`

### Criteria mapping lifecycle
Recommended default:
- `draft`
- `active`
- `retired`

### AI config lifecycle
Recommended default:
- `draft`
- `active`
- `retired`
- `superseded`

### Feature flag lifecycle
Recommended default:
- `inactive`
- `active`
- `scheduled`
- `expired`

### Admin job lifecycle
Recommended default:
- `queued`
- `processing`
- `completed`
- `failed`
- `cancelled` where applicable

---

## Recommended cross-resource relationships

- `ContentItemAdmin` references `ProfessionTaxonomyNode`, `CriteriaMappingVersion`, and revision resources.
- Learner `attempt` and `evaluation` should reference the exact `content_version_id` used at runtime.
- Expert `reviewRequest` should reference the content/evaluation/rubric versions active at request creation.
- `AIConfigVersion` may reference criteria mapping versions and experiment/flag keys.
- `FeatureFlag` may gate content exposure, routing behavior, or admin UI exposure, but should not erase auditability.
- `AuditEvent` should reference all sensitive content/config/ops/billing changes.

---

## Recommended server-side analytics / event emission strategy

Admin / CMS itself should emit operational events for observability and audit, separate from learner/expert product analytics.

Recommended event families:
- content.created
- content.updated
- content.published
- content.archived
- content.bulk_action_requested
- revision.restored
- taxonomy.created
- taxonomy.updated
- taxonomy.archived
- criteria.mapping.created
- criteria.mapping.activated
- ai_config.created
- ai_config.activated
- review_ops.reassignment
- review_ops.override
- billing.credit_adjusted
- billing.plan_changed
- flag.created
- flag.activated
- flag.deactivated
- audit.viewed

Each event should include where relevant:
- admin actor id
- admin role/capability
- resource id
- resource type
- environment
- reason note
- trace id
- timestamp

---

## Recommended backend delivery checklist before handing to frontend

- All admin endpoints are RBAC-protected.
- Sensitive config values are masked or omitted.
- Content creation/edit/publish/archive flows support version checks.
- Revision history is queryable and restorable.
- Filters/sorts/pagination exist for all table-heavy surfaces.
- High-risk writes carry audit trails.
- Bulk actions return partial success/failure details.
- Usage/impact summaries exist for archive/retire operations where feasible.
- Review ops can observe stuck/failed async flows.
- Analytics endpoints expose freshness timestamps.
- User/billing actions are clearly separated by permissions.
- Feature-flag changes are environment-aware and auditable.
- Audit logs are searchable enough to debug admin activity.
- All write endpoints return structured validation/conflict errors.

---

## Feature 1: Content Library

**Primary admin route:** `/admin/content`

### Explicitly defined in the blueprint
- Must support: data table view, optional card view, saved filters, bulk actions, publish/archive states, revision indicators.
- Admin / CMS is data-table heavy and filters are visible by default.
- Revision and version history must always be accessible.

### Globally defined elsewhere in the blueprint
- Component states apply: loading, empty, partial, error, permission denied, stale.
- Performance expectations for large tables apply.
- Role protection and auditability apply.
- QA critical path requires content can be created and published/unpublished.

### Not specified enough yet
- Exact columns are not defined.
- Search behavior and filter taxonomy are not defined.
- Saved-filter ownership/sharing is not defined.
- Bulk-action list is not defined.
- Card view v1/vlater is not defined.
- Impact preview before archive is not defined.

### Recommended backend/API implementation default
- Implement `GET /v1/admin/content` with cursor pagination, server-side filters, sort, search, and optional saved-filter reference.
- Include compact row fields plus `status`, `revision_summary`, `usage_counts`, and `allowed_actions`.
- Expose `POST /v1/admin/content/bulk-action` with dry-run mode and per-item results.
- Default sort by `updated_at desc` unless overridden.

---

## Feature 2: Task Builder

**Primary admin routes:** `/admin/content/new`, `/admin/content/:id`

### Explicitly defined in the blueprint
- For each content type support: metadata entry, profession selection, criteria mapping, difficulty, estimated duration, model answer/rubric notes, versioning.

### Globally defined elsewhere in the blueprint
- Validation must be immediate/calm.
- Partial data and save safety matter for long authoring tasks.
- Revision history must always be accessible.
- Design-system and domain components must be reused.

### Not specified enough yet
- Exact content-type schemas are not defined.
- Autosave vs explicit save is not defined.
- Draft vs publish policy is not fully defined.
- Edit-locking/concurrent editing is not defined.
- Structured fields for case notes/model answers are not fully defined.

### Recommended backend/API implementation default
- Implement `POST /v1/admin/content` and `PATCH /v1/admin/content/{contentId}` with version-aware writes.
- Support `draft` saves separately from `publish`.
- Return field-level validation errors and `version_conflict` when concurrent edits collide.
- Keep authoring schema typed by `content_type` + `subtest` and expose a validation profile per content type.

---

## Feature 3: Content Revisions

**Primary admin route:** `/admin/content/:id/revisions`

### Explicitly defined in the blueprint
- Revision and version history always accessible.
- Content can be versioned.
- Dedicated revisions route exists.

### Globally defined elsewhere in the blueprint
- Table/diff-friendly data shapes matter.
- Permission and audit requirements apply.
- QA requires revision history visible.

### Not specified enough yet
- Diff granularity is not defined.
- Restore semantics are not defined.
- Revision-note requirements are not defined.
- Whether unpublished drafts appear is not defined.

### Recommended backend/API implementation default
- Implement immutable revision snapshots.
- Expose revision list with version label, state, change note, created_by, created_at.
- Expose revision detail and restore action.
- Make restore create a new latest revision instead of rewriting history.

---

## Feature 4: Profession Taxonomy

**Primary admin route:** `/admin/taxonomy`

### Explicitly defined in the blueprint
- Profession Taxonomy exists as an Admin / CMS surface and route.
- The wider product relies on profession-specific learner and content behavior.

### Globally defined elsewhere in the blueprint
- OET-native fidelity applies.
- Validation, permissions, and revision/audit expectations still apply.
- Changes may affect learner/app behavior broadly.

### Not specified enough yet
- Flat vs hierarchical model is not defined.
- Delete vs archive policy is not defined.
- Impact analysis requirement is not defined.
- Scope beyond professions is not defined.

### Recommended backend/API implementation default
- Implement create/edit/archive for profession nodes.
- Block destructive delete when referenced.
- Return usage counts and impact summaries before archival.
- Keep taxonomy changes auditable and version-safe where used by live content.

---

## Feature 5: Rubrics / Criteria Mapping

**Primary admin route:** `/admin/criteria`

### Explicitly defined in the blueprint
- Admin IA includes Rubrics / Criteria Mapping.
- Product aligns to official Writing criteria and official Speaking linguistic/clinical communication criteria.
- Feedback is criterion-first.

### Globally defined elsewhere in the blueprint
- Trust-first and OET-native principles apply strongly.
- Sensitive scoring/config semantics should not leak broadly.
- Versioning and historical protection matter.

### Not specified enough yet
- Whether official criterion labels are editable is not defined.
- Whether weights/bands live here or elsewhere is not defined.
- Scope per profession/subtest/content type is not defined.
- Historical-effect policy is not defined.

### Recommended backend/API implementation default
- Treat official criterion identity as protected, while allowing internal mapping/version objects to change.
- Separate `CriterionDefinition` from `CriteriaMappingVersion`.
- Require activation for mapping versions instead of implicit live mutation.
- Preserve mapping-version references on evaluations and review requests.

---

## Feature 6: AI Evaluation Config

**Primary admin route:** `/admin/ai-config`

### Explicitly defined in the blueprint
- Must show active model version, thresholds, confidence routing rules, experiment flags, prompt/config labels.

### Globally defined elsewhere in the blueprint
- Trust-first is critical.
- Sensitive scoring config must not be exposed client-side to unauthorized users.
- Async/evaluation workflows and stale-state visibility matter.
- QA requires AI config page loads and is permission-gated.

### Not specified enough yet
- Exact editable fields are not defined.
- Approval flow is not defined.
- Environment separation is not defined.
- Rollback/test simulation policy is not defined.
- Relationship with feature flags is not fully defined.

### Recommended backend/API implementation default
- Use versioned config objects with explicit activation.
- Separate inspect-only metadata from editable threshold/routing fields.
- Require change note on save/activation.
- Provide optional `simulate` endpoint to test route outcomes before activation.
- Never expose raw secret/provider credentials to browser clients.

---

## Feature 7: Review Ops Dashboard

**Primary admin route:** `/admin/review-ops`

### Explicitly defined in the blueprint
- Review Ops Dashboard exists.
- Admin manages operational states.
- Quality analytics includes review SLA.
- Expert console includes review queue/workflows that this surface likely oversees.

### Globally defined elsewhere in the blueprint
- Async workflow states matter.
- Data-table-heavy and filter-visible-by-default rules apply.
- Operational realism and trust matter.

### Not specified enough yet
- Monitor-only vs intervention surface is not defined.
- Assignment/reassignment capabilities are not defined.
- Escalation rules are not defined.
- Stuck review detection and overrides are not defined.

### Recommended backend/API implementation default
- Provide summary + drill-through APIs for queue health, overdue work, SLA risk, and stuck workflow counts.
- Support controlled intervention endpoints such as reassign/cancel/reopen only if authorized.
- Return explicit `allowed_actions` and confirmation requirements for dangerous operations.

---

## Feature 8: Quality Analytics

**Primary admin route:** `/admin/analytics/quality`

### Explicitly defined in the blueprint
- Must show AI-human disagreement, content performance, review SLA, feature adoption, risk cases.

### Globally defined elsewhere in the blueprint
- Analytics instrumentation and performance requirements apply.
- Partial data and freshness visibility matter.
- Professional, high-trust presentation semantics matter.

### Not specified enough yet
- Exact formulas are not defined.
- Dimension filters/time windows are not defined.
- Warehouse vs online source is not defined.
- Drill-down paths are not defined.

### Recommended backend/API implementation default
- Expose one summary endpoint plus dedicated metric-family endpoints.
- Include freshness timestamp, dimension filters, and metric definition version metadata.
- Distinguish `no_data` from zero.
- Bound all analytics queries by time window and supported dimensions.

---

## Feature 9: User Ops

**Primary admin route:** `/admin/users`

### Explicitly defined in the blueprint
- User Ops exists as an Admin / CMS surface.

### Globally defined elsewhere in the blueprint
- Role-based protection, privacy, partial data tolerance, and table-heavy design rules apply.
- Strongly typed entities such as user, learnerGoal, subscription, wallet/credits may all matter here.

### Not specified enough yet
- Supported searches and actions are not defined.
- Learner/expert/internal model separation is not defined.
- Impersonation/support-notes/privacy exposure is not defined.

### Recommended backend/API implementation default
- Start with read-first profile/search plus limited safe actions such as account status changes and credit adjustments where approved.
- Require reason capture and audit events for support-impacting changes.
- Expose linked summaries rather than entire histories by default.

---

## Feature 10: Billing Ops

**Primary admin route:** `/admin/billing`

### Explicitly defined in the blueprint
- Billing Ops exists as an Admin / CMS surface.
- Learner billing includes plan, renewal, review credits, invoices, extras; admin likely oversees operational support for these.

### Globally defined elsewhere in the blueprint
- Sensitive financial operations require strong protection and auditability.
- Optimistic updates should be avoided for high-risk billing changes.

### Not specified enough yet
- Read-only vs editable scope is not defined.
- Refunds/plan changes/credit grants are not defined.
- Provider-linked behavior is not defined.
- Finance-vs-support separation is not defined.

### Recommended backend/API implementation default
- Expose subscription/invoice/credit read APIs first.
- Gate mutable actions such as credit adjustments or plan changes behind explicit permissions and reason capture.
- Return masked provider references only.
- Record all adjustments in immutable admin billing events.

---

## Feature 11: Feature Flags

**Primary admin route:** `/admin/flags`

### Explicitly defined in the blueprint
- Feature Flags exists as an Admin / CMS surface.
- Admin manages feature flags and operational states.
- AI Evaluation Config separately includes experiment flags.

### Globally defined elsewhere in the blueprint
- High-risk changes require conservative handling and auditability.
- Security/permissions are critical.
- Runtime behavior across learner/expert/admin surfaces may depend on flags.

### Not specified enough yet
- Scope hierarchy, rollout model, scheduling, dependencies, and kill-switch semantics are not defined.
- Relationship between experiment flags and product flags is not fully defined.

### Recommended backend/API implementation default
- Model flags as first-class resources with type, scope, targeting rules, environment, owner, and effective timestamps.
- Distinguish operational kill switches from ordinary product/experiment flags.
- Require explicit confirmation and audit note for high-impact toggles.

---

## Feature 12: Audit Logs

**Primary admin route:** `/admin/audit-logs`

### Explicitly defined in the blueprint
- Audit Logs exists as an Admin / CMS surface.

### Globally defined elsewhere in the blueprint
- Trust, security, and role protection apply strongly.
- Admin changes to sensitive objects must be auditable.
- Large-table performance requirements still apply.

### Not specified enough yet
- Event coverage, retention, search filters, redaction, and export permissions are not defined.
- Before/after diff visibility is not defined.
- Whether end-user actions appear here is not defined.

### Recommended backend/API implementation default
- Implement searchable structured audit events with actor/action/resource/time/environment model.
- Support filters by actor, resource type, action, date range, and trace id.
- Keep sensitive fields redacted or referenced via secure detail refs.
- Store before/after snapshots for high-risk writes where feasible.

---

## Final implementation note
This file is intentionally strict.

It gives the backend developer three separate layers:
- what the blueprint already requires
- what project-wide rules still apply to Admin / CMS
- what remains under-specified and must not be faked as final product truth

The backend developer should **build to the current platform design system, current domain components, and current workflow language**.

They should **not invent new admin workflows, new UI semantics, new scoring meaning, new permission behavior, or new financial/config authority** unless product/design explicitly approves them.

Where this file gives a recommended default, that default is there to unblock implementation safely — not to rewrite the source blueprint.
