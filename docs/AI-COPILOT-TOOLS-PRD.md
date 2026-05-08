# AI Copilot — Phase 5: Tool Calling

> **Status:** Locked decisions, implementation in flight.
> **Owner:** AI Copilot work-stream (Ralph-loop, parallel subagents).
> **Cadence:** Phase 5 ships and deploys before Phase 6 begins (locked decision `deploy-cadence=B`).
> **Scope guard:** This PRD is gated by `docs/AI-COPILOT-PRD.md` and `docs/AI-COPILOT-PROGRESS.md`. Any divergence here MUST be reflected back into those docs in the same commit.

## 1. Goal

Allow the rulebook-grounded gateway to expose a **deny-by-default, admin-grantable, per-feature** set of structured tools that the model may call as part of a single completion. Tool calls are validated, RBAC-checked, dispatched in-process, **always** audited, and never bypass the existing `PromptNotGroundedException` invariant.

### Non-goals (v1)

- **Streaming** tool deltas. v1 is non-streaming, single round-trip.
- **Cross-provider** tool calls. v1 supports the Copilot / `Azure.AI.Inference` adapter only. Other providers (`Anthropic`, `RegistryBacked` / OpenAI-compatible, `Mock`) silently ignore the new `Tools` field on the provider request and behave bit-for-bit as today.
- **Free-form sandboxed code execution.** All tools are pre-registered C# `IAiToolExecutor` implementations.
- **Frontend tool-call rendering.** Server returns the final text only; intermediate `tool_calls` are not surfaced to the learner UI in v1.

## 2. Locked decisions (verbatim)

| Decision | Value | Source |
| --- | --- | --- |
| `p5-tools-allowlist` | **C** — Full set including external-network tools | User popup answer |
| `p5-rbac-default` | **A** — Deny-by-default; admin opt-in per feature | User popup answer |
| Round-trip cap | **`N_TOOL_CALLS_PER_TURN = 4`** | This PRD (safety) |
| Tool catalog v1 | **7 tools** (4 read · 2 self-scoped write · 1 external-network) | Derived from C |
| Provider scope v1 | **Copilot only** | This PRD (risk-bounded rollout) |

## 3. Tool catalog (v1)

All 7 tools below are seeded by `AiToolSeedWorker` on first boot. Each row is `IsActive=true` but **no `AiFeatureToolGrant` rows are seeded** — RBAC is deny-by-default (`p5-rbac-default=A`).

| `Code` | Category | Side-effect | Purpose | Args (JSON Schema 2020-12) | Result shape |
| --- | --- | --- | --- | --- | --- |
| `lookup_rulebook_rule` | `Read` | none | Resolve a rulebook rule by `kind` + `rule_id` so the model can quote-cite it | `{ kind: enum[writing,speaking,reading,listening,grammar,pronunciation,conversation], rule_id: string }` | `{ rule_id, body_markdown, profession_scope }` |
| `lookup_vocabulary_term` | `Read` | none | Pull a `MedicalVocabularyTerm` row by lemma | `{ lemma: string, profession?: string }` | `{ lemma, definition, examples[] }` |
| `get_user_recent_attempts` | `Read` | none | Surface the caller's last N graded attempts in a single skill | `{ skill: enum[writing,reading,listening,speaking,pronunciation], limit: int (1..10) }` | `{ attempts: [{ id, scored_at, scaled_score }] }` |
| `search_recall_set` | `Read` | none | Scoped search inside a public recall question bank | `{ q: string (1..120), set_code?: string, limit?: int (1..20) }` | `{ items: [{ id, prompt, year_band }] }` |
| `save_user_note` | `Write` (self) | DB write to caller's `UserNote` | Persist a short note authored by the model on the caller's behalf | `{ title: string (1..120), body_markdown: string (1..2000) }` | `{ note_id, created_at }` |
| `bookmark_recall_term` | `Write` (self) | DB write to caller's `RecallBookmark` | Bookmark a recall item to caller's revision queue | `{ recall_item_id: string }` | `{ bookmark_id }` |
| `fetch_dictionary_definition` | `ExternalNetwork` | outbound HTTP to `api.dictionaryapi.dev` | Public dictionary lookup for non-medical English | `{ word: string (1..40) }` | `{ word, phonetic?, meanings: [{ part_of_speech, definitions: [string] }] }` |

### External-network controls (mandatory per locked `C`)

`fetch_dictionary_definition` is the only v1 external-network tool. Controls:

- Named `HttpClient` `"AiTool.FetchDictionary"`, `Timeout=4s`, `MaxResponseBytes=64KiB`.
- **Per-host allowlist:** `AiToolOptions.AllowedExternalHosts = [ "api.dictionaryapi.dev" ]`. Any other host = `Outcome="blocked_host"`, no HTTP issued.
- Request headers scrubbed: only `Accept: application/json`, `User-Agent: OET-Tool/1`. **No** auth headers, **no** cookies, **no** caller PII.
- Per-call budget recorded in `AiToolInvocation.LatencyMs`; per-day budget capped at `AiToolOptions.ExternalNetworkBudget.PerUserDailyCalls = 200` enforced inside `AiToolInvoker`.
- Response is parsed against the documented schema; arbitrary fields are dropped before being returned to the model.

## 4. Data model

### 4.1 `AiTool`

```csharp
public sealed class AiTool
{
    [Key, MaxLength(64)]    public string Id { get; set; } = "";          // ulid
    [Required, MaxLength(64)] public string Code { get; set; } = "";      // unique; matches IAiToolExecutor.Code
    [Required, MaxLength(128)] public string Name { get; set; } = "";
    [MaxLength(512)]         public string Description { get; set; } = "";
    public AiToolCategory Category { get; set; } = AiToolCategory.Read;   // Read | Write | ExternalNetwork
    [Required] public string JsonSchemaArgs { get; set; } = "{}";         // JSON Schema 2020-12, max 8 KiB
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    [MaxLength(64)] public string? UpdatedByAdminId { get; set; }
}

public enum AiToolCategory { Read = 0, Write = 1, ExternalNetwork = 2 }
```

Index: `IX_AiTools_Code` (unique).

### 4.2 `AiFeatureToolGrant`

```csharp
public sealed class AiFeatureToolGrant
{
    [Key, MaxLength(64)]      public string Id { get; set; } = "";        // ulid
    [Required, MaxLength(64)] public string FeatureCode { get; set; } = "";
    [Required, MaxLength(64)] public string ToolCode { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    [MaxLength(64)] public string? UpdatedByAdminId { get; set; }
}
```

Indexes: `IX_AiFeatureToolGrants_FeatureCode_ToolCode` (unique composite); `IX_AiFeatureToolGrants_FeatureCode`.

### 4.3 `AiToolInvocation`

```csharp
public sealed class AiToolInvocation
{
    [Key, MaxLength(64)]      public string Id { get; set; } = "";
    [Required, MaxLength(64)] public string AiUsageRecordId { get; set; } = ""; // FK, nullable behaviour: SetNull on usage delete
    [Required, MaxLength(64)] public string FeatureCode { get; set; } = "";
    [Required, MaxLength(64)] public string ToolCode { get; set; } = "";
    public AiToolCategory Category { get; set; }
    [MaxLength(64)]  public string? UserId { get; set; }
    public int TurnIndex { get; set; }                                    // 1-based
    [MaxLength(64)]  public string ArgsHash { get; set; } = "";           // sha256 hex
    [MaxLength(64)]  public string ResultHash { get; set; } = "";
    public AiToolOutcome Outcome { get; set; }
    [MaxLength(64)]  public string? ErrorCode { get; set; }
    [MaxLength(512)] public string? ErrorMessage { get; set; }
    public int LatencyMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public enum AiToolOutcome { Success = 0, ArgsInvalid = 1, RbacDenied = 2, ProviderError = 3, BlockedHost = 4, BudgetExceeded = 5, ExecutionError = 6 }
```

Indexes: `(AiUsageRecordId, TurnIndex)`, `(FeatureCode, CreatedAt)`, `(ToolCode, CreatedAt)`.

> **Why a child table, not denormalize on `AiUsageRecord`** — see researcher note: a single AI call may invoke 0..N tools across N turns. Keeping `AiUsageRecord` "one row per logical call" preserves all existing analytics + grafana queries unchanged.

## 5. Service contracts

### 5.1 `IAiToolExecutor`

```csharp
public interface IAiToolExecutor
{
    string Code { get; }                                       // matches AiTool.Code
    AiToolCategory Category { get; }
    string JsonSchemaArgs { get; }                              // immutable; loaded once
    Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct);
}

public sealed record AiToolContext(
    string FeatureCode,
    string? UserId,
    string? AuthAccountId,
    string AiUsageRecordId,
    int TurnIndex);

public sealed record AiToolExecutionResult(
    AiToolOutcome Outcome,
    JsonElement? ResultJson,
    string? ErrorCode = null,
    string? ErrorMessage = null);
```

Each tool registers itself via `services.AddScoped<IAiToolExecutor, FooTool>()`. The DI multi-binding lets the registry enumerate all implementations.

### 5.2 `IAiToolRegistry` (singleton, deny-by-default)

```csharp
public interface IAiToolRegistry
{
    IReadOnlyList<AiToolDefinition> ResolveForFeature(string featureCode);  // returns only tools the feature is granted
    bool IsKnownToolCode(string toolCode);
}
```

Implementation reads `AiFeatureToolGrant WHERE FeatureCode = @fc AND IsActive` joined to `AiTool WHERE IsActive`. Cached in-memory for **30 seconds** with an `IMemoryCache` entry keyed by `featureCode`; admin endpoints invalidate on grant mutation. Cache miss returns empty list — never throws.

### 5.3 `IAiToolInvoker` (scoped)

```csharp
public interface IAiToolInvoker
{
    Task<AiToolInvocation> InvokeAsync(
        string featureCode,
        string aiUsageRecordId,
        int turnIndex,
        string toolCode,
        JsonElement argsJson,
        AiToolContext ctx,
        CancellationToken ct);
}
```

Logic, in this order, every call:

1. **RBAC check.** If `(featureCode, toolCode)` is not in `IAiToolRegistry.ResolveForFeature(featureCode)` → `Outcome=RbacDenied`, audit, return.
2. **Schema validation.** Validate `argsJson` against `AiTool.JsonSchemaArgs` via NJsonSchema or System.Text.Json schema. On failure → `Outcome=ArgsInvalid`, audit, return.
3. **External-network budget gate.** If category is `ExternalNetwork`, check `(UserId, today)` against `AiToolOptions.ExternalNetworkBudget.PerUserDailyCalls`. On exceed → `Outcome=BudgetExceeded`, audit, return.
4. **Dispatch.** Resolve `IAiToolExecutor` keyed by `toolCode`, call `ExecuteAsync`. Catch all exceptions → `Outcome=ExecutionError`, log + audit, return.
5. **Audit unconditionally.** Persist `AiToolInvocation` row in same DB context. Hash args + result with SHA-256.

## 6. Gateway integration

`AiGatewayService.CompleteAsync` is wrapped with a **bounded multi-turn loop**:

```text
loop turn = 1..N_TOOL_CALLS_PER_TURN (4):
    response = provider.CompleteAsync(messages, tools, tool_choice="auto")
    if response.ToolCalls is empty -> break, return response.Text
    for each tool_call in response.ToolCalls:
        invocation = invoker.InvokeAsync(...)
        messages.append(assistant_with_tool_calls)
        messages.append(tool_message with invocation.ResultJson or error JSON)
if loop exits without natural finish -> return last response.Text plus an internal "tool_loop_truncated" tag in PolicyTrace
```

- **The grounded system prompt is unchanged across turns.** The `PromptNotGroundedException` invariant remains: only the first system prompt is checked, all subsequent provider calls reuse `request.Prompt.SystemPrompt`.
- **Token + cost accounting** sums `PromptTokens` + `CompletionTokens` across all turns into the **single** `AiUsageRecord` row.
- **Failure semantics**:
  - `BlockedHost`, `BudgetExceeded`, `ArgsInvalid`, `RbacDenied` are reported **back to the model** as a structured tool-result error (`{"error":"…","error_code":"…"}`). The model may apologise and continue without that tool.
  - `ProviderError` from the underlying chat-completions call escalates the whole gateway call to `AiCallOutcome.Failure` (existing behaviour).

### 6.1 `AiProviderRequest` extension

```csharp
public sealed class AiProviderRequest
{
    // Existing fields …
    public IReadOnlyList<AiChatMessage>? Messages { get; init; }   // NEW; if null falls back to SystemPrompt+UserPrompt one-shot
    public IReadOnlyList<AiToolDefinition>? Tools { get; init; }   // NEW; ignored by non-Copilot adapters
    public string? ToolChoice { get; init; }                        // NEW; "auto" | "none"
}

public sealed record AiChatMessage(
    string Role,                       // system | user | assistant | tool
    string? Content,
    IReadOnlyList<AiToolCall>? ToolCalls,
    string? ToolCallId);

public sealed record AiToolCall(string Id, string ToolCode, string ArgsJson);
```

`AiProviderCompletion` gains `IReadOnlyList<AiToolCall>? ToolCalls { get; init; }` and `string? FinishReason { get; init; }`.

### 6.2 Per-adapter behaviour

| Adapter | v1 behaviour |
| --- | --- |
| `CopilotAiModelProvider` (`Azure.AI.Inference`) | Maps `Tools` → `ChatCompletionsOptions.Tools`, returns `ToolCalls` from `completions.ToolCalls`. **Full support.** |
| `RegistryBackedProvider` | Ignores `Tools`; if model returns OpenAI `tool_calls` field, it is passed through `null` for now (deferred to Phase 5.5). |
| `AnthropicProvider` | Ignores `Tools`. Deferred. |
| `MockAiModelProvider` | Ignores `Tools`. Returns deterministic text. |

This keeps the cross-provider regression surface zero in v1.

## 7. Admin surface

### 7.1 Endpoints (under `MapGroup("/v1/admin/ai-tools")`)

| Method + Path | Body | Response | Audit event |
| --- | --- | --- | --- |
| `GET /tools` | — | `AiToolDto[]` | none |
| `GET /grants?featureCode=fc.code` | — | `AiFeatureToolGrantDto[]` | none |
| `POST /grants` | `{ featureCode, toolCode }` | `AiFeatureToolGrantDto` | `AiToolGrantCreated` |
| `PATCH /grants/{id}` | `{ isActive: bool }` | `AiFeatureToolGrantDto` | `AiToolGrantUpdated` |
| `DELETE /grants/{id}` | — | `204` | `AiToolGrantDeleted` |

All endpoints `RequireAuthorization("AdminPermission_ManageAiUsage")` and `.RequireRateLimiting("PerUserWrite")`. All mutations write `AuditEvent` with the existing `SaveWithAuditAsync` helper.

### 7.2 Frontend panel

`components/domain/ai-feature-tool-grants-panel.tsx`:

- Top: `Select` of `KnownFeatureCodes` (options-shape with `{ value, label }`).
- Below: `DataTable` (`keyExtractor=(g)=>g.id`) of grants for that feature with columns `Tool · Category · Active · Updated · Actions`.
- "Add grant" button → modal with `Select` of available `AiTool` rows minus already-granted.
- Toggle / delete actions; on success `Toast({ message: "Grant updated", onClose })`.
- Mounted in `app/admin/ai-providers/page.tsx` BELOW the existing `AiFeatureRoutesPanel`.

3 vitest cases mirroring the feature-routes panel: render-empty, add-grant, toggle-active. `vi.hoisted` for `lib/ai-management-api.ts` mocks.

## 8. Test plan

### Backend (xUnit + SQLite `:memory:`)

- `AiToolRegistryTests` — deny-by-default, cache invalidation on mutation, inactive grants ignored, inactive tools ignored, multi-tenant isolation (UserId scoping).
- `AiToolArgValidatorTests` — accept on valid; reject on missing required, wrong type, extra fields when `additionalProperties: false`.
- `AiToolInvokerTests` — RBAC denied path audits with `RbacDenied`; args-invalid audits with `ArgsInvalid`; budget exceeded audits with `BudgetExceeded`; happy path persists invocation row with hashes.
- `AiGatewayServiceToolLoopTests` — mock provider that returns `tool_calls` once then plain text; assert one usage record + N invocation rows + token sum across turns; assert `PromptNotGroundedException` still fires when system prompt header missing on turn 1.
- Per-tool unit tests — at least 1 success + 1 boundary failure per tool. The `FetchDictionaryDefinitionTool` test stubs `IHttpClientFactory` and asserts blocked-host path on `https://evil.example/`.

### Frontend (Vitest + RTL)

- 3 panel tests as listed in §7.2.

### Gate before deploy

`npx tsc --noEmit` + `npm run lint` + `npm test` (focused: `**/ai-feature-tool-grants-panel*`) + `dotnet test backend/OetLearner.sln --filter "FullyQualifiedName~AiTool|FullyQualifiedName~AiGatewayServiceTool"` all green.

## 9. Migration

`backend/src/OetLearner.Api/Data/Migrations/20260510120000_AddAiToolsAndGrants.cs` adds 3 tables. **MUST** carry `[DbContext(typeof(LearnerDbContext))]` + `[Migration("20260510120000_AddAiToolsAndGrants")]` attributes (lesson from 2026-05-09 prod incident, see `repo:memory/migration-drift-note.md`).

Roll-forward only. No backfills (deny-by-default means an empty grants table is the correct initial state).

## 10. Rollback

Tool calling is gated by the presence of grant rows. Empty `AiFeatureToolGrant` table = no behavioural change vs. Phase 4. Rollback procedure:

```sql
UPDATE "AiFeatureToolGrants" SET "IsActive" = false;
```

This kills tool calling globally without rolling back the schema. Schema rollback is only needed if a P0 logical bug requires reverting `AiGatewayService.CompleteAsync` — in that case `git revert` the feature commit and let migration history retain the (empty) tables.

## 11. Out of scope (deferred to Phase 5.5)

- OpenAI-compatible / Anthropic adapter tool-call mapping.
- Streaming tool calls.
- Per-feature per-user budgets (beyond the global external-network daily budget).
- Tool-call rendering in learner UI.
- A `developer.preview` feature code with all tools granted by default for internal testing.
