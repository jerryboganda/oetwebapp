-- scripts/db/preflight_jsonb_conversion.sql
--
-- Run BEFORE applying migration 20260424170000_ConvertHotJsonColumnsToJsonb
-- on any environment that already has data in the 7 target columns.
--
-- The migration casts text -> jsonb with NULLIF(col,'')::jsonb. Any value
-- that is not valid JSON and not empty will cause the ALTER TABLE to fail
-- with "invalid input syntax for type json" and abort the entire migration
-- batch. This script lists offending rows per target column so ops can
-- clean them up (usually: set to NULL or '{}') before deploying.
--
-- Usage (from host):
--   docker exec -i oetwebsite-postgres-1 \
--     psql -U postgres -d oetdb \
--     -f /dev/stdin < scripts/db/preflight_jsonb_conversion.sql
--
-- Or inside the container:
--   psql -U postgres -d oetdb -f /path/to/preflight_jsonb_conversion.sql
--
-- Clean exit = 0 offending rows across every column. Any non-zero count
-- should be investigated and fixed before running migrations.

\echo '--- jsonb pre-flight check ---'
\echo 'Bad = value is neither empty string nor valid JSON.'
\echo 'Expect "OK (0 bad rows)" on every column.'

-- Helper function: returns true if text is either empty or valid JSON.
-- Defined CREATE OR REPLACE so the script is idempotent and safe to re-run.
CREATE OR REPLACE FUNCTION pg_temp.is_empty_or_valid_json(t text)
RETURNS boolean
LANGUAGE plpgsql
AS $$
BEGIN
    IF t IS NULL OR t = '' THEN
        RETURN true;
    END IF;
    PERFORM t::jsonb;
    RETURN true;
EXCEPTION WHEN others THEN
    RETURN false;
END;
$$;

-- One query per target column. Each prints a single row labelled by
-- (table, column, bad_count). If the table doesn't exist yet (fresh DB),
-- the outer DO block suppresses the error and prints "skipped".
DO $$
DECLARE
    v_pair record;
    v_sql text;
    v_count bigint;
BEGIN
    FOR v_pair IN
        SELECT * FROM (VALUES
            ('AnalyticsEvents', 'PayloadJson'),
            ('Attempts', 'AnalysisJson'),
            ('Evaluations', 'StrengthsJson'),
            ('Evaluations', 'IssuesJson'),
            ('Evaluations', 'CriterionScoresJson'),
            ('Evaluations', 'FeedbackItemsJson'),
            ('PaymentWebhookEvents', 'PayloadJson')
        ) AS v(tbl, col)
    LOOP
        IF NOT EXISTS (
            SELECT 1 FROM pg_class
            WHERE relname = v_pair.tbl AND relnamespace = 'public'::regnamespace
        ) THEN
            RAISE NOTICE '[%.%] skipped: table missing', v_pair.tbl, v_pair.col;
            CONTINUE;
        END IF;

        -- Use pg_temp helper; still need dynamic SQL for the column ref.
        v_sql := format(
            'SELECT count(*) FROM public.%I WHERE NOT pg_temp.is_empty_or_valid_json(%I)',
            v_pair.tbl, v_pair.col);
        EXECUTE v_sql INTO v_count;
        IF v_count = 0 THEN
            RAISE NOTICE '[%.%] OK (0 bad rows)', v_pair.tbl, v_pair.col;
        ELSE
            RAISE WARNING '[%.%] BAD ROWS: %', v_pair.tbl, v_pair.col, v_count;
        END IF;
    END LOOP;
END $$;

\echo '--- Pre-flight complete ---'
