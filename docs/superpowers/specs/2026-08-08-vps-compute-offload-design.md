# Production VPS Compute Offload Design

**Date:** 2026-08-08  
**Status:** Approved working design  
**Scope:** `D:\Projects\NEW OET WEB APP` and its OET production deployment path

## Goal

Move every OET workload that can safely run away from the production host into
GitHub Actions, while leaving the VPS responsible only for live services,
PostgreSQL data operations, private backup data, image pulls, container
recreation, and health gates.

## Audit findings

The repository and a read-only production snapshot establish these boundaries:

| Workload | Current location | Decision |
| --- | --- | --- |
| Next.js production build and standalone web image | GitHub Actions today; emergency compose scripts can still build on the VPS | Keep the normal path in Actions and fail closed in ordinary VPS source-build helpers. |
| .NET restore/publish and API image | GitHub Actions today; emergency compose scripts can still build on the VPS | Keep the normal path in Actions and fail closed in ordinary VPS source-build helpers. |
| Backup-sidecar image build | Previously depended on a local VPS image | Build and publish `DB_BACKUP_IMAGE` in Actions, then pull it on the VPS. |
| Frontend lint/type-check/unit tests, backend tests, Playwright, mobile/desktop builds, SBOM/SCA, rulebook and speaking checks | GitHub Actions workflows | Keep these off-server; preserve existing caching and matrix/shard patterns. |
| EF Core migration generation | CI has migration-generation helpers, but production schema application is performed by API startup | Generate an idempotent PostgreSQL script in Actions and apply it through a CI-controlled SSH stream before promotion. |
| EF Core `MigrateAsync()` during production API startup | Production API container on the VPS | Remove this production startup execution. Production startup must fail closed when migrations remain pending. Development auto-migration remains available. |
| Nightly OET backup sidecar (`pg_dump`, media archive, GPG, object-storage upload, retention) | VPS, scheduled in the backup container | Keep data-local for now; classify as non-eligible for standard hosted runners because it handles private database/media bytes and is part of recovery independence. Its image is still built off-box. |
| Separate root-cron Google Drive dump (`oet-postgres`, `gzip -9`) | VPS, outside this checkout | Record as a duplicate backup workload. Do not disable it until Google Drive retention and restore parity with the sidecar are proven and owner-approved. |
| PostgreSQL query/DDL execution, ClamAV scanning, request-time OCR/AI/media work, API/web serving, background retention workers | VPS runtime | Keep close to production data and request paths; these are not GitHub Actions build artifacts. |
| Other tenant containers on the shared VPS | Shared host | Inventory-only for this repository; no account-wide or unrelated-project changes. |

The live snapshot showed the OET API blue/green containers using hundreds of MiB
each, ClamAV holding the largest OET memory footprint, and a newly started API
logging EF's "database already up to date" migration check. The backup sidecar
was idle at that instant, while the separate root cron proved that a second
database dump/compression path exists.

## Chosen architecture

### 1. Build and artifact jobs

The existing `deploy.yml` remains the release entry point. It will have three
independent build jobs:

- `build-web`: Build the Next.js standalone image with the existing BuildKit
  GitHub cache.
- `build-api`: Restore/publish the ASP.NET image with the existing BuildKit
  GitHub cache.
- `build-backup`: Build the backup sidecar image with its own cache scope.

Each job publishes an immutable commit-scoped GHCR reference and may also
update the convenience `latest` tag. The deploy job receives the exact
commit-scoped references. The VPS never receives source-build instructions as
part of the normal workflow.

### 2. CI-controlled database migration

The migration job runs after the API image has built and before deployment:

1. Check out the exact commit.
2. Restore the API project on a GitHub-hosted runner.
3. Run `dotnet ef migrations script --idempotent` against the PostgreSQL
   design-time context and save the SQL as a workflow-local artifact.
4. Open the existing SSH deployment connection to the VPS.
5. Run the existing read-only/snapshot pre-flight safety checks remotely,
   including the current backup policy and destructive-migration approval gate.
6. Stream the generated SQL over SSH into `docker exec -i oet-postgres psql`
   with `ON_ERROR_STOP=1`. The runner never receives the production database
   password; the remote command reads the existing production configuration
   without printing it.
7. Fail the workflow if the SQL application fails. The deploy job cannot start
   unless migration application succeeds.

PostgreSQL itself must execute DDL and data changes on the VPS because the
database and its protected volume are local. The expensive migration discovery,
compilation, SQL generation, and client-side transfer are off-server; the
irreducible database execution remains explicitly documented.

Production API startup will no longer call `Database.MigrateAsync()` in the
unconditional PostgreSQL startup block. The existing pending-migration check
and readiness gate remain, so a missed or partial CI migration prevents a new
slot from becoming healthy rather than silently migrating during startup.

### 3. Image-only promotion

The deploy job depends on all three image jobs and the migration job. It passes
`WEB_IMAGE`, `API_IMAGE`, and `DB_BACKUP_IMAGE` to the image-only rollout
script. The rollout must:

- pull the exact GHCR images;
- use `docker compose ... --no-build` for target containers and routers;
- health-gate the inactive blue/green slot;
- switch traffic only after internal and public health checks pass; and
- retain the previous slot for rollback.

The existing shell guard will reject active rollout scripts that contain
`docker compose build`, package builds, or .NET build/test/publish commands.
Emergency source-build helpers remain available only behind the explicit
owner-approved emergency variable and are not part of ordinary deployment.

## Data flow and failure handling

```text
push main
  -> Actions: build web/API/backup images + cache
  -> Actions: generate idempotent EF SQL
  -> SSH: pre-flight safety/backup checks
  -> SSH stdin: psql applies SQL inside oet-postgres
  -> Actions: deploy job pulls GHCR images
  -> VPS: start inactive slot with --no-build
  -> VPS: health gates
  -> VPS: switch routers and public smoke
```

- Build failure: no VPS connection and no production change.
- Migration-generation or migration-application failure: deployment is blocked;
  the currently serving slot remains active.
- New-slot health failure: the router is not switched.
- Public smoke failure after switch: use the existing rollback path to the
  previous slot and previous image references.
- Destructive migration: the existing explicit approval, maintenance-window,
  backup, and restore-drill requirements remain mandatory.
- Backup failure: remains an operational alert and does not get hidden inside
  the application deploy success signal.

## GitHub Actions limits and workarounds

GitHub's current hosted-runner reference lists standard public Linux runners as
4 CPU/16 GB RAM/14 GB SSD and standard private-repository Linux runners as
2 CPU/8 GB RAM/14 GB SSD. Hosted jobs have a six-hour execution limit, a
workflow run has a 35-day limit, and a matrix run can create at most 256 jobs.
See the [hosted-runner reference](https://docs.github.com/en/actions/reference/runners/github-hosted-runners)
and [Actions limits](https://docs.github.com/en/actions/reference/limits).

The implementation uses these mitigations:

- BuildKit `cache-from`/`cache-to` scopes keep web, API, and backup layers
  independent and avoid repeating dependency work.
- Existing QA matrix/shard jobs split backend and browser test pressure across
  runners rather than making the VPS a test host.
- Jobs that need more than standard memory or disk should use a configured
  larger runner; GitHub documents larger 4-core/16-GB/150-GB and larger
  variants in the [larger-runner reference](https://docs.github.com/en/actions/reference/runners/larger-runners).
- Private-network data workflows such as a future backup streamer should use a
  separate hardened self-hosted runner or a managed backup service, not the
  production VPS itself. GitHub's [self-hosted runner guidance](https://docs.github.com/en/actions/reference/runners/self-hosted-runners)
  requires the runner to have the necessary network and hardware access.
- Backup streaming remains out of the hosted-runner path until the storage
  credentials, retention, encryption, and restore drill are explicitly moved
  and proven.

## Verification acceptance

The implementation is complete only when all of these are evidenced:

1. Static workflow and shell checks prove that normal production deployment
   builds/publishes images in Actions and uses `--no-build` on the VPS.
2. A CI migration job generates and applies an idempotent script, and the API
   no longer executes production migrations during startup.
3. The backup sidecar image is present in GHCR and is pulled by deployment.
4. A failed migration or bad target slot leaves the active slot serving.
5. Production API/web health and migration readiness are green after a real
   GitHub Actions deployment.
6. The final report names the two VPS backup jobs, runtime workloads, shared
   tenant boundary, hosted-runner limits, and any live evidence that remains
   owner-side (backup restore drill and provider/device acceptance).

