# Manual Deploy (Single-Container VPS Model)

This is now the **only** supported production deploy flow. CI/CD deployment
pipelines and the blue/green router stack have been removed because the
build/runtime image-name split silently shipped stale images (visual fixes
appeared to "revert" after a build).

## Topology

One container per tier, on the shared host. nginx-proxy-manager already routes
to these exact container names — no NPM changes are ever needed:

| Domain                              | Container  | Port |
| ----------------------------------- | ---------- | ---- |
| https://app.oetwithdrhesham.co.uk   | `oet-web`  | 3000 |
| https://api.oetwithdrhesham.co.uk   | `oet-api`  | 8080 |

Supporting containers: `oet-postgres`, `oet-clamav`, `oet-db-backup`.

Compose file: [`docker-compose.vps.yml`](docker-compose.vps.yml).
Project name is pinned to `oetwebsite` so the live volumes
(`oetwebsite_oet_postgres_data`, `oetwebsite_oet_learner_storage`) and the
internal network are reused.

## Deploy

```bash
ssh vps
cd /opt/oetwebapp
git pull origin main
docker compose -f docker-compose.vps.yml --env-file .env.production build
docker compose -f docker-compose.vps.yml --env-file .env.production up -d
docker compose -f docker-compose.vps.yml --env-file .env.production ps
```

Because build and run use the **same** image tags (`oetwebsite-web:local`,
`oetwebsite-learner-api:local`), `up -d` always swaps in the freshly built
image. No retag step, no slot juggling.

`web` bakes `NEXT_PUBLIC_*` and `APP_URL` at build time, so frontend changes
(CSS, hover states, copy) only go live after the **build** step — never skip it.

## First-time cutover from blue/green (run once)

Remove only the old router + slot containers. **Never** use `-v` and never
remove volumes. Data lives in `oet_postgres_data` / `oet_learner_storage`.

```bash
cd /opt/oetwebapp
git pull origin main

# Build the new single-container images first.
docker compose -f docker-compose.vps.yml --env-file .env.production build

# Stop & remove ONLY the old OET router/slot containers (no volumes touched).
docker rm -f oet-web oet-api oet-web-blue oet-web-green oet-api-blue oet-api-green 2>/dev/null || true

# Bring up the simple stack (reuses oet-web / oet-api names NPM targets).
docker compose -f docker-compose.vps.yml --env-file .env.production up -d
docker compose -f docker-compose.vps.yml --env-file .env.production ps
```

Then verify:

```bash
curl -fsS https://api.oetwithdrhesham.co.uk/health/ready
curl -fsSI https://app.oetwithdrhesham.co.uk | head -n1
```

## Safety rules (shared production host)

- Only ever touch `oet-*` containers. ~50 unrelated containers share this host.
- Never run `docker compose down -v`, `docker volume rm`, or recreate the
  postgres/storage volumes without a verified backup and explicit approval.
- Builds run on the VPS only.
