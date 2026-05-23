# Reading Learner Module Audit And Enhancement Plan

Date: 2026-05-23  
Scope: learner Reading journey at `/reading`, `/reading/practice`, Reading paper player, review/results, backend Reading APIs, pathway signals, QA, and release safety.

## Executive Verdict

The Reading module has the right core foundations for the business goal: structured 42-item papers, Part A and Part B/C timing, server-authoritative grading, learner review, Error Bank, practice modes, and pathway logic. It is not a blank rebuild problem.

The improvement need is connective tissue and hardening. Before this enhancement slice, several gaps could reduce learner trust or produce weak learning outcomes: expired attempts looked resumable, locked papers could lead to failed practice launches, focused Error Bank retests ignored the learner's selected part, B/C passage HTML was not consistently sanitized in paper view, policy overrides could remain active after expiry, submit idempotency keys could collide across owners, archived-paper policy was unreachable, and pathway readiness used the wrong mock evidence.

## Business Outcomes

- Learners should always know the next best Reading action: diagnostic, drill, mini-test, mock, review, or book exam.
- Every Reading outcome shown to learners must be tied to canonical OET scoring and the 42-item paper structure.
- Review should create fruitful remediation, not just display a score: missed items should feed Error Bank, focused retests, and pathway guidance.
- Entitlement or expiry states should be honest and actionable instead of leading to dead-end errors.
- Admin/content/release teams should have enough test evidence to keep Reading regressions out of production.

## Implemented Enhancement Slice

### Backend And API Integrity

- Submit idempotency now checks attempt ownership before replay lookup.
- Caller-supplied idempotency keys are namespaced by user and attempt.
- Expired per-user Reading policy overrides are ignored during policy resolution and attempt launch.
- Archived Reading papers can be attempted only when the global policy explicitly allows archived paper attempts.
- Reading paper launch queries now require `SubtestCode == "reading"`.
- Reading pathway readiness now uses scored `MockSectionAttempt` evidence for the Reading subtest instead of whole mock-attempt state.

### Learner Frontend

- `/reading` now separates resumable attempts from expired attempts and sends expired attempts to a fresh paper start.
- `/reading/practice` only uses accessible papers for drills and mini-tests.
- Locked practice papers show package actions instead of failed start buttons.
- Focused Error Bank retests now pass the selected part filter into the API.
- Practice hub empty states now use learner dashboard primitives.
- Study-plan route normalization now rewrites legacy/unsafe route shapes before rendering navigation.
- Reading paper simulation now sanitizes Part B/C body HTML and improves MCQ keyboard focus styling.

### Auth Continuation

- Switching between sign-in and sign-up preserves safe `next` values such as `/reading`.
- Sign-in footer links and forgot-password links preserve the return path.
- Registration sign-in links preserve the return path.
- Scheme-relative values such as `//example.com` are dropped by the shared helper.

## Remaining Recommended Waves

### Wave 1: Review Depth And Learner Remediation

- Add a learner-facing review summary that groups misses by Part, skill tag, and question type.
- Add “why this was wrong” display only when policy permits explanations.
- Add a one-click action per weak skill that launches the matching drill.
- Add “review complete” milestones that mark Error Bank items resolved when evidence supports it.

Acceptance: a submitted Reading attempt produces a visible score, item review, Error Bank entries, and at least one targeted next action.

### Wave 2: Practice Hub Personalization

- Rank drills by current Error Bank weakness and recent results.
- Show locked content as upgrade paths without hiding the learner's progress.
- Add recent drill/mini-test performance history to practice cards.
- Add empty/loading/error states for every hub section.

Acceptance: a learner with no attempts, weak attempts, cleared errors, locked papers, or passed mocks all sees a coherent next action.

### Wave 3: Admin And Content Operations

- Add admin indicators for papers that are published but not learner-accessible due to entitlement configuration.
- Add validation that every published Reading paper has 20/6/16 questions and safe rendered HTML.
- Add release evidence for archived-paper policy changes.

Acceptance: content admins can identify why a learner cannot access a Reading paper without inspecting raw database rows.

### Wave 4: QA And Observability

- Add Playwright smoke coverage for `/reading`, `/reading/practice`, sign-in continuation, expired attempt handling, and focused Error Bank retest.
- Add API tests for archived policy launch, expired override, namespaced idempotency, and pathway mock readiness.
- Add telemetry for Reading next-action clicks, locked paper CTA clicks, retest launches, and expired attempt fresh starts.

Acceptance: every production Reading release has unit/API evidence plus one learner-browser smoke trail.

### Wave 5: DevOps And Release Safety

- Keep heavy validation on `oet-dev` only.
- Include Reading-focused checks in release evidence before production deploys.
- Require rollback notes for policy, entitlement, and scoring-affecting changes.

Acceptance: deploy evidence shows typecheck, focused tests, lint status, and no production data or Docker volume risk.

## Verification Matrix

| Area | Verification |
| --- | --- |
| Backend attempt integrity | Reading backend tests for idempotency, ownership, policy overrides, archived policy |
| Pathway readiness | Reading pathway tests with passing and failing mock section evidence |
| Frontend learner states | `/reading` and `/reading/practice` Vitest coverage |
| Paper safety | Paper simulation sanitizer test for Part B/C HTML |
| Auth continuation | Auth helper and sign-in/register link checks |
| Release | Remote `tsc`, focused tests, lint/build as risk requires |

## Risks And Controls

- Risk: review pages can leak answer material if policy checks are bypassed. Control: keep answer/explanation exposure policy-governed and server-side.
- Risk: practice modes can be mistaken for exam-valid scores. Control: subset attempts must not produce scaled OET scores.
- Risk: pathway can overstate exam readiness. Control: require scored Reading mock-section pass evidence.
- Risk: locked paper UX can create support tickets. Control: show package CTA and required scope when available.
- Risk: remote validation drift between local and VPS. Control: run heavy checks only on `oet-dev` and report any unreachable state.