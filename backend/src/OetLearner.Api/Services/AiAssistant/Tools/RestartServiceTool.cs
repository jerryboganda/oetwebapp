using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OetLearner.Api.Domain.AiAssistant;
using OetLearner.Api.Services.AiAssistant.Orchestration;

namespace OetLearner.Api.Services.AiAssistant.Tools;

public sealed class RestartServiceTool : IAgentTool
{
    public string Name => "restart_service";
    public string Description => "Restart a named long-running OET service in the dev sandbox.";
    public string JsonSchema => /*lang=json,strict*/ """
    { "type":"object", "properties": { "service": { "type":"string", "enum": ["web","api","worker"] } }, "required":["service"] }
    """;
    public AiToolApprovalPolicy ApprovalPolicy => AiToolApprovalPolicy.RequireAdmin;

    public Task<ToolResult> InvokeAsync(JsonElement args, AgentContext ctx, CancellationToken ct)
    {
        // TODO: via IFileStorage or ICodebaseExecutor (sandboxed)
        // TODO: write AuditEvent
        // TODO Phase 1: enforce sandbox-only — never touch prod containers.
        throw new NotImplementedException("TODO Phase 1.");
    }
}
