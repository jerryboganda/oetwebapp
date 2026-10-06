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
- CI/CD changes must keep the required build/Writing/guard gates before migration application and promotion. There is no QA workflow (owner directive 2026-10-06); do not remove a required build/Writing/guard gate to improve timing.

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

- **Mandatory accelerated baseline:** `AGENTS.md`'s owner directive 2026-10-04
 applies to all contributors/agents; preserve the verified 510.240-second release
 architecture, not an invented duration guarantee.
- One command: `pnpm run ship` — ship lock → rebase on `origin/main` → `ship:gate` → visibility lease + public flip → push with rebase-retry → watch `Deploy production` → `ax:record` → lease release (private only when no other lease holder and no run queued/in-progress). Diagnostics: `--dry-run`, `--no-push`, `--status`; verified recovery: `--sha <sha>`.
- `--no-watch`, `--no-record`, `--no-visibility`, `--force-release` and alternate
 rollout workflows are rejected before mutation. Unknown visibility/queue data,
 missing contract checkers and failed evidence recording cannot report success.
 A live ship lock is never stolen, including when synchronous watching delays its heartbeat.
 Workstation and console visibility holders use the same native channel; unknown
 local state blocks, lock creation is exclusive, and inactive locks require exact-file
 recovery rather than unsafe automatic unlink/recreate.
- `pnpm run ship:gate` alone stays the seconds-long pre-push check inside that flow (conflict markers, leftover rebase splices, brace imbalance). Not a full `pnpm build` / `dotnet test`.
- **Parallel agents (owner directive 2026-10-03):** never flip visibility by hand while another session is shipping — the wrapper owns the flips under a cross-session lease. A push may be SUPERSEDED by a successful descendant main build; the watcher follows actual promotion, not a successful stand-down. There is no automated QA in CI at all (owner directive 2026-10-06).
- Once live health is green, the wrapper records with `ax:record`; `pnpm run ax:verify`
 (or wrapper `--verify`) re-checks the recorded run ids against GitHub.
- `build-images.yml` `syntax-gate` job must stay first (`needs` of every image build). Do not remove it to "save a minute".
- Reuse only successful ancestor release manifests and immutable component digests; never mutable `latest`. Missing provenance rebuilds conservatively. Reused API images retain their original SQL source run/checksum.
- Generate API SQL using the existing publish compilation with `--no-build`; production downloads/verifies that artifact and never installs the SDK or recompiles. Required Writing tests still restore, compile and execute even when same-build API references are reused.
- Prepare and promote bind the same SHA, slot, images, effective Compose configuration and templates. CI rechecks successful-descendant eligibility at the boundary. Durable directory-mounted router configs are validated and gracefully reloaded; failure restores/reloads both previous configs.
- Measure before-first-push-attempt to verified public serving identity, including queues/retries. `DEPLOY_LIVE` marks the public health/image observation separately from cleanup. Five minutes is a measured target, never permission to shorten drainage or skip readiness.
- Rollbacks: `gh workflow run production-deploy.yml -f sha=<previous-sha>` — the images are already in GHCR, so no rebuild is needed.
- **Pipeline-only deploys (hard enforced):** the rollout path is `Build images` → `Production deploy`; never SSH-deploy, never run the rollout script or `docker compose` on the VPS by hand, never add a second rollout workflow. `pnpm run pipeline:check` (`scripts/deploy/verify-pipeline-contract.mjs`) runs in the `guards` job of every build **and** inside `pnpm run ship:gate`, so a bypassing change fails the pipeline before images exist.
- Flip the repo private only under the lease rule above. Then confirm public health + VPS image tags contain the SHA. VPS remains pull-only.

## Owner Fleet (owner directive 2026-10-05) — see `docs/ops/FLEET.md`, `docs/adr/0005-fleet-manager-and-remote-workers.md`

Read the `AGENTS.md` "Owner Fleet exception" before touching `platform/**`, `.github/workflows/fleet.yml` or the fleet rules of the pipeline contract.

- Fleet code lives under `platform/**`, never under `scripts/deploy/**`, `scripts/backup/**`, root `*.json|*.ts|*.mjs` or `docker-compose.production*.yml`
  (those are build inputs and would cost a production release). Its pipeline is `.github/workflows/fleet.yml` (name `Fleet (build + rollout)`,
  concurrency group `fleet`), separate from `Build images` and `Deploy production`: **no fifth release component**, no edit of the `build-images.yml`
  job graph, never `auto-deploy-ghcr.sh`.
- Its SSH rollout stays pull-only between `# BEGIN REMOTE FLEET ROLLOUT` and `# END REMOTE FLEET ROLLOUT` (`compose pull`, `up --no-build`; no build,
  install, source sync, volume removal or `:latest`) and `fleet.yml` runs its own `guards` job (`node scripts/deploy/verify-pipeline-contract.mjs`,
  `bash scripts/deploy/verify-compute-offload.sh`) because a platform-only push does not run the `Build images` guards.
- Only the audited workflows in `PROD_SSH_WORKFLOWS` (`scripts/deploy/verify-pipeline-contract.mjs`; the eight that hold an SSH credential, `fleet.yml` being
  the eighth: only its dispatch-only `sync` job) may hold a production/VPS SSH credential. A new SSH workflow is a visible edit of that list in the same
  commit plus the owner-written exception; the fleet scan also rejects `accept-new` host-key trust and committed keys or tokens.
- Verify a fleet change with `gh run watch` on `fleet.yml` for the SHA, then `pnpm run ax:record`. A platform-only push ends `pnpm run ship` with
  `SHIP-WATCH_NOTHING_TO_DEPLOY`; never loosen the `Deploy production` watcher to cover it.
- Helpers run prebuilt images by digest only; Ansible runs only inside the manager container after an owner UI action; agents never hold helper IPs, keys
  or tokens; console agents are denied every `oet-fleet*` container, volume and network (`agent-console/dockerproxy/src/policy.ts`).
- `scripts/deploy/protect-production-data.sh` lists `oet-fleet_fleet_data` as protected and `scripts/deploy/prune-stale-images.sh` skips `oetwebapp-fleet-*`.
- `fleet.yml` is **BUILD-ONLY** (owner directive 2026-10-06): compile, package, push and roll out. No test, load, parity, conformance or benchmark job in any
  workflow (rule 3 of the pipeline contract bars every test/QA runner outside `build-images.yml`; the fleet rule adds benchmark/parity/conformance). The owner
  QAs the fleet by hand; never claim a test passed. New fleet paths must stay under `platform/**` (not an image input of the four release components).

## VPS production operational notes

The VPS (`185.252.233.186`, production deploy target — never run validation there) has known gotchas:

- **No heavy builds on the VPS:** frontend, API, backend, Next.js, and .NET
  builds must run on GitHub Actions. The VPS only fetches the exact commit,
  pulls prebuilt GHCR images, recreates containers, and runs health gates. Do
  not run `docker compose build`, `docker compose up --build`, `pnpm run build`,
  `dotnet build`, `dotnet test`, or `dotnet publish` on production unless the
  user explicitly approves an emergency source-build exception in the current
  conversation. If Actions is broken, fix Actions first.
- **Digest identity:** stable runtime refs use verified `repository@sha256:...`; local per-release aliases support serving proof and rollback. Never replace a pinned runtime digest with a mutable tag or build a router on the VPS.
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
