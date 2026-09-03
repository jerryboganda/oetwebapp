# 08 — RCA: "Too many requests. Please try again later."

## Candidate-facing symptom

A single legitimate Submit surfaces
`{code: rate_limited, message: "Too many requests. Please try again later."}`.

## Actual root cause (traced, not speculated)

The message is emitted ONLY by the ASP.NET rate-limiter rejection
(`Program.cs:320`, `RejectionStatusCode = 429`), i.e. the request never
reached grading. Three compounding causes:

1. `AiScoring` allows 2 submits/min/user (`Program.cs:380-391`) and is shared
   by submit + revise + retry-grade. A double-tap (2 POSTs) fits, but any
   third send within the window — browser retry, React re-fire, impatient
   third tap, or submit-then-immediate-revise — 429s.
2. The client minted a FRESH random `idempotencyKey` per send
   (`createSubmitIdempotencyKey()` per call), so the server treated each tap
   as a distinct logical attempt: 2 taps → 2 submission rows → up to 2 paid
   rubric calls + 2 credit debits. The per-key idempotency, claim fencing and
   grade-reuse layers could not see the duplicates as duplicates.
3. The per-submit live Model Answer fallback added a second provider call on
   tasks without an approved pregen, doubling provider-side rate pressure.

Provider-side 429s were already handled (gateway retries, max 3, backoff);
they were not the source of this message.

## Fix (this change)

- Stable client key per (scenario, content) + single 2.5 s retry on 429/409
  with the same key (frontend).
- Server content-hash guard: identical resend within 10 min returns the
  existing row — one Submit can never open two paid workflows (backend).
- Publish gate requires approved pregen → fallback trends to zero; blank
  letters cost zero calls.
- `AiScoring` 2/min KEPT (cost control); with dedupe, legitimate flows no
  longer trip it.
- `retry-grade` gives failed attempts a no-retype, no-double-charge resume.

## Proof

- `CreateSubmissionAsync_DifferentKeysSameContent_ReturnsSingleRow`
  (1 row, 1 provider call for a double-tap).
- `EvaluateAsync_SecondClaim_DoesNotOpenSecondProviderCall` (pre-existing).
- `GenerateMissing_*` (repeat runs add zero provider calls).
- Rate-limit rejection string occurs only in `Program.cs` (verified by search);
  no grading error path emits it.
