# Manual Deploy

Production deploys must build heavy artifacts on GitHub Actions, not on the
production VPS.

The VPS is a shared production host. Do not run frontend, API, backend, Next.js,
or .NET build work there. The VPS deploy step only pulls prebuilt GHCR images,
recreates the target containers, runs health gates, and flips traffic after the
new slot is healthy.

## Required Flow

1. Push or merge the target commit to `main`.
2. Let `.github/workflows/build-images.yml` (`Build images`) run for that exact
   commit — `.github/workflows/production-deploy.yml` (`Deploy production`)
   starts automatically when the build succeeds. For a rollback, dispatch
   `Deploy production` with `-f sha=<previous-sha>` (images already in GHCR).
3. Confirm the verified release manifest built or immutably reused all four aliases:
   - `ghcr.io/jerryboganda/oetwebapp-web:<sha>`
   - `ghcr.io/jerryboganda/oetwebapp-api:<sha>`
   - `ghcr.io/jerryboganda/oetwebapp-db-backup:<sha>`
   - `ghcr.io/jerryboganda/oetwebapp-agent-gateway:<sha>`
4. Confirm the workflow SSH deploy step ran `scripts/deploy/auto-deploy-ghcr.sh`
   on the VPS.
5. Verify production:

```bash
curl -fsS https://api.oetwithdrhesham.co.uk/health/ready
curl -fsS https://api.oetwithdrhesham.co.uk/health/live
curl -fsS https://app.oetwithdrhesham.co.uk/api/health
```

## VPS Role

Only the production workflow invokes rollout operations. Never SSH-deploy, sync
source or run rollout scripts/Compose by hand. API migration SQL comes from the
same publish compilation on Actions and is verified before application.

The driver pulls immutable digests, preserves per-SHA aliases, repairs only
changed/stale/unhealthy services, and health-gates the inactive slot. CI rechecks
successful-descendant eligibility between bound preparation and promotion.
Durable router configs are validated and gracefully reloaded as a pair; a failed
cutover restores both previous configs. The prior slot remains warm.

For rollback, use `gh workflow run production-deploy.yml -f sha=<previous-deployed-sha>`
through the public-before-Actions visibility lease. The maintained driver uses
the proven target release's configuration and does not reverse database migrations.
`.deploy/auto-deploy-history.tsv` and `.deploy/live-release.env` record runtime
image/slot identity. Follow the run through actual promotion and live proof.

The inclusive 300-second target includes queues, cold builds, SQL and required
Writing checks. `pnpm run ship` reports conservative before-first-push-attempt
to verified-live elapsed; do not substitute a workflow completion timestamp or
an unrelated QA duration. See `docs/ops/deploy-gate.md`.

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
- Workflows: `.github/workflows/build-images.yml` (images → GHCR) and
  `.github/workflows/production-deploy.yml` (pull + blue/green rollout)

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
  `oetwebsite_oet_learner_storage`.
- All media/user files must stay on persistent storage at
  `/var/opt/oet-learner/storage`.
