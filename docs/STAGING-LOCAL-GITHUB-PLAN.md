# Local/GitHub Staging Plan

**Created:** 2026-04-26  
**Last updated:** 2025-07-14  
**Scope:** repository artifacts only. This document does not authorize production VPS commands, production Docker commands, or Nginx Proxy Manager changes.

---

## Repository artifacts

| File | Purpose |
|------|---------|
| `docker-compose.staging.yml` | Staging compose stack with distinct container names and `oetwebsite_staging_*` volumes. |
| `.env.staging.example` | Non-secret template for `.env.staging`. Lists every required/optional variable with comments. |
| `.github/workflows/deploy-staging.yml` | Guarded CI/CD workflow; only runs when repo variable `ENABLE_STAGING_DEPLOY` is `true`. |

---

## Staging hostnames

Recommended DNS / Nginx Proxy Manager proxy-host names:

| Service | Hostname |
|---------|----------|
| Web frontend | `staging.oetwithdrhesham.co.uk` |
| Learner API | `api-staging.oetwithdrhesham.co.uk` |

> **Action needed:** Create DNS A/CNAME records pointing both hostnames to the staging VPS IP, then add corresponding proxy-host entries in Nginx Proxy Manager forwarding to the `npm_proxy` Docker network.

---

## What's needed before staging can go live

### 1. Host / infrastructure

- [ ] A staging VPS (separate from production) with Docker & Docker Compose installed.
- [ ] DNS records for the two hostnames above pointing to the staging VPS.
- [ ] Nginx Proxy Manager (or equivalent reverse proxy) with the `npm_proxy` Docker network created.
- [ ] SSL certificates provisioned for both staging hostnames (Let's Encrypt via NPM works).

### 2. Secrets on the staging host

Create `.env.staging` by copying `.env.staging.example` and filling in every `replace-*` placeholder:

| Variable | What to provide |
|----------|-----------------|
| `POSTGRES_PASSWORD` | Any strong random string (non-production). |
| `AUTHTOKENS__ACCESSTOKENSIGNINGKEY` | 64-char random string (non-production). |
| `AUTHTOKENS__REFRESHTOKENSIGNINGKEY` | 64-char random string (different from above). |
| `BILLING__STRIPE__SECRETKEY` | Stripe **test** key (`sk_test_…`). Leave blank if not testing billing. |
| `BILLING__STRIPE__WEBHOOKSECRET` | Stripe test webhook secret (`whsec_…`). Leave blank if not testing billing. |
| `BREVO__APIKEY` | Brevo API key (only if `BREVO__ENABLED=true`). |
| `SMTP__USERNAME` / `SMTP__PASSWORD` | SMTP credentials (only if `SMTP__ENABLED=true`). |
| `GEMINI_API_KEY` | Google Gemini key (only if testing AI features). |
| `AI__ApiKey` | Backend AI provider key (only if `AI__ProviderId` is not `mock`). |

### 3. GitHub repository settings (for CI/CD)

**Repository variable:**

| Variable | Value |
|----------|-------|
| `ENABLE_STAGING_DEPLOY` | `true` |

**Repository secrets:**

| Secret | Description |
|--------|-------------|
| `STAGING_SSH_HOST` | Staging VPS IP or hostname. |
| `STAGING_SSH_USER` | SSH user with deploy permissions. |
| `STAGING_SSH_KEY` | Private SSH key (Ed25519 recommended). |
| `STAGING_DEPLOY_PATH` | Absolute path to the repo checkout on the VPS (e.g. `/opt/oet-staging`). |

---

## Manual staging command reference

Run **on the staging host** after `.env.staging` is created:

```bash
# Start / rebuild the staging stack
docker compose --env-file .env.staging -f docker-compose.staging.yml up -d --build

# Check service status
docker compose --env-file .env.staging -f docker-compose.staging.yml ps

# View logs
docker compose --env-file .env.staging -f docker-compose.staging.yml logs -f --tail=100

# Tear down (keeps volumes)
docker compose --env-file .env.staging -f docker-compose.staging.yml down

# Tear down AND delete volumes (full reset)
docker compose --env-file .env.staging -f docker-compose.staging.yml down -v
```

---

## Staging vs. production differences

| Concern | Staging | Production |
|---------|---------|------------|
| Compose file | `docker-compose.staging.yml` | `docker-compose.production.yml` |
| Env file | `.env.staging` | `.env.production` |
| Container prefix | `oet-staging-*` | `oet-prod-*` |
| Volume prefix | `oetwebsite_staging_*` | `oetwebsite_*` |
| `ASPNETCORE_ENVIRONMENT` | `Staging` | `Production` |
| `SEED_DEMO_DATA` | `true` (seed test data) | `false` |
| `ENABLE_SWAGGER` | `true` | `false` |
| `BILLING__ALLOWSANDBOXFALLBACKS` | `true` | `false` |
| `AI__ProviderId` | `mock` (default) | Real provider |
| `BREVO__ENABLED` / `SMTP__ENABLED` | `false` (default) | `true` |
| Sentry DSN | Optional / blank | Required |
| Backup jobs | Not configured | Scheduled (`BACKUP_*` vars) |
| Electron desktop hardening | Not applicable | Configured (`ELECTRON_*` vars) |
| Password breach-check | Disabled by default | Enabled |
| Host port mapping | None (expose only via NPM proxy) | `HOST_WEB_PORT`, `HOST_API_PORT` |

---

## Non-production guarantees

- `.env.staging` is git-ignored and must **never** be committed.
- `AI__ProviderId` defaults to `mock` — no real AI calls unless explicitly configured.
- Billing sandbox fallbacks are enabled — Stripe test keys are sufficient.
- Demo data is seeded by default for QA convenience.
- The production compose file and production deploy workflow are **not** modified by any staging artifact.
- Production-only variables (`BACKUP_*`, `ELECTRON_*`, `EVIDENCE_SIGNER_FINGERPRINT`, `READING_SMOKE_*`) are intentionally omitted from staging.
