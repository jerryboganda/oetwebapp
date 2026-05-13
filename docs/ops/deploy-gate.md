# Production Deploy Gate

Status: **active** — adopted 2026-05-10 to close `RW-016` against
`docs/STATUS/remaining-work.yaml`.

## Approval Owner

- **Dr Faisal Maqsood** is the named approver for every production deploy and
  rollback decision for the v1 launch (single-owner rotation accepted;
  reassess at first hire).

## Pre-Deploy Checklist

Every production deploy must, at minimum, attach:

1. Green CI run from the deploying SHA (lint, type-check, unit, backend
   tests, production build) — see `.github/workflows`.
2. Latest SBOM + SCA bundle from `scripts/evidence-collect.sh` with
   `scripts/evidence-verify.sh` exit 0. Production mode requires
   `checksums.sha256`, detached `checksums.sha256.asc`, the expected signer
   fingerprint from the protected GitHub `production` environment, a `git_sha`
   matching the deployed `HEAD`, and checksum-covered accepted-risk notes.
3. Pre-flight script success: `scripts/deploy/pre-flight.sh` against the
   target host.
4. `.env.production` validation — no missing keys, no `__placeholder__`
   values. The deploy/pre-flight scripts run
   `scripts/deploy/validate-production-env.sh` without printing secrets.
5. Production mock/stub scan success from `scripts/deploy/mock-stub-scan.sh`.
6. Approver acknowledgement (Dr Faisal Maqsood) recorded in the deploy
   commit message or release notes.
7. Pinned SSH host fingerprint configured as `VPS_SSH_FINGERPRINT` on the
   protected GitHub `production` environment.

## Deploy Command (current)

```bash
ssh root@185.252.233.186
cd /opt/oetwebapp
bash ./scripts/deploy/deploy-prod.sh
```

The active GitHub deploy checkout is `/opt/oetwebapp`. `/root/oetwebsite` is
stale and must not be used for builds. The deploy gate preserves the signed
evidence bundle, verifies that evidence against the deployed SHA, runs
pre-flight, deploy, post-deploy verification, observability smoke, and the
Reading/media smoke gate. The deploy driver builds services sequentially
(`COMPOSE_PARALLEL_LIMIT=1` by default) to avoid OOM on the 8 GB VPS and never
runs volume-destructive commands.

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

```bash
ssh root@185.252.233.186
cd /opt/oetwebapp
# Identify last good tag/sha
git log --oneline -10
# Roll back through the gated wrapper. The evidence bundle must be signed for
# the selected SHA.
DEPLOY_REF=<previous-good-sha> bash ./scripts/deploy/deploy-prod.sh
# Verify
scripts/deploy/post-deploy-verify.sh
```

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
  at least once per quarter; record the timestamp and the operator in
  `docs/release/evidence-checklist.md`.

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
- `docs/STATUS/remaining-work.yaml` — canonical register; this file
  closes `RW-016`.
