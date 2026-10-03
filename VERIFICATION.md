# Verification Ledger

Append-only. Every row is machine-written by `pnpm run ax:record` from GitHub Actions
(`gh run list` / `gh run view`). Never hand-edit a Result — re-check it with
`pnpm run ax:verify`. A claim with no run id is not evidence.

| Date (UTC) | Claim / gate | Workflow | Run | Job(s) | Conclusion | SHA |
| --- | --- | --- | --- | --- | --- | --- |
| 2026-10-03 19:40 | CI triage | CI triage | 37148762001 | Classify the failed run (jev-1.13.0)=skipped | SKIPPED | dcaa45207 |
| 2026-10-03 19:40 | AX Ledger Tools | AX Ledger Tools | 37148758479 | self-test (windows, node 24)=success self-test (linux, node 22)=success self-test (windows, node 22)=success | SUCCESS | dcaa45207 |
| 2026-10-03 19:11 | CI triage | CI triage | 37146989721 | Classify the failed run (jev-1.13.0)=skipped | SKIPPED | c8d7a570e |
| 2026-10-03 19:09 | CI triage | CI triage | 37146856878 | Classify the failed run (jev-1.13.0)=success | SUCCESS | c8d7a570e |
| 2026-10-03 19:07 | CI triage | CI triage | 37146777214 | Classify the failed run (jev-1.13.0)=skipped | SKIPPED | c8d7a570e |
| 2026-10-03 19:07 | Deploy production | Deploy production | 37146777264 | Resolve the build to deploy=success Roll out to the VPS=success Apply migration SQL (if the API changed)=skipped | SUCCESS | c8d7a570e |
| 2026-10-03 19:04 | Jev calibrate | Jev calibrate | 37146568822 | Jev live calibration (jev-1.13.0)=failure | FAILURE | c8d7a570e |
| 2026-10-03 19:04 | Build images | Build images | 37146567088 | Syntax gate (seconds)=success Deployment contract guards=success Detect what changed=success Writing model-answer gate (on change)=skipped Writing grader regression (on change)=skipped build-agent-gateway=success Retag unchanged web=success build-web=success build-backup=success Retag unchanged api=success build-api=skipped | SUCCESS | c8d7a570e |
| 2026-10-03 19:04 | Speaking Module CI | Speaking Module CI | 37146567146 | secrets-scan=success migrations-check=success | SUCCESS | c8d7a570e |
| 2026-10-03 18:37 | CI triage | CI triage | 37144878460 | Classify the failed run (jev-1.13.0)=success | SUCCESS | ca69764bc |
| 2026-10-03 18:32 | CI triage | CI triage | 37144560414 | Classify the failed run (jev-1.13.0)=skipped | SKIPPED | b9df2ec6d |
| 2026-10-03 18:32 | AX Ledger Tools | AX Ledger Tools | 37144557240 | self-test (linux, node 22)=success self-test (windows, node 22)=success self-test (windows, node 24)=success | SUCCESS | b9df2ec6d |
| 2026-10-03 18:25 | CI triage | CI triage | 37144115145 | Classify the failed run (jev-1.13.0)=success | SUCCESS | 9ffb41d34 |
| 2026-10-03 18:24 | CI triage | CI triage | 37144053151 | Classify the failed run (jev-1.13.0)=skipped | SKIPPED | 9ffb41d34 |
| 2026-10-03 18:24 | AX Ledger Tools | AX Ledger Tools | 37144049823 | self-test (linux, node 22)=failure self-test (windows, node 22)=failure self-test (windows, node 24)=failure | FAILURE | 9ffb41d34 |
| 2026-10-03 14:52 | CI triage | CI triage | 37131219592 | Classify the failed run (jev-1.13.0)=skipped | SKIPPED | 8c7f2b189 |
| 2026-10-02 02:01 | AX Ledger Tools | AX Ledger Tools | 36953672160 | self-test (windows, node 22)=success self-test (linux, node 22)=success self-test (windows, node 24)=success | SUCCESS | 333e92aa5 |
| 2026-10-02 02:01 | SBOM and SCA | SBOM and SCA | 36953672111 | sbom-sca=success | SUCCESS | 333e92aa5 |
| 2026-10-02 02:01 | Speaking Module CI | Speaking Module CI | 36953672131 | secrets-scan=success migrations-check=success | SUCCESS | 333e92aa5 |
| 2026-10-02 02:01 | QA Smoke | QA Smoke | 36953672205 | E2E smoke (shard 1/13 · chromium-learner)=cancelled Backend tests (.NET) · shard 4/6=cancelled E2E smoke (shard 1/13 · webkit-learner)=cancelled E2E smoke (shard 1/13 · chromium-admin)=cancelled E2E smoke (shard 1/13 · webkit-expert)=success Frontend unit (vitest + lint + tsc + build)=cancelled E2E smoke (shard 1/13 · firefox-expert)=cancelled E2E smoke (shard 1/13 · mobile-chromium-learner)=cance | CANCELLED | 333e92aa5 |
| 2026-10-02 02:01 | Build & Deploy (web + API) | Build & Deploy (web + API) | 36953672231 | writing-regression-gate=success syntax-gate=success build-web=success build-agent-gateway=success build-backup=success build-api=success migrate-production=success deploy=success | SUCCESS | 333e92aa5 |
| 2026-10-02 01:43 | AX Ledger Tools | AX Ledger Tools | 36952304624 | self-test (windows, node 22)=success self-test (linux, node 22)=success self-test (windows, node 24)=success | SUCCESS | 31e4c4540 |
| 2026-10-02 01:26 | AX Ledger Tools | AX Ledger Tools | 36950958768 | self-test (windows, node 22)=success self-test (windows, node 24)=success self-test (linux, node 22)=success | SUCCESS | e27b55dbf |
