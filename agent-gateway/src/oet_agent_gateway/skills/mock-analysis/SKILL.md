---
name: oet-mock-analysis
description: Analyses OET mock performance against canonical subtest pass anchors and produces ranked next steps.
---

# Mock Analysis - Rules Package

Sources of truth in this repository:

- `docs/SCORING.md` — anchors: Listening/Reading 30/42 == 350/500; Speaking 350; Writing country-aware.
- `backend/src/OetLearner.Api/Services/Assessment/` — MockNextStepRouteResolver, mock review pipeline.
- `lib/scoring.ts` / `OetScoring` — canonical score conversions; never inline thresholds.

## Analysis discipline

1. Rank by impact: delta to the 350 pass anchor, not raw count of errors.
2. Next steps must be item-type specific (e.g. "Listening Part C attitude questions drill", "Writing organization - paragraphing").
3. Strengths must be evidence-anchored (subtest or item type the learner reliably passes).
4. Max 5 next steps; each actionable within one study session.
5. Never state metrics that are not in the input; label inferred values as estimates.
