# Backup, Restore and Disaster Recovery Runbook

Security standard §14 (BCP-01…BCP-04). Status: **NOT YET VERIFIED** — no automated backup job exists in `docker-compose.production.yml`, so BCP-01/BCP-02 are FAIL until the job below runs and a restore is proven.

## Current state (verified 2026-09-12)

| Control | Requirement | Current state |
|---|---|---|
| BCP-01 | Automated encrypted backups of critical data, verified independently of the primary server | **FAIL** — Postgres is a Docker volume (`oet_postgres_data`) on the VPS with a `pg_isready` healthcheck only. No `pg_dump` job, no schedule, no off-host copy. |
| BCP-02 | Restore proven into an isolated environment | **FAIL** — never tested |
| BCP-03 | At least one backup path protected from routine production credentials | **FAIL** — not implemented |
| BCP-04 | Documented RTO/RPO for DB, payments, entitlement state, accounts, content | **PARTIAL** — objectives below are proposed, not yet agreed |

Data that must be covered: `oet_postgres_data` (learners, payments, entitlements, subscriptions, webhook evidence) and `oet_learner_storage` (uploaded content, generated PDFs, media).

## Proposed objectives

| Data | RPO | RTO |
|---|---|---|
| Payments / entitlements / subscriptions | 15 min | 4 h |
| User accounts and auth | 1 h | 4 h |
| Learning content and uploads | 24 h | 24 h |

## Backup job

Run on the VPS host (not inside the app container), as a dedicated cron entry. Never place credentials in the crontab line — source a root-owned env file.

```bash
#!/usr/bin/env bash
# /opt/oetwebapp/scripts/backup-oet.sh
set -euo pipefail

STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
OUT="/var/backups/oet"
AGE_RECIPIENT_FILE="/etc/oet/backup-recipient.pub"   # age public key, host-only
RETENTION_DAYS=30

install -d -m 0700 "$OUT"

# Database — custom format so a partial restore is possible.
docker exec oet-postgres pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc \
  | age -R "$AGE_RECIPIENT_FILE" > "$OUT/oet-db-$STAMP.dump.age"

# Uploaded content / generated artifacts.
docker run --rm -v oet_learner_storage:/data:ro alpine \
  tar -czf - -C /data . \
  | age -R "$AGE_RECIPIENT_FILE" > "$OUT/oet-storage-$STAMP.tar.gz.age"

# Integrity evidence for the backup itself.
sha256sum "$OUT/oet-db-$STAMP.dump.age" "$OUT/oet-storage-$STAMP.tar.gz.age" \
  > "$OUT/oet-$STAMP.sha256"

# Ship off-host (independent of the primary server).
rclone copy "$OUT/oet-db-$STAMP.dump.age"      oet-backup:oet/db/
rclone copy "$OUT/oet-storage-$STAMP.tar.gz.age" oet-backup:oet/storage/
rclone copy "$OUT/oet-$STAMP.sha256"           oet-backup:oet/manifests/

find "$OUT" -type f -name 'oet-*' -mtime +$RETENTION_DAYS -delete
```

Schedule: `17 */1 * * *` (hourly) for the database; `30 2 * * *` (daily) for storage.

BCP-03 (ransomware/operator-error resilience): the off-host target must use **write-only or append-only credentials** that the application and the normal deploy user do not hold. A backup the production credentials can delete is not a backup.

## Restore test (this is what makes BCP-02 PASS)

Do this on an isolated host, never against production. Record the date, the backup stamp used, the elapsed time, and the row counts observed.

```bash
# 1. Isolated target — separate host or a throwaway compose project.
docker run -d --name oet-restore-test -e POSTGRES_PASSWORD=test -p 55432:5432 postgres:16

# 2. Decrypt and restore.
age -d -i /etc/oet/backup.key -o /tmp/oet.dump /var/backups/oet/oet-db-<STAMP>.dump.age
docker exec -i oet-restore-test pg_restore -U postgres -d postgres --clean --if-exists < /tmp/oet.dump

# 3. Prove the data is usable, not merely present.
docker exec oet-restore-test psql -U postgres -c \
  "SELECT (SELECT count(*) FROM \"PaymentTransactions\") AS payments,
          (SELECT count(*) FROM \"PaymentWebhookEvents\") AS webhooks,
          (SELECT count(*) FROM \"Subscriptions\") AS subscriptions,
          (SELECT count(*) FROM \"ApplicationUserAccounts\") AS accounts;"

# 4. Reconcile against production counts captured at backup time and investigate any gap.
# 5. Drop the isolated environment.
docker rm -f oet-restore-test
```

A backup is **not valid** until step 3 returns plausible non-zero counts and step 4 reconciles. Repeat quarterly and after any schema change; keep the results as the BCP-02 evidence.

## Payment-specific recovery note

Payment state is the highest-value data and the hardest to rebuild. `PaymentWebhookEvents` holds the verified webhook evidence (`PayloadSha256`, `VerificationStatus`, `ParserVersion`), and `PaymentTransactions` holds the authoritative order state. If the database is lost, provider-side history (Stripe/PayPal dashboards, `GetTransactionConfirmationAsync` lookups, and the daily reconciliation worker) is the recovery source of truth — never assume entitlement from a learner's screenshot.

After any restore, run the reconciliation worker sweep (`Billing:Reconciliation:Enabled`) before reopening writes, and treat every `reconciliation.mismatch` billing event it emits as a manual review item.
