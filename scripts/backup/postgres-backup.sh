#!/usr/bin/env sh
# -----------------------------------------------------------------------------
# postgres-backup.sh
#
# Nightly encrypted backup for the production Postgres database.
#
# Runtime: this script is designed to be invoked by the oet-db-backup sidecar
# container defined in docker-compose.production.yml. The sidecar runs a cron
# entry that calls this script at LOG_BACKUP_SCHEDULE (default 02:17 daily).
#
# Flow
#   1. pg_dump --format=custom of $POSTGRES_DB  ---> /backups/oet-YYYYMMDD-HHMMSS.dump
#   2. (optional) gpg --symmetric --cipher-algo AES256 using $BACKUP_GPG_PASSPHRASE
#      ---> /backups/*.dump.gpg  and the plaintext .dump is shredded.
#   3. (optional) aws s3 cp to $BACKUP_S3_URL (works against S3, Cloudflare R2,
#      MinIO, Backblaze B2 via the S3-compatible endpoint).
#   4. Prune local retention past $BACKUP_RETENTION_DAYS (default 14).
#
# Fail-fast: the script exits non-zero on any error so cron logs surface the
# failure. Partial dumps are removed. Do NOT soften error handling without a
# dedicated runbook review.
#
# Restore
#   See DEPLOYMENT.md §Disaster Recovery for the verified restore procedure.
# -----------------------------------------------------------------------------

set -eu

# ── Configuration (all via env, no hard-coded secrets) ──────────────────────
: "${POSTGRES_HOST:=postgres}"
: "${POSTGRES_PORT:=5432}"
: "${POSTGRES_DB:?POSTGRES_DB must be set}"
: "${POSTGRES_USER:?POSTGRES_USER must be set}"
: "${PGPASSWORD:?PGPASSWORD must be set (Postgres client reads this automatically)}"

: "${BACKUP_DIR:=/backups}"
: "${BACKUP_RETENTION_DAYS:=14}"
: "${BACKUP_GPG_PASSPHRASE:=}"          # empty = skip encryption (NOT recommended)
: "${BACKUP_S3_URL:=}"                  # e.g. s3://bucket/prefix/ ; empty = local only
: "${BACKUP_S3_EXTRA_ARGS:=}"           # e.g. --endpoint-url https://... for R2
: "${BACKUP_ALERT_WEBHOOK:=}"           # optional POSTed-to-on-failure URL

stamp="$(date -u +%Y%m%dT%H%M%SZ)"
dump_path="${BACKUP_DIR}/oet-${stamp}.dump"
final_path="${dump_path}"

cleanup_partial() {
    # On any failure, delete the in-flight files so cron doesn't inherit half-baked artifacts.
    rm -f "${dump_path}" "${dump_path}.gpg" 2>/dev/null || true
    if [ -n "${BACKUP_ALERT_WEBHOOK}" ]; then
        # Best-effort webhook; do NOT fail the cleanup path on webhook failure.
        curl -fsS -X POST -H 'Content-Type: application/json' \
            -d "{\"stage\":\"$1\",\"stamp\":\"${stamp}\",\"db\":\"${POSTGRES_DB}\"}" \
            "${BACKUP_ALERT_WEBHOOK}" >/dev/null 2>&1 || true
    fi
}

mkdir -p "${BACKUP_DIR}"

# ── 1. pg_dump (custom format preserves roles/large objects/compression) ────
echo "[backup] pg_dump -> ${dump_path}"
if ! pg_dump \
    --host="${POSTGRES_HOST}" \
    --port="${POSTGRES_PORT}" \
    --username="${POSTGRES_USER}" \
    --dbname="${POSTGRES_DB}" \
    --format=custom \
    --compress=9 \
    --no-owner \
    --file="${dump_path}"
then
    cleanup_partial "pg_dump_failed"
    echo "[backup] pg_dump failed" >&2
    exit 1
fi

dump_size="$(stat -c%s "${dump_path}" 2>/dev/null || wc -c < "${dump_path}")"
echo "[backup] dump size: ${dump_size} bytes"

# Reject obviously-truncated dumps. 1 KiB is a deliberately generous floor —
# a real dump of this project's schema alone is far larger.
if [ "${dump_size}" -lt 1024 ]; then
    cleanup_partial "dump_too_small"
    echo "[backup] dump is implausibly small (${dump_size} bytes); aborting" >&2
    exit 1
fi

# ── 2. Optional GPG encryption ──────────────────────────────────────────────
if [ -n "${BACKUP_GPG_PASSPHRASE}" ]; then
    echo "[backup] encrypting with gpg"
    if ! gpg --batch --yes --passphrase "${BACKUP_GPG_PASSPHRASE}" \
            --symmetric --cipher-algo AES256 \
            --output "${dump_path}.gpg" "${dump_path}"
    then
        cleanup_partial "gpg_failed"
        echo "[backup] gpg encryption failed" >&2
        exit 1
    fi
    # Shred plaintext. On tmpfs/overlayfs 'shred' won't actually erase the
    # underlying block; the container image is expected to live on encrypted
    # storage for this to be meaningful. We still rm immediately.
    shred -u "${dump_path}" 2>/dev/null || rm -f "${dump_path}"
    final_path="${dump_path}.gpg"
else
    echo "[backup] WARNING: BACKUP_GPG_PASSPHRASE is empty; dump stored in plaintext"
fi

# ── 3. Optional offsite push (S3-compatible) ────────────────────────────────
if [ -n "${BACKUP_S3_URL}" ]; then
    echo "[backup] uploading ${final_path} -> ${BACKUP_S3_URL}"
    # shellcheck disable=SC2086  # we WANT word-splitting on BACKUP_S3_EXTRA_ARGS
    if ! aws s3 cp "${final_path}" "${BACKUP_S3_URL}" ${BACKUP_S3_EXTRA_ARGS}; then
        cleanup_partial "s3_upload_failed"
        echo "[backup] s3 upload failed" >&2
        exit 1
    fi
fi

# ── 4. Retention prune (local) ──────────────────────────────────────────────
echo "[backup] pruning local backups older than ${BACKUP_RETENTION_DAYS} days"
find "${BACKUP_DIR}" -type f \( -name 'oet-*.dump' -o -name 'oet-*.dump.gpg' \) \
    -mtime "+${BACKUP_RETENTION_DAYS}" -print -delete || true

echo "[backup] ok: ${final_path}"
