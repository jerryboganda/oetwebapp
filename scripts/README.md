# scripts/

Index by purpose. A script with no references is not automatically dead: check the
audit-trail list before deleting anything. Put one-off scratch in `scripts/_*` or
`scripts/admin/_*` (gitignored), never at the top level.

## 1. Deploy / CI-critical (do not rename or move without updating the callers)

- `deploy/` — **deploy-critical.** `auto-deploy-ghcr.sh` (the live blue/green rollout run by
  `deploy.yml`), `apply-migrations-from-ci.sh`, `validate-production-env.sh`, `prune-stale-images.sh`,
  `nginx/*.template`, the static guards `verify-compute-offload.sh` / `verify-image-only-rollout.sh`,
  and `deploy-prod.sh` (+ `rollout-release.sh`, `pre-flight.sh`): the manual digest-pinned incident path.
  `deploy-direct.sh` and `../deploy-production.sh` are owner-gated emergency source-build fallbacks
  (`ALLOW_VPS_SOURCE_BUILD`); `verify-compute-offload.sh` asserts that gate.
- `observability-smoke.sh` — **deploy-critical**, called by `deploy/rollout-release.sh`.
- `backup/` — **deploy-critical**, the db-backup sidecar image (compose).
- `ship/` — pre-push gate and deploy watcher (`pnpm ship:gate`, `pnpm ship:watch`).
- `apple/`, `release/`, `macos-acceptance/`, `tauri-*.cjs` — native build and release workflows.
- `qa/`, `perf/` — Playwright matrix, local-stack assert, a11y sign-off, perf summaries.
- `listening/` — Listening audio verify/repair workflows (also an audit trail, see 4).
- `rulebooks/build-canonical-writing-rulebooks.mjs` — Writing rulebook build; CI runs it with `--check`.
- `sbom-generate.sh`, `sca-scan.sh` — supply-chain workflows.
- `ops/create-owner-agent-db-role.sql` — Owner Agent Console DB role (agent-console tests pin it).

## 2. Dev tooling (local only, never on the VPS)

- `start-local-prod.ps1` (local stack against the live DB). Root `start-dev.ps1` (Podman DB + API,
  native Next.js) and `sync-prod-db.sh` (prod dump into the local Postgres) are the hotreload stack.
- `check-mojibake.mjs`, `ts-prune-filter.mjs`, `repomix/`, `refresh-autoskills.ps1`, `mobile-view/`.
- `agent/state.mjs` — the agent working-memory ledger (`pnpm run ax:check|status|next|init|record|verify`).
  Static file reads plus read-only `gh` calls only; see `agent/README.md` and `AGENTS.md` for the
  compute-locality rule before extending it.
- Smokes: `speaking-smoke.*`, `seed-speaking-dev.*`, `writing-v2-smoke.sh`, `probe-production.ps1`,
  `test-cf-gateway.ps1`, `test-cf-workers-ai.ps1`, `antigravity/` (`pnpm ai:*`).
- Content tooling: `admin/` (Reading manifest import/validate), `writing-qa-export.mjs`,
  `migrate-content.mjs`, `extract_writing_text.py` + `batch-ocr.ps1`, `generate-all-app-icons.py`,
  `maintenance/reading-pdf-reset.ps1` (guarded, dry-run default),
  `ai-learning-companion/validate_traceability.py`.

## 3. VPS host cron (run from the VPS copy, not from CI)

- `db-nightly-backup-gdrive.sh`, `db-weekly-audit.sh`, `db-weekly-audit-with-alerts.sh`.
  The weekly audit reads `scripts/db-audit.sql`, which is gitignored and exists only on the VPS.

## 4. AUDIT TRAIL — do not delete (provenance of applied production data changes / seeds)

- `ops/*.sql`, `ops/fresh-start-learner-reset.sh` — INC-2026-CLAUDE-01 (cited by the postmortem,
  a migration and `ListeningPartAAiRetryPolicy`), W11 vocabulary merge + unique index (cited by a
  migration and `VocabularyMergeReportService`), W12 probes, AI ledger reconcile, June 2026 learner reset.
- `videos/` — Bunny upload, July 2026 language/profession retag, collection merges.
- `materials/` — materials ingest, `prod-manifest.txt`, the 20-test Listening split that produced
  `test_audio_boundaries_computed.json`.
- `listening/` — September 2026 Listening audio repair.
- `build-recalls-2023-2025-json.py`, `extract-writing-pdfs/`, `import-speaking-cards.mjs` — seed and
  corpus sources (paths inside may point at old local clones).
- `conformance/tag-*-rulebooks.mjs` — rulebook enforcement taggers.
