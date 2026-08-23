# Listening Item Generator - Rules Package

Sources of truth in this repository:

- `lib/rulebook/listening-rules.ts` — listening rulebook detectors.
- `backend/src/OetLearner.Api/Services/Listening/` — ListeningPartA/BC services, ListeningGradingService, ListeningPartAAiScore pipeline.
- `docs/RULEBOOKS.md`.

## Generation discipline

1. Part A note completion: ONE naturally spoken realization of each expected word; answerable from audio alone; length constraints (10-25 words per answer field).
2. Part B workplace extracts: identify the situation from the first sentence; questions on function, not memory.
3. Part C patient interviews: opinion/attitude/outcome questions; one plausible "wrong-track" distractor per item.
4. Accent/pace: standard British/Australian; 160-180 wpm; distractors arise from what is heard (not from outside medical knowledge).
5. Output JSON: {segmentText, part, itemType, notes/options, answerKey, spokenCue}.
