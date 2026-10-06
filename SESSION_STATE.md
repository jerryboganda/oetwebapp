# SESSION STATE

Session: writing-ai-p0-queue
Goal: Fix the 6 Oct 2026 Writing P0 (letters stay Queued / no result): bound the secondary-reviewer hold, keep the 15-minute release honest, prove nothing is lost or double-charged, and unblock sample-PDF clarity.
Mode: verify
Updated: 2026-10-07T01:30:00Z
Branch: work/2026-10-05
HEAD: 1233ea32f

<!--
The current run's working memory. This is layer 2 of three:
  1. AGENTS.md / .github/instructions/**  permanent rules
  2. SESSION_STATE.md + TASKS.json         this run        <- you are here
  3. VERIFICATION.md / git / Actions runs  objective truth

Rules
- Header keys are required, and `Mode` must be plan | execute | verify | blocked | done.
- The seven H2 sections below are required and the order is load-bearing
  (`pnpm run ax:check` enforces it).
- Never tick a gate without evidence. A `PASS` row needs a run id, a workflow
  file, or `local:<command>`. Record real runs with `pnpm run ax:record`.
- Keep it short. It is working memory, not a history file.
- Two sessions writing this at once? Take the newer `Updated:` block wholesale —
  do not hand-merge. The durable, merge-safe ledger is PROGRESS.md.
-->

## Objective

Owner P0 (6 Oct 2026 PDF "Live grading, timed release, reviewer verification & sample clarity"): live Writing grading returns no result. Root cause (code-proven, production rows not readable by the agent): b3b10fdc8 made the Codex GPT-6.1 Sol reviewer a mandatory, default-Enforce, UNBOUNDED hold on every letter; any reviewer failure re-queues the letter (2/5/15/30 min) and finally fails it, so the learner sees "Queued / taking longer than usual". Fix: bound the hold, complete on the primary grade flagged for a tutor, never loop. Also make the 00:00 state honest, refresh Past submissions, stop a stale service-worker timer, and report the sample-PDF source problem.

## Acceptance criteria

- [ ] AC-1 A reviewer outage can no longer leave a letter Queued: at most ReviewMaxHolds (2) re-queues, then the letter completes on its primary result, flagged rv_unresolved + audited; a letter re-queued more often skips the review stage.
- [ ] AC-2 The 15-minute window stays server-anchored (SubmittedAt+15:00); at 00:00 a still-processing letter shows the real status ("still being assessed"), never "finalising" and never a new timer; Past submissions refreshes while a letter is processing.
- [ ] AC-3 No new provider call, AiUsageRecord or credit movement on the fallback; Max untouched; no migration; no hashed input change.
- [ ] AC-4 Production proof (owner QA, not agent-verifiable): owner account instant result; normal account 15:00 to 00:00 release; GPT-6.1 Sol reviewer calls visible on /admin/writing-ai (reviewerCallsWeek / reviewerSuccessesWeek) and writing.review.applied audit events.
- [ ] AC-5 Nursing Free Sample (Adam White) sharp on a phone: BLOCKED on an owner decision (original source or approved vector retype); other samples need the owner's visual QA.

## Decisions (do not revisit)

- D-1 No EF migration/column: release time = SubmittedAt+15 min at read time; reviewer state rides in ProviderResultJson, FeatureRecordJson and AuditEvent.
- D-2 Hashed regression inputs and WritingRuleEngine.ValidatorVersion untouched; every rule-engine relaxation is guarded by !IsModelAnswer.
- D-3 (REVISED 7 Oct 2026, supersedes "Enforce HOLDS on outage / never publishes unreviewed") Reviewer route stays writing-codex-sub gpt-6.1-sol via IAiGatewayService (writing.grade.review), Max never touched. The Enforce hold is BOUNDED: ReviewMaxHolds 2 and ReviewGiveUpMinutes 9, and never past what the release window allows; then WritingGradeReviewer.HoldExhausted completes the letter on its primary result (rv:off key, rv_unresolved tutor flag, writing.review.skipped audit). The owner PDF requires reviewer retry/fallback and "never permanently Queued"; "never publish unreviewed" was an implementer decision, not an AGENTS.md rule.
- D-4 Reviewer changes are applied by the pure deterministic applier (unchanged).
- D-5 Learner canon pages stay; revise endpoint is a hard 409 stub (unchanged).
- D-6 lib/catalog-website-packages.ts (another session's file) still says "instant" for Writing packages; left untouched.
- D-7 Not changed on purpose (reported, p2): early learner Retry for auto-retrying rows (needs the lost-claim fall-through fix first), dedicated reviewer provider row for circuit isolation, typed Codex sidecar errors, tutor/mock-human output not window-held, detail page does not auto-open at 00:00.

## Touched files

| Path | Change |
| --- | --- |
| backend/src/OetLearner.Api/Services/Writing/WritingGradeChain.cs | ReviewMaxHolds, ReviewGiveUpMinutes options |
| backend/src/OetLearner.Api/Services/Writing/Review/WritingGradeReviewer.cs, WritingReviewContracts.cs | HoldExhausted fallback outcome, doc comment |
| backend/src/OetLearner.Api/Services/Writing/WritingSubmissionEvaluationPipeline.cs | bounded hold + window-aware give-up, retry-cap bypass (flagged), Skipped branch flags tutor |
| backend/src/OetLearner.Api/Services/Writing/Crons/WritingCrons.cs | one DI scope per due row, heartbeat log |
| components/domain/writing/WritingReleaseCountdown.tsx, WritingMyWorkList.tsx, WritingStimulusViewer.tsx; app/(learner)/writing/submissions/[id]/grading/page.tsx (+test); messages/{en,ar}/writing.json | stillProcessing copy, polling, canvas oversampling, no false Queued flash |
| public/sw.js | bypass cached release-bearing GETs, cache v9 |
| lib/ai-management-api.ts, app/admin/writing-ai/page.tsx, docs/ops/WRITING-AI-PROVIDERS.md | reviewer KPI tile, bounded-hold policy |

## Verification gates

| Gate | Command / workflow | Evidence | Result |
| --- | --- | --- | --- |
| ship-gate (P0 fix 129e1c493) | pnpm run ship:gate | local:ship:gate | PASS |
| Build images (P0 fix 129e1c493): API + web compiled, Writing gates green | build-images.yml | 37521909385 | PASS |
| Deploy production (P0 fix 129e1c493): live X-Oet-Release 129e1c493 slot blue, web+api serving images verified, health 200 | production-deploy.yml | 37524732572 | PASS |

## Blockers

- Production rows/logs are not readable by the agent (no SSH/credentials by rule): whether the Codex route is down, the circuit open or the code throws is unconfirmed; the fix is correct for every cause. Not tested by the agent (owner directive): verified only by compilation in Build images plus live health/serving proof.
- Live acceptance checks 1-5 of the owner PDF need logged-in production sessions (owner QA or an owner-approved driven session).
- Adam White (Nursing Free Sample): only original on disk is a 960x540 JPEG; needs the owner's true original or an approved verbatim vector retype (CI-generated).
- lib/catalog-website-packages.ts still promises "instant" Writing feedback (another session's file).

## Next action

1. Owner QA on production (the P0 fix is live as 129e1c493): submit one letter on an allowlisted and one normal account, read the /admin/writing-ai reviewer tile and writing.review.* audit events; Retry any already-failed letter (it now completes at once). 2. Owner decides the Adam White source (true original, or approved verbatim vector retype).
