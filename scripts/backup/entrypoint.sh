#!/usr/bin/env sh
# Entrypoint for the oet-db-backup sidecar. Installs the crontab from env and
# hands control to crond in the foreground (so the container lifecycle matches
# the daemon's lifecycle, and docker logs shows every backup run).

set -eu

# Default: 02:17 UTC daily. Override with BACKUP_SCHEDULE="*/30 * * * *" etc.
SCHEDULE="${BACKUP_SCHEDULE:-17 2 * * *}"

# Carry all BACKUP_* / POSTGRES_* / PG_* / AWS_* / UPLOAD_* env vars into the
# cron-invoked shell. Cron runs with a minimal environment, so quote every value
# before sourcing it from the scheduled shell.
: > /etc/cron.env
env | while IFS='=' read -r key value; do
    case "$key" in
        POSTGRES_*|PG_*|BACKUP_*|AWS_*|UPLOAD_*)
            escaped=$(printf '%s' "$value" | sed "s/'/'\\''/g")
            printf "export %s='%s'\n" "$key" "$escaped" >> /etc/cron.env
            ;;
    esac
done
chmod 600 /etc/cron.env

cat <<EOF > /etc/crontabs/root
${SCHEDULE} PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin /bin/sh -lc '. /etc/cron.env; exec /usr/local/bin/postgres-backup.sh' >> /var/log/backup.log 2>&1
EOF

mkdir -p /var/log
touch /var/log/backup.log

# If the operator passed RUN_ONCE_NOW=YES, fire a backup immediately and exit.
# Useful for "verify my backup config works" smoke tests.
if [ "${RUN_ONCE_NOW:-NO}" = "YES" ]; then
    exec /usr/local/bin/postgres-backup.sh
fi

echo "[oet-db-backup] schedule: ${SCHEDULE}"
echo "[oet-db-backup] streaming /var/log/backup.log"
tail -F /var/log/backup.log &
exec crond -f -L /dev/stdout
