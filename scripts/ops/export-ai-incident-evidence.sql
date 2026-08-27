-- ═══════════════════════════════════════════════════════════════════════════
-- export-ai-incident-evidence.sql
--
-- Incident: INC-2026-CLAUDE-01 (Listening Part A advisory scorer retry storm).
-- Purpose:  READ-ONLY evidence export for the incident report — window,
--           affected attempts, AI usage rows, route configuration,
--           model/pricing, and the credential FINGERPRINT only.
--
-- Safety contract
--   * Every statement is a SELECT. Nothing is inserted, updated or deleted.
--   * The encrypted API key is NEVER selected. Only "ApiKeyHint" (the stored
--     last-4 style fingerprint) is exported.
--   * No attempt/user identifier is hard-coded: the affected set is derived
--     from the data by predicate, so the script is safe to re-run any time.
--   * "AiUsageRecords" is queried as an ordinary table (production relkind = 'r'
--     as of this incident). If it is ever partitioned or replaced by a view,
--     re-verify sections 1-3 and 7 before quoting their numbers.
--
-- Run (from /opt/oetwebapp on the VPS, production DB). The script is piped in on
-- stdin with `-f -` because it lives on the HOST, not inside the container:
--   cd /opt/oetwebapp
--   docker exec -i -e PGPASSWORD="$POSTGRES_PASSWORD" oet-postgres \
--     psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 \
--     -f - < scripts/ops/export-ai-incident-evidence.sql
--
-- Overrides go BEFORE `-f -` (psql reads the script last), e.g.:
--   docker exec -i -e PGPASSWORD="$POSTGRES_PASSWORD" oet-postgres \
--     psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 \
--     -v lookback_days=120 -v incident_cutoff=2026-08-26T11:00:00Z \
--     -f - < scripts/ops/export-ai-incident-evidence.sql
-- ═══════════════════════════════════════════════════════════════════════════

\pset border 2
\pset format aligned

\if :{?lookback_days}
\else
\set lookback_days 60
\endif

-- Stable inclusive incident cutoff. Production evidence (read-only) established
-- that every qualifying attempt was submitted at or before
-- 2026-08-26T10:49:58.902864Z; 11:00:00Z is the next round instant after that,
-- so the qualifying set is FROZEN at exactly four attempts no matter when this
-- export or the closure script is run. Without it the predicate is open-ended
-- and any NEW evidence-free attempt submitted after the incident would silently
-- join the set, break the fail-closed "exactly four" assertion, and (worse) be
-- tagged with an incident id it has nothing to do with.
\if :{?incident_cutoff}
\else
\set incident_cutoff '2026-08-26T11:00:00Z'
\endif

\set incident_id 'INC-2026-CLAUDE-01'
\set feature_code 'listening.parta.score'
\set skip_reason 'skipped_no_evidence'

-- Enum ordinals (EF stores these as int):
--   ListeningAttemptStatus.Submitted            = 1
--   ListeningQuestionType.ShortAnswer           = 0
--   ListeningQuestionType.FillInBlank           = 2
--   AssessmentGovernanceStatus.Effective        = 3
--   AiCallOutcome.Success                       = 0

\echo '== 0. Export header =================================================='
SELECT
    :'incident_id'                                              AS incident_id,
    :'feature_code'                                             AS feature_code,
    (now() AT TIME ZONE 'UTC')                                  AS exported_at_utc,
    (:lookback_days || ' days')::interval                       AS lookback,
    (now() - (:lookback_days || ' days')::interval)             AS window_start_utc,
    now()                                                       AS window_end_utc,
    :'incident_cutoff'::timestamptz                             AS incident_cutoff_utc;

\echo '== 1. Observed provider-call window for the feature =================='
SELECT
    min("CreatedAt")                                            AS first_call_utc,
    max("CreatedAt")                                            AS last_call_utc,
    count(*)                                                    AS total_usage_rows,
    count(*) FILTER (WHERE "Outcome" = 0)                       AS successful_rows,
    count(*) FILTER (WHERE "Outcome" <> 0)                      AS failed_rows
FROM "AiUsageRecords"
WHERE "FeatureCode" = :'feature_code'
  AND "CreatedAt" >= now() - (:lookback_days || ' days')::interval;

\echo '== 2. Usage rows grouped by day / outcome / error class =============='
-- Successful requests and failed requests are reported SEPARATELY on purpose:
-- Anthropic does not bill failed (e.g. 401) requests, so they must never be
-- folded into the spend estimate.
SELECT
    date_trunc('day', "CreatedAt")                              AS day_utc,
    "Outcome"                                                   AS outcome_enum,
    COALESCE("ErrorCode", '-')                                  AS error_class,
    count(*)                                                    AS calls,
    sum("PromptTokens")                                         AS prompt_tokens,
    sum("CompletionTokens")                                     AS completion_tokens,
    round(sum("CostEstimateUsd"), 4)                            AS estimated_cost_usd
FROM "AiUsageRecords"
WHERE "FeatureCode" = :'feature_code'
  AND "CreatedAt" >= now() - (:lookback_days || ' days')::interval
GROUP BY 1, 2, 3
ORDER BY 1, 2, 3;

\echo '== 3. Platform-wide AI spend in the same window (all features) ======='
SELECT
    "FeatureCode",
    COALESCE("ProviderId", '-')                                 AS provider_id,
    count(*)                                                    AS calls,
    count(*) FILTER (WHERE "Outcome" = 0)                       AS successful_calls,
    count(*) FILTER (WHERE "Outcome" <> 0)                      AS failed_calls,
    round(sum("CostEstimateUsd") FILTER (WHERE "Outcome" = 0), 4)
                                                                AS estimated_billable_usd
FROM "AiUsageRecords"
WHERE "CreatedAt" >= now() - (:lookback_days || ' days')::interval
GROUP BY 1, 2
ORDER BY estimated_billable_usd DESC NULLS LAST, calls DESC;

\echo '== 4. Stuck Part A attempts (no effective approved rationale) ========'
-- The qualifying set for the closure script: SAME predicate as
-- close-listening-incident-attempts.sql section 1 (submitted at or before the
-- incident cutoff, un-AI-scored, still open or already closed as
-- `skipped_no_evidence` with a NULL or matching incident tag, and zero effective
-- approved rationales). Keep the two in lockstep — this is the preview that must
-- report exactly the expected attempt count before the fail-closed closure
-- script is run. Deterministic marks are shown so the report can prove they are
-- unchanged before and after tagging.
--
-- Why the cutoff makes "exactly four" stable: production evidence puts the last
-- qualifying submission at 2026-08-26T10:49:58.902864Z, so an inclusive
-- `SubmittedAt <= 2026-08-26T11:00:00Z` bound covers all four and can never grow.
-- Attempts submitted after the incident are a live-service concern for the
-- repaired scorer, not incident scope, so they must not drift into this set.
-- Note that a NULL "SubmittedAt" fails the comparison and is therefore excluded:
-- that is fail-closed on purpose — it would drop the count below four and abort
-- the closure rather than tag an attempt whose submission time is unknown.
WITH parta AS (
    SELECT
        a."Id"                      AS answer_id,
        a."ListeningAttemptId"      AS attempt_id,
        a."ListeningQuestionId"     AS question_id,
        a."IsCorrect"               AS is_correct,
        a."PointsEarned"            AS points_earned,
        a."AiScoredAt"              AS ai_scored_at,
        a."AiSkipReason"            AS ai_skip_reason,
        a."AiAttemptCount"          AS ai_attempt_count,
        a."AiIncidentId"            AS ai_incident_id,
        (r."Id" IS NOT NULL)        AS has_effective_rationale
    FROM "ListeningAnswers" a
    JOIN "ListeningQuestions" q  ON q."Id"  = a."ListeningQuestionId"
    JOIN "ListeningAttempts"  at ON at."Id" = a."ListeningAttemptId"
    LEFT JOIN "AssessmentRationales" r
           ON r."Assessment"         = 'listening'
          AND r."QuestionRevisionId" = q."Id"
          AND r."Status"             = 3
    WHERE at."Status" = 1
      AND at."SubmittedAt" <= :'incident_cutoff'::timestamptz
      AND q."QuestionType" IN (0, 2)
      AND a."AiScoredAt" IS NULL
      AND (
            a."AiSkipReason" IS NULL
            OR (a."AiSkipReason" = :'skip_reason'
                AND (a."AiIncidentId" IS NULL
                     OR a."AiIncidentId" = :'incident_id'))
          )
)
SELECT
    attempt_id,
    count(*)                                                    AS parta_answers,
    count(*) FILTER (WHERE has_effective_rationale)              AS answers_with_evidence,
    count(*) FILTER (WHERE ai_skip_reason IS NOT NULL)           AS already_closed,
    max(ai_attempt_count)                                        AS max_attempts_spent,
    min(ai_incident_id)                                          AS incident_tag,
    sum(points_earned)                                           AS deterministic_points,
    count(*) FILTER (WHERE is_correct)                           AS deterministic_correct
FROM parta
GROUP BY attempt_id
HAVING count(*) FILTER (WHERE has_effective_rationale) = 0
ORDER BY attempt_id;

\echo '== 5. Feature route configuration ===================================='
SELECT
    "FeatureCode",
    "ProviderCode",
    COALESCE("Model", '(provider default)')                     AS model_override,
    "IsActive",
    "UpdatedAt",
    COALESCE("UpdatedByAdminId", '-')                           AS updated_by
FROM "AiFeatureRoutes"
WHERE "FeatureCode" LIKE 'listening.%'
ORDER BY "FeatureCode";

\echo '== 6. Provider model / pricing / credential FINGERPRINT =============='
-- "EncryptedApiKey" is intentionally absent. "ApiKeyHint" is the non-reversible
-- fingerprint already surfaced in the admin console.
SELECT
    "Code"                                                      AS provider_code,
    "Name"                                                      AS provider_name,
    "BaseUrl"                                                   AS base_url,
    "DefaultModel"                                              AS default_model,
    "PricePer1kPromptTokens"                                    AS price_per_1k_prompt_usd,
    "PricePer1kCompletionTokens"                                AS price_per_1k_completion_usd,
    "IsActive"                                                  AS is_active,
    "ApiKeyHint"                                                AS credential_fingerprint,
    "LastTestedAt"                                              AS last_tested_at,
    COALESCE("LastTestStatus", '-')                             AS last_test_status
FROM "AiProviders"
WHERE "Code" = 'anthropic'
ORDER BY "Code";

\echo '== 7. Distinct models actually charged for the feature ==============='
SELECT
    COALESCE("Model", '(none)')                                 AS model,
    count(*)                                                    AS calls,
    min("CreatedAt")                                            AS first_seen_utc,
    max("CreatedAt")                                            AS last_seen_utc
FROM "AiUsageRecords"
WHERE "FeatureCode" = :'feature_code'
  AND "CreatedAt" >= now() - (:lookback_days || ' days')::interval
GROUP BY 1
ORDER BY calls DESC;

\echo '== 8. Credential-quarantined work preserved for manual recovery ======='
-- Read-only PREVIEW of the population that
-- scripts/ops/requeue-listening-credential-quarantined.sql may requeue: Part A
-- answers closed `credential_quarantined` (the old key returned 401) on attempts
-- submitted at or before the incident cutoff, that DO have an effective
-- author-approved rationale and were therefore never given a fair chance.
--
-- Nothing here is requeued automatically. The recovery script only runs after
-- the replacement credential has been activated and canaried, and only when the
-- operator passes the explicit confirmation variable. Answers closed
-- `skipped_no_evidence`, `indeterminate_timeout`, `provider_rejected`,
-- `no_matching_verdicts` or `retries_exhausted` are OUT of scope and are shown
-- here only so the report proves they are untouched.
SELECT
    a."AiSkipReason"                                            AS skip_reason,
    count(*)                                                    AS answers,
    count(DISTINCT a."ListeningAttemptId")                      AS attempts,
    count(*) FILTER (WHERE r."Id" IS NOT NULL)                  AS answers_with_evidence,
    count(*) FILTER (WHERE a."AiScoredAt" IS NOT NULL)          AS falsely_ai_scored,
    sum(a."PointsEarned")                                       AS deterministic_points,
    count(*) FILTER (WHERE a."IsCorrect")                       AS deterministic_correct
FROM "ListeningAnswers" a
JOIN "ListeningQuestions" q  ON q."Id"  = a."ListeningQuestionId"
JOIN "ListeningAttempts"  at ON at."Id" = a."ListeningAttemptId"
LEFT JOIN "AssessmentRationales" r
       ON r."Assessment"         = 'listening'
      AND r."QuestionRevisionId" = q."Id"
      AND r."Status"             = 3
WHERE at."Status" = 1
  AND at."SubmittedAt" <= :'incident_cutoff'::timestamptz
  AND q."QuestionType" IN (0, 2)
  AND a."AiSkipReason" IS NOT NULL
GROUP BY 1
ORDER BY 1;
