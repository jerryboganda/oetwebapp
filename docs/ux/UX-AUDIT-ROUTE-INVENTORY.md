# UX Audit — Route Inventory (All Portals)

> Companion to [UX-AUDIT-MASTER-PLAN.md](./UX-AUDIT-MASTER-PLAN.md).
> 217 routes across 4 portals. Each row tracks: owner, persona, JTBD link, severity tier, audit state.
> States: `todo` → `in-review` → `scored` → `fix-queued` → `done`.

---

## Legend

- **Tier:** `T0` launch-critical · `T1` core journey · `T2` secondary · `T3` internal/rare
- **Persona:** P1 Nadia · P2 Dinesh · P3 Sana · P4 Emma · P5 Arman · P6 Sam
- **Scorecard link populates in Phase 4.**

---

## A. Public / Auth Surface (T0)

| Route | Persona | Tier | State | Notes |
|-------|---------|------|-------|-------|
| `/` (landing) | P1-P3 | T0 | todo | First-impression, conversion critical |
| `/(auth)/sign-in` | P1-P6 | T0 | todo | External auth, MFA fork |
| `/(auth)/register` | P1-P3 | T0 | todo | Includes profession picker |
| `/(auth)/verify-email` | P1-P3 | T0 | todo | |
| `/(auth)/mfa` | P1-P6 | T0 | todo | TOTP + backup codes |
| `/(auth)/forgot-password` | all | T0 | todo | |
| `/(auth)/reset-password` | all | T0 | todo | |
| `/(auth)/terms` | all | T2 | todo | Legal |
| `/(auth)/auth/*` (OAuth/deep-link) | all | T0 | todo | Desktop `oet-prep://` scheme |
| `/onboarding` | P1-P3 | T0 | todo | Goal, test date, profession |
| `/onboarding-tour` | P1-P3 | T1 | todo | |

## B. Learner Core (T0 / T1)

| Route | Persona | Tier | State | Notes |
|-------|---------|------|-------|-------|
| `/dashboard` | P1-P3 | T0 | todo | Home after login |
| `/dashboard/project` | P1-P3 | T1 | todo | |
| `/dashboard/score-calculator` | P1-P3 | T1 | todo | |
| `/study-plan` | P1-P3 | T0 | todo | Daily plan engine |
| `/goals` | P1-P3 | T1 | todo | |
| `/next-actions` | P1-P3 | T1 | todo | |
| `/progress` | P1-P3 | T1 | todo | |
| `/predictions` | P1-P3 | T1 | todo | |
| `/readiness` | P1-P3 | T1 | todo | |
| `/diagnostic` | P3 | T0 | todo | Funnel pivot |
| `/diagnostic/listening` | P3 | T0 | todo | |
| `/diagnostic/reading` | P3 | T0 | todo | |
| `/diagnostic/writing` | P3 | T0 | todo | |
| `/diagnostic/speaking` | P3 | T0 | todo | |

## C. Skills Modules (T0)

| Route | Persona | Tier | State | Notes |
|-------|---------|------|-------|-------|
| `/listening` + children | P1-P3 | T0 | todo | |
| `/reading` + children | P1-P3 | T0 | todo | 20+6+16 gate |
| `/writing` + children | P1-P3 | T0 | todo | Editor heavy |
| `/speaking` + children | P1-P3 | T0 | todo | Audio flow |
| `/private-speaking` + children | P1, P2 | T1 | todo | |
| `/conversation` + children | P1-P3 | T0 | todo | AI realtime |
| `/pronunciation` + children | P1-P3 | T0 | todo | ASR flow |
| `/grammar` + children | P1-P3 | T1 | todo | |
| `/vocabulary` + children | P1-P3 | T1 | todo | |
| `/lessons` + children | P1-P3 | T1 | todo | Video |
| `/strategies` + children | P1-P3 | T2 | todo | |
| `/practice` | P1-P3 | T0 | todo | **404 in prod per mobile audit** |
| `/mocks` + children | P1-P3 | T0 | todo | Full exam |
| `/review` + children | P1-P3 | T0 | todo | Post-attempt |
| `/submissions` + children | P1-P3 | T1 | todo | |
| `/remediation` | P1-P3 | T1 | todo | |
| `/peer-review` + children | P1-P3 | T2 | todo | |

## D. Learner Commerce & Growth (T0 / T1)

| Route | Persona | Tier | State | Notes |
|-------|---------|------|-------|-------|
| `/billing` + children | P1-P3 | T0 | todo | Subscription, invoice, portal |
| `/billing/score-guarantee` | P1, P2 | T1 | todo | |
| `/referral` | all learners | T1 | todo | |
| `/marketplace` + children | P1-P3 | T2 | todo | |
| `/tutoring` + children | P1, P2 | T2 | todo | |
| `/exam-booking` | P1-P3 | T1 | todo | CBLA link |
| `/exam-guide` | P1-P3 | T2 | todo | |
| `/test-day` | P1-P3 | T1 | todo | |
| `/feedback-guide` | P1-P3 | T2 | todo | |
| `/score-calculator` | P1-P3 | T1 | todo | |
| `/achievements` | P1-P3 | T2 | todo | Gamification |
| `/leaderboard` | P1-P3 | T2 | todo | |
| `/learning-paths` + children | P1-P3 | T1 | todo | |
| `/community` + children | P1-P3 | T2 | todo | |
| `/freeze` | P1-P3 | T2 | todo | Subscription pause |
| `/escalations` | P1-P3 | T2 | todo | |
| `/settings/*` | all | T1 | todo | Profile, notifs, security |
| `/media` | all | T3 | todo | |

## E. Expert Portal (T1)

| Route | Persona | Tier | State | Notes |
|-------|---------|------|-------|-------|
| `/expert` | P4 | T0 | todo | Queue home |
| `/expert/queue` + children | P4 | T0 | todo | |
| `/expert/queue-priority` | P4 | T1 | todo | |
| `/expert/review/[id]` | P4 | T0 | todo | **Core grading surface** |
| `/expert/mobile-review` | P4 | T1 | todo | |
| `/expert/calibration` + children | P4 | T0 | todo | Quality gate |
| `/expert/scoring-quality` | P4 | T1 | todo | |
| `/expert/rubric-reference` | P4 | T1 | todo | |
| `/expert/annotation-templates` | P4 | T2 | todo | |
| `/expert/ai-prefill` | P4 | T1 | todo | |
| `/expert/learners` + children | P4 | T2 | todo | |
| `/expert/ask-an-expert` | P4 | T2 | todo | |
| `/expert/schedule` | P4 | T1 | todo | |
| `/expert/metrics` | P4 | T1 | todo | |
| `/expert/onboarding` | P4 | T1 | todo | |
| `/expert/private-speaking` + children | P4 | T2 | todo | |

## F. Admin Portal (T1 / T2)

*All ~45 admin routes under `app/admin/*`. Key ones:*

| Route | Persona | Tier | State | Notes |
|-------|---------|------|-------|-------|
| `/admin` | P5 | T0 | todo | Ops home |
| `/admin/users` + children | P5 | T0 | todo | Support surface |
| `/admin/content/papers` + children | P5 | T0 | todo | Publish gate |
| `/admin/content/import` | P5 | T0 | todo | ZIP ingest |
| `/admin/content/publish-requests` | P5 | T0 | todo | |
| `/admin/content/quality` | P5 | T1 | todo | |
| `/admin/ai-config` + children | P5 | T1 | todo | |
| `/admin/ai-usage` | P5 | T1 | todo | Quota dashboards |
| `/admin/billing` + children | P5 | T0 | todo | Refunds, comps |
| `/admin/free-tier` | P5 | T1 | todo | |
| `/admin/score-guarantee-claims` | P5 | T1 | todo | |
| `/admin/review-ops` | P5 | T0 | todo | Expert ops |
| `/admin/experts` + children | P5 | T0 | todo | |
| `/admin/escalations` | P5 | T0 | todo | |
| `/admin/notifications` | P5 | T1 | todo | Broadcast |
| `/admin/flags` | P5 | T1 | todo | Feature flags |
| `/admin/roles` + `/admin/permissions` | P5 | T1 | todo | RBAC |
| `/admin/audit-logs` | P5 | T1 | todo | |
| `/admin/analytics` + `/admin/business-intelligence` | P5 | T1 | todo | |
| `/admin/community` + children | P5 | T2 | todo | Moderation |
| `/admin/content/grammar` / `content/pronunciation` / `content/strategies` / `private-speaking` | P5 | T1 | todo | Authoring |
| `/admin/content/media` + `/admin/content/dedup` | P5 | T2 | todo | |
| `/admin/taxonomy` | P5 | T2 | todo | |
| `/admin/webhooks` | P5 | T2 | todo | |
| `/admin/sla-health` | P5 | T1 | todo | |
| `/admin/marketplace-review` | P5 | T2 | todo | |
| `/admin/bulk-operations` | P5 | T1 | todo | |
| `/admin/credit-lifecycle` | P5 | T1 | todo | |
| `/admin/content/analytics` / `content/generation` / `content/hierarchy` | P5 | T1 | todo | |
| `/admin/freeze` | P5 | T2 | todo | |
| `/admin/playbook` | P5 | T2 | todo | |
| `/admin/enterprise` | P5 | T1 | todo | Sponsor ops |
| `/admin/criteria` | P5 | T2 | todo | |

## G. Sponsor Portal (T0 for sponsor persona)

| Route | Persona | Tier | State | Notes |
|-------|---------|------|-------|-------|
| `/sponsor` | P6 | T0 | todo | Home |
| `/sponsor/learners` + children | P6 | T0 | todo | Cohort |
| `/sponsor/billing` + children | P6 | T0 | todo | Seats, invoices |

## H. System / Utility

| Route | Persona | Tier | State | Notes |
|-------|---------|------|-------|-------|
| `/not-found` | all | T1 | todo | Global 404 |
| `/error` | all | T1 | todo | Error boundary |
| `/loading` | all | T2 | todo | Global skeleton |
| `/api/*` route handlers | n/a | n/a | n/a | Not UX-audited directly |

---

## Ownership & Cadence

- **Phase 4 populates** the `State` and scorecard columns.
- Triage cadence: weekly review of `todo` → `in-review`, monthly severity reassessment.
- File size expectation: one CSV derived from this table (`docs/ux/phase-4/scorecard.csv`) is the single source of truth once Phase 4 opens.

