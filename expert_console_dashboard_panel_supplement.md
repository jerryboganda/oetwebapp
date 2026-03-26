# OET Platform — Expert Console Dashboard / Panel Supplement
_Last updated: 2026-03-25_

## 1. Document purpose

This file is a **developer-facing supplement** to the existing product blueprint for the **Expert Console** surface only.

It consolidates three things for the expert-facing product surface:

1. **Explicitly defined in the blueprint**  
2. **Globally defined elsewhere in the blueprint but still applicable to the Expert Console**  
3. **Not specified enough yet** (open decisions, missing detail, implementation gaps, and ambiguity that must be resolved or consciously assumed)

This file is intended to help engineering start implementation without inventing product behavior that was never approved.

---

## 2. Non-negotiable instruction to the frontend developer

### 2.1 Use the current design system and existing product language
The Expert Console must be built using the **current platform design system, shared primitives, and existing domain components** already defined in the blueprint.

**Do not invent a new visual language, new interaction model, new navigation pattern, or unrelated UI components unless product/design explicitly approves them.**

Use the existing system first:
- AppShell
- Sidebar
- TopNav
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
- Timer
- Progress bar
- Stepper
- Status badge
- Score range badge
- Confidence badge
- Criterion chip
- Review comment anchor
- Submission card

Relevant domain-specific components already defined and expected to be reused where applicable:
- ReviewerRubricPanel
- CriterionBreakdownCard
- ContentMetadataPanel
- VersionHistoryDrawer

If a needed expert-console-specific component is missing, it should be created **inside the same design system language**, not as a visually unrelated one-off.

### 2.2 Do not invent product behavior where the blueprint is silent
If the blueprint does not define:
- exact scoring interaction rules
- reviewer assignment logic
- SLA escalation behavior
- calibration formulas
- queue sort precedence
- learner detail depth
- metrics formulas
- availability scheduling rules

then those should be treated as **open decisions**, not silently invented.

### 2.3 Treat this document as a supplement, not a replacement
This file does **not replace** the original blueprint. It clarifies and operationalizes it for the Expert Console surface.

---

## 3. Expert Console scope covered by this file

The blueprint defines the following Expert Console information architecture:

- Review Queue
- Writing Review Workspace
- Speaking Review Workspace
- Assigned Learners
- Calibration Center
- Schedule / Availability
- Performance Metrics

The blueprint defines the following Expert Console routes:

- `/expert/queue`
- `/expert/review/writing/:reviewRequestId`
- `/expert/review/speaking/:reviewRequestId`
- `/expert/learners/:learnerId`
- `/expert/calibration`
- `/expert/metrics`
- `/expert/schedule`

This supplement covers all of the above.

---

## 4. Canonical source material extracted from the blueprint

## 4.1 Expert role and capabilities — explicitly defined

The expert/reviewer role has the following explicitly defined capabilities:
- access assigned review queue
- review Writing submissions
- review Speaking submissions
- use calibration tools
- view learner context needed for review

This means the Expert Console is not a generic staff dashboard. It is an operational review surface for assessment-quality review work.

---

## 4.2 Expert Console IA — explicitly defined

The blueprint explicitly defines the Expert Console IA as:
- Review Queue
- Writing Review Workspace
- Speaking Review Workspace
- Assigned Learners
- Calibration Center
- Schedule / Availability
- Performance Metrics

This is the expert-facing navigation scope currently in product definition.

---

## 4.3 Expert Console route map — explicitly defined

The blueprint explicitly defines the following routes:

| Area | Route |
|---|---|
| Review Queue | `/expert/queue` |
| Writing Review Workspace | `/expert/review/writing/:reviewRequestId` |
| Speaking Review Workspace | `/expert/review/speaking/:reviewRequestId` |
| Assigned Learners | `/expert/learners/:learnerId` |
| Calibration Center | `/expert/calibration` |
| Performance Metrics | `/expert/metrics` |
| Schedule / Availability | `/expert/schedule` |

These routes should be treated as the current expected route contract unless product/navigation changes are approved.

---

## 4.4 Expert Console layout and UX foundation — explicitly defined elsewhere but directly applicable

The blueprint explicitly states for the Expert Console:

- **dense information layout**
- **split panes for submission vs rubric**
- **keyboard-first workflows for fast reviewing**

This is a major product requirement, not a visual suggestion.

Implications:
- the console should optimize for operational efficiency over spacious marketing-style layout
- review workspaces should avoid unnecessary clicks
- rubric interaction should be accessible without losing view of the submission
- keyboard interaction is a first-class path, not a later enhancement

---

## 4.5 Review Queue — explicitly defined in the blueprint

### Route
`/expert/queue`

### Required columns
The blueprint explicitly requires the Review Queue to show:
- review id
- learner
- profession
- sub-test
- AI confidence
- priority
- SLA due
- assigned reviewer
- status

### Required filters
The blueprint explicitly requires:
- Writing/Speaking
- profession
- priority
- overdue
- confidence band
- assigned/unassigned

### What is explicitly known
The queue is a filterable operational list where the reviewer can see core review metadata and triage work.

### What is **not** explicitly defined at this layer
The blueprint does not explicitly define:
- default sort order
- whether queue is table-only or hybrid table/cards
- batch actions
- claim/unclaim interaction
- auto-assignment behavior
- pagination vs infinite scroll vs virtualization
- row click behavior vs side preview
- status taxonomy beyond “status” existing
- whether experts may see unassigned items or only assigned items
- whether SLA urgency should use color severity rules
- whether review id is human-readable or system ID

These belong in the under-specified section later in this file.

---

## 4.6 Writing Review Workspace — explicitly defined in the blueprint

### Route
`/expert/review/writing/:reviewRequestId`

### Layout
The blueprint explicitly requires:
- case notes
- learner response
- AI draft feedback
- rubric entry panel
- final comment composer
- send/rework controls

### Requirements
The blueprint explicitly requires:
- keyboard shortcuts
- anchored comment support
- save draft review
- SLA visibility

### What is explicitly known
This is a reviewer workspace for reviewing Writing submissions with both submission content and rubric feedback tools available.

### What is **not** explicitly defined at this layer
The blueprint does not explicitly define:
- exact rubric scoring input UI
- whether criterion scoring is numeric, banded, descriptive, or hybrid
- whether AI draft feedback is editable inline or side-by-side only
- whether comments can attach to case notes as well as learner response
- exact draft-save lifecycle
- rework meaning and downstream system behavior
- final comment length limits or templates
- auto-save for review drafts
- review completion validation rules beyond “scoring fields in review/admin must validate before submit”
- conflict handling if multiple reviewers open same review

These are under-specified.

---

## 4.7 Speaking Review Workspace — explicitly defined in the blueprint

### Route
`/expert/review/speaking/:reviewRequestId`

### Layout
The blueprint explicitly requires:
- role card
- audio player/waveform
- transcript
- AI flags
- rubric panel
- final response panel

### Requirements
The blueprint explicitly requires:
- timestamp anchoring
- playback speed controls
- side-by-side AI and human notes

### What is explicitly known
This is a reviewer workspace for evaluation of Speaking submissions using audio, transcript, role card context, AI findings, and rubric scoring.

### What is **not** explicitly defined at this layer
The blueprint does not explicitly define:
- exact transcript segmentation model
- whether AI flags are reviewer-editable
- exact timestamp comment mechanics
- allowed playback speed options
- whether waveform and transcript are synchronized automatically
- whether reviewers can relabel or dismiss AI flags
- whether final response panel contains criterion summary, free text only, or structured template
- whether audio download is allowed
- what happens if transcript is delayed, partial, or missing
- whether reviewers can compare multiple takes/attempts if they exist

These are under-specified.

---

## 4.8 Calibration Center — explicitly defined in the blueprint

### Route
`/expert/calibration`

### Views
The blueprint explicitly requires:
- benchmark cases
- reviewer alignment scores
- disagreements
- notes/history

### What is explicitly known
The Calibration Center exists to support reviewer quality and alignment.

### What is **not** explicitly defined at this layer
The blueprint does not explicitly define:
- how alignment scores are calculated
- whether benchmark cases are Writing only, Speaking only, or both
- whether disagreements are case-level, criterion-level, or score-level
- whether notes/history is personal, team-wide, or both
- whether reviewers can submit calibration responses directly in this screen
- whether calibration is mandatory, scheduled, or ad hoc
- whether performance consequences are shown here
- whether calibration data is read-only or interactive

These are under-specified.

---

## 4.9 Assigned Learners — explicitly defined only at IA + route level

### Route
`/expert/learners/:learnerId`

### Explicitly known
The blueprint includes this route in the Expert Console IA and route map.

The reviewer role capabilities explicitly state:
- view learner context needed for review

Therefore, it is explicit that the reviewer needs some learner context and that an Assigned Learners / learner detail area exists.

### What is **not** explicitly defined at screen level
The blueprint does not explicitly define:
- screen layout
- learner profile fields
- attempt history visibility
- goal visibility
- readiness visibility
- review history visibility
- privacy boundaries on learner data
- whether this page is read-only
- what “assigned” means operationally
- whether reviewers can message learners
- whether reviewer notes about a learner are supported

This area is heavily under-specified.

---

## 4.10 Schedule / Availability — explicitly defined only at IA + route level

### Route
`/expert/schedule`

### Explicitly known
The route and IA exist:
- Schedule / Availability

### What is **not** explicitly defined
The blueprint does not define:
- calendar view vs list view
- availability slot model
- timezone behavior
- recurring availability
- blackout dates
- integration with SLA forecasting
- reviewer working hours
- booking logic
- conflict resolution
- whether experts self-manage or admins manage availability
- whether schedule affects assignment routing

This area is under-specified.

---

## 4.11 Performance Metrics — explicitly defined only at IA + route level

### Route
`/expert/metrics`

### Explicitly known
The route and IA exist:
- Performance Metrics

### What is **not** explicitly defined
The blueprint does not define:
- which metrics are shown
- whether metrics are personal, team, or blended
- whether SLA compliance is included
- whether quality disagreement is included
- whether calibration trends are included
- whether productivity metrics are included
- date filtering
- cohort comparison
- export behavior
- visibility restrictions

This area is under-specified.

---

# 5. Global blueprint requirements that also apply to the Expert Console

This section collects requirements defined elsewhere in the blueprint that still apply to expert-facing implementation.

These are **not optional** simply because they were not repeated under section 9.

---

## 5.1 Product mission and frontend mission — inherited

The overall platform mission is to be:
- trusted
- profession-specific
- assessment-grade
- criterion-based
- fast with AI
- trustworthy with human review

The frontend mission is to make the platform feel:
- clear
- high-trust
- exam-focused
- efficient for time-poor healthcare professionals
- stable under long writing and audio workflows
- understandable even when scoring is probabilistic

### Expert Console implication
The reviewer surface must support:
- trustworthy review workflows
- clarity around AI confidence and AI assistance
- criterion-based assessment structure
- professional operational tone
- stability during long Writing and Speaking reviews

---

## 5.2 Product principles — inherited

The blueprint defines platform-wide UI principles:

1. **OET-native, not generic ESL**
2. **Criterion-first**
3. **Practice-first**
4. **Trust-first**
5. **Time-poor user UX**
6. **Professional tone**

### Expert Console implication
For the Expert Console:
- review screens must remain OET-specific
- rubric and feedback must be criterion-first
- AI suggestions must never masquerade as authoritative final judgments
- review operations must be fast and obvious
- language must remain professional and educational

---

## 5.3 Visual tone — inherited

The blueprint defines the visual tone as:
- clinical
- clean
- high-trust
- no childish gamification
- progress should feel motivating but credible
- use status colors conservatively; scoring risk should not feel alarmist unless necessary

### Expert Console implication
The Expert Console should:
- look operational and credible
- avoid decorative or playful patterns
- use urgency/status color sparingly and deliberately
- not create false drama around confidence, disagreements, or risk unless operationally justified

---

## 5.4 Accessibility — inherited and required from first release

The blueprint requires from first release:
- keyboard navigation for all core paths
- visible focus states
- accessible form labels and validation
- high contrast compliance
- support for screen readers on dashboard, forms, results pages
- captions/transcript access where relevant
- scalable typography
- reduced-motion preference support

### Expert Console implication
At minimum:
- review queue filtering and table navigation must be keyboard accessible
- Writing/Speaking review workspaces must support keyboard review flows
- rubric forms must have accessible labels and validation
- transcript and waveform controls must be accessible where possible
- visible focus states must exist on review actions
- reduced motion should be respected
- screen-reader announcements should exist for critical review state changes where practical

---

## 5.5 Design system primitives — inherited

The following system primitives remain applicable to expert surfaces where relevant:
- AppShell
- Sidebar
- TopNav
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
- Timer
- Progress bar
- Stepper
- Status badge
- Score range badge
- Confidence badge
- Criterion chip
- Review comment anchor
- Submission card

### Expert Console implication
The queue and metrics surfaces should prefer:
- Table
- FilterBar
- Status badge
- Confidence badge
- Empty/error/retry states

The review workspaces should prefer:
- AppShell
- split panes using existing layout primitives
- Audio player / Waveform viewer / Transcript viewer
- ReviewerRubricPanel
- Review comment anchor
- Criterion chips / breakdown cards where appropriate

---

## 5.6 Domain-specific components — inherited

Relevant domain-specific components already defined:
- ReviewerRubricPanel
- CriterionBreakdownCard
- ContentMetadataPanel
- VersionHistoryDrawer

### Expert Console implication
These should be reused before inventing bespoke expert-console alternatives.

---

## 5.7 Component state support — inherited

The blueprint states every component must support:
- loading
- empty
- success
- partial data
- error
- permission denied where relevant
- stale data warning where relevant

### Expert Console implication
This applies to:
- review queue
- reviewer workspaces
- learner detail pages
- calibration views
- schedule views
- metrics views

Examples:
- partial transcript data in Speaking review
- stale queue data warning
- permission denied on reviewer-unavailable pages
- empty calibration history
- error state on review save failure

---

## 5.8 General integration rules — inherited

The blueprint requires:
- all core pages must tolerate partial data
- optimistic updates only where low risk
- explicit loading and stale states for evaluations, study plans, and reviews
- polling or subscription support for async evaluation states

### Expert Console implication
The Expert Console must tolerate partial data such as:
- transcript not yet fully available
- AI feedback missing
- learner context partially loaded
- outdated queue data
- review response save status not yet confirmed

Optimistic updates should be limited to low-risk actions only. High-risk actions like final review submission should not assume success silently.

---

## 5.9 Strongly typed frontend entities — inherited

The blueprint requires the frontend to strongly type:
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

### Expert Console implication
The most relevant expert-console entities are:
- user
- profession
- subtest
- criterion
- contentItem
- attempt
- evaluation
- criterionScore
- feedbackItem
- reviewRequest
- learnerGoal
- readinessSnapshot

The console should be implemented against typed domain models, not loose ad hoc objects.

---

## 5.10 Async workflow handling — inherited

The blueprint explicitly says the following flows are async and need interim states:
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

### Expert Console implication
The Expert Console must handle async state for:
- reviews waiting on AI draft feedback
- speaking reviews waiting on transcript or AI flags
- review submission completion
- possibly downstream report generation after review completion

These async states must be visible and not hidden behind silent spinners.

---

## 5.11 State architecture — inherited

The blueprint recommends:
- React + TypeScript
- route-based code splitting
- query library for server state
- lightweight client store for session/task-local state
- form library with schema validation
- design system as component package or shared module

State layers:
- server state
- local UI state
- persisted client state where appropriate

### Expert Console implication
The review workspaces likely need:
- server state for submission, AI findings, rubric data, SLA data
- local state for panel visibility, playback state, selected anchors, unsaved comments
- persisted draft state where supported for saved draft reviews

---

## 5.12 Validation rules — inherited

The blueprint says:
- input validation should be immediate but calm
- scoring fields in review/admin must validate before submit
- required diagnostic/goal fields must show clear reason and fix path

### Expert Console implication
For the Expert Console:
- rubric/scoring fields must validate before final review submission
- validation should explain exactly what must be corrected
- validation should not be aggressive, noisy, or ambiguous

---

## 5.13 Empty states — inherited

The blueprint says every empty state should guide action.

### Expert Console implication
Examples:
- no assigned reviews -> explain queue state and next operational action
- no benchmark cases -> explain calibration availability
- no learner context -> explain what is unavailable
- no metrics yet -> explain when metrics will populate

---

## 5.14 Error states — inherited

The blueprint says error states must support:
- retry
- save locally where possible
- “contact support” route only for true blockers
- preserving user work in long tasks

Critical errors explicitly called out platform-wide:
- writing draft save failure
- speaking audio upload failure
- evaluation timeout
- review purchase or entitlement mismatch

### Expert Console implication
Equivalent expert-console critical errors include:
- draft review save failure
- review submission timeout
- transcript or AI feedback retrieval timeout
- rubric submit failure
- queue refresh failure
- permission mismatch / review ownership conflict

The important inherited principle is: **preserve work where possible**.

---

## 5.15 Analytics instrumentation — inherited

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

Every tracked event should include where relevant:
- user id
- profession
- sub-test
- content id
- attempt id
- evaluation id
- mode
- device type
- timestamp

### Expert Console implication
Although the blueprint’s analytics list is learner-heavy, the expert console should still instrument expert-specific operational events if analytics exists there. The blueprint does **not** explicitly enumerate those expert events, so those are under-specified, but the global instrumentation discipline still applies.

At minimum, expert events should carry relevant domain identifiers consistently.

---

## 5.16 Frontend performance requirements — inherited

Targets include:
- route transitions feel immediate for local navigation
- transcript view handles long content without jank
- tables in expert/admin areas support virtualization or efficient pagination where needed

Key performance focus areas explicitly include:
- waveform/transcript sync
- data-heavy admin tables

### Expert Console implication
Strongly relevant expert-console performance areas:
- queue responsiveness
- transcript rendering for long Speaking submissions
- waveform/transcript synchronization
- rubric interaction without lag
- efficient filtering and navigation across review lists

---

## 5.17 Security and privacy requirements — inherited

Required:
- role-based route protection
- avoid exposing hidden admin/expert routes in learner bundles where practical
- signed upload flows for audio
- no sensitive scoring config exposed client-side
- secure handling of tokens and session refresh
- explicit consent messaging for audio capture where required

### Expert Console implication
The Expert Console must have:
- strict role-based access control
- no learner-surface user access
- no leakage of admin-only controls/config
- controlled access to sensitive review data
- careful privacy boundaries around learner information

---

## 5.18 QA checklist — inherited and directly relevant

The blueprint explicitly defines expert critical path QA:
- reviewer can filter queue
- reviewer can complete writing review
- reviewer can complete speaking review
- reviewer can save draft without losing comments

These are minimum acceptance paths and should be treated as release-critical.

---

## 5.19 Release slicing recommendation — inherited

The blueprint places:
- expert console
- admin/CMS
- quality dashboards

into **Slice 5**

### Expert Console implication
This suggests the expert console was intended to arrive after learner-facing slices, not necessarily because it is unimportant, but because it depends on earlier content, attempt, review-request, and evaluation foundations.

---

# 6. Screen-by-screen inherited requirements + missing detail

This section turns the global requirements into per-screen implementation implications and surfaces unresolved questions.

---

## 6.1 Review Queue

### 6.1.1 Explicitly defined in the blueprint
- route: `/expert/queue`
- columns:
  - review id
  - learner
  - profession
  - sub-test
  - AI confidence
  - priority
  - SLA due
  - assigned reviewer
  - status
- filters:
  - Writing/Speaking
  - profession
  - priority
  - overdue
  - confidence band
  - assigned/unassigned

### 6.1.2 Globally defined elsewhere that still applies
- must use dense operational layout
- keyboard navigation required
- visible focus states required
- table/filter interfaces must support loading/empty/error/partial/stale/permission states
- should use existing Table + FilterBar + StatusBadge + ConfidenceBadge patterns
- should tolerate partial data
- should show stale-state warning when queue data is outdated
- must be role-protected
- should support efficient pagination or virtualization where needed
- calm validation and actionable empty states still apply
- QA critical path: reviewer can filter queue

### 6.1.3 Not specified enough yet
The blueprint does not resolve:
- default sort order (e.g. SLA soonest, priority highest, assigned first, AI confidence lowest/highest)
- whether queue supports bulk actions
- whether reviewer can self-assign items
- whether unassigned items are visible to all reviewers
- whether row click opens full page or side preview
- whether overdue state has distinct visual treatment
- whether queue data auto-refreshes or needs manual refresh
- whether filters persist in URL/query params
- whether search exists
- whether queue supports saved views
- whether AI confidence is numeric, banded label, color chip, or hybrid
- whether review id should be copyable/human-friendly
- whether status values are standardized and what they are
- whether assigned reviewer is editable from queue
- whether SLA due shows timestamp, relative time, or both
- whether permission model allows only assigned reviews to open
- whether queue should expose attempts waiting on transcript/AI prep

### 6.1.4 Temporary implementation assumptions only if product has not decided
These are not blueprint facts; they are fallback assumptions to unblock development:
- default sort by SLA due ascending, then priority descending
- queue implemented as table-first surface
- filters reflected in URL state
- row click navigates to the review workspace
- no bulk actions in v1 unless explicitly approved
- overdue uses a visible but conservative status treatment
- queue refreshes on interval and also supports manual refresh

---

## 6.2 Writing Review Workspace

### 6.2.1 Explicitly defined in the blueprint
- route: `/expert/review/writing/:reviewRequestId`
- layout:
  - case notes
  - learner response
  - AI draft feedback
  - rubric entry panel
  - final comment composer
  - send/rework controls
- requirements:
  - keyboard shortcuts
  - anchored comment support
  - save draft review
  - SLA visibility

### 6.2.2 Globally defined elsewhere that still applies
- split-pane review layout required by expert UX foundation
- criterion-first principle applies
- professional tone applies
- loading/partial/error/stale states required
- form validation must occur before submit
- preserve reviewer work where possible
- role-based protection required
- high-trust presentation of AI support required
- immediate but calm validation required
- keyboard navigation and focus states required
- design system reuse required
- use ReviewerRubricPanel and Review comment anchor patterns where applicable
- long-task stability required

### 6.2.3 Not specified enough yet
The blueprint does not resolve:
- exact six-criterion scoring UI for Writing review
- whether scores are required for all criteria before final submit
- whether rubric allows draft partial completion
- whether AI draft feedback is editable, adoptable, or reference-only
- whether anchored comments attach to character ranges, paragraphs, or sentence blocks
- whether reviewers can add global comments and anchored comments together
- what “rework” means:
  - send back to AI?
  - send back to admin?
  - return to queue?
  - request another pass?
- whether case notes panel is collapsible
- whether learner response supports side-by-side diff against model answer or prior attempt
- whether there are reviewer templates/snippets
- whether comments auto-save or only manual save draft
- whether multiple reviewers can access concurrently
- whether there is review locking or claim ownership
- what SLA visibility means:
  - fixed banner?
  - header metadata?
  - countdown?
- whether final comment composer supports markdown/rich text/plain text
- whether rubric scoring requires justification text
- whether send action triggers learner-visible notification immediately
- whether there is preview-before-send
- whether the reviewer can see learner goal, exam date, previous attempts, and prior feedback inline

### 6.2.4 Temporary implementation assumptions only if product has not decided
- all required rubric fields validate before final send
- anchored comments attach to selected response text ranges or paragraph blocks
- AI draft feedback is reference-only in v1, not source-of-truth
- save draft is explicit and optionally auto-saved locally
- SLA shown persistently in workspace header
- rework remains disabled or hidden until product defines workflow semantics

---

## 6.3 Speaking Review Workspace

### 6.3.1 Explicitly defined in the blueprint
- route: `/expert/review/speaking/:reviewRequestId`
- layout:
  - role card
  - audio player/waveform
  - transcript
  - AI flags
  - rubric panel
  - final response panel
- requirements:
  - timestamp anchoring
  - playback speed controls
  - side-by-side AI and human notes

### 6.3.2 Globally defined elsewhere that still applies
- split-pane layout required
- keyboard-first workflow required
- transcript access and accessible controls matter
- loading/partial/error/stale/permission states required
- tolerate missing/partial transcript or AI data
- performance focus on transcript handling and waveform/transcript sync
- preserve reviewer work where possible
- role-based access required
- calm validation before submit
- criterion-first feedback structure still applies

### 6.3.3 Not specified enough yet
The blueprint does not resolve:
- exact speaking rubric input model
- whether linguistic vs clinical communication criteria are grouped visually
- whether AI flags are interactive, dismissible, or immutable
- how timestamp anchoring is created:
  - click waveform?
  - click transcript line?
  - manual timestamp entry?
- whether transcript lines are speaker-segmented or sentence-segmented
- whether playback speed options are fixed or configurable
- whether reviewers can loop selected audio ranges
- whether transcript corrections are allowed
- whether reviewers can create new flags in addition to AI flags
- whether final response panel is free text, template-based, or criterion-summary driven
- whether the reviewer can compare AI and human notes in merged or parallel format
- whether transcript unavailability blocks review completion
- whether audio download/export is permitted
- whether device/browser compatibility constraints exist for expert audio review
- whether speaker silences/pauses are visualized beyond waveform
- whether the role card stays pinned while reviewing transcript

### 6.3.4 Temporary implementation assumptions only if product has not decided
- transcript and waveform remain synchronized when selecting transcript lines
- timestamp comments can be anchored from waveform or transcript selection
- playback speeds include standard stepped options only
- AI flags are shown as review aids and can be acknowledged/dismissed locally by reviewer
- final response panel supports structured summary plus free text if product later confirms

---

## 6.4 Assigned Learners

### 6.4.1 Explicitly defined in the blueprint
- route: `/expert/learners/:learnerId`
- reviewer capability includes viewing learner context needed for review

### 6.4.2 Globally defined elsewhere that still applies
- role protection required
- loading/empty/error/partial/permission states required
- professional, privacy-conscious tone required
- design system reuse required
- all pages must tolerate partial data
- typography/accessibility/focus states still apply

### 6.4.3 Not specified enough yet
The blueprint does not define:
- whether this is a summary page, profile page, or review-context page
- which learner data is visible:
  - profession
  - goals
  - exam date
  - attempt history
  - sub-test performance
  - prior human reviews
  - AI estimates
  - readiness
  - study plan
  - subscription/review credit data
- whether reviewers can navigate between assigned learners
- whether reviewers can see only learners they have reviewed
- whether reviewer notes on learner are supported
- whether there is contact/messaging functionality
- whether there are privacy restrictions on sensitive learner fields
- whether the page is intended only as contextual reference from a review
- whether expert can trigger new review recommendations from here
- whether this area should surface writing vs speaking context differently
- whether assigned learner status/history is shown

### 6.4.4 Temporary implementation assumptions only if product has not decided
- treat this page as read-only contextual profile for review support
- include only fields necessary for review context
- exclude billing/private data unless explicitly required
- surface prior submissions and prior review summaries only if product confirms

---

## 6.5 Calibration Center

### 6.5.1 Explicitly defined in the blueprint
- route: `/expert/calibration`
- views:
  - benchmark cases
  - reviewer alignment scores
  - disagreements
  - notes/history

### 6.5.2 Globally defined elsewhere that still applies
- dense information layout
- table/card hybrid allowed only via design system
- loading/empty/error states required
- accessible navigation and focus states required
- professional tone required
- role protection required
- high-trust, non-alarmist status presentation required

### 6.5.3 Not specified enough yet
The blueprint does not define:
- whether calibration is passive analytics or an active task flow
- benchmark case content model
- whether cases are filterable by profession/sub-test
- whether alignment score is reviewer-wide, criterion-wide, or time-bounded
- whether disagreements are shown against gold standard, peer median, or lead reviewer
- whether notes/history refers to reviewer notes, system change log, or calibration sessions
- whether there is a “recalibrate now” workflow
- whether calibration completion affects queue access
- whether feedback to reviewer is qualitative, numeric, or both
- whether disagreement cases can be opened into a workspace
- whether reviewers can submit rebuttals/discussion notes
- whether historical trend visualization exists

### 6.5.4 Temporary implementation assumptions only if product has not decided
- first release uses a read-heavy calibration dashboard
- benchmark cases are filterable by sub-test and profession
- disagreements link to case detail view if available
- notes/history is a timeline of reviewer calibration activity and comments

---

## 6.6 Schedule / Availability

### 6.6.1 Explicitly defined in the blueprint
- route: `/expert/schedule`
- IA label: Schedule / Availability

### 6.6.2 Globally defined elsewhere that still applies
- design system reuse required
- loading/empty/error/partial states required
- accessibility/focus states required
- role protection required
- immediate but calm validation applies to forms
- empty state should guide action

### 6.6.3 Not specified enough yet
The blueprint does not define:
- whether schedule is calendar-based or form/list-based
- whether reviewers set working hours, ad hoc slots, or both
- timezone handling
- recurring availability rules
- overlap/conflict handling
- whether schedule affects queue assignment
- whether availability links to SLA/due-time commitments
- whether admins can override reviewer schedule
- whether experts can mark time off
- whether the screen includes current workload forecast
- whether mobile support is expected for schedule management
- whether notifications/reminders are part of this surface

### 6.6.4 Temporary implementation assumptions only if product has not decided
- v1 supports basic availability management only
- availability is set in reviewer local timezone with explicit timezone display
- recurring blocks and date-specific overrides are the minimum useful model
- no assignment-routing automation UI exposed until backend rules are defined

---

## 6.7 Performance Metrics

### 6.7.1 Explicitly defined in the blueprint
- route: `/expert/metrics`
- IA label: Performance Metrics

### 6.7.2 Globally defined elsewhere that still applies
- data-heavy views should be efficient
- visualization must remain credible and professional
- status colors used conservatively
- loading/empty/error states required
- stale state warning where relevant
- role protection required

### 6.7.3 Not specified enough yet
The blueprint does not define:
- which metrics are in scope
- whether metrics are personal only or include team benchmarks
- whether queue throughput is shown
- whether SLA compliance is shown
- whether calibration alignment is shown
- whether disagreement rate is shown
- whether review quality appeals/reworks are shown
- whether date ranges and filters exist
- whether exports are allowed
- whether metrics affect reviewer standing/compensation
- whether this page is visible to reviewers only or also leads/managers

### 6.7.4 Temporary implementation assumptions only if product has not decided
- v1 metrics should be read-only and limited to personal operational metrics
- include date range filter only if backend supports it cleanly
- no comparative leaderboard unless explicitly approved
- avoid gamified performance ranking UI

---

# 7. Cross-cutting expert-console decisions that still need product/design resolution

These are not tied to one screen only.

## 7.1 Review status model
The blueprint refers to:
- status
- save draft review
- send/rework controls
- assigned/unassigned
- SLA due

But it does **not** define the canonical status set.

This must be resolved:
- queued
- assigned
- in progress
- draft saved
- awaiting transcript
- awaiting AI assist
- submitted
- rework requested
- completed
- overdue
- blocked

Which of these are real product statuses vs UI states vs derived indicators must be defined.

---

## 7.2 Assignment model
Not defined:
- can reviewers claim work?
- can admins assign manually only?
- can queue show unassigned items to all?
- can reviewer reassign?
- what happens if assigned reviewer is unavailable?

This affects queue behavior, permissions, and schedule usage.

---

## 7.3 SLA model
The blueprint includes SLA due and SLA visibility but does not define:
- SLA calculation basis
- timezone basis
- urgency thresholds
- breach behavior
- escalation behavior
- visual severity rules

This is operationally critical.

---

## 7.4 AI assistance model
The blueprint includes:
- AI confidence
- AI draft feedback
- AI flags
- side-by-side AI and human notes

But it does not define:
- whether experts can edit AI content
- whether AI is advisory only
- whether AI confidence affects routing
- whether low confidence requires human escalation automatically
- whether AI notes are stored after human finalization
- how disagreements between AI and reviewer are represented

This must be resolved for trust and auditability.

---

## 7.5 Rubric model for expert scoring
The expert workspaces include rubric panels, but the blueprint does not define:
- exact criterion entry controls
- required vs optional evidence fields
- comment templates
- whether criterion scores are visible to learner exactly as entered
- save-as-draft semantics
- finalization semantics
- moderation/second-review process

This is one of the most important unresolved areas.

---

## 7.6 Auditability and revision history
The blueprint defines audit logs for admin and revision/history concepts elsewhere, but not clearly for expert review actions.

Need decision on:
- whether expert review drafts have version history
- whether comment edits are audited
- whether final sent reviews can be amended
- whether timestamps and actor metadata are visible
- whether reviewer notes are exportable/internal-only

---

## 7.7 Learner context boundaries
The capability says reviewers can view learner context needed for review, but not what is allowed or prohibited.

Need explicit privacy boundary on:
- goal and exam date
- prior attempts
- prior scores
- prior human reviews
- subscription/credits
- personal identifiers
- target country/organization
- internal support history

---

## 7.8 Expert-console device support
The blueprint explicitly gives richer device expectations for learner surfaces, but not for expert surfaces.

Need decision on:
- desktop-only vs tablet-tolerant
- supported minimum screen widths
- whether audio review is officially supported on mobile
- whether keyboard shortcuts are required only on desktop

Recommended default: desktop-first, tablet-tolerant where practical, no mobile-first commitment unless approved.

---

## 7.9 Notifications and background state
Not defined:
- when experts are notified of assignments
- whether queue refresh is real-time, polling, or manual
- whether draft-save confirmation uses toast vs inline state
- whether a submitted review stays open or returns to queue
- whether stale state is auto-detected

---

## 7.10 Search model
Not defined across expert surfaces:
- global search?
- learner search?
- review id search?
- case search?
- transcript search?

This is especially relevant for queue, learners, calibration, and metrics.

---

# 8. Developer implementation guardrails

## 8.1 Build from the existing architecture
Use the blueprint’s recommended frontend architecture:
- React + TypeScript
- route-based code splitting
- query library for server state
- lightweight client store for local UI state
- schema validation for forms
- shared design system module/package

## 8.2 Reuse existing domain language
Use:
- profession
- sub-test
- criterion
- evaluation
- attempt
- review request
- AI confidence
- SLA
- benchmark case
- calibration

Do not rename these concepts casually in the UI.

## 8.3 Avoid premature product invention
Until product confirms:
- do not add leaderboard-style reviewer ranking
- do not add messaging/chat features
- do not add new queue states not agreed with backend/product
- do not add editable learner profile behaviors
- do not add assignment automation controls if backend rules do not exist
- do not expose hidden scoring or routing config client-side

## 8.4 Prefer explicit interim states over hidden uncertainty
Where data may be delayed or missing, show explicit state:
- transcript queued
- AI feedback unavailable
- review save failed
- stale queue data
- partial learner context

This matches the blueprint’s trust-first requirement.

---

# 9. What is implementation-ready right now vs what still needs product resolution

## 9.1 Safe to build now from the blueprint
These areas have enough definition to start structural implementation:
- expert route map
- expert IA and navigation entries
- queue required columns
- queue required filters
- Writing review workspace major layout regions
- Speaking review workspace major layout regions
- requirement for keyboard-first reviewing
- requirement for split-pane review flows
- requirement for save draft review
- requirement for anchored comments in Writing
- requirement for timestamp anchoring in Speaking
- calibration center major sections
- existence of schedule and metrics routes
- use of current design system
- support for global loading/empty/error/partial/permission/stale states
- role protection and frontend architecture baseline

## 9.2 Needs product/design decision before “final” implementation
These should not be guessed silently:
- exact queue sort/order logic
- review status taxonomy
- assignment/claim model
- AI confidence visual/semantic model
- detailed rubric input design
- rework semantics
- expert-facing learner context fields
- calibration formulas and flows
- availability model
- metrics definitions
- transcript/waveform interaction details
- draft-save/versioning semantics
- concurrency/locking rules
- notification and refresh behavior

---

# 10. Recommended next deliverables after this file

To make the Expert Console fully implementation-ready, the next documents should be created:

1. **Expert Console wireframe spec**
   - queue
   - writing workspace
   - speaking workspace
   - learner profile/context
   - calibration center
   - schedule
   - metrics

2. **Expert Console data contract spec**
   - reviewRequest
   - reviewerAssignment
   - SLA model
   - AI assist payloads
   - writing rubric payload
   - speaking rubric payload
   - benchmark/calibration models
   - reviewer metrics payload
   - availability model

3. **Expert Console Jira backlog**
   - epics
   - stories
   - dependencies
   - QA cases
   - analytics
   - permissions

4. **Open decisions register**
   Every unresolved item in section 6 and section 7 should be converted into tracked product decisions.

---

# 11. Final instruction to the frontend developer

Build the Expert Console as a **high-trust, reviewer-operational assessment workspace**, not as a generic internal dashboard.

The most important UX outcomes are:
- reviewers can triage work quickly
- long review sessions are stable
- rubric and evidence stay visible and usable
- AI assistance is helpful but clearly non-authoritative
- reviewer work is never easily lost
- queue and workspace flows are keyboard-efficient
- all implementation stays inside the current design system and current product vocabulary

Do **not** invent new visual systems, new scoring semantics, or new workflow states without explicit approval.

When the blueprint is silent, treat it as an open decision and escalate it, rather than guessing.
