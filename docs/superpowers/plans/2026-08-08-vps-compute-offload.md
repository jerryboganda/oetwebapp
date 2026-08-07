# Production VPS Compute Offload Implementation Plan

> For agentic workers: REQUIRED SUB-SKILL: Use superpowers:executing-plans (recommended) to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Move eligible OET build, test, image-packaging, and migration-generation work from the production VPS to GitHub Actions, leaving the VPS with image pulls, database execution, backups, runtime services, and health gates only.

**Architecture:** Extend the existing '.github/workflows/deploy.yml' with a cached backup-image build and a production migration job. The migration job waits for all pre-built images, builds an idempotent EF PostgreSQL script on 'ubuntu-latest', then streams it through SSH to a small VPS-side 'psql' wrapper; the deploy job runs only after all image and migration gates pass and uses '--no-build' blue/green rollout.

**Tech Stack:** GitHub Actions YAML, Docker Buildx/GHCR, Bash, .NET 10/EF Core 10, PostgreSQL 'psql', ASP.NET Core Minimal API, existing blue/green Docker Compose deployment.

## Global Constraints

- Do not run frontend, API, backend, Next.js, or .NET builds/tests/publish on the production VPS; normal production deploys must use GitHub Actions-built images.
- All production media/user files remain under '/var/opt/oet-learner/storage' and protected Docker volumes must not be recreated or removed.
- Do not read, print, commit, or edit '.env*', credentials, SSH keys, database passwords, or customer data.
- Keep the existing backup sidecar and root-cron Google Drive backup until retention and restore parity are proven; do not make an account-wide or unrelated-tenant change.
- Preserve existing dirty user files and stage only explicit implementation paths.
- Use the existing 'main' push deployment path and report exactly which local checks, CI gates, and live health boundaries were verified.

---

## File Map

**Create:**

- 'scripts/deploy/apply-migrations-from-ci.sh' — reads an idempotent SQL stream from stdin and applies it through the existing PostgreSQL container without exposing production credentials.
- 'scripts/deploy/verify-compute-offload.sh' — static CI guard proving migration generation is in Actions and the active VPS rollout is image-only.
- 'docs/ops/production-compute-offload.md' — operator-facing audit, workload classification, backup boundary, GitHub Actions limits, and recovery notes.

**Modify:**

- '.github/workflows/deploy.yml' — add the migration generation/application gate, wire the backup image build, and make deployment depend on all off-server gates.
- 'backend/src/OetLearner.Api/Program.cs:1887-1917' — stop applying PostgreSQL migrations during production API startup while preserving development auto-migration and the existing readiness pending-migration check.
- 'backend/tests/OetLearner.Api.Tests/ProductionReadinessTests.cs' or a focused migration-policy test — prove production startup does not silently migrate and that the production profile remains fail-closed for pending schema state.
- 'scripts/deploy/auto-deploy-ghcr.sh' — require and pull 'DB_BACKUP_IMAGE', persist its commit-scoped reference, and retain '--no-build' for every Compose operation.
- 'scripts/deploy/verify-image-only-rollout.sh' — assert that the active rollout uses no source build/test/publish commands and requires '--no-build'.
- 'scripts/deploy-production.sh' and 'scripts/deploy/deploy-direct.sh' — preserve the explicit owner-approved emergency exception around legacy source-build paths.
- 'docker-compose.production.yml' — keep the backup service image-only and confirm 'DB_BACKUP_IMAGE' is the production image variable.
- '.github/agent-state.local.md' — prepend the completed checkpoint, changed paths, validation evidence, remaining live boundary, and next step without deleting existing handoff entries.

## Task 1: Add the safe stdin migration applicator

**Files:**

- Create: 'scripts/deploy/apply-migrations-from-ci.sh'
- Test: 'scripts/deploy/verify-compute-offload.sh'

**Interfaces:**

- Consumes: SQL on standard input; '.env.production' keys 'POSTGRES_USER' and 'POSTGRES_DB'; the running 'oet-postgres' container.
- Produces: exit code 0 only when 'psql -v ON_ERROR_STOP=1' applies the complete stream; no secret values in stdout/stderr.

- [x] Step 1: Write the stdin wrapper.

Implement strict Bash mode and a 'read_env_value' helper matching the existing deploy scripts. Require the app directory, verify that the SQL stream is non-empty, resolve only the database user/name, and invoke 'docker exec -i oet-postgres psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"'. Do not echo the connection string or any environment value. Preserve the production database volume and do not run Compose build/down commands.

- [x] Step 2: Add the static offload guard.

Make 'verify-compute-offload.sh' assert that:

    .github/workflows/deploy.yml contains 'dotnet ef migrations script --idempotent'
    .github/workflows/deploy.yml invokes 'apply-migrations-from-ci.sh'
    .github/workflows/deploy.yml deploy needs include the migration job
    scripts/deploy/auto-deploy-ghcr.sh contains '--no-build'
    scripts/deploy/auto-deploy-ghcr.sh contains no active package/build/test/publish command

Use 'grep' only against tracked source paths and fail with an actionable message.

- [x] Step 3: Run shell syntax checks.

Run 'bash -n scripts/deploy/apply-migrations-from-ci.sh scripts/deploy/verify-compute-offload.sh scripts/deploy/verify-image-only-rollout.sh scripts/deploy/auto-deploy-ghcr.sh'. Expected: exit 0 with no syntax errors.

## Task 2: Remove production API-side migration execution

**Files:**

- Modify: 'backend/src/OetLearner.Api/Program.cs:1887-1917'
- Test: 'backend/tests/OetLearner.Api.Tests/ProductionReadinessTests.cs' or a new focused migration-policy test beside the existing production readiness tests.

**Interfaces:**

- Consumes: 'BootstrapOptions.AutoMigrate', 'IHostEnvironment', and the existing EF database provider.
- Produces: development/test compatibility with existing auto-migration behavior; production startup never calls 'Database.MigrateAsync()' as a deployment mechanism and fails readiness when pending migrations exist.

- [x] Step 1: Add a focused policy seam or deterministic production assertion.

Use the smallest testable seam consistent with the current top-level 'Program.cs' style. The test must distinguish Production plus PostgreSQL plus AutoMigrate=false from Development plus AutoMigrate=true, and must not require a production database or credentials.

- [x] Step 2: Remove only the production 'MigrateAsync()' path.

Change the unconditional Npgsql startup block so production does not apply migrations. Keep the existing development auto-migration path through 'DatabaseBootstrapper.InitializeAsync', keep the bounded runtime-settings self-heal only if required for development compatibility, and retain the production pending-migration readiness check. Do not weaken fail-closed 'health/ready' behavior.

- [x] Step 3: Run the focused backend test.

Run 'dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj --filter "FullyQualifiedName~DatabaseMigrationExecutionPolicyTests" --nologo'. Expected: focused migration-policy tests pass, or any pre-existing host stall is reported without claiming a pass.

## Task 3: Add the Actions migration gate

**Files:**

- Modify: '.github/workflows/deploy.yml'
- Modify: 'scripts/deploy/apply-migrations-from-ci.sh'
- Modify: 'scripts/deploy/verify-compute-offload.sh'

**Interfaces:**

- Consumes: exact checked-out commit, .NET 10, EF design-time context, existing PROD_SSH_KEY, VPS host/path, and target commit SHA.
- Produces: a non-empty idempotent SQL script generated off-server; a failed migration blocks deploy; no database secret enters GitHub logs or workflow variables.

- [x] Step 1: Add the migration job after the API image job.

Add 'migrate-production' with 'needs: [build-web, build-api, build-backup]' and 'runs-on: ubuntu-latest'. Check out the exact commit, install .NET '10.0.x', install 'dotnet-ef' '10.0.5', restore and build 'backend/src/OetLearner.Api/OetLearner.Api.csproj', then run one-line 'dotnet ef migrations script --idempotent --no-build --configuration Release --project backend/src/OetLearner.Api/OetLearner.Api.csproj --startup-project backend/src/OetLearner.Api/OetLearner.Api.csproj --output output/oet-production-migrations.sql'. Assert the file is non-empty, print only its SHA-256, and upload it as a short-retention workflow artifact without production data.

- [x] Step 2: Apply the generated SQL through the existing SSH path.

Use the same key creation, host-key handling, and SSH options as the existing deploy job. Because the wrapper is new and the VPS still has the previous commit, first stream 'scripts/deploy/apply-migrations-from-ci.sh' to a mode-700 file under '/tmp' on the VPS, then run that temporary wrapper with the generated SQL on standard input using 'ssh ... root@host "bash /tmp/oet-apply-migrations-from-ci.sh" < output/oet-production-migrations.sql'. Remove the temporary file after a successful or failed attempt. Keep the remote command free of dotnet, pnpm, npm, Docker build, and test operations. If the pre-flight safety gate requires explicit destructive-migration approval, fail before psql and do not deploy.

- [x] Step 3: Make deployment depend on migration success.

Set 'deploy.needs' to '[build-web, build-api, build-backup, migrate-production]'. Keep the existing serialized concurrency group. Add migration output and status to the job summary without printing secrets or SQL contents.

- [x] Step 4: Run the static workflow guard.

Run 'bash scripts/deploy/verify-compute-offload.sh'. Expected: all offload assertions pass.

## Task 4: Complete pre-built image-only deployment

**Files:**

- Modify: '.github/workflows/deploy.yml'
- Modify: 'scripts/deploy/auto-deploy-ghcr.sh'
- Modify: 'scripts/deploy/verify-image-only-rollout.sh'
- Modify: 'docker-compose.production.yml' only if the existing 'DB_BACKUP_IMAGE' image/pull contract needs alignment.
- Preserve: 'scripts/deploy-production.sh', 'scripts/deploy/deploy-direct.sh', and the existing untracked guard until explicitly staged as part of this task.

**Interfaces:**

- Consumes: commit-scoped 'DB_BACKUP_IMAGE' GHCR reference.
- Produces: GHCR web/API/backup images and a VPS rollout that only pulls and starts them with '--no-build'.

- [x] Step 1: Verify the backup image job.

Keep the existing Buildx cache scope 'db-backup', push commit-scoped and convenience tags, and include 'build-backup' in the deploy 'needs' list.

- [x] Step 2: Wire the backup image into the remote rollout.

Require 'DB_BACKUP_IMAGE', persist it in '.env.production' alongside web/API refs without printing secrets, call 'pull_with_retry' for it, and include 'db-backup' in the target-slot 'up -d --no-build --force-recreate' call.

- [x] Step 3: Prove the active rollout is image-only.

Run the guard against the checked-out scripts and ensure legacy source-build helpers exit 78 unless 'ALLOW_VPS_SOURCE_BUILD=owner-approved-emergency' is explicitly supplied. Do not run those helpers on production.

- [x] Step 4: Run static Compose and shell checks.

Run 'bash -n scripts/deploy/auto-deploy-ghcr.sh scripts/deploy/verify-image-only-rollout.sh'. Use a non-secret Compose config inspection only if required; never substitute production '.env' values into logs.

## Task 5: Publish the operator audit and limits

**Files:**

- Create: 'docs/ops/production-compute-offload.md'
- Modify: '.github/agent-state.local.md'

**Interfaces:**

- Consumes: repository workflow inventory and the dated read-only VPS snapshot already collected.
- Produces: an operator-readable record of what moved, what remains on-host, why backups are excluded, runner limitations/workarounds, and exact evidence boundaries.

- [x] Step 1: Write the workload table.

Document web/API/backup image builds, lint/tests/E2E/mobile builds, EF migration generation, API startup migration removal, backup sidecar, root-cron Google Drive backup, ClamAV/PostgreSQL/runtime workers, and unrelated tenant containers.

- [x] Step 2: Document Actions limits and mitigations.

Link the current GitHub primary documentation and record standard runner CPU/RAM/disk, hosted job and workflow time limits, matrix limit, BuildKit caches, test sharding, larger runners, and the separate self-hosted-runner/managed-backup recommendation.

- [x] Step 3: Update the handoff.

Prepend a concise checkpoint to '.github/agent-state.local.md' naming changed files, focused validation, CI/deploy evidence, and the remaining backup restore-drill boundary. Preserve all existing entries below it.

## Task 6: Focused validation and production handoff

**Files:**

- Test: '.github/workflows/deploy.yml', 'scripts/deploy/*.sh', 'backend/src/OetLearner.Api/Program.cs', focused backend tests, and the operator doc.

- [x] Step 1: Run one lightweight local validation set.

Run the static guards, shell syntax checks, 'git diff --check', and the focused production-readiness test. Do not run a full local build/test marathon; GitHub Actions remains authoritative for full image and CI gates.

- [ ] Step 2: Review the staged diff.

Use 'git diff --cached --check' and inspect only explicit task paths. Confirm no '.env*', secrets, generated SQL containing production data, or unrelated dirty files are staged.

- [ ] Step 3: Commit the implementation.

Stage explicit task paths and commit with 'ci: offload production compute to GitHub Actions'. Do not include unrelated admin/UI/API changes or '.codex/config.toml' and '.superpowers/'.

- [ ] Step 4: Push and monitor the exact main commit.

Push 'main', identify the Build & Deploy run for the exact SHA, and wait for image builds, migration application, deployment, and health gates. A push alone is not deployment evidence.

- [ ] Step 5: Verify production boundaries.

Confirm production web '/api/health' and API '/health/ready', confirm API logs no longer show startup-applied migrations, and confirm deployed containers carry the GHCR image refs. Do not claim backup restore or authenticated learner/device acceptance without owner-side evidence.
