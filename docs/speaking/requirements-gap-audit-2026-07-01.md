# Speaking Module — Requirements Gap Audit (2026-07-01)

> Compares an external "OET Speaking Module — Structured Requirements" document (17 sections + 13 mission-critical rules) against the actual implementation. Produced via 3 broad exploration passes + 8 targeted read-only verification passes, all citing file:line evidence and existing backend tests. **Update (2026-07-01, later same day):** the user asked for all identified gaps to be closed; see "Implementation status" at the end of this document for what was actually built, and a correction to the route-tree finding below.

## Summary

The requirements doc reads like a fresh scope for a new build, but the Speaking module is already a mature, in-production system (see `docs/speaking/PROGRESS.md`) with almost everything described here already implemented and tested. Of 17 sections, all but 2 are fully confirmed. Two genuine gaps and one architectural risk were found — see below.

## ⚠️ Architectural finding: two parallel Speaking UI route trees — CORRECTED, not dead code

Found while verifying the prep-screen copy, independent of the requirements doc itself:

- **Legacy tree**: `app/speaking/sessions/[id]/{warmup,prep,results}/page.tsx`, `components/domain/speaking/PrepCountdown.tsx`. Header comments reference "plan C.2/C.3". Prep screen has **no** paper/pen instruction — just a digital notes textarea.
- **Current tree**: `app/speaking/exam/page.tsx`, `app/speaking/exam/[id]/page.tsx`, `app/speaking/roleplay/[id]/page.tsx`. Header comment: "Speaking module rebuild (2026-06-11 spec)". Has the paper/pen instruction, ties into `rulebooks/speaking/{profession}/rulebook.v1.json`.

**Correction:** follow-up investigation found `app/admin/onboarding/interlocutor/page.tsx` actively routes interlocutor trainees to `/speaking/sessions/{id}/prep` for practice/calibration — this is a **staff-only training flow**, not stale candidate-exam code, and no reference to it exists anywhere else. The missing paper/pen instruction is not a compliance gap here since trainees aren't sitting a real exam. Fix applied: clarifying doc comments added to all four legacy-tree files (`prep`, `warmup`, `results` pages + `PrepCountdown.tsx`) stating explicitly which flow each belongs to, so no future edit accidentally applies a candidate-facing change to the wrong tree (or vice versa). No deletion was made.

Static analysis can't tell whether the legacy tree is dead code, an intentional alternate flow, or mid-migration debris. Confirm with whoever did the 2026-06-11 rebuild whether `app/speaking/sessions/[id]/*` and `PrepCountdown.tsx` can be deleted — until then, any Speaking UI fix must be applied to *both* trees or it may silently miss whichever route real users hit.

## Gap audit table

| Doc § | Requirement | Verdict | Evidence |
|---|---|---|---|
| §1 | Engine supports AI tutor, human tutor, profession-specific cards, Card A/B exam, practice mode | CONFIRMED | `SpeakingSessionMode = 'ai_self_practice' \| 'ai_exam' \| 'live_tutor'` (`lib/api/speaking-sessions.ts`); single `RolePlayCard` schema filtered by `ProfessionId` |
| §2.1 | Candidate plays profession-specific role | CONFIRMED | `RolePlayCard.CandidateRole` free text (e.g. "Nurse", "Doctor") |
| §2.1 | Full profession list (Nurse, Pharmacist, Dentist, Physio, Radiographer, OT, Other) | UNCONFIRMED | Evidence only showed `ProfessionId` populated with "nursing"/"medicine"/"pharmacy"/"dentistry"; `WritingProfession` (a different module) supports 12 professions. Whether Speaking validates against the same broad enum wasn't directly confirmed. |
| §2.2 | Choice of Human Tutor Mode / AI Tutor Mode | CONFIRMED | `mode` field on session creation |
| §3 | Candidate card + separate hidden Patient card | CONFIRMED | `RolePlayCard` (candidate) + `InterlocutorScript` (patient), 1:1 paired |
| §3 | Patient card never visible to student | CONFIRMED, tested | Separate `RolePlayCardLearnerDetail` vs `RolePlayCardDetail` DTOs; `PrivateSpeakingEndpointProjectionTests` |
| §4 | 6 internal hidden card types, admin/tutor/AI only | CONFIRMED, exact match | `SpeakingCardTypeDetail` + `cardTypeId`, admin-only CRUD at `app/admin/content/speaking/card-types/page.tsx`, tested never to reach learner |
| §5 | Student view shows only candidate card, scenario, role, tasks, timer, prep instructions | CONFIRMED (structure) | `RolePlayCard.Task1..Task5`, `Setting`, `ScenarioTitle`, `CandidateRole`, `PrepTimeSeconds` all learner-visible fields |
| §5 | Matches attached screenshot reference | CANNOT VERIFY | No screenshot was provided |
| §6 | Intro = unscored warm-up, no credits, no scoring | CONFIRMED | `SpeakingExamState.Intro`/`WarmUp` distinct from `Prep_A`; credit debited only at card reveal (`SpeakingSessionService.cs:176-193`) |
| §6/§7/§8 | Prep = 3 min, Discussion = 5 min, total 8 min per card | CONFIRMED, exact | `RolePlayCardEntities.cs:100,104` (`PrepTimeSeconds=180`, `RolePlayTimeSeconds=300`), consistent across `SpeakingExamService.cs`, `SpeakingSessionService.cs`, `ConversationHub.SpeakingRoleplay.cs`, seed data |
| §7 | Card A auto-closes after 8 min, candidate cannot extend | CONFIRMED, tested, layered enforcement | `SpeakingExamAutoAdvanceWorker` (20s sweep) + lazy re-check on every exam API call + real-time SignalR `TimeUp` timer force-`Finished`s the session; turns after cutoff rejected `INVALID_STATE`. Test: `Advance_IsIdempotent_NeverDoubleCharges` |
| §8 | Card B auto-starts after Card A, no learner action | CONFIRMED, tested | Same `AdvanceAsync` transition closes A and creates/reveals B atomically, no bridge state. Test: `AutoAdvance_ClosesCardA_RevealsCardB_NoBridge_AndDebitsSecondCredit` |
| §9.1 | AI exam = 2 credits total (1/card), min-2-credit gate blocks start with upgrade message | CONFIRMED, tested | Gate in `SpeakingExamService.CreateExamAsync`; backed by `IAiPackageCreditService`/`AiPackageCreditAccount.SpeakingOnlyCredits`. Test: `AutoAdvance_FromIntroToCompleted_DebitsExactlyTwoCredits` |
| §9.2 | Human tutor credits separate pool from AI credits | CONFIRMED (functionally) | `Subscription.SpeakingSessionsRemaining` (human tutor) is fully separate from `Subscription.AiCreditsRemaining`/`AiPackageCreditAccount.SpeakingOnlyCredits` (AI). Live-tutor booking also allows pay-per-session beyond the included quota. |
| §10 | Practice mode: single card, AI or human tutor, profession-specific, card feedback, repeated practice | CONFIRMED | `ai_self_practice` = one card, advisory AI feedback (`IsAdvisory=true`), no A/B pairing, repeatable. `sessionFormat` normalized server-side to `"exam"`/`"practice"` even for live-tutor bookings |
| §10/§14 | Practice access limited/unlimited by plan tier | **GAP** | No `BillingPlan` field gates `ai_self_practice`. Metered instead by a plan-independent a-la-carte credit wallet (`AiPackageCreditAccount.SpeakingOnlyCredits`/`FlexibleCredits`, purchased via `app/ai-packages/page.tsx`). No "Practice Card Access" tier/flag concept anywhere (zero grep matches for `practice_card`/`PracticeCard`). |
| §11 | Same engine, profession-specific cards/rulebooks/scoring | CONFIRMED | One `SpeakingExamService`; `rulebooks/speaking/{profession}/rulebook.v1.json` per profession |
| §12 | AI/tutor/admin/combined feedback; 9-criteria OET rubric; card type visible to graders not students | CONFIRMED | 4 linguistic (0-6) + 5 clinical (0-3) criteria; `SpeakingTutorAssessment.MarkerRole`, double-marking + `SpeakingModerationService` |
| §13 | Booking: tutor availability, profession, mode, exam/practice, credit/quota validation — all pre-confirmation | CONFIRMED, tested, all 5 checks server-side | `PrivateSpeakingService.CreateBookingAndCheckoutAsync`, Serializable DB transaction, idempotency-key dedup, `NormalizeProfessionTrack` allow-list, `ResolveEligibleSpeakingSubscriptionAsync` quota check before booking row is written |
| §14 | Pricing packages clearly expose 4 separate, distinctly-labeled quotas (AI Speaking / Human Tutor Speaking / Practice Card Access / Full Mock Exam Access) | **GAP** | Only 2 generic fields exist: `AiPackage.speakingCredits` (AI grading only) and `AiPackage.mocks`/`mockExamsRemaining` (generic, not speaking-specific). Human tutor sessions live in a separate domain not surfaced in the same package editor. "Practice Card Access" has zero billing-model representation — confirmed in `ai-package-editor.tsx`, `plan-catalog-editor.tsx`, and the team's own `docs/product-strategy/07_subscription_pricing_and_entitlements_strategy.md`. |
| §15.1-15.11, 15.13 | Mission-critical rules 1,2,3,4,5,6,7,8,9,10,11,13 | CONFIRMED | Each maps to a row above |
| §15.12 | Student UI matches attached screenshot | CANNOT VERIFY | No screenshot provided |
| §15.13 | Prep screen instructs "bring blank paper and pen for rough work" | CONFIRMED (substance), only in current route tree | Present in `app/speaking/exam/page.tsx:52-58`, `app/speaking/exam/[id]/page.tsx:209-215,223-232`, `app/speaking/roleplay/[id]/page.tsx:143-157` (which adds a paper tear/cut-on-camera step the doc doesn't mention). Absent from legacy `app/speaking/sessions/[id]/prep/page.tsx` — see architectural finding above. |
| §16 | Suggested system flow (profession → mode → tutor type → credit check → intro → A → B → feedback) | CONFIRMED, broadly | Matches actual flow; exact UI ordering of profession-vs-mode selection is a cosmetic detail, not functional |
| §17 | Access control enforced at backend, not just hidden in UI | CONFIRMED, most heavily tested claim in the audit | Separate learner/admin DTOs at the type level, JWT-scoped endpoints, tests asserting patient card/card type never reach the learner in both AI and live-tutor modes |

## Confirmed genuine gaps (prioritized)

1. **Pricing package UI doesn't cleanly expose the 4 quota types the doc requires** (§14). AI vs. human-tutor credits are functionally separate under the hood, but Practice Card Access has no billing representation, and Full Mock Exam Access is a generic non-speaking-specific field.
2. **Practice mode isn't plan-tiered** (§10/§14) — gated by a universal a-la-carte credit wallet instead of a per-plan quota/flag.
3. **Dual Speaking route trees** — needs a decision on whether `app/speaking/sessions/[id]/*` + `PrepCountdown.tsx` are dead code, since the legacy tree is missing the paper/pen instruction.

## Open items

- Confirm whether a screenshot was meant to accompany the original requirements doc (§5, §15.12 unverifiable without it).
- Confirm whether Speaking's profession list should match Writing's full 12-profession enum or is intentionally narrower.
- ~~Decide whether gaps #1 and #2 warrant a follow-up implementation plan, or are acceptable as-is.~~ **Resolved — implemented same day, see below.**

## Implementation status (2026-07-01)

Gaps #1 and #2 were implemented; gap #3 turned out not to need code changes (see correction above).

### Gap 2 — "Practice Card Access" is now plan-tierable

- New `BillingPlan.SpeakingPracticeAccessEnabled` / `BillingPlanVersion.SpeakingPracticeAccessEnabled` (bool, default `true` — every existing plan keeps today's behavior unchanged).
- Wired into `EffectiveEntitlementResolver` (`EffectiveEntitlementSnapshot.SpeakingPracticeAccessEnabled`) alongside the existing `SpeakingAddonsEnabled` pattern.
- Enforced in `SpeakingSessionService.FinishWarmupAsync`: an **eligible** subscription whose plan disables this flag is blocked (403 `speaking_practice_not_included`) before any credit is touched. No-subscription / free accounts (today's a-la-carte AI-credit buyers) are **never** blocked — the gate only applies to a resolved, eligible plan that explicitly disables it, preserving all existing purchase behavior.
- Admin UI: `app/admin/billing/page.tsx` gained a "Speaking Practice Card Access" checkbox next to the existing add-on flags, with explanatory copy distinguishing it from AI Speaking Credits and Human Tutor Speaking.
- Tests: `backend/tests/OetLearner.Api.Tests/Speaking/SpeakingPracticeAccessGateTests.cs` (4 new tests — blocked/allowed/no-subscription-never-blocked/no-resolver-wired-regression-safe).

### Gap 1 — pricing packages now expose 4 distinct, labeled quotas

- **AI Speaking Credits** vs **Full Mock Speaking Exam Access** are now functionally distinct, not just relabeled. Previously both `ai_self_practice` and the 2-card `ai_exam` drew from the same `SpeakingOnlyCredits` wallet. Now `SpeakingExamService.DebitCardAsync` tries the account's `MockExamsRemaining` allowance first (one unit = one whole two-card exam) before falling back to the existing per-card `SpeakingOnlyCredits` debit — fully backward compatible since `MockExamsRemaining` defaults to 0 for every existing account.
  - New `SpeakingExamSession.FundedByMockCredit` column records which path funded a given exam so Card B's debit can no-op when Card A was mock-funded.
  - `CreateExamAsync`'s pre-flight credit gate updated to pass when `MockExamsRemaining >= 1`, even with insufficient `SpeakingOnlyCredits`.
  - Admin UI: `components/admin/billing/ai-package-editor.tsx` relabeled "Speaking credits" → "AI Speaking Credits (practice)" and "Mock exams" → "Full Mock Exam Credits", each with hint text explaining the distinction.
  - Tests: 4 new tests in `SpeakingExamServiceTests.cs` (mock-funds-whole-exam / Card-B-no-double-charge / mock-alone-satisfies-gate / still-blocked-without-either).
  - **Bug caught during testing:** `AiPackageCreditService.DeductMockAsync` has a "legacy account" grandfather bypass that silently grants a free mock credit to any account with zero `MockExamsRemaining` and no grant history — correct for its original caller (`MockService`, a genuine pre-quota legacy case) but would have given every existing Speaking account (all of which have zero `MockExamsRemaining` today) a free exam. Fixed by gating the call on a snapshot check (`MockExamsRemaining > 0`) before ever invoking `DeductMockAsync`, which structurally avoids the bypass. Caught by 4 pre-existing tests failing on first test run; all pass after the fix.
- **AI Speaking Credits** vs **Human Tutor Speaking**: already functionally separate under the hood (`AiPackageCreditAccount.SpeakingOnlyCredits` vs `Subscription.SpeakingSessionsRemaining`); admin UI relabeled for clarity — `app/admin/billing/page.tsx`'s "Speaking session add-ons" checkbox and "Speaking sessions" bundled-grant input now explicitly say "Human Tutor" and carry a hint block distinguishing all three quota types.
- Frontend types updated end-to-end: `lib/billing-types.ts` unaffected (different, learner-facing minimal type); `lib/types/admin.ts`, `lib/api.ts`, `lib/admin.ts`, `lib/catalog-presentation.ts` all carry the new `speakingPracticeAccessEnabled` field through the admin CRUD → public catalog pipeline.

### Migration

- `backend/src/OetLearner.Api/Data/Migrations/20260715090000_AddSpeakingPricingEntitlementFields.cs` — hand-written, raw-SQL `ADD COLUMN IF NOT EXISTS`, following the team's established pattern (does not touch the EF model snapshot, matching the documented WS6 decision in `docs/speaking/PROGRESS.md`).

### Validation performed

- `dotnet build` (backend): 0 errors, only pre-existing unrelated warnings.
- `dotnet test` filtered to Speaking suites: 55/55 pass (including the 4 new + 4 previously-broken-then-fixed).
- `dotnet test` filtered to Billing/Entitlement/AdminFlows/Oet2026/AiPackage: 457/457 pass — no regressions in the broader financial/admin system.
- `pnpm exec tsc --noEmit`: clean except pre-existing unrelated errors (missing `driver.js`/`@paypal/react-paypal-js` packages, stale `.next` type-gen cache) — confirmed 2 real errors from this work (test fixtures missing the new required field) and fixed both.
- `pnpm run lint` / targeted `eslint`: 1 real issue found and fixed (unescaped quote in new admin UI copy); rest is pre-existing, unrelated to touched files.
- `pnpm test` (vitest) on catalog + speaking component suites: 18/18 pass.
- `lib/__tests__/api.test.ts`: 3 failures (`window.localStorage.setItem is not a function`) reproduced identically with this session's `lib/api.ts` changes fully `git stash`-ed out — confirmed pre-existing test-environment issue, unrelated to this work.

### Still open (unchanged from the original audit)

- §5/§15.12 (screenshot match) — still unverifiable, no screenshot was ever provided.
- Speaking's profession-list breadth vs Writing's 12-profession enum — still unconfirmed, no code changed pending that answer.
