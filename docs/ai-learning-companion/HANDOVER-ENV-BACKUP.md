# §15 Handover — Environment, Secrets, Backup & Restore (OET Learner API)

Production host: `185.252.233.186` (`ssh vps`), app dir `/opt/oetwebapp`, env file
`/opt/oetwebapp/.env.production` (root-readable only — never printed in logs or tickets).

## 1. Runtime topology

| Component | Container | Notes |
|---|---|---|
| Learner API (blue/green) | `oet-api-blue` / `oet-api-green` | health-gated rollout; `X-Oet-Slot` header shows the serving slot |
| Web (blue/green) | `oet-web-blue` / `oet-web-green` | Next.js standalone |
| Edge nginx | `oet-api` (+ `ubag-nginx-dashboard`) | TLS termination, slot routing |
| PostgreSQL 17 | `oet-postgres` | user/db `oet_learner`; schema `public` |
| DB+media backup | `oet-db-backup` | cron `17 2 * * *` → `/usr/local/bin/postgres-backup.sh` |
| Fleet stack | `oet-fleet-manager`, `oet-agent-gateway`, `oet-agent-console`, `oet-agent-dockerproxy`, `oet-agent-egress` | helper fleet (see `docs/fleet/`) |
| Writing sidecars | `oet-writing-claude`, `oet-writing-codex` | subscription-backed graders (do not touch) |

## 2. Environment variable inventory (by consumer)

All production secrets live in `/opt/oetwebapp/.env.production` (never in source). Key groups:

- **Database**: `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB` (consumed by `oet-postgres` + API + backup sidecar).
- **AI provider platform keys** are NOT env: they live encrypted (`DataProtection` purpose `AiProvider.PlatformKey.v1`) in the `AiProviders.EncryptedApiKey` column, pasted via `/admin/ai-providers`. Rotation = admin edit + `Test` button per row. Exception: the writing sidecars hold their own subscription sessions inside their containers.
- **Stripe / PayPal / Checkout.com**: billing gateway env keys in `.env.production` (webhook secrets rotate in the gateway dashboard + env).
- **DataProtection key ring**: persisted volume shared by API slots — losing it invalidates every stored provider key; back it up with the same care as the DB.
- **OpenCode tuning** (this programme): `AiOpenAiCompatible__OpenCodeMaxInFlight` (default 8), `AiOpenAiCompatible__OpenCodeStreaming` (default on; `false` disables).
- **Auth**: JWT signing material + `BACKUP_GPG_PASSPHRASE` + optional `BACKUP_S3_URL` for off-host backup copy.

## 3. Backup & restore

**What runs automatically** (checked live 2026-10-07):

- Nightly `pg_dump --format=custom` → `/backups/oet-YYYYMMDD-HHMMSS.dump` inside `oet-db-backup`.
- Nightly media tarball → `/backups/oet-media-YYYYMMDD-HHMMSS.tar.gz` (verified present, daily cadence).
- Optional GPG symmetric encryption (`BACKUP_GPG_PASSPHRASE`) and optional `aws s3 cp` off-host copy when configured.

**Restore procedure** (production Postgres 17):

```sh
# 1. Stop writers (blue/green slots scale to 0) — prevents new writes mid-restore.
docker update --restart=no oet-api-blue oet-api-green && docker stop oet-api-blue oet-api-green

# 2. Drop + recreate the schema owner's objects (or restore into a fresh DB and repoint).
docker exec -i oet-postgres psql -U oet_learner -d postgres \
  -c "DROP DATABASE IF EXISTS oet_learner_restore;" \
  -c "CREATE DATABASE oet_learner_restore OWNER oet_learner;"

# 3. Restore the custom-format dump (parallel jobs if the host allows).
cat /backups/<dump>.dump | docker exec -i oet-postgres pg_restore -U oet_learner \
  -d oet_learner_restore --no-owner --jobs=4

# 4. Sanity-check, then swap:
docker exec oet-postgres psql -U oet_learner -d oet_learner_restore \
  -c "SELECT COUNT(*) FROM \"ApplicationUserAccounts\";"
# …verify counts, then rename databases in a maintenance window and start the API slots.
```

**Pre-change snapshot mode**: `postgres-backup.sh` also supports a one-shot snapshot for the
Owner Agent Console — use it before any risky migration or manual SQL.

**Media restore**: untar the desired `oet-media-*.tar.gz` over the media volume path (see
`docker inspect oet-db-backup` mounts for the host path).

## 4. Secret rotation runbook

| Secret | Where | Rotation |
|---|---|---|
| AI provider keys | `/admin/ai-providers` | paste new key → `Test` → save (encrypted at rest; hint column shows last 4) |
| Stripe webhooks | gateway dashboard + `.env.production` | roll in dashboard first, then env, then `docker compose up -d` for the API slots |
| JWT signing | `.env.production` | rotate → all sessions invalidate (users re-login; acceptable) |
| `BACKUP_GPG_PASSPHRASE` | `.env.production` + secure store | rotate → next nightly backup uses it; keep the old passphrase until the oldest restore point ages out |
| Temp ops admin (`sami-ops@…`) | `ApplicationUserAccounts` | **rotate/disable at handover**: delete the account or reset its password hash; grant row `apg_sami_ops_sysadmin` → remove |

## 5. Verification after any restore/rotation

1. `GET /health/ready` → `{"status":"ok", checks database/migrations ok}`.
2. Learner sign-in on a UAT account (device id header `x-oet-device-id`).
3. One companion chat turn (verifies route, quota, provider keys, hub).
4. `GET /v1/admin/companion/knowledge/ops/snapshot` → quality counters flowing.
