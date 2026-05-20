# Admin AI Assistant — Architecture Design

> **Scope:** In-app, Copilot-Chat-style agentic chatbot embedded in the OET Prep Platform, **admin role only**. Locked decisions in the prompt are treated as immutable. This is the target architecture and historical design record. The currently shipped surface is narrower; see [AI-ASSISTANT-PROGRESS.md](AI-ASSISTANT-PROGRESS.md) and [AI-ASSISTANT-ADMIN-RUNBOOK.md](AI-ASSISTANT-ADMIN-RUNBOOK.md) for live behavior.
>
> **Source-of-truth anchors:** [AGENTS.md](../AGENTS.md) §"AI calls (MISSION CRITICAL)", §"Runtime Settings", §"Security Considerations", §"Backend (.NET)"; [.github/copilot-instructions.md](../.github/copilot-instructions.md) "Prompt Defense Baseline".
>
> **Companion docs:** [AI-ASSISTANT-THREAT-MODEL.md](AI-ASSISTANT-THREAT-MODEL.md), [AI-ASSISTANT-DEVOPS.md](AI-ASSISTANT-DEVOPS.md), [AI-ASSISTANT-UX-SPEC.md](AI-ASSISTANT-UX-SPEC.md), [AI-ASSISTANT-ROLLOUT-PLAN.md](AI-ASSISTANT-ROLLOUT-PLAN.md), [AI-ASSISTANT-ADMIN-RUNBOOK.md](AI-ASSISTANT-ADMIN-RUNBOOK.md), [AI-ASSISTANT-THREAT-ACCEPTANCE.md](AI-ASSISTANT-THREAT-ACCEPTANCE.md), [AI-ASSISTANT-PHASE0-RESEARCH.md](AI-ASSISTANT-PHASE0-RESEARCH.md).

---

## 0. Design summary

A complete **Admin AI Assistant** subsystem target adds:

1. A floating chat widget on every `/admin/*` route, hard-disabled for any non-admin caller at four enforcement points (middleware, endpoint, hub, widget mount).
2. An **agent orchestrator** (planner → executor → critic) running server-side that streams over SignalR.
3. A **pluggable provider layer** (`ILlmProvider`) for GitHub Copilot, OpenAI, Anthropic, Azure OpenAI, GitHub Models, generic OpenAI-compatible.
4. A future **tool registry** for filesystem, semantic search (pgvector), shell, git, deploy/restart, and reindex - not live in Phase 1 and approval-gated before any production enablement.
5. A future **codebase indexing pipeline** writing to a new `AiCodebaseChunk` table with `vector(1536)` embeddings in the existing Postgres volume.
6. Reuse of `IAiGatewayService` (grounded prompt + `CompleteAsync`), `IRuntimeSettingsProvider` (secrets, kill-switch), `IFileStorage` (binary attachments), and SignalR (hub pattern from `ConversationHub`).

Two new permissions extend the existing 19-entry `AdminPermissions` registry:

- `ai_assistant:manage` — surfaced as `ManageAiAssistant`. Required for `/admin/settings/ai-assistant`, provider key CRUD, role matrix edits, kill-switch toggle.
- `ai_assistant:unrestricted` — surfaced as `UseAiAssistantUnrestricted`. Future high-risk tool phases must keep this grant platform-owner-only. It may only skip approval for read-only tools after threat-acceptance gates pass; it must never skip approvals for `write_file`, `run_command`, `git`, restart/deploy, or destructive operations.

## 1. High-level architecture (ASCII)

```text
┌──────────── BROWSER (admin only) ────────────┐
│  AiAssistantWidget.tsx  (floating; role-guarded)
│       │                                       │
│       │ REST (control)        SignalR (data)  │
│       ▼                            ▼          │
│  /v1/admin/ai-assistant/...   /v1/ai-assistant/hub
└──────┬─────────────────────────────┬──────────┘
       │                             │
       │ Next.js BFF proxy (/v1/* allowlist)
       ▼                             ▼
┌──────────── ASP.NET Core 10 ──────────────────┐
│  AiAssistantEndpoints   AiAssistantHub        │
│        │                       │              │
│        ▼                       ▼              │
│   AiAssistantOrchestrator (per-turn)          │
│   Planner ──► Executor ──► Critic ──► Stream  │
│        │           │           │              │
│        ▼           ▼           ▼              │
│  IAiGatewayService  IAgentToolRegistry        │
│  (grounded prompt   (read_file, write_file,   │
│   + CompleteAsync)   search, run_command,     │
│        │             git, restart_service,    │
│        ▼             reindex)                 │
│  ILlmProvider impls           │               │
│  (Copilot,OpenAI,Anthropic,   ▼               │
│   Azure,GH Models,generic)  ICodebaseExecutor │
└────────────┬──────────────────┬───────────────┘
             ▼                  ▼
        LLM HTTPS          Postgres 17 + pgvector
                           (existing volume —
                            AiChatThread/Message,
                            AiProviderConfig,
                            AiCodebaseChunk[vector(1536)],
                            AiUsageLog, AiAuditEvent,
                            AiToolInvocation,
                            AiRolePermissionMatrix)
```

## 2. Data model

All entities live under `backend/src/OetLearner.Api/Domain/AiAssistant/`. EF Core 10, PostgreSQL via Npgsql + `Pgvector.EntityFrameworkCore`. All ids are `string(64)` ULIDs to match existing convention.

### 2.1 `AiChatThread`

| Property | Type | EF column | Notes |
| --- | --- | --- | --- |
| `Id` | `string` | `varchar(64)` PK | ULID |
| `OwnerUserId` | `string` | `varchar(64)` FK ON DELETE CASCADE | always an admin |
| `Title` | `string` | `varchar(200)` default `"New chat"` | LLM-suggested after first turn |
| `ProviderId` | `string?` | `varchar(64)` FK nullable | per-thread override |
| `ModelId` | `string?` | `varchar(128)` | null = provider default |
| `SystemPromptOverride` | `string?` | `text` (max 8 KiB) | appended to grounded chatbot system prompt |
| `CreatedAt` / `ArchivedAt` | `DateTimeOffset` / `?` | `timestamptz` | soft delete |
| `TotalPromptTokens` / `TotalCompletionTokens` | `long` | `bigint` | usage rollup |
| `TotalCostCents` | `int` | `int` | converted at recording time |

Indexes: `IX_AiChatThread_OwnerUserId_CreatedAt DESC`, partial `WHERE archived_at IS NULL`.

### 2.2 `AiChatMessage`

`Id`, `ThreadId` (FK CASCADE), `Role` (enum: `system|user|assistant|tool`), `ContentMarkdown` (text), `ToolInvocationsJson` (jsonb), `AttachmentsJson` (jsonb), `ModelId`, `ProviderId`, `PromptTokens`, `CompletionTokens`, `CostCents`, `ParentMessageId` (supports branch/regenerate), `CreatedAt`. Indexes: `IX_AiChatMessage_ThreadId_CreatedAt`, `IX_AiChatMessage_ParentMessageId`.

### 2.3 `AiProviderConfig`

`Id`, `Kind` (enum: `github_copilot|openai|anthropic|azure_openai|github_models|openai_compatible`), `DisplayName` (unique), `EndpointUrl?` (required for `azure_openai`/`openai_compatible`), `EncryptedApiKey?` (Data Protection purpose `AiAssistant.ProviderSecret.v1` — never logged), `EncryptedOrgId?`, `DefaultModelId`, **`AllowedModelsCsv` stored as `text` not `varchar(N)`** to avoid the historic overflow bug, `IsEnabled`, `IsDefault` (partial unique index `WHERE is_default = true`), `MonthlyBudgetCents?`, `ByokOnly`, `CreatedAt`/`UpdatedAt`.

### 2.4 `AiRolePermissionMatrix` (future-proof, minimal seed)

`Id`, `Role` (`admin|expert|learner|sponsor`), `Permission` (`ai_assistant:use|ai_assistant:manage|ai_assistant:unrestricted`), `Allowed`. Unique `(Role, Permission)`. Seed = `ai_assistant:use` true for admins, management/unrestricted false unless explicitly granted, and all non-admin roles false. **Contract**: even if a row says `allowed=true` for a non-admin role, middleware/hub still rejects — table is only an upper bound.

### 2.5 `AiCodebaseChunk`

`Id`, `RepoSha` (`char(40)`), `Path` (`varchar(1024)`), `StartLine`/`EndLine` (1-based inclusive), `Language` (`csharp|ts|tsx|md|json|yaml|sql|prose`), `Kind` (`function|class|module|heading|paragraph|config`), `Symbol?`, `Content` (≤ 8000 chars after chunker), `TokenCount`, **`Embedding Pgvector.Vector vector(1536)`**, `ContentHash` SHA-256, `IndexedAt`.

Indexes:

- `UX_AiCodebaseChunk_Path_ContentHash` unique → idempotent upsert.
- `IX_AiCodebaseChunk_RepoSha` → orphan cleanup.
- **Vector index**: `CREATE INDEX ON ai_codebase_chunk USING hnsw (embedding vector_cosine_ops) WITH (m=16, ef_construction=64)`. HNSW chosen over IVFFlat: small corpus (~50k–200k chunks), recall > 95 % without `ANALYZE` retraining.

### 2.6 `AiUsageLog` (chatbot-scoped — distinct from global `AiUsageRecord`)

`Id`, `ThreadId`, `MessageId`, `ProviderId`, `ModelId`, `CallKind` (`ChatbotConversation`), `FeatureCode` (`admin.ai_chatbot`), `PromptTokens`/`CompletionTokens`, `CostCents`, `LatencyMs`, `Status` (`ok|provider_error|refused_ungrounded|refused_quota|cancelled`), `ErrorClass?`, `CreatedAt`.

> `IAiUsageRecorder` already writes the global `AiUsageRecord`. `AiUsageLog` is the chatbot-analytics overlay so the admin explorer can answer "tokens by thread / by tool-using turn" without scanning the global table.

### 2.7 `AiAuditEvent`

Distinct from global `AuditEvent` because before/after JSON for diffs can exceed the global table's `varchar(4000)` limit.

`Id`, `ActorUserId`, `Action` (`thread_create|message_send|tool_invoke|tool_approve|tool_deny|file_write|git_op|shell_exec|provider_update|kill_switch_toggle`), `TargetType`, `TargetId`, `BeforeJson?` (jsonb), `AfterJson?` (jsonb), `IpAddress?`, `UserAgent?`, `CreatedAt`. Retention: 365 days swept by `AiAuditRetentionWorker`.

### 2.8 `AiToolInvocation`

`Id`, `MessageId` (FK), `ToolName`, `ArgumentsJson`, `ResultJson?`, `ResultSummary?` (≤ 1024 chars for chat bubble), `Status` (`pending|awaiting_approval|approved|denied|running|succeeded|failed|cancelled|timed_out`), `DurationMs?`, `CreatedAt`, `ApprovalRequired`, `ApprovedByUserId?`, `ApprovedAt?`. Indexes: by `MessageId`, partial by `Status` for active states.

## 3. Agent loop (orchestration)

### 3.1 Roles

- **Planner agent** — receives user turn + thread history + top-k RAG. Emits JSON plan (ordered tool calls with rationales + final-answer schema). Low-temperature.
- **Executor agent** — iteratively calls tools per OpenAI function-calling schema. Streams token deltas between tool calls. May re-plan on tool error.
- **Critic agent** — runs **once at end** over (final assistant message, tool invocations, original user request). Emits `CriticVerdict { passed, concerns[], recommendedFollowUp? }`. If `passed=false` and concern is `security`, orchestrator **redacts** the assistant message and asks executor for a corrected version (max 1 retry).

### 3.2 Limits (`AiAssistantOptions`)

| Constant | Default | Why |
| --- | --- | --- |
| `MaxIterationsPerTurn` | 12 | Runaway-loop ceiling. |
| `TokenBudgetPerTurnPromptIn` | 60 000 | system + history + RAG. |
| `TokenBudgetPerTurnCompletionOut` | 8 000 | per turn across agents. |
| `ToolCallTimeoutSec` | per-tool default, cap 300 s | Shell has its own param cap. |
| `MaxParallelToolCalls` | 4 | Models support parallel calls; higher = `ICodebaseExecutor` contention. |
| `MaxAttachmentsPerMessage` | 8 | UI + IFileStorage cap. |
| `MaxIndexedChunksPerRagQuery` | 12 | Returned + re-ranked. |

### 3.3 Tool-call schema (provider-agnostic OpenAI function-calling shape)

```text
toolCall = { id: ULID, name: matches IAgentTool.Name, arguments: object (JSON-schema validated) }
toolResult = { id: matches toolCall.id, status: succeeded|failed|denied|timed_out|cancelled,
               content: string (≤ 4 KiB for model context), resourceRef?: ULID into AiToolInvocation }
```

Anthropic + GitHub Models translations live **inside each `ILlmProvider` adapter** — orchestrator only speaks the OpenAI shape.

### 3.4 Cancellation

- Each turn opens `CancellationTokenSource` registered against SignalR connection-id + message-id.
- Client `CancelTurn(messageId)` → server triggers `CancellationToken`.
- `ILlmProvider.StreamCompleteAsync` honors token; in-flight tool calls receive same token and pass to `ICodebaseExecutor.RunAsync`.
- Cancelled turn writes `AiUsageLog.Status="cancelled"` and any running `AiToolInvocation.Status="cancelled"`.
- Connection drop → 30s SignalR keep-alive grace, then cancel.

### 3.5 Streaming policy (hub frame order per turn)

1. `MessageStart` (assistant row created `status=streaming`).
2. Repeating: `TokenDelta` ⊕ `ToolCallStart` → `ToolCallDelta*` → `ToolCallResult` (interleaved with optional `ApprovalRequest` blocking the loop).
3. `MessageEnd` with usage totals.
4. If critic fails irrecoverably: `Error { code: "critic_refused", recoverable: false }`.

## 4. Provider abstraction (`ILlmProvider`)

The provider layer sits **inside** the gateway boundary. Every chatbot call still goes `Orchestrator → IAiGatewayService.CompleteAsync(...)`. Internally the gateway resolves an `IAiModelProvider` (existing interface) to a thin `ChatbotProviderRouter` which fans out to one `ILlmProvider`. This keeps the grounding invariant intact (`AiGatewayService.cs` physically refuses ungrounded prompts).

### 4.1 Interface signature

```csharp
public interface ILlmProvider
{
    string Name { get; }
    AiProviderKind Kind { get; }

    Task<IReadOnlyList<string>> ListModelsAsync(
        LlmProviderCredential credential, CancellationToken ct);

    IAsyncEnumerable<ChatStreamDelta> StreamCompleteAsync(
        ChatCompletionRequest request, LlmProviderCredential credential, CancellationToken ct);

    Task<ChatCompletionResult> CompleteAsync(
        ChatCompletionRequest request, LlmProviderCredential credential, CancellationToken ct);
}

public sealed record ChatCompletionRequest(
    string ModelId,
    IReadOnlyList<ChatMessage> Messages,    // Messages[0] MUST be the grounded system prompt
    IReadOnlyList<ToolSpec>? Tools,
    decimal? Temperature, int? MaxOutputTokens, string? Stop, bool ParallelToolCalls);

public sealed record ChatMessage(string Role, string? Content,
    IReadOnlyList<ToolCall>? ToolCalls, string? ToolCallId);

public sealed record ToolSpec(string Name, string Description, JsonElement JsonSchema);
public sealed record ToolCall(string Id, string Name, JsonElement Arguments);

public abstract record ChatStreamDelta;
public sealed record TextDelta(string Text) : ChatStreamDelta;
public sealed record ToolCallDeltaFrame(string ToolCallId, string Name, string ArgumentsChunk) : ChatStreamDelta;
public sealed record UsageDelta(int? PromptTokens, int? CompletionTokens) : ChatStreamDelta;
public sealed record FinishDelta(string FinishReason) : ChatStreamDelta;

public sealed record LlmProviderCredential(string ApiKey, string? OrganizationId,
    string? EndpointOverride, IReadOnlyDictionary<string, string>? Extras);
```

### 4.2 Adapter pattern

```text
Orchestrator → AiGatewayRequest { Prompt=BuildGroundedPrompt(...), FeatureCode="admin.ai_chatbot",
                                  CallKind=ChatbotConversation, Tools=registry.Specs }
            → IAiGatewayService.CompleteAsync [refuses if SystemPrompt missing rulebook header]
            → ChatbotProviderRouter : IAiModelProvider (wraps existing provider interface)
            → ILlmProvider impl (provider does not see grounding context; sees flat ChatCompletionRequest)
            → HTTPS to provider
```

Wrapper preserves two facts:

1. The gateway is still the only place that records `AiUsageRecord` via `IAiUsageRecorder` and enforces `IAiQuotaService` — `AiUsageLog` is written on top, never instead.
2. Providers cannot bypass grounding — gateway has already validated `SystemPrompt`; grounded prompt is `Messages[0]` before reaching `ILlmProvider`.

### 4.3 Provider-specific notes

- **GitHubCopilotProvider** — OAuth device-flow token in `IRuntimeSettingsProvider` (`AiAssistant:CopilotOAuthToken`, encrypted). Refresh handled internally. **API shape TBD** — see §11 O-1. Until SDK stabilises, ship a REST adapter shim targeting the public preview endpoint.
- **OpenAIProvider** — `chat.completions` with `stream=true, tools=[...], parallel_tool_calls=true`.
- **AnthropicProvider** — Messages API; `tools` + `tool_use`/`tool_result` content blocks; system prompt in top-level `system` field, not as a message.
- **AzureOpenAIProvider** — like OpenAI but `EndpointUrl` + `deployment-id` in `Extras["deployment"]`. API key auth only in v1.
- **GitHubModelsProvider** — REST `POST https://models.inference.ai.azure.com/chat/completions`; PAT in `Authorization`.
- **OpenAICompatibleProvider** — pure passthrough for Ollama, LiteLLM, vLLM, Together, Groq.

## 5. Tool registry

```csharp
public interface IAgentTool
{
    string Name { get; }
    string Description { get; }
    JsonElement ParametersSchema { get; }                     // JSON Schema 2020-12
    ToolApprovalPolicy ApprovalPolicy { get; }
    Task<AgentToolResult> InvokeAsync(AgentToolInvocationContext ctx,
        JsonElement arguments, CancellationToken ct);
}

public enum ToolApprovalPolicy
{
    None,                  // read-only, never asks
    UnlessUnrestricted,    // limited read-only convenience only; never write/shell/git/deploy/destructive
    Always                 // asks even with unrestricted (push, restart, destructive)
}
```

Full v1 surface. Every `arguments` payload is validated against its JSON schema **before** dispatch; schema violations short-circuit to `status="failed"` with `error="schema_invalid"`.

| Name | Params (sketch) | Returns | Approval | Audit `Action` |
| --- | --- | --- | --- | --- |
| `read_file` | `{ path (≤1024), startLine?, endLine?, maxBytes? (default 200_000) }` | `{ path, sha256, content, lineCount, truncated }` | `None` | `tool_invoke` |
| `write_file` | `{ path, mode: rewrite\|append\|patch, content?, unifiedDiff? (required for patch), expectedSha256? }` | `{ path, sha256, bytesWritten, unifiedDiff }` | **`Always`** | `file_write` |
| `search_codebase` | `{ query (≤1024), kind: semantic\|grep\|symbol, limit? (1..50, default 12), pathGlob?, language? }` | `{ hits: [{path, startLine, endLine, score, snippet}] }` | `None` | `tool_invoke` |
| `list_directory` | `{ path, recursive?, maxEntries? (1..2000, default 200), pattern? }` | `{ entries: [{path, kind, sizeBytes?, mtime}] }` | `None` | `tool_invoke` |
| `run_command` | `{ cwd (under /opt/oetwebapp), command (≤4096), timeoutSec? (1..300, default 60), env? }` | `{ exitCode, stdout, stderr, durationMs, truncated }` | **`Always`** | `shell_exec` |
| `git` | `{ op: status\|diff\|log\|branch\|checkout\|commit\|push\|pr_create, args? }` (per-op sub-schema) | op-specific `{ ok, summary, raw }` | `None` for `status`/`diff`/`log`; **`Always`** for `branch`/`checkout`/`commit`/`push`/`pr_create` | `git_op` |
| `reindex_codebase` | `{ scope: all\|path, path? }` | `{ scheduled, jobId, estimatedChunks }` | **`Always`** | `tool_invoke` |
| `deploy_status` | `{}` | `{ activeColor, lastDeployedSha, healthyContainers, postgresUp, npmConnected }` | `None` | `tool_invoke` |
| `restart_service` | `{ name: oet-web\|oet-api\|web-blue\|web-green\|api-blue\|api-green }` | `{ name, restartedAt, healthAfter }` | **`Always`** | `tool_invoke` |

### 5.1 Path & cwd safety

`ICodebaseExecutor` invariant: every `path`/`cwd` resolved via `Path.GetFullPath` and **rejected unless** absolute path begins with configured `CodebaseRoot` (`/opt/oetwebapp/` prod, repo root dev). Symlinks resolved **before** check. Denied paths return `status="denied"` `error="path_outside_codebase"` — never an exception with stack trace.

### 5.2 Diff preview for `write_file`

When an approval policy triggers, orchestrator emits `ApprovalRequest { toolCallId, summary, unifiedDiff }` over SignalR **before** invocation. Executor blocks on `TaskCompletionSource<bool>` keyed by `toolCallId`; client posts `RespondToApproval(toolCallId, approve)`. Timeout = 5 min → auto-deny. `UseAiAssistantUnrestricted` may only bypass approval for explicitly allowed read-only tools; it never bypasses `write_file`, `run_command`, state-changing `git` operations, restart/deploy, or destructive actions.

### 5.3 `run_command` allowlist

By default only these executables (configurable via `AiAssistant:ShellAllowlist` in `IRuntimeSettingsProvider`): `npm`, `npx`, `node`, `dotnet`, `pwsh`, `bash`, `git`, `docker`, `docker compose`, `ssh`, `curl`, `cat`, `ls`, `head`, `tail`, `rg`, `jq`, `psql`. Shell invoked **without** `set -e`, no interactive PTY, stdin closed, PATH frozen. `restart_service` does **not** go through `run_command` — dedicated `docker compose restart <slot>` with deploy-color guard.

## 6. Indexing pipeline

### 6.1 Chunking (TreeSitter-backed)

| Language | Chunk granularity | Fallback |
| --- | --- | --- |
| `csharp` | per method + per-class summary | top-level statements grouped 40-line windows |
| `ts`/`tsx` | per exported function/class/component | 40-line sliding window |
| `markdown`/`mdx` | per H2 section (H1 prepended) | 40-line window |
| `json`/`yaml` | per top-level key (parent path prepended) | whole-file if ≤ 200 lines |
| `sql` | per `CREATE`/`ALTER`/`INSERT` | split on `;` outside strings |
| `prose` (`.txt`, `.feature`) | per paragraph (~5 sentences) | 40-line window |

Hard cap: 8 000 chars / ~2 000 tokens. Stored `Content` includes a 1-line breadcrumb header (`// path/to/file.cs:120-178  Foo.Bar(int)`) for path anchor.

### 6.2 Embedding batching

- Batch size: 32 chunks/call.
- Default: OpenAI `text-embedding-3-small` (1536-dim). Pluggable via `IEmbeddingProvider`. Local fallback: Ollama `nomic-embed-text` (projected to 1536) — see O-2.
- Backoff: exp 1s/2s/4s/8s, max 5 retries on 429/5xx.
- Concurrency: 4 in-flight batches.

### 6.3 Upsert by `(Path, ContentHash)`

1. Compute current `RepoSha` (HEAD).
2. For each new chunk, compute `ContentHash = SHA256(normalised content)`.
3. Lookup `(Path, ContentHash)`:
   - **Hit** → touch `IndexedAt`.
   - **Miss** → batch-embed + insert.
4. Delete rows with `Path == this.Path AND ContentHash NOT IN currentSet` → handles in-file deletions.
5. After whole-repo scan, delete rows where `Path NOT IN scannedPaths` → handles file removals.

No-op reindex on clean tree embeds zero chunks.

### 6.4 Git post-receive hook (`scripts/deploy/post-deploy-reindex.sh`)

```text
1. Capture pre-deploy SHA from /opt/oetwebapp/.git/ORIG_HEAD.
2. git diff --name-only PRE..HEAD → changed paths.
3. POST to /v1/admin/ai-assistant/index/incremental { fromSha, toSha, changedPaths }
   with IRuntimeSettingsProvider:AiAssistant:DeployHookToken.
4. Orchestrator schedules incremental reindex (changed paths only).
5. Log result; non-zero exit does NOT fail deploy (best-effort).
```

### 6.5 Manual reindex

`POST /v1/admin/ai-assistant/index/run { scope, paths? }`. Requires `ManageAiAssistant`. Background `IndexingJob` broadcasts `IndexProgress { jobId, processed, total, currentPath }` for live UI progress bar.

## 7. Streaming contract (SignalR hub)

Path `/v1/ai-assistant/hub`. Current V1 auth uses the `AdminAiAssistantUse` policy, backed by admin role or `ai_assistant:use`. Connection rejected if `AiAssistant:GlobalEnabled` is false.

### 7.1 Client → server

| Method | Args | Notes |
| --- | --- | --- |
| `StartTurn` | `{ threadId, contentMarkdown, attachments?, providerOverrideId?, modelOverrideId? }` | Creates user message, kicks orchestrator. |
| `CancelTurn` | `{ messageId }` | Triggers turn-level cancellation. |
| `RespondToApproval` | `{ toolCallId, approve: bool, note? }` | Unblocks `ApprovalRequest`. |
| `Subscribe` | `{ threadId }` | Joins group `thread:{threadId}` for replay on reconnect. |

### 7.2 Server → client frames

| Frame | Payload |
| --- | --- |
| `MessageStart` | `{ messageId, threadId, role: assistant, startedAt }` |
| `TokenDelta` | `{ messageId, text }` |
| `ToolCallStart` | `{ messageId, toolCallId, name, argumentsPreview }` |
| `ToolCallDelta` | `{ messageId, toolCallId, argumentsChunk }` |
| `ApprovalRequest` | `{ messageId, toolCallId, toolName, summary, unifiedDiff?, allowedRoles, expiresAt }` |
| `ToolCallResult` | `{ messageId, toolCallId, status, resultSummary, resourceRef }` |
| `MessageEnd` | `{ messageId, finishReason, promptTokens, completionTokens, costCents, criticVerdict }` |
| `IndexProgress` | `{ jobId, processed, total, currentPath }` |
| `Error` | `{ scope: turn\|hub, code, message, recoverable, messageId? }` |

Frames persisted to per-thread in-memory ring (size 256) so reconnect within 60s replays missed frames via `Subscribe`. Older state reloaded from `AiChatMessage` rows.

## 8. Permission enforcement points (defence in depth)

| # | Layer | File / surface | Check |
| --- | --- | --- | --- |
| 1 | Next.js middleware | `middleware.ts` | `/admin/ai-assistant/*` requires role `admin` + `ManageAiAssistant` for settings sub-paths. Non-admins → 404 (not 403) to avoid leakage. |
| 2 | Backend REST endpoint auth | `Endpoints/AiAssistantEndpoints.cs` | `.RequireAuthorization("admin")` + `[RequireAdminPermission(...)]` filter. Settings additionally require `ManageAiAssistant`. |
| 3 | SignalR hub auth | `Hubs/AiAssistantHub.cs` | `[Authorize(Roles="admin")]` + `AdminAiAssistantUse` / `ai_assistant:use` permission + checks `GlobalEnabled`; closes connection with code `kill_switch`. |
| 4 | Hub method authorization | per hub method | `StartTurn` requires thread ownership (`thread.OwnerUserId == ctx.User.Id`). |
| 5 | Frontend route guard | `app/admin/ai-assistant/layout.tsx` | Server component reads session; non-admin → `notFound()`. Pure UX layer. |
| 6 | Frontend widget mount | `components/domain/ai-assistant/AiAssistantWidget.tsx` | `useCurrentUser()` → only renders if `role === 'admin'`. Non-admin sessions never download widget bundle. |
| 7 | Orchestrator entry | `AiAssistantOrchestrator.RunTurnAsync` | Re-reads perms from DB at turn start (not from JWT) so revocations propagate within cache TTL. |
| 8 | Tool execution gate | `IAgentToolRegistry.InvokeAsync` | Per-tool, checks `ToolApprovalPolicy` against caller perms; `Always` policies never bypassed regardless of `UseAiAssistantUnrestricted`. |
| 9 | Settings update | `AdminAiAssistantSettingsService.UpdateProviderAsync` | Requires `ManageAiAssistant`; writes `AiAuditEvent`; calls `IRuntimeSettingsProvider.Invalidate()`. |
| 10 | Kill-switch | every entry point above | Pre-flight `AiAssistant:GlobalEnabled` check; false → 503 / hub close / 404 widget. |

## 9. Kill switch

Target production design: `IRuntimeSettingsProvider` key **`AiAssistant:GlobalEnabled`** with env fallback and durable audit. Current V1 implementation is narrower: `AiAssistantSettingsService` reads `IConfiguration`, defaults disabled, and `/v1/admin/ai-assistant/kill-switch` applies a current-process in-memory override.

When `false`:

| Surface | Response |
| --- | --- |
| REST `/v1/admin/ai-assistant/*` | `503` body `{ code: "ai_assistant_disabled", banner: "..." }`. **Exception:** GETs for prior chats still work (admins need audit). Only mutating endpoints return 503. |
| SignalR hub | `OnConnectedAsync` sends `KillSwitchActive` then `Context.Abort()`; reconnects rejected. |
| Widget | Renders disabled state (greyed input, banner) instead of input. New-chat button disabled. |
| Indexing worker | Drains current batch, sleeps until re-enabled. |
| Deploy reindex hook | Treats 503 as "skip"; no-op. |

Current V1 propagation is immediate inside the current process because the admin endpoint updates in-memory state. Durable runtime-settings propagation, global `AuditEvent` writes, and hub-wide broadcast remain future production gates. V1 kill-switch flips write `AiAuditEvent`.

## 10. Threat surface (enumerated for critic / security review)

T-1 prompt injection from indexed files · T-2 runaway tool loops · T-3 secret leakage in chat history (mitigation: `read_file` denies `.env*`, `*.pem`, `*.key`, `appsettings.Production.json`; chat content not indexed) · T-4 malicious diff smuggled past approval (server echoes exact bytes that will be written) · T-5 `git push --force` (must be explicitly disallowed in `GitTool`; `push` op never accepts `--force`/`-f`) · T-6 blue/green disruption from `restart_service` mid-deploy (read deploy lockfile, refuse) · T-7 provider credential exfiltration (encrypted at rest, masked in API responses) · T-8 cross-tenant thread access (all queries filter `OwnerUserId == currentUser.Id`; no shared threads v1) · T-9 embedding model key sees code content (document; admin opts in via `ManageAiAssistant`) · T-10 cost runaway (`MonthlyBudgetCents` + pre-flight estimate + reject when exceeded) · T-11 tool argument schema bypass (strict JSON-Schema, no `additionalProperties`) · T-12 SignalR connection hijack (hub requires same JWT; ownership re-checked on every `Subscribe`) · T-13 approval race (single `TaskCompletionSource` keyed by `toolCallId`; first response wins) · T-14 PII in `AiAuditEvent.BeforeJson` (retention 365d + admin-only; DPIA documented) · T-15 critic timeout (fail-closed only for security concerns; otherwise proceed but flag).

See `AI-ASSISTANT-THREAT-MODEL.md` for the adversarial review and the must-fix / should-fix / accept partitioning.

## 11. Open questions / dependencies on user

| # | Question | Default if undecided |
| --- | --- | --- |
| O-1 | GitHub Copilot SDK API shape (preview moving) — ship `GitHubCopilotProvider` as thin REST adapter behind `AiAssistant:Providers:GithubCopilot:Enabled` so v1 ships even if SDK lands later. | Disabled-by-default; OpenAI is v1 default. |
| O-2 | Embedding model: OpenAI `text-embedding-3-small` (paid, ~$0.02/M tokens, 1536-dim native) vs local Ollama `nomic-embed-text` (free, sidecar, 768-dim → would change column size — pick before migration). | OpenAI; pgvector column 1536. Local fallback documented but not shipped. |
| O-3 | Per-thread rate limits beyond per-provider monthly budget. | Default: 30 turns/min/user, 240/day/user — soft cap, configurable. |
| O-4 | Attachment storage retention. | 90 days, `AiAttachmentRetentionWorker`. |
| O-5 | Cross-thread "memory" / vector store of past chats v1 scope? | **Out of scope v1.** Only active thread fed back. |
| O-6 | MCP server support — `IAgentTool` hosts an MCP-server bridge? | Deferred v2; design `IAgentTool` so `McpBridgeTool` lands without breaking registry. |
| O-7 | Audit export (PDF/CSV) for given actor. | Defer; rely on `psql` v1. |
| O-8 | Cost ceiling enforcement granularity — hard-fail or soft-warn at 80% of budget? | Soft-warn 80%, hard-fail 100%. |

## 12. AGENTS.md / domain-doc constraints honoured

- **AI calls (MISSION CRITICAL)** — every chatbot LLM call via `IAiGatewayService.BuildGroundedPrompt(...)` + `CompleteAsync(...)`. New enum members `AiCallKind.ChatbotConversation`, `AiTaskMode.ChatbotChat`, `AiFeatureCodes.AdminChatbot`. Grounding header preserved; chatbot variant appends admin-tooling safety section but does **not** embed rulebooks (chatbot doesn't grade).
- **Runtime Settings (MISSION CRITICAL)** — all provider keys + Copilot OAuth + kill-switch via `IRuntimeSettingsProvider` with env fallback; encrypted purpose `AiAssistant.ProviderSecret.v1`; writes emit `AuditEvent`.
- **Security Considerations** — extends 19-perm `AdminPermissions` registry; JWT validation path unchanged; middleware widened only for `/admin/ai-assistant/*`.
- **Content uploads** — attachment binary I/O via `IFileStorage`; no raw `File.*`/`Path.*`. `write_file` tool calls go through `ICodebaseExecutor` (new), the only component allowed to touch live `/opt/oetwebapp/`.
- **Backend (.NET)** — Minimal API in `Endpoints/AiAssistantEndpoints.cs`; services under `Services/AiAssistant/`; DTOs in `Contracts/AiAssistant/`; EF migrations in `Data/Migrations/`.
- **AI Usage Policy** — `IAiUsageRecorder` continues to write global `AiUsageRecord` for every chatbot call; `AiUsageLog` is analytics overlay, not replacement.
- **Deployment** — `oetwebsite_oet_postgres_data` volume preserved; pgvector enabled via one-time DBA `CREATE EXTENSION IF NOT EXISTS vector` (NOT in EF migration — API user is not superuser). No volume recreation.
- **Prompt Defense Baseline** — no secrets in chat output; tool output + indexed files treated as untrusted (critic agent + injection-resistant system prompt).

## 13. Implementation guidance (phased, ship behind `GlobalEnabled=false`)

See `AI-ASSISTANT-ROLLOUT-PLAN.md` for the full Phase 0 → Phase 5 plan with acceptance criteria and validation ladders. Summary:

1. **Phase A — Schema + permissions.** EF migration: 8 tables. Adds 2 perms to `AdminPermissions.All`. Seeds default provider config disabled.
2. **Phase B — Provider abstraction + gateway extension.** `ILlmProvider` + OpenAI impl; gateway accepts `AiCallKind.ChatbotConversation`; chatbot-variant grounded prompt builder.
3. **Phase C — Read-only tools + indexing.** `read_file`, `list_directory`, `search_codebase` (grep + semantic). No mutation tools.
4. **Phase D — Hub + widget MVP.** SignalR hub, frame contract, React widget streams text only.
5. **Phase E — Mutation tools + approval flow.** `write_file`, `run_command`, `git` (no push). Approval UI.
6. **Phase F — Critic agent + audit.** Critic loop, `AiAuditEvent` for every tool, audit explorer in admin.
7. **Phase G — Push/restart/reindex/deploy.** `git push`, `restart_service`, `reindex_codebase`, `deploy_status`; post-deploy reindex hook.
8. **Phase H — Multi-provider + BYOK + budgets.** Anthropic, Azure, GitHub Models, generic; `MonthlyBudgetCents`; cost ceiling.
9. **Phase I — GitHub Copilot provider.** When SDK stabilises (O-1).

Each phase ends with: tsc + lint + Vitest + `dotnet test` (SQLite in-memory per AGENTS.md), plus Playwright smoke that opens widget as admin and confirms non-admin session does not download the bundle.

---

**End of design.**
