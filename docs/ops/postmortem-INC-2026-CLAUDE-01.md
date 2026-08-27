# Postmortem — INC-2026-CLAUDE-01

> Written against the **Postmortem Template** in
> [`docs/ops/incident-response-runbook.md`](./incident-response-runbook.md).
> All figures below are either (a) produced by the queries named in-line or
> (b) an externally confirmed card/prepaid transaction. **No figure in this
> document is estimated or invented.** Where a number is not yet exported it is
> marked `PENDING EXPORT` together with the exact query that produces it.

- **Incident title:** Listening Part A AI advisory scorer — unbounded paid retry loop on evidence-free attempts
- **Severity:** SEV-2 (provider/cost incident; no candidate-facing outage, no data loss)
- **Start/end time UTC:** `PENDING EXPORT` — take `first_call_utc` / `last_call_utc` from
  `scripts/ops/export-ai-incident-evidence.sql` **section 1**
- **Customer impact:** None to marking. Listening scores are produced by the
  deterministic answer-key grader (`ListeningGradingService`) and were never
  touched. The only candidate-visible effect is that four submitted attempts
  never received the optional AI advisory commentary. Advisory work that the old
  key rejected with 401 is quarantined, **preserved and recoverable** — see
  *Recovering the credential-quarantined work* below.
- **Detection source:** Anthropic billing/credit alert followed by source review.

---

## Root cause

`ListeningPartAAiScoringService.ScoreAttemptAsync` filtered its gap list down to
gaps with an **effective, author-approved** `AssessmentRationale`, then resolved
the Anthropic provider and POSTed the request **without checking whether that
list was empty**.

For an attempt with zero eligible rationales the result was:

1. An evidence-free prompt was sent to `POST /v1/messages` (a real, billable call
   whenever the credential was valid).
2. No returned verdict could be matched back to a `QuestionNumber`, so `scored`
   stayed `0`.
3. Because nothing was stamped, `ListeningAnswer.AiScoredAt` stayed `NULL`.
4. `ListeningPartAAiScoringWorker` selected on `AiScoredAt == null` only, so the
   same attempt was re-selected on the next 20-second tick — in **both** the blue
   and green API slots, which each run every `AddHostedService`.

There was no attempt counter, no backoff, no terminal state and no credential
quarantine, so the loop had no exit condition other than an operator disabling
the feature flag.

Contributing factors:

- Raw provider error bodies were persisted into `AiUsageRecord.ErrorMessage`
  (`Truncate(body, 500)`), which is both a privacy and a triage hazard.
- The scorer calls Anthropic directly rather than through the canonical provider
  adapter, so none of the gateway's guards applied.

---

## Cost accounting — three separate figures

Anthropic API usage is funded from prepaid credits (with optional auto-reload),
and **failed requests are not charged**. These three numbers are therefore
different things and must never be added together or presented as one:

| # | Figure | Value | Evidence |
|---|---|---|---|
| 1 | **Successful provider requests** (the only requests that can consume credit) | `PENDING EXPORT` — `successful_rows` and `estimated_billable_usd` | `export-ai-incident-evidence.sql` §1, §2 (rows with `outcome_enum = 0`), §3 |
| 2 | **Failed authentication calls** (HTTP 401/403) | count = `PENDING EXPORT`; **provider usage = $0.00** — Anthropic does not charge failed requests | `export-ai-incident-evidence.sql` §2, filtered to `error_class = 'http_401'` / `'http_403'` |
| 3 | **Card / prepaid-credit reload** | **$30.00** (single confirmed card transaction) | Anthropic billing console + card statement. This is a *balance top-up*, **not** a measure of usage. |

Reading the table correctly:

- Figure 3 is money that moved to Anthropic as **prepaid balance**. It does not
  by itself tell you how much of that balance was consumed.
- Figure 1 is the only figure that consumes prepaid balance. Reconcile it against
  the Anthropic console's *successful usage* view, not against figure 3.
- Figure 2 is loud in the logs and in `AiUsageRecords`, but it is **$0.00** of
  provider usage. It is included so the incident narrative explains the request
  volume without implying spend.

Reference: [Anthropic — how do I pay for my Claude API usage](https://support.claude.com/en/articles/8977456-how-do-i-pay-for-my-claude-api-usage).

### Evidence commands

```bash
# Run from the deploy root on the VPS. These scripts live on the HOST, so they
# are piped into the container on stdin with `-f -`.
cd /opt/oetwebapp

# Full read-only export (window, usage rows, routes, model/pricing, credential
# fingerprint only — never the key itself).
docker exec -i -e PGPASSWORD="$POSTGRES_PASSWORD" oet-postgres \
  psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 \
  -f - < scripts/ops/export-ai-incident-evidence.sql

# Widen the window if the first call predates the default lookback. Variable
# overrides go BEFORE `-f -`, because psql reads the script last.
docker exec -i -e PGPASSWORD="$POSTGRES_PASSWORD" oet-postgres \
  psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 \
  -v lookback_days=120 \
  -f - < scripts/ops/export-ai-incident-evidence.sql
```

---

## What worked

- The advisory/canonical split held. `IsCorrect`, `PointsEarned` and `MissReason`
  were produced by the deterministic grader and are unchanged throughout.
- Every provider call already produced an `AiUsageRecord`, so the incident is
  reconstructable from the database rather than from logs alone.
- The `Listening__PartAAiScoring__Enabled` break-glass flag existed.

## What failed

- No zero-evidence guard before provider resolution/invocation.
- No durable attempt counter, backoff, terminal state or credential quarantine.
- Worker eligibility was a single nullable-timestamp check, duplicated across two
  API slots.
- Raw provider error bodies were persisted.

---

## Remediation shipped (W0)

| Change | Location |
|---|---|
| Evidence resolved before any provider resolution/invocation; zero eligible evidence ⇒ terminal `skipped_no_evidence`, zero HTTP calls, `AiScoredAt` left `NULL` | `Services/Listening/ListeningPartAAiScoringService.cs` |
| Locally valid but empty/unmatchable response ⇒ terminal `no_matching_verdicts` (never a second paid call for identical evidence) | `Services/Listening/ListeningPartAAiScoringService.cs` |
| Only gap numbers that were actually in the prompt can be stamped; an out-of-prompt (hallucinated) verdict number is discarded and the row is closed `skipped_no_evidence` with `AiScoredAt` left `NULL` | `Services/Listening/ListeningPartAAiScoringService.cs` |
| Response objects disposed on every path (including caller cancellation); only provably pre-send failures (DNS, proxy tunnel) retry — connect/TLS/ambiguous post-send/body-read failures are terminal `indeterminate_timeout` and are never re-called | `Services/Listening/ListeningPartAAiScoringService.Anthropic.cs` |
| Bounded scheduling: at most 3 **scheduled** attempts per answer, `Retry-After` honoured, jittered backoff, terminal on 401/402/403, invalid model/config, ambiguous post-send/body-read outcomes | `Services/Listening/ListeningPartAAiRetryPolicy.cs` |
| Raw provider bodies no longer persisted — sanitized error class only | `Services/Listening/ListeningPartAAiScoringService.cs` |
| Worker excludes terminal / attempt-capped / future-scheduled rows, and returns the remainder **oldest-due-first with a stable id tie-break** so a backlog larger than one batch cannot starve | `Services/Listening/ListeningPartAAiScoringWorker.cs` |
| Additive columns `AiSkipReason`, `AiAttemptCount`, `AiNextAttemptAt`, `AiIncidentId` | `Domain/ListeningEntities.cs`, migration `20261101090000_AddListeningAnswerAiSkipAndRetry` |
| Read-only evidence export | `scripts/ops/export-ai-incident-evidence.sql` |
| Idempotent, fail-closed incident closure (tag only, never delete, never re-score) | `scripts/ops/close-listening-incident-attempts.sql` |
| Manual, explicitly confirmed recovery of credential-quarantined work | `scripts/ops/requeue-listening-credential-quarantined.sql` |
| Regression tests | `backend/tests/OetLearner.Api.Tests/Listening/ListeningPartAAiScoringGuardTests.cs` |

### Closing the four stuck attempts

```bash
cd /opt/oetwebapp

# 1. Preview — read only. Section 4 must list exactly four attempts.
docker exec -i -e PGPASSWORD="$POSTGRES_PASSWORD" oet-postgres \
  psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 \
  -f - < scripts/ops/export-ai-incident-evidence.sql

# 2. Close them. Aborts with zero writes unless the read-only selection finds
#    exactly four qualifying attempts. Safe to re-run (no-op on the second run).
docker exec -i -e PGPASSWORD="$POSTGRES_PASSWORD" oet-postgres \
  psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 \
  -f - < scripts/ops/close-listening-incident-attempts.sql
```

The closure script tags `AiSkipReason = 'skipped_no_evidence'` and
`AiIncidentId = 'INC-2026-CLAUDE-01'`. It never deletes a row, never sets
`AiScoredAt`, and asserts before committing that `IsCorrect`, `PointsEarned`,
`MissReason`, `SelectedDistractorCategory`, `AiScoredAt` and `AiVerdict` are
byte-for-byte unchanged.

Its qualifying predicate is *submitted at or before the incident cutoff* **and**
*un-AI-scored* **and** (`AiSkipReason IS NULL` **or** `AiSkipReason =
'skipped_no_evidence'` with a NULL-or-matching `AiIncidentId`). The
NULL-incident branch matters: once W0 is deployed the repaired scorer closes
these rows itself, without an incident tag, and they must still be counted by
the fail-closed "exactly four" check. Section 4 of the export uses the identical
predicate, so the preview count and the closure scope cannot diverge.

#### Why the cutoff is what keeps "exactly four" stable

Read-only production evidence established four qualifying attempts, all
submitted at or before **`2026-08-26T10:49:58.902864Z`**. Both scripts therefore
default `incident_cutoff` to **`2026-08-26T11:00:00Z`** — the next round instant
after the last qualifying submission — and filter
`ListeningAttempts."SubmittedAt" <= :'incident_cutoff'::timestamptz`.

Without that bound the predicate is open-ended in time, so the qualifying set is
whatever happens to match *when the script runs*. Any new evidence-free attempt
submitted after the incident would join it, which is wrong twice over: the
fail-closed `expected_attempts = 4` assertion would abort for a reason unrelated
to the incident, and if an operator "fixed" that by raising the expected count,
an unrelated candidate's attempt would be permanently stamped with this
incident's id. With the cutoff the population is frozen: it covers all four
attempts and can never grow, so the preview, the first closure run and every
re-run see the identical four rows. Post-incident attempts stay where they
belong — with the repaired live scorer.

A NULL `SubmittedAt` fails the comparison and is excluded. That is fail-closed on
purpose: it lowers the count and aborts rather than tagging an attempt whose
submission time is unknown. Override only with the incident commander's
agreement, e.g. `-v incident_cutoff=2026-08-26T11:00:00Z` before `-f -`.

### Recovering the credential-quarantined work (no work is lost)

The 401s from the old key closed real, evidence-backed advisory work as
`credential_quarantined`. That state is terminal **in code on purpose** — an
automatic retry against a dead credential is the exact loop this incident was
about, and it would burn the attempt cap the moment a key expires. The scorer is
**not** disabled either: the W0 guards make it safe to leave running.

Instead, recovery is a deliberate operator action in this order:

1. **Deploy W0.** The guards go live: evidence-free attempts make zero provider
   calls, and a 401 closes the answer instead of spinning every 20 seconds.
2. **Old-key 401 work is quarantined and preserved.** Nothing was deleted, no
   deterministic mark moved, and `AiScoredAt` was never set — the rows are fully
   recoverable. Confirm 30 minutes of zero `listening.parta.score` invocations.
3. **Activate and canary the replacement credential.** Provision it, activate it
   atomically in the admin console, run one low-token canary per critical route,
   confirm a successful usage row (`Outcome = 0`), then revoke the old key.
4. **Requeue, with explicit confirmation.** Only now does the guarded script
   clear the quarantine, and only for `credential_quarantined` answers that have
   effective approved rationales and were submitted at or before the cutoff.

```bash
cd /opt/oetwebapp

# Read-only preview of the recovery population — no confirmation needed.
# Section 8 lists every terminal skip reason so the untouched ones are visible.
docker exec -i -e PGPASSWORD="$POSTGRES_PASSWORD" oet-postgres \
  psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 \
  -f - < scripts/ops/export-ai-incident-evidence.sql

# Requeue. Aborts with zero writes without the exact confirmation token.
docker exec -i -e PGPASSWORD="$POSTGRES_PASSWORD" oet-postgres \
  psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 \
  -v i_confirm_replacement_credential_is_validated_and_canaried=YES-I-VALIDATED-AND-CANARIED-THE-REPLACEMENT-CREDENTIAL \
  -f - < scripts/ops/requeue-listening-credential-quarantined.sql
```

The script runs in one transaction, prints its scope before writing, resets only
`AiSkipReason` / `AiAttemptCount` / `AiNextAttemptAt` (plus the audit-only
`AiIncidentId`), asserts that `IsCorrect`, `PointsEarned`, `MissReason`,
`SelectedDistractorCategory`, `AiScoredAt`, `AiVerdict`, `AiRationale` and
`AiModel` are unchanged, and leaves `skipped_no_evidence`,
`indeterminate_timeout`, `provider_rejected`, `no_matching_verdicts` and
`retries_exhausted` completely untouched. After recovery a second run finds
nothing quarantined in scope and commits a clean no-op. It also aborts if any
in-scope attempt overlaps the closure population, so the "exactly four"
assertion cannot be perturbed behind the operator's back.

After the default-cutoff recovery completes, run export section 8 again. If a
`credential_quarantined` bucket remains, it is live-service work quarantined
during the guarded credential-rotation window. Re-run the same recovery command
with `-v incident_cutoff=<replacement-credential-canary-completion-UTC>` before
`-f -`. The confirmation token, effective-rationale requirement, deterministic
field assertions, and all excluded skip reasons remain unchanged.

This is how the no-work-loss requirement is met **without** an automatic retry
and **without** a temporary feature shutdown: the work waits, safely and
terminally closed, until a human proves the replacement key works.

### W0 bound, and what it is not

`ListeningPartAAiRetryPolicy.MaxAttempts = 3` caps the attempts **scheduled per
answer**. It removes the unbounded loop, but it is not a global
at-most-three-physical-calls guarantee: the hosted worker still runs in both the
blue and green API slots, so two slots can read the same pre-increment
`AiAttemptCount` and each spend an attempt. Cross-slot exactly-once is delivered
by the W4 coordinator's database leasing (`SKIP LOCKED`), not by W0. Follow-up
actions 7 and 9 below carry that work.

---

## Follow-up actions

| # | Action | Owner | Target |
|---|---|---|---|
| 1 | Deploy W0 through the normal blue/green flow and confirm 30 minutes of **zero** `listening.parta.score` provider invocations on evidence-free attempts before activating the replacement credential | Dr Faisal Maqsood | with W0 release |
| 2 | Provision + atomically activate the replacement Anthropic credential, run one low-token canary per critical route, confirm a successful usage row, then revoke the old key | Dr Faisal Maqsood | after action 1 |
| 3 | Run the fail-closed closure with the stable cutoff (`incident_cutoff` defaults to `2026-08-26T11:00:00Z`; expected count 4) and keep the printed before/after deterministic marks with the incident record | Dr Faisal Maqsood | after action 1 |
| 4 | Run `scripts/ops/requeue-listening-credential-quarantined.sql` with the explicit confirmation token to hand the preserved 401 work back to the repaired scorer. **Only after action 2 is verified.** Re-check export §8 afterwards: the `credential_quarantined` bucket must be empty and every other skip bucket unchanged | Dr Faisal Maqsood | after action 2 |
| 5 | Run the final production export and fill in figures 1 and 2 above (they stay `PENDING EXPORT` until then), then reconcile figure 1 against the Anthropic console's successful-usage view | Dr Faisal Maqsood | after actions 2–4 |
| 6 | Move the scorer behind the canonical provider adapter/coordinator and add the architecture test that rejects direct Anthropic transport outside it | Dr Faisal Maqsood | W1+ |
| 7 | Move cost-bearing hosted workers out of both API slots into a dedicated worker run-mode with database leasing (`SKIP LOCKED`) — this is what turns the W0 per-answer bound into cross-slot exactly-once. The worker's deterministic oldest-due-first ordering is the W0 half of this: it makes the batch slice reproducible and starvation-free, but it does not stop two slots claiming the same row | Dr Faisal Maqsood | W4 |
| 8 | Parse and price `cache_creation_input_tokens` / `cache_read_input_tokens` so admin totals reconcile with the provider console | Dr Faisal Maqsood | W1+ |
| 9 | **Later-rationale re-arm (known W0 gap).** `skipped_no_evidence` is terminal by design, so an attempt closed today is *not* automatically reviewed if an author-approved rationale becomes effective later. This must be re-armed as a **new versioned AI operation** in **W5** (Listening behind the coordinator): a rationale reaching `Effective` enqueues a fresh `AiOperation` whose idempotency key includes the rationale/question revision version, so the replay is a distinct, budgeted, credit-reserved unit of work with its own audit trail. **Do not** hand-clear `AiSkipReason` in the database as a shortcut — it re-arms the exact loop this incident was about (no attempt counter reset semantics, no evidence-version check, no operation record, and it races both API slots). The credential-quarantine recovery script is *not* a precedent for this: it is confirmation-gated, time-bounded by the incident cutoff, and only touches rows that already had effective evidence. Until W5 ships, the supported action is to state the gap to the owner and leave the row closed. | Dr Faisal Maqsood | W5 |
| 10 | Revisit `IsSafePreSendFailure`: today only `NameResolutionError` and `ProxyTunnelError` are retried, because .NET's `HttpRequestError` cannot tell a pre-send connect/TLS failure from a mid-flight one. If a future runtime (or the W1 provider adapter) can prove the request never left the process, `ConnectionError` / `SecureConnectionError` can be moved back to retryable | Dr Faisal Maqsood | W1+ |
| 11 | After credential rotation, confirm export section 8 has no `credential_quarantined` rows. Recover any live-service rows quarantined during the rotation window by rerunning the guarded recovery with `incident_cutoff` set to the recorded replacement-credential canary-completion UTC instant | Dr Faisal Maqsood | after action 4 |
