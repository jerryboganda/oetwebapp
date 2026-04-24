#!/usr/bin/env bash
# Nightly offsite DB backup to Google Drive via rclone.
# Requires: rclone installed + remote named "gdrive" configured.
# Retention policy:
#   - Local: last 7 daily dumps
#   - Remote: last 30 daily dumps (pruned inside the target Drive folder)

set -euo pipefail

PG_CONTAINER=oet-postgres
PG_USER=oet_learner
PG_DB=oet_learner

LOCAL_DIR=/root/backups/nightly
REMOTE_NAME="${GDRIVE_REMOTE:-gdrive}"
REMOTE_DIR="${GDRIVE_REMOTE_DIR:-oet-db-backups}"
STAMP=$(date -u +%Y%m%d_%H%M%SZ)
FILE="oetdb_${STAMP}.sql.gz"
LOCAL_PATH="$LOCAL_DIR/$FILE"

mkdir -p "$LOCAL_DIR"

echo "[$(date -u +%FT%TZ)] pg_dump -> $LOCAL_PATH"
docker exec "$PG_CONTAINER" pg_dump -U "$PG_USER" -d "$PG_DB" --no-owner --no-privileges \
  | gzip -9 > "$LOCAL_PATH"
ls -lh "$LOCAL_PATH"

# Upload to Google Drive
if command -v rclone >/dev/null 2>&1 && rclone listremotes 2>/dev/null | grep -q "^${REMOTE_NAME}:"; then
  echo "[$(date -u +%FT%TZ)] Uploading to ${REMOTE_NAME}:${REMOTE_DIR}/"
  rclone copy "$LOCAL_PATH" "${REMOTE_NAME}:${REMOTE_DIR}/" --progress --stats-one-line
  echo "[$(date -u +%FT%TZ)] Pruning remote backups older than 30 days"
  rclone delete "${REMOTE_NAME}:${REMOTE_DIR}/" --min-age 30d --include "oetdb_*.sql.gz" || true
else
  echo "[$(date -u +%FT%TZ)] WARN: rclone remote '${REMOTE_NAME}' not configured; upload skipped"
fi

# Prune local: keep last 7
ls -1t "$LOCAL_DIR"/oetdb_*.sql.gz 2>/dev/null | tail -n +8 | xargs -r rm -f
echo "[$(date -u +%FT%TZ)] Local retention pruned, done."
