# OET Owner Agent Console — sidecar

Node 22 / TypeScript sidecar that runs **Claude Code** (via the Claude Agent
SDK and its bundled CLI) and **OpenAI Codex** (`codex app-server`) for the
**owner only**, behind `/admin/agent-console`. It is an operations tool, not a
product AI feature: it is never an `AiProvider`, never reachable by learner
traffic, and writes no `AiUsageRecord`.

Read before changing anything here:

| Document | What it owns |
|---|---|
| [`CONTRACT.md`](CONTRACT.md) | **Authoritative** wire contract: sidecar HTTP API, shapes, SSE events, proxy callbacks, audit actions |
| [`docs/ops/OWNER-AGENT-CONSOLE.md`](../docs/ops/OWNER-AGENT-CONSOLE.md) | Runbook: architecture, enablement, connect flows, modes/taint, snapshots, kill switch, rotation, retention, risks |
| `AGENTS.md` → "Owner Agent Console exception" | What console sessions may and may not do |
| [`docs/AI-USAGE-POLICY.md` §20](../docs/AI-USAGE-POLICY.md) | Policy: owner-only, credentials, audit, data protection |

## Layout

```
agent-console/
  CONTRACT.md                 interface contract (sidecar ↔ API ↔ UI ↔ proxies)
  README.md                   this file
  Dockerfile                  node:22-bookworm-slim (digest-pinned); users agent (10002); pinned CLIs
  package.json                exact-pinned direct deps; no lockfile committed yet (Dockerfile + CI fall back to npm install)
  tsconfig.json, vitest.config.ts
  src/
    server.ts                 Fastify control server :8410 (token + owner-account checks, routes of CONTRACT §3) + runtime wiring
    config.ts                 env + /run/secrets configuration; refuses to start without the internal token
    contract.ts               wire shapes of the HTTP API and event stream (CONTRACT §3–§4)
    errors.ts                 HttpError → CONTRACT error envelope
    validate.ts               request-body validators (400 + stable code)
    log.ts                    pino logger
    env.ts                    allow-listed child env for the engines (strips API keys / OWNER_AGENT_*)
    exec.ts                   shell-free child-process runner (optionally as the agent uid)
    workspace.ts              clone, per-session worktrees on agent/<yyyymmdd>-<slug>, diff
    guard.ts                  table-driven classifier + modes + taint (seatbelt, not the boundary)
    approvals.ts              pending-approval registry (single-use nonces, expiry)
    sessions.ts               SessionManager: session lifecycle, turns, Guard + approval routing
    store.ts                  SQLite index + per-session JSONL (monotonic seq), 90-day retention sweep
    sse.ts                    SSE framing for the session event stream
    redact.ts                 redaction of every persisted / streamed event
    retention.ts              90-day cleanup of engine-native transcripts (as the agent uid)
    engine-registry.ts        lazy engine adapter loading
    status.ts                 GET /v1/status aggregation
    ship.ts                   push agent/* → PR → visibility lease → merge → watch deploy → health
    lease.ts                  browser-heartbeat lease + kill switch / stop-all
    github.ts                 agent PAT (uid agent) and Ship PAT (control only)
    docker.ts                 control-plane Docker API client (through oet-agent-dockerproxy)
    proxies.ts                control-plane calls to the egress / docker proxies (drop session grants)
    snapshot.ts               DB pre-snapshot before a destructive operation
    engines/
      types.ts                engine-neutral interface (mirrors CONTRACT §3–§4)
      claude.ts               Agent SDK query() adapter (PreToolUse hook → Guard)
      codex.ts                codex app-server JSON-RPC adapter (approval requests → Guard)
      codex-protocol.ts       hand-written app-server wire shapes + pure mappers
    auth/
      claude.ts               PTY-driven `claude auth login` (URL + paste-back code)
      codex.ts                ChatGPT device-code login
  tests/                      unit tests (no network, no real engines, no credentials)
  bin/
    entrypoint.sh             container entrypoint (control plane, uid 0)
    as-agent                  run a command as the agent uid (10002)
    claude-as-agent           SDK-bundled Claude Code binary as uid agent
    oet-console-erase         GDPR erasure of one session
  etc/
    MANUAL.md                 operating manual appended to every session's system prompt
    managed-settings.json     /etc/claude-code/managed-settings.json (managed deny rules + hooks only)
    codex-config.toml         $CODEX_HOME/config.toml (ChatGPT-only login, workspace pin, untrusted projects)
    oet.rules                 Codex execpolicy `forbidden` backstop
  scripts/
    oet-env-edit              the only sanctioned path for guarded .env edits
  egress/                     allowlist CONNECT proxy image (oet-agent-egress)
  dockerproxy/                Docker API policy proxy image (oet-agent-dockerproxy)
```

The DB proxy (`oet-agent-dbproxy`) is a stock `alpine/socat` forwarder with
no code in this folder. Files are added phase by phase (see the approved
plan); if the tree above and the folder disagree, the folder wins — update
this section in the same PR.

Related files outside this folder: `docker-compose.agent-console.yml`,
`.github/workflows/agent-console.yml`,
`scripts/ops/create-owner-agent-db-role.sql`,
`scripts/backup/postgres-backup.sh` (`--snapshot` mode),
`backend/src/OetLearner.Api/{Endpoints/OwnerAgentEndpoints.cs,Hubs/OwnerAgentHub.cs,Services/OwnerAgent/,Security/OwnerAgentAuthorizationHandler.cs}`,
`app/admin/agent-console/**`, `components/admin/agent-console/**`,
`lib/owner-agent/**`, `hooks/use-owner-agent.ts`, `lib/backend-proxy.ts`
(hub path pattern) and `scripts/deploy/nginx/api-bluegreen.conf.template`.

## Identities (summary — CONTRACT §2 is authoritative)

- **Control plane** (uid 0, capabilities dropped, `no-new-privileges`):
  HTTP server, Guard, session store, Ship executor. Only it can read the
  internal token, the proxy token, the Ship PAT and the session store.
- **Agent** (uid/gid 10002): both engines and every tool subprocess, spawned
  through `/usr/local/bin/as-agent` with the env built by `src/env.ts`.
  Outbound HTTP only via `oet-agent-egress`; Docker only via
  `oet-agent-dockerproxy`; Postgres only via `oet-agent-dbproxy` as role
  `oet_owner_agent`.

## Build, test and deploy — GitHub Actions only

Per `AGENTS.md`, nothing in this folder is installed, built, type-checked,
tested or run on a developer machine or on the VPS. No `npm install`,
`npm test`, `tsc`, `node`, `docker build` or `docker compose up` locally.
The loop is: edit → push a branch → GitHub Actions → read logs → fix → push.

`.github/workflows/agent-console.yml`:

- **Triggers:** push to `main` touching `agent-console/**`, and
  `workflow_dispatch` (input `apply=true` recreates containers even with
  active turns — the console's **Apply update** drains first, then
  dispatches with it).
- **Test job** (one matrix leg each for the sidecar, `egress/` and
  `dockerproxy/`): install (`npm ci` when a lockfile exists, else
  `npm install`), type-check, the unit tests in `tests/`, and
  `codex execpolicy check` on `etc/oet.rules` (sidecar leg only).
  `npm audit signatures` runs in the sidecar `Dockerfile` build stage.
- **Build job:** three images →
  `ghcr.io/jerryboganda/oetwebapp-agent-console{,-egress,-dockerproxy}:<sha>`.
- **Deploy job** (Environment `production`, `main` only): SSH to the VPS,
  create the internal networks / external volumes if absent, write
  `/opt/oetwebapp/.deploy/agent-console.env`, pull, and recreate only when
  `/healthz` reports `activeTurns: 0` (or `apply=true`).

To validate a branch, dispatch the workflow on it:

```bash
gh workflow run agent-console.yml --ref <your-branch>
# wait a few seconds so the new run is listed
gh run watch "$(gh run list -w agent-console.yml -b <your-branch> -e workflow_dispatch -L 1 --json databaseId -q '.[0].databaseId')"
```

Never claim tests pass without quoting the Actions run, job and step.

## Conventions

- **Contract first.** A change to a route, shape, header or event type
  starts in `CONTRACT.md`, and the same PR updates `src/engines/types.ts`,
  the .NET DTOs and `lib/owner-agent/*`. Model and effort ids are opaque
  strings reported by the engine; never enumerate them.
- **Tests never touch the network, real engines or credentials.** Fake the
  Agent SDK `query()` stream and the app-server JSON-RPC; the Guard, taint,
  egress and docker policies are table-driven, so every new rule — and
  every bypass string it must catch — gets a table row and a test.
- **Env allow-list.** A new variable reaches an engine only by being added
  to `src/env.ts` deliberately; `ANTHROPIC_*`, `CLAUDE_CODE_USE_*`,
  `OPENAI_API_KEY`, `CODEX_API_KEY` and `OWNER_AGENT_*` must stay stripped
  (a test asserts it).
- **Credentials.** The engines own their credential files. Sidecar code never
  reads, copies, logs or returns them; there is no API-key or paste-token
  login path. GitHub tokens are write-only.
- **No secrets in code, tests, fixtures or examples.** Use obviously fake
  values; the redactor's own tests build token-shaped strings at runtime.
- **Pinned versions.** Base image by digest; Agent SDK (with bundled CLI),
  `@openai/codex`, gitleaks and apt packages are pinned; auto-updaters stay
  disabled. Bumps go by PR. The Codex app-server wire shapes in
  `src/engines/codex-protocol.ts` are hand-written; before a `@openai/codex`
  bump, re-check every function marked VERIFY-ON-PIN against
  `codex app-server generate-ts` output of the new version (on Actions,
  never on a workstation).
- **Resource budget.** Hard caps are 3 GiB RAM / 1.5 CPU / 512 pids, with at
  most 2 live Claude queries + 1 Codex app-server. Don't add hosted
  services to the .NET API for console work; it runs in three processes.
