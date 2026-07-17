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
| `docker-compose.local.yml` | Full local stack (postgres + API + web) for Docker Desktop development. Mirrors production topology with simplified networking. Use with `--env-file .env.docker-local`. |
| `docker-compose.dev.yml` | Backend-only (postgres + API) in Docker; run Next.js on the host with `npm run dev` for hot-reload. Use with `--env-file .env.docker-local`. |
| `docker-compose.production.yml` | Default VPS stack: stable `web`/`learner-api` router containers plus blue/green app slots, Postgres, ClamAV, and backup sidecar joined to the external `npm_proxy` network for Nginx Proxy Manager. This is the one deployed at `app.oetwithdrhesham.co.uk`. |
| `docker-compose.production.hostports.yml` | Override — exposes ports on the host (no reverse proxy). Use for bare-metal / single-host installs without NPM. |
| `docker-compose.production.prebuilt-web.yml` | Override — uses prebuilt web image instead of building on the VPS. Use when VPS CPU/RAM is too small to build. |
| `docker-compose.production.build.yml` | Override — emergency/local source-build when immutable image refs are unavailable. |
| `docker-compose.staging.yml` | Full staging stack with pg_stat_statements. Use with `--env-file .env.staging`. |
| `docker-compose.backend.yml` | Backend API + postgres only — for running the .NET API in Docker while developing the frontend locally via `npm run dev`. |
| `docker-compose.desktop.yml` | Local full-stack with demo accounts for Playwright E2E. Not production-safe. |

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
- The Reading/media smoke learner should have only the fixture entitlement needed by `scripts/deploy/reading-media-smoke.sh` and should not require MFA.

## 3. Build and start the stack

Production builds run on GitHub Actions. The production VPS must not run
frontend, API, backend, Next.js, or .NET build work. Its deploy role is limited
to fetching the exact commit, pulling the prebuilt GHCR images, recreating
containers, and running health gates. If Actions is unavailable, fix Actions
first; do not silently move heavy build work to the VPS.

Production rollout is exact-SHA only. First run the protected `Build Release
Images` workflow for the target commit. It checks out the exact 40-character
SHA, builds and pushes the web, API, DB-backup, and router images to GHCR, and
uploads `release-images-<sha>/release-images.env` with immutable `@sha256`
refs for `WEB_IMAGE`, `API_IMAGE`, `DB_BACKUP_IMAGE`, and `ROUTER_IMAGE`.

Then run the protected `Deploy Production` workflow with the same `target_sha`
and the four refs from `release-images.env`. The deploy workflow downloads the
matching release-image artifact, rejects any image ref that does not exactly
match that artifact, requires successful `qa-smoke.yml` and `sbom-sca.yml` runs
for the exact SHA, downloads the matching SBOM/SCA artifact, logs the VPS into
GHCR with a temporary Docker config, and passes the digest refs to the VPS
deploy helper with `DEPLOY_REF=<sha>`.

Manual shell rollout is reserved for incident use and still uses prebuilt
images. Prefer the protected workflow; if shell rollout is required, pass the
exact SHA plus all four immutable image refs:

```bash
DEPLOY_REF=<40-character-sha> \
WEB_IMAGE=<web-image@sha256:...> \
API_IMAGE=<api-image@sha256:...> \
DB_BACKUP_IMAGE=<db-backup-image@sha256:...> \
ROUTER_IMAGE=<router-image@sha256:...> \
bash ./scripts/deploy/deploy-prod.sh
```

The helper refuses branch names and short SHAs, starts the inactive blue/green
slot from digest-pinned images, verifies each pulled image carries the expected
`org.opencontainers.image.revision=<sha>` label, health-checks it internally,
switches the stable `web`/`learner-api` router containers to the new slot, and
runs post-deploy verification, observability smoke, and Reading/media smoke
before a release is recorded as previous-good.

Rollback operators should read `.deploy/rollback-target.env` first. The rollout
copies the prior known-good release there before overwriting
`.deploy/previous-good.env` with the newly successful release.

The API runs database migrations automatically on startup when `AUTO_MIGRATE=true`.

Production normally uses immutable image digest inputs. Local rehearsal may use
the build override away from the production VPS. Emergency source-build fallback
on the production VPS requires explicit user approval in the current
conversation:

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

## 5. Runtime Settings (admin-configurable secrets)

After the first deploy, a `system_admin` should visit
`/admin/settings` and paste the service-level secrets from `.env.production`
into the UI. Once saved, those values are stored encrypted in the database and
the API picks up changes within 30 seconds — no restart or SSH required for
future rotations.

**Covered sections** (see [`docs/ADMIN-RUNTIME-SETTINGS.md`](docs/ADMIN-RUNTIME-SETTINGS.md)
for the full field-to-env-key mapping):

- Email — Brevo API + SMTP relay
- Billing — Stripe (publishable key, secret key, webhook secret, price IDs)
- Monitoring — Sentry DSN (backend + frontend)
- Backup — S3-compatible storage credentials and GPG passphrase
- OAuth — Google and Apple provider credentials
- Push notifications — VAPID, FCM, APNs

**Bootstrap minimum that must always remain in `.env.production`**
(these are never managed through the UI):

- `ConnectionStrings__DefaultConnection` — database is required before the API starts
- `AUTHTOKENS__ACCESSTOKENSIGNINGKEY` / `AUTHTOKENS__REFRESHTOKENSIGNINGKEY` — JWT secrets require a restart to rotate
- `AUTHTOKENS__ISSUER` / `AUTHTOKENS__AUDIENCE` — static JWT config
- AI gateway key for the grounding gateway (see `docs/AI-USAGE-POLICY.md`)

After pasting all service secrets into `/admin/settings`, the remaining entries
in `.env.production` for Brevo, Stripe, Sentry, Backup, OAuth, and Push become
inert fallbacks. They are still read if the DB row is absent or a field is null,
so do not delete them — they are your last-resort baseline.

Full setup guide, rotation runbook, and disaster-recovery procedure:
**[`docs/ADMIN-RUNTIME-SETTINGS.md`](docs/ADMIN-RUNTIME-SETTINGS.md)**

---

## 6. Verify after first deploy

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

## 7. Persistent data and backups

The stack persists:

- PostgreSQL data in Docker volume `oetwebsite_oet_postgres_data`
- **ALL media/file data** in Docker volume `oetwebsite_oet_with_dr_hesham_storage` (audio, images, videos, documents, PDFs, uploads, OCR output, conversation recordings, pronunciation attempts, TTS output, content paper assets, profile photos, live class recordings, writing scans — everything)

**MISSION CRITICAL**: Every `docker-compose*.yml` MUST set
`Storage__LocalRootPath: /var/opt/oet-with-dr-hesham/storage` in the API environment.
Without this, the app defaults to `App_Data/storage` inside the container
filesystem and ALL media data is **permanently deleted** on container rebuild.
The backend crashes at startup in Production if this is misconfigured.

Back up both named volumes before upgrades or VPS maintenance.

## 8. Updating the deployment

For a clean production deploy, use GitHub Actions first, then let the protected
deploy workflow SSH to the VPS and run the pull-only deploy helper for the exact
SHA. The VPS must not build frontend, API, backend, Next.js, or .NET artifacts.
If a manual incident rollout is required, use the CI-recorded immutable image
digest handoff:

```bash
DEPLOY_REF=<40-character-sha> \
WEB_IMAGE=<web-image@sha256:...> \
API_IMAGE=<api-image@sha256:...> \
DB_BACKUP_IMAGE=<db-backup-image@sha256:...> \
ROUTER_IMAGE=<router-image@sha256:...> \
bash ./scripts/deploy/deploy-prod.sh
```

That script preserves named volumes, validates immutable image digests, runs
pre-flight, rolls out digest-pinned images into the inactive blue/green slot, and
only records `.deploy/previous-good.env` and `.deploy/active-slot.env` after the
health and smoke gates pass. The prior known-good record is copied to
`.deploy/rollback-target.env` before it is overwritten, and every successful
rollout appends `.deploy/release-history.tsv`. By default the previous slot
remains running for fast router rollback; set `KEEP_PREVIOUS_SLOT_RUNNING=false`
only after confirming VPS capacity and a separate rollback image path. Keep at
least one previous-good SHA, slot, and image digest set available for rollback.

Do **not** run `docker compose down -v`, `docker volume prune`, `docker system prune --volumes`, or manually delete `oetwebsite_*` named volumes as part of a normal redeploy. Volume cleanup is a separate destructive maintenance task and requires an explicit backup, restore plan, and approval naming the exact volume.

Direct `docker compose up -d --build`, `docker compose build`, `pnpm run build`,
`dotnet build`, `dotnet test`, or `dotnet publish` are forbidden on the
production VPS unless the user explicitly approves an emergency source-build
exception in the current conversation. These commands bypass the production
digest-input gate and can overload the shared host.

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

- Learner: `learner@oet-with-dr-hesham.dev` / `Password123!`
- Expert: `expert@oet-with-dr-hesham.dev` / `Password123!`
- Admin: `admin@oet-with-dr-hesham.dev` / `Password123!`

Notes:

- The desktop stack uses development auth, so the backend accepts the seeded local accounts immediately.
- The frontend is built against `http://localhost:5198`, so it can be opened directly in your browser on the host machine.


## Disaster Recovery

The production compose stack includes an oet-db-backup sidecar that runs an
encrypted pg_dump and a learner-media archive on BACKUP_SCHEDULE (default 02:17
UTC daily). Backups are kept locally in the `oetwebsite_oet_db_backups` Docker
volume for BACKUP_RETENTION_DAYS (default 14) and pushed to S3-compatible
storage via BACKUP_S3_URL.

### Prerequisites

- BACKUP_GPG_PASSPHRASE is set in .env.production. Without it backups are stored in plaintext which defeats the purpose.
- BACKUP_S3_URL is set to an offsite bucket. Local-only backups are lost if the VPS disk fails.
- BACKUP_AWS_ACCESS_KEY_ID / BACKUP_AWS_SECRET_ACCESS_KEY scoped to write-only on the bucket. Use a dedicated IAM user, not your root key.
- BACKUP_RESTORE_DRILL_ID names the latest successful non-live restore drill
  evidence. Production env validation fails closed without it. Temporary
  owner-approved break glass uses
  `BACKUP_BREAK_GLASS_ACKNOWLEDGEMENT=i-accept-temporary-no-offsite-backup-risk`.

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

1. Copy the target DB and media backups from S3/R2, or pick local ones from the
   `oetwebsite_oet_db_backups` volume, into the sidecar container.
2. Export `BACKUP_GPG_PASSPHRASE` in the shell or load it from the approved secret channel. Do not paste the passphrase into command history.
3. Restore into a **non-live** database first and verify:

```bash
cd /opt/oetwebapp
docker compose --env-file .env.production -f docker-compose.production.yml exec \
  -e CONFIRM_RESTORE=YES \
  -e BACKUP_FILE=/backups/oet-20260423T021700Z.dump.gpg \
  -e BACKUP_GPG_PASSPHRASE \
  -e TARGET_DB=oet_with_dr_hesham_restore_check \
  db-backup /usr/local/bin/postgres-restore.sh
```

4. Point a temporary API container at the restored DB and run smoke tests.
5. Verify the media archive into a non-live directory:

```bash
cd /opt/oetwebapp
docker compose --env-file .env.production -f docker-compose.production.yml exec \
  -e MEDIA_BACKUP_FILE=/backups/oet-media-20260423T021700Z.tar.gz.gpg \
  -e BACKUP_GPG_PASSPHRASE \
  db-backup /usr/local/bin/media-restore-verify.sh
```

6. Only if (4) and (5) succeed, restore into live targets. For DB restore, also
   set `RESTORE_INTO_LIVE=YES` and `TARGET_DB=`. Announce a maintenance window
   first.

### Verify backups are happening

```bash
cd /opt/oetwebapp
docker compose --env-file .env.production -f docker-compose.production.yml logs -f db-backup
```

A successful run ends with `[backup] ok: /backups/oet-...dump.gpg` and
`[backup] media ok: /backups/oet-media-...tar.gz.gpg`. If you see nothing for
48 hours, the sidecar is not running; check `BACKUP_SCHEDULE` and
`docker compose --env-file .env.production -f docker-compose.production.yml ps
db-backup`.
