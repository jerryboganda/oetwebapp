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
  tests/Fleet.Manager.Tests/        inert xUnit sources for the owner's manual use; no workflow runs them (see "Tests")
  ansible/                          playbooks (S1-S7), the helper-side gate and ctl, ansible.cfg, a data-only inventory template
  Dockerfile, docker-compose.fleet.yml
```

> The solution pre-registers the two agent projects (`src/Fleet.Agent`, `tests/Fleet.Agent.Tests`) that arrive with the
> agent layer, so `Fleet.sln` only resolves once that layer is merged; the manager image never builds the solution, it
> publishes `src/Fleet.Manager/Fleet.Manager.csproj` (see `Dockerfile`). Nothing is compiled or run on the workstation
> (AGENTS.md): the only automated check on this code is compilation inside the build-only `.github/workflows/fleet.yml`
> (also delivered with the agent layer).

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
| Owner access | `Auth/*`, `Endpoints/*`, `Pages/*` | Cookie auth, PBKDF2-SHA512 (>= 220k) password, TOTP with a replay guard on login AND on every privileged action, lockout (5 failures / 15 min), 20-minute idle and 60-minute absolute sessions, antiforgery on every state change, strict CSP. The owner console (Razor Pages, below) is built on the same services as the JSON API. |
| Telemetry | `GET /api/v1/events` (SSE), `GET /metrics` | Counts and ages only, never an address, token or key. |

## The owner console (dashboard)

Server-rendered Razor Pages (`src/Fleet.Manager/Pages`) over the same application services the JSON API uses, plus one small script
(`wwwroot/js/console.js`) for live updates. Reached through the SSH tunnel only, behind the sign-in (password + TOTP). Dense, operational,
keyboard-friendly (skip link, labelled controls, captioned tables, visible focus, light/dark, responsive); no marketing styling.

| Screen | Route | What it shows and does |
| --- | --- | --- |
| Fleet overview | `/` | The primary (host-wide CPU, memory pressure and headroom) and every helper: state, API health, heartbeat age, load, slots in use, capacity, SSH-status latency, agent, the open operation; totals, utilisation, offline helpers and a "needs your attention" list. Refreshes live; **Refresh now** polls the API at once. |
| Add helper | `/Hosts/Add` | IP or host name, optional name, SSH user (default `root`), port (default 22), region and provider, and the temporary SSH key (paste, or choose a file that is read in the browser) with its optional passphrase. Everything is validated before the authenticator code is used. |
| Enrollment progress | `/Operations/Detail/{id}` | The live, durable progress of any operation: steps, failure reason with plain-words guidance, the host-key check (type the first 8 characters), the key prompt (save in advance, use now, replace), retry and cancel. Survives closing the browser: the work is on the server. |
| Server detail | `/Hosts/Detail/{id}` | Summary, pinned host key, hardware, installed components (Docker, agent container, image, fleet-owned list), assigned workloads and limits, health history, operations and audit trail of that helper, credential hints; actions: drain, resume, disable, repair, rotate token, change limits (per-helper override), re-pin a changed host key, remove from the fleet (drain first, typed confirmation, only fleet-owned components). |
| Workloads | `/Workloads` | Per job kind: project, queue depth, active jobs, outcomes, completion rate, eligible helpers and where the next job would go with the reason (the placement engine run as a what-if, nothing reserved). |
| Policies | `/Policies` | The global policy (allocation, concurrency, budgets, pressure, polling) with a step-up save that is pushed to every helper, the fixed priority and fallback rules in words, and which helpers have limits of their own. |
| Operations | `/Operations` | History of provisioning, repair, maintenance and rollouts (filters), agent releases with approve and rollout, and the audit trail with hash-chain verification. |
| Credentials | `/Credentials` | Write-only: temporary owner keys (add, replace, erase early), node tokens (rotate), manager SSH keys, the presence of the service secret files, the vault key id, the in-memory registry token (discard). Only fingerprint hints and dates are ever shown. |
| Projects | `/Projects` | The primary's pressure, the OET integration status (API reachable, credential, protocol, kinds, helpers, releases, sync endpoint) and what the console cannot see. |

How it is kept safe:

* **Auth and CSRF.** Every page and fragment needs the owner session (the fallback policy; only `/Login` is open). Every POST needs the
  antiforgery token (Razor Pages validate it; each form carries `asp-antiforgery`). A privileged action also needs a fresh authenticator
  code, checked by the SAME replay-guarded step-up as the JSON API; the form is validated first, so a typo never burns a code. Pressing Enter
  in the code box cannot run an action nobody picked (one radio choice and one button on the host page; one button per mini form elsewhere).
* **No inline script, no nonce needed.** The CSP stays `script-src 'self'; style-src 'self'`: all script and CSS are files, there is no inline
  `<script>`, `style=` or event handler anywhere (a repository test scans for them), and the markup never uses a raw-HTML escape hatch.
* **Helper text is untrusted.** Everything a helper, the OET API or a child process says goes through `Fmt.Untrusted` (decode the manager's own
  storage encoding once, strip ANSI and control characters, redact credential shapes, cap) and then Razor's encoder.
* **Secrets are write-only.** A key or passphrase is moved out of the bound property at once, never echoed (a refused form comes back
  without it), never audited, never logged, cleared from the browser when the page is left, and shown afterwards only as an 8-character
  fingerprint hint of the public key. Service secrets are files: the console says present or absent, never reads one out.
* **Live updates.** The script re-fetches server-rendered fragments (`?handler=Fragment`) when the manager's SSE stream (`/api/v1/events`)
  says something changed, and on a timer as a fallback. Fragments never contain a form, so what you are typing is never replaced; a change
  of state that alters what you can do reloads the page unless something is typed in it. A session that ends shows a sign-in link.
* **Notices are fixed text.** A redirect carries a code from a table in `FleetPageModel`, never request text.

A key can be saved in advance (`StageOwnerCredentialAsync`): it is the same encrypted, 60-minute vault credential, but it is only used after
the host key is pinned, at which point confirming the key continues the enrollment with it. A key protected by a passphrase is opened once with
`ssh-keygen -p` on a copy in the tmpfs run directory (OpenSSH has no stdin or environment route for the old passphrase, so it is an argument of
that one 15-second call inside the container); only the unprotected key continues, the passphrase is kept nowhere.

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
  a repository test (a manual tool, see "Tests") is written to fail when they drift. Trust on first use never appears anywhere in the fleet code (OpenSSH's
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
3. Deploy the image with `docker compose ... up -d --no-build --wait` (the build-only fleet workflow's pull-only rollout).
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
mount only. The memory limit is an estimate for `forks=2` and one helper at a time: the owner watches the first real
enrollment before raising concurrency (no benchmark job exists for it, owner directive 2026-10-06).

## Helper side (ansible/)

`fleet-user.yml` installs the restricted account, `oet-fleet-gate`, `oet-fleet-ctl` and the one-line sudoers drop-in;
`install-key.yml` writes the manager key behind `restrict,command="/usr/local/sbin/oet-fleet-gate",no-pty`; `docker.yml`,
`firewall.yml` (own nftables table, SSH from the primary and the connecting IPv4 or IPv6 address only; the table is loaded
live, a NEW connection is proven, and only then is it written to disk, so a failed proof leaves nothing for a reboot to
reload), `host-baseline.yml` (swap off, no core dumps, volatile journal, chrony, `local` log driver) and `harden-ssh.yml`
(verifies a NEW manager login before and after, rolls back on any doubt) follow the order of 8.2. Only `ansible.builtin`
modules are used; `ansible.cfg` sets `ssh_args` explicitly so Ansible's default `ControlMaster=auto`/`ControlPersist=60s`
cannot override the manager's `ControlMaster=no`. `oet-fleet-ctl` adds one verb to the table of 7.4, `uninstall`, which
removes ONLY fleet-owned components and closes the `oetfleet` login as its very last act (key and gate first, account lock
last), so a retry after a lost response can tell from a refused login that it already ran.
`run` also rewrites `OET_AGENT_IMAGE_DIGEST` in the env file so a rolling update never needs the node token. `restart`
RECREATES the container from the env file (Docker bakes `--env-file` in at creation, so a plain `docker restart` would keep the
old node token after a rotation); token rotation then waits for a heartbeat from a NEW agent instance id. `status` works on a
helper without Docker (S3 proves the login before S4 installs it), `run` clamps the budgets to the machine's cores and RAM,
and the manager runs `prune` (running image plus the two newest others are kept) after enrollment and after every rollout.

The playbooks have never been run: nothing in this repository executes Ansible or the helper scripts automatically
(AGENTS.md, Owner Fleet exception (b)). The first enrollment is the owner's own manual check, against a disposable helper.
The static repository tests described under "Tests" (forbidden tokens, parity of the verb table, `restrict`) are
manual tools too, not a gate.

## Tests

**Not tested, owner QA.** `tests/Fleet.Manager.Tests` is a set of inert manual tools (owner directive 2026-10-06, "NO AUTOMATED
QA ANYWHERE"): no workflow, job or hook runs it, no agent runs it, and nothing here has ever been run, so no result of
any kind is claimed for this code. The sources stay in git so the owner can run them by hand if wanted; they are
never wired to CI and `pipeline:check` rejects any workflow that would. The only automated check on the fleet code is
that it compiles inside the build-only fleet workflow.

The suite is written to run the real services over a temp SQLite file with a fake outside world (`FleetWorld`: clock,
OET API, helpers, registry), so a 180-second verification window or a 60-minute credential lifetime would cost no wall time.

| Folder | What it is written to check |
| --- | --- |
| `Domain`, `Validation`, `Placement`, `Vault`, `Persistence` | state machine table, strict inventory validation, placement/pressure rules, AES-GCM vault and master-key rules, schema policy, hash chains |
| `Operations` | enrollment end to end; duplicate Add; a kill at EVERY provisioner call (before and after the effect) resumes without a second user, key, container, node or token; drain, disable, enable, repair, remove (only fleet-owned components), token rotation, rolling update with halt and rollback; policy and release rules |
| `Monitoring` | API-authoritative health, lifecycle adoption after two polls, quarantine and changed-key alerts, 14-day rotation, pressure governor, placement service, metrics format |
| `Provisioning`, `Repository` | Ansible/ssh argument lists (no shell, `-e @file`, strict options), error mapping, the API client, secret files, host-key parsing, the helper `gate`/`ctl` (real python3 when present, refusal paths only), and static guarantees on the compose file, Dockerfile, playbooks and source |
| `Web` | login, lockout, one-use TOTP codes, step-up, antiforgery, sessions, headers, rate limit, the JSON API, SSE, metrics, the CI sync endpoint, startup refusals, the operator CLI |
| `ConsoleViews`, `Web/Console*` | the owner console: every page and fragment (authz, antiforgery, step-up, validation errors, encoding of helper text, no secret in any response), the read models, the formatting and parsing helpers, the passphrase and saved-key enrollment path, static rules on the markup and the script |

## Deviations from the spec and open points

* The enrollment of a Probation node and rollout use the API eligibility of 3.8 literally: a canary is only claimable by an
  Active or Probation node, so the rolling update enables the node first and runs the canary after (draining it again on a
  mismatch) instead of before `enable`.
* The `GET /v1/internal/fleet/nodes` list and `GET /status` bodies are parsed tolerantly (a bare array or `items`/`nodes`;
  kinds as strings or objects) because the spec does not fix their envelope.
* `repair` and `uninstall` are additions: the operation kinds listed in 8.1 needed concrete steps.
* The helper-side Ansible in `ansible/` (flat playbooks, a Python `oet-fleet-ctl`/`oet-fleet-gate`, vars such as
  `fleet_manager_pubkey`) is the tree `AnsibleProvisioner` drives and the manual repository tests are written against. The `feat/fleet-agent*`
  branches ship a second, role-based tree under the same path (`roles/`, `bootstrap.yml`, a bash ctl, other variable names,
  `requirements.yml`). Exactly one of the two must survive the merge: this README and `FleetRepositoryTests` assume this one
  (builtin modules only, no `requirements.yml`, a single inventory template).
* JSON enums (for example a placement decision's `kind`) go over the wire by name (`"Remote"`, `"Wait"`), never as numbers.
* The operator CLI prints only its result on stdout; informational logs are suppressed and warnings go to stderr, so the
  one-time TOTP secret of `owner-init` is never interleaved with log lines.
* A ctl-level failure reports the helper's own sanitised error sentence as the failure detail (not its raw JSON).
* Console: "latency" is the round trip of the restricted SSH status call (recorded by the node monitor every 5 minutes for active helpers);
  the API heartbeat age is shown next to it. The health history is kept in memory per node (100 changes) and starts again at a restart; the
  durable record is the audit trail.
* Console: the manager SSH key cannot be rotated from the console (the install-key step skips itself when the key already works); remove
  and re-add the helper for a new one. The Credentials page says so.
* Console: the manager has no container runtime access by design, so "resource use across primary-hosted projects" is the host-wide pressure
  the placement rules already use plus the console's own footprint; per-project attribution is not possible without such access.
* Console: "saved key" means a key stored in advance for ONE enrollment (60 minutes, encrypted, used only after the host key is pinned). A key
  that outlives its enrollment would contradict OET-RWP/1 section 8.2 (S8 destroys the owner credential), so there is no cross-host key store.
* Console: the passphrase of a protected key is one argument of one `ssh-keygen -p` call inside the container (see above); the alternative is
  to give the manager only keys without a passphrase, which the JSON API also still accepts.
