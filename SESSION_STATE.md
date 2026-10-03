# SESSION STATE

Session: learner-dashboard-fixes
Goal: Every learner-dashboard header element is functional for all learners: true merged XP/streak numbers, real profile identity, a bell that tells the truth (no false offline, no pinned 99+), the gamification flag actually honored, shipped to production with Actions evidence.
Mode: verify
Updated: 2026-10-04T00:00:00Z
Branch: main
HEAD: c04b1b80e (pre-commit)

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

Continue and finish the interrupted "learner dashboard non-functional" round (opencode session ses_efcf9d8d0ffe, 3 Oct): the session's backend merge + retention edits survived in the tree, the frontend gaps it never reached are now implemented. Acceptance bar: qa-smoke green on the shipped SHA, production deployed, live health confirms the SHA, ledger rows recorded.

## Acceptance criteria

- [ ] AC-1 Profile chip shows the signed-in learner's real name/email (AppShell derives `effectiveUserSummary` from AuthContext when the caller omits `userSummary`).
- [ ] AC-2 Streak/level chips show merged truth: XP = grammar `LearnerXPs` + reading `LearnerXps`; streak = max(LearnerStreaks, StreakRecords, Users engagement); achievements criteria evaluate merged values; alltime leaderboard adds reading XP (weekly/monthly stay grammar-scoped). Backend xunit coverage.
- [ ] AC-3 Bell tells the truth: badge = unread + labeled admin alerts with 99+ cap; popover header counts the same way; degraded "offline" UI only after a real failed hub attempt (initial state `connecting`); inbox retention purges 180d rows and marks 14d stale unread read so the badge unpins.
- [ ] AC-4 `gamification` flag is honored: header badges hide on explicit `false` only (fail-open on fetch failure); seeding forces the flag on so production behavior is unchanged.
- [ ] AC-5 Theme toggle renders the real button immediately (no disabled placeholder flash); header icon chips carry tooltips.
- [ ] AC-6 Frontend vitest coverage for the identity fallback, bell counts/cap/degraded gating, and badge flag gating; all suites green in CI on the shipped SHA.

## Decisions (do not revisit)

- D-1 Badge/popover consistency = precise labeling (unread + admin alerts shown as separate parts in aria-label and header), not forcing admin ops alerts into the personal unread count — they are derived view models, not inbox rows.
- D-2 Flag semantics for the header chips are explicit-false-only (fail-open): a flag-endpoint blip must never strip the header chips; the seeder forces `gamification` on exactly like `strategy_guides`.
- D-3 Local compute stays banned (AGENTS.md): all builds/tests/typechecks run on GitHub Actions; fix-forward via push.

## Touched files

| Path | Change |
| --- | --- |
| backend/src/OetLearner.Api/Services/GamificationService.cs | merged XP/streak/achievement/leaderboard read-through (from interrupted session, verified) |
| backend/src/OetLearner.Api/Services/DataRetentionWorker.cs, Configuration/DataRetentionOptions.cs | inbox purge 180d + stale-unread mark-read 14d (from interrupted session, verified) |
| backend/src/OetLearner.Api/Services/SeedData.cs, SeedData.DemoUserData.cs | force `gamification` flag on at seed |
| backend/tests/OetLearner.Api.Tests/GamificationServiceTests.cs | new: merged XP/streak/achievement/leaderboard tests |
| components/layout/app-shell.tsx | `effectiveUserSummary` fallback (from interrupted session, verified) |
| components/layout/notification-center.tsx | popover header + aria-label count consistency, bell title |
| contexts/notification-center-context.tsx | initial connectionStatus `connecting` |
| components/ui/theme-toggle.tsx | CSS-swapped glyphs, no mounted placeholder |
| components/layout/learner-streak-badges.tsx | gamification flag gate + tooltips |
| components/layout/profile-menu.tsx | account tooltip |
| components/layout/__tests__/app-shell.test.tsx, notification-center.test.tsx, learner-streak-badges.test.tsx (new) | coverage for AC-1/3/4/5 |

## Verification gates

| Gate | Command / workflow | Evidence | Result |
| --- | --- | --- | --- |
| ship-gate | pnpm run ship:gate | NOT RUN | NOT RUN |
| frontend tsc + vitest + build | qa-smoke.yml | NOT RUN | NOT RUN |
| backend compile + tests | qa-smoke.yml (backend matrix, backend inputs changed) | NOT RUN | NOT RUN |
| images + deploy | build-images.yml / production-deploy.yml | NOT RUN | NOT RUN |
| live confirmation | app.oetwithdrhesham.co.uk/api/health + api health/ready + VPS image SHA | NOT RUN | NOT RUN |

## Blockers

- None. (Parallel Codex agent shares this tree — stage explicit paths only; deploys queue, never cancel.)

## Next action

1. Commit the round (feature commit + chore(ax) ledger commit), confirm repo public, `pnpm run ship`, watch qa-smoke green on the SHA, confirm live, `ax:record`, flip repo private after runs.
