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
| `Dockerfile` | Web-only multi-stage image (Next.js `output: 'standalone'`, `runner` target). Built and pushed to GHCR by `build-images.yml` `build-web`; also used by the local, desktop, staging and emergency source-build stacks. It does not build the API. |
| `backend/Dockerfile.runtime` | Production API image. `build-images.yml` `build-api` runs `dotnet publish` on the Actions host and packages `backend/publish` with this file. |
| `backend/Dockerfile` | API image built from source (SDK build stage). Used by the local, dev, backend, desktop, staging and emergency source-build compose files. |
| `backend/Dockerfile.dev` | `dotnet watch` API image for `docker-compose.hotreload.yml`. |
| `scripts/backup/Dockerfile` | `db-backup` sidecar image, built by `build-images.yml` `build-backup`. |
| `agent-gateway/Dockerfile` | Agent gateway image, built by `build-images.yml` `build-agent-gateway`. |
| `docker-compose.local.yml` | Full local stack (postgres + API + web) for Docker Desktop development. Mirrors production topology with simplified networking. Use with `--env-file .env.docker-local`. |
| `docker-compose.dev.yml` | Backend-only (postgres + API) in Docker; run Next.js on the host with `npm run dev` for hot-reload. Use with `--env-file .env.docker-local`. |
| `docker-compose.hotreload.yml` | Podman hot-reload stack (Next.js HMR + `dotnet watch`) started by `start-dev.ps1`; see `docs/QUICK-START.md`. |
| `docker-compose.production.yml` | The production stack (project `oetwebsite`): stable `web`/`learner-api` router containers plus blue/green app slots, Postgres, ClamAV, AI worker, agent gateway and backup sidecar joined to the external `npm_proxy` network for Nginx Proxy Manager. This is the one deployed at `app.oetwithdrhesham.co.uk`, and the only compose file `production-deploy.yml` ships to the VPS. |
| `docker-compose.production.hostports.yml` | Override — exposes ports on the host (no reverse proxy). Use for bare-metal / single-host installs without NPM. |
| `docker-compose.production.build.yml` | Override — emergency/local source-build when immutable image refs are unavailable. Needs explicit owner approval on the VPS (see §3). |
| `docker-compose.agent-console.yml` | Owner Agent Console, its own compose project (`oet-agent-console`). Deployed only by `.github/workflows/agent-console.yml`. |
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
- The API now uses first-party JWTs issued by the backend, so there is no Firebase, mock auth, or third-party JWT authority to configure for production. Firebase Phone Auth is an optional SMS OTP transport only (password reset + new-device approval). Email verification stays on Brevo. Do not treat Firebase ID tokens as app sessions.
- Firebase SMS OTP is off by default. Paste `FIREBASE__OTP__PROJECTID`, `FIREBASE__OTP__AUTHDOMAIN`, and `FIREBASE__OTP__APIKEY` (or set them in Admin → Firebase OTP), enable Phone Auth + SMS regions + authorized domain `oetwithdrhesham.co.uk` in Firebase Console, then turn `FIREBASE__OTP__ENABLED=true`. Keep `BREVO__ENABLED=true` as the email fallback. Localhost is not a valid Firebase phone-auth domain.
- `AUTHTOKENS__ACCESSTOKENSIGNINGKEY` and `AUTHTOKENS__REFRESHTOKENSIGNINGKEY` should be different random secrets, each at least 32 characters long.
- Brevo SMTP relay is the recommended production email path for this release. Set `SMTP__HOST=smtp-relay.brevo.com`, `SMTP__PORT=587`, `SMTP__ENABLESSL=true`, `SMTP__USERNAME` to the Brevo login shown in the Brevo console, and `SMTP__PASSWORD` to the Brevo SMTP key.
- If you want to use Brevo transactional templates through the API instead, enable `BREVO__ENABLED=true` and populate `BREVO__APIKEY`, `BREVO__FROMEMAIL`, `BREVO__EMAILVERIFICATIONTEMPLATEID`, and `BREVO__PASSWORDRESETTEMPLATEID`.
- `BREVO__WEBHOOKSECRET` should match the shared secret you configure on the Brevo webhook endpoint if you later wire webhook processing.
- `SMTP__USERNAME` and `SMTP__PASSWORD` are required for production SMTP relay delivery.
- `SEED_DEMO_DATA` should stay `false` in production.
- `AUTH__USEDEVELOPMENTAUTH` is only for local development and should remain `false` in production.
- The Reading/media smoke learner should have only the fixture entitlement needed by `scripts/deploy/reading-media-smoke.sh` and should not require MFA.

## 3. Build and deploy (GitHub Actions)

Production builds run on GitHub Actions. The production VPS must not run
frontend, API, backend, Next.js, or .NET build work. Its deploy role is limited
to pulling the prebuilt GHCR images, recreating containers, hosting the latest
native installers under `/var/opt/oet-learner/releases`, and running health
gates. If Actions is unavailable, fix Actions first; do not silently move heavy
build work to the VPS. Desktop/mobile release workflows upload only the latest
artifact per channel and delete the previous VPS copy automatically.

The normal path is two workflows (split 2026-10-03 so agents can build in
parallel while production rollouts serialize):

**Mandatory for every person, agent and subagent:** use this accelerated architecture
through `pnpm run ship` and inherit `AGENTS.md`. Native Claude/Gemini imports and
Copilot/Codex instructions point at the same contract. Release-bypass flags,
alternate production workflows, missing checkers, skipped physical proof and
unverified visibility/queue decisions are rejected. Owner-console engines remain
PR-only under their isolated Ship executor into this same graph; its deadline
watchdog cannot strand another ship's Actions by forcing private.

- `.github/workflows/build-images.yml` (**Build images**) — filtered build/deployment
  inputs on `main`, no cross-SHA lock:
  1. `changes` — compares the full successful-ancestor range and classifies
     web/API/backup/gateway/Writing inputs. Missing provenance rebuilds conservatively.
  2. `syntax-gate` — ship-gate self-test plus the CI ship gate (seconds); the
     Writing model-answer dotnet regression runs only when Writing changed.
  3. Component jobs build only changed inputs and publish immutable image digests
     plus per-SHA aliases. API publish generates idempotent SQL with `--no-build`
     and optional same-build references for the real required Writing tests.
     Its runtime separates stable published dependencies/content from the changing
     application assembly so a code change does not retransmit the full publish tree.
     The web runtime likewise separates root-owned standalone dependencies from
     `server.js` and `.next`; copy-time ownership preserves writable `.next`/`public`
     without a recursive ownership layer duplicating their files. Image-build
     assertions verify original standalone bytes and writable/read-only ownership.
     The existing test project opts into `DeploymentWritingGateOnly` for this gate
     only: unchanged gate/fixture/assembly sources, unchanged real test filter and TRX
     assertions. Actions checks the evaluated source sets; default full QA is unchanged.
  4. Unchanged components reuse a verified successful ancestor's immutable digest
     through registry-only aliases, never `:latest`. The final release manifest
     binds all components and original API SQL provenance after required gates.
- `.github/workflows/production-deploy.yml` (**Deploy production**) — starts when
  a Build images run on `main` succeeds (or `workflow_dispatch -f sha=<sha>` for
  a rollback); serialized by the `production-deploy` concurrency group:
  5. `apply-migrations` — verifies the original API source run/SHA/tool/checksum
     and applies its SQL when no actual successful production release proves the
     API digest was already deployed, even when the API was reused. Application uses the
     production PostgreSQL container (`scripts/deploy/apply-migrations-from-ci.sh`).
     Migrations are forward-only.
  6. `deploy` — streams `scripts/deploy/auto-deploy-ghcr.sh`,
   `docker-compose.production.yml`, `validate-production-env.sh` and the nginx
   router templates to the VPS with immutable refs. The script validates the
   environment, pulls once, reuses healthy native-config/image matches and repairs
   the inactive slot/shared services without touching postgres. Parallel readiness
   binds preparation to the exact SHA/slot/images/configuration. CI rechecks for
   successful descendant builds before promotion. Durable validated router configs
   are gracefully reloaded, with paired recovery on partial/public failure.
   Public release/slot headers and physical image proof precede `DEPLOY_LIVE`;
   the previous slot stays warm and optional cleanup is bounded.

This path does not wait for `qa-smoke.yml` or `sbom-sca.yml`; run those
separately when a change needs them. Operator checklist, forbidden commands and
topology: [`DEPLOY-MANUAL.md`](DEPLOY-MANUAL.md). Compute boundary:
[`docs/ops/production-compute-offload.md`](docs/ops/production-compute-offload.md).

### Pipeline rollback (digest-pinned)

Dispatch `gh workflow run production-deploy.yml -f sha=<previous-deployed-sha>`
through the public-before-Actions visibility lease. Only a proven previously
deployed release qualifies. The maintained driver loads that release's Compose
and templates, verifies immutable components, and does not reverse migrations.
Never invoke rollout scripts or Compose manually on the VPS.
`.deploy/live-release.env` and `.deploy/auto-deploy-history.tsv` record actual
runtime release/slot/images. A successful stand-down is not a promotion.

The 300-second target includes runner queues, cold builds, SQL and required
Writing gates. The ship watcher reports conservative before-first-push-attempt
to verified-live elapsed; `DEPLOY_LIVE` marks the earlier public-health/image
observation. See `docs/ops/deploy-gate.md` for measurement and guarded benchmark
dispatch. Never claim the target from unrelated QA duration or unrun checks.
Next's trusted cache remains enabled: measured compilation fell from 75 seconds
to 2.9 seconds. Cache persistence uses a native local-directory export instead of
loading a cache-only Docker image; only ephemeral hosted web builders skip explicit
teardown. Transfer costs and end-to-end elapsed remain part of acceptance.

Measured verified releases on 4 October 2026 were **744.784 seconds** (build
`37170176749`, deploy `37170535406`), **569.457 seconds** after the cache/API-layer
follow-up (build `37172887015`, deploy `37173143817`), and **582.880 seconds** for
a genuine changed-web/reused-API release (build `37174218390`, deploy `37174402463`).
The latter used a 5.7-second cached Next compilation and skipped API compilation
and already-applied SQL, but still spent 91.524 seconds pulling images and
94.649 seconds on actual target readiness. The web ownership-layer follow-up
(SHA `0689edfd0`, build `37176154885`, deploy `37176403559`) passed all original
standalone checksum/owner assertions and measured **529.552 seconds** inclusive
to verified physical serving, or **491.713 seconds** to the driver's public-live
marker. Native API/config reuse reduced its target readiness to 14.482 seconds;
image pulling took 54.659 seconds and runtime-layer export took 2.2 seconds.
Next compilation was cold at 89 seconds after the Dockerfile cache input changed.
These are different observed release conditions, not isolated causal benchmarks.
Cold forced-Writing benchmark `37173946904` verified the opt-in 3-source/default
666-source boundary and ran all 26 required tests successfully in a **310-second
build-only workflow**. It did not publish or promote a release. These results do
not establish a five-minute guarantee or cold-live acceptance on free runners.
Required gates remain intact. Long-running full QA is separate from public-live
acceptance; a superseded pending QA run with zero jobs is not test evidence.

The API-change/unchanged-web correction `e7485ce2f` (build `37180577765`,
deploy `37180764537`) verified exact green physical/public serving in
**510.240 seconds** inclusive, or **460.272 seconds** to the driver public-live
marker. Its new API compilation supplied the migration artifact, which was
verified and applied rather than skipped. Native image pulling took 22.908
seconds, initial readiness 68.965 seconds, the readiness recheck 13.755 seconds,
and router cutover 7.464 seconds; backup/gateway services were reused.
All 38 deployment protocol cases passed. A preserved raw-score regression
exposed by full QA was corrected without changing its assertions: manual
filtered run `37180640839` executed and passed all 67 selected Speaking cases
with zero skipped, plus the whole-solution compile and scoped frontend checks.
That is focused correction evidence, not a claim that the separate six-shard
QA run passed. The latest inclusive result is approximately **8 minutes
30 seconds**, still above the five-minute target on the approved free runners.

Migrations normally come from the build API publish artifact. Startup migration
is an opt-in (`AUTO_MIGRATE` → `Bootstrap__AutoMigrate`, default `false`).

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
- **ALL media/file data** in Docker volume `oetwebsite_oet_learner_storage` (audio, images, videos, documents, PDFs, uploads, OCR output, conversation recordings, pronunciation attempts, TTS output, content paper assets, profile photos, live class recordings, writing scans — everything)

**MISSION CRITICAL**: Every `docker-compose*.yml` MUST set
`Storage__LocalRootPath: /var/opt/oet-learner/storage` in the API environment.
Without this, the app defaults to `App_Data/storage` inside the container
filesystem and ALL media data is **permanently deleted** on container rebuild.
The backend crashes at startup in Production if this is misconfigured.

Back up both named volumes before upgrades or VPS maintenance.

## 8. Updating the deployment

Merge or push to `main` and let `build-images.yml` + `production-deploy.yml` build and deploy that exact SHA
(§3). The VPS must not build frontend, API, backend, Next.js, or .NET
artifacts. The step-by-step checklist is [`DEPLOY-MANUAL.md`](DEPLOY-MANUAL.md);
the pipeline rollback path is described in §3. Keep the previous slot warm and
at least one proven deployed SHA/slot/immutable image set available for rollback.

Do **not** run `docker compose down -v`, `docker volume prune`, `docker system prune --volumes`, or manually delete `oetwebsite_*` named volumes as part of a normal redeploy. Volume cleanup is a separate destructive maintenance task and requires an explicit backup, restore plan, and approval naming the exact volume.

Direct `docker compose up -d --build`, `docker compose build`, `pnpm run build`,
`dotnet build`, `dotnet test`, or `dotnet publish` are forbidden on the
production VPS unless the user explicitly approves an emergency source-build
exception in the current conversation. These commands bypass the production
digest-input gate and can overload the shared host.

Destructive or irreversible EF migrations require a maintenance window, fresh
verified backup ID, non-live restore drill evidence, and owner approval.
The legacy pre-flight helper describes those requirements; the
`build-images.yml` API SQL artifact / `production-deploy.yml` `apply-migrations` jobs do not independently approve destructiveness, so review such migrations
before they reach `main`.

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
  -e TARGET_DB=oet_learner_restore_check \
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

### Host cron jobs (outside Compose)

These host scripts sit next to the sidecar. The Google Drive backup runs from
the VPS root crontab and must not be disabled without restore-parity evidence
(`docs/ops/production-compute-offload.md`); the weekly audit scripts are
written for host cron as well:

- `scripts/db-nightly-backup-gdrive.sh` — `pg_dump` from `oet-postgres`,
  gzip, upload to Google Drive with `rclone` (remote `gdrive`); keeps 3 local
  dumps in `/root/backups/nightly` and only the latest remote copy.
- `scripts/db-weekly-audit.sh` — read-only DB audit report into
  `/root/backups/db-audits`. It needs `scripts/db-audit.sql` (and optionally
  `scripts/db-retention-audit.sql`) on the host; both are gitignored
  (`scripts/*.sql`) and exist only on the VPS.
- `scripts/db-weekly-audit-with-alerts.sh` — wraps the weekly audit and alerts
  through Sentry and Brevo (settings in `/root/.audit-alerts.env`).

The crontab lines themselves are not recorded in git yet (capturing them and a
SELECT-only `db-audit.sql` is an open owner item). Deploys no longer update the
source tree on the VPS, so the host copies of these scripts can drift from the
repository.
