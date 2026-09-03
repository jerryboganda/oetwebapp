---
name: "Deployment And Packaging"
description: "Use when editing Docker, CI/CD, release workflows, deployment docs, production/staging configuration, storage persistence, Electron packaging, or Capacitor build surfaces."
applyTo: "Dockerfile*,docker-compose*.yml,.github/workflows/*.yml,scripts/deploy/**,scripts/ship/**,DEPLOYMENT.md,DEPLOY-MANUAL.md,electron/**,capacitor.config.ts,android/**,ios/**"
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
- Android is `com.oetwithdrhesham.app`; iOS is `com.oetprep.learner`. These are
  independently owned (Android was renamed, iOS deliberately was not) — never let an
  Android-scoped change touch `ios/**` or vice versa without explicit owner sign-off.

## Play Store release automation (compulsory) — see `docs/play-store-automation.md`

Play Console access for this app is fully automated via a Google Play Developer API
service account and a Python toolkit at `automation/` (sibling folder, outside this
repo — see `docs/play-store-automation.md` for setup/CLI/gotchas). Any release upload,
store listing text/image change, tester-list check, or review reply **must** go through
that toolkit instead of manual Play Console clicking or a hand-authored/regenerated
asset that duplicates what the toolkit already manages. Before changing anything that
affects live Play Store state, query it first (`list-tracks` / `get-listing`) — don't
assume this repo's `fastlane/`/`docs/`/asset files match what's actually live. A parallel
agent skipping this check on 2026-09-04 shipped a conflicting package/branding change
that had already been superseded on live Play Console and had to be reverted.

## Post-push ownership (do not stop at "deploy initiated")

- Required local gate before every `main` push: `pnpm run ship:gate` (`scripts/ship/pre-push-gate.mjs`). Seconds only. Catches conflict markers, leftover rebase splices, and brace imbalance. Not a full `pnpm build` / `dotnet test`.
- After push, watch **only** `Build & Deploy (web + API)` for this SHA: `pnpm run ship:watch`. Dump `--log-failed` on red, fix, gate, push again without waiting for the owner. Ignore QA Smoke.
- `deploy.yml` `syntax-gate` job must stay first (`needs` of every image build). Do not remove it to "save a minute".
- Flip the repo private only after this SHA's Build & Deploy succeeds. Then confirm public health + VPS image tags contain the SHA. VPS remains pull-only.

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
