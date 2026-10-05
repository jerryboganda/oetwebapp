# OET fleet manager (platform/fleet)

The owner's console for renting helper VPSes and putting them to work. It runs as the isolated compose project
`oet-fleet` on the **primary** VPS with **no public ingress**, enrolls helpers over SSH, tracks their health through
the OET API, and decides where work could go. Normative wire contract: `OET-RWP/1` (the remote worker protocol; the
section numbers below refer to it). This folder is a separately governed pipeline: nothing here is a build input of
the production release graph (`platform/**` matches no path of `build-images.yml`).

```
platform/fleet/
  Fleet.sln                         pre-registers the agent projects (src/Fleet.Agent, tests/Fleet.Agent.Tests) built on another branch
  src/Fleet.Core/                   pure domain: state machines, validation, policy, placement, audit chain, vault + auth crypto, SSH rules
  src/Fleet.Manager/                ASP.NET Core 10: services, SQLite (EF Core), Ansible/SSH provisioner, owner auth, JSON API, SSE, /metrics
  tests/Fleet.Manager.Tests/        xUnit + WebApplicationFactory (no browser, no Playwright)
  ansible/                          playbooks (S1-S7), the helper-side gate and ctl, ansible.cfg, a data-only inventory template
  Dockerfile, docker-compose.fleet.yml
```

> `dotnet build Fleet.sln` needs the agent branch merged (the two agent projects are pre-registered here so the
> solution is complete after the merge). Until then build/test the manager with
> `dotnet test tests/Fleet.Manager.Tests`. Nothing is compiled or run on the workstation (AGENTS.md): CI does it.

## What it does

| Area | Where | Notes |
| --- | --- | --- |
| Enrollment | `Operations/EnrollmentService`, `OperationRunner`, `StepExecutor.*` | Persistent, resumable, idempotent operation (states of 8.1, steps S1-S13 of 8.2). A duplicate Add is a no-op that returns the existing operation. |
| Host-key trust | `Fleet.Core/Ssh/HostKeys`, `HostSecurityService` | `ssh-keyscan` output is display only. The owner types the first 8 characters of the fingerprint after comparing it with the provider console; only then is the key pinned. `StrictHostKeyChecking=yes` always; a changed key hard-fails, disables the host (and the API node) and raises an alert until an explicit re-pin. |
| Vault | `Fleet.Core/Crypto/Vault.cs`, `Vault/CredentialStore` | AES-256-GCM, layout and AAD of 8.5, master key from `/run/secrets/fleet_master_key` (missing or wrong length = startup fails), rewrap, write-only fields with a fingerprint hint. The owner key lives 60 minutes at most and is crypto-erased at S8. |
| Provisioning | `Provisioning/*`, `ansible/` | `IProvisioner` seam; the real one runs Ansible over OpenSSH (serialised, bounded forks, timeouts, values passed as `-e @file` JSON) and the restricted `oet-fleet-ctl` over SSH (the exact options of 7.4). |
| Health | `Monitoring/NodeMonitor` | Reads `GET /v1/internal/fleet/nodes` (the API is the authority, RW-144); never connects to an agent. SSH `status` of each active helper every 5 minutes. |
| Placement and policy | `Fleet.Core/Placement`, `Operations/PolicyService` | Capacity reservation, per-kind caps, resource budgets, drain/pause, least-normalised-load choice, fallback to the primary only with headroom (or after the 60-minute hard wait), pressure hysteresis (reduce after 15 s, restore after 120 s). Policy is validated against the 7.3 ranges and pushed with optimistic `expectedRevision`. |
| Maintenance | `HostService`, `StepExecutor.Maintenance` | drain, disable, enable, remove (only fleet-owned components), token rotation (14 days), repair, rolling image update (one host at a time, halts on the first failure). |
| Audit | `Fleet.Core/Audit`, `Persistence/AuditService` | Hash-chained audit and operations logs; verified at startup (a broken chain stops the process unless `Fleet:Audit:AllowBrokenChain=true`). |
| Owner access | `Auth/*`, `Endpoints/*`, `Pages/*` | Cookie auth, PBKDF2-SHA512 (>= 220k) password, TOTP with a replay guard on login AND on every privileged action, lockout (5 failures / 15 min), 20-minute idle and 60-minute absolute sessions, antiforgery on every state change, strict CSP. Only a login page and a health page exist; the dashboard is built on the JSON API later. |
| Telemetry | `GET /api/v1/events` (SSE), `GET /metrics` | Counts and ages only, never an address, token or key. |

## Security model (short)

* **No public ingress.** The listener is published on `127.0.0.1` of the primary (SSH tunnel). In the container the
  process listens on `0.0.0.0` inside its own network namespace; `StartupChecks` accepts that only when
  `Fleet__Binding__Mode=container`, and the container is attached to no production network.
* **No secrets in config.** `/run/secrets/*` only: `fleet_master_key`, `fleet_api_credential`, optional
  `fleet_master_key_prev`, `fleet_sync_token`, `fleet_metrics_token`.
* **Owner key** is submitted once through the tunnelled form into a write-only field, encrypted immediately, used by
  steps S1-S7 and destroyed at S8. A managed `string` cannot be zeroed: the request string exists until garbage
  collection (the pinned buffers that hold the key afterwards are zeroed). The master key lives next to the ciphertext,
  so the vault defends against backups, repo flips, logs and volume copies, not against a full compromise of the manager
  host (8.5); helpers therefore only ever get the restricted `oet-fleet-ctl` surface.
* **Injection.** Owner input is allow-list validated (`InputValidator`, `AddressGuard`: the primary, anything of the
  production deployment, private/loopback/link-local/CGNAT/ULA ranges, IPv4 shorthand tricks and the manager's own
  addresses are refused) and reaches child processes as argv elements or JSON data, never a shell string.
  Helper-originated text is stripped of ANSI/control characters, scrubbed, capped at 500 characters and HTML-encoded.
* **SSH.** `oet-fleet-gate` (forced command) and `oet-fleet-ctl` share one verb table with `Fleet.Core/Ssh/FleetCtlVerbs.cs`;
  a repository test fails when they drift. Trust on first use never appears anywhere in the fleet code (OpenSSH's
  auto-add host-key policy, `StrictHostKeyChecking=no` and a disabled Ansible host-key check are all scanned for).

## Database policy

SQLite (WAL, `foreign_keys`, `secure_delete`) in the external volume `oet-fleet_fleet_data`. Version 1 is created by
`EnsureCreated` from the EF model by `SchemaManager` and stamped in `schema_info`. Every later change ships as a
hand-written, numbered, forward-only SQL step in `SchemaManager.Upgrades` plus a `CurrentVersion` bump. A file stamped
newer than the build, or without `schema_info`, is refused. EF Core migrations are deliberately not used.

## Configuration

`Fleet__*` environment variables (see `FleetOptions`). The important ones:

| Variable | Default | Meaning |
| --- | --- | --- |
| `Fleet__Binding__Mode` | `loopback` | `container` only inside the compose project |
| `Fleet__Api__BaseUrl` | `https://api.oetwithdrhesham.co.uk` | service plane host (`/v1/internal/fleet`) |
| `Fleet__Image__Mode` | `scoped-token` | `public` when the agent package is public (no sync token needed) |
| `Fleet__Image__AutoApproveDigests` | `false` | approval of agent digests stays manual |
| `Fleet__Provisioning__DockerSource` | `distro` | `vendor` uses Docker's repository with the pinned key fingerprint |
| `Fleet__Timing__*` | see code | polling, timeouts, token rotation (14 days of a 30-day TTL) |
| `Fleet__Audit__AllowBrokenChain` | `false` | forensic boot only |

## First deployment (owner steps; the pipeline does the rest)

1. On the primary: create the secrets directory owned by uid 10020 (`chmod 0400` files): `fleet_master_key`
   (`openssl rand -hex 32`), `fleet_api_credential` (the `ofs1_...` value shown once by
   `POST /v1/admin/remote-workers/fleet-credential`).
2. `docker volume create oet-fleet_fleet_data` (the compose file pins it `external: true`).
3. Deploy the image with `docker compose ... up -d --no-build --wait` (the fleet workflow, a separate track).
4. Create the owner (password on stdin, one line):
   `docker exec -i oet-fleet-manager dotnet /app/Fleet.Manager.dll owner-init` and add the printed TOTP secret to an
   authenticator app. `--reset` replaces an existing owner.
5. Tunnel and sign in: `ssh -L 8480:127.0.0.1:8480 <primary>` then `http://127.0.0.1:8480`.

CLI: `owner-init [--reset]`, `healthcheck`, `sync-stdin` (CI sync document on stdin, authenticated by
`fleet_sync_token`), `vault-rewrap` (after rotating the master key: put the new key in `fleet_master_key` and the old one
in `fleet_master_key_prev`, run it, then delete the old key), `verify-chain`.

## Container hardening (the actual profile)

Read-only root filesystem, uid 10020, all capabilities dropped, `no-new-privileges`, 1 GiB / 1 CPU / 256 pids,
`oom_score_adj: 700`, writable paths = `/data` (volume) and tmpfs `/tmp` and `/home/fleet`. Ansible needs a writable
temp directory and the `ssh` client needs a passwd entry and a HOME: both are provided as tmpfs, and every run uses a
fresh 0700 directory under `/tmp/fleet` that is zeroed and deleted afterwards. No docker socket, no `oetwebsite_*`
volume, no production network. `/tmp` is `noexec`; if a delegated-to-localhost module ever needs exec there, relax that one
mount only. The memory limit is an estimate for `forks=2` and one helper at a time: prove it in an Actions job before
raising concurrency.

## Helper side (ansible/)

`fleet-user.yml` installs the restricted account, `oet-fleet-gate`, `oet-fleet-ctl` and the one-line sudoers drop-in;
`install-key.yml` writes the manager key behind `restrict,command="/usr/local/sbin/oet-fleet-gate",no-pty`; `docker.yml`,
`firewall.yml` (own nftables table, SSH from the primary and the connecting address only), `host-baseline.yml` (swap off,
no core dumps, volatile journal, chrony, `local` log driver) and `harden-ssh.yml` (verifies a NEW manager login before and
after, rolls back on any doubt) follow the order of 8.2. Only `ansible.builtin` modules are used. `oet-fleet-ctl` adds one
verb to the table of 7.4, `uninstall`, which removes ONLY fleet-owned components and locks the `oetfleet` account.
`run` also rewrites `OET_AGENT_IMAGE_DIGEST` in the env file so a rolling update never needs the node token.

The playbooks are covered by static repository tests (forbidden tokens, parity of the verb table, `restrict`), not by a
real Ansible run: do the first enrollment against a disposable helper.

## Tests

`tests/Fleet.Manager.Tests` runs the real services over a temp SQLite file with a fake outside world (`FleetWorld`: clock,
OET API, helpers, registry), so a 180-second verification window or a 60-minute credential lifetime costs no wall time.

| Folder | What it proves |
| --- | --- |
| `Domain`, `Validation`, `Placement`, `Vault`, `Persistence` | state machine table, strict inventory validation, placement/pressure rules, AES-GCM vault and master-key rules, schema policy, hash chains |
| `Operations` | enrollment end to end; duplicate Add; a kill at EVERY provisioner call (before and after the effect) resumes without a second user, key, container, node or token; drain, disable, enable, repair, remove (only fleet-owned components), token rotation, rolling update with halt and rollback; policy and release rules |
| `Monitoring` | API-authoritative health, lifecycle adoption after two polls, quarantine and changed-key alerts, 14-day rotation, pressure governor, placement service, metrics format |
| `Provisioning`, `Repository` | Ansible/ssh argument lists (no shell, `-e @file`, strict options), error mapping, the API client, secret files, host-key parsing, the helper `gate`/`ctl` (real python3 when present, refusal paths only), and static guarantees on the compose file, Dockerfile, playbooks and source |
| `Web` | login, lockout, one-use TOTP codes, step-up, antiforgery, sessions, headers, rate limit, the JSON API, SSE, metrics, the CI sync endpoint, startup refusals, the operator CLI |

Nothing has been run yet: there is no CI workflow for `platform/fleet` on this branch (the fleet workflow is a separate
track). Until it exists, `dotnet test tests/Fleet.Manager.Tests` on a developer machine or in a throw-away Actions job is
the way to run them.

## Deviations from the spec and open points

* The enrollment of a Probation node and rollout use the API eligibility of 3.8 literally: a canary is only claimable by an
  Active or Probation node, so the rolling update enables the node first and runs the canary after (draining it again on a
  mismatch) instead of before `enable`.
* The `GET /v1/internal/fleet/nodes` list and `GET /status` bodies are parsed tolerantly (a bare array or `items`/`nodes`;
  kinds as strings or objects) because the spec does not fix their envelope.
* `repair` and `uninstall` are additions: the operation kinds listed in 8.1 needed concrete steps.
* JSON enums (for example a placement decision's `kind`) go over the wire by name (`"Remote"`, `"Wait"`), never as numbers.
* The operator CLI prints only its result on stdout; informational logs are suppressed and warnings go to stderr, so the
  one-time TOTP secret of `owner-init` is never interleaved with log lines.
* A ctl-level failure reports the helper's own sanitised error sentence as the failure detail (not its raw JSON).
