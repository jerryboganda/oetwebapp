# Owner Agent Console — runbook

> **Status:** authoritative operations runbook for the Owner Agent Console
> (`/admin/agent-console`). Wire shapes, routes, headers and event types live
> in [`agent-console/CONTRACT.md`](../../agent-console/CONTRACT.md) (the
> contract wins on any conflict). Policy:
> [`docs/AI-USAGE-POLICY.md` §20](../AI-USAGE-POLICY.md). Repo-rule
> exception: `AGENTS.md` → "Owner Agent Console exception (owner directive
> 2026-09-27)". Developer overview: [`agent-console/README.md`](../../agent-console/README.md).

Never paste a token, password, cookie, PAT, credential file, `.env.production`
line or TOTP code into this document, a PR, an issue, a chat or a ticket.
Every value below written as `<...>` is a placeholder.

## Contents

1. [Purpose and scope](#1-purpose-and-scope)
2. [Architecture](#2-architecture)
3. [Phase 0 — owner checklist](#3-phase-0--owner-checklist-one-time)
4. [First-time enablement on the VPS](#4-first-time-enablement-on-the-vps)
5. [Connect and re-auth flows](#5-connect-and-re-auth-flows)
6. [Modes, approvals, taint and the lease](#6-modes-approvals-taint-and-the-lease)
7. [Snapshots and restore](#7-snapshots-and-restore)
8. [Kill switch and SSH break-glass](#8-kill-switch-and-ssh-break-glass)
9. [Updates and "Apply update"](#9-updates-and-apply-update)
10. [Token and credential rotation](#10-token-and-credential-rotation)
11. [Transcript retention and GDPR erasure](#11-transcript-retention-and-gdpr-erasure)
12. [Vendor terms](#12-vendor-terms)
13. [Residual risks (accepted)](#13-residual-risks-accepted)
14. [Troubleshooting](#14-troubleshooting)
15. [Appendix — inventory and read-only commands](#15-appendix--inventory-and-read-only-commands)

---

## 1. Purpose and scope

The console lets the **owner only** run **Claude Code** (Claude Max
subscription) and **OpenAI Codex** (ChatGPT Business subscription) on the
production VPS from inside `/admin`, with structured streaming sessions:
pick engine → model → reasoning effort (all reported by the engine at
runtime), interrupt, resume, hand off between engines, approve risky tool
calls, review the diff, and ship through a PR.

Sessions have full project authority, enforced by infrastructure rather than
by prompts:

| Authority | Mechanism |
|---|---|
| Codebase read/write | Per-session git worktree on an `agent/<yyyymmdd>-<slug>` branch |
| Production DB DDL + DML | Postgres role `oet_owner_agent` (`NOSUPERUSER`, inherits the app role's object privileges, cannot `SET ROLE` to it) via `oet-agent-dbproxy` |
| Docker on the VPS | Only through `oet-agent-dockerproxy` (policy table; `oet-*` / `oetwebsite*` free or gated, co-tenants gated, proxies denied) |
| Logs / health | `docker logs` on `oet-*` via the proxy; public health endpoints |
| Deploy root | `/opt/oetwebapp` mounted **read-only**; `.env*` changes only through the Guard-approved `oet-env-edit` helper |
| GitHub | Agent PAT (branches, PRs, workflow dispatch); merges and visibility flips only through the Ship executor (Ship PAT) |

Out of scope: any use by other admins, tutors or learners; any product AI
feature; web terminals; builds or tests on the VPS (those stay on GitHub
Actions — sessions dispatch `qa-smoke.yml`).

## 2. Architecture

```
Browser /admin/agent-console ──REST (x-csrf-token) + SignalR long-poll──▶ Next /api/backend proxy
  ──▶ .NET API blue|green   [OwnerAgent policy: owner auth_account_id (env) + system_admin + unlock ticket]
        │  net oet_agent_ctl (internal:true)  X-Oet-Internal-Token + X-Oet-Owner-Account
        ▼
  oet-agent-console  (compose project `oet-agent-console`; no published ports)
    control (uid 0, caps dropped) : Fastify control server :8410, session store, Guard, Ship executor, Ship PAT, token
    uid agent (10002)             : Claude Agent SDK → bundled `claude` CLI ;  `codex app-server` ; git/gh/psql/docker CLIs
        │  net oet_agent_net (internal:true)
        ├─▶ oet-agent-egress      allowlist CONNECT proxy (only container with internet)
        ├─▶ oet-agent-dockerproxy sole holder of /var/run/docker.sock; policy by container/volume/verb
        └─▶ oet-agent-dbproxy     TCP forward :5432 → oet-postgres (also on oetwebsite_internal)
  Volumes: oet_agent_home (engine creds, uid agent) · oet_agent_workspace (repo + worktrees) · oet_agent_sessions (JSONL/SQLite, control only)
  Mount:   /opt/oetwebapp:/opt/oetwebapp:ro
```

Key properties:

- **Separate compose project** (`docker-compose.agent-console.yml`, project
  `oet-agent-console`). Main deploys (`deploy.yml` → `auto-deploy-ghcr.sh`)
  never recreate it; they only ensure the `oet_agent_ctl` network exists.
  Only the API slots `oet-api-blue` / `oet-api-green` join `oet_agent_ctl`.
- **No published ports, internal-only networks.** The sidecar has no
  internet route of its own; all outbound HTTP(S) goes through
  `oet-agent-egress` (allowlist; anything else becomes an approval card).
- **uid split.** The control plane holds the internal token
  (`/run/secrets/owner_agent_internal_token`), the Ship PAT and the session
  store; the agent uid (which runs both engines and every tool subprocess)
  can read none of them.
- **Resource caps.** Sidecar `mem_limit`/`memswap_limit` 3 GiB, 1.5 CPUs,
  512 pids; at most 2 live Claude queries + 1 Codex app-server.
- **Enforcement boundary** = uid split + Postgres privileges + GitHub
  ruleset + docker policy proxy + egress proxy. The **Guard** (command
  classifier) is a seatbelt on top, not the boundary.
- **Evidence** = API-side hash-chained `AuditEvent` rows
  (`ResourceType = "OwnerAgent"`), JSONL transcripts (90 days), Postgres
  `log_statement = 'mod'` on the agent role (best-effort).

## 3. Phase 0 — owner checklist (one-time)

Complete every item before first enablement. Vendor UI paths are as of
2026-09 and may move; the setting names are what matter.

### 3.1 Owner account

- [ ] **TOTP enabled** on the owner's OET account (the one whose
      `auth_account_id` will be allow-listed). The console unlock requires
      password + a fresh authenticator code; recovery codes **cannot** unlock
      the console.
- [ ] Recovery codes stored offline.
- [ ] Note: once the console ships, re-enrolling the authenticator requires
      the current code (or a recovery code) + password, revokes unlock
      tickets and **blocks console unlock for 72 h**. Do not re-enrol casually.

### 3.2 Model training off on both subscriptions

- [ ] **Claude Max:** claude.ai → Settings → Privacy → model-improvement
      ("Help improve Claude") **off**.
- [ ] **ChatGPT Business:** confirm in workspace settings that workspace data
      is not used for training (Business workspaces are excluded by default),
      and switch off model improvement in personal Data controls if shown.
- [ ] Re-check both after any plan, workspace or vendor-policy change.

### 3.3 ChatGPT Business workspace toggles

- [ ] Workspace admin settings → enable **Codex** for local use (CLI / IDE).
- [ ] Workspace permissions / security → enable **device code login** for
      Codex (without it, the console's Connect ChatGPT flow fails).
- [ ] Copy the **workspace id** (a UUID). It pins Codex to this workspace
      (`forced_chatgpt_workspace_id`); see §4.2.

### 3.4 GitHub — two fine-grained PATs

Create both at GitHub → Settings → Developer settings → Fine-grained tokens.
Resource owner `jerryboganda`; **Repository access: Only select repositories
→ `jerryboganda/oetwebapp`**; expiry **90 days** (calendar reminder at 80).
Metadata: Read is added automatically. Grant nothing else.

| Token | Repository permissions | Where it lives |
|---|---|---|
| **Agent PAT** (`oet-agent-console-agent`) | Contents **RW**, Pull requests **RW**, Actions **RW**, Workflows **RW**. **No Administration**, no Variables, no Secrets, no Issues. | uid agent (git credential helper + `gh auth`) |
| **Ship PAT** (`oet-agent-console-ship`) | Contents **RW**, Pull requests **RW**, Actions **RW**, Workflows **RW**, **Administration RW** (visibility flips), **Variables RW** (the `PUBLIC_WINDOW_HOLDERS` visibility lease). | control plane only (`/var/lib/oet-agent/ship-token`, 0400 root) |

Both are entered **only** in the console (Settings → GitHub tokens, TOTP
step-up). They are write-only: the console never shows them again.

### 3.5 GitHub — `main` ruleset

Settings → Rules → Rulesets → New branch ruleset:

- [ ] Name `main-protection`, enforcement **Active**, target: default branch.
- [ ] Rules: **Require a pull request before merging** (0 required
      approvals — sole maintainer), **Block force pushes**, **Restrict
      deletions**.
- [ ] Bypass list: **Repository admin**, mode **Always** — keeps the owner's
      PC Ship-It flow (`git push origin main`) working.
- [ ] Understand the caveat in §13: both PATs act as the owner (an admin),
      so the ruleset alone does not stop a direct push with the agent PAT.
      The Guard and the session manual forbid it; the optional hardening is
      in §13.

### 3.6 GitHub — Environment `production`

Purpose: an `agent/*` branch that edits a workflow must never be able to read
production deploy secrets.

- [ ] Settings → Environments → New environment **`production`** →
      Deployment branches and tags: **Selected branches → `main`** only. No
      required reviewers (that would block auto-deploy).
- [ ] Add the VPS secrets as **environment** secrets: `PROD_SSH_KEY`,
      `VPS_HOST`, `VPS_USER`, `VPS_PORT` (the ones workflows read today).
- [ ] Merge the console branch: it already puts `environment: production` and
      `if: github.ref == 'refs/heads/main'` on the `migrate-production` and
      `deploy` jobs of `deploy.yml` and on the deploy job of
      `agent-console.yml`. Watch one green Build & Deploy.
- [ ] Those secrets are **also** read by `mobile-release.yml`,
      `publish-existing-desktop-to-vps.yml`, `publish-existing-mobile-to-vps.yml`,
      `tauri-desktop-release.yml` (also runs on `v*.*.*-tauri-desktop` tag
      pushes) and `ubag-integration-e2e.yml` (runs on a push to **any**
      branch touching its paths). Each job that reads them needs
      `environment: production` too, and the environment's deployment rules
      must also allow the `v*.*.*-tauri-desktop` tag pattern (or that release
      must run from `main`). Check the list is still complete:
      `grep -ln 'PROD_SSH_KEY\|secrets.VPS_' .github/workflows/*.yml`.
- [ ] **Only after all of those are merged and one of each has run green**,
      delete the repository-level copies. Deleting them first breaks deploys
      and app releases; keeping them leaves them readable from `agent/*`
      branches (§13).

## 4. First-time enablement on the VPS

Prerequisites: Phase 0 done; the sidecar, compose file, workflow and API
changes are merged to `main`; the latest Build & Deploy is green.

### 4.1 Find the owner's `auth_account_id`

`auth_account_id` is `ApplicationUserAccounts."Id"`. On the VPS (read-only
query, same pattern as `scripts/deploy/inspect-admins.sh`):

```bash
cd /opt/oetwebapp
export $(grep -E '^POSTGRES_(USER|DB)=' .env.production | xargs)
docker exec oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c \
  "SELECT \"Id\", \"Role\",
          \"EmailVerifiedAt\" IS NOT NULL AS email_verified,
          \"AuthenticatorEnabledAt\" IS NOT NULL AS totp_enabled
     FROM \"ApplicationUserAccounts\"
    WHERE lower(\"Email\") = lower('<owner-email>') AND \"DeletedAt\" IS NULL;"
```

Expect exactly one row with `Role = admin`, `email_verified = t`,
`totp_enabled = t`. The account also needs the `system_admin` permission.

### 4.2 Add the keys to `.env.production`

Edit `/opt/oetwebapp/.env.production` with your usual editor as root. Never
`cat`/`grep` values to the terminal in a shared or recorded session. Generate
random values on the VPS with `openssl rand -hex 32` (64 hex chars).

| Key | Value | Read by |
|---|---|---|
| `OWNER_AGENT__ENABLED` | `true` | API (`OwnerAgent__Enabled`) |
| `OWNER_AGENT__INTERNALTOKEN` | random, **≥ 32 chars** | API + sidecar (`/run/secrets/owner_agent_internal_token`) |
| `OWNER_AGENT__OWNERACCOUNTIDS` | the `Id` from §4.1 (comma-separated if ever more than one) | API + sidecar (`OWNER_AGENT_OWNER_ACCOUNT_IDS`) |
| `OWNER_AGENT__PROXYTOKEN` | random, ≥ 32 chars | sidecar + egress + dockerproxy (`/run/secrets/owner_agent_proxy_token`) |
| `OWNER_AGENT__DBPASSWORD` | random | DB role (§4.3) + sidecar (`OET_AGENT_DATABASE_URL`) |
| `OWNER_AGENT__CODEXWORKSPACEID` | workspace id from §3.3 | sidecar (Codex `forced_chatgpt_workspace_id`) |

The first three names are fixed (validated by
`scripts/deploy/validate-production-env.sh`: when `OWNER_AGENT__ENABLED=true`,
the token must be ≥ 32 chars and the account list non-empty). For the last
three, the `environment:` / `secrets:` maps in
`docker-compose.agent-console.yml` are authoritative — if they name a key
differently, use their name and fix this table.

### 4.3 Create the database role

`scripts/ops/create-owner-agent-db-role.sql` is idempotent. It creates
`oet_owner_agent` (`LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE`), grants it the
app role (`POSTGRES_USER`) `WITH INHERIT TRUE, SET FALSE`, and sets
`log_statement = 'mod'`. `POSTGRES_USER` is the image's bootstrap superuser;
membership passes on object privileges (including ownership of app tables),
never the superuser attribute, and `SET FALSE` blocks `SET ROLE`.

The VPS has no source tree, so stream the script from a repo checkout (same
`-f -` pattern as the other `scripts/ops/*.sql` files). The script header is
authoritative for its psql variable names:

```bash
ssh <vps-user>@<vps-host> 'set -eu; cd /opt/oetwebapp
  env_get() { sed -n "s/^$1=//p" .env.production | tail -n 1; }
  POSTGRES_USER=$(env_get POSTGRES_USER); POSTGRES_DB=$(env_get POSTGRES_DB)
  PGPASSWORD=$(env_get POSTGRES_PASSWORD)
  OWNER_AGENT_DBPASSWORD=$(env_get OWNER_AGENT__DBPASSWORD)
  export PGPASSWORD OWNER_AGENT_DBPASSWORD
  docker exec -i -e PGPASSWORD -e OWNER_AGENT_DBPASSWORD oet-postgres \
    psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 \
         -v app_role="$POSTGRES_USER" -f -' \
  < scripts/ops/create-owner-agent-db-role.sql
```

Values are read with `sed` (file argument, output captured by the shell) and
passed with `docker exec -e NAME` (no `=value`, so docker copies the variable
from the calling shell): the passwords never appear in any process argument
list. Do not "simplify" this to the `export $(grep … | xargs)` idiom used for
non-secret keys — `xargs` hands every value to `echo` as argv. `env_get`
assumes unquoted `KEY=value` lines, as in the rest of `.env.production`.

Verify (no secrets printed; `POSTGRES_USER`/`POSTGRES_DB` exported as in §4.1):

```bash
docker exec oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c \
  "SELECT rolname, rolsuper, rolcreaterole, rolcreatedb, rolcanlogin, rolconfig
     FROM pg_roles WHERE rolname = 'oet_owner_agent';"
```

Expect `rolsuper = f`, `rolcreaterole = f`, `rolcreatedb = f`,
`rolcanlogin = t`, and `rolconfig` = `{log_statement=mod,idle_in_transaction_session_timeout=10min,lock_timeout=15s}`.

### 4.4 Deploy the sidecar

From a machine with `gh` authenticated (this is a workflow dispatch — the
build and tests run on GitHub Actions, the VPS only pulls):

```bash
gh workflow run agent-console.yml --ref main -f apply=true
# wait a few seconds so the new run is listed, then watch it (not the previous run)
gh run watch "$(gh run list -w agent-console.yml -e workflow_dispatch -L 1 --json databaseId -q '.[0].databaseId')"
```

The workflow runs the unit tests, builds the three images
(`ghcr.io/jerryboganda/oetwebapp-agent-console{,-egress,-dockerproxy}:<sha>`),
then over SSH: creates `oet_agent_ctl` / `oet_agent_net` (`--internal`) and
the external volumes if absent, writes `/opt/oetwebapp/.deploy/agent-console.env`
(image refs + `DOCKER_GID`), refreshes `protect-production-data.sh`, pulls,
and recreates the four containers.

### 4.5 Recreate the API slots

The API reads `OwnerAgent__*` and joins `oet_agent_ctl` only when its slot
container is (re)created. Trigger a normal Build & Deploy and watch it:

```bash
gh workflow run deploy.yml --ref main
# wait a few seconds so the new run is listed
gh run watch "$(gh run list -w deploy.yml -e workflow_dispatch -L 1 --json databaseId -q '.[0].databaseId')"
```

### 4.6 Turn on the feature flag

`/admin/flags` → create (if absent) and enable **`owner_agent_console`**. The
flag is uncached and fail-closed: missing or off ⇒ every `/v1/owner-agent/*`
endpoint returns 503. Turning it on grants nothing to non-owners.

### 4.7 Verify

```bash
docker ps --filter name=oet-agent- --format 'table {{.Names}}\t{{.Status}}\t{{.Ports}}'
docker exec oet-agent-console curl -fsS http://127.0.0.1:8410/healthz
docker network inspect oet_agent_ctl --format '{{range .Containers}}{{.Name}} {{end}}'
```

- Four `oet-agent-*` containers, all `healthy`, **empty Ports column**.
- `/healthz` → `{"ok":true,...,"activeTurns":0,"draining":false}`.
- `oet_agent_ctl` members: `oet-agent-console` and the live API slot(s) only.
- As a non-owner admin, `GET /v1/owner-agent/status` → 403; `/me` →
  `{"isOwner":false}`.
- As the owner: open `/admin/agent-console` → unlock (password + TOTP) →
  status strip loads. Then §5 (connect engines) and Settings → GitHub tokens.
- First session: **Read-only**, e.g. "report the git SHA of `main` and
  `docker ps` for `oet-*`". Then follow the E2E list in the approved plan
  (Guarded scratch-table delete ⇒ approval card + `agent-snap-` file;
  Autopilot after reading a learner row ⇒ taint badge; Ship end to end).

## 5. Connect and re-auth flows

Both flows need the console unlocked plus a TOTP step-up. The sidecar runs
the vendor's own sign-in **inside the container as uid agent**; the browser
only ever sees a vendor URL and a code. The console never reads, copies or
exports the engines' credential files.

### 5.1 Claude Max (paste-code)

1. Settings → **Connect Claude Max**.
2. The dialog shows an Anthropic sign-in URL (produced by the SDK-bundled
   `claude auth login`, claude.ai method, running in a wide PTY).
3. Open it, sign in with the Max account, authorise, copy the code shown.
4. Paste the code into the dialog → state `completed`; the engine status
   shows `signed_in` with the plan. The binary stores its own credentials
   under its config dir in the `oet_agent_home` volume.
5. On start the sidecar asserts the engine reports first-party subscription
   auth; if it does not (for example an API key leaked into its env), the
   engine refuses to run.

### 5.2 ChatGPT Business (device code)

1. Settings → **Connect ChatGPT**.
2. The dialog shows a verification URL, a **user code** and an expiry
   countdown (about 15 minutes).
3. Open the URL (any device), sign in with the Business account, enter the
   code. Completion is detected automatically (`account/login/completed`).
4. Status shows `signed_in` with plan/workspace. Codex is pinned to ChatGPT
   login and to the workspace id; API-key login is never offered.

### 5.3 Re-auth and logout

- Re-auth when an engine shows `signed_out` / `error`, or turns fail with an
  auth error: repeat §5.1 / §5.2 (it replaces the stored credentials).
- **Logout** (Settings, TOTP step-up) signs the engine out inside the
  container. To also kill the vendor-side session, sign out of all devices
  / revoke sessions in the vendor's account settings.
- Rate limits show `unknown` until the first rate-limit event of a turn;
  that is normal.
- Break-glass sign-in over SSH: §8.3.

## 6. Modes, approvals, taint and the lease

### 6.1 Modes

| | Read-only | Guarded | Autopilot (untainted session) | Autopilot (tainted session) |
|---|---|---|---|---|
| Reads (files, DB `SELECT`, `docker ps/inspect/logs`) | allow | allow | allow | allow |
| Ordinary writes (worktree edits, commits, non-destructive DB/docker) | **deny** | allow | allow | allow, except the tainted-gated classes below |
| Destructive or unparseable command | **deny** | **owner card** | **pre-snapshot, then allow** | **owner card** |
| New egress domain / gated docker op (proxy callback) | **deny** | **owner card** | auto-approve | **owner card** |
| DB writes, docker writes, `git push`, `gh` write calls | deny | allow (destructive ⇒ card) | allow | **owner card** |
| `node scripts/ship/pre-push-gate.mjs` (agent-editable code) | **deny** | allow | allow | **owner card** |

- Destructive = e.g. `DROP`, `TRUNCATE`, `ALTER … DROP`, `DELETE`/`UPDATE`
  without a real `WHERE`, `DO $$`, `COPY … PROGRAM`; `docker volume rm|prune`,
  `system prune`, `compose … down -v`, privileged/host-root runs; `rm -rf`
  on volume paths; force/mirror pushes; `gh repo delete|edit --visibility`,
  `gh api -X PATCH|PUT|DELETE`; `sed -i` on `.env*`; writes to `.claude/**`,
  `.codex/**`, `.mcp.json`, `.github/workflows/**`, `docker-compose*.yml`,
  `*.template`.
- **Unparseable is treated as destructive**: `psql -f`, heredocs,
  `docker exec … psql`, `sh|bash -c`, `node|python -e|-c`, `| sh`, base64
  pipes, `env X=… cmd`, absolute binary paths.
- Switching to **Autopilot** needs a TOTP step-up. Read-only runs Claude in
  `dontAsk` with read-only tools; Guarded/Autopilot never use
  `bypassPermissions` / `acceptEdits`, and Codex never runs with approval
  policy `never`.
- Schema changes that should persist belong in an EF migration shipped by PR
  (applied by `migrate-production`), not ad-hoc DDL; ad-hoc DDL is for
  emergencies and data repair.

### 6.2 Approval cards

Each card shows the full command (control and bidi characters made visible,
base64 decoded), cwd, uid, target container/host, the classifier reasons,
whether the turn is tainted, and an expiry. Decisions: **Approve**, **Deny**,
**Approve for session**. "For session" maps to the engine's session-scoped
grant only — never persisted, dropped when the turn becomes tainted. Each
card carries a single-use nonce; an expired card resolves as a deny
(`by: "timeout"`). Proxy-originated cards with no session attribution appear
on the console home's **system** queue.

### 6.3 Taint

A session becomes **tainted** as soon as one of its turns reads
learner-authored DB content, `docker logs` output, web-fetch results or
GitHub issue/PR comments — the channels an attacker can write into. The taint
is persisted and lasts **for the rest of the session** (a hand-off inherits
it); start a new session to get a clean one. While tainted, new egress
domains, docker writes, DB writes, `git push`, `gh` write calls and the
pre-push gate script need an owner click **even in Autopilot** (the card
names the category, e.g. `taint-sensitive: worktree_code`), and
allow-for-session grants are dropped. The session shows a taint badge and a
`taint` event with the source.

### 6.4 The lease (dead-man switch)

While the console page is open the browser renews a lease every 60 s
(server clamps expiry to now + 3 min, and never past the unlock expiry).
If it lapses (tab closed, laptop asleep, network loss, unlock expired):
Autopilot drops to Guarded, no new turns start, and running turns pause at
the next tool boundary. Re-open and unlock to resume.

## 7. Snapshots and restore

### 7.1 What is taken

Before a destructive DB operation is allowed (owner-approved in Guarded,
automatic in untainted Autopilot) the control plane runs:

```bash
docker exec oet-db-backup /usr/local/bin/postgres-backup.sh --snapshot <label> [--table <name> ...]
```

- Files: `/backups/agent-snap-*.dump` (`.dump.gpg` when
  `BACKUP_GPG_PASSPHRASE` is set) in volume `oetwebsite_oet_db_backups`,
  pushed to `BACKUP_S3_URL` like nightly backups when configured.
- Own **30-day** retention for the `agent-snap-` prefix (nightly `oet-*`
  retention unchanged).
- Guards: free disk ≥ 2× the last dump; full-DB snapshots at most one per
  10 min; table-scoped preferred.
- The session stream records a `snapshot` event (`label`, `file`, `ok`).
  If the snapshot fails, the operation must not run — treat a destructive op
  that ran after `ok:false` as a defect.

### 7.2 Find a snapshot

```bash
docker exec oet-db-backup sh -c 'ls -lt /backups/agent-snap-* | head -20'
```

Match the file to the session's `snapshot` event (console → session → tool
card, or the audit tail).

### 7.3 Validate by restoring into a scratch database (safe)

Uses the existing `scripts/backup/postgres-restore.sh` (baked into the
backup image; it refuses to touch the live DB by default and decrypts with
the container's own passphrase):

```bash
docker exec -e CONFIRM_RESTORE=YES \
  -e BACKUP_FILE=/backups/agent-snap-<stamp>-<label>.dump.gpg \
  -e TARGET_DB=oet_agent_snap_check \
  oet-db-backup /usr/local/bin/postgres-restore.sh
```

Inspect the scratch DB, then drop it (the script prints the `dropdb` line).

### 7.4 Restore one table into live (owner decision)

For a table-scoped snapshot, replay its data in one transaction. Stop first
if anything is still writing to the table (pause the relevant feature or
worker). Run inside the backup container (it already has `PGPASSWORD`,
`POSTGRES_HOST`, `POSTGRES_USER`, `POSTGRES_DB` and `BACKUP_GPG_PASSPHRASE`
in its env). For an unencrypted `.dump`, drop the `gpg` line and point
`pg_restore` at the file directly:

```bash
docker exec -it oet-db-backup sh -c '
  set -eu
  trap "rm -f /tmp/snap.dump" EXIT
  f=/backups/agent-snap-<stamp>-<label>.dump.gpg
  printf %s "$BACKUP_GPG_PASSPHRASE" | gpg --batch --yes --pinentry-mode loopback \
    --passphrase-fd 0 --output /tmp/snap.dump --decrypt "$f"
  pg_restore --list /tmp/snap.dump | grep -i "TABLE DATA"       # confirm contents
  { echo "BEGIN;"; echo "TRUNCATE TABLE public.\"<Table>\";";
    pg_restore --data-only --table="<Table>" -f - /tmp/snap.dump;
    echo "COMMIT;"; } \
  | psql -h "$POSTGRES_HOST" -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1'
```

If `TRUNCATE` is refused because other tables reference this one, **stop**:
restore into a scratch DB (§7.3) and copy back only the affected rows with
reviewed SQL. Never add `CASCADE` without first listing what it would empty.
A full-database restore into live follows `DEPLOYMENT.md` → Disaster Recovery
(`RESTORE_INTO_LIVE=YES`) — never improvise it.

## 8. Kill switch and SSH break-glass

Use the lightest layer that works; each is independent.

| # | Layer | Effect |
|---|---|---|
| 1 | Console **Stop all** (`POST /v1/owner-agent/kill-switch`) | Aborts all turns, `pkill -9 -u agent`, stops containers labelled `oet.agent.session`. Sidecar stays up and refuses new turns (423) until **Resume** in the status strip (`POST /v1/owner-agent/resume`) or a restart. Audited. |
| 2 | `/admin/flags` → `owner_agent_console` **off** | Every console endpoint returns 503 immediately (uncached). |
| 3 | `.env.production` `OWNER_AGENT__ENABLED=false` + next API recreate | Console removed at config level. |
| 4 | **SSH hard stop** (below) | Nothing runs; survives reboots (`unless-stopped` respects a manual stop). |
| 5 | Revoke credentials (§10) | PATs revoked on GitHub; engines signed out vendor-side; `ALTER ROLE oet_owner_agent NOLOGIN` + terminate its backends. |

### 8.1 Hard stop

```bash
docker stop oet-agent-console
# full isolation, if needed:
docker stop oet-agent-egress oet-agent-dockerproxy oet-agent-dbproxy
```

Start again with `docker start oet-agent-dbproxy oet-agent-dockerproxy oet-agent-egress oet-agent-console`
(or dispatch `agent-console.yml`). In-flight turns come back `interrupted`
and are resumable.

### 8.2 Cut database access only

With `POSTGRES_USER`/`POSTGRES_DB` exported as in §4.1:

```bash
docker exec oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c \
  "ALTER ROLE oet_owner_agent NOLOGIN;
   SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE usename = 'oet_owner_agent';"
```

Re-enable with `ALTER ROLE oet_owner_agent LOGIN;`.

### 8.3 Break-glass engine sign-in over SSH

Signing in is ops, not compute (AGENTS.md exception (h)). Use when the
browser flow is broken:

```bash
docker exec -it -u agent oet-agent-console claude auth login
docker exec -it -u agent oet-agent-console claude auth status
docker exec -it -u agent oet-agent-console codex login --device-auth
```

The image sets `CLAUDE_CONFIG_DIR` / `CODEX_HOME`, `docker-compose.agent-console.yml`
sets `HTTPS_PROXY` / `HTTP_PROXY` / `NO_PROXY` at container level, and `-u agent`
gives `HOME=/home/agent` from the image's `agent` user, so these commands land
credentials exactly where the engines look for them. If a command cannot
reach the vendor, or the console still shows `signed_out` afterwards, the
exec'd shell is missing that environment: add
`-e HOME=/home/agent -e HTTPS_PROXY=http://oet-agent-egress:3128` plus the
same `CLAUDE_CONFIG_DIR` / `CODEX_HOME` values the sidecar gives the engines
(`agent-console/src/env.ts`; Codex uses `/home/agent/.codex`). A login
written to a different config dir is invisible to the console. If the image
has no named `agent` user, use `-u 10002:10002`. Afterwards use Settings →
refresh status, or `docker restart oet-agent-console` when `/healthz` shows
`activeTurns: 0`. Never use API-key login, never copy credential files in or
out of the container, never paste tokens into the console, chat or tickets.

## 9. Updates and "Apply update"

- **Normal path.** A merged PR touching `agent-console/**` triggers
  `agent-console.yml`: tests → images → pull on the VPS. The containers are
  recreated **only if `/healthz` shows 0 active turns**; otherwise the new
  images wait and the status strip shows **update pending**.
- **Apply update** (console button, `POST /v1/owner-agent/apply-update`):
  the API calls the sidecar's `POST /v1/admin/apply-update`, which sets
  draining (no new turns, 423 for new work) and immediately dispatches
  `agent-console.yml` on `main` with `apply=true` using the **Ship PAT**
  (Actions RW). The rollout recreates the containers once its tests and image
  builds finish (typically several minutes): a turn still running then is
  interrupted and comes back as resumable. If the dispatch fails (no Ship
  token, GitHub error) the console stays draining and the status strip says
  so; run the workflow by hand, or press **Resume** in the status strip
  (`POST /v1/owner-agent/resume` → sidecar `drain {draining:false}`, which
  also clears a kill-switch stop). Manual equivalent:

  ```bash
  gh workflow run agent-console.yml --ref main -f apply=true
  ```

- Engine versions (`@anthropic-ai/claude-agent-sdk` with its bundled CLI,
  `@openai/codex`) are pinned in `agent-console/package.json` + lockfile;
  auto-updaters are disabled in the image. Bump them by PR. Credentials
  survive recreation (`oet_agent_home` volume); re-auth only if the vendor
  invalidates sessions.
- Main web/API deploys never touch the sidecar; an unrelated deploy
  mid-session only reconnects the stream (SignalR resumes from `afterSeq`).
- **Rollback:** revert the PR and let the workflow run; in an emergency,
  §8.1 first.

## 10. Token and credential rotation

| Secret | Rotate when | Procedure |
|---|---|---|
| Agent PAT / Ship PAT | every 90 days, on suspicion, on expiry warning | Create the new PAT (§3.4) → console Settings → GitHub tokens (TOTP) → verify status → **revoke the old PAT on GitHub**. |
| Internal token (`OWNER_AGENT__INTERNALTOKEN`) | yearly, on suspicion | New value in `.env.production` → `gh workflow run agent-console.yml -f apply=true` → Build & Deploy to recreate the API slots. Expect console 502/401 between the two steps. |
| Proxy token | yearly, on suspicion | New value in `.env.production` → `agent-console.yml` with `apply=true` (all four containers recreate together). |
| DB role password | yearly, on suspicion | New value in `.env.production` → re-run §4.3 (idempotent; resets the password) → `agent-console.yml` with `apply=true`. |
| Claude / ChatGPT sign-in | on suspicion, plan change, owner device loss | Settings → Logout → vendor-side "sign out all sessions" → Connect again (§5). |
| Unlock ticket | on suspicion | Console **Lock**, or sign out all sessions of the owner account (revokes the token family). |

Never print old or new values. Compare configuration by length or hash only,
e.g. `printf %s "$VALUE" | sha256sum`.

## 11. Transcript retention and GDPR erasure

### 11.1 What is stored where

| Data | Location | Retention |
|---|---|---|
| Session events (JSONL, one line per event, redacted) + SQLite index | `oet_agent_sessions` → `/var/lib/oet-agent/sessions/<id>/events.jsonl` | **90 days** after the session's last update, then purged |
| Engine-native transcripts (needed for resume) | `oet_agent_home` → `$CLAUDE_CONFIG_DIR/projects/**` (e.g. `/home/agent/.claude/projects`), `$CODEX_HOME/sessions/**` (`/home/agent/.codex/sessions`) | `*.jsonl` not modified for **90 days** are deleted by the same retention sweep (every 6 h) |
| Audit | Postgres `AuditEvent` (`ResourceType = "OwnerAgent"`), hash-chained | platform audit retention; no secrets, message text ≤ 200 chars |
| Postgres statement log (`log_statement = 'mod'`) | `oet-postgres` container log (50 MB × 5 rotation) | rotation |
| Vendor side | Anthropic / OpenAI under each subscription's terms (training off) | vendor-defined |

Redaction before persistence covers known secrets plus JWT, `postgres://`,
`github_pat_`, `ghs_` and `sk-` patterns. It is best-effort; do not paste
secrets into prompts.

### 11.2 Erasure request for a data subject

1. Identify the subject's identifiers (email, user id, name) from the request.
2. Find affected sessions (lists file names only, no content):

   ```bash
   docker exec oet-agent-console grep -rlF -- '<identifier>' \
     /var/lib/oet-agent/sessions /home/agent/.claude/projects /home/agent/.codex/sessions
   ```

3. Erase each affected session (interrupt it first if a turn is running;
   hand-off chains are separate sessions, erase each one):

   ```bash
   docker exec oet-agent-console oet-console-erase <sessionId>
   ```

   The control plane deletes the session's JSONL, index rows, approvals and
   ship records, its worktree (unless another session shares it) and the
   engine-native transcripts named after its engine session id, and prints
   `{"erased":true,"engineTranscripts":N}`. The token is read from the
   root-only secret file and passed to `curl` on stdin, never on a command
   line. Re-run the step-2 `grep` to confirm nothing is left (a match inside
   an unrelated session means that session must be erased too).
4. Do not edit `AuditEvent` rows (it breaks the hash chain); they are
   minimised by design. Record the decision in the erasure log.
5. Vendor side: data sent as prompt context remains under the vendor's
   retention for the subscription; note this in the erasure response.
6. Snapshots (`agent-snap-*`) age out in 30 days; nightly backups follow
   `BACKUP_RETENTION_DAYS`. Note both in the erasure log.

## 12. Vendor terms

Checked 2026-09-27. Re-read both pages before any change to how the engines
authenticate.

- **Anthropic** — [code.claude.com/docs/en/legal-and-compliance](https://code.claude.com/docs/en/legal-and-compliance).
  Developers "may not collect, store, or intermediate Claude.ai credentials
  or session tokens". The same page allows an end user to sign in to the
  **unmodified** Claude Code binary with their own subscription, including
  where a platform hosts Claude Code, requires sign-in to complete through
  Anthropic's own flow, and states that advertised Pro/Max limits assume
  ordinary, individual use of Claude Code and the Agent SDK. ⇒ the console
  relays only the URL and paste-back code, the SDK-bundled binary owns the
  credential file, and the console is owner-only.
- **Anthropic Agent SDK billing** — [support.claude.com/en/articles/15036540](https://support.claude.com/en/articles/15036540):
  the planned move of Agent SDK usage on subscription plans to a separate
  monthly credit pool was **paused** in June 2026, not cancelled. "$0
  marginal cost" holds only while it stays paused; watch the per-session
  usage meter. Codex is the fallback engine.
- **OpenAI Codex** — [developers.openai.com/codex/auth](https://developers.openai.com/codex/auth)
  (currently redirects to learn.chatgpt.com/docs/auth). Headless machines
  sign in with device-code login, which a workspace admin must enable for
  ChatGPT workspaces; Codex caches credentials in `~/.codex/auth.json` (or
  the OS keyring), which the docs say to treat like a password.
  `forced_login_method` and `forced_chatgpt_workspace_id` pin login to
  ChatGPT and to one workspace. The docs also describe copying cached
  credentials to a headless machine as a fallback; **this project forbids
  it** (one credential store per machine).

## 13. Residual risks (accepted)

- **Engine credentials are readable by uid agent** — anything the agent
  runs could read them. Mitigated by egress allowlist, taint, owner-only
  access and quick revocation (§10).
- **The agent PAT acts as the owner (a repository admin).** With ruleset
  bypass = repository admin in mode *Always*, GitHub itself does not stop
  that token from pushing straight to `main`. Mitigations in place: Guard
  gating of `git push`, tainted-turn push approval, session manual, audit.
  Optional hardening: issue the agent PAT from a separate non-admin
  collaborator account (Write role), or switch the bypass mode to
  *pull requests only* once the owner's PC flow merges through PRs.
- **Workflows on `agent/*` branches run with repository-level secrets.**
  The agent PAT has Workflows RW, so a session can push a branch whose
  workflow prints any secret that is not environment-scoped. Only
  Environment `production` (branch `main`) protects a secret from that;
  keep every deploy/VPS secret there (§3.6) and treat any secret that must
  stay repository-level as readable by a session.
- **The agent can alter app tables, including audit tables**, through
  inherited privileges. The API-side hash chain is the authoritative audit;
  Postgres `log_statement = 'mod'` is best-effort.
- **Autopilot on untainted turns runs destructive operations after a
  snapshot only** — no human in the loop. Use Guarded for sensitive work.
- **Prompt injection** via content the agent reads. Contained, not
  eliminated: egress allowlist, docker policy proxy, taint, uid split,
  redaction, Ship gitleaks scan and file-type blocklist before any push.
- **Learner data sent to vendors** when a session reads it (§11.1).
- **Vendor policy can change** (Agent SDK billing pause, subscription
  usage rules). §12 is re-checked before any auth change.
- **Shared VPS capacity.** Caps are hard (3 GiB / 1.5 CPU), but a heavy
  session still competes with co-tenants; raise memory only on evidence.
- **Public window.** During Ship the repo may be public; the Ship executor
  scans agent refs and PR text with gitleaks and blocks `.sql`, `.dump`,
  `.csv`, `.jsonl`, `.env*` before pushing, and a watchdog restores private
  visibility within 90 min.
- **Root on the VPS** can bypass all of this; SSH access remains the
  highest-privilege credential in the system.

## 14. Troubleshooting

| Symptom | Likely cause | Check / fix |
|---|---|---|
| Every console call returns 503 | Flag `owner_agent_console` missing/off, or `OWNER_AGENT__ENABLED` not true in the running API slot | `/admin/flags`; recreate API slots after env changes (§4.5) |
| Console entry missing from the admin nav | `/me` says `isOwner:false`: wrong/missing id in `OWNER_AGENT__OWNERACCOUNTIDS`, missing `system_admin`, email not verified | §4.1 query; fix env; recreate API slots |
| Unlock rejected | Wrong or reused TOTP (the same 30-s step cannot be replayed), recovery code used, MFA lockout, re-enrolment block (72 h) | Wait for the next code; check SecurityEvents; lockout clears per auth policy |
| 502 / timeout from API to sidecar | Sidecar down, slot not on `oet_agent_ctl`, token mismatch | `docker ps --filter name=oet-agent-`; §15 network check; `docker logs --tail 100 oet-agent-console` (401 ⇒ token mismatch; compare `sha256sum`, never values) |
| Sidecar restart-loops at boot | Internal token missing or < 32 chars (it refuses to start by design), or owner id list empty | Fix `.env.production`; `agent-console.yml -f apply=true` |
| Claude connect stuck at `pending` / fails | Auth host blocked by egress, PTY flow changed upstream | `docker logs --tail 200 oet-agent-egress \| grep -i blocked`; allowlist changes go through a PR; §8.3 break-glass |
| Codex device code rejected / "not enabled" | Workspace toggle off (§3.3) or wrong workspace id | Fix in ChatGPT workspace settings; check `OWNER_AGENT__CODEXWORKSPACEID` |
| Engine refuses to start a turn: "not subscription auth" | API-key env leaked into the engine, or signed out | Check compose env for `ANTHROPIC_*` / `OPENAI_API_KEY`; reconnect |
| Autopilot silently became Guarded | Lease lapsed (tab closed, sleep, unlock expired) | Re-open, unlock; `mode_changed` event gives the reason |
| Approval card appeared in Autopilot | Turn tainted, or a proxy-gated action (co-tenant container, protected volume, exec into `oet-postgres`/`oet-api-*`) | By design; read the reasons on the card |
| `permission denied` for the API on a table the agent created | Objects created by `oet_owner_agent` are owned by it, not the app role | Ship schema changes as EF migrations; for ad-hoc objects grant access to the app role explicitly |
| `git push` to `main` rejected | Ruleset working as intended | Use `agent/*` + Ship |
| Snapshot `ok:false` | Low disk (< 2× last dump), full-dump rate limit (1 per 10 min), backup container down | `df -h`; `docker ps --filter name=oet-db-backup`; prefer table-scoped snapshots |
| Update pending never applies | Active turns never reach 0 | Use **Apply update** (drains first) |
| Sidecar OOM-killed | 3 GiB cap reached | `docker stats --no-stream oet-agent-console`; reduce concurrency; raise to 4 GiB only with recorded peak RSS evidence |
| Disk alert from the sidecar | Old worktrees | Archive finished sessions (worktrees are kept until archived) |
| Rate limits show `unknown` | No rate-limit event yet this turn | Normal |

## 15. Appendix — inventory and read-only commands

**Containers:** `oet-agent-console`, `oet-agent-egress`,
`oet-agent-dockerproxy`, `oet-agent-dbproxy` (compose project
`oet-agent-console`). **Networks:** `oet_agent_ctl` (internal; API slots ↔
sidecar :8410), `oet_agent_net` (internal; sidecar ↔ proxies). **Volumes**
(external): `oet_agent_home`, `oet_agent_workspace`, `oet_agent_sessions`;
never `docker volume rm` them — `protect-production-data.sh` blocks it.
**Images:** `ghcr.io/jerryboganda/oetwebapp-agent-console{,-egress,-dockerproxy}:<sha>`
plus stock `alpine/socat` for the DB proxy.

```bash
# health and placement
docker ps --filter name=oet-agent- --format 'table {{.Names}}\t{{.Status}}\t{{.Ports}}'
docker exec oet-agent-console curl -fsS http://127.0.0.1:8410/healthz
docker network inspect oet_agent_ctl --format '{{range .Containers}}{{.Name}} {{end}}'
docker network inspect oet_agent_net --format '{{.Internal}}'

# logs (no secrets are logged by design; still treat output as sensitive)
docker logs --tail 200 oet-agent-console
docker logs --tail 200 oet-agent-egress
docker logs --tail 200 oet-agent-dockerproxy      # one JSON line per policy decision

# resources
docker stats --no-stream oet-agent-console oet-agent-egress oet-agent-dockerproxy oet-agent-dbproxy

# deployed image refs
cat /opt/oetwebapp/.deploy/agent-console.env
```
