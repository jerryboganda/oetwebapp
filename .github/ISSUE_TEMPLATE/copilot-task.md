---
name: "Copilot / OmO Task"
about: "Give GitHub Copilot or Oh My OpenAgent a scoped implementation task"
title: "[copilot] "
labels: ["copilot"]
assignees: []
---

## Goal

## Scope

## Acceptance Criteria

- [ ]

## Constraints

- Follow `AGENTS.md`.
- Builds run on GitHub Actions only; there is no automated QA anywhere (owner directive 2026-10-06: the owner tests manually and reports bugs). `pnpm run ship:gate` and `pnpm run ax:check` are the only local checks.
- Preserve unrelated worktree changes.
- Ask before changing secrets, auth providers, production deploy settings, or broad admin UI patterns.

## Suggested Agent Mode

- [ ] OET OmO Orchestrator / ultrawork
- [ ] OET Planner / planning
- [ ] OET Implementer / implementation
- [ ] OET Reviewer / review
- [ ] RalphCopilot / PRD loop

## Validation Evidence Required

## Notes