
# Learner App Supplement — Global Inherited Requirements + Under-Specified Decisions for All 42 Features

## What this file is
This file supplements the original **OET Platform — Frontend Developer Handoff** for the **Learner App only**.

It exists to do two things:

1. collect the **global requirements that were defined elsewhere in the blueprint** but still apply to learner screens, and
2. identify what is **still not specified enough yet** for each of the 42 learner features, so the frontend developer does not silently invent product behavior.

This is intentionally written as a build handoff artifact for engineering.

---

## Read this before coding

### Source-of-truth order
1. **Original blueprint**
2. **This supplement**
3. **Approved product / design decisions made after this supplement**

### Strict rule for implementation
The developer must **use the current design system, current design-system elements, and current domain components** already defined for the platform.  
The developer must **not invent a new UI language, new patterns, new layout system, or screen-specific bespoke widgets** unless product/design explicitly approve them.

### Why this matters
The original blueprint is implementation-ready at the **feature inventory / route / capability** level, but not every interaction, state model, input model, algorithm, or component detail is fully resolved.  
This supplement makes those inherited requirements and open gaps visible so the build stays:
- OET-native
- high-trust
- consistent
- non-hallucinatory
- aligned to the current platform design system

---

## How to use this file
For each learner feature below, read the sections in this order:

1. **Inherited from elsewhere in the blueprint**  
   These are already required and should be built even if they were not repeated in the single-feature summaries.

2. **Not specified enough yet**  
   These are real gaps or unresolved decisions. Do **not** quietly invent a final product answer.

3. **Provisional build assumption**  
   These are safe recommendations to unblock engineering when product/design have not yet closed the gap.  
   Treat them as **recommended defaults**, not as retroactive product requirements.

---
## Core implementation rule
- This document is a supplement to the original blueprint, not a replacement for it.
- Where the original blueprint explicitly defines behavior, that blueprint remains the source of truth.
- Where this document lists an item under **Not specified enough yet**, that is a real gap or an explicit open decision from the blueprint; the developer must not invent a bespoke answer without product/design sign-off.
- Where this document gives a **Provisional build assumption**, that is a safe implementation recommendation to unblock work, not a newly invented product requirement.

## Non-negotiable design-system rule
- Use the **current design system, current primitives, and current domain-specific components** already defined for the platform.
- Do **not** invent a new visual language, new layout paradigm, new navigation model, or ad-hoc screen-specific widgets without approval.
- Prefer these existing shared primitives where relevant: AppShell, Sidebar, TopNav, BottomNav, Card, Table, FilterBar, Tabs, Accordion, Modal / Drawer, Toast / Inline alert, Skeleton loader, Empty state, Error state, Retry state, Audio player, Waveform viewer, Transcript viewer, Rich text editor / structured writing editor, Timer, Progress bar, Stepper, Status badge, Score range badge, Confidence badge, Criterion chip, Review comment anchor, Task card, Submission card.
- Prefer these existing domain components where relevant: ProfessionSelector, SubtestSwitcher, CriterionBreakdownCard, ReadinessMeter, WeakestLinkCard, StudyPlanItem, WritingCaseNotesPanel, WritingEditor, WritingIssueList, RevisionDiffViewer, SpeakingRoleCard, MicCheckPanel, TranscriptFlagList, BetterPhraseCard, MockReportSummary, ReviewRequestDrawer.
- If a needed pattern is not in the current design system, first try composition of existing primitives. Escalate for design approval before creating a new component.

## Learner app shell and layout inheritance
- All learner routes must live inside the learner AppShell.
- Use top app bar with context-aware title and actions.
- Use left sidebar on desktop and bottom navigation on mobile / compact tablet layouts.
- Keep the persistent 'next recommended action' strip/pattern on dashboard and study surfaces where applicable.
- Task views should support distraction-free mode; do not apply distraction-free mode to every screen by default.

## Visual tone inheritance
- The UI must feel clinical, clean, high-trust, exam-focused, efficient, and professional.
- No childish gamification.
- Use status colors conservatively.
- Estimated results must never feel like official scores.

## Mobile inheritance
- The product is web-first but must be highly usable on mobile.
- Navigation must be responsive.
- Task/result screens should use sticky primary CTA where needed.
- Transcript and feedback cards must remain readable on small screens.
- Writing editor is expected to be optimized for tablet and mobile landscape where possible.
- Audio upload must be reliable from mobile networks.

## Accessibility inheritance
- Keyboard navigation for all core paths is required.
- Visible focus states are required.
- Accessible form labels and validation are required.
- High contrast compliance is required.
- Screen-reader support is required on dashboard, forms, and results pages, and should extend to all learner-critical flows.
- Captions/transcript access where relevant is required.
- Typography must scale cleanly.
- Reduced-motion preference support is required.

## Component state inheritance
- Every component must support loading, empty, success, partial data, error, permission denied where relevant, and stale data warning where relevant.
- Not every state will appear on every screen, but every screen/component must be designed with the relevant subset explicitly handled.

## Integration and async inheritance
- All core pages must tolerate partial data.
- Optimistic updates are allowed only where low risk.
- Evaluations, study plans, and reviews need explicit loading and stale states.
- Speaking transcription, speaking evaluation, writing evaluation, human review completion, study-plan regeneration, and report generation need queued / processing / completed / failed states.
- Use polling or subscription support for async evaluation states, but the blueprint leaves the final mechanism open.

## Frontend architecture inheritance
- Frontend stack recommendation: React + TypeScript, route-based code splitting, query library for server state, lightweight client store for session/task-local state, form library with schema validation, and design system as shared module/package.
- State layers: server state for content/attempts/evaluations/plans/reviews; local UI state for filters/drawers/editors/playback; persisted client state for draft recovery and onboarding progress where appropriate.
- Writing editor must preserve unsaved content protection, local recovery snapshot, server draft sync state, and timer state.
- Speaking tasks must preserve audio upload state, transcript availability state, and recording interruption state.

## Validation, empty-state, and error inheritance
- Validation should be immediate but calm.
- Required diagnostic/goal fields must show clear reason and fix path.
- Every empty state must guide action.
- Every error state must support retry and preserve user work where possible.
- Critical error states needing special handling include writing draft save failure, speaking audio upload failure, evaluation timeout, and review purchase/entitlement mismatch.

## Analytics inheritance
- Track at minimum: onboarding started/completed, goals saved, diagnostic started/completed, task started/submitted, evaluation viewed, revision started/submitted, mock started/completed, review requested, readiness viewed, plan item completed/skipped/rescheduled, subscription started/changed.
- Every event should include where relevant: user id, profession, sub-test, content id, attempt id, evaluation id, mode, device type, timestamp.

## Performance inheritance
- Dashboard should become interactive within acceptable modern app standards on average broadband.
- Local route transitions should feel immediate.
- Writing editor input latency must stay low during long essays.
- Transcript view must handle long content without jank.
- Admin/expert tables must use virtualization or efficient pagination where needed; learner history/reporting surfaces should also avoid unnecessary DOM bloat.

## Security and privacy inheritance
- Role-based route protection is required.
- Avoid exposing hidden admin/expert routes in learner bundles where practical.
- Use signed upload flows for audio.
- Do not expose sensitive scoring config client-side.
- Handle tokens and session refresh securely.
- Show explicit consent messaging for audio capture where required.

## QA inheritance
- Learner critical path QA includes onboarding, diagnostic end-to-end, writing draft save/restore, speaking record/upload/evaluate, study-plan update after evaluated task, review request flow, and billing flow.
- Device coverage includes latest desktop Chrome/Safari/Edge, tablet for writing/result review, and mobile iOS/Android browsers for diagnostics, speaking, feedback, progress, and billing.

## Frontend release-slice context
- Slice 1: app shell, auth, onboarding/goals, dashboard skeleton, study plan read-only, taxonomy integration.
- Slice 2: Writing library, Writing player, Writing result view, Writing revision view, expert review request.
- Slice 3: Speaking home, mic check, speaking task flow, transcript review, better phrasing view.
- Slice 4: Reading/Listening flows, mock center, readiness center, progress/history.
- Slice 5: expert console, admin/CMS, quality dashboards.
- This matters because a developer may build learner features incrementally, but each slice still inherits the global rules above.

## Explicit open decisions already named by the blueprint
- Rich text editor strategy for Writing: plain text with structured helpers vs richer editor.
- Waveform library choice for Speaking review.
- Real-time vs polling for evaluation state changes.
- Dark mode from v1 or later.
- Mobile authoring experience scope for long Writing tasks.

## Feature-by-feature learner supplement

### 1. Onboarding Entry

**Route**: `/app/onboarding`

**Use current design-system / domain components**
- AppShell
- TopNav
- Sidebar / BottomNav
- Card
- Stepper
- Inline alert
- Button
- existing design-system typography, spacing, and form/action primitives

**Inherited from elsewhere in the blueprint**
- Must use the learner app shell with context-aware page title and platform navigation; do not create a standalone marketing-style landing layout.
- Must follow the clinical, clean, high-trust visual tone; no playful or childish onboarding treatment.
- Must be fully responsive, with the same core message and CTA available on mobile/tablet; stepper and explainer cards must remain readable on small screens.
- Must support keyboard navigation, visible focus states, screen-reader labels, scalable type, high contrast, and reduced-motion preference.
- Must implement loading, success, partial data, error, and retry states where server-backed onboarding/session data is involved.
- Must preserve partial onboarding progress according to the global persisted client-state requirement where appropriate.
- Must track analytics for onboarding started and onboarding completed, including user id, profession if already known, device type, and timestamp.

**Not specified enough yet**
- Exact onboarding step count and final step order are not specified beyond the existence of a progress stepper.
- The exact content and count of the short explainer cards are not specified.
- It is not specified whether the learner can skip onboarding, exit and resume later from this first screen, or dismiss explainer cards.
- It is not specified whether onboarding is a modal flow, a multi-route flow, or a single route with internal steps.

**Provisional build assumption**
- Implement onboarding as a multi-step in-app flow under the learner AppShell, with a persistent stepper and server/local progress recovery.
- Use 3 concise explainer cards only if content is available from product/content; otherwise ship one summary block and keep the stepper truthful.

---

### 2. Goal Setup

**Route**: `/app/goals`

**Use current design-system / domain components**
- AppShell
- TopNav
- Sidebar / BottomNav
- Card
- Stepper
- ProfessionSelector
- existing design-system form controls, validation messaging, date picker, selects, radios, and buttons

**Inherited from elsewhere in the blueprint**
- Must use calm immediate validation, with accessible labels and clear error/fix messaging.
- Must save partial progress and allow later return, aligned with persisted client-state and server-state rules.
- Must work on mobile with stacked form sections and sticky save/continue CTA where appropriate.
- Must support permission-safe partial data states because not all goal fields are required.
- Must not block users who do not know exam date or sub-test targets yet; this comes from the blueprint edge cases.
- Must emit analytics for goals saved, and include profession, sub-test targets where entered, device type, and timestamp.
- Must keep the professional, exam-focused tone; do not frame this as a casual preferences wizard.

**Not specified enough yet**
- The profession taxonomy source of truth is not specified here, though an admin taxonomy exists elsewhere in the platform.
- The exact target score input model is not specified: numeric score, grade band, score range, or both.
- The structure for previous attempts is not defined: simple count, dates plus scores, or free text.
- The weak sub-test self-report input style is not defined: single select, multi-select, or ranked preference.
- Study-hours-per-week input behavior is not defined: numeric only, presets, or range.
- Target country / organization is optional, but the allowed list vs free text is not defined.

**Provisional build assumption**
- Use controlled form sections with autosave-on-blur plus explicit save/continue action.
- Represent missing exam date and missing sub-test targets as explicit nullable values rather than placeholder fake defaults.
- If product has not defined the target score model, store both raw user-entered value and normalized internal fields so backend mapping can evolve.

---

### 3. Diagnostic Intro

**Route**: `/app/diagnostic`

**Use current design-system / domain components**
- AppShell
- TopNav
- Sidebar / BottomNav
- Card
- Inline alert
- Button
- Progress bar / Stepper

**Inherited from elsewhere in the blueprint**
- Must clearly present the trust-first notice that the result is a training estimate, not an official score.
- Must show estimated duration in a high-visibility location; this aligns to the time-poor user UX principle.
- Must remain readable and actionable on mobile, with sticky primary CTA if needed.
- Must support loading/error states if diagnostic eligibility, prior progress, or entitlements are fetched asynchronously.
- Must track diagnostic started when the learner actually begins the flow, not merely views the intro.

**Not specified enough yet**
- The exact diagnostic component list presentation is not specified: summary bullets, cards, or accordion.
- The duration source is not specified: fixed estimate, dynamically calculated estimate, or personalized estimate.
- It is not specified whether the learner can skip one or more sub-tests in the diagnostic path.
- The placement and wording of any upgrade prompt or gating at the intro step is not defined.

**Provisional build assumption**
- Display the four sub-tests with a single total duration estimate plus optional per-sub-test durations if available.
- Keep the non-official-score notice above the start CTA rather than burying it below the fold.

---

### 4. Diagnostic Hub

**Route**: `/app/diagnostic`

**Use current design-system / domain components**
- AppShell
- TopNav
- Sidebar / BottomNav
- Card
- Progress bar
- Status badge
- Button

**Inherited from elsewhere in the blueprint**
- Must use the learner app shell rather than a special diagnostic-only chrome unless a task screen explicitly supports distraction-free mode.
- Must show four diagnostic cards for the four sub-tests and preserve progress state between visits.
- Must support partial data and stale data states because diagnostic progress can be async and resumable.
- Must support strong empty/error states if no diagnostic session exists or if a resume token has expired.
- Must be mobile-usable, with cards that preserve progress visibility and resume actions.
- Must emit analytics for diagnostic started/completed at the sub-test and overall level where relevant.

**Not specified enough yet**
- The exact status model per diagnostic card is not defined: not started, in progress, completed, failed, needs retry, etc.
- It is not specified whether the user may complete sub-tests in any order or a prescribed order.
- Resume-later behavior is required, but the resume key/state model is not specified.
- It is not defined whether the hub displays prior diagnostic history or only the current in-progress session.

**Provisional build assumption**
- Use per-card statuses of not started / in progress / completed / failed, with resume only on in-progress items.
- Allow free order unless backend or product later imposes gating.

---

### 5. Writing Diagnostic Task

**Route**: `/app/diagnostic (writing task step or dedicated internal state)`

**Use current design-system / domain components**
- AppShell or distraction-free task layout
- WritingCaseNotesPanel
- WritingEditor
- Timer
- Checklist
- Status badge
- Inline alert
- Modal / confirm dialog
- existing design-system scratchpad area

**Inherited from elsewhere in the blueprint**
- Must support distraction-free mode for task views per global layout rules.
- Must preserve unsaved content protection, local recovery snapshot, server draft sync state, and timer state per editor-specific architecture requirements.
- Must save locally where possible during failure states and protect work during draft save failure.
- Must support high-contrast, keyboard navigation, focus visibility, scalable type, and reduced-motion.
- Must be desktop and tablet optimized for authoring; mobile viewing may exist, but authoring scope on small phones is not fully specified.
- Must support loading, stale, retry, and explicit queued/processing/completed/failed states if evaluation kicks off from this screen.
- Must emit task started and task submitted analytics with content id, attempt id, sub-test, mode, device type, and timestamp.

**Not specified enough yet**
- The exact timed-mode defaults for diagnostic Writing are not specified.
- The checklist contents and source of truth are not defined.
- Auto-save cadence is described only as present, not with an interval or debounce strategy.
- It is not specified how local draft recovery should resolve conflicts against fresher server content.
- The scratchpad behavior and persistence are not specified.
- The mobile landscape/tablet layout breakpoints are not defined.

**Provisional build assumption**
- Implement local draft snapshot + server draft sync with visible save states: saving, saved, offline-saved, failed.
- Show a confirm-leave modal only when there is unsynced or recently changed content.

---

### 6. Speaking Diagnostic Task

**Route**: `/app/diagnostic (speaking task step or dedicated internal state)`

**Use current design-system / domain components**
- AppShell or distraction-free task layout
- MicCheckPanel
- Timer
- Audio recorder / uploader
- Status badge
- Inline alert
- Audio player
- Button

**Inherited from elsewhere in the blueprint**
- Must explicitly handle audio capture consent messaging where required by frontend privacy requirements.
- Must use signed upload flows for audio and avoid exposing sensitive scoring config client-side.
- Must preserve audio upload state, transcript availability state, and recording interruption state.
- Must expose queued, processing, completed, and failed states for transcription/evaluation.
- Must be reliable on mobile networks and support mobile-safe upload behavior.
- Must preserve user work and guide retry if upload or transcription fails.
- Must emit task started and task submitted analytics with attempt and content identifiers.

**Not specified enough yet**
- The blueprint requires record/upload flow, but does not specify whether both are always available or environment-dependent.
- Allowed upload formats, max file size, and max recording length are not specified.
- The prep timer duration and whether it is configurable are not defined.
- The transcript preview polling strategy is not specified: polling vs subscription vs refresh CTA.
- Noise handling and mic-permission retry behavior are not specified in detail here.

**Provisional build assumption**
- Treat recording as primary and upload as fallback unless product explicitly wants both equal.
- Display transcript preview only after successful processing; never promise immediate availability.

---

### 7. Reading Diagnostic

**Route**: `/app/diagnostic (reading task step or dedicated internal state)`

**Use current design-system / domain components**
- AppShell or task layout
- Timer
- Progress bar
- Question navigation
- Inline alert
- Button

**Inherited from elsewhere in the blueprint**
- Must preserve answer persistence during navigation and reloads where appropriate.
- Must keep input latency and transitions fast under the performance targets for interactive task flows.
- Must support keyboard navigation and accessible question controls.
- Must support loading/error/retry states if questions or saved answers are fetched asynchronously.
- Must track task started/submitted analytics and include mode and attempt id where relevant.

**Not specified enough yet**
- The exact question navigation pattern is not specified: numbered nav, next/previous only, or section tabs.
- Time behavior is not defined in detail: countdown vs elapsed, pause rules, and timeout handling.
- It is not specified whether review-before-submit is available in diagnostic mode.
- The persistence trigger is not defined: on every selection, page turn, debounce, or manual save.

**Provisional build assumption**
- Persist answers on every interaction and on navigation transition to minimize data loss.
- Use a numbered question navigator plus next/previous if the design system already supports it.

---

### 8. Listening Diagnostic

**Route**: `/app/diagnostic (listening task step or dedicated internal state)`

**Use current design-system / domain components**
- AppShell or task layout
- Audio player
- Timer
- Question navigation
- Inline alert
- Button

**Inherited from elsewhere in the blueprint**
- Must provide stable audio controls and safe mobile playback behavior.
- Must preserve answers during playback and navigation.
- Must support accessibility for playback controls and question inputs.
- Must support performance and reliability on weaker mobile networks.
- Must track task started/submitted analytics with sub-test and attempt identifiers.

**Not specified enough yet**
- Replay policy is not specified for diagnostic mode.
- The exact mobile audio behavior is not defined: backgrounding, interruption recovery, headphone changes, or screen lock behavior.
- Timeout and end-of-audio behavior are not specified.
- The exact transition model between listening items is not defined.

**Provisional build assumption**
- Preserve answer state on each interaction and pause-safe lifecycle event.
- Do not add advanced playback controls unless already part of the current design system and exam mode rules.

---

### 9. Diagnostic Results

**Route**: `/app/diagnostic/results`

**Use current design-system / domain components**
- AppShell
- ReadinessMeter
- WeakestLinkCard
- CriterionBreakdownCard
- Card
- Score range badge
- Confidence badge
- StudyPlanItem
- Inline alert
- Button

**Inherited from elsewhere in the blueprint**
- Must clearly present all estimates as training estimates and never official results.
- Must tolerate partial data because some sub-tests may be incomplete or still processing.
- Must surface async evaluation states for any still-pending components.
- Must use strong empty/error/retry behavior when evaluation data is delayed or partially failed.
- Must be understandable even when scoring is probabilistic, aligning to the frontend mission.
- Must be mobile-readable, especially cards for readiness, blockers, criteria, and first-week suggestion.
- Must emit evaluation viewed analytics and support upgrade-prompt analytics if relevant.

**Not specified enough yet**
- The exact scoring-range and confidence-label taxonomy are not defined.
- The calculation and display logic for recommended intensity and first study week suggestion are not specified.
- The logic for likely blockers and top weak criteria derivation is not specified.
- The entitlement/business logic for upgrade prompts is not defined here.
- It is not specified how results behave if not all four sub-tests are completed.

**Provisional build assumption**
- Use separate cards for each sub-test readiness plus one overall action plan section.
- If any sub-test remains pending, show completed sections plus a partial-data warning instead of blocking the full page.

---

### 10. Dashboard Home

**Route**: `/app/dashboard`

**Use current design-system / domain components**
- AppShell
- TopNav
- Sidebar / BottomNav
- Card
- ReadinessMeter
- WeakestLinkCard
- StudyPlanItem
- Score range badge
- Status badge
- Submission card
- Empty state
- Skeleton loader
- Button

**Inherited from elsewhere in the blueprint**
- Must include the persistent next recommended action behavior required in learner layout rules.
- Must remain useful for low-activity users through strong empty states.
- Must support loading, partial data, stale data, error, and retry states because the dashboard aggregates multiple async sources.
- Must be highly performant because dashboard interactivity is explicitly a performance target.
- Must support mobile with sticky primary CTA where result/task cards require it.
- Must track readiness viewed, evaluation viewed, plan actions, and other dashboard CTA interactions.

**Not specified enough yet**
- The ranking/priority logic between today’s tasks, latest submission, next mock, and pending reviews is not specified.
- The exact definition of streak/completion momentum is not specified.
- The fallback logic when exam date is missing or outdated is not specified.
- The personalization rules for low-activity users are not defined beyond needing strong empty states.

**Provisional build assumption**
- Prioritize one primary CTA at the top based on the most urgent actionable item; do not surface multiple competing primary actions.
- If there is no activity, default the command-center action to start diagnostic or complete goals.

---

### 11. Study Plan

**Route**: `/app/study-plan`

**Use current design-system / domain components**
- AppShell
- StudyPlanItem
- Card
- Status badge
- Tabs or section headers
- Button
- Drawer / Modal

**Inherited from elsewhere in the blueprint**
- Must use the persistent recommendation/next action pattern from the learner layout rules.
- Must support optimistic updates only where low risk; plan mutations should surface stale/retry states if regeneration is async.
- Must support queued/processing/completed/failed states for study plan regeneration.
- Must support empty states when no plan exists and guide the user to diagnostic or goal completion.
- Must be mobile-usable, with plan actions possible without deep navigation.
- Must track plan item completed, skipped/rescheduled, or swapped analytics.

**Not specified enough yet**
- The exact item-type taxonomy is not specified: task, drill, checkpoint, mock, review, etc.
- Swap behavior is not specified: swap with same sub-test, same duration, or any recommendation pool.
- Reschedule behavior is not specified: date picker only, drag-and-drop, or quick presets.
- Mark complete semantics are not defined: manual completion vs auto-completion after task finish.
- Retake rescue mode trigger rules are not specified.

**Provisional build assumption**
- Use server-backed mutations for start/reschedule/swap/complete and refresh the plan after each successful mutation.
- Keep action menus shallow; expose the four specified actions directly on each item or in a simple overflow menu.

---

### 12. Writing Home

**Route**: `/app/writing`

**Use current design-system / domain components**
- AppShell
- Card
- FilterBar
- Task card
- Submission card
- Status badge
- Button

**Inherited from elsewhere in the blueprint**
- Must present an exam-focused module home, not a generic content library.
- Must support filters with accessible controls and visible active state.
- Must support empty states for no submissions, no credits, or no recommended tasks.
- Must support partial data because recommendations, library content, and credit balances may load from different services.
- Must emit analytics for module entry, task starts from module home, and review-credit interactions.

**Not specified enough yet**
- The default ranking/order of the recommended task, practice library, and criterion drills is not specified beyond section presence.
- The exact filter interaction model is not specified: multi-select, chips, dropdowns, or drawer on mobile.
- The criterion drill taxonomy is not defined in detail.
- The display format for expert review credits is not specified.

**Provisional build assumption**
- Place the recommended Writing task first and keep full mock entry below practice/review surfaces unless analytics later prove otherwise.
- Use the current design-system FilterBar and current card patterns; do not invent a new Writing-specific navigation pattern.

---

### 13. Writing Task Library

**Route**: `/app/writing/tasks`

**Use current design-system / domain components**
- AppShell
- FilterBar
- Task card
- Empty state
- Skeleton loader
- Button

**Inherited from elsewhere in the blueprint**
- Must use current task-card and filter patterns from the shared design system.
- Must support loading/empty/error/retry states for library fetches.
- Must remain mobile-usable with responsive filter treatment and readable task cards.
- Must support stale data warning where content metadata may have changed.
- Must track task library filter usage and task-start events.

**Not specified enough yet**
- Search, sort, pagination, and infinite scrolling are not specified.
- The card primary action is not specified: open details, start now, or choose mode first.
- It is not defined whether cards show availability/entitlement status.
- The exact meaning and source of scenario type are not detailed.

**Provisional build assumption**
- Use paginated or virtualized loading only if needed for performance; otherwise keep the library simple.
- Use one primary action per card and route to task details/start according to backend capability.

---

### 14. Writing Player

**Route**: `/app/writing/tasks/:id or /app/writing/attempt/:attemptId`

**Use current design-system / domain components**
- AppShell or distraction-free task layout
- WritingCaseNotesPanel
- WritingEditor
- Timer
- Checklist
- Status badge
- Inline alert
- Modal / Drawer
- Button

**Inherited from elsewhere in the blueprint**
- Must preserve unsaved content protection, local recovery snapshot, server draft sync state, and timer state.
- Must support distraction-free mode, visible save status, and low input latency under long essays.
- Must support dark mode only if dark mode is enabled platform-wide.
- Must support desktop and tablet authoring well; mobile must at least support result/feedback consumption if full phone authoring is not approved.
- Must save locally where possible during draft-save failure and preserve work under errors.
- Must support analytics for task started/submitted and potentially draft recovery events if the analytics plan includes them.

**Not specified enough yet**
- The exact Writing editor strategy remains an explicit open frontend decision in the blueprint.
- Auto-save cadence is specified only as every few seconds, not with exact thresholds.
- The exact scratchpad behavior and whether it syncs are not specified.
- Timer behavior by mode, including pause/resume rules, is not specified.
- Font-size control increments and boundaries are not specified.
- The exact distraction-free treatment (hide nav, collapse side panels, full-screen) is not defined.

**Provisional build assumption**
- Use a robust plain-text or minimally structured editor unless product has approved richer formatting helpers; do not expose formatting that conflicts with exam realism.
- Prioritize stability and recovery over advanced editing affordances.

---

### 15. Writing Result Summary

**Route**: `/app/writing/result/:evaluationId`

**Use current design-system / domain components**
- AppShell
- Score range badge
- Confidence badge
- Card
- Status badge
- Button
- Inline alert

**Inherited from elsewhere in the blueprint**
- Must present estimates as ranges and confidence-based guidance, never official or overconfident results.
- Must support queued/processing/completed/failed states because writing evaluation is async.
- Must support stale data warning where evaluation is updated after initial display.
- Must provide clear next-step CTAs and remain readable on small screens.
- Must emit evaluation viewed analytics and downstream CTA analytics.

**Not specified enough yet**
- The exact score-range UI format is not specified.
- The grade-range mapping model is not specified.
- The confidence-label taxonomy and thresholds are not specified.
- The logic for top strengths and top issues summarization is not specified.
- The visibility rules for expert review CTA based on plan/credits are not specified.

**Provisional build assumption**
- Display score range, grade range, and confidence label in one summary header, with the trust notice nearby.
- Keep the summary high-level and route detailed reasoning to the detailed feedback screen.

---

### 16. Writing Detailed Feedback

**Route**: `/app/writing/result/:evaluationId`

**Use current design-system / domain components**
- AppShell
- CriterionBreakdownCard
- Criterion chip
- Review comment anchor
- WritingIssueList
- Accordion / Tabs
- Card
- Button

**Inherited from elsewhere in the blueprint**
- Must be criterion-first and cover all six official Writing criteria.
- Must connect feedback to criteria rather than generic commentary, per product principles.
- Must support anchored comments and readable transcript/text review on smaller screens.
- Must tolerate partial data if some feedback blocks are delayed or unavailable.
- Must support accessibility for anchor navigation, headings, and comment associations.
- Must emit evaluation viewed analytics and potentially comment-anchor interaction analytics if instrumented.

**Not specified enough yet**
- The exact criterion score display model is not specified: numeric, banded, qualitative, or mixed.
- The anchor UX is not specified: side-by-side highlights, inline markers, or click-to-jump notes.
- The grouping logic for omissions vs unnecessary details vs revision suggestions is not specified.
- It is not specified whether comments can belong to multiple criteria.

**Provisional build assumption**
- Use one criterion section per official criterion with its own score card, explanation, and linked comment anchors.
- Treat omissions and unnecessary details as separate issue groups to preserve clarity.

---

### 17. Writing Revision Mode

**Route**: `/app/writing/revision/:attemptId`

**Use current design-system / domain components**
- AppShell
- RevisionDiffViewer
- WritingEditor
- WritingIssueList
- CriterionBreakdownCard
- Card
- Status badge
- Button

**Inherited from elsewhere in the blueprint**
- Must preserve long-task reliability, including local recovery, save failure handling, and unsaved-content protection where the revised text is editable.
- Must support desktop/tablet well and remain viewable on smaller screens even if full authoring is less comfortable.
- Must support loading, stale, and error states because original submission, revision, and evaluation deltas may come from different sources.
- Must emit revision started/submitted analytics.

**Not specified enough yet**
- The exact diff method is not specified: word-level, sentence-level, or paragraph-level emphasis.
- The criterion delta summary model is not specified.
- The unresolved issue list calculation logic is not defined.
- It is not specified whether the learner edits directly in split view or via a dedicated revision pane.

**Provisional build assumption**
- Keep original content read-only and revised content editable; do not allow mutation of the original artifact.
- Use visual diffing that is readable first, not overly granular if it harms comprehension.

---

### 18. Model Answer Explainer

**Route**: `/app/writing/model-answer/:contentId`

**Use current design-system / domain components**
- AppShell
- Card
- Accordion / Tabs
- Criterion chip
- Inline annotations
- Button

**Inherited from elsewhere in the blueprint**
- Must remain profession-specific and OET-native; avoid generic writing-coach language.
- Must be readable on mobile/tablet, especially long annotated answers.
- Must support loading/empty/error states because content may be profession- and task-specific.
- Must use current typography/card/annotation patterns from the design system; do not invent a special teaching UI.
- Must emit content-view analytics if model-answer consumption is tracked.

**Not specified enough yet**
- The annotation model is not specified: inline callouts, side notes, collapsible rationale blocks, or layered tabs.
- The paragraph-level rationale authoring format is not defined.
- The source and structure of include/exclude note logic are not defined.
- Profession-specific language note taxonomy is not specified.

**Provisional build assumption**
- Prefer annotation blocks adjacent to relevant paragraphs rather than a detached explanation list.
- Keep criterion mapping explicit for each relevant paragraph or section.

---

### 19. Writing Expert Review Request

**Route**: `/app/reviews or in-context request flow from writing result`

**Use current design-system / domain components**
- AppShell
- ReviewRequestDrawer or form card
- Card
- Inline alert
- Status badge
- Button
- existing design-system form controls

**Inherited from elsewhere in the blueprint**
- Must clearly distinguish expert review from AI evaluation, consistent with the trust-first product principle.
- Must support billing/credit integration states, including entitlement mismatch, purchase errors, and retry guidance.
- Must handle partial data and stale credit balances safely; avoid optimistic mutation on financial/entitlement actions unless low risk.
- Must be accessible and mobile-usable for a short transactional form.
- Must emit review requested analytics and billing-related interaction analytics if relevant.

**Not specified enough yet**
- The allowed turnaround-speed options are not specified.
- The focus-area taxonomy is not defined.
- The note length and formatting limits are not specified.
- The exact payment vs credit selection rules are not specified.

**Provisional build assumption**
- Show the learner exactly what will be submitted to the reviewer and what it will cost in credits/payment before final confirmation.
- Do not hide entitlement failures; surface them inline with recovery actions.

---

### 20. Speaking Home

**Route**: `/app/speaking`

**Use current design-system / domain components**
- AppShell
- Card
- Task card
- Status badge
- Submission card
- Button

**Inherited from elsewhere in the blueprint**
- Must function as an action-oriented module home, not a generic resource list.
- Must support empty states for no past attempts and no credits.
- Must support partial data because recommendations, drills, and past attempts may load independently.
- Must be mobile-usable, since speaking flows are especially likely on phones/tablets.
- Must emit task-start, drill-entry, and review-credit analytics from this screen.

**Not specified enough yet**
- The ranking logic for recommended role play vs drill sections is not specified.
- The exact structure of common issues, pronunciation drills, and empathy/clarification drills is not defined here.
- The past-attempt summary card contents are not specified.
- The display format for expert review credits is not specified.

**Provisional build assumption**
- Keep recommended role play first, then issue/drill sections, then past attempts/credits.
- Reuse current task-card and submission-card patterns rather than inventing speaking-specific cards.

---

### 21. Mic and Environment Check

**Route**: `/app/speaking/mic-check`

**Use current design-system / domain components**
- AppShell
- MicCheckPanel
- Audio player
- Inline alert
- Status badge
- Button

**Inherited from elsewhere in the blueprint**
- Must support explicit audio-capture consent messaging where required.
- Must handle microphone permission errors, retries, and device/browser compatibility states.
- Must remain usable on mobile and weak devices, since this is a preflight gate for speaking tasks.
- Must support accessibility for permission prompts and playback controls.
- Must emit speaking-start-precheck analytics if instrumented.

**Not specified enough yet**
- The noise warning threshold and detection strategy are not specified.
- The exact compatibility matrix and what qualifies for a device warning are not specified.
- The sample recording length and whether it is fixed are not specified.
- It is not specified whether this check is mandatory before every speaking attempt or skippable after success.

**Provisional build assumption**
- Treat mic permission, record test, playback test, and noise warning as separate checklist steps inside one panel.
- Persist a recent successful check for a limited period only if product approves.

---

### 22. Speaking Task Selection

**Route**: `/app/speaking/tasks`

**Use current design-system / domain components**
- AppShell
- FilterBar
- Task card
- Empty state
- Skeleton loader
- Button

**Inherited from elsewhere in the blueprint**
- Must use shared library/filter patterns and responsive filter behavior.
- Must support loading/empty/error/retry states.
- Must remain readable and tappable on mobile.
- Must track task-selection filters and task-start interactions.

**Not specified enough yet**
- The filter model is not specified for this screen even though metadata exists.
- Search, sorting, and pagination are not specified.
- The relation between task selection and mode selection is not defined: before task, after task, or inside task.
- The meaning/source of criteria focus for speaking tasks is not specified in detail.

**Provisional build assumption**
- Use one primary action per task card and defer unsupported options to the next screen rather than crowding the card.
- Reuse current task library patterns from Writing where possible for consistency.

---

### 23. Role Card Preview

**Route**: `/app/speaking/task/:id`

**Use current design-system / domain components**
- AppShell
- SpeakingRoleCard
- Timer
- Card
- existing design-system notes area
- Button

**Inherited from elsewhere in the blueprint**
- Must support mobile readability because role-card review may happen on smaller devices.
- Must support distraction-free focus even if full nav remains available outside active recording.
- Must support accessible timing and note-taking controls.
- Must track task started when the learner leaves preview and begins the live task.

**Not specified enough yet**
- The prep timer duration source is not specified.
- The persistence and storage of notes are not specified.
- It is not specified whether the learner can return to preview once the live task has started.
- The exact role-card scroll/overflow behavior on small screens is not defined.

**Provisional build assumption**
- Keep role card content immutable and notes learner-local unless backend needs persistence.
- Show the prep timer prominently beside the role card rather than in a hidden toolbar.

---

### 24. Live Speaking Task

**Route**: `/app/speaking/attempt/:attemptId`

**Use current design-system / domain components**
- AppShell or distraction-free task layout
- Audio recorder
- Timer
- Status badge
- Inline alert
- Button
- Modal / confirm dialog

**Inherited from elsewhere in the blueprint**
- Must preserve audio upload state, transcript availability state, and recording interruption state.
- Must use signed audio upload flows and explicit consent messaging where required.
- Must support robust reconnect/retry behavior if the platform supports it, and must never silently lose a recording.
- Must support queued/processing/completed/failed states for transcription/evaluation after submit.
- Must be reliable on mobile networks and safe on small screens.
- Must emit task started/submitted analytics with mode, attempt id, content id, device type, and timestamp.

**Not specified enough yet**
- The behavior of AI interlocutor mode is not specified in enough detail to implement without product definition.
- The mode definitions and constraints between AI interlocutor, self-practice, and exam simulation are not fully specified.
- The recording architecture is not specified: streaming vs local capture then upload.
- The exact stop/submit confirmation flow is not defined.
- Elapsed time is required, but whether remaining time is also shown is not specified.

**Provisional build assumption**
- Keep mode-specific UI differences minimal until product defines them; preserve one stable recording scaffold across all modes.
- Require explicit confirmation before destructive stop or final submit when work may be lost.

---

### 25. Speaking Result Summary

**Route**: `/app/speaking/result/:evaluationId`

**Use current design-system / domain components**
- AppShell
- Score range badge
- Confidence badge
- Card
- Status badge
- Button

**Inherited from elsewhere in the blueprint**
- Must present estimates as ranges/confidence guidance and never as official results.
- Must support async queued/processing/completed/failed states for speaking evaluation.
- Must support partial data if transcript or some AI findings are delayed.
- Must be mobile-readable and direct users toward transcript review or expert review.
- Must emit evaluation viewed analytics and result CTA analytics.

**Not specified enough yet**
- The exact score-range format and confidence taxonomy are not specified.
- The derivation of strengths, top improvement areas, and next recommended drill is not specified.
- CTA visibility rules based on review credits or entitlements are not specified.
- It is not specified how result summary behaves if transcript processing is still incomplete.

**Provisional build assumption**
- Keep the result summary concise and defer detailed evidence to transcript/audio review.
- If transcript is not ready, keep the transcript-review CTA disabled with a clear processing state rather than removing it.

---

### 26. Transcript + Audio Review

**Route**: `/app/speaking/review/:attemptId`

**Use current design-system / domain components**
- AppShell
- Transcript viewer
- Waveform viewer
- Audio player
- TranscriptFlagList
- Card
- Status badge
- Button

**Inherited from elsewhere in the blueprint**
- Must provide transcript and waveform/audio together and stay usable on small screens.
- Must keep waveform/transcript sync performant; this is a named performance focus area.
- Must support loading/partial/error/retry states because transcript and marker data may arrive asynchronously.
- Must support accessibility for transcript navigation, marker navigation, and playback controls.
- Must emit evaluation viewed/review viewed analytics and maybe marker interaction analytics if instrumented.

**Not specified enough yet**
- The waveform library choice is explicitly unresolved in the blueprint.
- Transcript-to-audio alignment detail is not specified.
- Marker placement granularity is not specified: token, phrase, sentence, or timestamp block.
- The small-screen layout pattern is not defined: tabs, stacked panes, drawer, or collapsible split view.
- It is not specified whether users can edit transcript text for correction or only view it.

**Provisional build assumption**
- Favor an implementation that keeps transcript and playback synchronized first; visual flourish is secondary.
- On small screens, stack transcript and audio panes rather than inventing a custom mini-waveform pattern.

---

### 27. Better Phrasing View

**Route**: `/app/speaking/review/:attemptId (subview) or dedicated internal state`

**Use current design-system / domain components**
- AppShell
- BetterPhraseCard
- Card
- Button
- Audio player

**Inherited from elsewhere in the blueprint**
- Must remain criterion-/improvement-focused and profession-aware, not generic speaking-coach advice.
- Must support mobile readability for per-segment cards.
- Must support loading/error states if phrasing suggestions are generated asynchronously.
- Must emit drill/review interaction analytics if tracked.

**Not specified enough yet**
- The stronger alternative generation source and approval model are not specified.
- The repeat drill prompt interaction is not specified: text-only prompt, playback, or re-record action.
- Segment navigation is not specified: one card at a time, carousel, list, or tabbed set.
- It is not specified whether the learner can save preferred alternatives.

**Provisional build assumption**
- Use one BetterPhraseCard per flagged segment with a clear original → issue → stronger alternative → repeat prompt structure.
- Avoid inventing extra coaching widgets unless already part of the current design system.

---

### 28. Speaking Expert Review Request

**Route**: `/app/reviews or in-context request flow from speaking result/review`

**Use current design-system / domain components**
- AppShell
- ReviewRequestDrawer or form card
- Card
- Inline alert
- Status badge
- Button
- existing design-system form controls

**Inherited from elsewhere in the blueprint**
- Must distinguish human review from AI output clearly.
- Must integrate with billing/credits safely, handling entitlement mismatch and payment failures explicitly.
- Must preserve the review handoff payload required by the blueprint: role card, transcript, audio, and AI findings.
- Must be mobile-usable and accessible for a short transactional request form.
- Must emit review requested analytics.

**Not specified enough yet**
- The exact focus-area taxonomy is not specified.
- Priority/turnaround options are not specified.
- The exact payment/credit logic is not specified.
- The payload packaging and visibility of what the reviewer will receive are not defined in UI terms.

**Provisional build assumption**
- Show a submission summary confirming that audio, transcript, role card, and AI findings are included in the request.
- Use the same request pattern as Writing to preserve consistency unless business rules differ.

---

### 29. Reading Home

**Route**: `/app/reading`

**Use current design-system / domain components**
- AppShell
- Card
- Task card
- Button
- Empty state

**Inherited from elsewhere in the blueprint**
- Must function as an action-oriented module home.
- Must support empty states and mobile access.
- Must support partial data if practice sets, explanations, and mocks come from different sources.
- Must emit module-entry and task-start analytics.

**Not specified enough yet**
- The exact information architecture between Part A/B/C entry points, drills, explanations, and mock sets is not specified.
- The default order of sections is not specified.
- It is not specified whether recommendations appear on this screen.

**Provisional build assumption**
- Keep section order task-first: part entry points, then drills, then explanations, then mocks.
- Reuse existing card/list patterns rather than creating a Reading-specific dashboard treatment.

---

### 30. Reading Player

**Route**: `/app/reading/task/:id`

**Use current design-system / domain components**
- AppShell or task layout
- Timer
- Question navigation
- Progress bar
- Inline alert
- Button

**Inherited from elsewhere in the blueprint**
- Must support easy navigation, persistent answers, and fast transitions under performance targets.
- Must support keyboard navigation and accessible item controls.
- Must support practice vs exam mode behavior without creating a wholly different UI unless necessary.
- Must support loading/error/retry states for content/answer fetches.
- Must emit task started/submitted analytics.

**Not specified enough yet**
- The exact practice vs exam mode differences are not specified.
- Time semantics are not specified in detail.
- The passage/question layout pattern is not defined.
- The submit/review flow is not specified.
- The answer persistence mechanism is not specified.

**Provisional build assumption**
- Use the same interaction scaffold as Reading diagnostic where possible, then layer in mode-specific restrictions.
- Persist answers on every change and when navigating between questions.

---

### 31. Reading Results

**Route**: `/app/reading/task/:id (result state) or dedicated evaluation route if implemented`

**Use current design-system / domain components**
- AppShell
- Card
- Accordion / review list
- Status badge
- Button

**Inherited from elsewhere in the blueprint**
- Must provide actionable review rather than score-only output.
- Must remain mobile-readable for item-by-item review.
- Must support loading/error states if explanations or clustering data are async.
- Must emit evaluation viewed analytics.

**Not specified enough yet**
- The exact score display format is not specified.
- The item-by-item review interaction is not specified.
- The error clustering taxonomy is not defined.
- The logic for next recommended drill is not specified.

**Provisional build assumption**
- Present score summary first, then item review, then clustered error patterns, then next drill CTA.
- Use expandable rows/cards for explanations if the item volume is high.

---

### 32. Listening Home

**Route**: `/app/listening`

**Use current design-system / domain components**
- AppShell
- Card
- Task card
- Button
- Empty state

**Inherited from elsewhere in the blueprint**
- Must function as an action-oriented module home and remain mobile-usable.
- Must support partial data and empty states.
- Must emit module-entry and task-start analytics.

**Not specified enough yet**
- The exact structure of part-based practice, transcript-backed review, distractor drills, and mock sets is not specified.
- The default section order is not specified.
- It is not specified whether recommendations appear here.

**Provisional build assumption**
- Group task entry, review, drill, and mock sections clearly rather than blending them into one list.
- Reuse existing card/list patterns rather than inventing a Listening-specific surface.

---

### 33. Listening Player

**Route**: `/app/listening/task/:id`

**Use current design-system / domain components**
- AppShell or task layout
- Audio player
- Question navigation
- Timer if mode requires
- Inline alert
- Button

**Inherited from elsewhere in the blueprint**
- Must provide stable audio, answer persistence, and safe mobile playback handling.
- Must support accessibility for playback and answer controls.
- Must support practice vs exam mode behavior and preserve work on interruptions where possible.
- Must emit task started/submitted analytics.

**Not specified enough yet**
- The exact practice vs exam mode differences are not specified.
- Replay restrictions and time behavior are not specified.
- The interaction model under mobile interruptions or backgrounding is not defined.
- Answer persistence triggers are not specified.

**Provisional build assumption**
- Keep audio controls minimal and stable; do not add unapproved advanced controls.
- Use the same persistence strategy as Listening diagnostic to reduce divergence.

---

### 34. Listening Results

**Route**: `/app/listening/task/:id (result state) or dedicated evaluation route if implemented`

**Use current design-system / domain components**
- AppShell
- Card
- Accordion / review list
- Status badge
- Button

**Inherited from elsewhere in the blueprint**
- Must focus on correctness, allowed transcript reveal, distractor explanation, and next drill action.
- Must remain mobile-readable.
- Must support loading/error states if transcript permissions or explanations are async.
- Must emit evaluation viewed analytics.

**Not specified enough yet**
- The exact correctness display pattern is not specified.
- The permission/condition rules for transcript reveal are not specified.
- The distractor explanation authoring structure is not defined.
- The next recommended drill logic is not specified.

**Provisional build assumption**
- Show correctness first, then conditionally reveal transcript, then distractor explanations, then drill recommendation.
- If transcript reveal is disallowed, explain why rather than silently omitting it.

---

### 35. Mock Center

**Route**: `/app/mocks`

**Use current design-system / domain components**
- AppShell
- Card
- MockReportSummary
- Status badge
- Button
- Empty state

**Inherited from elsewhere in the blueprint**
- Must support sub-test mocks, full mocks, purchased mock reviews, previous reports, and next recommendation in one command-center-style surface.
- Must support partial data because recommendations, prior reports, and purchased reviews may come from different systems.
- Must support empty states for no prior mocks or no purchased reviews.
- Must emit mock started/completed/viewed analytics and review-purchase interactions if relevant.

**Not specified enough yet**
- The recommendation logic for next mock is not specified.
- The list/sort/filter model for previous mock reports is not specified.
- The display and access model for purchased mock reviews is not specified.
- It is not specified whether mock center also surfaces readiness/risk messaging.

**Provisional build assumption**
- Keep this screen navigational and summary-focused; do not overload it with setup controls better handled in Mock Setup.
- Use clear separation between start-new, purchased-review, and prior-report sections.

---

### 36. Mock Setup

**Route**: `/app/mocks/:id or dedicated mock setup state`

**Use current design-system / domain components**
- AppShell
- Card
- Stepper if multi-step
- Inline alert
- Button
- existing design-system form controls

**Inherited from elsewhere in the blueprint**
- Must support calm validation and clear compatibility between choices.
- Must remain mobile-usable, with sticky primary CTA if setup is long.
- Must integrate billing/credits safely when expert review is included.
- Must emit mock started analytics when the learner actually begins the configured mock.

**Not specified enough yet**
- The exact mode definitions are not specified.
- Allowed option combinations are not specified.
- Strict timer applicability rules are not specified beyond depending on mode.
- It is not specified how profession selection for Writing/Speaking propagates inside a full mock.
- The pricing/credit impact of expert review inclusion is not specified.

**Provisional build assumption**
- Validate incompatible combinations inline before start rather than after submission.
- Keep configuration minimal and exam-focused; do not create advanced option panels without approval.

---

### 37. Mock Report

**Route**: `/app/mocks/:id (report state) or dedicated report route`

**Use current design-system / domain components**
- AppShell
- MockReportSummary
- ReadinessMeter
- CriterionBreakdownCard
- Card
- Button

**Inherited from elsewhere in the blueprint**
- Must support partial data if some sub-tests or review layers are pending.
- Must support comparison to prior mock where data exists and degrade gracefully when it does not.
- Must support mobile readability for summary and breakdown cards.
- Must emit evaluation/report viewed analytics and study-plan-update CTA analytics.

**Not specified enough yet**
- The exact composition of overall summary is not specified.
- Weakest criterion logic across multi-sub-test reports is not specified.
- The comparison baseline and time window for prior mock comparison are not specified.
- The effect of the study plan update CTA is not specified: immediate mutation, confirm step, or suggestion preview.

**Provisional build assumption**
- Present overall summary first, then sub-test breakdown, then weakest criterion, then comparison, then study-plan action.
- If no prior mock exists, replace comparison with a meaningful empty state instead of leaving a blank block.

---

### 38. Readiness Center

**Route**: `/app/readiness`

**Use current design-system / domain components**
- AppShell
- ReadinessMeter
- WeakestLinkCard
- CriterionBreakdownCard
- Card
- Confidence badge
- Button

**Inherited from elsewhere in the blueprint**
- Must be high-trust and explain probabilistic estimates rather than overstate precision.
- Must tolerate partial or stale data because readiness is a synthesized view from evaluations and plans.
- Must show evidence behind the estimate and key blockers, not only a score or status.
- Must remain mobile-readable and accessible.
- Must emit readiness viewed analytics.

**Not specified enough yet**
- The readiness-meter visualization is not specified.
- The target-date risk scale and labels are not specified.
- The exact evidence model is not defined.
- The blockers taxonomy and ordering are not specified.
- Recommended study remaining unit and granularity are not specified.

**Provisional build assumption**
- Represent readiness per sub-test with one consistent meter pattern and a separate evidence panel beneath.
- Keep confidence and risk language measured; avoid red/alarmist treatment unless the evidence is strong.

---

### 39. Progress Dashboard

**Route**: `/app/progress`

**Use current design-system / domain components**
- AppShell
- Card
- Tabs if needed
- existing design-system chart components if available
- Button
- Empty state

**Inherited from elsewhere in the blueprint**
- Must support the listed charts and remain performant with larger histories.
- Must support review-turnaround/usage only when relevant.
- Must support empty states when there is not enough data to draw trends.
- Must remain accessible, including non-color-dependent interpretation and keyboard/reader support where possible.
- Must emit progress viewed analytics.

**Not specified enough yet**
- The chart types, interactions, and time windows are not specified.
- The aggregation rules for trends are not defined.
- Submission volume grouping cadence is not specified.
- The exact conditions for showing review turnaround/usage are not specified.

**Provisional build assumption**
- Use the current design-system chart language if one exists; do not introduce a new chart aesthetic for this one screen.
- Prefer simple, readable trend views over dense analytics.

---

### 40. Submission History

**Route**: `/app/history`

**Use current design-system / domain components**
- AppShell
- Table or list
- Submission card
- Status badge
- Button
- Empty state

**Inherited from elsewhere in the blueprint**
- Must support history viewing, reopening feedback, comparing attempts, and requesting review.
- Must remain mobile-usable; use cards on small screens if tables become unreadable.
- Must support loading/empty/error/retry states.
- Must emit evaluation viewed and review requested analytics from history actions.

**Not specified enough yet**
- Default sort order is not specified.
- Filters/search are not specified.
- The review status taxonomy is not defined.
- Compare-attempts constraints and UX are not defined.
- Pagination or virtualization rules are not specified.

**Provisional build assumption**
- Sort most recent first unless product says otherwise.
- Use responsive list/cards on mobile and table/list on larger screens only if the existing design system already supports both.

---

### 41. Billing

**Route**: `/app/billing`

**Use current design-system / domain components**
- AppShell
- Card
- Table / list for invoices
- Status badge
- Inline alert
- Button

**Inherited from elsewhere in the blueprint**
- Must integrate safely with subscription, credits, invoices, and purchase actions.
- Must handle payment/entitlement errors explicitly and route to support only for true blockers.
- Must support mobile readability for plans, credits, and invoices.
- Must emit subscription started/changed analytics and purchase interaction analytics.

**Not specified enough yet**
- The billing provider integration details are not specified.
- Invoice file access/download behavior is not specified.
- The exact extras catalog is not defined.
- Renewal timezone/currency formatting rules are not specified.
- It is not specified how failed payment or grace-period states appear in the learner UI.

**Provisional build assumption**
- Keep billing actions explicit and transactional; avoid mixing them with settings content.
- Surface plan, renewal, credits, and invoices in separate cards so failure in one data source does not blank the page.

---

### 42. Settings

**Route**: `/app/settings`

**Use current design-system / domain components**
- AppShell
- Card
- Tabs / section navigation
- Inline alert
- Button
- existing design-system form controls

**Inherited from elsewhere in the blueprint**
- Must include profile, goals, notifications, privacy, accessibility, low-bandwidth mode, audio preferences, and exam date/study preferences.
- Must support calm validation and accessible form controls.
- Must support permission/privacy-safe handling where settings affect consent or personal data.
- Must support mobile readability and section navigation without inventing a new settings paradigm.
- Must emit analytics for settings changes if the product instrumentation plan includes them.

**Not specified enough yet**
- The exact field list under each settings section is not specified.
- The persistence model is not specified: instant-save, save-per-section, or explicit save button.
- Low-bandwidth mode behavior is not technically defined.
- Audio preference options are not defined.
- It is not specified which goal fields are editable here vs routed back to Goal Setup.

**Provisional build assumption**
- Use section-based settings with existing form controls and per-section save or autosave only if the codebase already uses that pattern consistently.
- Keep accessibility and low-bandwidth controls first-class, not buried under generic preferences.

---


## Final implementation instruction to the frontend developer

Build the Learner App as a **high-trust assessment application**, not as a course website and not as a generic LMS.

### Non-negotiables
- The learner must always know what to do next.
- Long-task reliability must be excellent.
- Feedback must be specific and criterion-based.
- Estimated results must never overpromise.
- The current design system is mandatory; do not improvise a new UI layer.
- Where this supplement flags an item as under-specified, escalate it or implement only the listed provisional assumption and mark it for product/design review.

### Practical delivery rule
Before development begins on each route, create a short build ticket that explicitly records:
- route
- reused current components
- required states
- API entities needed
- async states
- analytics events
- unresolved decisions from this file
- whether the provisional assumption is being used

That prevents hidden product decisions from leaking into frontend code.

---
