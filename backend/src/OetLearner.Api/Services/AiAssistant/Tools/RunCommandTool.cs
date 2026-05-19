using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OetLearner.Api.Domain.AiAssistant;
using OetLearner.Api.Services.AiAssistant.Orchestration;

namespace OetLearner.Api.Services.AiAssistant.Tools;

public sealed class RunCommandTool : IAgentTool
{
    public string Name => "run_command";
    public string Description => "Run a whitelisted shell command in the sandboxed devbox. Requires admin approval.";
    public string JsonSchema => /*lang=json,strict*/ """
    { "type":"object", "properties": { "command": { "type":"string" }, "cwd": { "type":"string" }, "timeoutSec": { "type":"integer" } }, "required":["command"] }
    """;
    public AiToolApprovalPolicy ApprovalPolicy => AiToolApprovalPolicy.RequireAdmin;

    public Task<ToolResult> InvokeAsync(JsonElement args, AgentContext ctx, CancellationToken ct)
    {
        // TODO: via IFileStorage or ICodebaseExecutor (sandboxed)
        // TODO: write AuditEvent
        throw new NotImplementedException("TODO Phase 1: enforce allow-list + timeout + capture stdout/stderr.");
    }
}
