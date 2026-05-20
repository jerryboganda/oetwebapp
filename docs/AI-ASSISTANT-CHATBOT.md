# AI Assistant (admin-only agentic chatbot)

Scaffolding-only entry point. The real specifications live in two
sibling documents to be authored in the next phase:

- [`AI-ASSISTANT-PLAN.md`](AI-ASSISTANT-PLAN.md) — feature plan, data
  model, provider model, tool catalog, sandboxing, UI, and rollout.
- [`AI-ASSISTANT-THREAT-MODEL.md`](AI-ASSISTANT-THREAT-MODEL.md) —
  STRIDE-style threat model, prompt-injection mitigations, kill switch,
  audit / approval boundaries, secret handling, and abuse controls.

## Status

Phase 0 — scaffolding only. No business logic. No real provider
credentials. No real shell execution. No production deploy.

## Quick Index (created stubs)

- Backend
  - `Domain/AiAssistant/*` — entities + enums.
  - `Contracts/AiAssistant/*` — DTOs + `StreamFrame` tagged union.
  - `Services/AiAssistant/Providers/*` — `ILlmProvider` + 6 provider
    classes (GitHubCopilot, OpenAi, Anthropic, AzureOpenAi,
    GitHubModels, OpenAiCompatible).
  - `Services/AiAssistant/Orchestration/*` — supervisor + planner +
    executor + critic.
  - `Services/AiAssistant/Tools/*` — `IAgentTool` + 8 tools (read /
    write / search / list / run-command / git / reindex / restart).
  - `Services/AiAssistant/Execution/*` — `ICodebaseExecutor` +
    `DevboxCodebaseExecutor`.
  - `Services/AiAssistant/Indexing/*` — `ICodebaseIndexer` +
    `PgVectorCodebaseIndexer` + `TreeSitterChunker`.
  - `Services/AiAssistant/Permissions/AiAssistantAuthorizationService.cs`.
  - `Endpoints/AiAssistantAdminEndpoints.cs`,
    `Endpoints/AiAssistantChatEndpoints.cs`.
  - `Hubs/AiAssistantHub.cs`.
  - `Data/Migrations/20260520000000_AddAiAssistant.cs` (empty Up/Down).
  - `Data/LearnerDbContext.AiAssistant.cs` (partial DbSets — requires
    one-line `partial` keyword edit in `LearnerDbContext.cs`).
  - `Services/AiAssistant/AiAssistantServiceCollectionExtensions.cs`
    (`AddAiAssistant` extension — requires one-line `Program.cs` edit).
- Frontend
  - `lib/ai-assistant/{types,client,signalr,permissions}.ts`.
  - `hooks/use-ai-assistant.ts`.
  - `components/domain/ai-assistant/*` — widget, launcher, panel,
    message list, composer, attachments, tool stream, approval modal,
    diff viewer, settings menu, thread sidebar, mount wrapper.
  - `app/admin/ai-assistant/**` — layout + 7 admin pages.

## Hard Constraints (Phase 0)

- Admin role ONLY. Widget renders nothing for learner/expert.
- Every LLM call comment includes
  `// TODO: route via IAiGatewayService.BuildGroundedPrompt + CompleteAsync`.
- Every secret access includes `// TODO: via IRuntimeSettingsProvider`.
- Every file IO includes
  `// TODO: via IFileStorage or ICodebaseExecutor (sandboxed)`.
- Every mutation includes `// TODO: write AuditEvent`.
- No existing OET module touched (scoring, rulebooks, content upload,
  result card, reading, grammar, pronunciation, conversation).
