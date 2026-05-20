# Admin AI Assistant — Phase 0 / 1 Progress

Status: **shipped behind a kill-switch (`AiAssistant:GlobalEnabled = false` by default)**.

## Scope delivered (Phase 0 + 1)

### Backend (`backend/src/OetLearner.Api`)

- New EF Core migration `20260519205920_AddAiAssistant` (8 `CreateTable`, 12 `CreateIndex`, **no `pgvector`** — Phase 2).
- Domain entities under `Domain/AiAssistant/*`: `AiChatThread`, `AiChatMessage`, `AiProviderConfig`, `AiAssistantToolInvocation`, `AiAuditEvent`, `AiUsageLog`, `AiKnowledgeChunk`, `AiAssistantSettings`.
- `Data/LearnerDbContext.AiAssistant.cs` — DbSets + Fluent mappings. `AiAssistantToolInvocations` `ToTable` chosen to avoid collision with canonical `AiToolInvocations`.
- Services under `Services/AiAssistant/**`:
  - `AiAssistantSettingsService` — kill-switch / global enabled from `IConfiguration`, plus current-process in-memory override from the admin kill-switch endpoint. Durable `IRuntimeSettingsProvider` persistence remains deferred.
  - `AiAssistantTurnRegistry` — in-flight turn cancellation.
  - `Permissions/*` — admin permission resolver bound to JWT `perm` claim.
  - `Providers/OpenAiProvider` — legacy SSE-capable provider path retained for compatibility; chatbot turns now dispatch through the canonical gateway.
  - `Orchestration/SupervisorAgent` — kill-switch refusal + grounded `IAiGatewayService` chatbot dispatch.
  - `Execution/*` and `Indexing/*` — placeholder hooks for Phase 2/3.
- New SignalR hub `Hubs/AiAssistantHub.cs` mounted at `/v1/ai-assistant/hub` (NOT `/hubs/...`), policy `AdminAiAssistantUse`.
  - Methods: `Subscribe(threadId)`, `StartTurn(threadId, content) → Guid`, `Cancel(messageId)`. Legacy optional model arguments are ignored; provider/model routing is canonical gateway-only in V1.
  - Events: `ReceiveFrame`, `KillSwitch`. Group key: `"ai-thread:{threadId:N}"`.
  - JWT path whitelist updated in `Program.cs`.
- New endpoints:
  - `Endpoints/AiAssistantChatEndpoints.cs` → `/v1/ai-assistant/threads*`, `/messages/{id}/cancel`. Policy `AdminAiAssistantUse`.
  - `Endpoints/AiAssistantAdminEndpoints.cs` → `/v1/admin/ai-assistant/{settings,kill-switch,threads,audit,usage,providers,indexing}*`. Policy `AdminAiAssistantManage` + rate-limit `PerUserWrite` on the group (satisfies `AdminEndpointAuthorizationInventoryTests`).
- `Program.cs` policies registered: `AdminAiAssistantUse`, `AdminAiAssistantManage`. Names start with `Admin` per granular-policy test.

### Frontend

- `lib/admin-permissions.ts` — added `UseAiAssistant`, `ManageAiAssistant`, `UseAiAssistantUnrestricted` constants and 7 `/admin/ai-assistant/*` entries in `adminRoutePermissionMap`.
- `lib/ai-assistant/{signalr,client,permissions}.ts` — `AiAssistantConnection` wrapper around `@microsoft/signalr@^10`, REST client via `apiClient`, role/perm gate.
- `hooks/use-ai-assistant.ts` — Phase-1 hook. Manages `threads`, `activeThread`, `messages`, `pendingApproval`, `isConnected`, `isStreaming`, `error`, `liveFrames`. Optimistic user message, MessageStart/TokenDelta/MessageEnd frame handlers, auto-create thread on first send, cancel mid-stream.
- UI sub-components (`components/domain/ai-assistant/`):
  - `AiAssistantWidget.tsx` — gated by `useAuth()` (admin role + permission).
  - `AiAssistantWidgetMount.tsx` — wraps widget for layout mount.
  - `AiAssistantLauncher.tsx` — floating launcher button.
  - `ChatMessageList.tsx` — role-coloured cards, auto-scroll, streaming indicator, error banner.
  - `MessageComposer.tsx` — controlled textarea, Enter-to-send, Stop button while streaming.
  - `ThreadSidebar.tsx` — list / `+ New thread` / hover delete.
  - All 13 files updated with `import type { JSX } from 'react'` for TS 5.9 + React 19.
- `app/admin/layout.tsx` — `<AiAssistantWidgetMount />` mounted in admin shell.

## Validation

| Check | Result |
| --- | --- |
| `npx tsc --noEmit` | **EXIT 0** |
| `npm run lint` | 0 errors (2 pre-existing unrelated warnings) |
| `npm test` (Vitest, 207 files / 1287 tests) | **all green** |
| `npm run build` | ✓ Compiled successfully in 85s (169+ pages) |
| `dotnet build backend/.../OetLearner.Api.csproj` | **0 errors, 0 warnings** |
| `dotnet test --filter AdminEndpointAuthorizationInventoryTests` | **11/11 pass** |
| Full `dotnet test backend/OetLearner.sln` | Not used as the final backend signal in this workspace because targeted backend filters are more reliable; see 2026-05-20 validation below. |

### Superseded backend failures

The following failures were seen earlier in the workstream and are no longer the current state. Targeted reruns now pass after focused fixes or setup corrections:

- `OetLearner.Api.Tests.StrategyGuideServiceTests.ListGuides_returns_progress_buckets_and_weak_subtest_recommendations` — `Expected: 2, Actual: 0`.
- `OetLearner.Api.Tests.ReadingAuthoringTests.Extraction_reject_requires_reason_and_caps_length` — `InvalidOperationException: Draft is already Failed.`
- `OetLearner.Api.Tests.ReadingAuthoringTests.Extraction_retains_response_metadata_not_raw_provider_body` — `Expected: Pending, Actual: Failed`.
- `OetLearner.Api.Tests.LearnerSpecRegressionTests.DiagnosticFlow_CompletesAcrossAllSubtests_AndProducesScopedResults` — now passes after seeding speaking transcript evidence.
- `OetLearner.Api.Tests.PronunciationAiGroundingTests.AdminDraft_FallbackWarning_DoesNotExposeRawProviderError` — now passes after deterministic sanitized fallback handling.
- `OetLearner.Api.Tests.AdminFlowsTests.AdminVocabularyImport_RollbackBlocksActiveRowsAndNeverDeletes` — now passes after supplying required publish-gate fields.

Current accepted backend evidence is the focused validation matrix in the 2026-05-20 closure update.

## Current deferred / approval-gated scope

| Phase | Item | Why deferred |
| --- | --- | --- |
| 2 | pgvector + codebase indexing | Requires DBA-approved `CREATE EXTENSION vector`, migration planning, backup awareness, and production rollout approval. |
| 3 | Devbox container + tool execution + approval flow | Requires a separate security design for side-effect classification, per-tool RBAC, argument validation, audit events, and human approval semantics. |
| 4 | Provider CRUD / production provider enablement | Chatbot dispatch now uses the canonical gateway provider registry. Editing provider rows, storing secrets, and flipping production routes remains operator-approved work. |
| Ops | Production enablement | Applying migrations, configuring provider secrets, flipping `AiAssistant:GlobalEnabled`, or running VPS/Docker/database commands remains approval-gated. |

## Non-negotiables preserved

- `oetwebsite_oet_postgres_data` volume untouched.
- EF migration is **non-destructive** and contains **no pgvector column**.
- Ships safely: `AiAssistant:GlobalEnabled = false` by default; Supervisor refuses turns and the hub fires `KillSwitch` if a session is opened.
- Hub path `/v1/ai-assistant/hub` (not `/hubs/ai-assistant`).
- All admin POST/PUT/DELETE routes carry both a granular `Admin*` policy AND the `PerUserWrite` rate-limit binding — verified by `AdminEndpointAuthorizationInventoryTests`.
- SignalR frames are normalized client-side via `normaliseStreamFrame`, including backend snake_case frame discriminators such as `token_delta` and `message_end`.
- Browser SignalR uses long polling when routed through the same-origin `/api/backend` proxy because the Next route proxy is HTTP-only.
- Stream frames carry `threadId`; the React hook filters frames by active thread and unsubscribes from the previous thread group on switch.
- Per-thread provider/model override is disabled in V1. Chatbot turns use the canonical gateway route for `AiFeatureCodes.AdminAiChatbot`.
- Cancel is scoped to the authenticated owner for both REST and hub paths.

## How to enable

1. Apply migration: `dotnet ef database update --project backend/src/OetLearner.Api`.
2. Grant a user the `system_admin` role or `ai_assistant:use` admin permission.
3. Set `AiAssistant:GlobalEnabled = true` in configuration, or via `/v1/admin/ai-assistant/kill-switch` for a current-process override.
4. Configure the canonical gateway provider/feature route for `admin.ai_chatbot` through the main AI provider/runtime settings surfaces.

---

## Closure update - 2026-05-20

### Validation results

| Check | Result |
| --- | --- |
| `cmd /c "npx tsc --noEmit"` | **0 errors** |
| `cmd /c "npm run lint"` | **0 errors, 2 unrelated warnings** (`app/admin/content/import/page.tsx:180` `<a>`/`<Link>`) |
| `cmd /c "npm test -- --run"` | **208/208 files, 1289/1289 tests** (81.72s) |
| `cmd /c "npm run build"` | **Compiled successfully in 85s** (169+ pages) |
| backend API build with `UseAppHost=false` + fresh `bin-verify-aiassistant-closure2` output | **0 warnings, 0 errors** |
| `dotnet test --filter FullyQualifiedName~AiAssistantSupervisorTests` | **3/3 pass** (6.3s) |
| `dotnet test --filter FullyQualifiedName~PronunciationAiGroundingTests` | **15/15 pass** (1s) |
| `dotnet test --filter FullyQualifiedName~AiGatewayRecorderIntegrationTests` | **7/7 pass** (2s) |
| `dotnet test --filter FullyQualifiedName~AiCredentialResolverTests` | **14/14 pass** (2s) |
| `dotnet test --filter FullyQualifiedName~AiQuotaServiceTests` | **27/27 pass** (4s) |
| `dotnet test --filter FullyQualifiedName~AdminVocabularyImport_RollbackBlocksActiveRowsAndNeverDeletes` | **1/1 pass** (15s) |
| `dotnet test --filter FullyQualifiedName~DiagnosticFlow_CompletesAcrossAllSubtests_AndProducesScopedResults` | **1/1 pass** (20s) |
| `dotnet test --filter FullyQualifiedName~ReadingAuthoringTests` | **74/74 pass** (51.4s) |
| `npx vitest run lib/ai-assistant/signalr.test.ts` | **10/10 pass** (1.68s) |
| `npx vitest run hooks/use-ai-assistant.test.tsx contexts/__tests__/ai-assistant-context.test.tsx components/domain/ai-assistant/AiAssistantWidget.test.tsx lib/__tests__/backend-proxy.test.ts lib/__tests__/admin-permissions.test.ts lib/ai-assistant/signalr.test.ts` | **51/51 pass** (3.47s) |
| `dotnet test --filter "FullyQualifiedName~AiGatewayRoutingTests\|FullyQualifiedName~AiAssistantAdminEndpointStubTests"` | **19/19 pass** (23.5s) |

### V1 security and transport closure - 2026-05-20

- AI Assistant SignalR now resolves the hub from `env.apiBaseUrl` and uses `HttpTransportType.LongPolling` for the relative `/api/backend` proxy topology.
- Backend stream frames now include `ThreadId`; frontend stream handling ignores frames for non-active threads and uses the frame thread id when creating streaming placeholders.
- The hook unsubscribes from the previous thread group when switching threads or clearing the active thread.
- Arbitrary per-thread provider/model selection is not persisted or sent to `StartTurn`; V1 uses the canonical gateway feature route only.
- `AiAssistantTurnRegistry` binds cancellation to owner user id, and REST cancel verifies message ownership before cancelling.
- The Next backend proxy exempts the AI Assistant SignalR hub from proxy CSRF for negotiate/polling requests, matching notifications and conversations.
- The Stop action cancels live in-flight assistant turns through the SignalR hub first, then falls back to REST when the hub connection is missing or temporarily rejects during reconnect.
- Current docs describe the V1 kill switch as `IConfiguration` plus current-process in-memory override; durable runtime-settings persistence and hub-wide broadcast remain future gates.
- Widget rendering is covered for admin-with-permission, non-admin, and admin-without-permission cases.
- V1 write-disabled management surfaces are locked by backend tests to remain `501 Not Implemented` for settings PUT, provider CRUD, and indexing reindex until those future phases are explicitly approved.
- V1 no-tools behavior is now enforced in code: `admin.ai_chatbot` bypasses tool resolution in `AiGatewayService`, and the AI tool-grant admin endpoint rejects grants for that feature code.
- The manage dashboard route now requires `ai_assistant:manage`; use-only admins can chat through the widget but cannot load kill-switch/provider management pages.
- The shared AI Assistant React provider stays disabled until an admin has `ai_assistant:use`, preventing hidden hub/thread requests for admins without assistant access.
- The kill-switch response now returns the full settings DTO shape expected by the frontend client.

### Resolved backend regressions

- Pronunciation AdminDraft fallback: gateway/provider failures, unparseable completions, and completions with no valid pronunciation rule IDs now return a deterministic starter template with a sanitized warning. `PromptNotGroundedException` and missing rulebooks still fail closed.
- `AdminVocabularyImport_RollbackBlocksActiveRowsAndNeverDeletes`: activate PUT now supplies required IPA + audio fields so the publish gate remains intact.
- `LearnerSpecRegressionTests.DiagnosticFlow_CompletesAcrossAllSubtests_AndProducesScopedResults`: the diagnostic speaking attempt now seeds transcript evidence before submit, matching the production-readiness test pattern.

### Phase 5 - Admin CMS pages (DONE)

Built 8 admin pages under `/admin/ai-assistant/*`, all read-only V1 with proper write stubs surfaced as toasts where later write-enabled work is needed:

| Route | Status |
| --- | --- |
| `/admin/ai-assistant` | Overview + module navigation |
| `/admin/ai-assistant/audit` | DataTable of `AiAssistantAudit` rows |
| `/admin/ai-assistant/indexing` | Codebase index summary + reindex trigger |
| `/admin/ai-assistant/providers` | Provider/model config read-only (CRUD remains deferred) |
| `/admin/ai-assistant/role-matrix` | Permission matrix from `ADMIN_PERMISSION_DEFINITIONS` |
| `/admin/ai-assistant/test-console` | Free-form prompt to SupervisorAgent round-trip |
| `/admin/ai-assistant/threads` | Thread browser + open transcript |
| `/admin/ai-assistant/layout.tsx` | Shared admin shell wrapper |

All pages: `Badge variant='danger'`, `Button variant='primary'`, `DataTable` uses `getRowKey`, useCallback'd loaders wrapped in `queueMicrotask(() => void load(...))` to satisfy React 19 `react-hooks/set-state-in-effect`.

### Phase 3 (partial) - React-context single-connection (DONE)

- `contexts/ai-assistant-context.tsx` exports `AiAssistantProvider` + `useAiAssistantContext`; one SignalR connection per provider, shared across all child consumers.
- `app/admin/layout.tsx` wraps `{children}` in `<AiAssistantProvider>`.
- 3 consumer panels (chat, indexing-status, audit-feed) migrated from direct `useAiAssistant()` to `useAiAssistantContext()`.
- `contexts/__tests__/ai-assistant-context.test.tsx`: **2/2 pass**.
- Layout regression covered: `app/admin/layout.test.tsx` mocks the context provider and passes 5/5.

### Phase 1.5(b) - Grounded chatbot gateway path (DONE)

- Added `RuleKind.Chatbot` and `AiTaskMode.AssistAdminCommand`.
- `RulebookPromptBuilder` now builds a canonical admin-chatbot safety profile via `BuildGroundedPrompt`; chatbot prompts do not load a fake OET rulebook.
- `SupervisorAgent` now calls `IAiGatewayService.BuildGroundedPrompt()` + `CompleteAsync()` for chatbot turns.
- Chatbot responses still use the existing SignalR frame contract; token streaming is V1-compatible as a single `token_delta` containing the completion. True provider token streaming should be added later inside the gateway, not below it.
- Thread-selected model values are no longer passed as explicit gateway model overrides; chatbot turns use the configured feature route/provider defaults to avoid bypassing provider allow-list and cost controls.
- `AiFeatureCodes.AdminAiChatbot` is platform-only in `AiCredentialResolver`, recognised by `AiFeatureRouteResolver`, and exempt from per-plan feature allow-lists in `AiQuotaService` while still respecting feature disable, global kill-switch, global budget, and per-user admin disable.

### Phase 1.5(c) - Canonical usage logging (DONE)

- Gateway-success and gateway-provider-error paths emit exactly one `AiUsageRecord` through the canonical gateway recorder.
- Pre-gateway refusals (`kill_switch`, `thread_not_found`) emit exactly one `AiUsageRecord` through `IAiUsageRecorder`.
- The AI Assistant admin `/usage` endpoint now reads canonical `AiUsageRecords` filtered to `AiFeatureCodes.AdminAiChatbot`; new chatbot turns no longer write legacy `AiUsageLog` rows.
- Existing `AiUsageLog` table remains in place for compatibility with already-created rows/migrations.

### Phase 4 - Multi-provider routing for chatbot (DONE for gateway dispatch)

- Because chatbot turns now use `IAiGatewayService`, they inherit the existing provider registry and implemented gateway providers, including registry-backed OpenAI-compatible providers, Anthropic, and GitHub Copilot/GitHub Models adapter paths.
- `/v1/admin/ai-assistant/providers` now reports the canonical DB-backed gateway provider registry rather than the legacy in-process chatbot provider registry.
- Remaining Phase 4 work is admin CRUD/credential UX and production provider enablement, which require explicit operator decisions and secrets handling.

### Still deferred / approval-gated

- Phase 2 pgvector codebase indexing: requires DBA-approved `CREATE EXTENSION vector`, migration planning, backup awareness, and production rollout approval for `oetwebsite_oet_postgres_data`.
- Phase 3 devbox container / tool execution / approval flow: requires a separate security design for side-effect classification, per-tool RBAC, argument schema validation, audit events, and human approval semantics.
- Production enablement: applying migrations, configuring provider secrets, flipping `AiAssistant:GlobalEnabled`, or running VPS/Docker/database commands remains operator-approved work only.

_End of current AI Assistant progress record._
