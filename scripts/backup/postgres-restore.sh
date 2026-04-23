#!/usr/bin/env sh
# -----------------------------------------------------------------------------
# postgres-restore.sh
#
# Restore a pg_dump custom-format backup produced by postgres-backup.sh.
#
# Usage (inside the oet-db-backup sidecar container, or any container with
# pg_restore + gpg + aws CLI on PATH):
#
#   BACKUP_FILE=/backups/oet-20260423T021700Z.dump.gpg ./postgres-restore.sh
#
# This script DOES NOT touch live production blindly. It requires you to:
#   1. Confirm CONFIRM_RESTORE=YES in the environment.
#   2. Provide TARGET_DB (the destination DB name). If it differs from
#      $POSTGRES_DB, the target database is created and the dump is restored
#      into that instead of the live database \u2014 this is the supported path.
#      Overwriting the live DB is gated by TARGET_DB == POSTGRES_DB AND
#      explicitly setting RESTORE_INTO_LIVE=YES.
#
# Read DEPLOYMENT.md \u00a7Disaster Recovery before running this in production.
# -----------------------------------------------------------------------------

set -eu

: "${BACKUP_FILE:?BACKUP_FILE must be set (path to .dump or .dump.gpg)}"
: "${POSTGRES_HOST:=postgres}"
: "${POSTGRES_PORT:=5432}"
: "${POSTGRES_USER:?POSTGRES_USER must be set}"
: "${PGPASSWORD:?PGPASSWORD must be set}"
: "${POSTGRES_DB:?POSTGRES_DB must be set}"
: "${TARGET_DB:=${POSTGRES_DB}_restore_$(date -u +%Y%m%d%H%M%S)}"
: "${CONFIRM_RESTORE:=NO}"
: "${RESTORE_INTO_LIVE:=NO}"
: "${BACKUP_GPG_PASSPHRASE:=}"

if [ "${CONFIRM_RESTORE}" != "YES" ]; then
    echo "Set CONFIRM_RESTORE=YES to proceed." >&2
    exit 2
fi

if [ "${TARGET_DB}" = "${POSTGRES_DB}" ] && [ "${RESTORE_INTO_LIVE}" != "YES" ]; then
    echo "Refusing to restore into the live database (${POSTGRES_DB})." >&2
    echo "Either set TARGET_DB=<other> or RESTORE_INTO_LIVE=YES (dangerous)." >&2
    exit 3
fi

workdir="$(mktemp -d)"
trap 'rm -rf "${workdir}"' EXIT

# ── Decrypt if needed ───────────────────────────────────────────────────────
dump_path="${BACKUP_FILE}"
case "${BACKUP_FILE}" in
    *.gpg)
        if [ -z "${BACKUP_GPG_PASSPHRASE}" ]; then
            echo "BACKUP_GPG_PASSPHRASE is required to decrypt ${BACKUP_FILE}" >&2
            exit 4
        fi
        dump_path="${workdir}/restore.dump"
        echo "[restore] decrypting ${BACKUP_FILE}"
        gpg --batch --yes --passphrase "${BACKUP_GPG_PASSPHRASE}" \
            --output "${dump_path}" --decrypt "${BACKUP_FILE}"
        ;;
esac

# ── Create target DB if it doesn't exist ────────────────────────────────────
echo "[restore] ensuring target database ${TARGET_DB} exists"
psql --host="${POSTGRES_HOST}" --port="${POSTGRES_PORT}" --username="${POSTGRES_USER}" \
     --dbname=postgres --quiet --tuples-only --no-align \
     -c "SELECT 1 FROM pg_database WHERE datname='${TARGET_DB}'" | grep -q 1 \
    || psql --host="${POSTGRES_HOST}" --port="${POSTGRES_PORT}" --username="${POSTGRES_USER}" \
         --dbname=postgres -c "CREATE DATABASE \"${TARGET_DB}\""

# ── Restore ─────────────────────────────────────────────────────────────────
echo "[restore] pg_restore -> ${TARGET_DB}"
pg_restore \
    --host="${POSTGRES_HOST}" \
    --port="${POSTGRES_PORT}" \
    --username="${POSTGRES_USER}" \
    --dbname="${TARGET_DB}" \
    --clean \
    --if-exists \
    --no-owner \
    --verbose \
    "${dump_path}"

echo "[restore] ok: restored into ${TARGET_DB}"
echo "If this is a validation restore, drop the database with:"
echo "  dropdb -h ${POSTGRES_HOST} -U ${POSTGRES_USER} ${TARGET_DB}"
