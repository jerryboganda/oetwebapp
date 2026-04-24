#!/usr/bin/env bash
# Run on VPS: full deploy with pre-migration pg_dump backup.
set -euo pipefail

cd /root/oetwebsite

STAMP=$(date +%Y%m%d_%H%M%S)
BACKUP_DIR=/root/backups
mkdir -p "$BACKUP_DIR"
BACKUP_FILE="$BACKUP_DIR/pre_partition_${STAMP}.sql.gz"

echo "==> 1/5 Backing up postgres to $BACKUP_FILE"
docker exec oet-postgres pg_dump -U oet_learner -d oet_learner --no-owner --no-privileges | gzip > "$BACKUP_FILE"
ls -lh "$BACKUP_FILE"

echo "==> 2/5 JSON pre-flight: detecting invalid JSON in target jsonb columns"
docker exec -i oet-postgres psql -U oet_learner -d oet_learner -v ON_ERROR_STOP=1 <<'SQL'
DO $$
DECLARE
  rec record;
  bad bigint;
  queries text[] := ARRAY[
    $q$SELECT count(*) FROM "AnalyticsEvents" WHERE "PayloadJson" IS NOT NULL AND "PayloadJson" <> '' AND NOT ("PayloadJson"::text ~ '^\s*[{\[]')$q$,
    $q$SELECT count(*) FROM "Attempts" WHERE "AnalysisJson" IS NOT NULL AND "AnalysisJson" <> '' AND NOT ("AnalysisJson"::text ~ '^\s*[{\[]')$q$,
    $q$SELECT count(*) FROM "Evaluations" WHERE "StrengthsJson" IS NOT NULL AND "StrengthsJson" <> '' AND NOT ("StrengthsJson"::text ~ '^\s*[{\[]')$q$,
    $q$SELECT count(*) FROM "Evaluations" WHERE "IssuesJson" IS NOT NULL AND "IssuesJson" <> '' AND NOT ("IssuesJson"::text ~ '^\s*[{\[]')$q$,
    $q$SELECT count(*) FROM "Evaluations" WHERE "CriterionScoresJson" IS NOT NULL AND "CriterionScoresJson" <> '' AND NOT ("CriterionScoresJson"::text ~ '^\s*[{\[]')$q$,
    $q$SELECT count(*) FROM "Evaluations" WHERE "FeedbackItemsJson" IS NOT NULL AND "FeedbackItemsJson" <> '' AND NOT ("FeedbackItemsJson"::text ~ '^\s*[{\[]')$q$,
    $q$SELECT count(*) FROM "PaymentWebhookEvents" WHERE "PayloadJson" IS NOT NULL AND "PayloadJson" <> '' AND NOT ("PayloadJson"::text ~ '^\s*[{\[]')$q$
  ];
  q text;
BEGIN
  FOREACH q IN ARRAY queries LOOP
    EXECUTE q INTO bad;
    IF bad > 0 THEN
      RAISE NOTICE 'Potentially non-JSON rows: % -> %', bad, q;
    END IF;
  END LOOP;
END $$;
SQL

echo "==> 3/5 Pull latest from origin/main"
git fetch origin
git reset --hard origin/main
git log -1 --oneline

echo "==> 4/5 docker compose up -d --build"
docker compose --env-file .env.production -f docker-compose.production.yml up -d --build

echo "==> 5/5 Wait 15s then show api health + recent logs"
sleep 15
docker compose --env-file .env.production -f docker-compose.production.yml ps
echo "---- last 80 api log lines ----"
docker compose --env-file .env.production -f docker-compose.production.yml logs --tail=80 learner-api || true
echo
echo "Deploy script finished. Backup at: $BACKUP_FILE"
