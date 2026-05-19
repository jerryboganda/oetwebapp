using System;
using System.Threading;
using System.Threading.Tasks;

namespace OetLearner.Api.Services.AiAssistant.Orchestration.SubAgents;

public sealed class ExecutorAgent
{
    public ExecutorAgent()
    {
        // TODO Phase 1: inject tool registry + codebase executor.
    }

    public Task ExecutePlanAsync(AgentContext ctx, string planJson, CancellationToken ct)
    {
        // TODO: route via IAiGatewayService.BuildGroundedPrompt + CompleteAsync (per step)
        // TODO: via IFileStorage or ICodebaseExecutor (sandboxed)
        // TODO: write AuditEvent for each tool invocation.
        throw new NotImplementedException("TODO Phase 1: step executor.");
    }
}
