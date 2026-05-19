using System;
using System.Threading;
using System.Threading.Tasks;

namespace OetLearner.Api.Services.AiAssistant.Orchestration.SubAgents;

public sealed class PlannerAgent
{
    public PlannerAgent()
    {
        // TODO Phase 1: inject provider + grounded gateway.
    }

    public Task<string> PlanAsync(AgentContext ctx, string userMessage, CancellationToken ct)
    {
        // TODO: route via IAiGatewayService.BuildGroundedPrompt + CompleteAsync
        throw new NotImplementedException("TODO Phase 1: produce ordered plan.");
    }
}
