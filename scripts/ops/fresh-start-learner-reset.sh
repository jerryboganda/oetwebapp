#!/usr/bin/env bash
set -euo pipefail

APP_DIR="${APP_DIR:-/opt/oetwebapp}"
ENV_FILE="${ENV_FILE:-$APP_DIR/.env.production}"
PG_CONTAINER="${PG_CONTAINER:-oet-postgres}"
BACKUP_DIR="${BACKUP_DIR:-$APP_DIR/backups/manual}"
EXECUTE="${EXECUTE:-false}"

read_env_value() {
  local key="$1"
  awk -F= -v k="$key" '$1 == k { sub(/^[^=]*=/, ""); gsub(/^\"|\"$/, ""); print; exit }' "$ENV_FILE"
}

POSTGRES_USER="${POSTGRES_USER:-$(read_env_value POSTGRES_USER)}"
POSTGRES_DB="${POSTGRES_DB:-$(read_env_value POSTGRES_DB)}"
POSTGRES_PASSWORD="${POSTGRES_PASSWORD:-$(read_env_value POSTGRES_PASSWORD)}"

if [[ -z "$POSTGRES_USER" || -z "$POSTGRES_DB" || -z "$POSTGRES_PASSWORD" ]]; then
  echo "Missing POSTGRES_USER/POSTGRES_DB/POSTGRES_PASSWORD from $ENV_FILE" >&2
  exit 1
fi

mkdir -p "$BACKUP_DIR"

echo "[fresh-reset] mode: $([[ "$EXECUTE" == "true" ]] && echo EXECUTE || echo DRY-RUN)"
echo "[fresh-reset] database: $POSTGRES_DB container: $PG_CONTAINER"

if [[ "$EXECUTE" == "true" ]]; then
  ts="$(date -u +%Y%m%d-%H%M%S)"
  backup_path="$BACKUP_DIR/fresh-start-before-learner-reset-$ts.dump"
  echo "[fresh-reset] creating backup: $backup_path"
  docker exec -e PGPASSWORD="$POSTGRES_PASSWORD" "$PG_CONTAINER" \
    pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" --format=custom --no-owner --no-privileges \
    > "$backup_path"
  test -s "$backup_path"
  echo "[fresh-reset] backup complete ($(du -h "$backup_path" | awk '{print $1}'))"
else
  echo "[fresh-reset] dry-run: no backup and no data mutation will be performed"
fi

if [[ "$EXECUTE" == "true" ]]; then
  docker exec -i -e PGPASSWORD="$POSTGRES_PASSWORD" "$PG_CONTAINER" \
    psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 <<'SQL'
BEGIN;

CREATE TEMP TABLE _learner_ids ON COMMIT DROP AS
SELECT "Id"::text AS id, "AuthAccountId"::text AS auth_id, "Email"::text AS email
FROM "Users"
WHERE lower("Role"::text) = 'learner';

DO $$
DECLARE
  n integer;
BEGIN
  SELECT count(*) INTO n FROM _learner_ids;
  RAISE NOTICE 'Learners queued for deletion: %', n;
END $$;

CREATE TEMP TABLE _learner_delete_columns ON COMMIT DROP AS
SELECT table_schema, table_name, column_name
FROM information_schema.columns
WHERE table_schema = current_schema()
  AND table_name <> 'Users'
  AND data_type IN ('text', 'character varying', 'character', 'uuid')
  AND (
    column_name = 'UserId'
    OR column_name LIKE '%UserId'
    OR column_name IN (
      'LearnerId', 'LearnerUserId', 'LinkedLearnerId', 'RecipientUserId',
      'AssignedToUserId', 'SubmitterUserId', 'ReviewerUserId', 'AuthorUserId',
      'CreatorUserId', 'CreatedByUserId', 'UpdatedByUserId', 'UploadedByUserId',
      'ReportedByUserId', 'ActorUserId', 'ReferrerUserId', 'ReferredUserId',
      'FromUserId', 'OwnerUserId', 'SponsorUserId', 'RequestedBy', 'ProposedByUserId',
      'DecidedByUserId'
    )
  );

DO $$
DECLARE
  pass integer;
  skipped integer;
  deleted_rows integer;
  total_deleted integer := 0;
  r record;
BEGIN
  FOR pass IN 1..12 LOOP
    skipped := 0;
    FOR r IN SELECT * FROM _learner_delete_columns ORDER BY table_name DESC, column_name LOOP
      BEGIN
        EXECUTE format(
          'DELETE FROM %I.%I WHERE %I::text IN (SELECT id FROM _learner_ids)',
          r.table_schema, r.table_name, r.column_name
        );
        GET DIAGNOSTICS deleted_rows = ROW_COUNT;
        total_deleted := total_deleted + deleted_rows;
        IF deleted_rows > 0 THEN
          RAISE NOTICE 'pass %, deleted % from %.% via %', pass, deleted_rows, r.table_schema, r.table_name, r.column_name;
        END IF;
      EXCEPTION WHEN foreign_key_violation THEN
        skipped := skipped + 1;
      END;
    END LOOP;
    EXIT WHEN skipped = 0;
  END LOOP;
  RAISE NOTICE 'total direct learner-owned rows deleted: %', total_deleted;
END $$;

DELETE FROM "BillingMetricDailies";
DELETE FROM "SubscriptionItems";
DELETE FROM "BillingNotificationDispatchLogs" WHERE "UserId"::text IN (SELECT id FROM _learner_ids);
DELETE FROM "Users" WHERE "Id"::text IN (SELECT id FROM _learner_ids);

-- Remove the learner LOGIN identities ("ApplicationUserAccounts") and their
-- auth-keyed dependents. The dynamic loop above keys on "Users"."Id"; the login
-- account is keyed by "AuthAccountId", so without this step the accounts are left
-- orphaned: the learner cannot sign in ("account suspended") and the email stays
-- "already registered", blocking re-invite/registration. FK children are discovered
-- live from pg_constraint (robust to schema changes); "ExpertUsers"/"Users" are
-- excluded so only learner auth rows are affected.
DO $$
DECLARE
  pass integer;
  skipped integer;
  deleted_rows integer;
  total_deleted integer := 0;
  r record;
BEGIN
  FOR pass IN 1..12 LOOP
    skipped := 0;
    FOR r IN
      SELECT con.conrelid::regclass::text AS tbl, att.attname AS col
      FROM pg_constraint con
      JOIN pg_attribute att ON att.attrelid = con.conrelid AND att.attnum = con.conkey[1]
      WHERE con.contype = 'f'
        AND con.confrelid = '"ApplicationUserAccounts"'::regclass
        AND con.conrelid <> '"ExpertUsers"'::regclass
        AND con.conrelid <> '"Users"'::regclass
    LOOP
      BEGIN
        EXECUTE format(
          'DELETE FROM %s WHERE %I::text IN (SELECT auth_id FROM _learner_ids)',
          r.tbl, r.col
        );
        GET DIAGNOSTICS deleted_rows = ROW_COUNT;
        total_deleted := total_deleted + deleted_rows;
        IF deleted_rows > 0 THEN
          RAISE NOTICE 'auth-pass %, deleted % from % via %', pass, deleted_rows, r.tbl, r.col;
        END IF;
      EXCEPTION WHEN foreign_key_violation THEN
        skipped := skipped + 1;
      END;
    END LOOP;
    EXIT WHEN skipped = 0;
  END LOOP;
  RAISE NOTICE 'total learner auth-owned rows deleted: %', total_deleted;
END $$;

DELETE FROM "ApplicationUserAccounts" WHERE "Id"::text IN (SELECT auth_id FROM _learner_ids);

UPDATE "BillingPlans" p
SET "ActiveSubscribers" = COALESCE(s.real_count, 0), "UpdatedAt" = now()
FROM (
  SELECT p2."Id", count(s."Id")::int AS real_count
  FROM "BillingPlans" p2
  LEFT JOIN "Subscriptions" s ON s."PlanId" = p2."Id" AND s."Status"::text IN ('1','2','active','Active')
  GROUP BY p2."Id"
) s
WHERE p."Id" = s."Id";

COMMIT;

SELECT 'learners_remaining' AS metric, count(1)::text AS value FROM "Users" WHERE lower("Role"::text)='learner'
UNION ALL SELECT 'learner_login_accounts_remaining', count(1)::text FROM "ApplicationUserAccounts" WHERE lower("Role"::text)='learner'
UNION ALL SELECT 'subscriptions_remaining', count(1)::text FROM "Subscriptions"
UNION ALL SELECT 'subscription_items_remaining', count(1)::text FROM "SubscriptionItems"
UNION ALL SELECT 'billing_quotes_remaining', count(1)::text FROM "BillingQuotes"
UNION ALL SELECT 'payment_transactions_remaining', count(1)::text FROM "PaymentTransactions"
UNION ALL SELECT 'invoices_remaining', count(1)::text FROM "Invoices"
UNION ALL SELECT 'billing_events_remaining', count(1)::text FROM "BillingEvents"
UNION ALL SELECT 'billing_metric_dailies_remaining', count(1)::text FROM "BillingMetricDailies";

SELECT "Code", "ActiveSubscribers" FROM "BillingPlans" ORDER BY "Code";
SQL
else
  docker exec -i -e PGPASSWORD="$POSTGRES_PASSWORD" "$PG_CONTAINER" \
    psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 <<'SQL'
CREATE TEMP TABLE _learner_ids AS
SELECT "Id"::text AS id, "AuthAccountId"::text AS auth_id, "Email"::text AS email
FROM "Users"
WHERE lower("Role"::text) = 'learner';

SELECT 'learner' AS kind, id, auth_id, email FROM _learner_ids ORDER BY email;

SELECT 'learners' AS metric, count(1)::text AS value FROM _learner_ids
UNION ALL SELECT 'learner_login_accounts', count(1)::text FROM "ApplicationUserAccounts" WHERE lower("Role"::text)='learner'
UNION ALL SELECT 'subscriptions_total', count(1)::text FROM "Subscriptions"
UNION ALL SELECT 'basic_monthly_real_subscriptions', count(1)::text FROM "Subscriptions" WHERE "PlanId" = (SELECT "Id" FROM "BillingPlans" WHERE "Code"='basic-monthly')
UNION ALL SELECT 'billing_quotes_total', count(1)::text FROM "BillingQuotes"
UNION ALL SELECT 'payment_transactions_total', count(1)::text FROM "PaymentTransactions"
UNION ALL SELECT 'invoices_total', count(1)::text FROM "Invoices"
UNION ALL SELECT 'subscription_items_total', count(1)::text FROM "SubscriptionItems"
UNION ALL SELECT 'billing_events_total', count(1)::text FROM "BillingEvents"
UNION ALL SELECT 'billing_metric_dailies_total', count(1)::text FROM "BillingMetricDailies";

SELECT "Code", "Name", "ActiveSubscribers" AS stored_active_subscribers
FROM "BillingPlans"
ORDER BY "ActiveSubscribers" DESC, "Code";

WITH cols AS (
  SELECT table_schema, table_name, column_name
  FROM information_schema.columns
  WHERE table_schema = current_schema()
    AND table_name <> 'Users'
    AND data_type IN ('text', 'character varying', 'character', 'uuid')
    AND (
      column_name = 'UserId'
      OR column_name LIKE '%UserId'
      OR column_name IN (
        'LearnerId', 'LearnerUserId', 'LinkedLearnerId', 'RecipientUserId',
        'AssignedToUserId', 'SubmitterUserId', 'ReviewerUserId', 'AuthorUserId',
        'CreatorUserId', 'CreatedByUserId', 'UpdatedByUserId', 'UploadedByUserId',
        'ReportedByUserId', 'ActorUserId', 'ReferrerUserId', 'ReferredUserId',
        'FromUserId', 'OwnerUserId', 'SponsorUserId', 'RequestedBy', 'ProposedByUserId',
        'DecidedByUserId'
      )
    )
), statements AS (
  SELECT format(
    'SELECT %L AS table_name, %L AS column_name, count(1)::text AS rows FROM %I.%I WHERE %I::text IN (SELECT id FROM _learner_ids)',
    table_name, column_name, table_schema, table_name, column_name
  ) AS sql
  FROM cols
), unioned AS (
  SELECT string_agg(sql, ' UNION ALL ') AS sql FROM statements
)
SELECT sql FROM unioned \gexec
SQL
fi
