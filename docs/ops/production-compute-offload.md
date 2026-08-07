# Production compute boundary

This is the OET-only audit of the shared production VPS and its release path.
The repository/workflow inventory and a read-only VPS snapshot were checked on
2026-08-08. Other tenants on the host were inventoried only; this change does
not alter their containers, schedules, or storage.

## Audit findings

The host snapshot reported 6 CPUs, about 11 GiB RAM, and about 9.8 GiB swap.
During the snapshot the OET API startup process reached roughly 40% CPU while
the new slot booted. Representative container readings were approximately
338 MiB for `oet-api-green`, 482 MiB for `oet-api-blue`, 225 MiB for the web
slot, 710 MiB for ClamAV, and 213 MiB for PostgreSQL. No `pg_dump`, archive, or
encryption process was active at the instant of the snapshot; the backup
container was idle between scheduled runs. These are point-in-time readings,
not resource limits or capacity guarantees.

| Workload observed | Execution after this change | Decision and evidence |
| --- | --- | --- |
| Next.js/Turbopack web bundling and Docker packaging | GitHub Actions `build-web` | The web image is built and pushed to GHCR off-server. |
| .NET restore, compile, publish, and API Docker packaging | GitHub Actions `build-api` | The production VPS no longer needs a source tree or compiler. |
| Backup-sidecar Docker image packaging | GitHub Actions `build-backup` | The VPS pulls a commit-scoped GHCR image; it does not build the sidecar. |
| TypeScript, lint, unit, backend, E2E, mobile, performance, security, and SBOM jobs | Existing GitHub Actions workflows | These jobs already run on hosted runners; the deploy path does not invoke them on the VPS. |
| EF migration compilation and SQL generation | GitHub Actions `migrate-production` | An idempotent SQL script is generated off-server, retained for three days, and streamed to the VPS without production data. |
| EF migration application | Existing PostgreSQL container, invoked by Actions | PostgreSQL must execute against its local production volume. The stdin wrapper runs only `psql -v ON_ERROR_STOP=1`; it performs no build, test, or Compose operation. |
| API startup `Database.MigrateAsync()` | Development only when explicitly enabled | Production startup no longer applies migrations. The production bootstrap/readiness path still fails closed when pending migrations exist. |
| Scheduled database/media backup (`pg_dump`, compression, optional GPG/S3, and media archive) | VPS backup sidecar | Moving the dump would require moving or exposing the private database and persistent media volume. Keep this data-local until a separately secured managed backup path and restore drill are proven. |
| Root-cron Google Drive database backup (`pg_dump` + `gzip` + `rclone`) | VPS, unchanged | This is a second retention/destination policy. Do not disable it or the sidecar without restore-parity evidence. |
| Runtime OCR/PDF extraction, TTS/audio workers, content imports, AI calls, queue workers, and scheduled billing/retention work | OET API/runtime services | These are request- or queue-driven and operate on live user data, provider credentials, or persistent state. Ephemeral hosted Actions runners are not a safe replacement. A dedicated managed or isolated self-hosted worker is the future option if this workload needs to move. |
| PostgreSQL, ClamAV, image pulls, blue/green routing, and health gates | VPS | These are stateful/runtime operations required to serve traffic. |
| Unrelated tenant containers and host schedules | Out of scope | Inventory only; no account-wide or cross-tenant change was made. |

## Deployment contract

`.github/workflows/deploy.yml` now gates deployment on `build-web`, `build-api`,
`build-backup`, and `migrate-production`. Only after all four succeed does the
deploy job SSH to production, stream a small deployment bundle, pull
commit-scoped GHCR images, and run the blue/green rollout. It does not fetch or
reset the source repository on the VPS.

The rollout requires `WEB_IMAGE`, `API_IMAGE`, and `DB_BACKUP_IMAGE`, persists
their references, pulls them with retry, and passes `--no-build` to every
Compose start or router switch. `verify-compute-offload.sh` and
`verify-image-only-rollout.sh` are static guards for this contract. The legacy
source-build helpers exit with code 78 unless the owner explicitly supplies
`ALLOW_VPS_SOURCE_BUILD=owner-approved-emergency`; that exception is not part
of the normal production workflow.

The migration job uploads only the generated schema SQL as a short-retention
audit artifact. The production database password and `.env.production` stay on
the VPS. The temporary mode-700 wrapper is streamed to `/tmp`, consumes SQL on
stdin, and is removed after the SSH attempt. Protected PostgreSQL and media
volumes are not recreated, removed, or passed through a hosted runner.

## GitHub Actions limits and workarounds

GitHub-hosted standard Linux runners are finite: the documented baseline is 4
vCPUs/16 GB RAM for public repositories and 2 vCPUs/8 GB RAM for private
repositories, with an ephemeral workspace. A hosted job has a 6-hour maximum,
a workflow run has a 35-day maximum, and a matrix is capped at 256 jobs. Check
the current primary documentation before relying on a plan or runner size:

- [GitHub-hosted runners](https://docs.github.com/en/actions/reference/runners/github-hosted-runners)
- [Actions limits](https://docs.github.com/en/actions/reference/limits)
- [Larger runners](https://docs.github.com/en/actions/reference/runners/larger-runners)
- [Self-hosted runners](https://docs.github.com/en/actions/reference/runners/self-hosted-runners)

The current mitigation is to build web/API/backup images in separate jobs,
reuse BuildKit `type=gha` caches, retain only a short-lived migration artifact,
and keep test fan-out in existing matrix/shard workflows. If a job outgrows a
standard runner, split it into independent matrix shards and pass only small
artifacts between jobs; use a billed larger runner for a genuinely memory-bound
single build; or use a dedicated isolated self-hosted runner for trusted,
long-running workloads. Do not put live production database/media credentials
on a generic hosted runner. For backups, prefer a managed backup service or a
dedicated private runner with network and retention controls rather than
streaming production data into a normal CI job.

## Recovery and remaining boundary

An image failure is health-gated before router cutover; the previous blue/green
slot remains available for rollback. Database migrations are forward-only in
this workflow: take the existing backup path into account before approving a
destructive schema change, and keep the documented restore-drill/maintenance
approval process as an operator gate. This implementation proves compute
offload and image-only deployment; it does not prove a fresh backup restore,
managed-backup replacement, or authenticated learner/browser acceptance.
