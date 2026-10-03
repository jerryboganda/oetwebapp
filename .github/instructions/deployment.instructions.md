---
name: "Deployment And Packaging"
description: "Use when editing Docker, CI/CD, release workflows, deployment docs, production/staging configuration, storage persistence, Tauri desktop packaging, or Capacitor build surfaces."
applyTo: "Dockerfile*,docker-compose*.yml,.github/workflows/*.yml,scripts/deploy/**,scripts/ship/**,DEPLOYMENT.md,DEPLOY-MANUAL.md,capacitor.config.ts,android/**,ios/**"
---

# Deployment And Packaging

Covers production/staging deployment, container images, CI/CD, and desktop/mobile packaging.
Validation is not done here or on the VPS — it runs only on GitHub Actions (see `validation.instructions.md`).

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

- Desktop is Tauri 2 (`src-tauri/`): gated by `tauri-ci.yml`, released by `tauri-desktop-release.yml`.
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

- One command: `pnpm run ship` — ship lock → rebase on `origin/main` → `ship:gate` → visibility lease + public flip → push with rebase-retry → watch `Deploy production` → `ax:record` → lease release (private only when no other lease holder and no run queued/in-progress). Escape hatches: `--dry-run`, `--no-push`, `--no-watch`, `--sha <sha>`, `--status`, `--release-lease`.
- `pnpm run ship:gate` alone stays the seconds-long pre-push check inside that flow (conflict markers, leftover rebase splices, brace imbalance). Not a full `pnpm build` / `dotnet test`.
- **Parallel agents (owner directive 2026-10-03):** never flip visibility by hand while another session is shipping — the wrapper owns the flips under a cross-session lease. A push may be SUPERSEDED before its deploy runs (the newest push contains it); the watcher follows the newer run and prints `SHIP-WATCH_SUPERSEDED_BY`. Ignore QA Smoke on a push: the 13-project e2e matrix is on-demand (nightly + dispatch) and no longer cancelled by the next push.
- Once live health is green, `pnpm run ax:record` then `pnpm run ax:verify` (the wrapper already does this on a green watch) so `VERIFICATION.md` carries this SHA's real run ids.
- `build-images.yml` `syntax-gate` job must stay first (`needs` of every image build). Do not remove it to "save a minute".
- Rollbacks: `gh workflow run production-deploy.yml -f sha=<previous-sha>` — the images are already in GHCR, so no rebuild is needed.
- **Pipeline-only deploys (hard enforced):** the rollout path is `Build images` → `Production deploy`; never SSH-deploy, never run the rollout script or `docker compose` on the VPS by hand, never add a second rollout workflow. `pnpm run pipeline:check` (`scripts/deploy/verify-pipeline-contract.mjs`) runs in the `guards` job of every build **and** inside `pnpm run ship:gate`, so a bypassing change fails the pipeline before images exist.
- Flip the repo private only under the lease rule above. Then confirm public health + VPS image tags contain the SHA. VPS remains pull-only.

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
