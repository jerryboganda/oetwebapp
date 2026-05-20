# AI Assistant (admin-only agentic chatbot)

Current-status entry point for the admin-only AI Assistant. The shipped V1
surface is an admin chatbot behind `AiAssistant:GlobalEnabled`, routed through
the canonical grounded AI gateway and SignalR hub. It intentionally does not
ship devbox tools, pgvector indexing, provider secret CRUD, shell/git/restart,
or production enablement.

The deeper specifications live in these sibling documents:

- [`AI-ASSISTANT-PLAN.md`](AI-ASSISTANT-PLAN.md) — feature plan, data
  model, provider model, tool catalog, sandboxing, UI, and rollout.
- [`AI-ASSISTANT-THREAT-MODEL.md`](AI-ASSISTANT-THREAT-MODEL.md) —
  STRIDE-style threat model, prompt-injection mitigations, kill switch,
  audit / approval boundaries, secret handling, and abuse controls.

## Status

Phase 1 is shipped locally behind the disabled-by-default kill switch. Chatbot
turns use `IAiGatewayService.BuildGroundedPrompt()` + `CompleteAsync()` with
`RuleKind.Chatbot`, `AiTaskMode.AssistAdminCommand`, and
`AiFeatureCodes.AdminAiChatbot`. Stream frames are delivered over
`/v1/ai-assistant/hub`, and browser clients using `/api/backend` use
long polling through the Next proxy.

The current V1 kill-switch implementation is `IConfiguration` plus an
in-memory current-process admin override. Durable runtime-settings persistence,
global `AuditEvent` integration, and hub-wide `KILL` broadcast are future gates.

Use [AI-ASSISTANT-PROGRESS.md](AI-ASSISTANT-PROGRESS.md) for implementation
evidence and [AI-ASSISTANT-ADMIN-RUNBOOK.md](AI-ASSISTANT-ADMIN-RUNBOOK.md)
for operator behavior.

## Quick Index (created stubs)

- Backend
  - `Domain/AiAssistant/*` — entities + enums.
  - `Contracts/AiAssistant/*` — DTOs + `StreamFrame` tagged union.
  - `Services/AiAssistant/Providers/*` — `ILlmProvider` + 6 provider
    classes (GitHubCopilot, OpenAi, Anthropic, AzureOpenAi,
    GitHubModels, OpenAiCompatible).
  - `Services/AiAssistant/Orchestration/*` — supervisor + planner +
    executor + critic.
  - `Services/AiAssistant/Tools/*` — future tool scaffolding only; not live in
    V1.
  - `Services/AiAssistant/Execution/*` — `ICodebaseExecutor` +
    `DevboxCodebaseExecutor`.
  - `Services/AiAssistant/Indexing/*` — `ICodebaseIndexer` +
    `PgVectorCodebaseIndexer` + `TreeSitterChunker`.
  - `Services/AiAssistant/Permissions/AiAssistantAuthorizationService.cs`.
  - `Endpoints/AiAssistantAdminEndpoints.cs`,
    `Endpoints/AiAssistantChatEndpoints.cs`.
  - `Hubs/AiAssistantHub.cs`.
  - `Data/Migrations/20260519205920_AddAiAssistant.cs` — non-destructive V1
    schema without pgvector.
  - `Data/LearnerDbContext.AiAssistant.cs` — V1 DbSets and mappings.
- Frontend
  - `lib/ai-assistant/{types,client,signalr,permissions}.ts`.
  - `hooks/use-ai-assistant.ts`.
  - `components/domain/ai-assistant/*` — widget, launcher, panel,
    message list, composer, attachments, tool stream, approval modal,
    diff viewer, settings menu, thread sidebar, mount wrapper.
  - `app/admin/ai-assistant/**` — layout + 7 admin pages.

## Hard Constraints (Current V1)

- Admin role and assistant permission only. Widget renders nothing for
  learner/expert/sponsor users.
- Every chatbot LLM call routes through the grounded gateway.
- `admin.ai_chatbot` is hard-denied from canonical AI tool resolution in V1,
  and `/v1/admin/ai-tools/grants` rejects tool grants for that feature code.
- No provider secrets are entered through the assistant provider page in V1.
- No file, shell, git, restart, deploy, or reindex tool is live in V1.
- Assistant mutations write `AiAuditEvent`; global `AuditEvent` integration is
  future-gated for later production tooling phases.
- No existing OET module is bypassed: scoring, rulebooks, content upload,
  result card, reading, grammar, pronunciation, and conversation keep their
  canonical service boundaries.
