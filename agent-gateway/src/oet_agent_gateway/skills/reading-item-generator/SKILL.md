---
name: oet-reading-item-generator
description: Generates OET Reading medicine items with text-verifiable answers and disciplined distractors for admin review.
---

# Reading Item Generator - Rules Package

Sources of truth in this repository:

- `lib/rulebook/reading-rules.ts` — reading rulebook detectors.
- `backend/src/OetLearner.Api/Services/Reading/` — ReadingGradingService, SkillScoringService; reading is server-authoritative (see AGENTS.md domain invariants).
- `docs/RULEBOOKS.md` — reading rulebook conventions.

## Item-generation discipline

1. Faithful to OET format: Part A speed-reading (25-70 words text, 20-30 min weighting), Part B workplace texts (short), Part C extended medicine articles.
2. Text-validity: every answer must be directly justified by a verbatim span in the given text. Never rely on outside knowledge.
3. Distractor quality: each distractor must be plausible from the text (misread, misremembered, wrong-name, wrong-direction) and objectively wrong.
4. One clear best answer. No cue-anchoring (answer text in the stem), no negatives doublings, no 'all/none of the above'.
5. Output: JSON with item, 4 options, answerIndex, evidenceQuote; the platform stores provenance (publish gates).
