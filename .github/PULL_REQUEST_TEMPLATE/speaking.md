<!--
  Use this template for any change scoped to the Speaking module.
  Open the PR with the URL query: ?template=speaking.md
-->

## Summary

<!-- 1–3 sentences. What does this PR change and why? -->

## Plan phase(s)

<!-- Reference `~/.claude/plans/1-oet-speaking-module-sequential-candy.md` -->

- [ ] P1 Foundation
- [ ] P2 Role-play hardening
- [ ] P3 Warm-up
- [ ] P4 AI patient turn loop
- [ ] P5 Mock orchestrator
- [ ] P6 LiveKit Cloud
- [ ] P7 Tutor assess + calibration
- [ ] P8 Drills + pathway
- [ ] P9 Admin analytics
- [ ] P10 Compliance hardening
- [ ] P11 Content library
- [ ] P12 E2E + runbook

## Files touched

<!-- Paste `git diff --stat origin/main...HEAD` here. -->

## Migrations

- [ ] No migration in this PR.
- [ ] Migration: `____.cs` — safe under concurrent writes; rollback path documented.

## AI provider impact

- [ ] Feature routes unchanged.
- [ ] Modified `____` — provider/model: ____, prompt-caching: ____.

## Compliance impact

- [ ] No recording / consent surface changed.
- [ ] Touched recording lifecycle / consent — audit logging verified, retention windows confirmed.

## Test plan

- [ ] `dotnet test backend/OetLearner.sln --filter "FullyQualifiedName~Speaking"`
- [ ] `npm test`
- [ ] `npm run lint`
- [ ] `npx tsc --noEmit`
- [ ] Local smoke: `./scripts/speaking-smoke.sh`
- [ ] Playwright (if E2E-relevant): `npx playwright test tests/e2e/speaking-*.spec.ts`
- [ ] Axe (if UI-touching): `npx playwright test tests/a11y`

## Screenshots / recordings

<!-- Required for any user-visible UI change. -->

## Rollback plan

<!-- How do we revert if this goes wrong in production? Flag toggle? Revert PR? -->

## On-call

- [ ] On-call paged / awaiting paging
- [ ] No on-call impact
