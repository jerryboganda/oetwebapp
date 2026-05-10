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
   `scripts/evidence-verify.sh` exit 0 (production mode requires a signed
   `checksums.sha256`).
3. Pre-flight script success: `scripts/deploy/pre-flight.sh` against the
   target host.
4. `.env.production` validation — no missing keys, no `__placeholder__`
   values.
5. Approver acknowledgement (Dr Faisal Maqsood) recorded in the deploy
   commit message or release notes.

## Deploy Command (current)

```bash
ssh root@185.252.233.186
cd /root/oetwebsite
git fetch origin && git reset --hard origin/main
docker compose --env-file .env.production -f docker-compose.production.yml up -d --build
```

## Post-Deploy Smoke Gate

Within 5 minutes of deploy completion the approver must confirm:

1. `scripts/deploy/post-deploy-verify.sh` exit 0 (web container healthy,
   API container healthy, `/api/health` returning 200).
2. `scripts/observability-smoke.sh` against the production base URL with
   `API_BASE_URL` set — exit 0.
3. No new entries in the SEV-1 alert channel for the 5-minute window after
   container restart.

## Rollback Trigger (quantitative)

Roll back immediately when **any** of these holds:

- API 5xx error rate > **2% for 5 consecutive minutes** on T0 routes.
- p95 request latency > **2 s for 10 consecutive minutes** on T0 routes.
- `/api/health` returning non-200 for **3 consecutive minutes**.

Other triggers (security disclosure, payment processor outage, evidence
of data loss) escalate to SEV-1 immediately and may bypass the time
windows above at the approver's discretion.

## Rollback Procedure

```bash
ssh root@185.252.233.186
cd /root/oetwebsite
# Identify last good tag/sha
git log --oneline -10
# Roll back
git reset --hard <previous-good-sha>
docker compose --env-file .env.production -f docker-compose.production.yml up -d --build
# Verify
scripts/deploy/post-deploy-verify.sh
```

If rollback itself fails, page the incident commander
(`docs/ops/incident-response-runbook.md`) and follow SEV-1 containment.

## Backups & Restore Drill

- Postgres volume `oetwebsite_oet_postgres_data` is the source of truth.
- A manual restore drill from a recent `pg_dump` snapshot must be performed
  at least once per quarter; record the timestamp and the operator in
  `docs/release/evidence-checklist.md`.

## Production Mock / Stub Enforcement

`scripts/evidence-verify.sh` rejects bundles that include `mock`, `stub`,
or `__placeholder__` markers in any production-tagged image manifest.
The approver must confirm no in-process mock provider is selected before
deploy (admin AI config console + `ConversationOptions.AsrProvider` /
`PronunciationOptions.Provider` must not be `mock`).

## Cross-References

- `DEPLOYMENT.md` — full host/network/storage layout.
- `docs/PROD-SMOKE-RUNBOOK.md` — smoke procedure detail.
- `docs/ops/incident-response-runbook.md` — SEV severity ladder.
- `docs/ops/observability-slo-checklist.md` — SLOs that drive the
  rollback trigger thresholds above.
- `docs/STATUS/remaining-work.yaml` — canonical register; this file
  closes `RW-016`.
