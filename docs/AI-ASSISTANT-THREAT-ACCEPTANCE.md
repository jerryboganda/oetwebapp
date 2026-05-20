# Admin AI Assistant — Threat Acceptance & Owner Signoff

> Required reading + signature before each phase ships. Companion to `docs/AI-ASSISTANT-THREAT-MODEL.md`.

## Purpose

This document records the explicit, dated, signed acknowledgement by the platform owner that the Admin AI Assistant introduces categorically new risks to the OET Prep platform, and that the mitigations described in `AI-ASSISTANT-PLAN.md`, `AI-ASSISTANT-DEVOPS.md`, `AI-ASSISTANT-ROLLOUT-PLAN.md`, and `AI-ASSISTANT-ADMIN-RUNBOOK.md` are accepted as the residual risk floor for the feature.

No phase may ship to production without an in-date signature in §6 corresponding to the phase being shipped.

## 1. Capability statement (what is being shipped)

By phase:

| Phase | Capabilities |
| --- | --- |
| 0 | Schema + permissions only. No user-visible surface. |
| 1 | Streaming chat to LLM provider. **No tool calls.** Kill switch, global budget kill, and per-user admin disable. Per-admin daily quota remains a future owner-signoff gate. |
| 2 | + Grounded retrieval over allowlisted repo paths (pgvector). **No tool calls.** Trust-tier separation enforced; untrusted content not yet indexed. |
| 3 | + Per-call approval flow. Tools: `read_file` (with secret scan), `list_directory`, `search_codebase`, `write_file` (writes only to devbox workspace, never `/opt/oetwebapp/` directly; mission-critical paths hard-denied). |
| 4 | + `run_command` in `oet-devbox` sandbox (egress-denied, executable allowlist, no Docker socket, read-only rootfs, drop-all caps), `git` (status/diff/log/branch/checkout/commit — **no push, no pr_create, no force**), `reindex_codebase`, `restart_service` (always-approval, respects `.deploy.lock`), `deploy_status` (read-only). |
| 5 | + Multi-provider (Anthropic, Azure OpenAI, GitHub Models, OpenAI-compatible). BYOK. Budgets. Audit explorer. Attachments. Mobile UX. Off-host append-only audit sink. Retention workers. |

Out of scope (will not ship in any phase without re-signoff):

- `git push` to any branch from chat.
- `pr_create` from chat.
- Editing mission-critical OET surfaces (`lib/scoring.ts`, `lib/rulebook/**`, `rulebooks/**`, `OetStatementOfResultsCard.tsx`, grading services) from chat.
- Direct host-shell access (no shell on host; everything via devbox RPC).
- Docker socket inside devbox.
- Cross-thread "memory" / vector store of past chats fed back into prompts.
- Read access by chatbot to: `.env*`, `**/appsettings.Production.json`, `**/.git/**`, `**/secrets/**`, files matching secret regex.
- Use by any non-admin role.

## 2. Risk statement

The AI Assistant is, by construction, **a high-privilege automated actor inside the production system**. Even with every mitigation enabled:

1. A successful prompt-injection attack against an admin session can cause the model to attempt actions the admin did not intend. Mitigations reduce blast radius but cannot eliminate this class of attack as long as LLMs are non-deterministic.
2. The agent has read access to repo source and the indexed codebase. A compromise of the chatbot database tables compromises the codebase content (mitigated by per-path denylists + secret-scan, but the chat history table itself remains a sensitive long-lived store).
3. The agent has WRITE access to a sandboxed workspace and (with admin approval) can run shell commands in `oet-devbox`. Compromise of admin credentials gives an attacker a much faster path to operational disruption than without the assistant.
4. Third-party LLM providers see prompt content (subject to per-provider data-retention contracts). Secret/PII egress filter mitigates leakage of obvious patterns; it cannot guarantee zero exposure of contextual sensitive content.
5. Current V1 cost runaway is bounded by global platform budget kill and per-user admin disable; per-admin daily quota and per-provider monthly cap remain future owner-signoff gates.

## 3. Mitigations enabled by default

- All chatbot LLM calls route through `IAiGatewayService.BuildGroundedPrompt + CompleteAsync`. `PromptNotGroundedException` enforced at stream-open.
- Server-side secret/PII egress filter inside `IAiGatewayService` before `CompleteAsync` — refuses prompts containing secret-shaped tokens, emails/phones from `users`, payment strings.
- Two-tier RAG: trusted (allowlisted repo paths) vs untrusted (learner essays, forum posts, recalls — when later indexed). **When any untrusted chunk is in scope, all tool calls are disabled server-side.**
- Path allowlist for `read_file`; post-read secret-scan. Refuses if any hit.
- `write_file` operates on devbox workspace only; mission-critical OET paths hard-denied.
- Per-tool-call nonce approvals (server-side). `UseAiAssistantUnrestricted` cannot skip approvals for `write_file`, `run_command`, `git`, or `restart_service`.
- Devbox sandbox: uid 10001, no Docker socket, no published ports, attached only to `oetwebsite_default`, read-only rootfs, drop-all caps, no-new-privileges, egress-denied except LLM provider + GitHub allowlist, executable allowlist (no docker/psql/ssh/rm/curl/sudo), wall-clock + memory + PID caps, permanent denylist L8.
- Current V1: global platform budget kill and per-user admin disable apply to `admin.ai_chatbot`. Future phases add per-admin daily token + turn quota and per-provider monthly budget caps.
- Kill switch via `IRuntimeSettingsProvider:AiAssistant:GlobalEnabled` + immediate hub `KILL` broadcast (≤2s effective).
- All mutations write to `AuditEvent` + chatbot-specific `AiAuditEvent` with before/after JSON diffs.
- Off-host append-only audit sink (S3 object-lock or syslog) — Phase 5; hash-chained rows for tamper detection.
- Chat messages encrypted at rest via ASP.NET Data Protection (purpose `RuntimeSettings.Secret.v1`); retention 30 days default.
- Embeddings encrypted at rest; separate DB role; excluded from logical dumps' standard path.
- Pre-deploy freeze and post-flip restore around blue/green slot switches.
- Non-admins never download widget bundle (role-gated server-side); chatbot endpoints return 404 (not 403) to non-admins to avoid disclosure.
- Web-only for Phase 1; Electron and Capacitor builds explicitly excluded via build-time flag.

## 4. Residual risks expressly accepted by owner

By signing this document for a given phase, owner acknowledges and accepts these residual risks for that phase:

| ID | Residual risk | Mitigation | Phase |
| --- | --- | --- | --- |
| R1 | Prompt-injection-driven action against admin's intent, within the bounds of approved tools and per-call nonce constraints | Two-tier RAG, mission-critical write denylist, per-call approval, critic agent, off-host audit | 1+ |
| R2 | LLM provider sees prompt content within their retention contract | Secret/PII egress filter, contractual data-retention review per provider | 1+ |
| R3 | Cost overrun within configured per-admin daily and per-provider monthly caps | Quota + cap; alert at 80% | 1+ |
| R4 | Chat history table contains sensitive admin queries and (post-Phase 3) redacted tool results | At-rest encryption + 30-day retention | 1+ |
| R5 | Embeddings table contains plaintext code chunks | At-rest encryption + separate DB role + backup separation | 2+ |
| R6 | Approval-fatigue causes admin to rubber-stamp legitimate-looking diffs | Critic agent + diff preview + cooldown for repeated approvals; UX review post-Phase 3 | 3+ |
| R7 | Sandbox escape bug in `oet-devbox` (kernel / container runtime) | Reduce blast radius via least-privilege; track CVE feed; quarterly base-image refresh | 4+ |
| R8 | LLM hallucinates a destructive command that passes denylist but admin approves | Approval modal shows full command + dry-run mode for shell where feasible; critic agent flags | 4+ |
| R9 | Multi-provider rollout adds attack surface (each provider = one more potential leak path) | Per-provider data-retention review; rotate keys quarterly; off-host audit | 5+ |
| R10 | Side-channel inference via provider rate-limit signals or latency variance | Accepted; not mitigated | 1+ |
| R11 | Future MCP server bridge (deferred to v2) re-opens this signoff | Out of scope; new signoff required | — |

## 5. Decision authority

- Phase ship decisions: **platform owner** + at least one of (`system_admin`, security reviewer). Two-person rule.
- Kill switch flip: **any** `system_admin` with `ai_assistant:manage`. Audited per `RuntimeSettingsUpdated` + `kill_switch_toggle`.
- Permission grant `ai_assistant:unrestricted`: **platform owner only**. Audited via existing permission-change `AuditEvent`.
- Mission-critical write denylist exception: **NOT ALLOWED**. Any change to scoring/rulebook/grading goes through normal PR + pixel-diff process.
- `git push` re-enablement: **NOT ALLOWED in any phase**. Re-opening this requires a new threat acceptance + complete redesign signoff.

## 6. Phase-by-phase signoff

For each phase that ships, owner signs the block below. Signatures must be in-date relative to the deploy SHA.

### Phase 0 — Schema & Permissions

| Field | Value |
| --- | --- |
| Owner name | _________________________ |
| Date | _________________________ |
| Deploy SHA | _________________________ |
| Acknowledged residual risks | (none — no surface) |
| Signature | _________________________ |

### Phase 1 — MVP Streaming Chat (no tools)

| Field | Value |
| --- | --- |
| Owner name | _________________________ |
| Date | _________________________ |
| Deploy SHA | _________________________ |
| Acknowledged residual risks | R1, R2, R3, R4, R10 |
| Per-admin daily token quota | __________ tokens |
| Per-provider monthly cap (USD) | __________ |
| Default provider | __________ |
| `GlobalEnabled` initial state | ☐ false (recommended) / ☐ true |
| Signature | _________________________ |

### Phase 2 — Codebase RAG (read-only retrieval)

| Field | Value |
| --- | --- |
| Owner name | _________________________ |
| Date | _________________________ |
| Deploy SHA | _________________________ |
| pgvector installed on prod (date / superuser) | _________________________ |
| Backup → restore round-trip verified on staging (date) | _________________________ |
| Acknowledged residual risks | R1, R2, R3, R4, R5, R10 |
| Indexed allowlist confirmed | ☐ matches §10 runbook |
| Untrusted-content indexing | ☐ off (Phase 2 default) |
| Signature | _________________________ |

### Phase 3 — Write Tools

| Field | Value |
| --- | --- |
| Owner name | _________________________ |
| Date | _________________________ |
| Deploy SHA | _________________________ |
| `oet-devbox` deployed and isolated per DevOps §1 | ☐ verified |
| Mission-critical write denylist tested | ☐ verified |
| Per-call nonce approval tested | ☐ verified |
| Secret-scan on `read_file` tested | ☐ verified |
| Acknowledged residual risks | R1, R2, R3, R4, R5, R6, R10 |
| `RequireApprovalAlways` initial state | ☐ true (recommended) |
| Signature | _________________________ |

### Phase 4 — Shell, Git, Reindex, Restart

| Field | Value |
| --- | --- |
| Owner name | _________________________ |
| Date | _________________________ |
| Deploy SHA | _________________________ |
| Devbox sandbox audited per DevOps §1, §2 | ☐ verified |
| Permanent denylist L8 tested with negative cases | ☐ verified |
| `restart_service` honors `.deploy.lock` | ☐ verified |
| `git push` confirmed NOT in tool registry | ☐ verified |
| Per-admin daily quota effective | ☐ verified |
| Acknowledged residual risks | R1, R2, R3, R4, R5, R6, R7, R8, R10 |
| Signature | _________________________ |

### Phase 5 — Polish, Multi-provider, GA

| Field | Value |
| --- | --- |
| Owner name | _________________________ |
| Date | _________________________ |
| Deploy SHA | _________________________ |
| Off-host append-only audit sink configured | ☐ verified (target: _____) |
| All Phase 1–4 gates re-verified on this SHA | ☐ verified |
| Per-provider DPA reviewed | ☐ verified (providers: ____________) |
| Retention workers tested | ☐ verified |
| Acknowledged residual risks | R1, R2, R3, R4, R5, R6, R7, R8, R9, R10 |
| Signature | _________________________ |

## 7. Annual re-acknowledgement

Owner re-signs this document annually (calendar year), or whenever any of the following changes:

- New tool added to registry.
- New LLM provider added.
- Denylist L8 modified.
- Mission-critical surface list (`AGENTS.md` Common Gotchas) modified.
- Devbox sandbox parameters relaxed.
- Per-admin quota or per-provider cap raised by >2× baseline.
- Retention defaults relaxed.
- Trust-tier separation policy modified.
- Off-host audit sink reconfigured.

| Year | Owner name | Date | Signature |
| --- | --- | --- | --- |
| 2026 | _____________ | _____________ | _____________ |
| 2027 | _____________ | _____________ | _____________ |
| 2028 | _____________ | _____________ | _____________ |

## 8. Phase-by-phase ship gates summary

Before signing a phase block above, owner has confirmed:

- All acceptance gates in `AI-ASSISTANT-ROLLOUT-PLAN.md` for that phase are green on the candidate SHA.
- `release-evidence-<sha>` is signed and includes immutable image digests for `oet-api`, `oet-web`, and (Phase 3+) `oet-devbox`.
- Staging deploy of the same SHA passed full E2E including Playwright smoke for AI Assistant role gating.
- A verified Postgres backup taken within the last 24 hours exists and is restorable.
- Two-person rule satisfied (owner + security reviewer or `system_admin`).
- All open items in `AI-ASSISTANT-ROLLOUT-PLAN.md` §9 are either resolved or accepted in writing for that phase.
