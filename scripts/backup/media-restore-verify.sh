#!/usr/bin/env sh
# Verify a learner media backup by decrypting if needed and extracting to a
# non-live directory. Never point TARGET_MEDIA_RESTORE_DIR at production storage.
set -eu

: "${MEDIA_BACKUP_FILE:?MEDIA_BACKUP_FILE must be set (path to .tar.gz or .tar.gz.gpg)}"
: "${BACKUP_GPG_PASSPHRASE:=}"
: "${TARGET_MEDIA_RESTORE_DIR:=}"

workdir="$(mktemp -d)"
trap 'rm -rf "${workdir}"' EXIT

archive_path="${MEDIA_BACKUP_FILE}"
case "${MEDIA_BACKUP_FILE}" in
    *.gpg)
        if [ -z "${BACKUP_GPG_PASSPHRASE}" ]; then
            echo "BACKUP_GPG_PASSPHRASE is required to decrypt ${MEDIA_BACKUP_FILE}" >&2
            exit 4
        fi
        archive_path="${workdir}/media-restore.tar.gz"
        echo "[media-restore] decrypting ${MEDIA_BACKUP_FILE}"
        gpg --batch --yes --pinentry-mode loopback --passphrase "${BACKUP_GPG_PASSPHRASE}" \
            --output "${archive_path}" --decrypt "${MEDIA_BACKUP_FILE}"
        ;;
esac

restore_dir="${TARGET_MEDIA_RESTORE_DIR:-${workdir}/media-restore}"
case "${restore_dir}" in
    /var/opt/oet-with-dr-hesham/storage|/var/opt/oet-with-dr-hesham/storage/*|/media-storage|/media-storage/*)
        echo "Refusing to verify media restore into live media storage: ${restore_dir}" >&2
        exit 3
        ;;
esac

mkdir -p "${restore_dir}"
echo "[media-restore] extracting ${archive_path} -> ${restore_dir}"
tar -xzf "${archive_path}" -C "${restore_dir}"

file_count="$(find "${restore_dir}" -type f | wc -l | tr -d ' ')"
echo "[media-restore] ok: extracted ${file_count} files into non-live restore directory"
