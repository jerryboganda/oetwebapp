# 2026-09-16 — Production freshness ship

Owner order: commit everything → push `main` → deploy production → make sure nothing is
uncommitted → everything fresh on the production VPS.

## State found at ship start
- Remote `main`: `792445367` (parent `d8932f1b`).
- Production VPS: healthy. Active slot `blue`; live images tagged `d8932f1b` (deployed 14:35Z).
- The `792445367` push (15:08Z) failed **every** workflow within 3–17s. GitHub annotation:
  "The job was not started because recent account payments have failed or your spending limit
  needs to be increased." The repo was **private**, so Actions minutes are billed and the
  deploy never ran.

## Unblock
Per the standing process (`AGENTS.md` → "GitHub Actions visibility"), the repository is flipped
**public** immediately before a run (free Actions minutes), the run executes, then the repo is
returned to **private**. No application code change was required to unblock the deploy.

## This commit
- `.gitignore`: ignore local agent/IDE state (`.swarm/`, `.claude-flow/`, `.claude.backup-*/`,
  `.mcp.json`, `ruvector.db`) so the working tree stops reporting machine-local state as
  "uncommitted" without committing it into git history.
- This release note.

No application code changed. Peer/parallel-session work-in-progress (`WritingRuleEngine*.cs`,
`WritingRev8RuleTests.cs`, `src-tauri/splash/splash.js`) was deliberately **not** staged
(explicit-path staging only; another session was actively editing during this ship).

## Rollback
The blue/green previous slot is retained; `.deploy/previous-good.env` plus the
`auto-deploy-ghcr.sh` health gate auto-roll the routers back on a failed promotion.
Code rollback is `git revert` followed by a normal forward deploy (never force-push to `main`).
