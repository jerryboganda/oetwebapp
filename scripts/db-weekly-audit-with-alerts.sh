#!/usr/bin/env bash
# Alerting wrapper around db-weekly-audit.sh.
# - Runs the audit.
# - On non-zero exit OR if report contains "ERROR:" lines, sends alerts:
#     * Sentry event (if SENTRY_DSN is set)
#     * Email via Brevo transactional API (if BREVO_API_KEY is set)
#
# Env inputs (from ${VPS_APP_DIR:-/opt/oetwebapp}/.env.production plus /root/.audit-alerts.env):
#   SENTRY_DSN            e.g. https://<key>@oXXX.ingest.sentry.io/<projectId>
#   BREVO_API_KEY         Brevo v3 API key (same as Brevo__ApiKey)
#   ALERT_EMAIL_TO        recipient address
#   ALERT_EMAIL_FROM      verified sender (default: alerts@oetwithdrhesham.co.uk)

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ENV_FILE_APP="${VPS_APP_DIR:-/opt/oetwebapp}/.env.production"
ENV_FILE_ALERT=/root/.audit-alerts.env

# Load env
[ -f "$ENV_FILE_ALERT" ] && set -a && . "$ENV_FILE_ALERT" && set +a || true

# Also pull Sentry DSN + Brevo key from app env if not already set
if [ -f "$ENV_FILE_APP" ]; then
  SENTRY_DSN="${SENTRY_DSN:-$(grep -E '^SENTRY__DSN=' "$ENV_FILE_APP" | cut -d= -f2- | tr -d '"')}"
  BREVO_API_KEY="${BREVO_API_KEY:-$(grep -E '^BREVO__APIKEY=' "$ENV_FILE_APP" | cut -d= -f2- | tr -d '"')}"
fi

: "${ALERT_EMAIL_TO:?Set ALERT_EMAIL_TO in /root/.audit-alerts.env}"
ALERT_EMAIL_FROM="${ALERT_EMAIL_FROM:-alerts@oetwithdrhesham.co.uk}"
ALERT_EMAIL_FROM_NAME="${ALERT_EMAIL_FROM_NAME:-OET DB Monitor}"

# Run audit, capture exit code
"$SCRIPT_DIR/db-weekly-audit.sh"
AUDIT_RC=$?

# Locate most recent report
LATEST_REPORT=$(ls -1t /root/backups/db-audits/audit_*.txt 2>/dev/null | head -1)

# Detect errors inside report (psql errors surface as "ERROR:" lines)
ERROR_COUNT=0
if [ -n "${LATEST_REPORT:-}" ] && [ -f "$LATEST_REPORT" ]; then
  ERROR_COUNT=$(grep -cE '^(psql:|ERROR:|FATAL:)' "$LATEST_REPORT" || true)
fi

# No alert needed if exit==0 AND no errors in report
if [ "$AUDIT_RC" -eq 0 ] && [ "$ERROR_COUNT" -eq 0 ]; then
  echo "[audit-alert] OK ($LATEST_REPORT)"
  exit 0
fi

SUMMARY="DB weekly audit FAILED (rc=$AUDIT_RC, errors_in_report=$ERROR_COUNT)"
TAIL=$(tail -50 "$LATEST_REPORT" 2>/dev/null || echo "(no report)")

echo "[audit-alert] $SUMMARY"

# --- Sentry ---
if [ -n "${SENTRY_DSN:-}" ]; then
  # Parse DSN: https://<key>@<host>/<projectId>
  KEY=$(echo "$SENTRY_DSN" | sed -E 's#https://([^@]+)@.*#\1#')
  HOST=$(echo "$SENTRY_DSN" | sed -E 's#https://[^@]+@([^/]+)/.*#\1#')
  PROJECT=$(echo "$SENTRY_DSN" | sed -E 's#.*/([0-9]+)$#\1#')
  if [ -n "$KEY" ] && [ -n "$HOST" ] && [ -n "$PROJECT" ]; then
    PAYLOAD=$(jq -Rn --arg msg "$SUMMARY" --arg tail "$TAIL" '{
      message: $msg,
      level: "error",
      logger: "db-weekly-audit",
      platform: "other",
      server_name: "oet-vps",
      tags: { component: "db-audit", kind: "weekly" },
      extra: { report_tail: $tail }
    }' 2>/dev/null) || PAYLOAD="{\"message\":\"$SUMMARY\",\"level\":\"error\",\"platform\":\"other\"}"
    curl -sS -m 10 -o /dev/null \
      -H "Content-Type: application/json" \
      -H "X-Sentry-Auth: Sentry sentry_version=7, sentry_key=$KEY, sentry_client=db-audit/1.0" \
      -d "$PAYLOAD" \
      "https://$HOST/api/$PROJECT/store/" && echo "[audit-alert] Sentry sent" || echo "[audit-alert] Sentry failed"
  fi
fi

# --- Brevo email ---
if [ -n "${BREVO_API_KEY:-}" ] && [ -n "$ALERT_EMAIL_TO" ]; then
  SUBJECT="[OET DB] $SUMMARY"
  BODY="<h3>${SUMMARY}</h3><p>Host: $(hostname)</p><pre style='font:12px monospace;background:#f4f4f4;padding:12px;white-space:pre-wrap'>${TAIL//</&lt;}</pre>"
  BODY_JSON=$(jq -Rn --arg to "$ALERT_EMAIL_TO" --arg from "$ALERT_EMAIL_FROM" --arg fname "$ALERT_EMAIL_FROM_NAME" --arg subj "$SUBJECT" --arg html "$BODY" '{
    sender: { email: $from, name: $fname },
    to: [ { email: $to } ],
    subject: $subj,
    htmlContent: $html
  }')
  curl -sS -m 15 -o /dev/null -w "[audit-alert] Brevo http=%{http_code}\n" \
    -H "api-key: $BREVO_API_KEY" \
    -H "Content-Type: application/json" \
    -d "$BODY_JSON" \
    https://api.brevo.com/v3/smtp/email
fi

exit "$AUDIT_RC"
