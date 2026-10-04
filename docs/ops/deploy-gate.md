# Production Deploy Gate

Status: **active** — pipeline-only contract, updated 2026-10-04.

## Approval Owner

- **Dr Faisal Maqsood** is the named approver for every production deploy and
  rollback decision for the v1 launch (single-owner rotation accepted;
  reassess at first hire).

## Pre-Deploy Checklist

The standing owner-approved shipping path requires:

1. Successful **Build images** (`.github/workflows/build-images.yml`) with syntax,
   deployment contracts and applicable real Writing tests green. The release
   manifest binds all four immutable component digests and their successful
   source builds; unchanged components are registry-only aliases, never `:latest`.
2. API SQL from the exact publish compilation (`--no-build`), bound to its source
   SHA/run/tool/checksum. Even a reused API needs SQL when no successful actual
   production release proves that API digest was deployed. Missing or mismatched
   required SQL blocks promotion; production never recompiles.
3. Production environment validation without printing secrets, protected-volume
   invariants, target readiness, durable validated router configuration, public
   web/API health and exact serving-release/image proof.
4. Successful **Deploy production** (`.github/workflows/production-deploy.yml`)
   with actual promotion evidence. A green superseded stand-down is not a deploy.
   Record real run IDs through `pnpm run ax:record`.
5. Pinned SSH host fingerprint. **Not enforced today:** `production-deploy.yml`
   uses `ssh-keyscan` with `StrictHostKeyChecking=accept-new`, and no workflow
   reads a `VPS_SSH_FINGERPRINT` secret. Pinning it is an open owner item.

QA Smoke, SBOM/SCA and operational smoke evidence remain separate quality/incident
tools, not invented prerequisites for the live pipeline. Browser lanes remain
manual-dispatch only. Do not describe an unrun check as passed.

## Deploy Command (current)

Production deploys come from `.github/workflows/build-images.yml` (filtered
build/deployment-input pushes to `main`: components built or immutably reused on
Actions, API SQL generated from the same publish) followed automatically by
`.github/workflows/production-deploy.yml` (apply migrations, then run
`scripts/deploy/auto-deploy-ghcr.sh` on the VPS). A manual dispatch of
`production-deploy.yml` with `-f sha=<sha>` is the rollback path. See
`DEPLOYMENT.md` §3 and `DEPLOY-MANUAL.md`. (The earlier protected `Build Release
Images` / `Deploy Production` workflows were removed in e616c3dcd.)

The active GitHub deploy checkout is `/opt/oetwebapp`. `/root/oetwebsite` is
stale and must not be used. Do not invoke rollout scripts or Compose manually on
the VPS. The maintained driver prepares verified images and the exact target
slot; CI rechecks for a successful descendant build before promoting the same
bound identity. Unchanged healthy services are reused by native configuration
hash and physical image ID. Changed worker/gateway services retain drainage.

The routers use durable directory-mounted rendered configs. Both candidates
must pass nginx validation; graceful pair reload leaves the previous slot warm.
Partial cutover or public health/identity failure restores and reloads both
previous configurations. First deployment or a changed router image/configuration
may recreate routers; recovery retains previous physical images and the last
successful router Compose source (not interpolated credentials), including its
original project directory. No path destroys volumes.

## Deployment timing

The target is 300 seconds from the main push through healthy public serving,
including queues, cold builds, migrations and required Writing checks on the
existing free hosted runners. It is not a guaranteed runner/database deadline.
The shipping wrapper measures conservatively from before the first push attempt
through verified public serving identity, including retries and watcher overhead.
`DEPLOY_LIVE` marks the VPS public health/image observation before optional bounded
cleanup. Report measured misses and unmeasured categories explicitly.
An absent build run is never proof of a no-op: the watcher must bind the
before-push base and match the complete changed range against the actual ordered
workflow path filters. Missing/truncated/expected-input evidence fails explicitly.
Public gates require direct HTTP 200 and exact release/slot identity. Remote
registry authorization is invocation-scoped and cleaned on both success and failure.

Cold-cache dispatch uses `build-images.yml` with `benchmark=true` and
`rebuild_all=true`; it cannot push images or deploy. `writing_gate=true` executes
the real required Writing gate. These are measurement tools, not a second rollout path.

## Post-Deploy Smoke Gate

Within 5 minutes of deploy completion the approver must confirm:

1. `scripts/deploy/post-deploy-verify.sh` exit 0 (web `/api/health` direct
   2xx and API `/health/ready` direct 2xx).
2. `scripts/observability-smoke.sh` against the production base URL with
   `API_BASE_URL` set — exit 0.
3. `scripts/deploy/reading-media-smoke.sh` exits 0: disabled paper mode returns
   no `questionPaperAssets`; protected Reading question-paper media returns 404
   from `/v1/media/{id}/content` and `/v1/media/{id}/url` even if a
   free-preview row exists; enabled paper mode plus entitlement can fetch the
   expected source PDF; legacy `/v1/reading/*` routes return 410.
4. No new entries in the SEV-1 alert channel for the 5-minute window after
   container restart.

## Rollback Trigger (quantitative)

Roll back immediately when **any** of these holds:

- API 5xx error rate > **2% for 5 consecutive minutes** on T0 routes.
- p95 request latency > **2 s for 10 consecutive minutes** on T0 routes.
- web `/api/health` or API `/health/ready` returning non-200 for
   **3 consecutive minutes**.

Other triggers (security disclosure, payment processor outage, evidence
of data loss) escalate to SEV-1 immediately and may bypass the time
windows above at the approver's discretion.

## Rollback Procedure

```powershell
gh workflow run production-deploy.yml -f sha=<previous-deployed-40-character-sha>
```

Use a proven previously deployed release, not merely a successful build.
The pipeline uses the maintained driver with the target release's Compose and
templates; it validates immutable images and serving identity and does not reverse
database migrations. `.deploy/auto-deploy-history.tsv` and
`.deploy/live-release.env` retain the runtime release/slot/image mapping.
Follow the public-before-Actions visibility lease in `AGENTS.md`; watch the
rollback run and verify actual promotion plus live health.

Before rollback or hotfix deploys, run `scripts/deploy/pre-flight.sh` to record
a database snapshot when the host is stable enough. If Reading media policy is
suspect, flip `ReadingPolicy.AllowPaperReadingMode=false` until the
Reading/media smoke checks above pass.

If rollback itself fails, page the incident commander
(`docs/ops/incident-response-runbook.md`) and follow SEV-1 containment.

## Backups & Restore Drill

- Postgres volume `oetwebsite_oet_postgres_data` is the database source of
   truth. Learner-uploaded media lives in `oetwebsite_oet_learner_storage`, and
   encrypted dumps live in `oetwebsite_oet_db_backups`.
- A manual restore drill from a recent `pg_dump` snapshot must be performed
   at least once per quarter; record the timestamp and the operator in the
   release/deploy notes.
- Destructive migrations require `DESTRUCTIVE_MIGRATION_APPROVAL`,
  `DESTRUCTIVE_MIGRATION_MAINTENANCE_WINDOW`,
  `DESTRUCTIVE_MIGRATION_BACKUP_ID`, and
  `DESTRUCTIVE_MIGRATION_RESTORE_DRILL_ID` before pre-flight proceeds.

## Production Mock / Stub Enforcement

`scripts/deploy/mock-stub-scan.sh` rejects `.env.production` values and the
rendered production Compose config when they contain `mock`, `stub`, `noop`, or
`__placeholder__` markers. The approver must also confirm no in-process mock
provider is selected before deploy (admin AI config console +
`ConversationOptions.AsrProvider` / `PronunciationOptions.Provider` must not be
`mock`).

## Cross-References

- `DEPLOYMENT.md` — full host/network/storage layout.
- `docs/PROD-SMOKE-RUNBOOK.md` — smoke procedure detail.
- `docs/ops/incident-response-runbook.md` — SEV severity ladder.
- `docs/ops/observability-slo-checklist.md` — SLOs that drive the
  rollback trigger thresholds above.
