# Verification Ledger

Append-only. Every row is machine-written by `pnpm run ax:record` from GitHub Actions
(`gh run list` / `gh run view`). Never hand-edit a Result — re-check it with
`pnpm run ax:verify`. A claim with no run id is not evidence.

| Date (UTC) | Claim / gate | Workflow | Run | Job(s) | Conclusion | SHA |
| --- | --- | --- | --- | --- | --- | --- |
| 2026-10-03 14:52 | CI triage | CI triage | 37131219592 | Classify the failed run (jev-1.13.0)=skipped | SKIPPED | 8c7f2b189 |
| 2026-10-02 02:01 | AX Ledger Tools | AX Ledger Tools | 36953672160 | self-test (windows, node 22)=success self-test (linux, node 22)=success self-test (windows, node 24)=success | SUCCESS | 333e92aa5 |
| 2026-10-02 02:01 | SBOM and SCA | SBOM and SCA | 36953672111 | sbom-sca=success | SUCCESS | 333e92aa5 |
| 2026-10-02 02:01 | Speaking Module CI | Speaking Module CI | 36953672131 | secrets-scan=success migrations-check=success | SUCCESS | 333e92aa5 |
| 2026-10-02 02:01 | QA Smoke | QA Smoke | 36953672205 | E2E smoke (shard 1/13 · chromium-learner)=cancelled Backend tests (.NET) · shard 4/6=cancelled E2E smoke (shard 1/13 · webkit-learner)=cancelled E2E smoke (shard 1/13 · chromium-admin)=cancelled E2E smoke (shard 1/13 · webkit-expert)=success Frontend unit (vitest + lint + tsc + build)=cancelled E2E smoke (shard 1/13 · firefox-expert)=cancelled E2E smoke (shard 1/13 · mobile-chromium-learner)=cance | CANCELLED | 333e92aa5 |
| 2026-10-02 02:01 | Build & Deploy (web + API) | Build & Deploy (web + API) | 36953672231 | writing-regression-gate=success syntax-gate=success build-web=success build-agent-gateway=success build-backup=success build-api=success migrate-production=success deploy=success | SUCCESS | 333e92aa5 |
| 2026-10-02 01:43 | AX Ledger Tools | AX Ledger Tools | 36952304624 | self-test (windows, node 22)=success self-test (linux, node 22)=success self-test (windows, node 24)=success | SUCCESS | 31e4c4540 |
| 2026-10-02 01:26 | AX Ledger Tools | AX Ledger Tools | 36950958768 | self-test (windows, node 22)=success self-test (windows, node 24)=success self-test (linux, node 22)=success | SUCCESS | e27b55dbf |
