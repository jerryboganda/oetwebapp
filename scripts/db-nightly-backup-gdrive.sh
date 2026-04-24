#!/usr/bin/env bash
# Nightly offsite DB backup to Google Drive via rclone.
# Requires: rclone installed + remote named "gdrive" configured.
# Retention policy:
#   - Local: keep last 3 daily dumps (local safety net).
#   - Remote: keep ONLY THE LATEST upload. Previous remote backup is deleted
#     ONLY AFTER the new upload is verified, so we never have zero remote copies.

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
NEW_SIZE=$(stat -c%s "$LOCAL_PATH")
if [ "$NEW_SIZE" -lt 10000 ]; then
  echo "[$(date -u +%FT%TZ)] ERROR: dump is suspiciously small ($NEW_SIZE bytes). Aborting upload."
  exit 2
fi

# Upload to Google Drive
if command -v rclone >/dev/null 2>&1 && rclone listremotes 2>/dev/null | grep -q "^${REMOTE_NAME}:"; then
  echo "[$(date -u +%FT%TZ)] Uploading to ${REMOTE_NAME}:${REMOTE_DIR}/"
  rclone copy "$LOCAL_PATH" "${REMOTE_NAME}:${REMOTE_DIR}/" --stats-one-line

  # Verify new file actually landed before pruning anything
  if ! rclone lsf "${REMOTE_NAME}:${REMOTE_DIR}/" --include "${FILE}" | grep -q "${FILE}"; then
    echo "[$(date -u +%FT%TZ)] ERROR: upload verification failed; leaving prior backups untouched."
    exit 3
  fi
  echo "[$(date -u +%FT%TZ)] Upload verified: ${FILE}"

  # Delete every other oetdb_*.sql.gz on the remote (keep only the one we just uploaded)
  echo "[$(date -u +%FT%TZ)] Pruning previous remote backups (keep latest only)"
  rclone lsf "${REMOTE_NAME}:${REMOTE_DIR}/" --include "oetdb_*.sql.gz" \
    | grep -v "^${FILE}\$" \
    | while read -r OLD; do
        [ -z "$OLD" ] && continue
        echo "  deleting gdrive:${REMOTE_DIR}/${OLD}"
        rclone deletefile "${REMOTE_NAME}:${REMOTE_DIR}/${OLD}" || true
      done

  # Empty remote trash too so Drive quota is actually reclaimed
  rclone cleanup "${REMOTE_NAME}:" 2>/dev/null || true
else
  echo "[$(date -u +%FT%TZ)] WARN: rclone remote '${REMOTE_NAME}' not configured; upload skipped"
fi

# Prune local: keep last 3
ls -1t "$LOCAL_DIR"/oetdb_*.sql.gz 2>/dev/null | tail -n +4 | xargs -r rm -f
echo "[$(date -u +%FT%TZ)] Local retention pruned (last 3), done."
