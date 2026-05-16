# Linux VPS Deployment

This repository now ships with a production Docker stack for:

- `web`: Next.js learner app
- `learner-api`: ASP.NET Core 10 API
- `postgres`: PostgreSQL 17

It is designed for a Linux VPS where Nginx Proxy Manager runs in Docker and proxies to the app over a shared Docker network.

## Dockerfile & compose-file matrix

The repo intentionally ships multiple Docker build + compose files. Pick the
right pair for the scenario:

| File | Purpose |
| --- | --- |
| `Dockerfile` | Primary multi-stage image — builds Next.js (`output: 'standalone'`) + .NET API from source. Used by every `docker-compose.production*.yml`. |
| `Dockerfile.prebuilt` | Thin image that copies an already-built `.next/standalone` tree. Pair with `docker-compose.production.prebuilt-web.yml` when CI builds the web app and the VPS only needs to run it. |
| `docker-compose.production.yml` | Default VPS stack: stable `web`/`learner-api` router containers plus blue/green app slots, Postgres, ClamAV, and backup sidecar joined to the external `npm_proxy` network for Nginx Proxy Manager. This is the one deployed at `app.oetwithdrhesham.co.uk`. |
| `docker-compose.production.hostports.yml` | Same as above but exposes ports on the host (no reverse proxy) — use for bare-metal / single-host installs without NPM. |
| `docker-compose.production.prebuilt-web.yml` | Production variant that pulls the prebuilt web image instead of building on the VPS. Use when VPS CPU/RAM is too small to build. |
| `docker-compose.backend.yml` | Backend API + postgres only — for running the .NET API in Docker while developing the frontend locally via `npm run dev`. |
| `docker-compose.desktop.yml` | Local full-stack with demo accounts for Electron/Playwright E2E. Not production-safe. |

Rule of thumb: anything under `production.*.yml` must be launched with
`--env-file .env.production`; other files read from `.env` or defaults.

## 0. Local production smoke test

Before deploying, you can verify that the backend release build boots in `Production` mode with safe local overrides:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\probe-production.ps1
```

Expected output includes:

```text
READY_STATUS=200
```

## 1. Prepare the VPS

Install:

- Docker Engine
- Docker Compose plugin

Create the shared network that Nginx Proxy Manager and this stack will both join:

```bash
docker network create npm_proxy
```

If your Nginx Proxy Manager stack already uses a different external network name, reuse that name and set `NPM_PROXY_NETWORK` in your env file to match.

## 2. Create the production env file

Copy the template and fill in every value:

```bash
cp .env.production.example .env.production
```

Minimum values you must set correctly:

- `APP_URL`
- `NEXT_PUBLIC_API_BASE_URL`
- `PUBLIC_API_BASE_URL`
- `CHECKOUT_BASE_URL`
- `CORS_ALLOWED_ORIGINS`
- `POSTGRES_PASSWORD`
- `AUTHTOKENS__ISSUER`
- `AUTHTOKENS__AUDIENCE`
- `AUTHTOKENS__ACCESSTOKENSIGNINGKEY`
- `AUTHTOKENS__REFRESHTOKENSIGNINGKEY`
- `AUTHTOKENS__ACCESSTOKENLIFETIME`
- `AUTHTOKENS__REFRESHTOKENLIFETIME`
- `AUTHTOKENS__OTPLIFETIME`
- `AUTHTOKENS__AUTHENTICATORISSUER`
- `BREVO__ENABLED`
- `BREVO__APIKEY`
- `BREVO__FROMEMAIL`
- `BREVO__EMAILVERIFICATIONTEMPLATEID`
- `BREVO__PASSWORDRESETTEMPLATEID`
- `SMTP__HOST`
- `SMTP__USERNAME`
- `SMTP__PASSWORD`
- `SENTRY_DSN`
- `NEXT_PUBLIC_SENTRY_DSN`
- `BACKUP_GPG_PASSPHRASE`
- `BACKUP_S3_URL`
- `BACKUP_AWS_ACCESS_KEY_ID`
- `BACKUP_AWS_SECRET_ACCESS_KEY`
- `BACKUP_ALERT_WEBHOOK`
- `EVIDENCE_SIGNER_FINGERPRINT`
- `READING_SMOKE_LEARNER_EMAIL` / `READING_SMOKE_LEARNER_PASSWORD` for a least-privilege smoke learner
- `READING_SMOKE_*_ID` fixture values for the deploy-gate media smoke
- `ROUTER_IMAGE` pinned to an immutable `nginx`-compatible `@sha256:` digest
  for the stable blue/green router containers

Notes:

- `CHECKOUT_BASE_URL` should point at your frontend billing route or external payment handoff page.
- `PUBLIC_API_BASE_URL` must be the final public HTTPS API URL because the backend returns absolute upload/audio links.
- The API now uses first-party JWTs issued by the backend, so there is no Firebase, mock auth, or third-party JWT authority to configure for production.
- `AUTHTOKENS__ACCESSTOKENSIGNINGKEY` and `AUTHTOKENS__REFRESHTOKENSIGNINGKEY` should be different random secrets, each at least 32 characters long.
- Brevo SMTP relay is the recommended production email path for this release. Set `SMTP__HOST=smtp-relay.brevo.com`, `SMTP__PORT=587`, `SMTP__ENABLESSL=true`, `SMTP__USERNAME` to the Brevo login shown in the Brevo console, and `SMTP__PASSWORD` to the Brevo SMTP key.
- If you want to use Brevo transactional templates through the API instead, enable `BREVO__ENABLED=true` and populate `BREVO__APIKEY`, `BREVO__FROMEMAIL`, `BREVO__EMAILVERIFICATIONTEMPLATEID`, and `BREVO__PASSWORDRESETTEMPLATEID`.
- `BREVO__WEBHOOKSECRET` should match the shared secret you configure on the Brevo webhook endpoint if you later wire webhook processing.
- `SMTP__USERNAME` and `SMTP__PASSWORD` are required for production SMTP relay delivery.
- `SEED_DEMO_DATA` should stay `false` in production.
- `AUTH__USEDEVELOPMENTAUTH` is only for local development and should remain `false` in production.
- The production GitHub Environment variable `EVIDENCE_SIGNER_FINGERPRINT` must match `.env.production`; the protected GitHub value is the deploy verifier trust root.
- The Reading/media smoke learner should have only the fixture entitlement needed by `scripts/deploy/reading-media-smoke.sh` and should not require MFA.

## 3. Build and start the stack

Production rollout is exact-SHA only. First set the protected GitHub production
environment variable `ROUTER_IMAGE` to the same immutable digest configured in
`.env.production`, then run the protected GitHub workflow
`Verify Release Artifacts` for the target commit in `production` mode. It builds
and pushes immutable GHCR image digests, writes `image-digests.env`, signs the
evidence manifest, and uploads an artifact named `release-evidence-<sha>`.

Then run the protected `Deploy Production` workflow with the same 40-character
`target_sha`. The workflow downloads only `release-evidence-<sha>`, copies it to
the VPS, and runs the deploy helper with `DEPLOY_REF=<sha>`.

```bash
DEPLOY_REF=<40-character-sha> bash ./scripts/deploy/deploy-prod.sh
```

The helper refuses branch names and short SHAs, verifies signed evidence before
checkout reset, starts the inactive blue/green slot from digest-pinned images,
health-checks it internally, switches the stable `web`/`learner-api` router
containers to the new slot, and runs post-deploy verification, observability
smoke, and Reading/media smoke before a release is recorded as previous-good.

The API runs database migrations automatically on startup when `AUTO_MIGRATE=true`.

Production normally uses immutable image digests from release evidence. Local
rehearsal or emergency source-build fallback must opt in to the build override:

```bash
docker compose --env-file .env.production \
  -f docker-compose.production.yml \
  -f docker-compose.production.build.yml \
  up -d --build
```

That override bypasses immutable release images and is not the normal production
path.

## 4. Configure Nginx Proxy Manager

Attach your Nginx Proxy Manager container to the same external Docker network.

Create these proxy hosts:

1. `app.example.com` -> forward host `oet-web`, port `3000`
2. `api.example.com` -> forward host `learner-api`, port `8080`

Recommended Nginx Proxy Manager settings:

- Enable WebSocket support for both hosts
- Request a LetsEncrypt certificate for both hosts
- Force SSL
- Enable HTTP/2

## 5. Verify after first deploy

Check containers:

```bash
docker compose --env-file .env.production -f docker-compose.production.yml ps
```

Check API health:

```bash
curl https://api.example.com/health/live
curl https://api.example.com/health/ready
```

Check frontend:

```bash
curl https://app.example.com/api/health
```

## 6. Persistent data and backups

The stack persists:

- PostgreSQL data in Docker volume `oetwebsite_oet_postgres_data`
- Uploaded learner audio in Docker volume `oetwebsite_oet_learner_storage`

Back up both named volumes before upgrades or VPS maintenance.

## 7. Updating the deployment

For a clean production deploy, use the exact SHA that already has signed release
evidence:

```bash
DEPLOY_REF=<40-character-sha> bash ./scripts/deploy/deploy-prod.sh
```

That script preserves named volumes, verifies signed release evidence and image
digests for the deployed SHA, runs pre-flight, rolls out digest-pinned images
into the inactive blue/green slot, and only records `.deploy/previous-good.env`
and `.deploy/active-slot.env` after the health and smoke gates pass. By default
the previous slot remains running for fast router rollback; set
`KEEP_PREVIOUS_SLOT_RUNNING=false` only after confirming VPS capacity and a
separate rollback image path. Keep at least one previous-good evidence bundle and
digest set available for rollback.

Do **not** run `docker compose down -v`, `docker volume prune`, `docker system prune --volumes`, or manually delete `oetwebsite_*` named volumes as part of a normal redeploy. Volume cleanup is a separate destructive maintenance task and requires an explicit backup, restore plan, and approval naming the exact volume.

Direct `docker compose up -d --build` is reserved for local rehearsal and emergency use after explicit approval; it bypasses the production evidence gate.

Destructive or irreversible EF migrations require a maintenance window, fresh
verified backup ID, non-live restore drill evidence, and owner approval before
`scripts/deploy/pre-flight.sh` will proceed.

## Troubleshooting

- If the API exits on startup, check the first-party auth and SMTP settings first. The app fails fast when production settings are incomplete.
- If the API exits on startup after enabling Brevo API mode, check `BREVO__APIKEY`, `BREVO__FROMEMAIL`, and the required template IDs first. If you are using Brevo SMTP relay, check `SMTP__HOST`, `SMTP__USERNAME`, `SMTP__PASSWORD`, `SMTP__FROMEMAIL`, and `SMTP__ENABLESSL=true`.
- If browser uploads fail, confirm `PUBLIC_API_BASE_URL` is correct and that Nginx Proxy Manager can reach `learner-api:8080`.
- If the frontend cannot call the API, confirm `NEXT_PUBLIC_API_BASE_URL` matches the public API host and `CORS_ALLOWED_ORIGINS` includes the frontend host.

## Docker Desktop local deployment

If you want to run the full stack locally on Docker Desktop with built-in demo accounts, use the desktop compose file:

```powershell
docker compose -f docker-compose.desktop.yml up -d --build
```

Local test URLs:

- Frontend: `http://localhost:3000`
- Backend health: `http://localhost:5198/health`
- Backend liveness: `http://localhost:5198/health/live`
- Backend readiness: `http://localhost:5198/health/ready`
- Swagger: `http://localhost:5198/swagger`

Seeded local accounts:

- Learner: `learner@oet-prep.dev` / `Password123!`
- Expert: `expert@oet-prep.dev` / `Password123!`
- Admin: `admin@oet-prep.dev` / `Password123!`

Notes:

- The desktop stack uses development auth, so the backend accepts the seeded local accounts immediately.
- The frontend is built against `http://localhost:5198`, so it can be opened directly in your browser on the host machine.


## Disaster Recovery

The production compose stack includes an oet-db-backup sidecar that runs an encrypted pg_dump on BACKUP_SCHEDULE (default 02:17 UTC daily). Backups are kept locally in the `oetwebsite_oet_db_backups` Docker volume for BACKUP_RETENTION_DAYS (default 14) and pushed to S3-compatible storage via BACKUP_S3_URL.

### Prerequisites

- BACKUP_GPG_PASSPHRASE is set in .env.production. Without it backups are stored in plaintext which defeats the purpose.
- BACKUP_S3_URL is set to an offsite bucket. Local-only backups are lost if the VPS disk fails.
- BACKUP_AWS_ACCESS_KEY_ID / BACKUP_AWS_SECRET_ACCESS_KEY scoped to write-only on the bucket. Use a dedicated IAM user, not your root key.

### Run a backup immediately

```bash
ssh root@185.252.233.186
cd /opt/oetwebapp
docker compose --env-file .env.production -f docker-compose.production.yml exec \
  -e RUN_ONCE_NOW=YES \
  db-backup /usr/local/bin/entrypoint.sh
```

### List backups

```bash
cd /opt/oetwebapp
docker compose --env-file .env.production -f docker-compose.production.yml exec db-backup ls -lh /backups
```

### Restore procedure

1. Copy the target backup from S3/R2, or pick a local one from the `oetwebsite_oet_db_backups` volume, into the sidecar container.
2. Export `BACKUP_GPG_PASSPHRASE` in the shell or load it from the approved secret channel. Do not paste the passphrase into command history.
3. Restore into a **non-live** database first and verify:

```bash
cd /opt/oetwebapp
docker compose --env-file .env.production -f docker-compose.production.yml exec \
  -e CONFIRM_RESTORE=YES \
  -e BACKUP_FILE=/backups/oet-20260423T021700Z.dump.gpg \
  -e BACKUP_GPG_PASSPHRASE \
  -e TARGET_DB=oet_learner_restore_check \
  db-backup /usr/local/bin/postgres-restore.sh
```

4. Point a temporary API container at the restored DB and run smoke tests.
5. Only if (4) succeeds, restore into the live DB by also setting `RESTORE_INTO_LIVE=YES` and `TARGET_DB=`. Announce a maintenance window first.

### Verify backups are happening

```bash
cd /opt/oetwebapp
docker compose --env-file .env.production -f docker-compose.production.yml logs -f db-backup
```

A successful run ends with `[backup] ok: /backups/oet-...dump.gpg`. If you see nothing for 48 hours, the sidecar is not running; check `BACKUP_SCHEDULE` and `docker compose --env-file .env.production -f docker-compose.production.yml ps db-backup`.
