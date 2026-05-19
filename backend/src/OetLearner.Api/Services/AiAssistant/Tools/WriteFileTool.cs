using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OetLearner.Api.Domain.AiAssistant;
using OetLearner.Api.Services.AiAssistant.Orchestration;

namespace OetLearner.Api.Services.AiAssistant.Tools;

public sealed class WriteFileTool : IAgentTool
{
    public string Name => "write_file";
    public string Description => "Write content to a file in the OET repo. Requires admin approval.";
    public string JsonSchema => /*lang=json,strict*/ """
    { "type":"object", "properties": { "path": { "type":"string" }, "content": { "type":"string" }, "expectedContentHash": { "type":"string" } }, "required":["path","content"] }
    """;
    public AiToolApprovalPolicy ApprovalPolicy => AiToolApprovalPolicy.RequireAdmin;

    public Task<ToolResult> InvokeAsync(JsonElement args, AgentContext ctx, CancellationToken ct)
    {
        // TODO: via IFileStorage or ICodebaseExecutor (sandboxed)
        // TODO: write AuditEvent
        throw new NotImplementedException("TODO Phase 1.");
    }
}
