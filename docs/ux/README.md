# UX Audit Workspace

Complete, platform-wide UX audit for the OET Prep Platform. Read the master plan first, then drill into phases.

## Start here
- [UX-AUDIT-MASTER-PLAN.md](./UX-AUDIT-MASTER-PLAN.md) — objectives, personas, phases, success metrics.
- [UX-AUDIT-ROUTE-INVENTORY.md](./UX-AUDIT-ROUTE-INVENTORY.md) — every route, tier, persona, state.
- [UX-AUDIT-HEURISTICS.md](./UX-AUDIT-HEURISTICS.md) — scoring rubric + CSV schema.

## Phases
- `phase-1/` — Discovery & evidence (screenshots, content inventory, a11y baseline, analytics).
- `phase-2/` — Learner JTBD + journey maps + flow specs.
- `phase-3/` — Expert / Admin / Sponsor JTBD + journeys + flows.
- `phase-4/` — Heuristic evaluation scorecards + gap register.
- `phase-5/` — Content & copy audit + rewrites + voice guide.
- `phase-6/` — Accessibility remediation plan.
- `phase-7/` — Design-system & motion consistency.
- `phase-8/` — Prioritised backlog, rollout, validation, governance.

## How to contribute
1. Pick a route from the inventory in `todo` state.
2. Capture evidence into `phase-1/<route>/`.
3. Score against `UX-AUDIT-HEURISTICS.md`.
4. File gaps into `phase-4/gap-register.md` with IDs `UX-<portal>-<nnn>`.
5. For critical-path journeys, add/extend a JTBD + journey map in `phase-2` or `phase-3`.
6. Link Figma frames in the flow spec; flag "code-only" where no Figma is needed.

See the master plan § 6 "Definition of Done" before closing any gap.
