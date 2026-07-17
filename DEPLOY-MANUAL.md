# Manual Deploy

Production deploys must build heavy artifacts on GitHub Actions, not on the
production VPS.

The VPS is a shared production host. Do not run frontend, API, backend, Next.js,
or .NET build work there. The VPS deploy step only pulls prebuilt GHCR images,
recreates the target containers, runs health gates, and flips traffic after the
new slot is healthy.

## Required Flow

1. Push or merge the target commit to `main`.
2. Let `.github/workflows/deploy.yml` run for that exact commit, or manually run
   the `Build & Deploy (web + API)` workflow from GitHub Actions.
3. Confirm the workflow built and pushed:
   - `ghcr.io/jerryboganda/oetwebapp-web:<sha>`
   - `ghcr.io/jerryboganda/oetwebapp-api:<sha>`
4. Confirm the workflow SSH deploy step ran `scripts/deploy/auto-deploy-ghcr.sh`
   on the VPS.
5. Verify production:

```bash
curl -fsS https://api.oetwithdrhesham.co.uk/health/ready
curl -fsS https://app.oetwithdrhesham.co.uk/api/health
```

## VPS Role

The VPS may run only lightweight production operations:

```bash
cd /opt/oetwebapp
git fetch origin main
git reset --hard <exact-sha>
docker login ghcr.io
WEB_IMAGE=ghcr.io/jerryboganda/oetwebapp-web:<exact-sha> \
API_IMAGE=ghcr.io/jerryboganda/oetwebapp-api:<exact-sha> \
bash scripts/deploy/auto-deploy-ghcr.sh
```

Those commands pull and run existing images. They must not build images.

## Forbidden On The Production VPS

Never run these on production unless the user explicitly approves an emergency
source-build exception in the current conversation:

```bash
docker compose build
docker compose up --build
pnpm run build
pnpm exec tsc --noEmit
pnpm test
dotnet build
dotnet test
dotnet publish
```

If GitHub Actions is unavailable or broken, stop and fix the workflow first.
Do not silently move the heavy work to the VPS.

## Topology

Production currently uses the blue/green GHCR image flow:

- Stable router containers: `oet-web`, `oet-api`
- App slots: `oet-web-blue`, `oet-web-green`, `oet-api-blue`, `oet-api-green`
- Supporting containers: `oet-postgres`, `oet-clamav`, `oet-db-backup`
- Deploy helper: `scripts/deploy/auto-deploy-ghcr.sh`
- Workflow: `.github/workflows/deploy.yml`

Nginx Proxy Manager routes to the stable containers:

| Domain | Container | Port |
| --- | --- | --- |
| `https://app.oetwithdrhesham.co.uk` | `oet-web` | 3000 |
| `https://api.oetwithdrhesham.co.uk` | `oet-api` | 8080 |

## Safety Rules

- Only touch OET containers on the shared host.
- Never run `docker compose down -v`, `docker volume rm`, or recreate postgres
  or storage volumes without a verified backup and explicit approval.
- Preserve `oetwebsite_oet_postgres_data` and
  `oetwebsite_oet_with_dr_hesham_storage`.
- All media/user files must stay on persistent storage at
  `/var/opt/oet-with-dr-hesham/storage`.
