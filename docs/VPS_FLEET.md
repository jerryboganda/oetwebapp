# VPS Fleet — the coding-agent and operator workflow

> **Purpose:** everything an AI coding agent (or the owner) needs to add, inspect, control, test,
> upgrade and remove a helper VPS without reverse-engineering anything. This is the *how-to*;
> the governing runbook is [`docs/ops/FLEET.md`](ops/FLEET.md) and the decision record is
> [`docs/adr/0005-fleet-manager-and-remote-workers.md`](adr/0005-fleet-manager-and-remote-workers.md).
> Load all three before touching `platform/fleet/**` or the fleet rules of
> `scripts/deploy/verify-pipeline-contract.mjs`.

## 30-second mental model

```text
 Owner / coding agent --SSH--> PRIMARY VPS (app, Postgres, durable RemoteJobs queue, reaper,
                                   |                 fallback executor, Fleet Manager on loopback)
                                   | docker exec (this is the whole access path)
                                   v
                            Fleet Manager (oet-fleet-manager: inventory, enrollment, policy,
                                   |          rollouts, audit chain, vault)
                                   | HTTPS + node tokens (worker-pull)
                                   v
        Helper VPS A (agent container)   Helper VPS B   ... any count, any size
```

- Jobs are **durable rows in Postgres on the primary**. Helpers **pull** (atomic claims with
  leases + fencing tokens); nothing durable lives on a helper.
- **The primary is the standing fallback executor.** With zero helpers the queue drains locally
  (soft fallback after 10 minutes, hard local execution after 60). Helpers only ever add
  throughput. Nothing about the application depends on a helper existing.
- A helper dies → its lease expires (reaper) → the job requeues with a fresh attempt → another
  node (or the primary) claims it. A stale worker's late completion is rejected by its fence.

## Minimum-info onboarding (the whole workflow)

The owner supplies exactly three things: the **IP**, the **SSH user**, and an **SSH key that can
reach it** (root or sudo). Everything else is automation.

```bash
# from the repository root (any machine with SSH access to the primary):
ops/fleet/fleet add helper-02 \
  --host 203.0.113.10 --port 22 --user root \
  --region sg --provider upcloud \
  --confirm-fingerprint SHA256:FINGERPRINT-FIRST-12-HEX \
  --ssh-key ~/.ssh/helper-bootstrap-key
```

What that one command drives (idempotent; a crash re-runs the failed step safely):

1. inventory validation (forbidden hosts refused before any connection),
2. host-key pin against the fingerprint you confirmed out-of-band,
3. preflight (OS, arch, cores, RAM, disk, clock, docker, controller reachability) — a clean
   `REJECTED: <reason>` if the machine is not fit, never a partial join,
4. restricted `oetfleet` user + forced-command gate + sshd hardening + firewall (SSH from the
   primary only) + baseline,
5. agent image pull **by digest only** (GHCR), hardened container start,
6. heartbeats observed (`Probation`), known-answer canary, then `Active`.

Successful output ends with:

```text
NODE helper-02
STATUS: ACTIVE
ENROLLMENT: PASS (preflight, bootstrap, image, agent, heartbeat, canary)
```

If the bootstrap key is passphrase-protected the CLI refuses it — supply a passphrase-free
bootstrap key, or enroll through the dashboard (which asks for the passphrase in memory only).
The owner credential is destroyed by the manager when bootstrap finishes (60-minute TTL at the
latest); afterwards only the restricted per-host key exists.

### The out-of-band host-key step is NOT optional

`--confirm-fingerprint` must come from the VPS provider console (or API), never from the network
you are about to use: first contact is not authenticated. A changed key later is a hard failure
(`host_key_changed`) that disables the node and needs an explicit re-pin.

## Command map

All commands run from the repo root via the wrapper (`ops/fleet/fleet VERB ...`), which is
`ssh primary + docker exec -i oet-fleet-manager dotnet Fleet.Manager.dll VERB ...`. Run the same
verbs inside the container directly if you are already on the primary. Add `--json` to most
read-only verbs for machine-readable output.

| Spec verb | Command | What it does |
|---|---|---|
| `fleet status` | `ops/fleet/fleet status` | node table (status, health, CPU%, mem-free%, free slots, pressure, last heartbeat) + primary/local-fallback row + per-kind queue + feature flags |
| `fleet nodes` | `ops/fleet/fleet nodes` | compact roster with agent version and offered kinds |
| `fleet inspect <node>` | `ops/fleet/fleet inspect <ref>` | host facts, pinned key, image digest, capacity, policy, canary, strikes, tokens, last operations |
| `fleet add <host>` | `ops/fleet/fleet add ...` | full enrollment (above) |
| `fleet drain <node>` | `ops/fleet/fleet drain <ref>` | stop new claims, let leases finish, safe to stop |
| `fleet disable <node>` | `ops/fleet/fleet disable <ref>` | excluded from placement, in-flight allowed to finish |
| `fleet enable <node>` | `ops/fleet/fleet enable <ref>` | back into placement (canary needed if quarantined) |
| `fleet remove <node>` | `ops/fleet/fleet remove <ref> [--force]` | drain → disable → revoke (credentials + leases requeued) → wipe agent → delete record. `--force` for dead machines. Provider-side VPS destruction stays a human action |
| `fleet test <node>` | `ops/fleet/fleet test <ref>` | queues the known-answer canary and waits: `SMOKE TEST: PASS` |
| `fleet upgrade` | `ops/fleet/fleet upgrade --digest sha256:...` | rolling image update to an approved digest, one host at a time, halts on first failure |
| `fleet rebalance` | `ops/fleet/fleet rebalance` | worker-pull balance report: per kind, queue depth vs eligible nodes and free slots (there is nothing to move by hand — claims are atomic pulls) |
| — | `ops/fleet/fleet rotate-token <ref>` | rotate a node token (14-day rotation is automatic; this is the manual path) |
| — | `ops/fleet/fleet policy show [ref]` / `policy set <ref> --kinds a,b,c` | show global/host/effective policy; edit a host's allowed kinds and concurrency and push it |
| — | `ops/fleet/fleet jobs [--state Failed] ...` / `job <id>` | queue depth, dead-letter list, one job's attempts/fence/owner/errors |
| — | `ops/fleet/fleet requeue <job-id>` | retry a failed/quarantined job after the underlying problem is fixed |
| — | `ops/fleet/fleet force-local <job-id>` | run a quarantined job on the primary instead |
| — | `ops/fleet/fleet cancel <job-id>` | cancel a queued/leased job |
| — | `ops/fleet/fleet operations` / `op <id>` | durable operation log (enroll/drain/rollout steps and failures) |
| — | `ops/fleet/fleet tunnel` | open the SSH tunnel for the dashboard UI at `http://127.0.0.1:8480` |

### Authentication for privileged verbs

Every state-changing verb (add, drain, enable, disable, remove, rotate-token, upgrade, policy
set, requeue, force-local, cancel) requires a **fresh single-use TOTP code**, exactly like the
dashboard's step-up. Supply it with `--totp CODE` or on the first stdin line. For unattended
agent runs, point the wrapper at a secret file **outside the repository**:

```bash
export FLEET_TOTP_SECRET_FILE=/secure/path/fleet-totp-base32   # 0600, never in the repo
export FLEET_SSH=vps                                           # the primary's SSH alias
ops/fleet/fleet status          # no TOTP needed for read-only verbs
```

The wrapper derives one code per invocation and never reuses a 30-second step (state file beside
the secret). The secret never crosses SSH; the derived code does, and it is valid once.

## Health, failure and recovery behaviour

| Signal | Meaning | What happens without you |
|---|---|---|
| heartbeats every 15 s; `Stale` after 45 s; `Offline` after 600 s | helper quiet | claims stop immediately when stale; leases expire via the reaper (120 s) |
| lease expiry / worker death | job orphaned | attempt marked, job requeued with backoff (5 s→300 s, jittered), 3 attempts then `Quarantined` (dead-letter) |
| all helpers down | no claimer for a kind | after 10 min queued jobs fall back to the primary if it has headroom; after 60 min the primary runs them locally at concurrency 1 |
| helper returns / reboots | systemd restarts the agent (`Restart=always`) | node re-registers, heartbeats, passes readiness, receives work again — no SSH needed |
| admission pressure (CPU > 80% or mem-free < 20% for 15 s) | node strained | node sheds load (no new claims, youngest job released); restores after 120 s of relief |
| `Quarantined` job | attempts exhausted | never auto-run locally (poison-input loop protection); an admin inspects then `requeue` or `force-local` |
| control-plane (manager) down | dashboard unreachable | job flow unaffected — agents talk to the API, not the manager |
| API restarted | controller restart | durable state in Postgres; queue survives; agents probe with backoff and resume |

### Recovery guarantees (exact wording — do not oversell)

**Guaranteed:** queued jobs persist across worker *and* primary-service restarts (Postgres is the
source of truth); expired leases are recovered by the reaper; helper failure triggers
reassignment to another helper or the primary; the primary processes work with zero helpers;
node reboot rejoins automatically; a stale worker's completion is rejected by its fencing token;
credentials are revocable per node in one transaction.

**Best effort (at-least-once):** a job may execute more than once across a failover (executors
are idempotent: derived outputs are content-addressed/overwritten, committed results are fenced);
recovery latency is bounded by heartbeat + lease settings, not instant; a workload without
checkpoints restarts from its beginning.

### Remaining single points of failure (honest list)

The **primary VPS** is one machine: Postgres, the queue, the API, the fallback executor and the
manager all share it (helpers only add throughput). HA for the primary is future work and does
**not** require a consensus stack. The manager's vault key lives beside its ciphertext on the
primary (protects against casual disclosure, not full host compromise — `FLEET.md` §15).

## Security rules that must never drift

1. Helper IPs, SSH keys, node tokens, TOTP secrets and the fleet credential **never enter the
   repository, chat, PRs or logs** (the `platform/**` secret scan in `pnpm run pipeline:check`
   enforces the repo part).
2. Helpers run **one digest-pinned image and nothing else** — no checkout, build, source or
   mutable tags; no AI calls; no database/provider credentials.
3. The manager has **no public ingress**: loopback bind + SSH tunnel + owner password + TOTP +
   step-up. Never publish it through a proxy or a non-loopback `ports:` entry.
4. The primary is never a helper. Fleet code never uses `StrictHostKeyChecking=accept-new`.
5. No automated test/benchmark/QA runs anywhere (owner directive 2026-10-06): builds compile in
   `fleet.yml`, the owner QAs by hand through the dashboard and these CLI verbs. Do not add QA
   workflows to make a change feel safer.

## Upgrading the fleet software itself

Fleet changes merge like any other (PR, `Build images` guards green), then
`.github/workflows/fleet.yml` builds the images and a `sync` dispatch rolls the manager
(pull-only, `--no-build`, health-checked):

```bash
gh workflow run fleet.yml -f sync=true && gh run watch   # manager rollout
# agent images: approve the release digest in the dashboard, then:
ops/fleet/fleet upgrade --digest sha256:<64 hex>
```

Rollback: re-point `upgrade` at the previous approved digest (the approved window keeps the
current + two previous). API protocol changes roll the API first; agents accept N and N-1, so a
helper never needs a same-minute update.

## Adding the next VPS — the exact agent script

1. Ask the owner for: IP, SSH user, bootstrap key path, and the **first 12 hex of the provider
   console's SSH host-key fingerprint** (they read it out-of-band).
2. `ops/fleet/fleet add <name> --host <ip> --user <user> --confirm-fingerprint <12hex> --ssh-key <path>`
   (with `FLEET_TOTP_SECRET_FILE` exported, or `--totp` supplied by the owner).
3. Watch the operation stream to `STATUS: ACTIVE ... ENROLLMENT: PASS`; if it fails, it says the
   exact stage (`docs/ops/FLEET.md` §11 maps every failure reason); fix and re-run the same
   command — it resumes idempotently.
4. Prove it: `ops/fleet/fleet test <name>` → `SMOKE TEST: PASS`, then
   `ops/fleet/fleet status` and `ops/fleet/fleet rebalance` to see it taking slots.
5. Optionally widen the kinds it may run: `ops/fleet/fleet policy set <name> --kinds pdf.extract,media.audio-extract,media.speaking-join,companion.index-prep`
   (the kind's `remote_jobs_kind_*` feature flag must also be enabled — owner-gated).
6. Record the provider + region in the processor register (`FLEET.md` §15) — owner action.
