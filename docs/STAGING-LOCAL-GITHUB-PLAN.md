# Local/GitHub Staging Plan

**Created:** 2026-04-26  
**Scope:** repository artifacts only. This document does not authorize production VPS commands, production Docker commands, or Nginx Proxy Manager changes.

## Added artifacts

- `docker-compose.staging.yml` - staging compose stack with distinct container names and `oetwebsite_staging_*` volumes.
- `.env.staging.example` - non-secret template for `.env.staging`.
- `.github/workflows/deploy-staging.yml` - guarded workflow skeleton; it only runs when repository variable `ENABLE_STAGING_DEPLOY` is set to `true`.

## Staging hostnames

Recommended DNS/proxy names:

- Web: `staging.oetwithdrhesham.co.uk`
- API: `api-staging.oetwithdrhesham.co.uk`

## Required GitHub settings before enabling workflow

Repository variable:

- `ENABLE_STAGING_DEPLOY=true`

Repository secrets:

- `STAGING_SSH_HOST`
- `STAGING_SSH_USER`
- `STAGING_SSH_KEY`
- `STAGING_DEPLOY_PATH`

## Manual staging command reference

Run only on the staging host/path after `.env.staging` is created with non-production secrets:

```bash
docker compose --env-file .env.staging -f docker-compose.staging.yml up -d --build
docker compose --env-file .env.staging -f docker-compose.staging.yml ps
```

## Non-production guarantees

- `.env.staging` is intentionally ignored and must not be committed.
- `AI__ProviderId` defaults to `mock` in `.env.staging.example`.
- Billing sandbox fallbacks default to enabled in staging only.
- The production compose file and production deploy workflow are not modified by this staging artifact.
