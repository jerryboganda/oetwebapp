using System;
using System.Threading;
using System.Threading.Tasks;

namespace OetLearner.Api.Services.AiAssistant.Orchestration.SubAgents;

public sealed class CriticAgent
{
    public CriticAgent()
    {
        // TODO Phase 1: inject provider + grounded gateway.
    }

    public Task<string> CritiqueAsync(AgentContext ctx, string draftAnswer, CancellationToken ct)
    {
        // TODO: route via IAiGatewayService.BuildGroundedPrompt + CompleteAsync
        throw new NotImplementedException("TODO Phase 1: adversarial critic pass.");
    }
}
