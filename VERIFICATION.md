# Verification Ledger

Append-only. Every row is machine-written by `pnpm run ax:record` from GitHub Actions
(`gh run list` / `gh run view`). Never hand-edit a Result — re-check it with
`pnpm run ax:verify`. A claim with no run id is not evidence.

| Date (UTC) | Claim / gate | Workflow | Run | Job(s) | Conclusion | SHA |
| --- | --- | --- | --- | --- | --- | --- |
| 2026-10-04 03:11 | CI triage | CI triage | 37173356860 | Classify the failed run (jev-1.13.0)=skipped | SKIPPED | f13b93bd4 |
| 2026-10-04 03:07 | CI triage | CI triage | 37173143859 | Classify the failed run (jev-1.13.0)=skipped | SKIPPED | f13b93bd4 |
| 2026-10-04 03:07 | Deploy production | Deploy production | 37173143817 | Resolve the build to deploy=success Apply migration SQL (if the API changed)=success Roll out to the VPS=success | SUCCESS | f13b93bd4 |
| 2026-10-04 03:02 | SBOM and SCA | SBOM and SCA | 37172887093 | sbom-sca=success | SUCCESS | f13b93bd4 |
| 2026-10-04 03:02 | Build images | Build images | 37172887015 | Detect what changed=success Deployment contract guards=success Syntax gate (seconds)=success Writing grader regression (on change)=skipped build-api=success build-agent-gateway=success build-web=success build-backup=success Retag unchanged ${{ matrix.image }}=skipped Publish verified release provenance=success Writing model-answer gate (on change)=skipped | SUCCESS | f13b93bd4 |
| 2026-10-04 03:02 | .github/workflows/ax-check.yml | .github/workflows/ax-check.yml | 37172886438 | (none) | FAILURE | f13b93bd4 |
| 2026-10-04 02:21 | CI triage | CI triage | 37170802825 | Classify the failed run (jev-1.13.0)=skipped | SKIPPED | 66458ca82 |
| 2026-10-04 02:16 | CI triage | CI triage | 37170535352 | Classify the failed run (jev-1.13.0)=skipped | SKIPPED | 66458ca82 |
| 2026-10-04 02:16 | Deploy production | Deploy production | 37170535406 | Resolve the build to deploy=success Apply migration SQL (if the API changed)=success Roll out to the VPS=success | SUCCESS | 66458ca82 |
| 2026-10-04 02:09 | Build images | Build images | 37170176749 | Syntax gate (seconds)=success Detect what changed=success Deployment contract guards=success Writing grader regression (on change)=success build-api=success build-agent-gateway=success build-web=success build-backup=success Retag unchanged ${{ matrix.image }}=skipped Writing model-answer gate (on change)=success Publish verified release provenance=success | SUCCESS | 66458ca82 |
| 2026-10-03 22:54 | CI triage | CI triage | 37159991561 | Classify the failed run (jev-1.13.0)=skipped | SKIPPED | 58ba842ea |
| 2026-10-03 22:47 | CI triage | CI triage | 37159617383 | Classify the failed run (jev-1.13.0)=skipped | SKIPPED | 58ba842ea |
| 2026-10-03 22:47 | Deploy production | Deploy production | 37159617346 | Resolve the build to deploy=success Apply migration SQL (if the API changed)=success Roll out to the VPS=success | SUCCESS | 58ba842ea |
| 2026-10-03 22:42 | Build images | Build images | 37159346852 | Syntax gate (seconds)=success Detect what changed=success Deployment contract guards=success Writing model-answer gate (on change)=skipped Writing grader regression (on change)=skipped build-agent-gateway=success build-backup=success Retag unchanged web=success build-api=success Retag unchanged api=success build-web=skipped | SUCCESS | 58ba842ea |
| 2026-10-03 22:09 | CI triage | CI triage | 37157490344 | Classify the failed run (jev-1.13.0)=skipped | SKIPPED | 42f69600c |
| 2026-10-03 22:00 | CI triage | CI triage | 37156952417 | Classify the failed run (jev-1.13.0)=skipped | SKIPPED | 42f69600c |
| 2026-10-03 22:00 | Deploy production | Deploy production | 37156952312 | Resolve the build to deploy=success Apply migration SQL (if the API changed)=success Roll out to the VPS=success | SUCCESS | 42f69600c |
| 2026-10-03 21:55 | Build images | Build images | 37156713454 | Syntax gate (seconds)=success Deployment contract guards=success Detect what changed=success Writing grader regression (on change)=skipped Writing model-answer gate (on change)=skipped build-web=success build-backup=success build-agent-gateway=success build-api=success Retag unchanged ${{ matrix.image }}=skipped | SUCCESS | 42f69600c |
| 2026-10-03 21:00 | CI triage | CI triage | 37153578566 | Classify the failed run (jev-1.13.0)=skipped | SKIPPED | e484b7104 |
| 2026-10-03 20:53 | CI triage | CI triage | 37153138339 | Classify the failed run (jev-1.13.0)=skipped | SKIPPED | e484b7104 |
| 2026-10-03 20:53 | Deploy production | Deploy production | 37153138315 | Resolve the build to deploy=success Apply migration SQL (if the API changed)=success Roll out to the VPS=success | SUCCESS | e484b7104 |
| 2026-10-03 20:52 | CI triage | CI triage | 37153101431 | Classify the failed run (jev-1.13.0)=skipped | SKIPPED | e484b7104 |
| 2026-10-03 20:49 | CI triage | CI triage | 37152901331 | Classify the failed run (jev-1.13.0)=skipped | SKIPPED | e484b7104 |
| 2026-10-03 20:49 | Build images | Build images | 37152898262 | Syntax gate (seconds)=success Deployment contract guards=success Detect what changed=success Writing model-answer gate (on change)=skipped Writing grader regression (on change)=skipped build-agent-gateway=success build-backup=success build-api=success Retag unchanged api=success Retag unchanged web=success build-web=skipped | SUCCESS | e484b7104 |
| 2026-10-03 20:47 | CI triage | CI triage | 37152781674 | Classify the failed run (jev-1.13.0)=skipped | SKIPPED | a479f98d4 |
| 2026-10-03 20:38 | CI triage | CI triage | 37152261132 | Classify the failed run (jev-1.13.0)=skipped | SKIPPED | a479f98d4 |
| 2026-10-03 20:38 | Deploy production | Deploy production | 37152261199 | Resolve the build to deploy=success Apply migration SQL (if the API changed)=success Roll out to the VPS=success | SUCCESS | a479f98d4 |
| 2026-10-03 20:38 | CI triage | CI triage | 37152240160 | Classify the failed run (jev-1.13.0)=success | SUCCESS | a479f98d4 |
| 2026-10-03 20:35 | CI triage | CI triage | 37152063848 | Classify the failed run (jev-1.13.0)=skipped | SKIPPED | a479f98d4 |
| 2026-10-03 20:35 | Jev integration | Jev integration | 37152060321 | Jev typed judgment contracts=failure | FAILURE | a479f98d4 |
| 2026-10-03 20:35 | Build images | Build images | 37152060338 | Deployment contract guards=success Detect what changed=success Syntax gate (seconds)=success Writing grader regression (on change)=skipped Writing model-answer gate (on change)=skipped Retag unchanged web=success build-agent-gateway=success build-backup=success build-api=success Retag unchanged api=success build-web=skipped | SUCCESS | a479f98d4 |
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
