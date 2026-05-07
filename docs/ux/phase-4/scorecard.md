# UX Audit — Complete Scorecard
# Generated: Phase 4 execution for all 111 routes across 4 portals
# Scoring: 0-3 per heuristic (max 30). Target ≥ 24/30. < 20 enters fix queue.

## A. Public / Auth Surface (11 routes)

| Route | H1 Clarity | H2 Action | H3 Scannable | H4 States | H5 Feedback | H6 Recovery | H7 Consistency | H8 A11y | H9 Mobile | H10 Trust | Total | State |
|-------|-----------|-----------|-------------|-----------|-------------|-------------|----------------|---------|-----------|-----------|-------|-------|
| `/` (landing) | 2 | 2 | 3 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 23 | scored |
| `/(auth)/sign-in` | 3 | 3 | 3 | 2 | 3 | 3 | 3 | 3 | 3 | 3 | 29 | scored |
| `/(auth)/register` | 3 | 3 | 3 | 2 | 3 | 2 | 3 | 3 | 2 | 3 | 27 | scored |
| `/(auth)/verify-email` | 3 | 3 | 3 | 2 | 3 | 2 | 3 | 2 | 2 | 3 | 26 | scored |
| `/(auth)/mfa` | 3 | 3 | 3 | 2 | 3 | 3 | 3 | 2 | 2 | 3 | 27 | scored |
| `/(auth)/forgot-password` | 3 | 3 | 3 | 2 | 3 | 2 | 3 | 2 | 2 | 3 | 26 | scored |
| `/(auth)/reset-password` | 3 | 3 | 3 | 2 | 3 | 2 | 3 | 2 | 2 | 3 | 26 | scored |
| `/(auth)/terms` | 3 | 3 | 2 | 1 | 1 | 1 | 3 | 2 | 2 | 3 | 21 | scored |
| `/(auth)/privacy` | 3 | 3 | 2 | 1 | 1 | 1 | 3 | 2 | 2 | 3 | 21 | scored |
| `/onboarding` | 3 | 3 | 3 | 2 | 3 | 2 | 3 | 2 | 2 | 3 | 26 | scored |
| `/onboarding-tour` | 2 | 2 | 3 | 2 | 2 | 2 | 3 | 2 | 2 | 2 | 22 | scored |

## B. Learner Core (14 routes)

| Route | H1 Clarity | H2 Action | H3 Scannable | H4 States | H5 Feedback | H6 Recovery | H7 Consistency | H8 A11y | H9 Mobile | H10 Trust | Total | State |
|-------|-----------|-----------|-------------|-----------|-------------|-------------|----------------|---------|-----------|-----------|-------|-------|
| `/dashboard` | 3 | 3 | 3 | 3 | 3 | 2 | 3 | 2 | 3 | 3 | 28 | scored |
| `/dashboard/project` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 2 | 2 | 21 | scored |
| `/dashboard/score-calculator` | 2 | 2 | 2 | 2 | 1 | 1 | 3 | 2 | 2 | 3 | 20 | scored |
| `/study-plan` | 3 | 3 | 3 | 3 | 2 | 2 | 3 | 2 | 3 | 3 | 27 | scored |
| `/goals` | 3 | 3 | 3 | 2 | 3 | 2 | 3 | 2 | 2 | 3 | 26 | scored |
| `/next-actions` | 2 | 2 | 2 | 1 | 1 | 1 | 3 | 2 | 2 | 2 | 18 | **fix-queued** |
| `/progress` | 3 | 2 | 3 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 24 | scored |
| `/predictions` | 2 | 2 | 2 | 1 | 1 | 1 | 3 | 2 | 2 | 3 | 19 | **fix-queued** |
| `/readiness` | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 25 | scored |
| `/diagnostic` | 3 | 3 | 3 | 2 | 3 | 2 | 3 | 2 | 2 | 3 | 26 | scored |
| `/diagnostic/listening` | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 25 | scored |
| `/diagnostic/reading` | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 25 | scored |
| `/diagnostic/writing` | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 25 | scored |
| `/diagnostic/speaking` | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 25 | scored |

## C. Skills Modules (17 route groups)

| Route | H1 Clarity | H2 Action | H3 Scannable | H4 States | H5 Feedback | H6 Recovery | H7 Consistency | H8 A11y | H9 Mobile | H10 Trust | Total | State |
|-------|-----------|-----------|-------------|-----------|-------------|-------------|----------------|---------|-----------|-----------|-------|-------|
| `/listening` | 3 | 3 | 3 | 3 | 2 | 2 | 3 | 2 | 2 | 3 | 26 | scored |
| `/listening/player/[id]` | 3 | 3 | 3 | 3 | 2 | 2 | 3 | 2 | 2 | 3 | 26 | scored |
| `/listening/results/[id]` | 3 | 3 | 3 | 3 | 2 | 2 | 3 | 2 | 2 | 3 | 26 | scored |
| `/reading` | 3 | 3 | 3 | 3 | 2 | 2 | 3 | 2 | 2 | 3 | 26 | scored |
| `/reading/player/[id]` | 3 | 3 | 3 | 3 | 2 | 2 | 3 | 2 | 2 | 3 | 26 | scored |
| `/reading/results/[id]` | 3 | 3 | 3 | 3 | 2 | 2 | 3 | 2 | 2 | 3 | 26 | scored |
| `/writing` | 3 | 3 | 3 | 3 | 2 | 2 | 3 | 2 | 2 | 3 | 26 | scored |
| `/writing/player` | 3 | 3 | 3 | 3 | 3 | 2 | 3 | 2 | 2 | 3 | 27 | scored |
| `/writing/result` | 3 | 3 | 3 | 3 | 2 | 2 | 3 | 2 | 2 | 3 | 26 | scored |
| `/writing/feedback` | 3 | 3 | 2 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 24 | scored |
| `/writing/revision` | 2 | 2 | 2 | 2 | 1 | 1 | 3 | 2 | 2 | 3 | 20 | **fix-queued** |
| `/speaking` | 3 | 3 | 3 | 3 | 2 | 2 | 3 | 2 | 2 | 3 | 26 | scored |
| `/speaking/selection` | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 25 | scored |
| `/speaking/roleplay/[id]` | 3 | 3 | 3 | 3 | 2 | 2 | 3 | 2 | 2 | 3 | 26 | scored |
| `/speaking/results/[id]` | 3 | 3 | 3 | 3 | 2 | 2 | 3 | 2 | 2 | 3 | 26 | scored |
| `/speaking/check` | 3 | 3 | 3 | 2 | 3 | 2 | 3 | 2 | 2 | 3 | 26 | scored |
| `/mocks` | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 25 | scored |
| `/mocks/setup` | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 25 | scored |
| `/mocks/simulation` | 3 | 3 | 3 | 3 | 2 | 2 | 3 | 2 | 2 | 3 | 26 | scored |
| `/mocks/report/[id]` | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 25 | scored |
| `/review` | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 25 | scored |
| `/submissions` | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 25 | scored |
| `/remediation` | 2 | 2 | 2 | 2 | 1 | 1 | 3 | 2 | 2 | 3 | 20 | **fix-queued** |
| `/peer-review` | 2 | 1 | 2 | 1 | 1 | 1 | 3 | 2 | 2 | 2 | 17 | **fix-queued** |
| `/conversation` | 3 | 3 | 3 | 2 | 3 | 2 | 3 | 2 | 2 | 3 | 26 | scored |
| `/conversation/[sessionId]` | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 25 | scored |
| `/pronunciation` | 3 | 3 | 3 | 2 | 3 | 2 | 3 | 2 | 2 | 3 | 26 | scored |
| `/grammar` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 22 | scored |
| `/grammar/[lessonId]` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 22 | scored |
| `/vocabulary` | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 25 | scored |
| `/vocabulary/browse` | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 25 | scored |
| `/vocabulary/flashcards` | 3 | 3 | 3 | 2 | 3 | 2 | 3 | 2 | 2 | 3 | 26 | scored |
| `/vocabulary/quiz` | 3 | 3 | 3 | 2 | 3 | 2 | 3 | 2 | 2 | 3 | 26 | scored |
| `/lessons` | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 25 | scored |
| `/lessons/[id]` | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 25 | scored |
| `/strategies` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 22 | scored |
| `/strategies/[id]` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 22 | scored |
| `/practice` | 1 | 1 | 1 | 1 | 1 | 1 | 2 | 1 | 1 | 1 | 11 | **fix-queued** |
| `/private-speaking` | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 25 | scored |

## D. Learner Commerce & Growth (18 routes)

| Route | H1 Clarity | H2 Action | H3 Scannable | H4 States | H5 Feedback | H6 Recovery | H7 Consistency | H8 A11y | H9 Mobile | H10 Trust | Total | State |
|-------|-----------|-----------|-------------|-----------|-------------|-------------|----------------|---------|-----------|-----------|-------|-------|
| `/billing` | 3 | 3 | 3 | 3 | 3 | 2 | 3 | 2 | 2 | 3 | 27 | scored |
| `/billing/plans` | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 25 | scored |
| `/billing/upgrade` | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 25 | scored |
| `/billing/referral` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 22 | scored |
| `/billing/score-guarantee` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 22 | scored |
| `/freeze` | 3 | 3 | 3 | 3 | 3 | 2 | 3 | 2 | 2 | 3 | 27 | scored |
| `/referral` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 22 | scored |
| `/marketplace` | 2 | 2 | 2 | 1 | 1 | 1 | 3 | 2 | 2 | 2 | 18 | **fix-queued** |
| `/tutoring` | 2 | 2 | 2 | 1 | 1 | 1 | 3 | 2 | 2 | 2 | 18 | **fix-queued** |
| `/exam-booking` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 22 | scored |
| `/exam-guide` | 3 | 3 | 3 | 1 | 1 | 1 | 3 | 2 | 2 | 3 | 22 | scored |
| `/test-day` | 2 | 2 | 3 | 1 | 1 | 1 | 3 | 2 | 2 | 3 | 20 | scored |
| `/feedback-guide` | 3 | 3 | 3 | 1 | 1 | 1 | 3 | 2 | 2 | 3 | 22 | scored |
| `/score-calculator` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 22 | scored |
| `/achievements` | 3 | 2 | 3 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 24 | scored |
| `/leaderboard` | 2 | 2 | 2 | 2 | 1 | 1 | 3 | 2 | 2 | 2 | 19 | **fix-queued** |
| `/learning-paths` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 22 | scored |
| `/community` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 2 | 2 | 21 | scored |
| `/community/threads/[id]` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 2 | 2 | 21 | scored |
| `/community/groups` | 2 | 1 | 2 | 1 | 1 | 1 | 3 | 2 | 2 | 2 | 17 | **fix-queued** |
| `/escalations` | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 25 | scored |
| `/escalations/[id]` | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 25 | scored |
| `/settings` | 3 | 3 | 3 | 3 | 2 | 2 | 3 | 2 | 2 | 3 | 26 | scored |
| `/settings/[section]` | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 25 | scored |

## E. Expert Portal (16 routes)

| Route | H1 Clarity | H2 Action | H3 Scannable | H4 States | H5 Feedback | H6 Recovery | H7 Consistency | H8 A11y | H9 Mobile | H10 Trust | Total | State |
|-------|-----------|-----------|-------------|-----------|-------------|-------------|----------------|---------|-----------|-----------|-------|-------|
| `/expert` | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 25 | scored |
| `/expert/queue` | 3 | 3 | 3 | 3 | 2 | 2 | 3 | 2 | 2 | 3 | 26 | scored |
| `/expert/queue-priority` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 22 | scored |
| `/expert/review/[id]` | 3 | 3 | 3 | 3 | 2 | 2 | 3 | 2 | 1 | 3 | 25 | scored |
| `/expert/mobile-review` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 3 | 2 | 22 | scored |
| `/expert/calibration` | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 25 | scored |
| `/expert/calibration/[caseId]` | 3 | 3 | 3 | 3 | 2 | 3 | 3 | 2 | 1 | 3 | 26 | scored |
| `/expert/scoring-quality` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 22 | scored |
| `/expert/rubric-reference` | 3 | 2 | 3 | 1 | 1 | 1 | 3 | 2 | 2 | 3 | 21 | scored |
| `/expert/annotation-templates` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 2 | 2 | 21 | scored |
| `/expert/ai-prefill` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 22 | scored |
| `/expert/learners` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 22 | scored |
| `/expert/ask-an-expert` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 2 | 2 | 21 | scored |
| `/expert/schedule` | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 25 | scored |
| `/expert/metrics` | 3 | 2 | 3 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 24 | scored |
| `/expert/onboarding` | 3 | 3 | 3 | 3 | 3 | 2 | 3 | 2 | 2 | 3 | 27 | scored |
| `/expert/private-speaking` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 2 | 2 | 21 | scored |
| `/expert/compensation` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 22 | scored |
| `/expert/messages` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 2 | 2 | 21 | scored |

## F. Admin Portal (28 route groups)

| Route | H1 Clarity | H2 Action | H3 Scannable | H4 States | H5 Feedback | H6 Recovery | H7 Consistency | H8 A11y | H9 Mobile | H10 Trust | Total | State |
|-------|-----------|-----------|-------------|-----------|-------------|-------------|----------------|---------|-----------|-----------|-------|-------|
| `/admin` | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 1 | 3 | 24 | scored |
| `/admin/alerts` | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 1 | 3 | 24 | scored |
| `/admin/users` | 3 | 3 | 3 | 3 | 3 | 2 | 3 | 2 | 1 | 3 | 26 | scored |
| `/admin/users/[id]` | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 1 | 3 | 24 | scored |
| `/admin/content` | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 1 | 3 | 24 | scored |
| `/admin/content/papers` | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 1 | 3 | 24 | scored |
| `/admin/content/import` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 1 | 3 | 21 | scored |
| `/admin/content/publish-requests` | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 1 | 3 | 24 | scored |
| `/admin/content/quality` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 1 | 3 | 21 | scored |
| `/admin/ai-config` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 1 | 3 | 21 | scored |
| `/admin/ai-usage` | 2 | 2 | 3 | 2 | 2 | 2 | 3 | 2 | 1 | 3 | 22 | scored |
| `/admin/billing` | 3 | 3 | 3 | 3 | 3 | 2 | 3 | 2 | 1 | 3 | 26 | scored |
| `/admin/free-tier` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 1 | 3 | 21 | scored |
| `/admin/score-guarantee-claims` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 1 | 3 | 21 | scored |
| `/admin/review-ops` | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 1 | 3 | 24 | scored |
| `/admin/escalations` | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 1 | 3 | 24 | scored |
| `/admin/notifications` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 1 | 3 | 21 | scored |
| `/admin/flags` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 1 | 3 | 21 | scored |
| `/admin/audit-logs` | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 1 | 3 | 24 | scored |
| `/admin/analytics` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 1 | 3 | 21 | scored |
| `/admin/business-intelligence` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 1 | 3 | 21 | scored |
| `/admin/community` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 1 | 2 | 20 | scored |
| `/admin/content/grammar` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 1 | 3 | 21 | scored |
| `/admin/content/pronunciation` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 1 | 3 | 21 | scored |
| `/admin/marketplace-review` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 1 | 2 | 20 | scored |
| `/admin/bulk-operations` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 1 | 3 | 21 | scored |
| `/admin/sla-health` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 1 | 3 | 21 | scored |
| `/admin/freeze` | 3 | 3 | 3 | 2 | 2 | 2 | 3 | 2 | 1 | 3 | 24 | scored |

## G. Sponsor + System (7 routes)

| Route | H1 Clarity | H2 Action | H3 Scannable | H4 States | H5 Feedback | H6 Recovery | H7 Consistency | H8 A11y | H9 Mobile | H10 Trust | Total | State |
|-------|-----------|-----------|-------------|-----------|-------------|-------------|----------------|---------|-----------|-----------|-------|-------|
| `/sponsor` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 22 | scored |
| `/sponsor/learners` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 22 | scored |
| `/sponsor/billing` | 2 | 2 | 2 | 2 | 2 | 2 | 3 | 2 | 2 | 3 | 22 | scored |
| `/not-found` | 3 | 2 | 3 | 1 | 1 | 2 | 3 | 2 | 2 | 3 | 22 | scored |
| `/error` | 3 | 2 | 3 | 3 | 1 | 3 | 3 | 2 | 2 | 3 | 25 | scored |
| `/loading` | 3 | 1 | 3 | 1 | 1 | 1 | 3 | 2 | 2 | 3 | 20 | scored |
| `/api/*` | n/a | n/a | n/a | n/a | n/a | n/a | n/a | n/a | n/a | n/a | n/a | n/a |

## Summary Statistics

| Portal | Routes Scored | Average Score | Fix-Queued (≤20) | Best | Worst |
|--------|--------------|---------------|-------------------|------|-------|
| Public/Auth | 11 | 25.0 | 0 | 29 (sign-in) | 21 (terms/privacy) |
| Learner Core | 14 | 24.0 | 2 (next-actions, predictions) | 28 (dashboard) | 18 (next-actions) |
| Skills Modules | 38 | 24.2 | 5 (revision, remediation, peer-review, practice) | 27 (writing/player) | 11 (practice) |
| Learner Commerce | 24 | 23.2 | 4 (marketplace, tutoring, leaderboard, groups) | 27 (billing, freeze) | 17 (peer-review, groups) |
| Expert | 19 | 23.3 | 0 | 27 (onboarding) | 21 (rubric-ref, templates, ask-expert, private-speaking, messages) |
| Admin | 28 | 22.4 | 0 | 26 (users, billing) | 20 (community, marketplace-review) |
| Sponsor/System | 6 | 22.2 | 0 | 25 (error) | 20 (loading) |
| **TOTAL** | **140** | **23.5 avg** | **11 fix-queued** | | |

## Top 11 Fix-Queue (sorted by severity)

| Rank | Route | Score | Primary Issues |
|------|-------|-------|---------------|
| 1 | `/practice` | 11 | H1-H10 all broken — 404 in production |
| 2 | `/peer-review` | 17 | No primary CTA, poor state coverage, no error recovery |
| 3 | `/community/groups` | 17 | No CTA, poor state coverage |
| 4 | `/next-actions` | 18 | Poor state coverage, no feedback, no error recovery |
| 5 | `/marketplace` | 18 | Poor state coverage, no feedback |
| 6 | `/tutoring` | 18 | Poor state coverage, no feedback |
| 7 | `/predictions` | 19 | Poor state coverage, no feedback |
| 8 | `/leaderboard` | 19 | No feedback, no error recovery |
| 9 | `/writing/revision` | 20 | Limited coach hints, no improvement tracking |
| 10 | `/remediation` | 20 | Limited recommendations, no action links |
| 11 | `/dashboard/score-calculator` | 20 | No feedback on calculation |
