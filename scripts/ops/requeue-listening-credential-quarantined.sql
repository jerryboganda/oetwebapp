-- ═══════════════════════════════════════════════════════════════════════════
-- requeue-listening-credential-quarantined.sql
--
-- Incident: INC-2026-CLAUDE-01 (Listening Part A advisory scorer retry storm).
-- Purpose:  MANUAL, explicitly confirmed recovery of the advisory work that was
--           quarantined when the OLD Anthropic key started returning 401. Those
--           answers had real, effective, author-approved evidence — they were
--           never given a fair chance — so the work is preserved, not lost, and
--           this script hands it back to the repaired scorer.
--
-- This is the deliberate alternative to an automatic retry. `credential_quarantined`
-- stays terminal in code (an auto-retry against a bad key is exactly the loop
-- that caused this incident, and it would burn the attempt cap the moment a key
-- expires). Recovery is therefore an operator action, gated on proof that the
-- replacement credential is already live and canaried.
--
-- ── Deploy sequence (do NOT run this script out of order) ──────────────────
--   1. Deploy W0 through the normal blue/green flow. The guards go live: an
--      evidence-free attempt makes ZERO provider calls, and a 401 closes the
--      answer as `credential_quarantined` instead of spinning every 20 s.
--   2. Old-key 401 work is now quarantined AND PRESERVED. Nothing is deleted,
--      no deterministic mark moved, and `AiScoredAt` was never set — so the
--      rows are fully recoverable. Confirm 30 minutes of zero provider
--      invocations before touching credentials.
--   3. Provision the replacement Anthropic credential, activate it atomically
--      in the admin console, run ONE low-token canary per critical route, and
--      confirm the canary succeeded (HTTP 2xx, usage row with Outcome = 0).
--      Revoke the old key.
--   4. Only then run this script, with the confirmation variable, from
--      /opt/oetwebapp. It clears the quarantine on in-scope answers and the
--      worker picks them up on its next tick, oldest work first.
--
-- Because of step 3, no automatic retry and no temporary feature shutdown is
-- required to satisfy the no-work-loss requirement: the work waits, safely and
-- terminally closed, until a human proves the replacement key works.
--
-- Hard safety contract
--   * FAIL CLOSED on confirmation. Without the exact confirmation token the
--     transaction raises before a single row is read for update.
--   * SCOPE IS NARROW: only `AiSkipReason = 'credential_quarantined'`, only on
--     SUBMITTED attempts submitted at or before :incident_cutoff, only Part A
--     gap questions, only answers that HAVE an effective author-approved
--     rationale, and only answers that were never AI-scored.
--   * `skipped_no_evidence`, `indeterminate_timeout`, `provider_rejected`,
--     `no_matching_verdicts` and `retries_exhausted` are NEVER touched. Those
--     are different failure modes with different (or no) recovery paths.
--   * Deterministic marking is untouched: "IsCorrect", "PointsEarned",
--     "MissReason", "SelectedDistractorCategory" are never in a SET clause and
--     a before/after assertion aborts the transaction if any of them moved.
--     "AiScoredAt", "AiVerdict", "AiRationale" and "AiModel" are asserted too.
--   * Only the advisory scheduling fields needed to resume are reset:
--     "AiSkipReason" -> NULL, "AiAttemptCount" -> 0, "AiNextAttemptAt" -> NULL.
--     "AiIncidentId" is stamped with the incident id purely as an audit trail —
--     no code reads it.
--   * IDEMPOTENT after recovery: a second run finds zero quarantined rows in
--     scope, updates nothing and commits a clean no-op. (Zero scope is NOT an
--     error — only a missing/incorrect confirmation is.)
--   * No attempt/user id is hard-coded — the set is derived by predicate.
--
-- Preview first, read-only, with no confirmation needed:
--   scripts/ops/export-ai-incident-evidence.sql section 8.
--
-- Run (from /opt/oetwebapp on the VPS, production DB). The script lives on the
-- HOST, so it is piped into the container on stdin with `-f -`, and the
-- confirmation/override variables go BEFORE `-f -` (psql reads the script last):
--   cd /opt/oetwebapp
--   docker exec -i -e PGPASSWORD="$POSTGRES_PASSWORD" oet-postgres \
--     psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 \
--     -v i_confirm_replacement_credential_is_validated_and_canaried=YES-I-VALIDATED-AND-CANARIED-THE-REPLACEMENT-CREDENTIAL \
--     -f - < scripts/ops/requeue-listening-credential-quarantined.sql
--
-- Running it without that variable, or with any other value, aborts with zero
-- writes. That is intentional: it must be impossible to requeue paid work by
-- pasting a command without reading it.
-- ═══════════════════════════════════════════════════════════════════════════

\set ON_ERROR_STOP on
\pset border 2
\pset format aligned

-- Unmistakable operator confirmation. Default is a value that can only fail.
\if :{?i_confirm_replacement_credential_is_validated_and_canaried}
\else
\set i_confirm_replacement_credential_is_validated_and_canaried 'NOT-CONFIRMED'
\endif

-- Same stable inclusive cutoff as the export and closure scripts. Production
-- evidence put the last incident-era submission at 2026-08-26T10:49:58.902864Z,
-- so 2026-08-26T11:00:00Z bounds the incident population and cannot grow. Work
-- submitted later is outside the incident population by default. If the old
-- credential quarantines live-service work during the guarded rotation window,
-- recover it only by explicitly widening this cutoff to the recorded replacement
-- credential activation/canary-completion instant; every other safety guard below
-- remains in force.
\if :{?incident_cutoff}
\else
\set incident_cutoff '2026-08-26T11:00:00Z'
\endif

\set incident_id 'INC-2026-CLAUDE-01'
\set skip_reason 'credential_quarantined'
\set required_confirmation 'YES-I-VALIDATED-AND-CANARIED-THE-REPLACEMENT-CREDENTIAL'
\set closure_skip_reason 'skipped_no_evidence'

-- Enum ordinals (EF stores these as int):
--   ListeningAttemptStatus.Submitted     = 1
--   ListeningQuestionType.ShortAnswer    = 0
--   ListeningQuestionType.FillInBlank    = 2
--   AssessmentGovernanceStatus.Effective = 3

BEGIN;

CREATE TEMP TABLE recovery_params ON COMMIT DROP AS
SELECT
    :'incident_id'::text            AS incident_id,
    :'skip_reason'::text            AS skip_reason,
    :'closure_skip_reason'::text    AS closure_skip_reason,
    :'incident_cutoff'::timestamptz AS incident_cutoff,
    :'required_confirmation'::text  AS required_confirmation,
    :'i_confirm_replacement_credential_is_validated_and_canaried'::text AS supplied_confirmation;

-- ── 0. Fail closed on a missing or wrong confirmation ──────────────────────
-- Runs before anything is selected for update. No confirmation, no reads, no
-- writes, no commit.
DO $$
DECLARE
    supplied text;
    required text;
BEGIN
    SELECT supplied_confirmation, required_confirmation
      INTO supplied, required
      FROM recovery_params;

    IF supplied IS DISTINCT FROM required THEN
        RAISE EXCEPTION
            'Requeue aborted: the replacement credential has not been confirmed as validated and canaried. Re-run with -v i_confirm_replacement_credential_is_validated_and_canaried=%. No rows changed.',
            required;
    END IF;
END $$;

-- ── 1. Scope: quarantined answers that DO have effective approved evidence ──
CREATE TEMP TABLE requeue_answers ON COMMIT DROP AS
SELECT DISTINCT
    a."Id"                  AS answer_id,
    a."ListeningAttemptId"  AS attempt_id
FROM "ListeningAnswers" a
JOIN "ListeningQuestions" q  ON q."Id"  = a."ListeningQuestionId"
JOIN "ListeningAttempts"  at ON at."Id" = a."ListeningAttemptId"
JOIN "AssessmentRationales" r
       ON r."Assessment"         = 'listening'
      AND r."QuestionRevisionId" = q."Id"
      AND r."Status"             = 3
WHERE at."Status" = 1
  AND at."SubmittedAt" <= (SELECT incident_cutoff FROM recovery_params)
  AND q."QuestionType" IN (0, 2)
  AND a."AiScoredAt" IS NULL
  AND a."AiSkipReason" = (SELECT skip_reason FROM recovery_params);

\echo '-- Recovery scope (credential_quarantined answers with effective evidence):'
SELECT
    ra.attempt_id,
    count(*)                                    AS answers_to_requeue,
    max(a."AiAttemptCount")                     AS attempts_already_spent,
    count(*) FILTER (WHERE a."AiScoredAt" IS NOT NULL) AS falsely_ai_scored,
    sum(a."PointsEarned")                       AS deterministic_points,
    count(*) FILTER (WHERE a."IsCorrect")       AS deterministic_correct
FROM requeue_answers ra
JOIN "ListeningAnswers" a ON a."Id" = ra.answer_id
GROUP BY ra.attempt_id
ORDER BY ra.attempt_id;

\echo '-- Out of scope and deliberately untouched (must be unchanged after this run):'
SELECT
    a."AiSkipReason"                            AS skip_reason,
    count(*)                                    AS answers
FROM "ListeningAnswers" a
WHERE a."AiSkipReason" IS NOT NULL
  AND a."AiSkipReason" <> (SELECT skip_reason FROM recovery_params)
GROUP BY 1
ORDER BY 1;

-- ── 2. Fail closed if recovery would disturb the closure population ────────
-- close-listening-incident-attempts.sql asserts EXACTLY four qualifying
-- attempts, and its qualifying predicate reads the answers that are open or
-- closed `skipped_no_evidence`. If an attempt held BOTH a quarantined answer
-- (with evidence) and a `skipped_no_evidence` answer, clearing the quarantine
-- would change that attempt's evidence profile and the closure script's
-- fail-closed count would move on its next run. In production the four closed
-- attempts have no effective evidence at all, so this must never fire; if it
-- does, the two populations genuinely overlap and the incident commander must
-- decide the order of operations before anything is written.
DO $$
DECLARE
    overlapping int;
BEGIN
    SELECT count(DISTINCT ra.attempt_id) INTO overlapping
    FROM requeue_answers ra
    JOIN "ListeningAnswers" a ON a."ListeningAttemptId" = ra.attempt_id
    WHERE a."AiSkipReason" = (SELECT closure_skip_reason FROM recovery_params);

    IF overlapping > 0 THEN
        RAISE EXCEPTION
            'Requeue aborted: % attempt(s) in scope also hold skipped_no_evidence answers counted by close-listening-incident-attempts.sql. Escalate to the incident commander. No rows changed.',
            overlapping;
    END IF;
END $$;

-- ── 3. Snapshot every field this script must NOT move ──────────────────────
CREATE TEMP TABLE requeue_score_before ON COMMIT DROP AS
SELECT
    a."Id"                          AS answer_id,
    a."IsCorrect"                   AS is_correct,
    a."PointsEarned"                AS points_earned,
    a."MissReason"                  AS miss_reason,
    a."SelectedDistractorCategory"  AS distractor_category,
    a."AiScoredAt"                  AS ai_scored_at,
    a."AiVerdict"                   AS ai_verdict,
    a."AiRationale"                 AS ai_rationale,
    a."AiModel"                     AS ai_model
FROM "ListeningAnswers" a
JOIN requeue_answers ra ON ra.answer_id = a."Id";

-- ── 4. Clear the quarantine (advisory scheduling fields only) ──────────────
-- Exactly the three fields the worker eligibility query reads, plus the audit
-- tag. Nothing else is in the SET list, so the deterministic mark and any
-- existing advisory output cannot be rewritten by construction.
UPDATE "ListeningAnswers" a
SET "AiSkipReason"    = NULL,
    "AiAttemptCount"  = 0,
    "AiNextAttemptAt" = NULL,
    "AiIncidentId"    = (SELECT incident_id FROM recovery_params)
FROM requeue_answers ra
WHERE a."Id" = ra.answer_id
  AND (
        a."AiSkipReason"    IS NOT NULL
     OR a."AiAttemptCount"  <> 0
     OR a."AiNextAttemptAt" IS NOT NULL
     OR a."AiIncidentId"    IS DISTINCT FROM (SELECT incident_id FROM recovery_params)
      );

-- ── 5. Prove nothing outside the advisory scheduling fields moved ──────────
DO $$
DECLARE
    drifted int;
BEGIN
    SELECT count(*) INTO drifted
    FROM requeue_score_before b
    JOIN "ListeningAnswers" a ON a."Id" = b.answer_id
    WHERE a."IsCorrect"                  IS DISTINCT FROM b.is_correct
       OR a."PointsEarned"               IS DISTINCT FROM b.points_earned
       OR a."MissReason"                 IS DISTINCT FROM b.miss_reason
       OR a."SelectedDistractorCategory" IS DISTINCT FROM b.distractor_category
       OR a."AiScoredAt"                 IS DISTINCT FROM b.ai_scored_at
       OR a."AiVerdict"                  IS DISTINCT FROM b.ai_verdict
       OR a."AiRationale"                IS DISTINCT FROM b.ai_rationale
       OR a."AiModel"                    IS DISTINCT FROM b.ai_model;

    IF drifted > 0 THEN
        RAISE EXCEPTION
            'Requeue aborted: % answer row(s) had a deterministic/advisory-output field changed. Rolling back.',
            drifted;
    END IF;
END $$;

-- ── 6. Verification output ─────────────────────────────────────────────────
\echo '-- Post-requeue state (skip_reason must be NULL, attempts 0, no ai_scored_at):'
SELECT
    a."ListeningAttemptId"                              AS attempt_id,
    count(*)                                            AS requeued_answers,
    count(*) FILTER (WHERE a."AiSkipReason" IS NULL)    AS cleared,
    max(a."AiAttemptCount")                             AS attempts_after,
    min(a."AiIncidentId")                               AS incident_tag,
    count(*) FILTER (WHERE a."AiScoredAt" IS NOT NULL)  AS falsely_ai_scored,
    sum(a."PointsEarned")                               AS deterministic_points,
    count(*) FILTER (WHERE a."IsCorrect")               AS deterministic_correct
FROM "ListeningAnswers" a
JOIN requeue_answers ra ON ra.answer_id = a."Id"
GROUP BY a."ListeningAttemptId"
ORDER BY a."ListeningAttemptId";

COMMIT;

\echo '-- Requeue committed. The worker picks these up on its next tick (oldest work first).'
\echo '-- Re-running this file after recovery is a clean no-op: nothing is left quarantined in scope.'
