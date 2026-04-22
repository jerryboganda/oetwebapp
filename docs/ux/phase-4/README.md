# Phase 4 — Heuristic Evaluation (All Routes)

## Artifacts
- `scorecard.csv` — one row per route, schema in [UX-AUDIT-HEURISTICS.md](../UX-AUDIT-HEURISTICS.md).
- `gap-register.md` — every gap `UX-<portal>-<nnn>` with severity, owner, acceptance, status.
- `critical-fix-list.md` — launch blockers extracted from gap register (severity 🔴).

## Process
1. Score each route using the template in `UX-AUDIT-HEURISTICS.md`.
2. Any score < 2 on any heuristic spawns a gap entry.
3. Triage weekly: severity re-assessed with product + eng.
4. Gaps carry forward to Phase 8 backlog.

## Exit criteria
- 100% of inventory routes scored.
- Zero 🔴 gaps without an owner.
- Gap register indexed by portal, severity, heuristic.
