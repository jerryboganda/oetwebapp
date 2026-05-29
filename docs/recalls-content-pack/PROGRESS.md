# PROGRESS — Recalls Real Content Pack v1

> **Current status (2026-05-29)**: historical/live ledger awaiting reconciliation. Treat unchecked items here as candidate backlog only after verifying current code and [`../STATUS/REMAINING-WORK.md`](../STATUS/REMAINING-WORK.md); do not assume this file alone reflects project truth.

> Live ledger. Update every phase. Newest first.

## 2026-05-05 — Wave 1 + Wave 2 kickoff

### Decisions
- **Professions:** all 12 OET professions.
- **Tag model:** matrix — `Category` (functional) **×** new `OetSubtestTagsJson`.
- **Quality gate:** all seeded rows ship `Status='draft'`.

### Wave 1 — Infrastructure
- [ ] Migration `20260505000000_AddVocabularySubtestTags`
- [ ] `VocabularyTerm` entity update (3 new columns)
- [ ] Seed JSON `SCHEMA.md` spec
- [ ] `RecallsContentSeeder` (idempotent)
- [ ] DI wiring + `DatabaseBootstrapper` hook
- [ ] Audio backfill admin endpoint
- [ ] Frontend matrix filter
- [ ] Backend test
- [ ] Frontend test

### Wave 2 — Content (6 parallel subagents)
- [ ] A: Medicine + Nursing
- [ ] B: Dentistry + Pharmacy
- [ ] C: Physiotherapy + Occupational Therapy
- [ ] D: Radiography + Optometry
- [ ] E: Veterinary + Dietetics
- [ ] F: Speech Pathology + Podiatry

### Wave 3 — Validation
- [ ] `npm run backend:test` (RecallsContentSeederTests)
- [ ] `npx tsc --noEmit`
- [ ] `npm run lint`
- [ ] Manual smoke
