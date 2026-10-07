# Owner Agent Console — interface contract (v1)

Single source of truth shared by the sidecar (`agent-console/`), the .NET API
(`/v1/owner-agent/*`) and the admin UI (`/admin/agent-console`). Design and
rationale: `docs/ops/OWNER-AGENT-CONSOLE.md`. All JSON is camelCase. All
timestamps are ISO-8601 UTC strings. Ids are ULIDs (26 chars, Crockford
base32) unless stated otherwise.

## 1. Vocabulary

| Term | Values |
|---|---|
| `Engine` | `"claude"` \| `"codex"` \| `"opencode"` |
| `Mode` | `"read_only"` \| `"guarded"` \| `"autopilot"` |
| `SessionStatus` | `"idle"` \| `"running"` \| `"awaiting_approval"` \| `"interrupted"` \| `"error"` \| `"archived"` |
| `ApprovalDecision` | `"approve"` \| `"deny"` \| `"approve_session"` |
| Model / effort ids | **Opaque strings** reported by the engine at runtime. Never enumerate them in code. |

## 2. Processes, users, networks (sidecar container)

| Identity | Container uid | Runs | Can read |
|---|---|---|---|
| control ("console") | `0` with `cap_drop: ALL` + `cap_add: SETUID, SETGID, KILL, CHOWN, FOWNER, DAC_OVERRIDE`, `no-new-privileges` | Fastify control server, Guard, session store, Ship executor | everything in the container |
| agent | `10002:10002` | `claude` CLI (via Agent SDK), `codex app-server`, gateway Read/Write/Edit/Bash tool subprocesses (no OpenCode inference runtime) | `/home/agent` (engine creds, agent PAT), `/workspace`, `/opt/oetwebapp` (ro) |

- Engines are spawned through `/usr/local/bin/as-agent` (`setpriv --reuid=10002 --regid=10002 --clear-groups --reset-env`-style wrapper) with an allow-listed env (`src/env.ts`).
- Control-only secrets: `/run/secrets/owner_agent_internal_token` (mode 0400 root), `/var/lib/oet-agent/ship-token` (0400 root), session store `/var/lib/oet-agent/sessions` (0700 root).
- Networks: `oet_agent_ctl` (internal; API slots ↔ console :8410; console → API router :8080) and `oet_agent_net` (internal; sidecar ↔ `oet-agent-egress:3128`, `oet-agent-dockerproxy:2375`, `oet-agent-dbproxy:5432`).
- Agent env: `HTTPS_PROXY=HTTP_PROXY=http://oet-agent-egress:3128`, `NO_PROXY=oet-agent-dockerproxy,oet-agent-dbproxy,localhost,127.0.0.1`, `DOCKER_HOST=tcp://oet-agent-dockerproxy:2375`, `OET_AGENT_DATABASE_URL=postgres://oet_owner_agent:…@oet-agent-dbproxy:5432/<db>`.
- Direct OpenCode gateway uses the shared encrypted backend provider row. Console inference calls the API router over the private control network; no provider key, SDK, executable, server, OAuth or native auth store is used. Read/Write/Edit/Bash run through Guard/approvals and the UID 10002 runner. Root-only gateway transcripts retain opaque encrypted provider state. See [direct gateway runbook](../docs/ops/DIRECT-OPENCODE-GATEWAY.md).

## 3. Sidecar HTTP API (internal only)

Base: `http://oet-agent-console:8410`. Every route except `GET /healthz` requires:

- `X-Oet-Internal-Token: <token>` — constant-time compared; the server refuses to start if the token is missing or < 32 chars.
- `X-Oet-Owner-Account: <authAccountId>` — must be in `OWNER_AGENT_OWNER_ACCOUNT_IDS` (comma list).
- Optional `X-Request-Id`.

Errors: `{ "error": { "code": string, "message": string } }` with 400/401/403/404/409/423 (killed or draining)/429 (concurrency)/500.

| Method | Path | Body | Response |
|---|---|---|---|
| GET | `/healthz` | – | `{ ok: true, version, activeTurns, draining }` (no auth) |
| GET | `/v1/status` | – | `ConsoleStatus` |
| POST | `/v1/lease` | `{ expiresAt }` | `{ expiresAt }` (server clamps to now+3 min) |
| POST | `/v1/auth/:engine/connect` | Claude/Codex: –; OpenCode rejects sign-in (shared provider settings) | `ConnectFlow` |
| GET | `/v1/auth/:engine/flows/:flowId` | – | `ConnectFlow` |
| POST | `/v1/auth/:engine/code` | `{ flowId, code }` | `ConnectFlow` (Claude paste-code only) |
| POST | `/v1/auth/:engine/cancel` | `{ flowId }` | `ConnectFlow` |
| POST | `/v1/auth/:engine/logout` | – | `EngineAuth` |
| PUT | `/v1/github-tokens` | `{ agentToken?: string, shipToken?: string }` | `GithubStatus` (tokens are write-only, never returned) |
| GET | `/v1/sessions?q=&engine=&status=&includeArchived=false&before=&limit=` | – | `SessionSummary[]`, newest `updatedAt` first (filters below) |
| POST | `/v1/sessions` | `CreateSession` | `SessionDetail` |
| GET | `/v1/sessions/:id` | – | `SessionDetail` |
| PATCH | `/v1/sessions/:id` | `{ title?, mode?, model?, effort?, archived? }` | `SessionDetail` |
| POST | `/v1/sessions/:id/messages` | `{ text, model?, effort?, jevAdvisory? }` | `{ turnId }` |
| POST | `/v1/sessions/:id/interrupt` | – | `{ ok: true }` |
| POST | `/v1/sessions/:id/handoff` | `{ engine, model, effort? }` | `SessionDetail` (new session, same worktree/branch, seeded with a summary) |
| POST | `/v1/sessions/:id/approvals/:approvalId` | `{ decision, nonce, note? }` | `{ ok: true }` |
| GET | `/v1/sessions/:id/diff` | – | `SessionDiff` |
| POST | `/v1/sessions/:id/ship` | `{ prTitle?, prBody? }` | `ShipState` |
| GET | `/v1/sessions/:id/ship` | – | `ShipState \| null` |
| GET | `/v1/sessions/:id/events?after=<seq>` | – | `text/event-stream` (see §5) |
| POST | `/v1/admin/stop-all` | – | `{ stoppedTurns, killedProcesses }` |
| POST | `/v1/admin/drain` | `{ draining: boolean }` | `{ draining, activeTurns }` (`draining:false` also clears the kill-switch state = "resume") |
| POST | `/v1/admin/apply-update` | – | `{ draining: true, activeTurns, dispatched, detail? }` — drains, then dispatches `agent-console.yml` (`apply=true`, ref `main`) with the Ship PAT |
| POST | `/v1/admin/sessions/:id/erase` | – | `{ erased: true, engineTranscripts }` — GDPR erasure; control-plane only (SSH `oet-console-erase`), not relayed by the API; 409 while a turn runs |

Additive (v1.1): `ConsoleStatus` also carries `systemApprovals: ApprovalRequest[]` and
`systemSessionId: "00000000000000000000000000"` — the pseudo-session of the §6 "system"
queue, valid for `…/approvals/:approvalId` and `…/events` only. A wrong approval nonce is
409 `approval_nonce_mismatch`: the sidecar never answers 401/403 except for the API's own
credentials.

Additive (v1.2) — `GET /v1/sessions` query parameters, all optional (no parameters = every
non-archived session, as before). Unknown keys are ignored; a bad value (or a repeated key) is
400 `bad_request` in the usual envelope. Results are ordered `updatedAt` DESC (ties: `id` DESC).

| Param | Values | Meaning |
|---|---|---|
| `q` | ≤ 100 chars after trim, no control characters | case-insensitive (ASCII) substring of `title` or `firstMessage`; empty = no filter |
| `engine` | `claude` \| `codex` \| `opencode` | only that engine |
| `status` | `idle` \| `running` \| `awaiting_approval` \| `interrupted` \| `error` \| `archived` | `archived` = archived sessions only (regardless of `includeArchived`); any other value = non-archived sessions in that status |
| `includeArchived` | `true` \| `false` (case-insensitive) | also return archived sessions (default `false`) |
| `before` | ISO-8601 timestamp | only sessions with `updatedAt` strictly earlier — pass the last item's `updatedAt` to fetch the next page |
| `limit` | integer 1..200 | page size; default 200 when omitted |

A page shorter than `limit` is the last page.

#### Jev triage advice (additive v1.3)

`jevAdvisory` is optional on `POST /v1/sessions` (for `initialMessage`) and `POST /v1/sessions/:id/messages`
(for `text`); `null`/absent = no advice. It is **advice only, not authorization**: the sidecar quotes it to
the engine and never lets it choose the engine, model, reasoning effort or mode, grant an approval, widen a
session's authority or relax the Guard (the Guard and the Ship executor stay deterministic).

- Only a confident `ok` judgment is accepted. `status !== "ok"` or `requiresHumanReview !== false` → 409
  `jev_review_required` (defence in depth: the API never forwards one). A `model` that is not a pinned
  `jev-<major>.<minor>.<patch>`, or a confidence that is not a finite number in 0.5..1 → 400
  `jev_invalid_advisory`; a `taskKind` / `riskLevel` outside the `JevAdvisory` shape's lists (see Shapes) → 400 `bad_request`.
- `effortTier` is optional: absent or `null` adds nothing; any value outside `lookup` | `bounded_edit` |
  `cross_module` (including `unclear`) → 400 `bad_request`. When present, the engine prompt gets the line
  `Suggested effort tier: <tier> (advice only, not authorization)` inside the advisory preamble, in front of
  the owner's text. The recorded `user_message` stays the owner's text exactly.
- On `POST /v1/sessions` the advice is validated before any worktree exists; without an `initialMessage` it
  is 400 `jev_invalid_advisory`. A handoff summary is machine generated and never carries advice.

### Shapes

```ts
type ConsoleStatus = {
  version: string; draining: boolean; killed: boolean; updatePending: boolean;
  activeTurns: number; maxConcurrentTurns: number;
  lease: { expiresAt: string | null };
  engines: { claude: EngineStatus; codex: EngineStatus; opencode: EngineStatus };
  github: GithubStatus;
};
type EngineStatus = {
  engine: Engine; version: string | null;
  auth: EngineAuth;
  models: ModelInfo[];                 // empty until signed in
  rateLimits: RateLimit[] | null;      // null = unknown yet
  providers?: EngineProvider[];        // optional legacy wire metadata; absent for direct gateway
};
type EngineProvider = {
  id: string; name: string; connected: boolean;
  oauthMethods: { index: number; label: string }[];
  apiMethods: { index: number; label: string }[];
};
type EngineAuth = {
  state: "signed_out" | "signing_in" | "signed_in" | "error";
  account?: { email?: string; plan?: string; workspace?: string };
  detail?: string;
};
type ModelInfo = {
  value: string; displayName: string; description?: string;
  supportsEffort: boolean; efforts: string[]; defaultEffort?: string;
};
type RateLimit = {
  label: string; status: "ok" | "warning" | "limited" | "unknown";
  usedPercent?: number; resetsAt?: string;
};
type ConnectFlow = {
  flowId: string; engine: Engine; kind: "paste_code" | "device_code" | "api_key";
  state: "pending" | "awaiting_code" | "completed" | "failed" | "cancelled" | "expired";
  verificationUrl?: string; userCode?: string; expiresAt?: string; detail?: string;
  providerId?: string; providerName?: string; // legacy wire fields
};
type GithubStatus = { agentTokenSet: boolean; shipTokenSet: boolean; login?: string };
type CreateSession = {
  engine: Engine; model: string; effort?: string; mode: Mode;
  title?: string; initialMessage?: string;
  jevAdvisory?: JevAdvisory | null;    // v1.3: advice for `initialMessage` only (400 `jev_invalid_advisory` without one)
};
type JevAdvisory = {                   // v1.3: advice only, see "Jev triage advice" above
  status: "ok"; model: string;         // model: pinned judgment model id, /^jev-\d+\.\d+\.\d+$/
  requiresHumanReview: false;
  taskKind: "implement" | "debug" | "review" | "verify" | "plan" | "content" | "other";
  taskConfidence: number;              // 0.5..1
  riskLevel: "low" | "elevated" | "high";
  riskConfidence: number;              // 0.5..1
  effortTier?: "lookup" | "bounded_edit" | "cross_module" | null;  // absent/null = Jev was unsure or "unclear"
  reason?: string | null;              // ignored
};
type SessionSummary = {
  id: string; title: string; engine: Engine; model: string; effort?: string; mode: Mode;
  status: SessionStatus; branch: string; tainted: boolean;
  createdAt: string; updatedAt: string; lastSeq: number;
  usage: { inputTokens: number; outputTokens: number; costUsd?: number };
  createdBy?: string;      // v1.2: owner account id (X-Oet-Owner-Account, lower-cased) that created it; absent for older sessions
  firstMessage?: string;   // v1.2: first 200 chars of the first user_message, redacted, whitespace collapsed; absent until one is sent
};
type SessionDetail = SessionSummary & {
  pendingApprovals: ApprovalRequest[];
  pr?: { number: number; url: string; state: string };
  handoffFrom?: string;
};
type SessionDiff = {
  baseRef: string; head: string; branch: string;
  files: { path: string; status: "added" | "modified" | "deleted" | "renamed" | "untracked"; additions: number; deletions: number }[];
  patch: string; truncated: boolean;   // patch capped at 2 MB
};
type ShipState = {
  shipId: string; sessionId: string;
  phase: "queued" | "scanning" | "pushing" | "pr_open" | "visibility" | "merging" | "deploying" | "health" | "restoring_visibility" | "done" | "failed";
  prNumber?: number; prUrl?: string; mergeSha?: string; runUrl?: string;
  error?: string; startedAt: string; updatedAt: string;
};
type ApprovalRequest = {
  approvalId: string; nonce: string; toolCallId: string;
  summary: string; command?: string; cwd?: string; uid: number; target?: string;
  reasons: string[]; tainted: boolean; expiresAt: string;
};
```

## 4. Events

Envelope (persisted as one JSON line per event in `sessions/<id>/events.jsonl`):

```ts
type AgentEvent = { seq: number; sessionId: string; turnId?: string; ts: string; type: string; data: object };
```

`seq` is monotonic per session starting at 1. `heartbeat` is never persisted and has no `seq`.

| `type` | `data` |
|---|---|
| `turn_started` | `{ model, effort?, mode }` |
| `user_message` | `{ text }` |
| `text_delta` | `{ messageId, text }` |
| `text` | `{ messageId, text }` (final text of an assistant message) |
| `thinking_delta` | `{ text }` (summarized thinking) |
| `tool_call` | `{ toolCallId, name, input, command?, cwd?, classification?: { destructive, unparseable, reasons[] } }` |
| `tool_output_delta` | `{ toolCallId, text }` |
| `tool_result` | `{ toolCallId, ok, output, exitCode? }` (output capped at 64 KB) |
| `file_change` | `{ path, changeKind: "add" \| "modify" \| "delete", diff? }` |
| `approval_request` | `ApprovalRequest` |
| `approval_resolved` | `{ approvalId, decision, by: "owner" \| "autopilot" \| "lease_expired" \| "kill" \| "timeout" }` |
| `snapshot` | `{ approvalId?, label, file?, ok, error? }` |
| `taint` | `{ reason, source }` |
| `mode_changed` | `{ mode, reason }` |
| `usage` | `{ model, inputTokens, outputTokens, cacheReadTokens?, costUsd? }` |
| `rate_limit` | `{ engine, limits: RateLimit[] }` |
| `turn_complete` | `{ status: "ok" \| "interrupted" \| "error" \| "max_turns", durationMs }` |
| `error` | `{ code, message }` |
| `ship` | `{ phase, message, level: "info" \| "warn" \| "error" }` |
| `heartbeat` | `{}` (every 15 s on idle streams) |

SSE framing: `id: <seq>\nevent: <type>\ndata: <AgentEvent JSON>\n\n`. `?after=N` replays every persisted event with `seq > N`, then streams live.

## 5. Public API (.NET) — `/v1/owner-agent`

Policy `OwnerAgent`: role `admin` + `email_verified` + permission `system_admin` + `auth_account_id ∈ OwnerAgent:OwnerAccountIds` (env only) + valid unlock ticket. Feature flag `owner_agent_console` (uncached, fail-closed ⇒ 503).

| Method | Path | Extra requirement | Behaviour |
|---|---|---|---|
| GET | `/me` | owner (no unlock) | `{ isOwner, unlocked, unlockExpiresAt?, absoluteExpiresAt?, featureEnabled }` — non-owners get `{ isOwner:false }` with 200 so the nav can hide the entry |
| POST | `/unlock` | owner (no unlock) | body `{ password, code }` ⇒ `{ ticket, expiresAt, absoluteExpiresAt }` + `Set-Cookie: oet_owner_unlock=<ticket>; HttpOnly; Secure; SameSite=Strict; Path=/; Max-Age=<remaining s>`. Fixed lifetime `OwnerAgent:UnlockMinutes` (default 60, clamped 5..480) from the unlock; `expiresAt == absoluteExpiresAt`. `ticket` stays in the body for non-browser callers only — the admin UI never reads it |
| POST | `/unlock/refresh` | unlock | ⇒ same shape; re-mints with the SAME expiry (never extends) and re-sets the cookie. Back-compat only |
| POST | `/lock` | unlock | durable revocation watermark (every ticket issued before it dies) + `Set-Cookie` expiring `oet_owner_unlock` |
| * | every sidecar route in §3 under `/v1/owner-agent/…` (minus `/healthz`, `/v1` prefix dropped: e.g. `GET /v1/owner-agent/status`, `POST /v1/owner-agent/sessions/{id}/messages`, `POST /v1/owner-agent/kill-switch` → `/v1/admin/stop-all`, `POST /v1/owner-agent/apply-update` → drain + dispatch `agent-console.yml`) | unlock | pass-through JSON |
| GET | `/audit?take=100` | unlock | `{ items, chainIntact }` — latest `AuditEvent` rows with `ResourceType = "OwnerAgent"` (newest first, `details` as a JSON object) |
| POST | `/resume` | unlock | → `/v1/admin/drain {draining:false}`; undoes the kill switch / a drain |

Additive (v1.1): `POST /apply-update` → sidecar `/v1/admin/apply-update`, answered as
`{ draining, activeTurns, dispatched, instructions }`; `/me` adds `unlockBlockedUntil`.
v1.2: the per-action TOTP step-up (`POST /step-up`, `X-Owner-Agent-StepUp`) is removed —
one unlock covers every action (engine connect/logout, GitHub tokens, Autopilot, Ship) for its lifetime.
Sidecar 4xx bodies are relayed as `{ code, message, retryable, correlationId, error: { code, message } }`
(flat for the app's shared API client, nested per §3); sidecar 401/403/5xx become 502.

Jev triage (additive v1.3). While `TypeSafe:Enabled` and `TypeSafe:DevelopmentTriageEnabled` are both on, the
API screens the owner's own text before relaying it: `initialMessage` of `POST /sessions` (if present) and
`text` of `POST /sessions/{id}/messages`, through one shared helper. One batched Jev call returns `task_kind`,
`risk_level` and the advisory `effort_tier`; the call is recorded as an `AdminBatch` `AiUsageRecord`
(`jev.development.triage`). It is advice for a break-glass console, never a gate on it:

| Jev outcome | API behaviour |
|---|---|
| flags off | no Jev call, no extra work; body relayed unchanged |
| `unavailable` (no key, outage, open breaker, timeout, oversized, bad config) | **fail-open**: logged, message relayed **without** `jevAdvisory`. There is no `jev_unavailable` error and no 503 for it |
| `review_required`, trimmed message ≥ 60 chars | `409 jev_review_required`; nothing is relayed, no session is created |
| `review_required`, trimmed message < 60 chars (e.g. "continue") | relayed **without** `jevAdvisory` (too little context to triage) |
| `ok` | relayed with `jevAdvisory` (incl. `effortTier` only when confident and not `unclear`) |

Only an `ok` advisory is ever forwarded (the sidecar 409s anything else). A handoff summary is never
screened. `effortTier` is advice only and never feeds `review_required`. The `session_created` (only when an
`initialMessage` was screened) and `message_sent` audit rows carry `jevStatus`, `jevModel`, `jevTask`,
`jevRisk`, `jevEffort`, `jevReason`.

Unlock ticket presentation: the API reads the `X-Owner-Agent-Unlock` header first, else the
`oet_owner_unlock` cookie (never a query string). The browser relies on the HttpOnly cookie only
(sent through the same-origin Next `/api/backend` proxy, `credentials: 'include'`), so reloads, new
tabs and other admin pages stay unlocked until the fixed expiry; `/me` reports
`unlocked`/`unlockExpiresAt` from the cookie. The ticket is bound to the session's `sfam` and
account, so sign-out (session-family revocation; `/v1/auth/sign-out` also expires the cookie),
`/lock` and an authenticator re-enrolment all revoke it. Mutations also carry the app's normal
`x-csrf-token`; SameSite=Strict keeps the cookie off cross-site requests.

Hub: `/v1/owner-agent/hub`, single server-streaming method `Stream(string sessionId, long afterSeq)` → `IAsyncEnumerable<AgentEvent>`; negotiate and every long-poll request carry the unlock cookie (or the `X-Owner-Agent-Unlock` header via the SignalR `headers` option); the unlock is re-validated per forwarded batch and the stream ends at the fixed expiry.

## 6. Proxies ↔ sidecar (network `oet_agent_net`)

Shared secret `OWNER_AGENT_PROXY_TOKEN` (file `/run/secrets/owner_agent_proxy_token` in all three custom containers; ≥ 32 chars). The agent uid can never read it.

**Session attribution**
- Egress (`oet-agent-egress:3128`, HTTP CONNECT + plain HTTP proxy): the sidecar sets each session's `HTTPS_PROXY`/`HTTP_PROXY` to `http://<sessionId>:x@oet-agent-egress:3128`; the proxy reads the session id from `Proxy-Authorization: Basic`.
- Docker (`oet-agent-dockerproxy:2375`, Docker Engine API over TCP): the sidecar gives each session its own `DOCKER_CONFIG` dir whose `config.json` holds `{"HttpHeaders":{"X-Oet-Agent-Session":"<sessionId>"}}`.
- Requests from the control plane (snapshots, stop-all container cleanup) send `X-Oet-Control-Token: <proxy token>` and bypass policy.

**Approval callback** — proxies call the sidecar:

`POST http://oet-agent-console:8410/internal/approvals` with header `X-Oet-Proxy-Token`, body
`{ source: "egress" | "docker", sessionId: string | null, summary, target, reasons: string[], details?: object }`
→ blocks until decided (≤ 600 s) → `{ decision: "approve" | "deny", scope: "once" | "session" }`.
The sidecar applies mode + taint rules (read_only ⇒ deny; guarded ⇒ owner card; autopilot ⇒ auto-approve unless the session is tainted ⇒ owner card; unknown/absent session ⇒ owner card on a global "system" queue shown on the console home) and emits `approval_request` / `approval_resolved` events on the session stream.

**Egress policy** — static allowlist (suffix match): `api.anthropic.com`, `claude.ai`, `console.anthropic.com`, `platform.claude.com`, `statsig.anthropic.com`, `chatgpt.com`, `auth.openai.com`, `api.openai.com`, `ab.chatgpt.com`, `github.com`, `api.github.com`, `githubusercontent.com`, `ghcr.io`, `registry.npmjs.org`, `oetwithdrhesham.co.uk`, `opencode.ai`, `models.opencode.ai`, `oaiusercontent.com`. Anything else ⇒ approval callback; `scope:"session"` adds the host for that session until the session ends. Denied ⇒ `403` with body `blocked by oet-agent-egress: <host>`.

**Docker policy** — first match wins:
1. `deny`: any request touching containers `oet-agent-*`, the egress/dockerproxy/dbproxy containers, or the console volumes; and (owner directive 2026-10-05) ANY container, volume or network whose name starts with `oet-fleet` (the Owner Fleet manager, compose project `oet-fleet`): reads, logs, lifecycle, exec, create/mount/attach/network-connect and delete are all denied outright, never gated; those names are also hidden from list and event responses.
2. `allow`: `GET` on containers/images/networks/volumes/events/info/version whose name matches `^/?(oet-|oetwebsite)`; responses of container inspect have `Config.Env` values replaced with `"<redacted>"`; `GET /containers/json` lists are filtered to OET names.
3. `allow`: lifecycle `POST /containers/{oet-*}/(start|stop|restart|kill|pause|unpause|wait|resize|attach)` and `GET …/logs` for `oet-*` except `oet-postgres`.
4. `approval`: `POST /containers/{id}/exec` + `/exec/{id}/start` for `oet-postgres`, `oet-api-*`, `oet-ai-worker`, `oet-db-backup`; `POST /containers/create` (any), `DELETE` anything, `POST /volumes/prune|/containers/prune|/images/prune|/system/prune`, network changes, any non-OET resource; creates with `Privileged`, `CapAdd`, `Devices`, `PidMode/NetworkMode/IpcMode = host`, or binds outside `/opt/oetwebapp` and `/var/opt/oet-learner/releases`.
5. `deny`: everything else.
Every decision is logged as JSON lines to stdout (`docker logs oet-agent-dockerproxy`).

## 7. Audit

API-side `AuditEvent.ResourceType = "OwnerAgent"` actions: `unlock`, `unlock_failed`, `lock`, `engine_connect`, `engine_logout`, `github_tokens_updated`, `session_created`, `message_sent`, `approval_decided`, `mode_changed`, `ship_started`, `kill_switch`, `apply_update`, `resume`. `Details` never contains secrets or message bodies beyond the first 200 chars. When Jev triage ran, `message_sent` / `session_created` details also carry `jevStatus`, `jevModel`, `jevTask`, `jevRisk`, `jevEffort`, `jevReason` (§5, "Jev triage").

