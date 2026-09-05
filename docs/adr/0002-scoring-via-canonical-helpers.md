# Scoring only via canonical helpers

All pass/fail and grade thresholds resolve through `lib/scoring.ts` on the frontend and `OetScoring` on the backend (anchor: Listening/Reading 30/42 == 350/500). No inline cut-scores in UI, endpoints, or tests, so the anchor can change in exactly two places.
