#!/usr/bin/env sh
# -----------------------------------------------------------------------------
# postgres-backup.sh
#
# Nightly encrypted backup for the production Postgres database, plus a
# one-shot pre-change snapshot mode for the Owner Agent Console.
#
# Runtime: this script is designed to be invoked by the oet-db-backup sidecar
# container defined in docker-compose.production.yml. The sidecar runs a cron
# entry that calls this script at BACKUP_SCHEDULE (default 02:17 daily).
#
# Nightly flow (no arguments)
#   1. pg_dump --format=custom of $POSTGRES_DB  ---> /backups/oet-YYYYMMDD-HHMMSS.dump
#   2. tar.gz of learner media storage ---> /backups/oet-media-YYYYMMDD-HHMMSS.tar.gz
#   3. (optional) gpg --symmetric --cipher-algo AES256 using $BACKUP_GPG_PASSPHRASE
#      ---> /backups/*.gpg and the plaintext artifact is shredded.
#   4. (optional) aws s3 cp to $BACKUP_S3_URL (works against S3, Cloudflare R2,
#      MinIO, Backblaze B2 via the S3-compatible endpoint).
#   5. Prune local retention past $BACKUP_RETENTION_DAYS (default 14) for oet-*
#      artifacts, and past $AGENT_SNAPSHOT_RETENTION_DAYS (default 30) for
#      agent-snap-* artifacts. The two retention policies never overlap.
#
# Snapshot flow (Owner Agent Console Guard, before destructive DB operations)
#   docker exec oet-db-backup /usr/local/bin/postgres-backup.sh \
#       --snapshot <label> [--table <name>]...
#   1. free-disk check: available space in $BACKUP_DIR must be >= 2x the most
#      recent agent-snap-* artifact (for a full snapshot also >= 2x the latest
#      nightly dump) and >= $AGENT_SNAPSHOT_MIN_FREE_BYTES; otherwise exit 3.
#   2. pg_dump -Fc (table-scoped with --strict-names when --table is given)
#      ---> /backups/agent-snap-<label>-<UTC stamp>.dump, verified with pg_restore --list.
#   3. same gpg encryption + S3 upload as the nightly flow when configured
#      ($AGENT_SNAPSHOT_S3_URL overrides the destination, default $BACKUP_S3_URL).
#   4. prune agent-snap-* past $AGENT_SNAPSHOT_RETENTION_DAYS (default 30).
#   All progress goes to stderr; the ONLY stdout line is the final artifact path
#   (…/agent-snap-<label>-<stamp>.dump or ….dump.gpg), so callers can take the
#   last stdout line.
#
# Fail-fast: the script exits non-zero on any error so cron logs surface the
# failure. Partial dumps are removed. Do NOT soften error handling without a
# dedicated runbook review.
#
# Restore
#   See DEPLOYMENT.md Disaster Recovery for the verified restore procedure.
# -----------------------------------------------------------------------------

set -eu

# ── Mode selection ───────────────────────────────────────────────────────────
mode="nightly"
snapshot_label=""
snapshot_tables=""

usage() {
    echo "usage: postgres-backup.sh                                   # nightly backup" >&2
    echo "       postgres-backup.sh --snapshot <label> [--table <name>]...  # one-shot snapshot" >&2
    exit 2
}

add_table() {
    # Plain or schema-qualified identifiers, optionally double-quoted
    # (e.g. public."AuditEvents"). No whitespace, globs or shell metacharacters.
    if ! printf '%s' "$1" | grep -Eq '^[A-Za-z0-9_."]{1,200}$'; then
        echo "[backup] invalid --table value: only letters, digits, _ . and \" are allowed" >&2
        exit 2
    fi
    snapshot_tables="${snapshot_tables} $1"
}

while [ "$#" -gt 0 ]; do
    case "$1" in
        --snapshot)
            [ "$#" -ge 2 ] || usage
            mode="snapshot"
            snapshot_label="$2"
            shift 2
            ;;
        --snapshot=*)
            mode="snapshot"
            snapshot_label="${1#--snapshot=}"
            shift
            ;;
        --table)
            [ "$#" -ge 2 ] || usage
            add_table "$2"
            shift 2
            ;;
        --table=*)
            add_table "${1#--table=}"
            shift
            ;;
        -h|--help)
            usage
            ;;
        *)
            echo "[backup] unknown argument: $1" >&2
            usage
            ;;
    esac
done

if [ "$mode" = "snapshot" ]; then
    if ! printf '%s' "$snapshot_label" | grep -Eq '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$'; then
        echo "[backup] --snapshot label must match [A-Za-z0-9][A-Za-z0-9._-]{0,63}" >&2
        exit 2
    fi
    # From here on every progress message goes to stderr; fd 3 keeps the
    # original stdout for the single final "path" line.
    exec 3>&1 1>&2
elif [ -n "$snapshot_tables" ]; then
    echo "[backup] --table is only valid together with --snapshot" >&2
    exit 2
fi

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
: "${MEDIA_BACKUP_ENABLED:=true}"
: "${MEDIA_BACKUP_SOURCE_DIR:=/media-storage}"
: "${AGENT_SNAPSHOT_RETENTION_DAYS:=30}"
: "${AGENT_SNAPSHOT_MIN_FREE_BYTES:=268435456}"   # 256 MiB floor for the free-disk check
: "${AGENT_SNAPSHOT_S3_URL:=${BACKUP_S3_URL}}"

stamp="$(date -u +%Y%m%dT%H%M%SZ)"
if [ "$mode" = "snapshot" ]; then
    dump_path="${BACKUP_DIR}/agent-snap-${snapshot_label}-${stamp}.dump"
    media_archive_path=""
    alert_prefix="snapshot_"
else
    dump_path="${BACKUP_DIR}/oet-${stamp}.dump"
    media_archive_path="${BACKUP_DIR}/oet-media-${stamp}.tar.gz"
    alert_prefix=""
fi
final_path="${dump_path}"
media_final_path="${media_archive_path}"

cleanup_partial() {
    # On any failure, delete the in-flight files so cron doesn't inherit half-baked artifacts.
    rm -f "${dump_path}" "${dump_path}.gpg" 2>/dev/null || true
    if [ -n "${media_archive_path}" ]; then
        rm -f "${media_archive_path}" "${media_archive_path}.gpg" 2>/dev/null || true
    fi
    if [ -n "${BACKUP_ALERT_WEBHOOK}" ]; then
        # Best-effort webhook; do NOT fail the cleanup path on webhook failure.
        curl -fsS -X POST -H 'Content-Type: application/json' \
            -d "{\"stage\":\"${alert_prefix}$1\",\"stamp\":\"${stamp}\",\"db\":\"${POSTGRES_DB}\"}" \
            "${BACKUP_ALERT_WEBHOOK}" >/dev/null 2>&1 || true
    fi
}

mkdir -p "${BACKUP_DIR}"

encrypt_artifact() {
    artifact_path="$1"
    if [ -n "${BACKUP_GPG_PASSPHRASE}" ]; then
        echo "[backup] encrypting ${artifact_path} with gpg" >&2
        if ! printf '%s' "${BACKUP_GPG_PASSPHRASE}" | gpg --batch --yes --pinentry-mode loopback --passphrase-fd 0 \
                --symmetric --cipher-algo AES256 \
                --output "${artifact_path}.gpg" "${artifact_path}"
        then
            cleanup_partial "gpg_failed"
            echo "[backup] gpg encryption failed for ${artifact_path}" >&2
            exit 1
        fi
        shred -u "${artifact_path}" 2>/dev/null || rm -f "${artifact_path}"
        printf '%s.gpg' "${artifact_path}"
    else
        echo "[backup] WARNING: BACKUP_GPG_PASSPHRASE is empty; ${artifact_path} stored in plaintext" >&2
        printf '%s' "${artifact_path}"
    fi
}

upload_artifact() {
    artifact_path="$1"
    if [ -n "${BACKUP_S3_URL}" ]; then
        : "${AWS_ACCESS_KEY_ID:?AWS_ACCESS_KEY_ID must be set when BACKUP_S3_URL is set}"
        : "${AWS_SECRET_ACCESS_KEY:?AWS_SECRET_ACCESS_KEY must be set when BACKUP_S3_URL is set}"
        echo "[backup] uploading ${artifact_path} -> ${BACKUP_S3_URL}"
        # shellcheck disable=SC2086  # we WANT word-splitting on BACKUP_S3_EXTRA_ARGS
        if ! aws s3 cp "${artifact_path}" "${BACKUP_S3_URL}" ${BACKUP_S3_EXTRA_ARGS}; then
            cleanup_partial "s3_upload_failed"
            echo "[backup] s3 upload failed for ${artifact_path}" >&2
            exit 1
        fi
    fi
}

file_size() {
    stat -c%s "$1" 2>/dev/null || wc -c < "$1"
}

newest_matching() {
    # Newest file among the given glob patterns (unmatched patterns are ignored).
    # shellcheck disable=SC2012  # names are generated by this script
    ls -1t "$@" 2>/dev/null | head -n 1 || true
}

prune_agent_snapshots() {
    echo "[backup] pruning agent snapshots older than ${AGENT_SNAPSHOT_RETENTION_DAYS} days"
    find "${BACKUP_DIR}" -maxdepth 1 -type f \( -name 'agent-snap-*.dump' -o -name 'agent-snap-*.dump.gpg' \) \
        -mtime "+${AGENT_SNAPSHOT_RETENTION_DAYS}" -print -delete || true
}

# ── Snapshot mode (Owner Agent Console pre-change snapshot) ─────────────────
if [ "$mode" = "snapshot" ]; then
    if [ -e "${dump_path}" ] || [ -e "${dump_path}.gpg" ]; then
        echo "[backup] snapshot ${dump_path} already exists; retry with another label" >&2
        exit 1
    fi

    # 1. Free-disk check: >= 2x the reference size (and >= the floor).
    reference_path="$(newest_matching "${BACKUP_DIR}"/agent-snap-*.dump "${BACKUP_DIR}"/agent-snap-*.dump.gpg)"
    reference_size=0
    if [ -n "${reference_path}" ]; then
        reference_size="$(file_size "${reference_path}")"
    fi
    if [ -z "${snapshot_tables}" ]; then
        nightly_path="$(newest_matching "${BACKUP_DIR}"/oet-2*.dump "${BACKUP_DIR}"/oet-2*.dump.gpg)"
        if [ -n "${nightly_path}" ]; then
            nightly_size="$(file_size "${nightly_path}")"
            if [ "${nightly_size}" -gt "${reference_size}" ]; then
                reference_size="${nightly_size}"
            fi
        fi
    fi
    required_bytes=$(( reference_size * 2 ))
    if [ "${required_bytes}" -lt "${AGENT_SNAPSHOT_MIN_FREE_BYTES}" ]; then
        required_bytes="${AGENT_SNAPSHOT_MIN_FREE_BYTES}"
    fi
    available_kb="$(df -Pk "${BACKUP_DIR}" | awk 'NR == 2 { print $4 }')"
    available_bytes=$(( ${available_kb:-0} * 1024 ))
    if [ "${available_bytes}" -lt "${required_bytes}" ]; then
        cleanup_partial "insufficient_disk"
        echo "[backup] refusing snapshot: ${available_bytes} bytes free in ${BACKUP_DIR}, need ${required_bytes}" >&2
        exit 3
    fi

    # 2. pg_dump (custom format), table-scoped when --table was given.
    set -- \
        --host="${POSTGRES_HOST}" \
        --port="${POSTGRES_PORT}" \
        --username="${POSTGRES_USER}" \
        --dbname="${POSTGRES_DB}" \
        --format=custom \
        --compress=9 \
        --no-owner \
        --file="${dump_path}"
    if [ -n "${snapshot_tables}" ]; then
        set -- "$@" --strict-names
        for table in ${snapshot_tables}; do
            set -- "$@" "--table=${table}"
        done
        echo "[backup] snapshot pg_dump (tables:${snapshot_tables}) -> ${dump_path}"
    else
        echo "[backup] snapshot pg_dump (full database) -> ${dump_path}"
    fi
    if ! pg_dump "$@"; then
        cleanup_partial "pg_dump_failed"
        echo "[backup] snapshot pg_dump failed" >&2
        exit 1
    fi
    if ! pg_restore --list "${dump_path}" >/dev/null; then
        cleanup_partial "archive_unreadable"
        echo "[backup] snapshot archive failed pg_restore --list verification" >&2
        exit 1
    fi
    echo "[backup] snapshot size: $(file_size "${dump_path}") bytes"

    # 3. Same encryption + offsite path as the nightly backup.
    final_path="$(encrypt_artifact "${dump_path}")"
    BACKUP_S3_URL="${AGENT_SNAPSHOT_S3_URL}"
    upload_artifact "${final_path}"

    # 4. Snapshot-only retention.
    prune_agent_snapshots

    echo "[backup] snapshot ok: ${final_path}"
    printf '%s\n' "${final_path}" >&3
    exit 0
fi

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

dump_size="$(file_size "${dump_path}")"
echo "[backup] dump size: ${dump_size} bytes"

# Reject obviously-truncated dumps. 1 KiB is a deliberately generous floor —
# a real dump of this project's schema alone is far larger.
if [ "${dump_size}" -lt 1024 ]; then
    cleanup_partial "dump_too_small"
    echo "[backup] dump is implausibly small (${dump_size} bytes); aborting" >&2
    exit 1
fi

# 2. GPG encryption and offsite push for DB dump.
final_path="$(encrypt_artifact "${dump_path}")"
upload_artifact "${final_path}"

# 3. Learner media archive, encryption, and offsite push.
if [ "${MEDIA_BACKUP_ENABLED}" = "true" ]; then
    if [ ! -d "${MEDIA_BACKUP_SOURCE_DIR}" ]; then
        cleanup_partial "media_source_missing"
        echo "[backup] media source directory missing: ${MEDIA_BACKUP_SOURCE_DIR}" >&2
        exit 1
    fi
    echo "[backup] learner media archive -> ${media_archive_path}"
    if ! tar -C "${MEDIA_BACKUP_SOURCE_DIR}" -czf "${media_archive_path}" .; then
        cleanup_partial "media_tar_failed"
        echo "[backup] learner media archive failed" >&2
        exit 1
    fi
    media_size="$(file_size "${media_archive_path}")"
    echo "[backup] learner media archive size: ${media_size} bytes"
    media_final_path="$(encrypt_artifact "${media_archive_path}")"
    upload_artifact "${media_final_path}"
fi

# 4. Retention prune (local). oet-* only: agent-snap-* has its own policy below.
echo "[backup] pruning local backups older than ${BACKUP_RETENTION_DAYS} days"
find "${BACKUP_DIR}" -type f \( -name 'oet-*.dump' -o -name 'oet-*.dump.gpg' -o -name 'oet-media-*.tar.gz' -o -name 'oet-media-*.tar.gz.gpg' \) \
    -mtime "+${BACKUP_RETENTION_DAYS}" -print -delete || true
prune_agent_snapshots

echo "[backup] ok: ${final_path}"
if [ "${MEDIA_BACKUP_ENABLED}" = "true" ]; then
    echo "[backup] media ok: ${media_final_path}"
fi
