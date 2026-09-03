# 10 — Performance / Cost Verification

Measured by unit-test provider-call counting (CountingGateway) and static
call-graph analysis. Live latency/$-figures: NOT MEASURABLE IN CURRENT
ENVIRONMENT (no staging provider harness or prod traffic access from dev host).

| Metric | Before | After | Change |
|---|---|---|---|
| AI calls per normal fresh submit | 1 rubric + 0/1 canon-LLM + 0/1 live model-answer | 1 rubric + 0/1 canon-LLM + 0 model-answer (pregen) | −0/1 call |
| Model Answer calls per submit | 0 (pregen ready) else 1 | 0 (gate enforces pregen for new tasks) | → 0 for prepared tasks |
| OCR/PDF calls per submit | 0 | 0 | unchanged |
| Provider calls, double-tap | up to 2 (2 rows) | 1 (1 row) | −1 call + −1 credit debit |
| Provider calls, blank submit | N/A (rejected) | 0 | −1 call, −1 debit |
| Provider calls, identical retry in TTL | 0 (reuse) | 0 (reuse; dedupe even earlier) | unchanged |
| Rulebook source loads per submit | cached JSON (loader cache) | cached JSON (unchanged) | unchanged |
| Grading input tokens | LT-* tasks got generic rules only | + applicable letter-type rules (more precise, marginally more tokens) | quality fix |
| Candidate latency | — | −1 model-answer generation on unprepared tasks; −full AI wait on blanks | improved (unmeasured live) |

Billing invariants (code-verified): one logical submit = one reservation;
internal retries share it; failures release (refund); blanks and reuses hold
nothing; `retry-grade` reuses the submission reference. Candidate surfaces
show credits/attempts, never raw tokens (unchanged).
