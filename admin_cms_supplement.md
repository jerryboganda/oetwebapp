# OET Platform — Admin / CMS Supplement
_Last updated: 2026-03-25_

## 1. Document purpose

This file is a **developer-facing supplement** to the existing product blueprint for the **Admin / CMS** surface only.

It consolidates three things for the admin-facing product surface:

1. **Explicitly defined in the blueprint**
2. **Globally defined elsewhere in the blueprint but still applicable to Admin / CMS**
3. **Not specified enough yet** (open decisions, missing detail, implementation gaps, and ambiguity that must be resolved or consciously assumed)

This file is intended to help engineering start implementation without inventing product behavior that was never approved.

It is also intended to make sure the Admin / CMS surface covers the operational needs of the entire OET platform as defined in the blueprint:
- content and metadata management
- task creation and versioning
- profession taxonomy management
- rubric and criteria mapping
- AI evaluation configuration
- review operations oversight
- quality analytics
- user operations
- billing operations
- feature flags
- audit logging

This file does **not** introduce new product scope. It organizes and operationalizes what the blueprint already implies and highlights unresolved areas that still require product decisions.

---

## 2. Non-negotiable instruction to the frontend developer

### 2.1 Use the current design system and existing product language
The Admin / CMS must be built using the **current platform design system, shared primitives, and existing domain components** already defined in the blueprint.

**Do not invent a new visual language, new interaction model, new navigation pattern, new content builder paradigm, or unrelated one-off admin UI unless product/design explicitly approves it.**

Use the existing system first:
- AppShell
- Sidebar
- TopNav
- BottomNav where globally applicable, though admin will likely be desktop-first
- Card
- Table
- FilterBar
- Tabs
- Accordion
- Modal / Drawer
- Toast / Inline alert
- Skeleton loader
- Empty state
- Error state
- Retry state
- Timer where relevant
- Progress bar where relevant
- Stepper where relevant
- Status badge
- Score range badge where relevant
- Confidence badge where relevant
- Criterion chip
- Task card where relevant
- Submission card where relevant

Relevant domain-specific components already defined and expected to be reused where applicable:
- ProfessionSelector
- SubtestSwitcher
- CriterionBreakdownCard
- ReviewerRubricPanel
- ContentMetadataPanel
- VersionHistoryDrawer

If a needed admin-specific component is missing, it should be created **inside the same design system language**, not as a visually unrelated one-off.

### 2.2 Do not invent product behavior where the blueprint is silent
If the blueprint does not define:
- exact content schema per content type
- publishing workflow details
- version branching rules
- taxonomy hierarchy depth
- criterion/rubric editing permissions
- AI threshold semantics
- feature-flag scope hierarchy
- review-ops escalation logic
- user-ops permissions
- billing-ops capabilities
- audit-log retention or search behavior

then those must be treated as **open decisions**, not silently invented.

### 2.3 Treat this document as a supplement, not a replacement
This file does **not replace** the original blueprint. It clarifies and operationalizes it for the Admin / CMS surface.

---

## 3. Admin / CMS scope covered by this file

The blueprint defines the following Admin / CMS information architecture:

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

The blueprint defines the following Admin / CMS routes:

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

This supplement covers all of the above.

### 3.1 Project-coverage expectation for Admin / CMS
The blueprint implicitly makes Admin / CMS the operational control surface for the entire application layer, excluding the public marketing site.

That means Admin / CMS should be understood as the place where internal operators manage:
- learner-facing content objects
- evaluation and rubric configuration dependencies
- operational review systems
- user and subscription operations
- feature rollout controls
- compliance/audit visibility

If a platform domain exists in production but has **no clear admin owner in the blueprint**, that gap must be called out rather than silently left unmanaged.

---

## 4. Canonical source material extracted from the blueprint

## 4.1 Admin / Content Manager role and capabilities — explicitly defined

The admin/content-manager role has the following explicitly defined capabilities:
- manage content and metadata
- create and version tasks
- configure AI evaluation settings
- inspect quality dashboards
- manage feature flags and operational states

This means the Admin / CMS is not merely a content uploader. It is an operational control plane for the assessment product.

---

## 4.2 Admin / CMS IA — explicitly defined

The blueprint explicitly defines the Admin / CMS IA as:
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

This is the current admin navigation scope currently in product definition.

---

## 4.3 Admin / CMS route map — explicitly defined

The blueprint explicitly defines the following routes:

| Area | Route |
|---|---|
| Content Library | `/admin/content` |
| New Content / Task Builder Entry | `/admin/content/new` |
| Content Detail / Edit | `/admin/content/:id` |
| Content Revisions | `/admin/content/:id/revisions` |
| Profession Taxonomy | `/admin/taxonomy` |
| Rubrics / Criteria Mapping | `/admin/criteria` |
| AI Evaluation Config | `/admin/ai-config` |
| Review Ops Dashboard | `/admin/review-ops` |
| Quality Analytics | `/admin/analytics/quality` |
| User Ops | `/admin/users` |
| Billing Ops | `/admin/billing` |
| Feature Flags | `/admin/flags` |
| Audit Logs | `/admin/audit-logs` |

These routes should be treated as the current expected route contract unless product/navigation changes are approved.

---

## 4.4 Admin / CMS layout and UX foundation — explicitly defined elsewhere but directly applicable

The blueprint explicitly states for Admin / CMS:

- **data-table heavy**
- **filters visible by default**
- **revision and version history always accessible**

This is a major product requirement, not a visual suggestion.

Implications:
- admin workflows should favor information density over decorative layout
- list pages should prioritize table usability and operational scanning
- filters should not be hidden behind unnecessary secondary actions by default
- version/revision access should be consistently reachable from relevant content/config surfaces

---

## 4.5 Content Library — explicitly defined in the blueprint

### Route
`/admin/content`

### Must support
The blueprint explicitly requires the Content Library to support:
- data table view
- card view optional
- saved filters
- bulk actions
- publish/archive states
- revision indicators

### What is explicitly known
The Content Library is the main operational browser for content assets and must support at least one table-based management surface, optional card representation, filtering persistence, batch actions, lifecycle state visibility, and revision visibility.

### What is not explicitly defined at this layer
The blueprint does not explicitly define:
- which content types exist in the library
- which columns appear in the table
- whether card view is v1 or later
- which filters can be saved
- which bulk actions are available
- who can publish/archive
- revision indicator format
- whether library search exists
- whether library supports folders/tags
- whether archived content is editable
- whether there is draft state in addition to publish/archive

These belong in the under-specified section later in this file.

---

## 4.6 Task Builder — explicitly defined in the blueprint

### Routes
- `/admin/content/new`
- `/admin/content/:id`

### For each content type support
The blueprint explicitly requires Task Builder support for:
- metadata entry
- profession selection
- criteria mapping
- difficulty
- estimated duration
- model answer / rubric notes
- versioning

### What is explicitly known
Task Builder is the authoring/editing surface for content items and must support at least the listed fields/capabilities for every content type in scope.

### What is not explicitly defined at this layer
The blueprint does not explicitly define:
- exact content types
- field-level schemas
- autosave behavior
- draft vs published workflow
- whether model answer is structured rich content
- whether rubric notes are internal-only
- validation rules per field
- collaboration/edit locking
- approval flow before publishing
- preview mode
- localization/internationalization support
- attachment/media handling

These belong in the under-specified section later in this file.

---

## 4.7 Content Revisions — explicitly defined only at route/IA level

### Route
`/admin/content/:id/revisions`

### Explicitly known
The blueprint includes Content Revisions in IA and route map, and globally requires revision/version history always accessible.

### What this means
There must be a revision/history surface for content items, but the blueprint does not specify its exact data model or interactions.

### What is not explicitly defined
The blueprint does not explicitly define:
- revision list fields
- compare view behavior
- restore workflow
- publish-from-revision workflow
- diff granularity
- who can restore revisions
- whether revisions apply only to content or also configs
- whether comments/change notes exist

These belong in the under-specified section later in this file.

---

## 4.8 Profession Taxonomy — explicitly defined only at route/IA level

### Route
`/admin/taxonomy`

### Explicitly known
The blueprint includes Profession Taxonomy in IA and route map.

### What this means
There must be an admin surface for maintaining profession-related taxonomy used throughout learner, expert, and scoring workflows.

### What is not explicitly defined
The blueprint does not explicitly define:
- taxonomy depth or hierarchy
- whether this covers professions only or profession + scenario types + organizations + countries
- create/edit/archive behavior
- impact analysis for taxonomy changes
- migration behavior for content already mapped to older taxonomy values
- whether taxonomy supports localization labels
- whether ordering/prioritization is configurable

These belong in the under-specified section later in this file.

---

## 4.9 Rubrics / Criteria Mapping — explicitly defined only at route/IA level

### Route
`/admin/criteria`

### Explicitly known
The blueprint includes Rubrics / Criteria Mapping in IA and route map. The overall platform is explicitly aligned to:
- official Writing criteria
- official Speaking linguistic and clinical communication criteria
- profession-specific workflows
- criterion-first feedback

### What this means
There must be an admin surface for managing or mapping criteria/rubrics that underpin evaluation and feedback systems.

### What is not explicitly defined
The blueprint does not explicitly define:
- whether admins can edit official criteria labels or only map internal configs to them
- whether Writing and Speaking live in separate tabs
- whether Reading/Listening item tags appear here
- whether score bands are editable
- whether criteria weights are configurable
- whether historical evaluations are reinterpreted after mapping changes
- whether changes require approvals/versioning

These belong in the under-specified section later in this file.

---

## 4.10 AI Evaluation Config — explicitly defined in the blueprint

### Route
`/admin/ai-config`

### Must show
The blueprint explicitly requires AI Evaluation Config to show:
- active model version
- thresholds
- confidence routing rules
- experiment flags
- prompt/config labels

### What is explicitly known
There is an admin surface for inspecting and/or editing evaluation configuration metadata and routing logic.

### What is not explicitly defined
The blueprint does not explicitly define:
- which fields are editable vs read-only
- whether config changes are immediate or staged
- whether there is environment separation
- what thresholds mean in practice
- what routes confidence triggers
- how experiment flags differ from general feature flags
- whether prompts are editable inline
- rollback behavior
- approval requirements
- audit requirements beyond general audit logs

These belong in the under-specified section later in this file.

---

## 4.11 Review Ops Dashboard — explicitly defined only at route/IA level

### Route
`/admin/review-ops`

### Explicitly known
The blueprint includes Review Ops Dashboard in IA and route map, and the admin role explicitly includes managing operational states. Quality analytics also explicitly includes review SLA.

### What this means
There must be an operational surface for review-system oversight, but the blueprint does not fully define it.

### What is not explicitly defined
The blueprint does not explicitly define:
- which review ops KPIs are shown here vs in quality analytics
- whether queue controls are embedded here
- whether assignment/reassignment happens here
- whether reviewer availability management is visible here
- whether SLA breach handling exists
- whether admin can override review status
- whether admin can intervene in stuck reviews
- whether this dashboard is analytics-only or operationally interactive

These belong in the under-specified section later in this file.

---

## 4.12 Quality Analytics — explicitly defined in the blueprint

### Route
`/admin/analytics/quality`

### Must show
The blueprint explicitly requires Quality Analytics to show:
- AI-human disagreement
- content performance
- review SLA
- feature adoption
- risk cases

### What is explicitly known
There is an admin analytics surface for platform quality/operations monitoring across AI, content, and review performance.

### What is not explicitly defined
The blueprint does not explicitly define:
- metrics definitions
- chart/table formats
- date filters
- drill-down depth
- segmentation options
- what qualifies as a risk case
- whether adoption is learner-only or includes expert/admin usage
- who can export data
- whether this is near-real-time or batch-updated

These belong in the under-specified section later in this file.

---

## 4.13 User Ops — explicitly defined only at route/IA level

### Route
`/admin/users`

### Explicitly known
The blueprint includes User Ops in IA and route map.

### What this means
There must be an admin surface for user-related operations, but the blueprint does not define its actual capabilities.

### What is not explicitly defined
The blueprint does not explicitly define:
- search fields
- user profile fields
- role management
- entitlement management
- account state controls
- impersonation policy
- audit visibility within user view
- support actions
- privacy restrictions
- whether learner and expert user ops share one table

These belong in the under-specified section later in this file.

---

## 4.14 Billing Ops — explicitly defined only at route/IA level

### Route
`/admin/billing`

### Explicitly known
The blueprint includes Billing Ops in IA and route map.

### What this means
There must be an admin surface for subscription/billing operations, but the blueprint does not define its exact tools.

### What is not explicitly defined
The blueprint does not explicitly define:
- invoice search
- subscription editing powers
- credit adjustments
- refund workflows
- payment-provider integration surface
- manual grant rules
- finance-role permissions
- reporting/export functionality

These belong in the under-specified section later in this file.

---

## 4.15 Feature Flags — explicitly defined only at route/IA level

### Route
`/admin/flags`

### Explicitly known
The blueprint explicitly states the admin role manages feature flags and operational states, and includes Feature Flags in IA and route map.

### What this means
There must be a surface for flag management and possibly project operational toggles.

### What is not explicitly defined
The blueprint does not explicitly define:
- flag types
- rollout scopes
- environment separation
- kill-switch semantics
- dependencies between flags
- approvals for changes
- who can toggle which flags
- schedule-based rollouts
- flag descriptions and owner fields

These belong in the under-specified section later in this file.

---

## 4.16 Audit Logs — explicitly defined only at route/IA level

### Route
`/admin/audit-logs`

### Explicitly known
The blueprint includes Audit Logs in IA and route map.

### What this means
There must be a system-level visibility surface for changes/actions across the platform.

### What is not explicitly defined
The blueprint does not explicitly define:
- which events are logged
- retention period
- search/filter fields
- actor/resource model
- before/after visibility
- sensitive-data redaction rules
- export permissions
- whether logs cover AI config changes, content changes, billing changes, user actions, and flag changes equally

These belong in the under-specified section later in this file.

---

## 5. Globally defined elsewhere in the blueprint that still applies to Admin / CMS

## 5.1 Product mission and frontend mission — inherited

The product mission is to build the most trusted, profession-specific OET preparation platform by combining:
- assessment-grade practice flows
- criterion-based feedback
- adaptive study planning
- AI speed
- human review trust

The frontend mission is to make the platform feel:
- clear
- high-trust
- exam-focused
- efficient for time-poor healthcare professionals
- stable under long writing and audio workflows
- understandable even when scoring is probabilistic

### Admin / CMS implication
Even though Admin / CMS is an internal surface, its controls must preserve the same platform qualities:
- content tools must preserve OET-native fidelity
- criteria/rubric tools must reinforce criterion-first design
- config tools must not enable overconfident scoring presentation
- operational tools must support stability and trust
- admin language should remain professional and measured

---

## 5.2 Product principles — inherited

The blueprint defines six product principles:

1. OET-native, not generic ESL
2. Criterion-first
3. Practice-first
4. Trust-first
5. Time-poor user UX
6. Professional tone

### Admin / CMS implication
These principles still constrain admin work:

- **OET-native**  
  Admin data models and content tools must reflect OET sub-tests, profession-specific Writing/Speaking, official criteria, and serious assessment logic.

- **Criterion-first**  
  Criteria mapping, rubric configuration, and quality analytics should revolve around criteria, not vague labels.

- **Practice-first**  
  Content authoring and operational priorities should support rapid learner action flows, not content sprawl.

- **Trust-first**  
  Admin controls around scoring/config must not enable UI claims that look official when they are only training estimates.

- **Time-poor user UX**  
  Even internal admin tools should expose clear actions, defaults, and visible state.

- **Professional tone**  
  Avoid playful or casual admin terminology that weakens trust in a medical-education assessment context.

---

## 5.3 Visual tone — inherited

The blueprint explicitly requires the product visual tone to be:
- clinical
- clean
- high-trust
- no childish gamification
- progress motivating but credible
- status colors conservative; scoring risk should not feel alarmist unless necessary

### Admin / CMS implication
Admin pages should:
- feel operational and trustworthy
- avoid toy-like dashboards
- use severity colors conservatively
- avoid “growth-hack” visual language for critical controls such as billing, AI config, flags, and audits

---

## 5.4 Accessibility — inherited and required from first release

The blueprint explicitly requires:
- keyboard navigation for all core paths
- visible focus states
- accessible form labels and validation
- high contrast compliance
- support for screen readers on dashboard, forms, results pages
- captions/transcript access where relevant
- scalable typography
- reduced-motion preference support

### Admin / CMS implication
This still applies to admin, especially:
- keyboard navigation across data tables and forms
- visible focus in dense operational layouts
- accessible labels on complex config forms
- screen-reader-compatible tables and filters
- scalable typography in data-heavy views
- reduced motion for drawers, transitions, and alerts

---

## 5.5 Design system primitives — inherited

The blueprint explicitly defines the following core UI primitives:
- AppShell
- Sidebar
- TopNav
- BottomNav
- Card
- Table
- FilterBar
- Tabs
- Accordion
- Modal / Drawer
- Toast / Inline alert
- Skeleton loader
- Empty state
- Error state
- Retry state
- Audio player
- Waveform viewer
- Transcript viewer
- Rich text editor / structured writing editor
- Timer
- Progress bar
- Stepper
- Status badge
- Score range badge
- Confidence badge
- Criterion chip
- Review comment anchor
- Task card
- Submission card

### Admin / CMS implication
Admin should preferentially reuse:
- AppShell
- Sidebar
- TopNav
- Table
- FilterBar
- Tabs
- Modal / Drawer
- Toast / Inline alert
- Skeleton loader
- Empty state
- Error state
- Retry state
- Status badge
- Confidence badge where AI-related controls need it
- Criterion chip
- Rich text editor / structured writing editor if used in authoring/model-answer fields

The existence of these primitives means engineering should not invent unrelated list, form, or status systems.

---

## 5.6 Domain-specific components — inherited

The blueprint explicitly defines these domain-specific components:
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
- ReviewerRubricPanel
- ContentMetadataPanel
- VersionHistoryDrawer

### Admin / CMS implication
The following are especially relevant to admin:
- ProfessionSelector
- CriterionBreakdownCard
- RevisionDiffViewer
- ReviewerRubricPanel
- ContentMetadataPanel
- VersionHistoryDrawer

Where admin needs content metadata editing or revision access, reuse these patterns first.

---

## 5.7 Component state support — inherited

The blueprint explicitly requires every component to support:
- loading
- empty
- success
- partial data
- error
- permission denied where relevant
- stale data warning where relevant

### Admin / CMS implication
Every admin screen and every important panel/widget should have explicit state handling, including:
- loading content/config/analytics state
- empty table state
- partial data state for incomplete or lagging analytics/config fetches
- permission denied state for restricted admin actions
- stale warning when operational data may be outdated

---

## 5.8 General integration rules — inherited

The blueprint explicitly requires:
- all core pages must tolerate partial data
- optimistic updates only where low risk
- explicit loading and stale states for evaluations, study plans, and reviews
- polling or subscription support for async evaluation states

### Admin / CMS implication
Admin pages should extend the same discipline to:
- content records
- version metadata
- AI config records
- ops dashboards
- analytics
- audit events
- billing/user operational records

Optimistic updates should be used cautiously for high-risk operations such as:
- publishing
- archiving
- changing thresholds
- toggling flags
- billing changes
- role changes

---

## 5.9 Strongly typed frontend entities — inherited

The blueprint explicitly says frontend should strongly type these entities:
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

### Admin / CMS implication
Admin will directly or indirectly need strong typing around at least:
- user
- profession
- subtest
- criterion
- contentItem
- attempt
- evaluation
- criterionScore
- reviewRequest
- subscription
- wallet/credits

It will likely also need additional admin-specific types not explicitly listed in the blueprint, such as:
- contentRevision
- taxonomyNode
- rubricMapping
- aiConfigVersion
- featureFlag
- auditEvent
- reviewOpsSummary

Those extra types are **reasonable engineering needs**, but they are not explicitly named in the blueprint and therefore should be formally approved or documented.

---

## 5.10 Async workflow handling — inherited

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

### Admin / CMS implication
Admin surfaces that inspect or control these workflows should expose those states explicitly, especially:
- Review Ops Dashboard
- Quality Analytics
- User Ops when support staff inspect stuck workflows
- Audit Logs when tracing failures
- AI Evaluation Config when understanding routing/threshold side effects

---

## 5.11 State architecture — inherited

The blueprint recommends:
- React + TypeScript
- route-based code splitting
- query library for server state
- lightweight client store for session/task-local state
- form library with schema validation
- design system as component package or shared module

It also defines state layers:
- server state
- local UI state
- persisted client state

### Admin / CMS implication
Admin should follow the same architecture:
- server state for content/config/analytics/logs
- local UI state for filters/drawers/tabs/table state
- persisted client state only where appropriate, such as saved UI preferences or unsent drafts

---

## 5.12 Validation rules — inherited

The blueprint explicitly requires:
- input validation should be immediate but calm
- scoring fields in review/admin must validate before submit
- required diagnostic/goal fields must show clear reason and fix path

### Admin / CMS implication
For admin:
- form errors should be immediate but not disruptive
- AI config forms should validate before save
- content builder required fields should show clear fix paths
- criteria/taxonomy forms should prevent invalid states before publish/save
- billing/user ops actions should validate before dangerous submission

---

## 5.13 Empty states — inherited

The blueprint says every empty state should guide action.

### Admin / CMS implication
Examples:
- no content items → create new content
- no revisions → explain revision history availability after first save/publish
- no taxonomy nodes → create first taxonomy entry
- no audit logs in filter range → adjust filters
- no risk cases → show healthy state, not dead space

---

## 5.14 Error states — inherited

The blueprint explicitly requires:
- retry
- save locally where possible
- “contact support” only for true blockers
- preserving user work in long tasks

Critical errors explicitly called out elsewhere include:
- writing draft save failure
- speaking audio upload failure
- evaluation timeout
- review purchase or entitlement mismatch

### Admin / CMS implication
Admin equivalents likely need explicit handling for:
- content save/publish failure
- revision restore failure
- config save failure
- flag toggle failure
- permission mismatch
- stale update conflict
- analytics fetch timeout
- audit log load failure

These admin examples are inferred from the general rule; the blueprint does not list them one by one.

---

## 5.15 Analytics instrumentation — inherited

The blueprint explicitly lists minimum tracked events, but they are learner-oriented:
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

### Admin / CMS implication
The blueprint does **not** explicitly define admin analytics events.

This creates an important gap.

At minimum, Admin / CMS probably needs internal instrumentation for:
- content created/updated/published/archived
- revision restored
- taxonomy changed
- criteria mapping changed
- AI config changed
- flag changed
- user role/entitlement changed
- billing action taken
- export performed
- audit log viewed

However, these are **not explicitly defined** in the blueprint and should be documented as admin analytics/audit requirements before implementation is finalized.

---

## 5.16 Frontend performance requirements — inherited

The blueprint explicitly requires:
- dashboard interactive within acceptable modern app standards on average broadband
- route transitions feel immediate for local navigation
- writing editor input latency stays low during long essays
- transcript view handles long content without jank
- tables in expert/admin areas support virtualization or efficient pagination where needed

Key performance focus areas include:
- writing editor
- waveform/transcript sync
- data-heavy admin tables
- diagnostic results and mock reports

### Admin / CMS implication
Admin performance priorities include:
- large content tables
- quality analytics tables/charts
- audit log browsing
- filter responsiveness
- version history browsing
- large-form authoring without lag

---

## 5.17 Security and privacy requirements — inherited

The blueprint explicitly requires:
- role-based route protection
- avoid exposing hidden admin/expert routes in learner bundles where practical
- signed upload flows for audio
- no sensitive scoring config exposed client-side
- secure handling of tokens and session refresh
- explicit consent messaging for audio capture where required

### Admin / CMS implication
For admin specifically:
- strict role-based protection is non-negotiable
- sensitive evaluation config must not leak into public or learner bundles
- billing/user data should be permission-gated
- audit views may require extra redaction
- feature flags and operational states must not be exposed in unauthorized contexts

---

## 5.18 QA checklist — inherited and directly relevant

The blueprint explicitly lists Admin critical path:
- content can be created
- revision history visible
- content can be published/unpublished
- AI config page loads and is permission-gated

### Admin / CMS implication
These four items are minimum acceptance gates for admin v1. They do not cover the full admin surface, but they do define what cannot be broken.

---

## 5.19 Release slicing recommendation — inherited

The blueprint explicitly recommends:
- Slice 5 includes expert console, admin/CMS, quality dashboards

### Admin / CMS implication
Admin / CMS is not part of the earliest learner slices in the current release recommendation. This increases the risk that admin operational detail remained under-specified and must be clarified before build.

---

## 5.20 Open frontend decisions from the blueprint that affect Admin / CMS

The blueprint explicitly lists open frontend decisions:
1. Rich text editor strategy for Writing
2. Waveform library choice for Speaking review
3. Real-time vs polling for evaluation state changes
4. Dark mode from v1 or later
5. Mobile authoring experience scope for long Writing tasks

### Admin / CMS implication
The following directly affect admin:
- rich text editor strategy affects Task Builder if model answers/rubric notes are rich
- real-time vs polling affects review ops, analytics freshness, and config/ops visibility
- dark mode impacts admin only if adopted platform-wide
- mobile authoring scope affects admin only if content editing on tablet/mobile is expected, which is not currently specified

---

## 6. Screen-by-screen supplement for Admin / CMS

## 6.1 Content Library

### 6.1.1 Explicitly defined in the blueprint
- route: `/admin/content`
- must support:
  - data table view
  - card view optional
  - saved filters
  - bulk actions
  - publish/archive states
  - revision indicators

### 6.1.2 Globally defined elsewhere that still applies
- admin is data-table heavy
- filters visible by default
- revision history must always be accessible
- keyboard navigation required
- visible focus states required
- table/filter interfaces must support loading/empty/error/partial/stale/permission states
- should use existing Table + FilterBar + StatusBadge patterns
- should tolerate partial data
- should support efficient pagination or virtualization where needed
- calm validation and actionable empty states still apply
- role-based protection required
- performance focus on large tables applies
- QA critical path indirectly depends on content being creatable and publishable from admin flow

### 6.1.3 Not specified enough yet
The blueprint does not resolve:
- content table columns
- whether table supports inline actions or row-level action menu
- which content types are listed together
- search behavior
- filter taxonomy
- saved filter sharing vs personal-only
- exact publish/archive lifecycle states
- whether draft/unpublished/review states exist
- whether card view is v1 or later
- revision indicator design and depth
- bulk action list
- permissions by role
- default sort order
- whether there is content ownership/author column
- whether archived items remain searchable by default
- whether content usage counts appear
- whether impacted learner tasks/mocks are shown before archive
- whether library supports column customization or exports

### 6.1.4 Temporary implementation assumptions only if product has not decided
These are not blueprint facts; they are fallback assumptions to unblock development:
- table-first surface in v1; card view can be deferred if needed
- filters visible in persistent FilterBar
- default sort by updated_at descending
- core states include draft, published, archived, with room for future expansion
- row click navigates to content detail/edit route
- bulk actions limited initially to publish/archive where permissions allow
- revisions accessible via row indicator and content detail

---

## 6.2 Task Builder

### 6.2.1 Explicitly defined in the blueprint
- routes:
  - `/admin/content/new`
  - `/admin/content/:id`
- for each content type support:
  - metadata entry
  - profession selection
  - criteria mapping
  - difficulty
  - estimated duration
  - model answer / rubric notes
  - versioning

### 6.2.2 Globally defined elsewhere that still applies
- revision/version history always accessible
- content forms must support loading/error/partial/permission states
- immediate but calm validation required
- accessible form labels required
- high contrast/focus states required
- should reuse ContentMetadataPanel and existing editor patterns where relevant
- design-system form controls should be reused
- role protection required
- preserve unsaved work where possible for long authoring sessions
- low-risk optimistic updates only
- no invention of new UI language

### 6.2.3 Not specified enough yet
The blueprint does not resolve:
- exact content types and per-type schemas
- whether authoring is one long form or multi-step flow
- whether there is autosave
- whether save draft exists separately from publish
- whether versioning happens automatically on every save or only on publish
- whether metadata differs by sub-test
- difficulty scale vocabulary
- estimated duration units/granularity
- model answer format
- rubric notes visibility
- whether content preview exists
- whether attachments/media/case-note blocks are structured
- whether multiple professions can map to one content item
- whether criteria mapping is required before publish
- whether edit locking or concurrent editing prevention exists
- whether content duplication/cloning exists
- whether there are validation rules around official OET structure
- whether there is a “deprecation reason” when content is retired

### 6.2.4 Temporary implementation assumptions only if product has not decided
- use one primary edit form with tabbed/sectioned layout
- explicit Save Draft and Publish actions if lifecycle supports it
- version history visible in-page via drawer or side panel
- metadata, criteria mapping, and notes are sectioned rather than separate routes
- required fields block publish, not necessarily interim draft save
- no collaborative live editing in v1

---

## 6.3 Content Revisions

### 6.3.1 Explicitly defined in the blueprint
- route: `/admin/content/:id/revisions`
- revision and version history always accessible
- content can be versioned

### 6.3.2 Globally defined elsewhere that still applies
- data-heavy views should be table-first where practical
- filters visible by default if revision lists become filterable
- loading/empty/error/partial/permission/stale states required
- should reuse VersionHistoryDrawer and RevisionDiffViewer patterns where applicable
- role-based protection required
- accessible table and diff patterns required
- QA critical path: revision history visible

### 6.3.3 Not specified enough yet
The blueprint does not resolve:
- revision fields (author, date, status, note, version label, publish state)
- compare workflow
- restore workflow
- whether restore creates a new revision
- diff granularity
- preview from revision
- whether revisions exist for unpublished drafts
- whether revision notes are mandatory
- whether revisions are immutable
- whether restoring a published revision republishes instantly or stages a draft
- permissions for restore/delete
- whether non-content configs share this pattern

### 6.3.4 Temporary implementation assumptions only if product has not decided
- revision list shows version label, updated by, updated at, state, and optional note
- revision compare opens diff view
- restore creates a new latest revision rather than mutating history
- no hard delete of revisions in v1
- revision history visible from content detail and standalone revisions route

---

## 6.4 Profession Taxonomy

### 6.4.1 Explicitly defined in the blueprint
- route: `/admin/taxonomy`
- admin/CMS includes Profession Taxonomy in IA
- learner and content surfaces rely on profession-specific behavior throughout the platform

### 6.4.2 Globally defined elsewhere that still applies
- OET-native principle applies
- professional tone applies
- filters visible by default for table/tree views
- loading/empty/error/partial/permission states required
- immediate but calm validation required
- version/revision access should be available if taxonomy edits affect production behavior
- role-based protection required

### 6.4.3 Not specified enough yet
The blueprint does not resolve:
- whether taxonomy is flat list, tree, or graph
- whether it covers professions only or other controlled vocabularies
- whether taxonomy values are soft-deletable/archive-only
- whether existing content references block deletion
- whether there is impact analysis before edit/archive
- whether labels, slugs, display order, descriptions, and aliases exist
- whether learner-facing copy can be edited here
- whether this page also controls target-country or organization options used in goal setup
- whether taxonomy is versioned
- whether taxonomy changes require reindex/regeneration jobs

### 6.4.4 Temporary implementation assumptions only if product has not decided
- start with a flat or lightly hierarchical profession taxonomy
- support create/edit/archive, but block destructive deletion where linked records exist
- show content-count usage indicator before archival
- keep taxonomy changes auditable

---

## 6.5 Rubrics / Criteria Mapping

### 6.5.1 Explicitly defined in the blueprint
- route: `/admin/criteria`
- admin IA includes Rubrics / Criteria Mapping
- product is aligned to official Writing criteria
- product is aligned to official Speaking linguistic and clinical communication criteria
- feedback is criterion-first

### 6.5.2 Globally defined elsewhere that still applies
- criterion-first principle is foundational
- OET-native fidelity is non-negotiable
- design system reuse required
- loading/error/partial/permission/stale states required
- validation must be clear and calm
- version/revision access should exist if mapping changes affect production scoring or feedback
- secure handling required because scoring config may be sensitive

### 6.5.3 Not specified enough yet
The blueprint does not resolve:
- whether this page edits official labels or only internal mappings
- whether Writing and Speaking are separated into tabs/views
- whether criteria can be activated/deactivated
- whether score bands or descriptors are editable
- whether weights or thresholds live here or in AI config
- whether criteria mapping is per profession, per sub-test, per content type, or global
- whether changes affect historical evaluations
- whether mapping changes require staged rollout
- whether reviewers see the same source definitions
- whether internal notes/explanations are stored per criterion
- whether Reading/Listening item tags belong here or elsewhere

### 6.5.4 Temporary implementation assumptions only if product has not decided
- separate Writing and Speaking views
- official criterion labels are protected; internal mapping/config fields are editable
- mappings are versioned and auditable
- destructive edits are blocked when in active production use
- historical evaluations are not silently rewritten without explicit reprocessing policy

---

## 6.6 AI Evaluation Config

### 6.6.1 Explicitly defined in the blueprint
- route: `/admin/ai-config`
- must show:
  - active model version
  - thresholds
  - confidence routing rules
  - experiment flags
  - prompt/config labels

### 6.6.2 Globally defined elsewhere that still applies
- trust-first principle applies strongly
- no sensitive scoring config exposed client-side
- explicit stale/loading/error states required
- optimistic updates only where low risk
- role-based route protection required
- auditability is especially important
- validation before submit required
- professional tone and conservative status treatment required

### 6.6.3 Not specified enough yet
The blueprint does not resolve:
- which fields are editable vs inspect-only
- whether there are separate environments
- whether config changes are immediately live
- whether there is approval flow
- whether active model version can be changed in UI
- threshold types and units
- exact confidence-routing outcomes
- relationship between experiment flags here and feature flags in `/admin/flags`
- prompt/config label meaning and schema
- rollback semantics
- whether validation includes simulation/test run
- whether previous config versions are compared in-line
- which admin roles can see prompts or model details
- whether Writing and Speaking configs are separate
- whether Reading/Listening evaluation config appears here too

### 6.6.4 Temporary implementation assumptions only if product has not decided
- treat high-risk fields as explicit-save, not autosave
- separate read-only metadata from editable thresholds/rules
- require change note on save
- always keep previous config versions accessible
- do not expose raw secret/provider credentials in frontend

---

## 6.7 Review Ops Dashboard

### 6.7.1 Explicitly defined in the blueprint
- route: `/admin/review-ops`
- admin role includes managing operational states
- quality analytics includes review SLA
- expert console includes review queue and reviewer workflows

### 6.7.2 Globally defined elsewhere that still applies
- data-table-heavy admin layout applies
- filters visible by default
- loading/error/partial/stale states required
- async workflow states are important
- role-based protection required
- high-trust operational visibility required
- performance requirements for large operational tables apply

### 6.7.3 Not specified enough yet
The blueprint does not resolve:
- whether this is dashboard-only or action-taking surface
- whether it includes reviewer assignment/reassignment
- whether it includes queue health, backlog, aging, and SLA breach counts
- whether it includes stuck review detection
- whether it includes human-review turnaround controls
- whether it includes temporary operational overrides
- whether reviewer availability appears here or only in expert schedule
- whether this page links deeply into expert queue or embeds queue segments
- whether admin can force-complete, cancel, reopen, or reassign reviews
- whether there are profession/sub-test filters
- whether it includes AI-confidence-based routing oversight
- whether it includes credit/refund issues for failed reviews

### 6.7.4 Temporary implementation assumptions only if product has not decided
- start as operational monitoring + drill-through surface, not full embedded expert console replacement
- include backlog, overdue, SLA risk, and status distribution at minimum
- allow drill-through into affected review/request records rather than heavy inline editing
- keep dangerous ops behind explicit confirmation

---

## 6.8 Quality Analytics

### 6.8.1 Explicitly defined in the blueprint
- route: `/admin/analytics/quality`
- must show:
  - AI-human disagreement
  - content performance
  - review SLA
  - feature adoption
  - risk cases

### 6.8.2 Globally defined elsewhere that still applies
- high-trust and professional tone required
- data-heavy admin patterns apply
- loading/empty/error/partial/stale states required
- should support efficient charts/tables without jank
- analytics should tolerate partial data
- visibility of uncertainty/freshness matters if metrics are delayed
- role protection required

### 6.8.3 Not specified enough yet
The blueprint does not resolve:
- metric formulas and source-of-truth systems
- segmentation by profession/sub-test/content type/time range
- chart vs table breakdown
- drill-down paths
- export/download availability
- whether metrics are near-real-time or warehouse-lagged
- what defines “content performance”
- what defines “feature adoption”
- what defines “risk case”
- whether disagreement is shown as percent, count, or distribution
- whether SLA is shown by reviewer, sub-test, or priority
- whether quality analytics includes cohort comparisons
- whether benchmark thresholds or alerts exist

### 6.8.4 Temporary implementation assumptions only if product has not decided
- use date filters and profession/sub-test filters as baseline analytics controls
- combine summary cards with drillable tables/charts
- show data freshness timestamp
- distinguish no-data from healthy-zero states
- keep metric definitions documented in UI help text or companion spec

---

## 6.9 User Ops

### 6.9.1 Explicitly defined in the blueprint
- route: `/admin/users`
- admin IA includes User Ops

### 6.9.2 Globally defined elsewhere that still applies
- role-based protection is critical
- loading/empty/error/partial/permission states required
- filters visible by default for large user lists
- secure handling of user data required
- accessible tables and forms required
- professional tone required
- partial data tolerance required

### 6.9.3 Not specified enough yet
The blueprint does not resolve:
- what user search supports (name, email, id, role, profession, subscription)
- whether learner and expert users share one list
- which actions are allowed:
  - role change
  - account suspend/reactivate
  - entitlement adjustment
  - goal reset
  - review credit adjustments
  - password/auth support actions
- whether impersonation is allowed
- what PII is visible
- whether user timeline/history is included
- whether user support notes exist
- whether admin can view submissions/evaluations directly from user page
- whether consent/privacy flags are surfaced
- whether actions require reason capture or approvals

### 6.9.4 Temporary implementation assumptions only if product has not decided
- start with read-first operational profile plus limited safe actions
- expose only necessary PII to authorized roles
- require confirmation and reason capture for dangerous account/entitlement changes
- link outward to relevant content/reviews/billing records rather than overloading the page

---

## 6.10 Billing Ops

### 6.10.1 Explicitly defined in the blueprint
- route: `/admin/billing`
- admin IA includes Billing Ops
- learner billing surface includes plan, renewal, review credits, invoices, and extras

### 6.10.2 Globally defined elsewhere that still applies
- secure handling and role-based protection are critical
- sensitive financial actions should avoid optimistic updates
- loading/error/partial/permission states required
- filters visible by default for large operational lists
- auditability is important
- professional tone required

### 6.10.3 Not specified enough yet
The blueprint does not resolve:
- whether billing ops is read-only or editable
- whether admins can issue refunds
- whether admins can adjust review credits
- whether admins can change plans or grant manual extensions
- invoice search/export capabilities
- payment failure handling
- integration with provider-specific objects
- finance-vs-support role separation
- whether subscription changes sync instantly
- whether billing disputes/notes are tracked
- whether wallet/credits adjustments require double confirmation

### 6.10.4 Temporary implementation assumptions only if product has not decided
- start with searchable subscription/invoice/credit operations
- separate read-only financial records from editable support actions
- require explicit confirmation and audit reason for any credit/plan adjustment
- avoid exposing provider secrets or raw financial payloads in frontend

---

## 6.11 Feature Flags

### 6.11.1 Explicitly defined in the blueprint
- route: `/admin/flags`
- admin role manages feature flags and operational states
- admin IA includes Feature Flags
- AI Evaluation Config includes experiment flags

### 6.11.2 Globally defined elsewhere that still applies
- role-based protection required
- high-risk changes should not use optimistic updates casually
- loading/error/partial/permission states required
- auditability is required
- professional tone and conservative status presentation required

### 6.11.3 Not specified enough yet
The blueprint does not resolve:
- whether operational states live on the same page as feature flags
- flag scope hierarchy (global, by role, by profession, by cohort, by environment)
- schedule-based activation
- staged rollout percentages
- dependency handling
- approval requirements
- kill-switch behavior
- ownership metadata
- description/docs fields
- relationship to experiment flags inside AI config
- whether some flags are read-only in production
- whether learner/expert/admin flags are separated

### 6.11.4 Temporary implementation assumptions only if product has not decided
- show flags in a table with owner/description/status/scope fields
- distinguish product flags from operational kill switches
- require explicit confirmation for high-impact toggles
- surface environment clearly
- audit every change

---

## 6.12 Audit Logs

### 6.12.1 Explicitly defined in the blueprint
- route: `/admin/audit-logs`
- admin IA includes Audit Logs

### 6.12.2 Globally defined elsewhere that still applies
- data-table-heavy admin layout applies
- filters visible by default
- role-based protection required
- sensitive-data exposure should be minimized
- loading/empty/error/partial/permission states required
- performance requirements for large tables apply
- auditability is a trust requirement across config and ops changes

### 6.12.3 Not specified enough yet
The blueprint does not resolve:
- event coverage
- retention window
- search and filter schema
- actor/resource/action model
- before/after diff visibility
- export permissions
- redaction rules
- whether end-user actions appear or only admin/system actions
- correlation IDs for tracing
- timezone display rules
- whether immutable append-only guarantees are surfaced
- whether alert-worthy events are highlighted

### 6.12.4 Temporary implementation assumptions only if product has not decided
- implement append-only audit list with strong search/filter controls
- default fields include timestamp, actor, action, resource type, resource id, result
- allow drill into event detail with redacted before/after payload where appropriate
- separate audit visibility permissions from ordinary admin permissions

---

## 7. Cross-cutting admin models and unresolved contracts

## 7.1 Content object model
The blueprint names `contentItem` as a strongly typed entity, but does not define:
- required base fields
- per-sub-test variants
- publication state machine
- ownership metadata
- archive semantics
- dependency graph to mocks/drills/diagnostics/model answers

This is a major contract gap. Admin screens cannot be finalized without it.

### Minimum content-model decisions needed
- content type taxonomy
- field schema by type
- required vs optional fields
- lifecycle states
- relation to profession/sub-test/criteria
- relation to model answers and rubric notes
- revision/version linkage

---

## 7.2 Versioning model
The blueprint clearly expects versioning and revisions, but does not define:
- save-to-version behavior
- publish-to-version behavior
- revision labels
- restore semantics
- branching/forking support
- immutable history rules

Without this, Content Library, Task Builder, and Content Revisions remain partially blocked.

---

## 7.3 Taxonomy model
The blueprint names Profession Taxonomy but does not define:
- hierarchy
- controlled vocabularies beyond profession
- lifecycle/change constraints
- usage references

This affects content authoring, learner goals, analytics segmentation, and possibly feature-flag scoping.

---

## 7.4 Criteria and rubric governance model
The blueprint is criterion-first, but still leaves open:
- which labels are canonical and locked
- which elements are editable
- where score bands live
- where routing/weights live
- versioning of rubric mappings
- change impact on historical data

This is central to platform trust.

---

## 7.5 Content publishing and deprecation workflow
The blueprint mentions publish/archive states and versioning, but does not define:
- draft review
- publish approvals
- archive constraints
- scheduled publish/unpublish
- deprecation notes
- impact checks before archive

Admin needs a clear lifecycle contract before implementation is finalized.

---

## 7.6 AI config governance model
The blueprint exposes AI config concepts, but not governance:
- who can edit what
- what is environment-specific
- what is staged
- what requires approval
- what can be rolled back instantly
- what gets audited
- how experiment flags interact with normal flags

This is a high-risk area and must be explicitly specified.

---

## 7.7 Review operations model
Admin Review Ops depends on unresolved definitions for:
- SLA rules
- queue ownership model
- reassignment authority
- stuck review handling
- conflict resolution
- interaction with expert schedule
- interaction with refunds/credits for review failures

This is broader than analytics and needs product/ops clarification.

---

## 7.8 User operations model
User Ops is mostly unspecified. Needed decisions include:
- support-safe action list
- role/entitlement boundaries
- privacy-safe visibility
- PII masking/redaction
- action confirmations
- support notes / internal commentary
- audit requirements
- self-serve vs admin-override boundaries

---

## 7.9 Billing operations model
Billing Ops depends on unresolved business-policy choices:
- what support/admin can change
- refund policy representation
- review-credit adjustment policy
- payment-provider artifact visibility
- permissions by role
- financial audit requirements
- export/reporting needs

---

## 7.10 Feature-flag and operational-state model
The blueprint explicitly includes both feature flags and operational states, but does not define:
- their separation
- their data model
- rollout scopes
- environment strategy
- emergency kill-switch policy
- approval flow
- dependency handling

Admin cannot safely own platform rollout without this.

---

## 7.11 Audit-log model
Audit Logs are route-defined but not modeled. Needed decisions:
- event taxonomy
- actor taxonomy
- resource taxonomy
- immutable retention policy
- redaction and privacy rules
- export permissions
- traceability conventions
- timezone conventions
- data freshness and indexing strategy

---

## 7.12 Permissions and separation of duties
The blueprint says role-based route protection, but Admin / CMS likely needs **finer-grained permissions** than one broad “admin” role.

Potentially separate responsibilities may include:
- content manager
- taxonomy/rubric manager
- AI config manager
- review ops manager
- billing/support ops
- super admin / security admin
- audit viewer

The blueprint does not define this. If not resolved, engineering may over-broaden admin access by accident.

---

## 7.13 Search and filtering model across admin
The blueprint says filters visible by default and data-table heavy, but does not define:
- which screens have global search
- shared filter primitives
- whether filters persist in URL
- saved-filter ownership
- cross-screen search consistency
- export behavior relative to filters

A consistent admin filtering contract is needed.

---

## 7.14 Device support and responsive expectations for admin
The overall product is web-first and must be highly usable on mobile, but the admin blueprint is explicitly data-table heavy.

This creates an ambiguity:
- Is Admin / CMS desktop-primary with responsive degradation only?
- Or must full admin workflows work well on tablet/mobile?

The blueprint does not exempt admin from responsiveness, but it also does not define mobile admin scope.

### Safe interpretation
Admin should be treated as **desktop-first** unless product explicitly requires robust mobile admin operations.

---

## 7.15 Notifications, background refresh, and stale-state behavior
The blueprint requires stale-state warnings and async workflow handling, but does not define:
- auto-refresh cadence
- notification surfaces
- background job completion toasts
- conflict detection for concurrent edits
- last-updated timestamps
- websocket vs polling for ops dashboards

Admin screens handling live operations need this clarified.

---

## 8. Implementation guidance to the developer

## 8.1 Build from the existing architecture
Use the recommended architecture already in the blueprint:
- React + TypeScript
- route-based code splitting
- query library for server state
- lightweight local store for transient UI state
- schema-validated forms
- shared design system module/package

Do not create separate ad hoc admin architectural patterns unless justified.

## 8.2 Reuse existing domain language
Use the platform’s existing domain vocabulary:
- profession
- sub-test
- criterion
- content item
- review request
- evaluation
- subscription
- credits
- revision
- version
- publish/archive

Avoid introducing alternative terms unless product approves them.

## 8.3 Avoid premature product invention
If the blueprint does not specify behavior, do one of the following:
1. mark it as open and request product decision
2. use a conservative fallback assumption clearly documented in code/spec
3. avoid shipping the risky action until clarified

Do **not** silently invent:
- lifecycle states
- config semantics
- audit coverage
- financial permissions
- learner-data exposure
- scoring governance behavior

## 8.4 Prefer explicit interim states over hidden uncertainty
For operations that are async, sensitive, or approval-like:
- show pending state
- show last updated time
- show stale warning when needed
- show clear success/failure outcomes
- preserve drafts/work whenever possible

This is especially important for:
- content publishing
- revision restore
- AI config save
- flag changes
- billing/user operational changes
- analytics/report refresh

---

## 9. Readiness summary

## 9.1 Safe to build now from the blueprint
These are safe to start structurally because the blueprint or global rules clearly support them:
- Admin routes and shell structure
- table-first admin layout
- filters visible by default
- Content Library baseline with table, saved filters, bulk actions, states, revision indicators
- Task Builder baseline sections:
  - metadata
  - profession selection
  - criteria mapping
  - difficulty
  - estimated duration
  - model answer / rubric notes
  - versioning entry point
- revision/version-history access patterns
- AI Config baseline readouts:
  - active model version
  - thresholds
  - confidence routing rules
  - experiment flags
  - prompt/config labels
- Quality Analytics baseline categories:
  - AI-human disagreement
  - content performance
  - review SLA
  - feature adoption
  - risk cases
- permission gating
- loading/empty/error/partial/stale state system
- accessibility baseline
- audit/logging hooks in frontend flows

## 9.2 Needs product/design decision before “final” implementation
These should not be finalized without explicit decisions:
- content-type schema model
- publish/archive/version lifecycle
- revision restore semantics
- taxonomy structure and scope
- official criteria editability rules
- AI config editability/governance
- review ops interactivity level
- quality metric definitions
- user ops capability set
- billing ops capability set
- feature flag scoping and environment model
- audit event taxonomy and retention
- fine-grained admin permission model
- desktop vs mobile admin support scope
- saved-filter ownership/sharing behavior
- dangerous-action confirmations and approval rules

---

## 10. Final instruction

Build Admin / CMS as an **operational control system for a high-trust OET assessment platform**, not as a generic back office.

The most important outcomes are:
- content and criteria are managed without breaking OET fidelity
- version history is always reachable
- operational and quality visibility are trustworthy
- sensitive config and financial actions are permission-gated and auditable
- the UI reuses the **current design system and existing platform components**
- engineering does **not invent new design language or product behavior** where the blueprint is silent

This file is intentionally written to help engineering begin route creation, component planning, and technical design immediately while still surfacing the unresolved product decisions that need confirmation.
