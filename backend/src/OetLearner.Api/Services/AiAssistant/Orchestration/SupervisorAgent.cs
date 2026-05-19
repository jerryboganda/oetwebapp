using System;
using System.Threading;
using System.Threading.Tasks;

namespace OetLearner.Api.Services.AiAssistant.Orchestration;

// Top-level orchestrator that decomposes a turn into planner -> executor -> critic loops.
public sealed class SupervisorAgent : IAgentOrchestrator
{
    public SupervisorAgent()
    {
        // TODO Phase 1: inject planner, executor, critic, provider registry, tool registry.
    }

    public Task RunTurnAsync(AgentContext ctx, string userMessage, CancellationToken ct)
    {
        // TODO: route via IAiGatewayService.BuildGroundedPrompt + CompleteAsync
        // TODO: write AuditEvent
        throw new NotImplementedException("TODO Phase 1: supervisor loop.");
    }
}
