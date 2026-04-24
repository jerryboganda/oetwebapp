#!/usr/bin/env bash
# Weekly read-only DB health audit.
# Safe: executes only SELECT queries via scripts/db-audit.sql.
# Writes timestamped report; retains last 26 reports (~6 months).

set -euo pipefail

REPO_DIR=/root/oetwebsite
OUT_DIR=/root/backups/db-audits
AUDIT_SQL="$REPO_DIR/scripts/db-audit.sql"
RETENTION_DIR="$REPO_DIR/scripts/db-retention-audit.sql"   # optional; see below
PG_CONTAINER=oet-postgres
PG_USER=oet_learner
PG_DB=oet_learner

STAMP=$(date -u +%Y%m%d_%H%M%SZ)
REPORT="$OUT_DIR/audit_${STAMP}.txt"

mkdir -p "$OUT_DIR"

if [ ! -f "$AUDIT_SQL" ]; then
  echo "Audit SQL not found: $AUDIT_SQL" >&2
  exit 1
fi

{
  echo "=== DB weekly audit ${STAMP} ==="
  echo "host: $(hostname)"
  echo "git:  $(cd "$REPO_DIR" && git rev-parse --short HEAD 2>/dev/null || echo n/a)"
  echo
  docker cp "$AUDIT_SQL" "$PG_CONTAINER":/tmp/audit.sql
  docker exec "$PG_CONTAINER" psql -U "$PG_USER" -d "$PG_DB" -f /tmp/audit.sql 2>&1 || true

  # Retention-sweeper validation: counts and oldest row per append-only table.
  echo
  echo "=== 11. Retention sweeper validation ==="
  docker exec -i "$PG_CONTAINER" psql -U "$PG_USER" -d "$PG_DB" -v ON_ERROR_STOP=0 <<'SQL' 2>&1 || true
SELECT 'AnalyticsEvents'               AS tbl, count(*) AS rows, min("OccurredAt")   AS oldest FROM "AnalyticsEvents"
UNION ALL SELECT 'AuditEvents',                count(*), min("OccurredAt")   FROM "AuditEvents"
UNION ALL SELECT 'PaymentWebhookEvents',       count(*), min("ReceivedAt")   FROM "PaymentWebhookEvents"
UNION ALL SELECT 'NotificationDeliveryAttempts', count(*), min("AttemptedAt") FROM "NotificationDeliveryAttempts";
SQL
} > "$REPORT" 2>&1

# Retain last 26 reports (~6 months)
ls -1t "$OUT_DIR"/audit_*.txt 2>/dev/null | tail -n +27 | xargs -r rm -f

echo "Audit written to $REPORT"
