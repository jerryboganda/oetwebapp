# SESSION STATE

Session: admin-chat-long-tasks
Goal: Repair failed admin sends and multi-tool long tasks, retain short-task behavior and safe interruption
Mode: execute
Updated: 2026-10-08
Branch: main
HEAD: 6ce8f33f9

## Objective

Production admin hub requests returned 429 during polling and negotiation. Fix the transport limiter and harden connection refresh, turn serialization, durable tool progress and interrupted history.

## Acceptance criteria

- [x] Assistant polling has separate bounded 600/min transport allowance; negotiation remains capped.
- [x] Token refresh preserves connection; account/role changes invalidate it; events filtered by conversation.
- [x] One running turn per conversation; tool intent and each result saved immediately; no automatic resend.
- [x] Interrupted tool history normalized; orphan/duplicate results omitted; tool context and provider stream reads bounded.
- [ ] Image build and production serving proof recorded.
- [ ] Short/long tool tasks, cancel, token refresh and interruption functional acceptance: not tested—owner QA.

## Decisions (do not revisit)

- Production evidence: admin long-poll GET and reconnect negotiate requests returned 429 at 22:29 UTC Oct 7. MapHub used shared HubConnect 30/min for every poll.
- Retain existing role permissions, provider routing, reasoning metadata, quotas, guard/approval and iteration limits.
- No automatic task resubmission or tool retry after interruption. Unknown tool outcomes explicitly require inspection/confirmation.
- No automated QA. Compile through image builds and record serving/health evidence; owner functional QA remains separate.

## Touched files

| Path | Change |
| --- | --- |
| backend/src/OetLearner.Api/Program.cs | Separate bounded assistant transport rate bucket |
| backend/src/OetLearner.Api/Services/AiAssistant/AiAssistantOrchestrator.cs | Single turn, durable tool progress and complete history |
| backend/src/OetLearner.Api/Services/Ai/OpenCodeStreamingCall.cs | Async reads and stream deadline |
| hooks/use-ai-assistant.ts, contexts/ai-assistant-context.tsx, lib/ai-assistant/signalr.ts | Stable account-scoped connection, scoped events and honest interruption |

## Verification gates

| Gate | Command / workflow | Evidence | Result |
| --- | --- | --- | --- |
| compilation | build-images.yml | Awaiting release | NOT RUN |
| deployment | production-deploy.yml | Awaiting release | NOT RUN |
| functional acceptance | Owner manual QA | Short/long/cancel/reconnect/history scenarios | NOT TESTED |

## Blockers

- Hindsight service refused connection; diagnosis uses current source and production evidence.

## Next action

1. Ship scoped changes through existing wrapper; repair compilation if necessary and verify exact serving release.
