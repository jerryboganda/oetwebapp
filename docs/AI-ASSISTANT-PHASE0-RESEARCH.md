# Phase 0 Research — Admin Copilot Chat Widget

> Read-only codebase analysis produced by the `agency-researcher` subagent.
> No design, no recommendations — facts only. See `AI-ASSISTANT-PLAN.md` for the design.

## 0. Pre-existing scaffolding (relevant — not yet wired)

A partial "AI Assistant" scaffold already exists under `backend/src/OetLearner.Api/Services/AiAssistant/` and `backend/src/OetLearner.Api/Domain/AiAssistant/`. It is **separate** from the canonical `IAiGatewayService` and is **not yet** registered in DI, DbContext, or endpoints:

- Domain entities: `AiChatThread`, `AiChatMessage`, `AiToolInvocation`, `AiUsageLog`, `AiAuditEvent`, `AiCodebaseChunk`, `AiProviderConfig`, `AiRolePermissionMatrix`, `Enums`.
- Contracts: `ChatThreadDto`, `ChatMessageDto`, `CreateThreadRequest`, `SendMessageRequest`, `StreamFrame`, `ProviderConfigDto`, `ToolInvocationDto`.
- Orchestration: `IAgentOrchestrator` + `SupervisorAgent` with sub-agents `PlannerAgent`, `ExecutorAgent`, `CriticAgent` — every method carries a `// TODO: route via IAiGatewayService.BuildGroundedPrompt + CompleteAsync` comment.
- Providers (parallel to canonical): `OpenAiProvider`, `AnthropicProvider`, `AzureOpenAiProvider`, `GitHubCopilotProvider`, `GitHubModelsProvider`, `OpenAiCompatibleProvider`, implementing a new `ILlmProvider` which is **distinct from** the existing `IAiModelProvider`.
- Tools: `AgentToolRegistry`, `ReadFileTool`, `WriteFileTool`, `ListDirectoryTool`, `SearchCodebaseTool`, `ReindexCodebaseTool`, `RunCommandTool`, `GitTool`, `RestartServiceTool`.

Related design docs already present: `docs/AI-COPILOT-PRD.md`, `docs/AI-COPILOT-TOOLS-PRD.md`, `docs/AI-COPILOT-SDK-INTEGRATION.md`, `docs/AI-COPILOT-PROGRESS.md` — about the gateway-side integration, not the chat widget.

## 1. Existing AI Gateway Surface

**File:** `backend/src/OetLearner.Api/Services/Rulebook/AiGatewayService.cs`

### `IAiGatewayService`

```csharp
public interface IAiGatewayService
{
    Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default);
    AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context);
}
```

### `IAiModelProvider`

```csharp
public interface IAiModelProvider
{
    string Name { get; }
    Task<AiProviderCompletion> CompleteAsync(AiProviderRequest request, CancellationToken ct);
}
```

Concrete implementations: `OpenAiCompatibleProvider`, `CopilotAiModelProvider`, two more registry-backed providers in `AiProviderRegistry.cs`, and `MockAiProvider` (dev/test only — production startup rejects).

### `IAiUsageRecorder`

```csharp
public interface IAiUsageRecorder
{
    Task RecordSuccessAsync(AiUsageContext context, string providerId, string model,
        AiKeySource keySource, AiUsage? usage, int latencyMs, int retryCount,
        string? policyTrace, CancellationToken ct,
        string? accountId = null, string? failoverTrace = null);

    Task RecordFailureAsync(AiUsageContext context, string? providerId, string? model,
        AiKeySource keySource, AiCallOutcome outcome,
        string errorCode, string? errorMessage,
        int latencyMs, int retryCount, string? policyTrace,
        CancellationToken ct, string? accountId = null, string? failoverTrace = null);
}
```

### Enums

- `RuleKind { Writing, Speaking, Grammar, Pronunciation, Vocabulary, Conversation, Listening, Reading }`
- `AiTaskMode { Score, Coach, Correct, Summarise, GenerateFeedback, GenerateContent, GenerateGrammarLesson, ScorePronunciationAttempt, GeneratePronunciationDrill, GeneratePronunciationFeedback, GenerateVocabularyTerm, GenerateVocabularyGloss, GenerateConversationOpening, GenerateConversationReply, EvaluateConversation, GenerateConversationScenario, GenerateListeningStructure, GenerateReadingStructure }`
- `AiKeySource { None, Platform, Byok, PlatformFallback }`
- `AiCallOutcome { Success, ProviderError, GatewayRefused, Cancelled, Timeout, PlatformError }`

There is **no** `AiCallKind` enum. The `Kind` field on grounding context is `RuleKind`.

### `AiFeatureCodes` (full list)

Scoring-critical: `writing.grade`, `writing.sample_score`, `speaking.grade`, `mock.full_grade`. Non-scoring/BYOK-eligible: `writing.coach.suggest`, `writing.coach.explain`, `conversation.reply`, `conversation.opening`, `pronunciation.tip`, `summarise.passage`, `vocabulary.gloss`. Recalls: `recalls.mistake_explain`, `recalls.revision_plan`. Mocks V2: `mock.remediation_draft`. Platform-only pronunciation: `pronunciation.score`, `pronunciation.feedback`. Platform-only conversation eval: `conversation.evaluation`. Admin tooling (platform-only always): `admin.content_generation`, `admin.grammar_draft`, `admin.pronunciation_draft`, `admin.vocabulary_draft`, `admin.conversation_draft`, `admin.listening_draft`, `admin.reading_draft`, `admin.writing_draft`. Listening V2 admin: `admin.listening.skill_tag`, `admin.listening.transcript_segment`. Fallback: `unclassified`.

### Grounding enforcement

`AiGatewayService.CompleteAsync` rejects with `PromptNotGroundedException` when any of:

1. `request.Prompt is null`
2. `string.IsNullOrWhiteSpace(request.Prompt.SystemPrompt)`
3. `!request.Prompt.SystemPrompt.Contains("OET AI — Rulebook-Grounded System Prompt", StringComparison.Ordinal)`

Refusals recorded via `RecordRefusalAsync` → `IAiUsageRecorder.RecordFailureAsync` with `GatewayRefused` outcome and `"ungrounded"` error code.

Phase 5 tool-calling loop (`AiToolDefinition`, `AiToolCall`, `AiChatMessage`) lives inline in `CompleteAsync`. Multi-turn = one parent `AiUsageRecord` + N tool invocation rows.

## 2. SignalR Setup

Hubs registered in `Program.cs`:

```csharp
app.MapHub<NotificationHub>("/v1/notifications/hub").RequireAuthorization();
app.MapHub<ConversationHub>("/v1/conversations/hub").RequireAuthorization();
app.MapHub<MockLiveRoomHub>("/v1/mocks/live-room/hub").RequireAuthorization();
```

| Hub | Auth | Notes |
|---|---|---|
| `NotificationHub` | `[Authorize]` (any role) | Adds connection to group `account:{authAccountId}`. |
| `ConversationHub` | `[Authorize(Policy = "LearnerOnly")]` | Per-session group; scoped services via `IServiceScopeFactory`. |
| `MockLiveRoomHub` | `[Authorize]` (admin/expert/learner in `CanAccess`) | Group `mock-booking:{bookingId}`. |

**Frontend client pattern** (`lib/mocks/live-room-hub.ts`):

```ts
const [{ HubConnectionBuilder, LogLevel }, { ensureFreshAccessToken }] = await Promise.all([
  import('@microsoft/signalr'),
  import('@/lib/auth-client'),
]);

const connection = new HubConnectionBuilder()
  .withUrl(`${process.env.NEXT_PUBLIC_API_BASE_URL || ''}/v1/mocks/live-room/hub`, {
    accessTokenFactory: async () => (await ensureFreshAccessToken()) ?? '',
  })
  .configureLogging(LogLevel.None)
  .withAutomaticReconnect([0, 2_000, 5_000, 10_000])
  .build();
```

## 3. Admin Permission Model

### Backend (`Domain/AuthEntities.cs`)

```csharp
public static class AdminPermissions
{
    public const string ContentRead = "content:read";
    public const string ContentWrite = "content:write";
    public const string ContentPublish = "content:publish";
    public const string ContentEditorReview = "content:editor_review";
    public const string ContentPublisherApproval = "content:publisher_approval";
    public const string BillingRead = "billing:read";
    public const string BillingWrite = "billing:write";              // legacy superset
    public const string BillingRefundWrite = "billing:refund_write";
    public const string BillingCatalogWrite = "billing:catalog_write";
    public const string BillingSubscriptionWrite = "billing:subscription_write";
    public const string UsersRead = "users:read";
    public const string UsersWrite = "users:write";
    public const string ReviewOps = "review_ops";
    public const string QualityAnalytics = "quality_analytics";
    public const string AiConfig = "ai_config";
    public const string FeatureFlags = "feature_flags";
    public const string AuditLogs = "audit_logs";
    public const string SystemAdmin = "system_admin";
    public const string ManagePermissions = "manage_permissions";
    public static readonly string[] All = [ /* all 19 above */ ];
}
```

AGENTS.md says "16 granular admin permissions" — source defines **19** (Billing was expanded by Billing-Hardening I-7).

Frontend mirror: `lib/admin-permissions.ts`.

### Enforcement

Authorization policies declared in `Program.cs` via `RequireAssertion(ctx => HasAdminPermission(ctx, "<perm>", "system_admin"))`. Per-endpoint helper:

```csharp
internal static class AdminRouteBuilderExtensions
{
    public static RouteHandlerBuilder WithAdminWrite(this RouteHandlerBuilder builder, string permission)
        => builder.RequireRateLimiting("PerUserWrite").RequireAuthorization(permission);

    public static RouteHandlerBuilder WithAdminRead(this RouteHandlerBuilder builder, string permission)
        => builder.RequireAuthorization(permission);
}
```

Frontend route gating via `sidebarPermissionMap` / `adminRoutePermissionMap` in `lib/admin-permissions.ts`; `system_admin` is implicit super-permission.

### Adding a new permission

1. Add constant to `AdminPermissions`; append to `All[]`.
2. Mirror in `AdminPermission` const in `lib/admin-permissions.ts`.
3. Add authorization policy in `Program.cs`.
4. Map sidebar/route paths.

## 4. Runtime Settings (`IRuntimeSettingsProvider`)

### Interface

```csharp
public interface IRuntimeSettingsProvider
{
    Task<EffectiveSettings> GetAsync(CancellationToken ct = default);
    Task<RuntimeSettingsRow> GetRawAsync(CancellationToken ct = default);
    void Invalidate();
    string Protect(string plain);
    string? Unprotect(string? cipher);
}
```

### Implementation

- Singleton; `IMemoryCache` key `"runtime-settings:effective:v1"`, TTL **30 s**.
- DB row loaded via short-lived `IServiceScopeFactory` scope from `LearnerDbContext.RuntimeSettings`.
- Secret fields use `IDataProtector` purpose `"RuntimeSettings.Secret.v1"`.
- `Merge(row)` applies "DB non-null wins, else env-baseline" per-field.

Singleton row `Id = "default"`. Cache invalidation via admin PUT handler calling `Invalidate()`. Every write emits `AuditEvent { Action = "RuntimeSettingsUpdated" }` (key names only, never values).

## 5. EF Core DbContext

`backend/src/OetLearner.Api/Data/LearnerDbContext.cs`:

```csharp
public partial class LearnerDbContext(DbContextOptions<LearnerDbContext> options) : DbContext(options)
```

Provider configuration in `Data/DatabaseConfiguration.cs` — `UseNpgsql` for Postgres, `UseSqlite` for tests, `UseInMemoryDatabase` for `InMemory:` prefix. **No** `HasPostgresExtension` calls anywhere in the source tree.

Migrations in `backend/src/OetLearner.Api/Data/Migrations/`. Adding a Postgres extension is done by raw SQL in `Up`/`Down` — no existing example exists. Database init via `Database.Migrate()`.

## 6. Frontend Role Gating

### `CurrentUser` (`lib/types/auth.ts`)

```ts
export interface CurrentUser {
  userId: string;
  email: string;
  role: UserRole;                // 'learner' | 'expert' | 'admin' | 'sponsor'
  displayName: string | null;
  isEmailVerified: boolean;
  isAuthenticatorEnabled: boolean;
  requiresEmailVerification: boolean;
  requiresMfa: boolean;
  emailVerifiedAt: string | null;
  authenticatorEnabledAt: string | null;
  adminPermissions?: string[] | null;
}
```

Auth via `contexts/auth-context.tsx` (`AuthProvider`, `useAuth()`). Middleware (`middleware.ts`) reads `oet_auth` cookie (presence only), redirects unauth → `/sign-in?next=...`, double-submit CSRF for `/api/backend/*`, per-request CSP nonce.

## 7. Existing Admin Pages Pattern

Admin pages live under `app/admin/` (one folder per route). All admin pages render inside `app/admin/layout.tsx` which uses `AdminDashboardShell`. Sidebar `NavGroup[]` hard-coded in the layout (`adminNavGroups`).

Adding to sidebar: append `NavItem` to `adminNavGroups`, then add href → required-permission entry in `sidebarPermissionMap` and `adminRoutePermissionMap`.

Almost all admin pages are `'use client'` and call `apiClient.request` / `.get` / `.post` from `lib/api.ts`.

## 8. File Storage

```csharp
public interface IFileStorage
{
    Task<long> WriteAsync(string key, Stream source, CancellationToken ct);
    Task<Stream> OpenReadAsync(string key, CancellationToken ct);
    Task<Stream> OpenWriteAsync(string key, CancellationToken ct);
    bool Exists(string key);
    bool Delete(string key);
    long Length(string key);
    void Move(string sourceKey, string destKey, bool overwrite);
    int DeletePrefix(string prefix);
    string? TryResolveLocalPath(string key);
}
```

`LocalFileStorage` impl. **Sandboxed.** All keys POSIX-style under `StorageOptions.LocalRootPath`. `ResolvePath` rejects: empty keys, segments `.`/`..`, segments with `:`, any path resolving outside the configured root. **Cannot write to arbitrary `/opt/oetwebapp/` paths** — only within the configured storage root.

## 9. Audit Logging

```csharp
public class AuditEvent
{
    [Key, MaxLength(64)] public string Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    [MaxLength(64)] public string ActorId { get; set; }
    [MaxLength(64)] public string? ActorAuthAccountId { get; set; }
    [MaxLength(128)] public string ActorName { get; set; }
    [MaxLength(128)] public string Action { get; set; }
    [MaxLength(64)] public string ResourceType { get; set; }
    [MaxLength(64)] public string? ResourceId { get; set; }
    public string? Details { get; set; }
    public ApplicationUserAccount? ActorAuthAccount { get; set; }
}
```

Write pattern (20+ services):

```csharp
db.AuditEvents.Add(new AuditEvent {
    Id = Guid.NewGuid().ToString("N"),
    OccurredAt = DateTimeOffset.UtcNow,
    ActorId = userId,
    ActorAuthAccountId = authAccountId,
    ActorName = displayName,
    Action = "RuntimeSettingsUpdated",
    ResourceType = "RuntimeSettings",
    ResourceId = "default",
    Details = JsonSerializer.Serialize(new { keys = changedKeys }),
});
await db.SaveChangesAsync(ct);
```

## 10. Mission-critical Surfaces the Chatbot MUST NOT Break

Per AGENTS.md Common Gotchas:

- **OET Scoring** — route only through `lib/scoring.ts` / `OetScoring`; anchors `30/42 ≡ 350/500`; country-aware Writing.
- **OET Rulebooks** — `lib/rulebook` / `Services.Rulebook`; never read `rulebooks/**/rulebook.v*.json` directly.
- **AI calls** — always via grounded gateway; one `AiUsageRecord` per call.
- **Content uploads** — `ContentPaper` → `ContentPaperAsset` → `MediaAsset`; all I/O through `IFileStorage`.
- **Statement of Results** — `components/domain/OetStatementOfResultsCard.tsx` pixel-faithful; use adapter.
- **Reading Authoring** — exact-match grading; canonical 20+6+16=42 shape; learner endpoints never serialise correct-answer JSON.
- **Grammar / Pronunciation / Conversation** — server-authoritative; rulebook-grounded; advisory band projections via `OetScoring`.
- **Runtime Settings** — all secrets via `IRuntimeSettingsProvider`; only `system_admin` may write.

Deployment invariants: `oetwebsite_` Docker volume prefix; never recreate `oetwebsite_oet_postgres_data` without backup; exact-SHA prod deploys with signed `release-evidence-<sha>`.

**End of facts.**
