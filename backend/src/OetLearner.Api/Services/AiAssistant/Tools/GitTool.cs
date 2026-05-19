using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OetLearner.Api.Domain.AiAssistant;
using OetLearner.Api.Services.AiAssistant.Orchestration;

namespace OetLearner.Api.Services.AiAssistant.Tools;

public sealed class GitTool : IAgentTool
{
    public string Name => "git";
    public string Description => "Run a whitelisted git subcommand in the sandbox. Requires admin approval for write ops.";
    public string JsonSchema => /*lang=json,strict*/ """
    { "type":"object", "properties": { "subcommand": { "type":"string" }, "args": { "type":"array", "items": { "type":"string" } } }, "required":["subcommand"] }
    """;
    public AiToolApprovalPolicy ApprovalPolicy => AiToolApprovalPolicy.RequireAdmin;

    public Task<ToolResult> InvokeAsync(JsonElement args, AgentContext ctx, CancellationToken ct)
    {
        // TODO: via IFileStorage or ICodebaseExecutor (sandboxed)
        // TODO: write AuditEvent
        throw new NotImplementedException("TODO Phase 1: whitelist {status,diff,log,add,commit,branch,checkout}.");
    }
}
