---
name: "Deployment And Packaging"
description: "Use when editing Docker, CI/CD, release workflows, deployment docs, production/staging configuration, storage persistence, Electron packaging, or Capacitor build surfaces."
applyTo: "Dockerfile*,docker-compose*.yml,.github/workflows/*.yml,scripts/deploy/**,DEPLOYMENT.md,DEPLOY-MANUAL.md,electron/**,capacitor.config.ts,android/**,ios/**"
---

# Deployment And Packaging

Covers production/staging deployment, container images, CI/CD, and desktop/mobile packaging.
Local validation is NOT done here — it runs on the host via pnpm (see `validation.instructions.md`).

## Storage persistence (mission critical)

- All media/user files persist at `/var/opt/oet-learner/storage`.
- Every API-running `docker-compose*.yml` must set `Storage__LocalRootPath: /var/opt/oet-learner/storage`.
- Media/user file I/O goes through `IFileStorage` / `S3CompatibleFileStorage` — never raw
  `File.*`, `Path.*`, or `Directory.*` for media/user data.
- Never run `docker compose down -v`, `docker volume rm`, or recreate postgres/storage volumes without
  a verified backup and explicit user approval.

## Environments

- Keep production, staging, and local compose files distinct. Do not point local work at production
  data, secrets, or the VPS.
- Secrets come from environment / runtime settings, never hardcoded in images, compose, or workflows.
- CI/CD changes must keep build → test → deploy ordering and not weaken required checks.

## Desktop / mobile

- Electron packaging uses `electron-builder.config.cjs` and the desktop compose/Playwright configs.
- Capacitor (`capacitor.config.ts`, `android/`, `ios/`) wraps the web build; keep platform configs in sync.

## VPS production operational notes

The VPS (`185.252.233.186`, production deploy target — never run validation there) has known gotchas:

- **No heavy builds on the VPS:** frontend, API, backend, Next.js, and .NET
  builds must run on GitHub Actions. The VPS only fetches the exact commit,
  pulls prebuilt GHCR images, recreates containers, and runs health gates. Do
  not run `docker compose build`, `docker compose up --build`, `pnpm run build`,
  `dotnet build`, `dotnet test`, or `dotnet publish` on production unless the
  user explicitly approves an emergency source-build exception in the current
  conversation. If Actions is broken, fix Actions first.
- **ROUTER_IMAGE digest bug:** `.env.production` sets `ROUTER_IMAGE=nginx:...@sha256:...`. Docker cannot
  use a digest as a build tag. Always override `ROUTER_IMAGE=oetwebsite-nginx-router:local` for build/up.
- **Protected volumes:** never destroy `oetwebsite_oet_postgres_data` (database) or
  `oetwebsite_oet_learner_storage` (uploads). No `down -v` on the VPS.
- **Blue/green slots:** `oet-api-<slot>` + `oet-web-<slot>`; only one slot is live. Confirm the active
  slot before acting.
- **Nginx Proxy Manager health-test 400 false alarm:** `curl http://oet-api:8080/...` sends `Host: oet-api`, which ASP.NET
  rejects with 400. Use `wget` or `curl -H 'Host: <real-domain>'`. Real Nginx Proxy Manager traffic uses
  the real domain header, so this is not a production fault.
- Verify health via container health, restart counts, logs, direct service health endpoints, migrations,
  and backups — never treat a homepage `200` as sufficient.
- For VPS database SQL from Windows, pipe SQL through SSH into `docker exec -i ... psql`.

Detailed runbook: repo memory `/memories/repo/vps-production-deployment.md`.
