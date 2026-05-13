# Expert Console Manual

This manual documents the expert-facing review console as implemented in the current codebase.

Related documents:

- [Master Product Manual](./master-product-manual.md)
- [Learner App Manual](./learner-app-manual.md)
- [Sponsor Portal Manual](./sponsor-portal-manual.md)
- [Admin Dashboard and CMS Manual](./admin-dashboard-cms-manual.md)
- [Cross-System Business Logic and Workflows](./cross-system-business-logic-and-workflows.md)
- [Route, API, and Domain Surface Index](./route-api-domain-surface-index.md)
- [Reference Appendix](./reference-appendix.md)

Status labels used in this document:

- `implemented`
- `partial`
- `unclear`

`implemented` means the current UI and API surface exist. `partial` means the surface exists but depends on upstream artifacts, policy, capacity, or transitional data. `unclear` means the route or workflow is referenced but was not confirmed strongly enough to document as complete behavior.

These labels follow the canonical vocabulary in [README](./README.md) and do not by themselves mean end-to-end release validation.

## Expert Route Inventory

The expert surface currently contains 26 page routes. It includes the original review console plus onboarding, private speaking, messages, compensation, quality tooling, and live/mock speaking operations.

Review and dashboard routes:

- `/expert`
- `/expert/queue`
- `/expert/queue-priority`
- `/expert/review/[reviewRequestId]`
- `/expert/review/writing/[reviewRequestId]`
- `/expert/review/speaking/[reviewRequestId]`

Quality, calibration, and performance routes:

- `/expert/calibration`
- `/expert/calibration/[caseId]`
- `/expert/calibration/speaking`
- `/expert/metrics`
- `/expert/scoring-quality`
- `/expert/rubric-reference`

Learner, schedule, and tutor operations:

- `/expert/learners`
- `/expert/learners/[learnerId]`
- `/expert/schedule`
- `/expert/private-speaking`
- `/expert/speaking-room/[bookingId]`
- `/expert/mocks/bookings`
- `/expert/messages`
- `/expert/messages/[threadId]`
- `/expert/compensation`
- `/expert/onboarding`

Assisted-review and support routes:

- `/expert/ask-an-expert`
- `/expert/ai-prefill`
- `/expert/annotation-templates`
- `/expert/mobile-review`

## Current Coverage Addendum

- Private speaking is a first-class expert workstream, not only a learner commercial route.
- Mock booking and speaking-room routes connect expert operations to learner speaking-room experiences.
- Messages and ask-an-expert routes support communication outside the formal review workspace.
- Compensation gives experts an operational view of payable or completed work.
- Queue priority, scoring quality, rubric reference, annotation templates, AI prefill, mobile review, and speaking calibration are specialist review-quality surfaces.
- Onboarding can be inserted into the expert nav for experts whose profile is incomplete.

## Additional Expert Workstream Reference

| Workstream | Status | Purpose | QA focus |
| --- | --- | --- | --- |
| Private speaking | `implemented` | manage tutor availability and paid speaking work | booking visibility, schedule alignment, room launch |
| Speaking room | `implemented` | deliver booked live speaking sessions | booking auth, audio/session state, completion handling |
| Mock bookings | `implemented` | support scheduled mock-related speaking work | queue visibility and handoff to room/review surfaces |
| Messages and ask-an-expert | `implemented` | communicate outside formal review workspaces | thread routing, permissions, notification behavior |
| Compensation | `implemented` | expose payable/completed expert work | period filters and payout-state accuracy |
| Onboarding | `implemented` | complete expert profile readiness | nav insertion and completion persistence |
| Quality aids | `implemented` | AI prefill, annotation templates, queue priority, rubric reference, scoring quality, mobile review | advisory-only AI use, template persistence, mobile-safe review |

## 1. Expert Dashboard

- Status: `implemented`
- Purpose: Provide an operating summary of assigned work, calibration obligations, learner coverage, and personal availability.
- Business logic served: Helps the reviewer manage workload, quality obligations, and turnaround expectations.
- Location: `/expert`
- Who uses it: Experts and reviewers
- Inputs:
  - expert dashboard summary
  - assignment counts
  - calibration state
  - availability state
- Outputs:
  - active assigned review count
  - overdue assigned review count
  - calibration due
  - learner count
  - recent activity
- Main actions:
  - resume work
  - inspect queue
  - move into calibration or schedule
- Step-by-step workflow:
  1. The expert opens the dashboard.
  2. The expert inspects current workload and overdue pressure.
  3. The expert enters queue, calibration, or schedule management as needed.
- Dependencies:
  - queue
  - calibration
  - metrics
  - schedule
- Operational notes:
  - The page includes MFA-related prompting for privileged access hygiene.

## 2. Review Queue

- Status: `implemented`
- Purpose: Present incoming and assigned review work with operational filters.
- Business logic served: Turns learner review requests into manageable expert work items.
- Location: `/expert/queue`
- Who uses it: Experts
- Inputs:
  - queue filter metadata
  - review request rows
  - claim/release capability
- Outputs:
  - filtered queue
  - assignment state changes
- Main actions:
  - filter by sub-test, profession, status, priority, AI confidence, overdue state, assignment state
  - claim review
  - release review
  - open review
- Step-by-step workflow:
  1. The expert filters the queue to the most relevant work.
  2. The expert claims an eligible request.
  3. The expert opens the request in a sub-test-specific workspace.
  4. If needed, the expert releases the item.
- Dependencies:
  - Writing review workspace
  - Speaking review workspace
  - learner review requests from the learner app
- Operational notes:
  - Queue rows include SLA due time, AI confidence, and assignment information.
- What to test:
  - claim and release permissions
  - queue filtering
  - overdue visibility
  - open-route resolution into the correct review workspace

## 3. Writing Review Workspace

- Status: `implemented`
- Purpose: Let experts assess learner Writing against OET-relevant rubric criteria.
- Business logic served: Adds human review to productive-skill evidence where judgment quality matters.
- Location: `/expert/review/writing/[reviewRequestId]`
- Who uses it: Experts reviewing Writing
- Inputs:
  - learner response
  - case notes
  - AI draft feedback
  - AI suggested scores
  - model answer when available
  - learner context
  - prior review history
- Outputs:
  - criterion scores
  - criterion comments
  - overall expert comment
  - saved draft review
  - submitted review
  - rework request
- Main actions:
  - inspect learner response
  - switch between response, case notes, and AI draft tabs
  - create anchored comments from selected response text
  - score rubric criteria
  - save draft
  - submit review
  - request rework
- Step-by-step workflow:
  1. The expert opens a Writing review bundle.
  2. The expert reviews learner context and prior review history.
  3. The expert reads the learner response and case notes.
  4. The expert uses the AI draft as advisory input, not as the final review.
  5. The expert scores each criterion and writes comments.
  6. The expert saves a draft or submits the final review.
- Dependencies:
  - learner review request
  - expert review history
  - learner profile and goal context
- Operational notes:
  - Keyboard shortcuts and unload protection are implemented.
  - The workspace includes an SLA timer and can surface partial artifact readiness.

## 4. Speaking Review Workspace

- Status: `implemented`
- Purpose: Let experts assess speaking evidence using audio, transcript, AI flags, and rubric scoring.
- Business logic served: Operationalizes human review of spoken OET performance.
- Location: `/expert/review/speaking/[reviewRequestId]`
- Who uses it: Experts reviewing Speaking
- Inputs:
  - audio
  - transcript lines
  - role card
  - AI flags
  - AI suggested scores
  - learner context
  - prior review history
- Outputs:
  - rubric scores
  - rubric comments
  - overall expert review
  - draft or submitted review state
  - rework request
- Main actions:
  - play waveform audio
  - inspect synchronized transcript
  - create timestamped comments
  - inspect AI flags
  - score rubric criteria
  - save draft
  - submit review
  - request rework
- Step-by-step workflow:
  1. The expert opens the speaking review bundle.
  2. The expert listens to the audio and reviews the transcript.
  3. The expert uses timestamped evidence and AI flags to inspect problem spots.
  4. The expert scores the response against speaking criteria.
  5. The expert saves or submits the review.
- Dependencies:
  - speaking recording and transcript generation
  - learner context
  - review history
- Operational notes:
  - The workspace can be blocked or partial if transcript or AI-flag artifacts are not complete.
- What to test:
  - audio playback
  - transcript jump behavior
  - timestamp comment creation
  - artifact-partial states

## 5. Learner Roster and Learner Context

- Status: `implemented`
- Purpose: Give experts enough context to review fairly and consistently.
- Business logic served: Prevents isolated marking by exposing learner goals, history, and relevant prior reviews.
- Location:
  - `/expert/learners`
  - `/expert/learners/[learnerId]`
- Who uses it: Experts
- Inputs:
  - learner list data
  - learner profile data
  - expert learner review context
- Outputs:
  - filtered learner roster
  - learner detail context panel
- Main actions:
  - search learners
  - filter by profession, sub-test relevance, or name
  - inspect learner goals and review history
- Step-by-step workflow:
  1. The expert opens the learner roster.
  2. The expert narrows the set to relevant learners.
  3. The expert opens learner detail to understand goals and prior expert interactions.
- Dependencies:
  - review requests
  - learner goal and performance data

## 6. Calibration Center

- Status: `implemented`
- Purpose: Maintain review alignment against benchmarked cases.
- Business logic served: Protects review quality and consistency across experts.
- Location:
  - `/expert/calibration`
  - `/expert/calibration/[caseId]`
- Who uses it: Experts
- Inputs:
  - calibration cases
  - benchmark scoring data
  - calibration notes
- Outputs:
  - calibration submissions
  - visible alignment metrics
- Main actions:
  - filter cases
  - open a calibration case
  - score benchmark artifacts
  - submit calibration response
- Step-by-step workflow:
  1. The expert opens the calibration center.
  2. The expert selects a pending case.
  3. The expert reviews the artifact set and benchmark guidance.
  4. The expert submits a calibration decision.
- Dependencies:
  - quality governance
  - expert metrics
- Operational notes:
  - Calibration is a quality-control surface, not a learner-facing feature.

## 7. Metrics

- Status: `implemented`
- Purpose: Show the expert's throughput, compliance, and quality indicators.
- Business logic served: Supports reviewer self-management and admin oversight.
- Location: `/expert/metrics`
- Who uses it: Experts
- Inputs:
  - period-specific metrics
- Outputs:
  - total completed
  - draft reviews
  - SLA compliance
  - average turnaround
  - alignment score
  - rework rate
- Main actions:
  - inspect performance over a selected time window

## 8. Schedule and Availability

- Status: `implemented`
- Purpose: Let experts declare work availability.
- Business logic served: Supports operational review assignment and staffing.
- Location: `/expert/schedule`
- Who uses it: Experts
- Inputs:
  - weekly availability slots
  - timezone
- Outputs:
  - persisted schedule
- Main actions:
  - toggle days on and off
  - edit start and end time
  - save availability
- Step-by-step workflow:
  1. The expert opens schedule settings.
  2. The expert defines available days and time windows.
  3. The expert saves the schedule.
- Dependencies:
  - review operations and assignment planning
- Operational notes:
  - The UI validates that end time follows start time.

## Expert QA Focus Areas

- queue claim/release/open behavior
- Writing review save-draft and submit paths
- Speaking transcript and audio synchronization
- learner-context loading in review workspaces
- rework request behavior
- calibration submission and benchmark display
- metrics period switching
- schedule validation and persistence

## Observed Gaps and Partial Implementations

- Expert workspaces are implemented and detailed, but they depend on upstream artifact readiness; transcript or AI-flag readiness can produce partial review states.
- The redirect route `/expert/review/[reviewRequestId]` is operational infrastructure and should be tested whenever sub-test routing logic changes.
- Advanced capacity is split across schedule, queue priority, private speaking, mock bookings, messages, compensation, and admin review/private-speaking operations rather than one single capacity screen.

Revision source: `ROUTE-SNAPSHOT-2026-05-13`.
