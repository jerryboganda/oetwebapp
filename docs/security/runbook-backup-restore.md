# Backup, Restore and Disaster Recovery Runbook

Security standard §14 (BCP-01…BCP-04). Status: **NOT YET VERIFIED** — automated backups exist (below), but their production configuration and a restore have not been evidenced, so BCP-01/BCP-02 stay open until the owner records that evidence.

The operational procedure (run a backup now, list backups, restore, verify) is `DEPLOYMENT.md` §Disaster Recovery. This page tracks the security controls only.

## Current state

| Control | Requirement | Current state |
|---|---|---|
| BCP-01 | Automated encrypted backups of critical data, verified independently of the primary server | **PARTIAL** — the `db-backup` sidecar (below) and a root-cron Google Drive dump both run on the VPS. Encryption and off-host copy depend on `BACKUP_GPG_PASSPHRASE` and `BACKUP_S3_URL` being set in `.env.production`; owner to verify and record. |
| BCP-02 | Restore proven into an isolated environment | **OPEN** — no restore drill evidence recorded (`BACKUP_RESTORE_DRILL_ID`) |
| BCP-03 | At least one backup path protected from routine production credentials | **OPEN** — owner to verify the S3/R2 and Drive credentials cannot delete existing backups |
| BCP-04 | Documented RTO/RPO for DB, payments, entitlement state, accounts, content | **PARTIAL** — objectives below are proposed, not yet agreed |

Data that must be covered: the `oetwebsite_oet_postgres_data` volume (learners, payments, entitlements, subscriptions, webhook evidence) and `oetwebsite_oet_learner_storage` (uploaded content, generated PDFs, media).

## Proposed objectives

| Data | RPO | RTO |
|---|---|---|
| Payments / entitlements / subscriptions | 15 min | 4 h |
| User accounts and auth | 1 h | 4 h |
| Learning content and uploads | 24 h | 24 h |

## Backup jobs (existing)

- **`db-backup` sidecar** (`oet-db-backup`, `docker-compose.production.yml`,
  image built by `deploy.yml` `build-backup`, scripts in `scripts/backup/`).
  On `BACKUP_SCHEDULE` (default `17 2 * * *`, 02:17 UTC daily) it writes a
  `pg_dump --format=custom` and a learner-media `tar.gz`, GPG-encrypts both
  (AES256) when `BACKUP_GPG_PASSPHRASE` is set, copies them to `BACKUP_S3_URL`
  (S3/R2) when set, and keeps `BACKUP_RETENTION_DAYS` (default 14) locally in
  the `oetwebsite_oet_db_backups` volume.
- **Root-cron Google Drive dump** — `scripts/db-nightly-backup-gdrive.sh`
  (`pg_dump` + gzip + `rclone`, not encrypted by the script). See
  `DEPLOYMENT.md` §Host cron jobs and `docs/ops/production-compute-offload.md`.

The RPO targets below are tighter than the current daily schedule; closing that
gap is part of agreeing BCP-04.

BCP-03 (ransomware/operator-error resilience): the off-host target must use **write-only or append-only credentials** that the application and the normal deploy user do not hold. A backup the production credentials can delete is not a backup.

## Restore test (this is what makes BCP-02 PASS)

Do this into a non-live database, never over production. Record the date, the backup stamp used, the elapsed time, and the row counts observed.

1. Restore a sidecar backup into a non-live database with
   `scripts/backup/postgres-restore.sh` (`TARGET_DB=oet_learner_restore_check`),
   and verify the media archive with `scripts/backup/media-restore-verify.sh`.
   The exact commands are in `DEPLOYMENT.md` §Disaster Recovery → Restore
   procedure.
2. Prove the data is usable, not merely present:

```bash
docker exec oet-postgres psql -U oet_learner -d oet_learner_restore_check -c \
  "SELECT (SELECT count(*) FROM \"PaymentTransactions\") AS payments,
          (SELECT count(*) FROM \"PaymentWebhookEvents\") AS webhooks,
          (SELECT count(*) FROM \"Subscriptions\") AS subscriptions,
          (SELECT count(*) FROM \"ApplicationUserAccounts\") AS accounts;"
```

3. Reconcile against production counts captured at backup time and investigate any gap.
4. Drop the restore-check database.

A backup is **not valid** until step 2 returns plausible non-zero counts and step 3 reconciles. Repeat quarterly and after any schema change; keep the results as the BCP-02 evidence.

## Payment-specific recovery note

Payment state is the highest-value data and the hardest to rebuild. `PaymentWebhookEvents` holds the verified webhook evidence (`PayloadSha256`, `VerificationStatus`, `ParserVersion`), and `PaymentTransactions` holds the authoritative order state. If the database is lost, provider-side history (Stripe/PayPal dashboards, `GetTransactionConfirmationAsync` lookups, and the daily reconciliation worker) is the recovery source of truth — never assume entitlement from a learner's screenshot.

After any restore, run the reconciliation worker sweep (`Billing:Reconciliation:Enabled`) before reopening writes, and treat every `reconciliation.mismatch` billing event it emits as a manual review item.
