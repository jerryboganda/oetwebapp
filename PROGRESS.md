# PROGRESS - Active Agent Continuity

Last updated: 2026-06-06

This file is intentionally compact. Historical progress was archived locally at
`.github/context-archive-20260531/PROGRESS-before-continuity.md` and is also
available through git history. Do not paste long historical ledgers back here.

## Current Operating Goal

Backend/API investigation and fix pass for confirmed authorization,
frontend/backend contract, deployment config, and focused regression gaps.

## Current State

- Writing marking-surface endpoints now require expert auth plus an assigned
  claimed/submitted tutor-review assignment before reading context,
  pre-assessment, annotations, submission, or moderation data.
- Speaking review voice notes now require active expert review assignment,
  same-uploader ready audio media, and a non-terminal review state.
- Admin notification RBAC now has a canonical `notifications` permission and
  registered `AdminNotifications` policy.
- Admin billing analytics now exposes `/v1/admin/billing/analytics` with the
  frontend contract shape.
- Admin audio-preview helpers now use shared API-client blob handling.
- Conversation SignalR now routes through `/api/backend/v1/conversations/hub`.
- Production-like auto-migrate defaults are false; GHCR deploy path validates
  `.env.production`; env examples are restored with safe placeholders.
- Writing evaluation profile-country lookup now uses SQLite-friendly
  materialize-then-sort behavior, matching the existing learner-goal lookup.
- Investigation report and issue register:
  `docs/backend-api-investigation-2026-06-06.md`.

## Validation Snapshot

- Focused backend tests for the changed auth/contract surfaces: passed, 67 tests.
- Writing evaluation pipeline focused tests: passed, 4 tests.
- `pnpm exec tsc --noEmit`: passed.
- `pnpm run lint`: passed with 0 errors and 364 existing warnings.
- `pnpm test`: passed, 280 files and 1,866 tests.
- `pnpm run backend:build`: passed with existing warnings.
- `pnpm run backend:test`: failed, 14 failed / 758 passed / 772 total, then test host crash warning.
- Local PostgreSQL TCP check on `127.0.0.1:5432`: succeeded.

## Next-Step Protocol For New Agent Runs

1. Read `AGENTS.md`, `.github/copilot-instructions.md`, this file, and
   `.github/agent-state.local.md` if it exists.
2. Run a scoped `git status --short -- <relevant paths>` instead of dumping the
   whole tree.
3. Continue from `## Next Concrete Step` in `.github/agent-state.local.md` when
   it matches the user's newest request.
4. If state is stale or conflicts with the newest user request, update the
   state file and follow the newest user request.
5. Before ending substantial work, update `.github/agent-state.local.md` with
   goal, touched files, validation, blockers, and next concrete step.

## Active Risks

- Full backend test suite is not green. The next pass should triage the 14
  failing backend tests listed in the investigation report.
- `/v1/learner/quick-session` is still a missing backend route; the frontend
  intentionally falls back to local questions.
- Local app-level `/health/live` and `/health/ready` were not exercised after
  this patch set.
