# Fleet manager and remote workers: isolated manager, digest-only helpers, API-owned job table

Status: accepted by the owner, 2026-10-05. Repo rules: `AGENTS.md` "Owner Fleet exception". Operations: `docs/ops/FLEET.md`.
Wire contract: OET Remote Worker Protocol (OET-RWP/1); section numbers below refer to it.

## Context

The primary VPS runs everything: the web and API slots, Postgres, the `ai-worker`, the media volume and the owner console. Some deterministic,
CPU-heavy work (first: PdfPig text extraction of admin-uploaded PDFs; later: live-class audio extraction and speaking clip joins) competes with
learner-facing requests for the same cores. The owner asked to share that compute load with rented helper VPSs, managed automatically, while the
primary stays the single place that holds data, credentials and decisions. The repository's compute and deployment rules (GitHub Actions is the only
build/test compute; the only path to production is `Build images` then `Deploy production`; the 510.240 s accelerated baseline is mandatory) were
written for a world with exactly one runtime host, so the fleet needs an explicit, narrow exception rather than a quiet bypass.

## Decision

Owner decisions of 2026-10-05 (authoritative):

1. **Manager host.** The fleet manager runs on the SAME primary VPS as the isolated compose project `oet-fleet`, with NO public ingress: loopback bind
   reached through an SSH tunnel, owner password + TOTP, lockout, short sessions. It sits on no production network and keeps its state in the external
   protected volume `oet-fleet_fleet_data`.
2. **Data.** ANY data may leave the primary for helpers. Engineering hygiene still applies: tmpfs scratch, delete-on-complete, no content in logs,
   per-job scoped access; helpers hold no database, provider or storage credentials.
3. **Transport.** Helpers talk to the primary over HTTPS with a per-node revocable token. There is no WireGuard and no overlay network.
4. **Delivery.** Branch + PR only; the work never pushes to `main` and never runs `pnpm run ship`.
5. **Rust is not built.** Reopened only by a labelled, dispatch-only benchmark that first shows byte-exact parity and then beats the .NET baseline by
   at least 30% p95 or 40% CPU per job.
6. **Max is untouched.** Helpers make no AI calls and hold no provider keys; the Claude Max route is never skipped; one `AiUsageRecord` per provider
   call stays on the primary.

Resulting architecture:

- **The API owns the work.** A hand-authored Postgres migration adds `RemoteJobs`, `RemoteWorkers` and `RemoteCredentials` (ADR 0001 style). A job is a
  fenced, at-least-once unit: claim is one `FOR UPDATE SKIP LOCKED` statement that increments a fence token; every mutating call carries the fence; the
  completion transaction is a single compare-and-swap that also runs the domain applier; the only reclaim path is a reaper (OET-RWP/1 section 3). No
  message broker is added and SignalR stays single-process.
- **Helpers are pull-only executors.** An agent claims jobs from the API, downloads inputs through job-scoped endpoints that stream with `IFileStorage`
  on the primary (ADR 0004 stays true: helpers never see the volume), executes, and posts a bounded JSON result that the API validates and commits.
  Everything a helper may do is delivered inside claim and heartbeat responses; the manager is never contacted by an agent.
- **The manager provisions; it does not schedule.** It owns inventory, enrollment, hardening, image rollout and removal, over SSH with pinned host
  keys and a forced-command gate on each helper. Ansible runs only inside the manager container, after an owner action. The owner's SSH key is used
  once to bootstrap, then crypto-erased; only a manager-generated, restricted key remains.
- **Helpers execute prebuilt images by immutable digest.** Bootstrap is the Docker engine plus the agent. A helper never checks out, builds, tests or
  installs source; the manager refuses `185.252.233.186` and any `oetwebsite` host.
- **A separate pipeline.** `.github/workflows/fleet.yml` builds, tests (no browser) and rolls the manager out pull-only. There is NO fifth release
  component and the `Build images` graph is not edited: a fifth component would break every historical manifest and the measured baseline.
  `scripts/deploy/verify-pipeline-contract.mjs` makes that mechanical: SSH allow-list, `fleet.yml` identity and guards, `platform/**` secret scan,
  exactly four components.
- **Console isolation first.** `oet-agent-dockerproxy` denies every `oet-fleet*` container, volume and network to console engines before the manager
  can exist.

Related trade-offs approved the same day (implemented by other tracks, recorded here because they bound the program): `KEEP_PREVIOUS_SLOT_RUNNING=false`;
5-30 s per-process caches of JWT account-state and entitlement/freeze; dropping the per-save Reading `AuditEvent`; a FIFO wait queue that caps
concurrent live AI speaking (waiting must not start the exam timer and must not hold or consume a credit).

## Consequences

- A new, owner-written exception in `AGENTS.md` (hosts, Ansible, credentials, pipeline, console, Rust) and a runbook, `docs/ops/FLEET.md`. Nothing else
  in the repository rules is relaxed; the primary VPS is still DEPLOY + SERVE only.
- A fleet-only push deploys nothing: `pnpm run ship` ends with `SHIP-WATCH_NOTHING_TO_DEPLOY`, and the change is verified with `gh run watch` on
  `fleet.yml`. Every API-side change for the remote boundary is default-off behind `FeatureFlags` rows and still costs one normal production release.
- Credential custody is weaker than a dedicated host: the vault master key sits beside its ciphertext on the primary. It protects against casual
  disclosure (backups, repo flips, logs, volume copies), not against full compromise of the manager host, so helpers only ever get the restricted
  `oet-fleet-ctl` surface and the manager has no public ingress.
- The VPS vendor of each helper becomes a processor of whatever a job carries. `Region` and `Provider` are recorded per node; privacy-notice and
  transfer-assessment updates are an owner/legal task.
- The 1000-learner target is unproven until a dispatch-only load workflow has a recorded run; no capacity figure is claimed without one.

## Alternatives rejected

- **Manager on a dedicated small VPS.** Cleaner blast radius but a second always-on host for the owner to run; rejected by the owner's decision 1.
  The isolation controls above exist because of that choice.
- **Public manager UI behind the proxy.** An internet-reachable admin surface holding keys able to root every helper. Rejected: no public ingress.
- **WireGuard overlay.** More moving parts, and a subnet inside `10.0.0.0/8` or `172.16.0.0/12` would be trusted as a forwarded-header proxy by the API.
- **A fifth release component for the agent.** Breaks historical manifests and the measured baseline (see Decision).
- **Reusing `auto-deploy-ghcr.sh` or the `Deploy production` workflow for helpers.** That is the production rollout; a second consumer of it, or a
  second SSH rollout workflow that is not allow-listed, is exactly what the pipeline contract forbids.
- **A broker (Redis, NATS, RabbitMQ).** Not needed for a pull-based, lease-fenced table; more state to run and secure.
- **Rust worker.** PdfPig output parity must be byte-exact for downstream parsers, the volume is small, and the DB-bound API paths gain nothing.

## Gates

1. This exception and the contract hardening land first (one `scripts/deploy/**` commit, one all-reuse production rollout).
2. The dockerproxy deny rule ships through `agent-console.yml` before the first manager deploy and is verified live (a push to `main` recreates the
   proxy when no console turn is active; otherwise dispatch with `apply=true` or use the console "Apply update").
3. Then `fleet.yml` and the manager; then the API remote endpoints default-off; then per-node enablement after a passing canary.
4. Every parity, benchmark and k6 number is quoted from a recorded Actions run (workflow, run, job, step); none is claimed from a local run.
