# syntax=docker/dockerfile:1
# Backup sidecar image. Carries:
#   * postgresql-client matching the server major version
#   * gpg for symmetric encryption
#   * aws CLI v2 for S3-compatible offsite push
#   * dcron to trigger backups on a schedule
#
# The image is small and intentionally does only one thing. The actual backup
# logic lives in scripts/backup/postgres-backup.sh so it stays reviewable in
# the repo rather than baked into the image.
FROM alpine:3.22

ARG PG_MAJOR=17

RUN apk add --no-cache \
        "postgresql${PG_MAJOR}-client" \
        gnupg \
        aws-cli \
        dcron \
        curl \
        tzdata \
        shadow \
    && adduser -D -H backup \
    && mkdir -p /backups /etc/periodic/custom \
    && chown -R backup:backup /backups

COPY scripts/backup/postgres-backup.sh  /usr/local/bin/postgres-backup.sh
COPY scripts/backup/postgres-restore.sh /usr/local/bin/postgres-restore.sh
COPY scripts/backup/entrypoint.sh       /usr/local/bin/entrypoint.sh

RUN chmod +x /usr/local/bin/postgres-backup.sh \
             /usr/local/bin/postgres-restore.sh \
             /usr/local/bin/entrypoint.sh

# The cron daemon needs to run as root, but pg_dump inherits BACKUP_USER from
# env in the backup script's own process via a su- step. Alpine's dcron reads
# /etc/crontabs/<user>.
ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
