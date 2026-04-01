# Learner App Manual

This manual documents the learner-facing product as implemented in the current codebase.

Related documents:

- [Master Product Manual](./master-product-manual.md)
- [Expert Console Manual](./expert-console-manual.md)
- [Admin Dashboard and CMS Manual](./admin-dashboard-cms-manual.md)
- [Cross-System Business Logic and Workflows](./cross-system-business-logic-and-workflows.md)

Status labels used in this document:

- `implemented`: confirmed in the current UI and supported by the current API surface
- `partial`: available but still transitional, constrained, or not fully data-driven
- `unclear`: referenced by the system, but not confirmed strongly enough to document as a complete behavior

## Learner Route Inventory

Primary learner routes:

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

Support and account routes exist separately under the auth route group and are not the main learner preparation surface.

## 1. Onboarding

- Status: `implemented`
- Purpose: Introduce the platform, frame the preparation model, and move the learner into goal definition and diagnostics.
- Business logic served: Prevents learners from entering practice without understanding the diagnostic-first workflow.
- Location: `/onboarding`
- Who uses it: New learner accounts
- When it is used: First-use journey or when onboarding state is incomplete
- Inputs:
  - onboarding state
  - learner progression through intro steps
- Outputs / Results:
  - onboarding completion state
  - redirect toward goals and diagnostics
- Main user actions:
  - start onboarding
  - move through onboarding panels
  - complete onboarding
- Step-by-step usage:
  1. The learner opens the onboarding route.
  2. The app explains the platform's diagnostic, study-plan, and expert-review model.
  3. The learner completes the flow and is moved toward goal setup.
- Connected features / Dependencies:
  - goals
  - diagnostics
  - auth/session state
- Edge cases / States:
  - incomplete onboarding state
  - resuming onboarding
- Notes:
  - The onboarding copy explicitly positions expert review around Writing and Speaking.
- What to test:
  - first-use redirect behavior
  - onboarding completion persistence
  - downstream redirect into goals

## 2. Goals Setup

- Status: `implemented`
- Purpose: Capture exam intent, profession context, target scores, and study constraints.
- Business logic served: Provides the target state used to interpret diagnostics, readiness, and planning.
- Location: `/goals`
- Who uses it: Learners
- When it is used: Early account setup and any later goal revision
- Inputs:
  - profession
  - exam date
  - target country
  - prior attempts
  - target scores by sub-test
  - weak-skill declaration
  - available study hours
- Outputs / Results:
  - updated learner profile
  - redirect into diagnostics
- Main user actions:
  - edit goal fields
  - save target profile
- Step-by-step usage:
  1. The learner enters profession and target context.
  2. The learner sets desired scores for Writing, Speaking, Reading, and Listening.
  3. The learner saves and moves into diagnostic preparation.
- Connected features / Dependencies:
  - user profile
  - diagnostic routes
  - readiness target comparisons
- Edge cases / States:
  - incomplete target information
  - updated goal state after initial setup
- Notes:
  - This step is OET-specific because it captures target scores by sub-test rather than a single generic course goal.

## 3. Diagnostic Center

### 3.1 Diagnostic Start

- Status: `implemented`
- Purpose: Start or resume the learner's diagnostic program.
- Business logic served: Establishes a baseline before personalized planning.
- Location: `/diagnostic`
- Who uses it: Learners
- When it is used: After goals are set, or when a learner returns to diagnostics
- Inputs:
  - learner readiness to begin
- Outputs / Results:
  - diagnostic attempt/session creation
  - redirect to hub
- Main user actions:
  - start diagnostics
- Step-by-step usage:
  1. The learner launches the diagnostic from the start page.
  2. The system creates or resumes the diagnostic session.
  3. The learner is routed to the diagnostic hub.
- Connected features / Dependencies:
  - goals
  - diagnostic hub
  - backend diagnostic-attempt routes

### 3.2 Diagnostic Hub and Results

- Status: `implemented`
- Purpose: Coordinate diagnostic completion across all four sub-tests and summarize the baseline outcome.
- Business logic served: Converts four separate attempts into one interpretable readiness starting point.
- Location:
  - `/diagnostic/hub`
  - `/diagnostic/results`
- Who uses it: Learners
- When it is used: During and after diagnostic completion
- Inputs:
  - per-sub-test diagnostic attempt state
- Outputs / Results:
  - completion visibility
  - score/risk summary
  - weak-link identification
- Main user actions:
  - open pending diagnostic modules
  - review final baseline summary
- Step-by-step usage:
  1. The learner checks which diagnostic sub-tests are complete.
  2. The learner enters any incomplete diagnostic module.
  3. The learner reviews results once the diagnostic program is complete.
- Connected features / Dependencies:
  - diagnostic Writing, Speaking, Reading, Listening flows
  - study plan
  - readiness
- Edge cases / States:
  - incomplete diagnostic coverage
  - resumed attempts
- Notes:
  - The results page includes AI-estimate caveats rather than treating the baseline as a guaranteed prediction.
- What to test:
  - partial completion handling
  - status updates after each sub-test
  - results summary after all four diagnostic attempts

### 3.3 Diagnostic Writing

- Status: `partial`
- Purpose: Measure baseline OET Writing performance through a timed draft-and-submit flow.
- Business logic served: Establishes productive-skill baseline evidence.
- Location: `/diagnostic/writing`
- Who uses it: Learners
- When it is used: During diagnostic completion
- Inputs:
  - writing task
  - learner draft
- Outputs / Results:
  - submitted diagnostic writing attempt
  - return to diagnostic hub
- Main user actions:
  - review case notes
  - draft response
  - use checklist
  - autosave
  - submit
- Step-by-step usage:
  1. The learner opens the diagnostic Writing task.
  2. The learner drafts a response with autosave support.
  3. The learner reviews submission confirmation.
  4. The learner submits and returns to the hub.
- Connected features / Dependencies:
  - writing task retrieval
  - writing draft submission
  - diagnostic hub
- Edge cases / States:
  - leave confirmation
  - autosaved local and server draft state
- Notes:
  - The current page uses a fixed diagnostic task ID, so the flow is implemented but still transitional.

### 3.4 Diagnostic Speaking

- Status: `partial`
- Purpose: Capture baseline OET Speaking performance through a structured recording flow.
- Business logic served: Establishes productive-skill baseline evidence for oral performance.
- Location: `/diagnostic/speaking`
- Who uses it: Learners
- When it is used: During diagnostic completion
- Inputs:
  - speaking role card
  - learner recording
- Outputs / Results:
  - submitted diagnostic speaking attempt
  - return to diagnostic hub
- Main user actions:
  - complete microphone check
  - review role card
  - record
  - rerecord
  - submit
- Step-by-step usage:
  1. The learner opens the diagnostic speaking task.
  2. The learner moves through mic-check and role-card phases.
  3. The learner records and reviews the attempt.
  4. The learner submits and returns to the hub.
- Connected features / Dependencies:
  - role card retrieval
  - speaking recording submission
  - diagnostic hub
- Edge cases / States:
  - recording review phase
  - upload phase
  - resubmission from review
- Notes:
  - The flow is real, but it also uses a fixed diagnostic task ID in the current implementation.

### 3.5 Diagnostic Reading

- Status: `partial`
- Purpose: Establish baseline reading accuracy through an objective timed attempt.
- Business logic served: Measures the learner's initial performance on an objective sub-test.
- Location: `/diagnostic/reading`
- Who uses it: Learners
- When it is used: During diagnostic completion
- Inputs:
  - reading task
  - question answers
- Outputs / Results:
  - submitted objective diagnostic attempt
  - return to diagnostic hub
- Main user actions:
  - navigate questions
  - answer objective items
  - submit
- Step-by-step usage:
  1. The learner opens the diagnostic Reading task.
  2. The learner reads the passage and answers questions.
  3. The learner submits and returns to the hub.
- Connected features / Dependencies:
  - reading task retrieval
  - reading answer submission
  - diagnostic hub
- Edge cases / States:
  - local answer persistence
  - timer completion warning
- Notes:
  - The current implementation is working but uses a fixed diagnostic reading task ID.

### 3.6 Diagnostic Listening

- Status: `partial`
- Purpose: Establish baseline listening accuracy through an objective audio-driven attempt.
- Business logic served: Measures initial comprehension and answer accuracy on an objective sub-test.
- Location: `/diagnostic/listening`
- Who uses it: Learners
- When it is used: During diagnostic completion
- Inputs:
  - listening task
  - question answers
- Outputs / Results:
  - submitted objective diagnostic attempt
  - return to diagnostic hub
- Main user actions:
  - control playback
  - answer questions
  - submit
- Step-by-step usage:
  1. The learner plays the diagnostic listening audio.
  2. The learner answers questions while audio progress is tracked.
  3. The learner submits and returns to the hub.
- Connected features / Dependencies:
  - listening task retrieval
  - listening answer submission
  - diagnostic hub
- Edge cases / States:
  - managed playback state
  - local answer persistence
- Notes:
  - The current implementation is working but uses a fixed diagnostic listening task ID.

## 4. Learner Home Dashboard

- Status: `implemented`
- Purpose: Give the learner a single-screen view of current preparation state.
- Business logic served: Turns multiple evidence streams into an actionable home surface.
- Location: `/`
- Who uses it: Learners
- When it is used: Regular return point after sign-in
- Inputs:
  - study plan summary
  - readiness summary
  - user profile
  - dashboard-home data
- Outputs / Results:
  - visible next action
  - current focus area
  - pending review awareness
- Main user actions:
  - inspect recommended next action
  - jump into practice or planning
- Step-by-step usage:
  1. The learner opens the dashboard.
  2. The learner reviews today's plan, readiness, and weak-link signals.
  3. The learner follows the main CTA into the next recommended activity.
- Connected features / Dependencies:
  - study plan
  - readiness
  - mocks
  - pending reviews
- Edge cases / States:
  - loading or error state from any combined query

## 5. Study Plan

- Status: `implemented`
- Purpose: Translate learner weaknesses and goals into a practical schedule of work.
- Business logic served: Makes preparation operational rather than passive.
- Location: `/study-plan`
- Who uses it: Learners
- When it is used: After diagnostics and throughout ongoing preparation
- Inputs:
  - learner profile
  - diagnostic or readiness evidence
  - study-plan tasks
- Outputs / Results:
  - today list
  - weekly tasks
  - next checkpoint
  - weak-skill focus
- Main user actions:
  - start task
  - mark complete
  - reset
  - reschedule
  - swap task
- Step-by-step usage:
  1. The learner opens the plan.
  2. The learner works from today's items.
  3. The learner updates task state as work is completed or moved.
- Connected features / Dependencies:
  - diagnostic results
  - readiness
  - mocks
  - task-specific routes
- Edge cases / States:
  - empty state when no plan exists yet
- Notes:
  - This is an active planning tool, not a static recommendation page.
- What to test:
  - task completion and reset
  - swap and reschedule persistence
  - empty-state routing back to diagnostics

## 6. Writing Module

### 6.1 Writing Home and Library

- Status: `implemented`
- Purpose: Organize Writing practice, drills, and prior work.
- Business logic served: Creates a managed Writing practice program instead of a single-task experience.
- Location:
  - `/writing`
  - `/writing/library`
- Who uses it: Learners
- When it is used: Ongoing Writing preparation
- Inputs:
  - writing task list
  - prior submission data
  - review-credit state
- Outputs / Results:
  - recommended next Writing action
  - task library access
  - past submission access
- Main user actions:
  - browse library
  - start task
  - inspect past attempts
- Step-by-step usage:
  1. The learner enters the Writing home page.
  2. The learner chooses between library, criterion drills, and past submissions.
  3. The learner launches a task from the library or recommendation.
- Connected features / Dependencies:
  - writing player
  - submission history
  - expert review request

### 6.2 Writing Player

- Status: `implemented`
- Purpose: Provide a focused workspace for composing OET Writing responses.
- Business logic served: Supports realistic practice conditions and submission capture.
- Location: `/writing/player`
- Who uses it: Learners
- When it is used: During Writing practice or mock sections
- Inputs:
  - writing task
  - draft content
  - checklist state
  - timer
- Outputs / Results:
  - draft persistence
  - final submission
- Main user actions:
  - write response
  - inspect case notes
  - use scratchpad
  - autosave
  - submit
- Step-by-step usage:
  1. The learner opens a task.
  2. The learner reviews task materials and writes in the editor.
  3. The learner uses checklist and scratchpad support.
  4. The learner confirms submission.
- Connected features / Dependencies:
  - writing results
  - detailed feedback
  - revision mode
  - expert review request
- Edge cases / States:
  - leave guard
  - autosave
  - distraction-free mode

### 6.3 Writing Result

- Status: `implemented`
- Purpose: Summarize the first evaluation outcome after a Writing submission.
- Business logic served: Gives the learner an immediate, actionable interpretation of the attempt.
- Location: `/writing/result`
- Who uses it: Learners
- When it is used: After Writing submission
- Inputs:
  - writing result ID
  - evaluation status
- Outputs / Results:
  - estimated score or grade range
  - confidence
  - strengths
  - issues
- Main user actions:
  - inspect summary
  - navigate to detailed feedback
  - open revision mode
  - request expert review
- Step-by-step usage:
  1. The learner lands on the result page after submission.
  2. The page polls until evaluation is complete.
  3. The learner chooses a follow-up path.
- Connected features / Dependencies:
  - feedback
  - revision
  - expert review request
- Edge cases / States:
  - pending evaluation state
  - completed evaluation state

### 6.4 Writing Detailed Feedback

- Status: `implemented`
- Purpose: Break Writing performance into criterion-level, anchored feedback.
- Business logic served: Turns evaluation into revision-ready evidence.
- Location: `/writing/feedback`
- Who uses it: Learners
- When it is used: After a Writing result is available
- Inputs:
  - writing result
  - criterion-level feedback
  - anchored comments
- Outputs / Results:
  - criterion cards
  - omission list
  - unnecessary-detail list
  - revision suggestions
- Main user actions:
  - inspect criterion cards
  - open anchored comments
  - review issues summary
  - move to revision
- Step-by-step usage:
  1. The learner opens detailed feedback from the result page.
  2. The learner reviews criterion scoring and explanations.
  3. The learner opens anchored comments tied to highlighted text.
  4. The learner uses omissions and suggestions to plan revision.
- Connected features / Dependencies:
  - writing result
  - writing revision
  - submissions history
- Edge cases / States:
  - no anchored comments available
- What to test:
  - anchored-comment toggling
  - criterion issue summaries
  - transition from feedback to revision

### 6.5 Writing Revision Mode

- Status: `implemented`
- Purpose: Support targeted revision against known issues.
- Business logic served: Makes improvement a structured second attempt rather than a fresh restart.
- Location: `/writing/revision`
- Who uses it: Learners
- When it is used: After reviewing Writing feedback
- Inputs:
  - original attempt
  - criterion deltas
  - unresolved issues
- Outputs / Results:
  - revised submission
  - comparison support
- Main user actions:
  - revise response
  - inspect diff
  - review unresolved issues
  - resubmit revision
- Step-by-step usage:
  1. The learner enters revision mode from feedback or result.
  2. The learner reviews unresolved issues and criterion deltas.
  3. The learner edits with the original response as reference.
  4. The learner resubmits.
- Connected features / Dependencies:
  - detailed feedback
  - submissions compare

### 6.6 Writing Model Answer

- Status: `implemented`
- Purpose: Explain what a strong model response looks like and why.
- Business logic served: Gives the learner a high-quality exemplar tied to evaluation criteria.
- Location: `/writing/model`
- Who uses it: Learners
- When it is used: After feedback, or when the learner wants an exemplar
- Inputs:
  - model answer data
  - paragraph-by-paragraph rationale
- Outputs / Results:
  - interpreted model answer
- Main user actions:
  - read the model answer
  - inspect criterion rationale
  - inspect profession-language notes
- Connected features / Dependencies:
  - writing tasks
  - writing feedback

### 6.7 Writing Expert Review Request

- Status: `implemented`
- Purpose: Escalate a Writing attempt into human review.
- Business logic served: Adds expert trust where AI or self-revision is not sufficient.
- Location: `/writing/expert-request`
- Who uses it: Learners
- When it is used: After a Writing submission exists
- Inputs:
  - turnaround choice
  - focus areas
  - notes
  - billing or credit method
- Outputs / Results:
  - review request record
  - expert work creation
- Main user actions:
  - choose focus
  - choose turnaround
  - submit review request
- Step-by-step usage:
  1. The learner opens the review request page from a Writing result or history page.
  2. The learner chooses review priorities and turnaround.
  3. The learner submits the request using available entitlement or payment logic.
- Connected features / Dependencies:
  - billing
  - expert queue
  - submissions history
- What to test:
  - focus-area selection
  - entitlement display
  - successful request creation and visibility downstream

## 7. Speaking Module

### 7.1 Speaking Home and Task Selection

- Status: `implemented`
- Purpose: Organize Speaking practice and route the learner into task setup.
- Business logic served: Makes Speaking practice structured and review-aware.
- Location:
  - `/speaking`
  - `/speaking/selection`
- Who uses it: Learners
- When it is used: During Speaking preparation
- Inputs:
  - speaking task inventory
  - recent evidence
- Outputs / Results:
  - next speaking action
  - task selection entry
- Main user actions:
  - browse speaking tasks
  - launch device check

### 7.2 Speaking Device Check

- Status: `implemented`
- Purpose: Verify the learner can capture usable audio.
- Business logic served: Protects review quality and avoids invalid Speaking evidence.
- Location: `/speaking/check`
- Who uses it: Learners
- When it is used: Before speaking tasks
- Inputs:
  - microphone access
  - sample recording
- Outputs / Results:
  - saved device-check evidence
  - readiness to continue
- Main user actions:
  - record sample
  - playback sample
  - confirm readiness
- Connected features / Dependencies:
  - speaking task route
  - backend device-check endpoint

### 7.3 Speaking Role Card Preview

- Status: `implemented`
- Purpose: Present the speaking prompt and let the learner choose session mode.
- Business logic served: Bridges preparation from task selection into a specific speaking scenario.
- Location: `/speaking/roleplay/[id]`
- Who uses it: Learners
- When it is used: Before a speaking task starts
- Inputs:
  - task ID
  - role card
  - mode choice
- Outputs / Results:
  - selected attempt mode
  - transition into speaking task
- Main user actions:
  - read role card
  - use scratchpad
  - choose mode
- Notes:
  - `self` and `exam` are meaningful current modes.
  - `ai` is offered in the route logic, but the actual task page downgrades it to `self`.

### 7.4 Speaking Task Workspace

- Status: `partial`
- Purpose: Run the actual speaking attempt.
- Business logic served: Capture speaking evidence under guided or exam-like conditions.
- Location: `/speaking/task/[id]`
- Who uses it: Learners
- When it is used: During a speaking attempt
- Inputs:
  - selected task
  - selected mode
  - learner recording
- Outputs / Results:
  - submitted speaking attempt
  - generated result assets
- Main user actions:
  - record
  - stop
  - review
  - submit
- Step-by-step usage:
  1. The learner enters from role-card preview or device check.
  2. The learner records the response.
  3. The learner reviews and submits.
- Connected features / Dependencies:
  - speaking results
  - transcript
  - expert review request
- Edge cases / States:
  - recording state
  - review state
  - upload state
- Notes:
  - The route is real and usable.
  - Live AI mode is not available yet and is downgraded to self-guided behavior.

### 7.5 Speaking Result

- Status: `implemented`
- Purpose: Summarize the speaking evaluation outcome and route the learner into next steps.
- Business logic served: Translates a recording into a usable improvement plan.
- Location: `/speaking/results/[id]`
- Who uses it: Learners
- When it is used: After speaking submission
- Inputs:
  - result ID
  - evaluation assets
- Outputs / Results:
  - score range
  - confidence
  - strengths
  - improvements
- Main user actions:
  - open transcript
  - open phrasing drill
  - request expert review

### 7.6 Speaking Transcript and Phrasing Drills

- Status: `implemented`
- Purpose: Let the learner inspect recorded evidence and rehearse stronger phrasing.
- Business logic served: Supports self-correction between attempt and expert review.
- Location:
  - `/speaking/transcript/[id]`
  - `/speaking/phrasing/[id]`
- Who uses it: Learners
- When it is used: After speaking evaluation
- Inputs:
  - audio URL
  - transcript lines
  - markers and phrasing suggestions
- Outputs / Results:
  - transcript-backed self-review
  - guided phrase improvement
- Main user actions:
  - review markers
  - inspect transcript lines
  - jump by timestamp
  - practice improved phrases
- Connected features / Dependencies:
  - speaking result
  - expert review request

### 7.7 Speaking Expert Review Request

- Status: `implemented`
- Purpose: Escalate speaking evidence into expert feedback.
- Business logic served: Adds human judgment to communication quality, intelligibility, and appropriateness.
- Location: `/speaking/expert-review/[id]`
- Who uses it: Learners
- When it is used: After a speaking result exists
- Inputs:
  - focus areas
  - notes
  - turnaround choice
- Outputs / Results:
  - expert review request
- Main user actions:
  - choose review focus
  - submit request
- Connected features / Dependencies:
  - billing and credits
  - expert queue
- What to test:
  - request submission
  - focus-area capture
  - downstream expert visibility

## 8. Reading Module

- Status: `implemented`
- Purpose: Deliver objective reading practice and results.
- Business logic served: Supports one of the two objective OET sub-tests with direct answer-based practice.
- Location:
  - `/reading`
  - `/reading/player/[id]`
  - `/reading/results/[id]`
- Who uses it: Learners
- When it is used: During reading preparation or mock sections
- Inputs:
  - reading task
  - learner answers
- Outputs / Results:
  - submitted attempt
  - score and explanations
- Main user actions:
  - open reading task
  - navigate questions
  - answer and submit
  - inspect results
- Step-by-step usage:
  1. The learner launches a Reading task.
  2. The learner works through objective questions.
  3. The learner submits and opens the results page.
- Connected features / Dependencies:
  - progress
  - readiness
  - mocks
- Edge cases / States:
  - practice vs exam mode in player
- Notes:
  - Reading results are objective and explanation-driven rather than human reviewed.

## 9. Listening Module

- Status: `implemented`
- Purpose: Deliver objective listening practice with stronger post-attempt review support.
- Business logic served: Supports listening accuracy while exposing transcript-backed remediation.
- Location:
  - `/listening`
  - `/listening/player/[id]`
  - `/listening/results/[id]`
  - `/listening/review/[id]`
  - `/listening/drills/[id]`
- Who uses it: Learners
- When it is used: During listening preparation or mock sections
- Inputs:
  - listening task
  - learner answers
- Outputs / Results:
  - score
  - distractor explanations
  - review and drill follow-ups
- Main user actions:
  - play audio
  - answer questions
  - submit
  - inspect transcript-backed review
  - open drill page
- Step-by-step usage:
  1. The learner completes a listening attempt.
  2. The learner reviews score and explanation output.
  3. The learner opens transcript-backed review.
  4. The learner uses drills to target weak patterns.
- Connected features / Dependencies:
  - progress
  - readiness
  - mocks
- What to test:
  - practice vs exam playback constraints
  - results-to-review navigation
  - drill targeting behavior

## 10. Mock Center

### 10.1 Mocks Home and Setup

- Status: `implemented`
- Purpose: Let learners start full or single-subtest mocks and choose exam-like options.
- Business logic served: Provides readiness evidence beyond ordinary practice.
- Location:
  - `/mocks`
  - `/mocks/setup`
- Who uses it: Learners
- When it is used: Later-stage preparation or milestone checks
- Inputs:
  - mock type
  - sub-test scope
  - profession
  - practice vs exam mode
  - strict timer choice
  - productive-skill review add-on
- Outputs / Results:
  - mock session creation
- Main user actions:
  - configure mock
  - start mock session

### 10.2 Mock Player

- Status: `partial`
- Purpose: Orchestrate a mock session across one or more sub-tests.
- Business logic served: Treats mock-taking as a managed exam flow rather than just another task launch.
- Location: `/mocks/player/[id]`
- Who uses it: Learners
- When it is used: During a live mock session
- Inputs:
  - mock session ID
  - selected sections
- Outputs / Results:
  - section launch state
  - mock submission
- Main user actions:
  - open current section
  - submit session
- Step-by-step usage:
  1. The learner starts a mock.
  2. The player tracks current section status.
  3. The player launches the relevant sub-test route.
  4. The learner submits the mock session.
- Connected features / Dependencies:
  - Reading, Listening, Writing, and Speaking task routes
  - mock report
- Notes:
  - The orchestration page is implemented.
  - Some section launch routes still rely on hardcoded task IDs, so this surface is not fully content-driven yet.

### 10.3 Mock Report

- Status: `implemented`
- Purpose: Summarize overall mock performance and show where the learner should focus next.
- Business logic served: Turns mock evidence into planning and readiness input.
- Location: `/mocks/report/[id]`
- Who uses it: Learners
- When it is used: After mock completion
- Inputs:
  - mock report
  - prior comparison data
- Outputs / Results:
  - overall score
  - comparison trend
  - sub-test breakdown
  - weakest criterion
- Main user actions:
  - inspect overall performance
  - compare to prior mock
  - jump back to study plan
- Connected features / Dependencies:
  - study plan
  - readiness
  - progress
- What to test:
  - report loading from session completion
  - prior-comparison rendering
  - weakest-area CTA into study plan

## 11. Readiness

- Status: `implemented`
- Purpose: Show how prepared the learner appears to be across OET requirements.
- Business logic served: Converts practice and review evidence into a decision-support signal.
- Location: `/readiness`
- Who uses it: Learners
- When it is used: Ongoing preparation and pre-exam confidence checks
- Inputs:
  - diagnostic evidence
  - mock evidence
  - trend data
  - expert review counts
- Outputs / Results:
  - overall risk
  - recommended remaining study hours
  - sub-test readiness
  - blockers
- Main user actions:
  - inspect readiness by sub-test
  - review blockers
  - decide next focus area
- Connected features / Dependencies:
  - study plan
  - mocks
  - progress
  - submissions

## 12. Progress Analytics

- Status: `implemented`
- Purpose: Visualize learner progress over time.
- Business logic served: Gives the learner and operators a longitudinal view of improvement rather than only the latest attempt.
- Location: `/progress`
- Who uses it: Learners
- When it is used: Ongoing review of preparation progress
- Inputs:
  - trend data
  - completion data
  - submission volume
  - evidence summary
- Outputs / Results:
  - trend charts
  - completion trend
  - submission volume
  - expert turnaround visibility
- Main user actions:
  - inspect trend lines
  - compare sub-tests and criteria

## 13. Submission History and Compare

- Status: `implemented`
- Purpose: Give the learner a history of attempts, review state, and comparison views.
- Business logic served: Preserves evidence and makes improvement reviewable.
- Location:
  - `/submissions`
  - `/submissions/[id]`
  - `/submissions/compare`
- Who uses it: Learners
- When it is used: After multiple attempts exist
- Inputs:
  - submission records
  - per-attempt detail
- Outputs / Results:
  - history cards
  - detail pages
  - comparison views
- Main user actions:
  - open a prior attempt
  - reopen feedback
  - request review
  - compare attempts
- Connected features / Dependencies:
  - Writing result and feedback
  - Speaking results and transcript
  - expert review requests

## 14. Billing and Entitlements

- Status: `implemented`
- Purpose: Show learner-facing commercial options and entitlement usage.
- Business logic served: Controls access to plans, add-ons, and human review capacity.
- Location: `/billing`
- Who uses it: Learners
- When it is used: During plan purchase, upgrade, or review-credit decisions
- Inputs:
  - quote requests
  - coupon entry
  - plan state
- Outputs / Results:
  - plan previews
  - proration view
  - coupon impact
  - invoices
- Main user actions:
  - view plan and add-on choices
  - apply coupon
  - inspect invoice history
- Connected features / Dependencies:
  - Writing and Speaking expert review requests
  - admin billing configuration
- Notes:
  - The current product language and add-on design tie review-linked value most clearly to Writing and Speaking.
- What to test:
  - quote generation
  - coupon application
  - invoice download visibility

## 15. Settings and Learner Preferences

- Status: `implemented`
- Purpose: Let the learner manage profile, privacy, notifications, audio, accessibility, and study targets.
- Business logic served: Supports a personalized and operationally manageable preparation experience.
- Location:
  - `/settings`
  - `/settings/[section]`
- Who uses it: Learners
- When it is used: Ongoing account and experience management
- Inputs:
  - profile fields
  - privacy preferences
  - notification preferences
  - audio and accessibility preferences
  - study-related target data
- Outputs / Results:
  - persisted preference updates
- Main user actions:
  - open settings hub
  - edit by section
  - save section changes
- Sections confirmed:
  - profile
  - privacy
  - notifications
  - audio
  - accessibility
  - study
  - goals
- Connected features / Dependencies:
  - learner profile
  - notification governance
  - speaking audio experience
  - readiness target logic

## Learner QA Focus Areas

- onboarding -> goals -> diagnostic routing
- completion status propagation across diagnostic sub-tests
- study-plan task state mutation
- Writing submission -> result -> feedback -> revision -> review request
- Speaking selection -> device check -> task -> results -> transcript -> review request
- Reading and Listening exam-mode restrictions
- mock setup -> player -> report -> study-plan CTA
- readiness refresh after new evidence
- submission history compare and feedback reopening
- billing impact on review-request eligibility
- settings persistence by section

## Observed Gaps and Partial Implementations

- Diagnostic flows are implemented but still use fixed task IDs in the current UI.
- Speaking task mode selection exposes an AI path that is not functionally available.
- Mock player orchestration is real but not fully data-driven yet.
- Some UI copy contains encoding artifacts.
- Reading and Listening review flows are solid for objective practice, but the remediation depth is lighter than the productive-skill revision loop.
