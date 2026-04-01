# OET Platform Audit Fact Base

This file is the working evidence base used to draft the product-manual package. It is intentionally operational and source-oriented rather than polished.

## Source of Truth Used

- Frontend routes under `app/`
- Learner/expert/admin navigation in the route `layout.tsx` files and shared shell components
- Frontend integration and domain contracts in:
  - `lib/api.ts`
  - `lib/auth-routes.ts`
  - `lib/types/auth.ts`
  - `lib/types/expert.ts`
  - `lib/types/admin.ts`
  - `lib/admin.ts`
- Backend route groups in:
  - `backend/src/OetLearner.Api/Endpoints/LearnerEndpoints.cs`
  - `backend/src/OetLearner.Api/Endpoints/ExpertEndpoints.cs`
  - `backend/src/OetLearner.Api/Endpoints/AdminEndpoints.cs`
  - `backend/src/OetLearner.Api/Endpoints/NotificationEndpoints.cs`
- Selected route pages and domain components for workflow confirmation
- `README.md` for project identity and stack confirmation

## Role Model

- `learner`
- `expert`
- `admin`

Role defaults are defined in `lib/auth-routes.ts`:

- learner -> `/`
- expert -> `/expert`
- admin -> `/admin`

## Route Matrix

### Learner product routes

Implemented unless noted otherwise.

- `/`
- `/onboarding`
- `/goals`
- `/diagnostic`
- `/diagnostic/hub`
- `/diagnostic/results`
- `/diagnostic/writing`
- `/diagnostic/speaking`
- `/diagnostic/reading`
- `/diagnostic/listening`
- `/study-plan`
- `/writing`
- `/writing/library`
- `/writing/player`
- `/writing/result`
- `/writing/feedback`
- `/writing/revision`
- `/writing/model`
- `/writing/expert-request`
- `/speaking`
- `/speaking/selection`
- `/speaking/check`
- `/speaking/roleplay/[id]`
- `/speaking/task/[id]`
  - `partial`: UI accepts `ai` mode but downgrades it to `self` because live AI speaking is unavailable in the current implementation.
- `/speaking/results/[id]`
- `/speaking/transcript/[id]`
- `/speaking/phrasing/[id]`
- `/speaking/expert-review/[id]`
- `/reading`
- `/reading/player/[id]`
- `/reading/results/[id]`
- `/listening`
- `/listening/player/[id]`
- `/listening/results/[id]`
- `/listening/review/[id]`
- `/listening/drills/[id]`
- `/mocks`
- `/mocks/setup`
- `/mocks/player/[id]`
  - `partial`: section launch routes still use fixed task identifiers for some sub-tests.
- `/mocks/report/[id]`
- `/mocks/[id]`
- `/progress`
- `/readiness`
- `/submissions`
- `/submissions/[id]`
- `/submissions/compare`
- `/billing`
- `/settings`
- `/settings/[section]`

### Learner auth/support routes

These are access and account routes, not primary product surfaces.

- `/sign-in`
- `/register`
- `/register/success`
- `/forgot-password`
- `/forgot-password/verify`
- `/reset-password`
- `/reset-password/success`
- `/verify-email`
- `/mfa/challenge`
- `/mfa/setup`
- `/terms`
- `/auth/callback/[provider]`

### Expert routes

- `/expert`
- `/expert/queue`
- `/expert/calibration`
- `/expert/calibration/[caseId]`
- `/expert/metrics`
- `/expert/schedule`
- `/expert/learners`
- `/expert/learners/[learnerId]`
- `/expert/review/[reviewRequestId]`
  - redirect route that resolves the sub-test-specific review workspace
- `/expert/review/writing/[reviewRequestId]`
- `/expert/review/speaking/[reviewRequestId]`

### Admin routes

- `/admin`
- `/admin/content`
- `/admin/content/new`
- `/admin/content/[id]`
- `/admin/content/[id]/revisions`
- `/admin/taxonomy`
- `/admin/criteria`
- `/admin/ai-config`
- `/admin/review-ops`
- `/admin/notifications`
- `/admin/analytics/quality`
- `/admin/users`
- `/admin/users/[id]`
- `/admin/billing`
- `/admin/flags`
- `/admin/audit-logs`

### Support and alias routes

- `/dashboard/project`
  - route alias that re-exports the main learner dashboard page

## Surface Feature Matrix

### Learner App

- Onboarding
  - `implemented`
  - guided introduction, diagnostic framing, and completion state
- Goals setup
  - `implemented`
  - profession, exam date, target geography, score goals, weak-skill declaration
- Diagnostics
  - `implemented` overall
  - Writing, Speaking, Reading, Listening diagnostic flows exist
  - `partial` in data wiring because some diagnostic pages still use fixed task IDs
- Dashboard home
  - `implemented`
  - combines study plan, readiness, user profile, and dashboard summary data
- Study plan
  - `implemented`
  - task actions include complete, reset, reschedule, and swap
- Writing workflow
  - `implemented`
  - practice library, timed player, AI result, detailed feedback, revision mode, model answer, expert review request
- Speaking workflow
  - `implemented`
  - task selection, device check, role card preview, recording, result, transcript, phrasing drills, expert review request
  - `partial` for live AI speaking mode
- Reading workflow
  - `implemented`
  - task player, objective submission, results
- Listening workflow
  - `implemented`
  - player, results, transcript-backed review, targeted drills
- Mock center
  - `implemented` as a full surface
  - `partial` in orchestration because player launch routes still include hardcoded task IDs
- Readiness
  - `implemented`
- Progress analytics
  - `implemented`
- Submission history and compare
  - `implemented`
- Billing and subscriptions
  - `implemented` at UI and API surface level
  - precise external processor behavior was not fully audited in this pass
- Settings
  - `implemented`

### Expert Console

- Dashboard
  - `implemented`
- Review queue with filters and claim/release controls
  - `implemented`
- Writing review workspace
  - `implemented`
  - anchored comments, rubric scoring, AI draft comparison, draft save, submit, rework
- Speaking review workspace
  - `implemented`
  - waveform, transcript comments, AI flags, rubric scoring, draft save, submit, rework
- Learner roster and learner context view
  - `implemented`
- Calibration center and calibration case scoring
  - `implemented`
- Metrics
  - `implemented`
- Schedule/availability
  - `implemented`

### Admin Dashboard / CMS

- Operations dashboard
  - `implemented`
- Content library
  - `implemented`
- Content editor
  - `implemented`
- Content revisions and restore
  - `implemented`
- Profession taxonomy
  - `implemented`
- Rubrics and criteria
  - `implemented`
- AI evaluation configuration
  - `implemented`
- Review operations
  - `implemented`
- Notification governance
  - `implemented`
- Quality analytics
  - `implemented`
- User operations
  - `implemented`
- Billing operations
  - `implemented`
- Feature flags
  - `implemented`
- Audit logs
  - `implemented`

## Role-Specific Actions

### Learner

- complete onboarding
- define profession and score goals
- run diagnostic attempts
- complete study-plan tasks
- start Writing, Speaking, Reading, and Listening practice
- submit productive-skill work for AI evaluation
- request expert review for Writing and Speaking
- take mocks
- inspect readiness, progress, and submission history
- manage billing and settings

### Expert

- claim and release review work
- open learner review bundles
- inspect learner context and prior review history
- score Writing and Speaking against rubric criteria
- attach anchored or timestamped comments
- save drafts, submit final reviews, request rework
- complete calibration cases
- manage availability and inspect personal metrics

### Admin

- create, edit, publish, archive, and restore content
- manage profession taxonomy and criterion definitions
- control AI evaluation configs and feature flags
- monitor and intervene in review operations
- manage notification policies
- inspect quality analytics
- invite and manage users
- manage plan, add-on, coupon, subscription, and invoice surfaces
- inspect audit history

## System Relationship Matrix

- Admin content/configuration -> Learner availability
  - content library/editor/revisions/taxonomy/criteria/AI config determine what practice content, scoring logic, and profession mapping are available to learners
- Learner submission -> Expert queue
  - Writing and Speaking expert-review requests generate review work that appears in expert queue and review workspaces
- Expert feedback -> Learner improvement loop
  - expert outputs return to learner-visible review history, evidence, and readiness/progress context
- Mocks -> Readiness and study planning
  - mock reports surface weakest areas and direct the learner back to the study plan
- Admin review ops -> Expert delivery stability
  - review assignment, cancellation, reopening, and failure monitoring govern the human-review pipeline
- Admin quality analytics -> model and content governance
  - agreement, appeals, timing, and risk trends provide oversight of evaluation quality
- Admin notification policies -> learner/expert/admin communications
  - policy changes control audience/channel behavior across operational events
- Billing operations -> learner entitlements and review capacity
  - plans, add-ons, coupon rules, and credits affect access to review-linked capabilities

## Key Confirmed Business Behaviors

- The platform is OET-specific, not a generic LMS.
- Productive skills are treated differently from objective skills.
  - Writing and Speaking include criterion-level evaluation and expert review request paths.
  - Reading and Listening are objective, answer-driven, and reviewable through explanations or transcript support rather than human marking workspaces.
- Readiness is evidence-based and aggregates diagnostic, practice, mock, and review signals.
- Study planning is operational, not informational.
  - tasks can be completed, reset, rescheduled, and swapped.
- Admin tooling is not cosmetic.
  - the CMS, taxonomy, criteria, AI config, review ops, billing ops, flags, and notifications all have concrete UI and backend surfaces.

## Observed Partial, Transitional, or Unclear Areas

- `lib/mock-data.ts` still contains a transitional note about replacing centralized mock data with real API responses when backend readiness is complete.
- Some learner pages continue to rely on fixed task IDs:
  - diagnostic Writing
  - diagnostic Speaking
  - diagnostic Reading
  - diagnostic Listening
  - mock player launch logic
- Speaking `ai` mode is not functionally available and is downgraded to self-guided behavior.
- The learner mock player is a real orchestration surface, but the section-to-route mapping is not fully data-driven yet.
- UI copy includes visible encoding artifacts on several pages, which should be treated as implementation quality issues, not intentional product language.
- Billing and notification governance are clearly implemented as product surfaces, but the exact external-provider contract was not exhaustively traced in this documentation pass.

