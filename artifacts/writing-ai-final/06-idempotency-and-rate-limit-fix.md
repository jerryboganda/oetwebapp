# 06 — Idempotency & Rate-Limit Fix

## Duplicate prevention (three layers)

1. Client stable key (`lib/writing/submit-keys.ts`): same (scenario, content)
   reuses the key across double-taps, resends, and the single 429/409 retry;
   edited content mints a new key. Used by practice + paper session pages.
   Tested (`submit-keys.test.ts`, 4 cases).
2. Server idempotency key: exact `(UserId, IdempotencyKey)` match returns the
   existing row; unique-constraint race re-queries instead of double-inserting.
3. Server content guard (new): same user+scenario+mode+letter-hash, non-revision,
   created within 10 min → returns the existing row. Collapses double-taps that
   carry different random keys. Revisions excluded (they intentionally branch).
   Tested (`CreateSubmissionAsync_DifferentKeysSameContent_ReturnsSingleRow`;
   `…_DifferentContent_CreatesSeparateSubmission`).

Deeper safety nets (pre-existing, verified): atomic claim
(`queued|preflight → grading`, loser gets 409 or reuses), grade-reuse by
`ReuseKeyHash` within TTL (zero AI calls), reservation idempotency on
`writing-grade:{submissionId}`, provider-result persistence (DB failure after
a successful rubric call resumes without a second paid call), AI-operation
dedupe (`writing_rubric_already_in_progress`).

## Rate limits (unchanged values, new behavior around them)

- `AiScoring` 2/min on submit/revise/retry-grade; `PerUser` 5000/min;
  writing-submissions daily budget (20 free / 100 paid). 429 body:
  `{code: rate_limited, message: "Too many requests. Please try again later.",
  retryable: true}`.
- One Submit = 1 request → never 429s. Double-tap = 2 requests (fits 2/min)
  and dedupes to 1 submission. A 429/409 on submit waits 2.5 s and retries
  ONCE with the same key, then surfaces normally (draft autosaved).
- Provider 429/5xx: `AiRetryPolicy` (max 3, exp backoff 200 ms·2ⁿ + 25%
  jitter, honors Retry-After) inside `AiGatewayService`; retries share the
  single credit reservation → no duplicate deduction, no duplicate history.
  Persistent outage: submission stays `failed` with letter intact → controlled
  `retry-grade` resume.

## Billing/credit rules (verified in code)

- Internal retries reuse `CreditReservationId` → single debit; terminal
  failures `ReleaseAsync` (refund) via `GradeWithReservationAsync` catch.
- Blank submissions hold no credit (no provider cost incurred).
- `retry-grade` reuses the submission's business reference (no new debit).

## Concurrency expectation

10 parallel duplicate submits → 1 row (content guard) or N rows collapsing to
1 provider call (claim + reuse + reservation idempotency). Worst case
measured in tests: 1 provider call, 1 grade row per logical attempt.
A live 10-way parallel test was NOT executed (no staging harness in this
environment) — recommended as a post-deploy check (script the same POST ×10
with distinct keys, assert one `AiUsageRecord` for `writing.score.v1`).
