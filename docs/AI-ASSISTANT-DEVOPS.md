# Admin AI Assistant — DevOps & Release Plan

**Current status:** This is a future devbox/pgvector deployment design. The shipped Phase 1 assistant does not deploy `oet-devbox`, does not run shell/git/restart tools, and does not require pgvector. Do not execute the production actions in this document without explicit owner approval and the normal OET production safety gates.

> Produced by the `agency-devops` subagent. **Plan only.** No code edits or deploy commands executed.
> Cites `AGENTS.md` "Deployment", "Security Considerations", and the Runtime Settings gotcha.
> All production-affecting steps require explicit owner approval per repo Operational Safety rules.

## 0. Scope & Invariants Recap

From `AGENTS.md` → **Deployment**:

- Prod VPS `185.252.233.186`, app root `/opt/oetwebapp/`, Docker project `oetwebsite`.
- Blue/green app slots: `oet-web-{blue,green}` + `oet-api-{blue,green}`. Stable router containers `oet-web:3000` and `oet-api:8080` switch only after healthchecks pass.
- Postgres 17 in volume `oetwebsite_oet_postgres_data` — **never recreate without backup**.
- External NPM network `nginx-proxy-manager_default`.
- Deploys are **exact-SHA only** with signed `release-evidence-<sha>`. Destructive migrations need maintenance window + verified backup + non-live restore drill + owner approval.

From `AGENTS.md` → **Runtime Settings (MISSION CRITICAL)**: all secrets routed through `IRuntimeSettingsProvider.GetAsync()` (30s cache TTL, Data Protection purpose `RuntimeSettings.Secret.v1`, `AuditEvent { Action="RuntimeSettingsUpdated" }`).

The chatbot inherits these contracts — it does **not** get to bypass them.

## 1. Container Access Strategy — recommend (b) `oet-devbox` sidecar

### Why not (a) bind-mount `/opt/oetwebapp` into `oet-api-*`

- Expands the privileged surface of the public-facing API container.
- Couples deploy slot lifecycle to a long-lived host fs mount.
- Makes blue↔green divergence possible.
- Violates least privilege: a request-handling container should not be able to `git push` or `rm -rf`.

### Recommended: `oet-devbox` privileged sidecar (option b)

Single long-lived container, decoupled from blue/green slot rotation. The `oet-api-*` slots call it over the internal Docker network via authenticated HTTP (mTLS or shared bearer from `IRuntimeSettingsProvider`).

| Property | Value |
| --- | --- |
| Name | `oet-devbox` |
| Image | Internal image: `mcr.microsoft.com/dotnet/aspnet:10.0` base + git, openssh-client, gh CLI, dotnet SDK, node LTS, ripgrep. No editors, no curl-to-shell installers. |
| User | `assistant` (uid 10001), no sudo, no shell login from outside |
| Mounts | `/opt/oetwebapp` → `/workspace` (rw); `/var/run/docker.sock` **NOT** mounted; no other host paths |
| Networks | `oetwebsite_default` only — **not** attached to `nginx-proxy-manager_default`. No `ports:` published. |
| Restart | `unless-stopped` |
| Healthcheck | `GET /healthz` on internal port 9090 |
| Env (from `IRuntimeSettingsProvider`) | `DEVBOX_RPC_TOKEN`, `GITHUB_DEPLOY_TOKEN` (only when needed by one subprocess) |
| Logging | `json-file`, max 50MB × 5 |
| Read-only rootfs | Yes, with `tmpfs` for `/tmp` (size=256m) |
| Capabilities | Drop ALL, add none |
| `security_opt` | `no-new-privileges:true` |

### Internal RPC contract (api → devbox)

`POST /v1/devbox/exec`, `POST /v1/devbox/write_file`, `POST /v1/devbox/read_file`, `POST /v1/devbox/git/*`. All authenticated by `DEVBOX_RPC_TOKEN` header + source-IP allowlist (docker-internal subnet for `oetwebsite_default` only). Every call gets a request-id propagated to Sentry + AuditEvent on the api side.

### GitHub deploy token handling

- Stored as encrypted secret via `IRuntimeSettingsProvider` (key `AiAssistant:GitHubDeployToken`), purpose `RuntimeSettings.Secret.v1`.
- Devbox **never** writes it to disk, never logs it, never echoes it.
- Wrapper script exports `GITHUB_TOKEN` only for lifetime of one `git push` / `gh pr create` subprocess.
- Audit-log every retrieval (key name only).

## 2. Shell Sandbox Rules (always-on, even with "unrestricted")

Apply inside `oet-devbox` regardless of any admin "unrestricted mode" flag.

| Control | Default | Hard cap |
| --- | --- | --- |
| Process user | `assistant` (uid 10001) | Cannot be overridden |
| Wall-clock timeout | 60s | 600s |
| Stdout/stderr captured | 10MB total | Truncated with marker |
| Working directory | `/workspace` or `/tmp/<request-id>` | No `cd` outside |
| CPU | 1.0 core | `cpus: 1.0` |
| Memory | 1 GiB | OOMKill on overrun |
| PIDs | 256 | Blocks fork bombs |
| Network egress | GitHub + LLM provider only via egress proxy | Allowlist enforced by sidecar firewall |
| Filesystem | rw on `/workspace` and `/tmp/<request-id>`; ro elsewhere | rootfs read-only |

### Permanent command denylist (admin override **cannot** disable)

Reject the command if any of these patterns match:

- `docker volume rm`, `docker compose down -v`, `docker compose rm -v`, `docker system prune`
- `rm -rf /`, `rm -rf /*`, `rm -rf /opt`, `rm -rf /opt/oetwebapp`, `rm -rf .git`, `rm -rf /workspace/.git`
- `dd if=`, `mkfs`, `mkswap`, `wipefs`
- `chmod 777 /`, `chown root /`, `chown -R root`
- `truncate -s 0 /`, `> /dev/sda`, redirect into `/dev/sd*` or `/dev/nvme*`
- `psql ...` containing `DROP DATABASE|DROP SCHEMA|TRUNCATE` (case-insensitive, parsed against `-c`/`-f`)
- `git push --force` on `main`/`production` refs without explicit approval token
- `curl ... | sh`, `wget ... | sh`, `bash <(curl ...)`
- Anything writing to `/etc/`, `/var/lib/docker/`, `/var/run/`, `/proc/`, `/sys/`

Denial path: structured error, `AuditEvent { Action="DevboxDenylistHit" }`, Sentry breadcrumb, surface as refusal in admin chat.

## 3. pgvector — one-time DBA action

Volume already populated; cannot re-init via `/docker-entrypoint-initdb.d/`. Use **manual one-time superuser action**.

```bash
ssh root@185.252.233.186
docker exec -i oet-postgres psql -U postgres -d oet_prod \
  -c "CREATE EXTENSION IF NOT EXISTS vector;"
docker exec -i oet-postgres psql -U postgres -d oet_prod \
  -c "SELECT extname, extversion FROM pg_extension WHERE extname='vector';"
```

- Run **before** deploying the SHA that contains the EF migration referencing `vector` columns.
- EF migration must NOT execute `CREATE EXTENSION` (API runtime user is not superuser).
- Repeat on staging Postgres before staging deploy.
- Record in `mission-critical-execution-ledger.md` and `deploy-runbook.md`.

## 4. Migration Safety

- Change: **+8 tables**, no column drops, no type narrowing, no FK retargeting. **Non-destructive.**
- Runtime estimate: <1s on empty tables.
- No maintenance window required.
- Pre-deploy verified backup still mandatory.

### Rollback SQL (kept in repo, executed only on owner approval)

```sql
BEGIN;
DROP TABLE IF EXISTS ai_assistant_audit_events CASCADE;
DROP TABLE IF EXISTS ai_assistant_tool_invocations CASCADE;
DROP TABLE IF EXISTS ai_assistant_messages CASCADE;
DROP TABLE IF EXISTS ai_assistant_conversations CASCADE;
DROP TABLE IF EXISTS ai_assistant_approvals CASCADE;
DROP TABLE IF EXISTS ai_assistant_code_chunks CASCADE;
DROP TABLE IF EXISTS ai_assistant_indexed_files CASCADE;
DROP TABLE IF EXISTS ai_assistant_settings CASCADE;
COMMIT;
```

Extension `vector` intentionally **not** dropped on rollback (harmless, may be reused).

## 5. Backup Compatibility

- Current prod backup tool: `pg_dump` (custom format) of entire `oet_prod` DB. New tables + `vector` columns auto-included.
- Vector type dumped as binary in `pg_dump -Fc`. Restore requires `vector` extension to exist in target DB **before** restore.
- Document in restore runbook:
  1. `createdb` target.
  2. `psql -c "CREATE EXTENSION vector;"` as superuser.
  3. `pg_restore -d target dump.dump`.
- Validate on staging by full prod backup → restore to staging Postgres → run app → confirm conversations + embeddings query correctly.

## 6. Deploy Sequence — Phase 1 Ship

1. **Pre-flight** — verified prod backup; staging deploy of same SHA passed full E2E; `release-evidence-<sha>` signed.
2. **DBA one-time** (section 3): `CREATE EXTENSION vector;` on prod.
3. **Build images** for target SHA (api + web + new `oet-devbox`). Tag with immutable digests, attach to release evidence.
4. **Compose diff** to `docker-compose.production.yml`:
   - Add `oet-devbox` service per section 1 (image, user, mounts, network, no ports, healthcheck, security_opt, read_only).
   - Bind-mount `/opt/oetwebapp` only (no named volume).
   - Attach `oet-devbox` to `oetwebsite_default` only; **never** `nginx-proxy-manager_default`.
   - Add `AiAssistant__DevboxBaseUrl=http://oet-devbox:9090` to `oet-api-blue` and `oet-api-green`.
   - Add shared bearer env var (from runtime settings at startup).
5. **Seed RuntimeSettings rows (ship disabled):**
   - `AiAssistant:GlobalEnabled = false`
   - `AiAssistant:RequireApprovalAlways = true`
   - `AiAssistant:DefaultProviderId = null`
   - `AiAssistant:DevboxRpcToken = <generated>` (encrypted)
   - `AiAssistant:GitHubDeployToken = null`
6. **Deploy** the SHA: `DEPLOY_REF=<sha> bash ./scripts/deploy/deploy-prod.sh`. Blue/green flip after healthchecks. `oet-devbox` brought up as part of compose convergence.
7. **Post-deploy verification**
   - `docker ps` shows `oet-devbox` healthy, no published ports.
   - `docker network inspect nginx-proxy-manager_default` does **not** contain `oet-devbox`.
   - From `oet-api-<active>`: `curl http://oet-devbox:9090/healthz` returns 200.
   - From host: `curl http://<vps-ip>:9090` fails (no published port).
8. **Admin enablement**: `system_admin` opens `/admin/ai-assistant`, configures LLM provider (paste API key — encrypted via runtime settings), flips `GlobalEnabled = true`, runs built-in smoke test.

## 7. Observability

Add to existing Sentry / metrics pipeline:

| Metric | Type | Labels |
| --- | --- | --- |
| `ai_assistant.hub.connections` | gauge | `slot` |
| `ai_assistant.llm.latency_ms` | histogram | `provider`, `model`, `outcome` |
| `ai_assistant.llm.tokens` | counter | `provider`, `model`, `direction` |
| `ai_assistant.tool.invocations` | counter | `tool`, `outcome` |
| `ai_assistant.tool.duration_ms` | histogram | `tool`, `outcome` |
| `ai_assistant.cost.usd_daily` | gauge | `admin_user_id`, `provider` |
| `ai_assistant.approvals.pending` | gauge | — |
| `ai_assistant.devbox.denylist_hits` | counter | `pattern` |

Sentry tags: `feature=ai_assistant`, `conversation_id`, `tool`, `provider`, `model`. **Never** tag with raw prompts or secrets.

## 8. Kill Switch — instant propagation

30s cache TTL is too slow for emergency. Two-layer kill:

1. **Setting flip**: `AiAssistant:GlobalEnabled = false` → audited via `AuditEvent { Action="RuntimeSettingsUpdated" }`. Takes effect within 30s for NEW requests.
2. **Hub `KILL` broadcast**: same admin action triggers SignalR broadcast on dedicated `aiAssistantControl` channel. Every connected client receives `{ type: "KILL", reason }`. Server-side hub:
   - Cancels in-flight LLM streams (cooperative `CancellationToken`).
   - Refuses new `SendMessage` invocations with `HubException("AiAssistant disabled")`.
   - Disconnects clients after 2s grace.
3. **Devbox lockout**: api refuses any `oet-devbox` RPC while `GlobalEnabled = false`, regardless of cache TTL — checked against in-process bypass flag set synchronously when `KILL` issued.

The 30s cache stays intact for all OTHER consumers; only the hub gets local bypass for the kill case.

## 9. Concurrency / Multi-Admin Writes

Optimistic locking in `write_file` tool contract:

- Request: `{ path, expected_sha256, new_content }`.
- Devbox computes SHA-256 of current `/workspace/<path>` (or "absent" sentinel).
- Mismatch → reject with `{ code: "FILE_CHANGED", current_sha256 }`. UI surfaces "file changed on disk".
- Match → write atomically (`write to tmp + rename`), return new sha.
- All writes recorded in `ai_assistant_tool_invocations` with old + new sha.
- Bonus: advisory PG lock keyed by `hashtext(path)` for write duration.

## 10. Blue/Green Collision

Deploy script wraps blue/green flip with chatbot freeze:

1. **Pre-flip**: set `AiAssistant:GlobalEnabled = false` via signed system action, fire `KILL` broadcast. `AuditEvent { Action="DeployFreezeChatbot" }`.
2. **Standard blue/green deploy** proceeds.
3. **Post-flip healthchecks + smoke test green**: deploy script restores `AiAssistant:GlobalEnabled` to previous value (captured at step 1). `AuditEvent { Action="DeployUnfreezeChatbot" }`.
4. **On deploy failure / rollback**: leave `GlobalEnabled = false`, require explicit admin re-enable.

Devbox itself is **not** restarted during blue/green flip (outside slot rotation), so no in-flight git/build operations torn down — freeze prevents new ones from racing the slot switch.

## 11. CI/CD Gating

New required checks in `.github/workflows/`:

- **Frontend**: lint + typecheck + Vitest scoped to `app/admin/ai-assistant/**` and `components/domain/ai-assistant/**`.
- **Backend**: `dotnet build` + `dotnet test` scoped to `backend/src/OetLearner.Api/Services/AiAssistant/**` and `backend/src/OetLearner.Api/Endpoints/AiAssistant*.cs`.
- **Devbox image**: build + `trivy` scan, fail on HIGH/CRITICAL.
- **Playwright smoke** (new project):
  - Admin: widget mount visible on `/admin/*`, "Open assistant" focusable.
  - Learner: widget DOM absent on `/dashboard`, `/listening/*`, `/reading/*`, `/writing/*`, `/speaking/*`.
  - Expert: widget DOM absent on `/expert/*`.
  - Unauth: widget DOM absent on `/sign-in`.
- **Release evidence**: workflow publishing a SHA must include image digests for `oet-api`, `oet-web`, **and** `oet-devbox`. Block prod deploy if any missing.

## 12. Rollback Plan

Tiered, fastest first:

1. **Soft kill (seconds)**: admin flips `AiAssistant:GlobalEnabled = false` + `KILL` broadcast. Feature off; data intact; no redeploy.
2. **Provider rollback (minutes)**: switch `AiAssistant:DefaultProviderId` to known-good provider.
3. **Slot rollback (minutes)**: standard blue/green re-flip to previous-good slot. New tables remain unused.
4. **Full removal (owner-approved, scheduled)**:
   - Deploy previous-good SHA (no AI assistant code).
   - Run rollback SQL (section 4) under maintenance window with verified backup.
   - Remove `oet-devbox` from compose, `docker compose up -d --remove-orphans`.
   - Extension `vector` left installed.
   - All embeddings lost; reindex on any future re-deploy.

## 13. Monitoring Alerts

| Alert | Trigger | Severity |
| --- | --- | --- |
| Cost-per-day breach | `sum(ai_assistant.cost.usd_daily) > $BUDGET` | P1 |
| Tool error rate | `rate(tool.invocations{outcome=~"error\|timeout"}[5m]) / rate(tool.invocations[5m]) > 0.10` | P2 |
| Approval bypass anomaly | `increase(approvals.bypassed[24h]) > 0` while `RequireApprovalAlways = true` | P1 |
| Audit-log write failure | exception in `IAuditEventWriter` for `feature=ai_assistant` | P1 |
| Devbox denylist surge | `increase(devbox.denylist_hits[1h]) > 5` | P2 |
| Devbox unhealthy | container healthcheck failing > 2m | P1 |
| Hub disconnect storm | `connections` drop > 50% in 1m without deploy event | P2 |
| LLM latency regression | p95 `llm.latency_ms` > 10s for 10m | P3 |

P1 pages owner; P2/P3 to dashboard + email.

## 14. Open Questions for Owner

1. **GitHub deploy token scope**: repo-write only on `oetwebapp` repo, or org-admin? Recommend repo-scoped fine-grained PAT: `contents: write`, `pull_requests: write`, no admin, expires 90d.
2. **Embedding provider**: OpenAI `text-embedding-3-small` (~$1 to index current repo) vs local Ollama. Recommend OpenAI for Phase 1.
3. **Monthly LLM budget cap** (USD): suggest $50/mo soft / $100/mo hard for Phase 1.
4. **Approval UX scope** for unrestricted mode.
5. **Devbox base image hardening**: accept minimal tool allowlist or extend.
6. **Retention**: messages 90d rolling, audit events forever (compliance) — default proposal.

## References

- `AGENTS.md` → Deployment, Security Considerations, Common Gotchas (Runtime Settings, Docker volumes)
- `.github/copilot-instructions.md` → Prompt Defense Baseline + Project Routing
- Repo memory: `deploy-runbook.md`, `mission-critical-execution-ledger.md`, `production-compose-plugin.md`, `compose-env-interpolation.md`, `postgres-credentials.md`
