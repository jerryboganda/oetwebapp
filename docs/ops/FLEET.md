# Owner Fleet manager and helper VPSs: runbook

> **Status:** governing runbook for the Owner Fleet program. Decision record:
> [`docs/adr/0005-fleet-manager-and-remote-workers.md`](../adr/0005-fleet-manager-and-remote-workers.md). Repo-rule exception:
> `AGENTS.md` -> "Owner Fleet exception (owner directive 2026-10-05)". AI statement: [`docs/AI-USAGE-POLICY.md` section 22](../AI-USAGE-POLICY.md).
> The wire contract is the OET Remote Worker Protocol (OET-RWP/1); "RWP x.y" below cites its sections and `RW-nnn` its conformance ids.
> The manager, agent and API endpoints are delivered by separate tracks. Where this document describes behaviour of code that is not merged yet,
> it states the contract that code must meet. Nothing here claims a measured result.

Never paste an IP address of a helper, an SSH key, a token, a TOTP code or a password into this document, a PR, an issue, a chat, a ticket,
`SESSION_STATE.md` or any file in the repository. Every value written as `<...>` is a placeholder. Those values never go in the repository (section 2).

## Contents

1. [Purpose and scope](#1-purpose-and-scope)
2. [Hard rules](#2-hard-rules)
3. [Architecture](#3-architecture)
4. [Rollout order (enforced)](#4-rollout-order-enforced)
5. [Private access: the SSH tunnel](#5-private-access-the-ssh-tunnel)
6. [Enrollment](#6-enrollment)
7. [Credential custody and rotation](#7-credential-custody-and-rotation)
8. [Host-key verification](#8-host-key-verification)
9. [Removal and revocation](#9-removal-and-revocation)
10. [Rollback and version skew](#10-rollback-and-version-skew)
11. [Failure states](#11-failure-states)
12. [Capacity numbers](#12-capacity-numbers)
13. [What is and is not automatic](#13-what-is-and-is-not-automatic)
14. [Verification and the pipeline contract](#14-verification-and-the-pipeline-contract)
15. [Residual risks (accepted)](#15-residual-risks-accepted)
16. [Owner checklist before the first enrollment](#16-owner-checklist-before-the-first-enrollment)

> **Operator CLI (owner directive 2026-10-07):** every enrollment/lifecycle/job action in this
> runbook also has a command form: `ops/fleet/fleet <verb>` from the repo (ssh + `docker exec` into
> the manager), e.g. `add`, `status`, `nodes`, `inspect`, `drain`, `enable`, `disable`, `remove`,
> `test` (canary), `upgrade --digest`, `rotate-token`, `policy show|set`, `jobs`, `job`,
> `requeue`, `force-local`, `cancel`, `rebalance`, `operations`. Read-only verbs need no step-up;
> privileged verbs need the same fresh single-use TOTP as the dashboard routes. The agent-facing
> workflow built on it is [`docs/VPS_FLEET.md`](../VPS_FLEET.md); this runbook still governs
> wherever they disagree.

---

## 1. Purpose and scope

The fleet lets the owner rent extra VPSs ("helpers") that execute deterministic, CPU-heavy jobs for the primary VPS so the primary keeps its cores for
learners. First workload: PdfPig text extraction of **admin-uploaded** content PDFs (RWP 6.1). Later workloads (companion index prep, live-class audio
extraction, speaking clip join) are separate owner decisions, each behind its own feature flag and review.

Two parts:

- **Fleet manager** (`platform/fleet`, compose project `oet-fleet` on the primary): inventory, enrollment, hardening, image rollout, removal, owner UI.
- **Helper agent** (one container per helper): claims jobs from the API over HTTPS, executes, returns results. It never talks to the manager.

Out of scope: any AI call (OCR tiers, grading, Whisper, embeddings stay on the primary), anything that needs the database, the media volume or a
provider key, learner Writing PDFs and speaking recordings in phase 1, and any build, test or benchmark on a helper (builds run on GitHub Actions; no
automated test or benchmark runs anywhere, owner directive 2026-10-06).

## 2. Hard rules

These are the operating form of the `AGENTS.md` exception. If a step here conflicts with it, `AGENTS.md` wins and this document is fixed.

1. **Digest-only executors.** A helper runs `ghcr.io/jerryboganda/oetwebapp-fleet-agent@sha256:<digest>` and nothing else. Its bootstrap is Docker engine
   plus the agent: no checkout, build, test, source install, or `latest`/`:<sha>` tag.
2. **The manager runs Ansible inside its own container.** *(Owner revision 2026-10-06: the former ban on agents running `ssh`/`docker`/`ansible`
   against a helper is removed; an agent may use an owner-designated SSH identity to reach a helper the owner names.)*
3. **Helper IPs, SSH keys, node tokens, the fleet-service credential and the vault master key never go in the repository, chat, PRs or logs.** *(Owner
   revision 2026-10-06: the former "agents never hold helper keys" rule is removed.)* The repository carries `.gitignore` rules and a `platform/**` secret scan (section 14).
4. **No public ingress for the manager.** Loopback bind, SSH tunnel, owner password + TOTP, lockout, short sessions.
5. **The primary VPS is never a helper.** The inventory validator refuses `185.252.233.186`, any host of compose project `oetwebsite`, and any name that
   resolves to a loopback, link-local, private, CGNAT or unique-local address.
6. **No AI, no credentials on helpers.** No database, provider or storage credential, no AI call, no Claude Max change. One `AiUsageRecord` per provider
   call stays on the primary.
7. **Data hygiene is mandatory even though any data may leave.** Scratch is tmpfs, deleted on completion, never in logs; access is job-scoped; derivatives
   of learner data are registered as `MediaAsset`s so retention workers see them.
8. **Never loosen a gate to make the fleet pass**: not `Deploy production`, not the pipeline contract, not the SSH allow-list.

## 3. Architecture

```
 Owner browser --SSH tunnel--> Fleet manager (oet-fleet, loopback only, on the primary)
                                   |  HTTPS, fleet-service credential       |  SSH, pinned host key, forced command
                                   v                                        v  (provisioning, repair, rollout, status only)
                            OET API (active slot) <--- HTTPS, node token --- Helper agent (container, tmpfs only)
                                   |                   /v1/internal/remote-worker/*
                                   v
                     Postgres (RemoteJobs ...)     IFileStorage (named volume, primary only)
```

- **Job plane:** agent -> API over `https://api.oetwithdrhesham.co.uk/v1/internal/remote-worker`, per-node bearer token, agent initiates every call.
- **Service plane:** manager -> API over `.../v1/internal/fleet`, fleet-service credential, gated by the feature flag `remote_fleet_service_enabled`.
- **Provisioning plane:** manager -> helper over SSH only. This is the only inbound path to a helper (RWP 1.1, 7.4).
- **No agent-to-manager path.** Node state reaches the manager through the API; desired state (drain, limits, approved digests) reaches the agent inside
  claim and heartbeat responses. A manager outage never stops job flow (RWP 1.2).

The manager is an isolated compose project: no `build:` sections, `pull_policy: missing`, `cap_drop: ALL`, read-only root, `no-new-privileges`, hard
`mem_limit`, `cpus` and `pids_limit`, `oom_score_adj` above Postgres and the API, one playbook at a time, external protected volume
`oet-fleet_fleet_data`, attached to no production network. Console engines cannot see or touch any `oet-fleet*` object (denied by `oet-agent-dockerproxy`).

## 4. Rollout order (enforced)

Do not skip or reorder. Each step is verified by a recorded GitHub Actions run (build, guard and rollout) plus the owner's own manual testing. No test,
lint or typecheck run exists to claim (owner directive 2026-10-06): say "not tested - owner QA".

| # | Step | Verified by |
|---|---|---|
| 1 | **Governance + contract**: `AGENTS.md` exception, this runbook, ADR 0005, `.gitignore` rules, the hardened pipeline contract (one `scripts/deploy/**` commit that rides one all-reuse production rollout), protected-volume and prune exclusions | `Build images` guards job (`pipeline:check`, `release-manifest.test.mjs`) then `Deploy production` for that SHA |
| 2 | **Console isolation**: `agent-console/dockerproxy` denies `oet-fleet*` (shipped through `agent-console.yml`; a push to `main` recreates the proxy when no console turn is active, otherwise dispatch with `apply=true` or use "Apply update") | `agent-console.yml` run: static rollout guards, image build and the pull-only rollout (no test job); then the live deny check below |
| 3 | **Manager deploy** through `.github/workflows/fleet.yml` (BUILD-ONLY: compile, images to GHCR, pull-only rollout of `oet-fleet`; no QA job) | `gh run watch` on `fleet.yml` for the SHA, then `pnpm run ax:record` |
| 4 | **API remote endpoints, default-off** (hand-authored migration, `RemoteWorkerOnly`/`FleetServiceOnly`, all flags off) | normal `Build images` + `Deploy production` |
| 5 | **Enable per node**: register, canary, enable; then one job kind at a time behind its flag | the manager UI node view and an `AuditEvent` per change |

Verify step 2 live before step 3: from a console session, `docker ps` must not list `oet-fleet*` and `docker inspect oet-fleet-manager` must be
refused (a deny, never an approval card).

## 5. Private access: the SSH tunnel

The manager listens on `127.0.0.1` of the primary only. There is no hostname and no proxy entry. The owner reaches it from their own workstation:

```
ssh -N -L 127.0.0.1:<local-port>:127.0.0.1:<manager-port> <owner-ssh-user>@<primary-host>
```

then browses to `http://127.0.0.1:<local-port>` on the workstation. Keep the tunnel open only while using the UI.

- Sign in with the owner password and TOTP. 5 failed logins lock the account and the source for 15 minutes. Sessions end after 20 minutes idle or 60
  minutes absolute. Every privileged action (enroll, rotate, revoke, delete, drain, approve a digest, submit an owner key) asks for TOTP again
  (replay-guarded). The manager never validates platform JWTs and never holds the JWT signing key.
- Never publish the manager through Nginx Proxy Manager, `npm_proxy` or a `ports:` entry that is not `127.0.0.1:<port>:8080`. An internet-reachable
  admin surface that holds keys able to root every helper is the one thing this design exists to avoid.
- The tunnel is the owner's own action. No agent opens it, and no console session can reach the loopback listener.

## 6. Enrollment

An enrollment is the manager operation `enroll` (RWP 8.1). The owner supplies, in the manager UI over the tunnel: a `nodeRef`, the helper's address and SSH
port, region and provider, and a **one-time owner SSH credential** for the helper (root or a sudo user). Steps are idempotent and resumable; a crash
re-runs the failed step safely.

1. **Rent a helper**: Ubuntu 22.04/24.04/26.04 or Debian 12, `x86_64`, systemd, at least 2 cores, 4 GiB RAM and 20 GiB free disk (design default for a node
   is 4 vCPU / 8 GiB). It must be a fresh VPS with no `oet-*` containers and not the primary.
2. **Validate**: the inventory validator rejects forbidden addresses and malformed input before any connection (RWP 8.9).
3. **Verify the host key out-of-band** (section 8). The manager fetches fingerprints only to display them; the owner compares with the VPS provider
   console and types the first 8 characters to confirm. Only then is the key pinned.
4. **Bootstrap (owner credential, 60-minute TTL)**: preflight, create the restricted `oetfleet` user with a forced-command gate and a one-command
   sudoers drop-in, install a manager-generated ed25519 key, install Docker Engine from the pinned vendor repository, firewall (SSH only from the
   primary), host baseline (swap off, core dumps off, volatile journald, time sync, Docker log driver with no content), sshd hardening. Before the final
   sshd reload the manager re-verifies its own login in a second connection and aborts with `lockout_risk` rather than lock the operator out.
5. **Discard the owner key**: crypto-erased at the end of step 4 (also on cancel and on expiry); no plaintext copy may remain in SQLite, its WAL or logs
   (the owner checks this by hand, there is no automated test). From here only the restricted key and the `oet-fleet-ctl` verbs exist.
6. **Image**: pull the agent by approved digest (section 7, GHCR), verify the image id, write the 0600 env file, start the container with the fixed
   hardened flags (read-only root, `--cap-drop ALL`, tmpfs scratch, no mounts, no docker socket, non-root).
7. **Verify, canary, activate**: wait for the node's first heartbeats (`Probation`), run the known-answer canary job, then enable (`Active`). Only an
   `Active` node claims real jobs ("auto-assign" means exactly this).

A late-enrolled helper waits in `ImageAwaitingSync` until the next `fleet.yml` `sync` dispatch supplies a pull token (unless the owner makes the agent
package public). The UI says so.

## 7. Credential custody and rotation

| Secret | Lives only in | Never in | Lifetime and rotation |
|---|---|---|---|
| Vault master key (`/run/secrets/fleet_master_key`, 32 bytes, 0400) | a root-only host file on the primary (directory 0700) that the console's read-only view of `/opt/oetwebapp` cannot read | the repo, env files, images, backups, logs | Rotate by introducing a new key beside `fleet_master_key_prev`, run `rewrap`, then remove the old file. A missing or wrong-length key is a hard startup failure. |
| Fleet-service credential (`ofs1_...`, `/run/secrets/fleet_api_credential`) | that file only; the API stores its SHA-256 | any log, any other container | 90-day TTL. Mint and rotate from the owner-gated admin endpoint (shown once); the previous credential stays valid for the grace period (default 3600 s, max 86400 s). |
| Node token (`orw1_...`) | the helper's 0600 env file and the agent's memory; the API stores its SHA-256 | the manager's disk (transits memory only), logs, repo | 30-day TTL, the manager rotates at 14 days (`tokens/rotate`, grace, restart, heartbeat check, then the old token expires). At most 3 active credentials per node. |
| Manager SSH key per helper (ed25519, restricted forced command) | the vault, AES-256-GCM with AAD per host, purpose and id | any plaintext copy | Rotate by re-running the `install-key` step; revoke by removing the helper's `authorized_keys` line during removal. |
| Owner SSH credential for bootstrap | memory only, up to 60 minutes | disk, logs, SQLite, WAL, browser storage | Destroyed at the end of bootstrap, on cancel and on expiry. A later root-level repair is a new operation that asks again. |
| GHCR pull token | the manager's memory for one rollout (at most 60 minutes), sent on SSH stdin | argv, env, any file, SQLite, logs | Per-rollout, supplied by the `fleet.yml` `sync` dispatch as the job-scoped `GITHUB_TOKEN`. No long-lived PAT exists anywhere. |
| Owner TOTP secret | the manager vault, encrypted | logs | Re-enroll from the UI. |

Write-only fields: the UI never shows a secret after submission, only a fingerprint hint (first 8 hex characters of the SHA-256 of the public key, or of
the secret). The owner key is accepted only by a POST over the tunnelled UI into an `autocomplete="off"` field. All log lines, operation summaries and
audit details pass the same redaction regexes as the owner console (including PEM private-key blocks). The audit table is hash-chained and verified at
startup.

The master key shares a host with its ciphertext. The vault therefore protects against casual disclosure (backups, repo flips, logs, volume copies),
not against full compromise of the manager host (section 15).

## 8. Host-key verification

1. Open the VPS provider's console or API and read the host's SSH key fingerprint (SHA256) from there. Do not trust a fingerprint shown to you over the same
   network path you are about to use: first contact is not authenticated.
2. In the manager, compare it with the displayed fingerprint and type its first 8 characters. The manager stores the algorithm and fingerprint and
   writes a per-host known_hosts file from that pin.
3. Every later connection uses `StrictHostKeyChecking=yes` against that file. `accept-new` is forbidden anywhere in fleet code (the contract scan rejects
   it), unlike the legacy workflows that still use it.
4. **A changed key is a hard failure** (`host_key_changed`): the operation stops, the host is set `Disabled` (and its API node is disabled), an alert is
   raised, and only an explicit owner re-pin with a fresh out-of-band check clears it. Treat it as a possible man-in-the-middle or a reinstalled host.

## 9. Removal and revocation

Normal removal (manager UI, operation `remove`):

1. **Drain**: the node stops receiving new jobs; wait until its leases reach zero (or 5 minutes).
2. **Disable**, then **revoke** the node in the API: irreversible; every credential of the node is revoked in one transaction and its leases are
   requeued (attempts refunded).
3. **Stop and wipe** the agent on the helper (container, env file, images, scratch), remove the manager key from `authorized_keys`, close the firewall rule.
4. **Delete** the host record and its vault credentials in the manager.
5. **Destroy the VPS at the provider.** The provider was a processor of whatever the node saw; record the end date in the processor register.

Break-glass without the manager (owner session, `OwnerAgent` policy): `POST /v1/admin/remote-workers/nodes/{id}/disable` and `.../revoke`. Emergency
stop for everything: turn off the `remote_jobs_enabled` feature flag (queued work falls back to the primary within one reaper tick; running leases
finish); `remote_jobs_freeze_applies` makes completions answer `503 applies_frozen` and cancels the jobs.

A lost or stolen helper credential: revoke the node first (section 9), rotate the fleet-service credential, then rotate the vault master key.
A suspected manager-host compromise: stop `oet-fleet-manager` on the primary through SSH (ops break-glass, not an agent), revoke every node from the
admin endpoints, rotate everything, and re-enroll helpers from fresh VPSs.

## 10. Rollback and version skew

- **Protocol numbers:** the API and the agent each accept protocol N and N-1 (RWP 2.4), so either may roll first. Operating order for a protocol
  bump: roll the API (`Deploy production`), then dispatch the agent `sync` once it is green.
- **API rollback** (`gh workflow run production-deploy.yml -f sha=<previous-sha>`): an API without the routes answers 404/405; agents enter
  `ApiUnsupported`, stop claiming, abort in-flight jobs at their local lease expiry, delete scratch and probe with backoff. An older protocol answers
  `426`; agents downgrade once or enter `ProtocolMismatch`. No manual action is needed on helpers (RWP 5.6).
- **Agent rollback:** the manager rolls one host at a time (drain, pull the previous approved digest, verify, run, canary, enable). The approved list
  keeps the current digest plus the previous two (at most 8). A failure halts the rollout and leaves later hosts untouched.
- **Manager rollback:** `fleet.yml` always builds the commit it runs on and has no by-SHA reuse path (unlike `production-deploy.yml`), so revert or fix
  forward on `main` and dispatch `fleet.yml` with `sync=true`; a push alone only builds and records. The manager's state volume is never recreated.
- **Kill switch:** `remote_jobs_enabled` off returns every claim `204 disabled`; in-flight leases keep working; queued work falls back to the primary.

## 11. Failure states

Stable manager failure reasons (RWP 8.1): `inventory_invalid`, `forbidden_host`, `host_key_unreachable`, `host_key_mismatch`, `host_key_changed`,
`ssh_unreachable`, `auth_failed`, `owner_credential_expired`, `preflight_rejected` (with a detail such as `os_unsupported`, `mem_below_min`,
`existing_oet_workload`), `bootstrap_step_failed`, `lockout_risk`, `owner_key_discard_failed`, `api_unreachable`, `api_register_failed`,
`token_render_failed`, `image_pull_failed`, `image_digest_unapproved`, `image_id_mismatch`, `agent_start_failed`, `agent_not_heartbeating`,
`protocol_unsupported`, `digest_not_approved_by_api`, `canary_timeout`, `canary_mismatch`, `drain_timeout`, `internal_error`.

| Symptom | Meaning | Action |
|---|---|---|
| Enrollment stops at `Failed(host_key_changed)` | the host's key differs from the pin | Section 8 step 4; do not re-pin without the provider console. |
| `Failed(owner_credential_expired)` | bootstrap took longer than 60 minutes | Re-enter the owner key; the operation resumes at the failed step. |
| `Failed(lockout_risk)` | the manager could not re-verify its own login before the sshd reload | Nothing was locked out; fix connectivity and retry the step. |
| `ImageAwaitingSync` | no pull token available | Dispatch `fleet.yml` `sync` (or make the agent package public). |
| Node `Quarantined` | 3 integrity strikes in 60 minutes, or a canary mismatch | Inspect the audit trail; `enable` needs a fresh passing canary. Treat repeated strikes as a faulty or hostile helper and revoke. |
| Node `Stale`/`Offline` (no heartbeat for 45 s / 10 min) | helper down, network cut, or agent stopped (exit code 3 = superseded by a second instance with the same token) | Section 9 if it does not return; jobs requeue by themselves via the reaper. |
| Agent `AuthFailed` | token expired or revoked | Rotate the token; the manager rewrites the env file and restarts the agent. |
| Agent `Degraded` (`unapproved_digest`) | the running digest is not in the approved list | Approve the digest in the manager, or roll an approved image. The node claims nothing meanwhile. |
| Jobs piling up `Queued` | no healthy node for the kind, or the kind flag is off | Check `GET /stats`; after 10 minutes work falls back to the primary if it has headroom, after 60 minutes it runs locally at concurrency 1. |
| Job `Quarantined` | attempts exhausted | Never auto-run locally (poison-PDF crash-loop protection). An admin chooses `requeue` or `force-local` after inspecting it. |
| Manager UI unreachable | tunnel closed, or the container is down | Re-open the tunnel; `docker ps` on the primary through SSH. Job flow is unaffected. |

## 12. Capacity numbers

These are design defaults from the protocol, not measurements. **No throughput, latency or learner-count figure is claimed** until the owner has measured
it by hand on real hardware (no workflow measures capacity: owner directive 2026-10-06); the 1000-learner target stays unproven until then. Any number
added to this document must say who measured it, when and how.

| Item | Design default |
|---|---|
| Helper node (reference) | 4 vCPU / 8 GiB RAM, no swap |
| Agent container budget | 3 CPU, 5 GiB memory (tmpfs scratch counts inside it): `cpuMilli 3000`, `memMiB 5120`, `tmpMiB 3072` |
| Initial heavy concurrency per node | 2 weight units |
| `pdf.extract` per job | weight 1, 1000 mCPU, 2048 MiB + 256 MiB tmp, 120 s timeout (two fit in 5120 MiB: 2 x 2304 = 4608 MiB) |
| `media.audio-extract` per job | weight 2 (takes both slots), 2000 mCPU, 1024 MiB + 1536 MiB tmp |
| Admission pressure | reduce above 80% CPU or below 20% free memory for 15 s; restore after 120 s below 60% / above 25%; shed the youngest job below 10% free |
| Lease / job heartbeat / node heartbeat | 120 s / 20 s / 15 s; stop new assignment after 45 s, Offline after 600 s |
| Attempts | 3, with at most 5 refunded releases; backoff `min(300, 5 * 2^(attempt-1))` s with up to 20% jitter |
| Fallback to the primary | after 10 minutes Queued if the primary has headroom; hard after 60 minutes at concurrency 1 |
| Manager on the primary | design sizing about 0.5 CPU and 512 MiB with `oom_score_adj` above Postgres and the API; re-check against `docker stats` on the real host |

Sizing rule: helpers needed = peak concurrent heavy weight / (2 per node), plus one spare so a drain or an outage does not starve the queue. The rule
needs the real peak from the owner's manual measurement before it is applied.

## 13. What is and is not automatic

| Automatic (after the owner has enrolled and enabled a node) | Never automatic (owner action required) |
|---|---|
| Job claim and placement ("auto-assign"): a healthy `Active` node simply claims eligible jobs | Rent, enroll, remove or revoke a helper; approve an agent digest (unless `AutoApproveDigests` is switched on, default off) |
| Heartbeats, health states, admission-pressure shedding, lease expiry and reaper requeue, fallback to the primary | Provide or rotate the owner bootstrap key, the master key, the fleet-service credential |
| Node token rotation at 14 days; rolling agent update to the approved digest, halting on the first failure | Pin a host key; clear `host_key_changed`; release a node from quarantine |
| Periodic status polling over SSH (every 5 minutes); the known-answer canary | Turn a job kind or the master switch on (`FeatureFlags`); add a kind to a node's policy |
| Cleaning scratch, deleting terminal job rows after 30 days, orphaned outputs after 1 hour | Anything on the primary VPS: deploys stay `Build images` then `Deploy production`; the fleet only ever runs prebuilt images |

Ansible itself is never "automatic" in the sense of a schedule: it runs only as part of an owner-initiated operation, inside the manager container.

## 14. Verification and the pipeline contract

- Fleet changes are verified with `gh run watch` on `.github/workflows/fleet.yml` for the SHA, then `pnpm run ax:record`. A push that only touches
  `platform/**` deploys nothing, so `pnpm run ship` ends with `SHIP-WATCH_NOTHING_TO_DEPLOY`; that is success for the production pipeline, not proof
  that the fleet workflow passed. Console-authored fleet PRs merge through the Ship executor and are verified the same way.
- `scripts/deploy/verify-pipeline-contract.mjs` (`pnpm run pipeline:check`; the `guards` job of `Build images`; `pnpm run ship:gate`) enforces:
  - the SSH allow-list `PROD_SSH_WORKFLOWS` (the eight workflows that hold an SSH credential: `agent-console`, `fleet` (its dispatch-only `sync`
    job), `mobile-release`, `production-deploy`, `publish-existing-desktop-to-vps`, `publish-existing-mobile-to-vps`, `tauri-desktop-release`,
    `writing-ai`): any other workflow holding a production or VPS SSH credential fails, so a ninth SSH workflow needs a visible edit of that list in
    the same commit plus the `AGENTS.md` exception;
  - once `fleet.yml` exists: the name `Fleet (build + rollout)`, concurrency `group: fleet` with `cancel-in-progress: false`, the `main`-only deploy with
    `environment: production`, its own `guards` job running `node scripts/deploy/verify-pipeline-contract.mjs` and
    `bash scripts/deploy/verify-compute-offload.sh`, no `schedule` or `pull_request_target` trigger, no `:latest`, no reference to
    `auto-deploy-ghcr.sh`, and an SSH rollout between `# BEGIN REMOTE FLEET ROLLOUT` and `# END REMOTE FLEET ROLLOUT` that only pulls and starts with
    `--no-build` (no build, install or source sync, no volume removal; each `compose up` is written on one line with `--no-build`, as in
    `agent-console.yml`);
  - `platform/**`: no private keys and no node or fleet token literals (a deliberate test fixture carries a `secret-scan:allow` marker on its line), and
    fleet code never uses `StrictHostKeyChecking=accept-new`/`no`, a `/dev/null` known_hosts or Ansible `host_key_checking = False`;
  - no workflow other than `build-images.yml` runs a test/QA runner (rule 3, owner directive 2026-10-06: vitest, `dotnet test`, Playwright, k6 and the
    rest), and `fleet.yml` additionally runs no benchmark, parity or conformance job, so it stays BUILD-ONLY;
  - the release stays exactly four components (no fifth fleet component).
- `scripts/deploy/verify-compute-offload.sh` applies the same pull-only block check to `fleet.yml` and rejects `build:` sections in
  `platform/fleet/docker-compose*.yml` and `docker-compose.fleet*.yml` (existence-conditional: it passes before they exist).
- `scripts/deploy/protect-production-data.sh` blocks `docker volume rm`, `volume prune` and `compose down -v` for `oet-fleet_fleet_data`;
  `scripts/deploy/prune-stale-images.sh` never removes the fleet images.
- **No automated QA.** No test, load, parity, conformance or benchmark workflow exists or may be added (owner directive 2026-10-06). The fleet UI, the
  protocol behaviour and capacity are checked by the owner by hand. Fleet test source files may live in git as inert manual tools; no workflow runs them
  and nobody claims they passed. Rust stays unbuilt; reopening it needs the owner's say-so and a benchmark that the owner runs manually (AGENTS.md
  "Owner Fleet exception" (h)).

## 15. Residual risks (accepted)

- **Master key beside its ciphertext** on the primary. A full compromise of the manager host yields every helper key. Mitigations: no public ingress,
  restricted helper keys (a forced command, not a shell), per-host pins, rotation, quick revocation from the API.
- **The VPS vendor is a processor** of whatever a job carries (any data may leave the primary by owner decision). Record each node's region and
  provider in the processor register; update the privacy notice and transfer assessment (owner/legal task, outside this repository).
- **Tmpfs does not cover swap, hypervisor snapshots or provider-side forensics.** The baseline disables swap and core dumps and uses a log driver with
  no content; it cannot stop a hostile provider reading memory.
- **Docker is installed from the internet on each helper** (pinned repository key and package versions); the agent image is verified by digest. Supply-chain
  risk remains for those two links.
- **Helper output is untrusted** after cutover: structural validation, hash echoes, a known-answer canary, a verify sample against the in-process
  extractor, and strikes limit but do not eliminate a lying node.
- **The API's forwarded-header trust** (`Proxy:KnownNetworks`) was sized for the current proxy hops; helpers behind provider NAT share rate-limit
  partitions. Per-node limits never rely on IP.
- **Accept-new in legacy workflows.** `production-deploy.yml`, `agent-console.yml` and `writing-ai.yml` still pin host keys with
  `accept-new` (see `docs/ops/deploy-gate.md`); that is outside this program and stays an open owner item. Fleet code must not copy it.

## 16. Owner checklist before the first enrollment

- [ ] `AGENTS.md` carries the Owner Fleet exception, and `pnpm run pipeline:check` passed in the `Build images` guards job for the merge.
- [ ] The dockerproxy deny rule is live: a console session cannot list or inspect `oet-fleet*`.
- [ ] The environment `production` holds the secret `PROD_SSH_KNOWN_HOSTS` (the primary's pinned host-key line, verified out-of-band); `fleet.yml sync`
      refuses to connect without it. `FLEET_MANAGER_ENABLED=true` is set in `.env.production` and `fleet_sync_token` exists beside the other fleet secrets.
- [ ] The manager is deployed through `fleet.yml` (`sync=true`), listens on `127.0.0.1` only, and is on no production network (`docker inspect` through SSH;
      the rollout also asserts both).
- [ ] The vault master key and the fleet-service credential are in root-only files (0400, directory 0700) that the console's read-only view of
      `/opt/oetwebapp` cannot read; neither is in any env file or repository.
- [ ] The API remote endpoints are deployed with every `remote_*` flag still off; the manager's service-plane test call answers.
- [ ] A helper candidate is rented from a provider whose console shows the SSH host-key fingerprint, with a throwaway bootstrap credential.
- [ ] The processor register and privacy notice name the provider and region.
- [ ] The kill switches are known: `remote_jobs_enabled`, `remote_fleet_service_enabled`, `remote_jobs_freeze_applies`, and the break-glass `disable` and
      `revoke` endpoints.
