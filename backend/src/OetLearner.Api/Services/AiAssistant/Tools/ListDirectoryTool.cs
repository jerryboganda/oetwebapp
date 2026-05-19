using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OetLearner.Api.Domain.AiAssistant;
using OetLearner.Api.Services.AiAssistant.Orchestration;

namespace OetLearner.Api.Services.AiAssistant.Tools;

public sealed class ListDirectoryTool : IAgentTool
{
    public string Name => "list_directory";
    public string Description => "List files/folders in a repo-relative directory.";
    public string JsonSchema => /*lang=json,strict*/ """
    { "type":"object", "properties": { "path": { "type":"string" } }, "required":["path"] }
    """;
    public AiToolApprovalPolicy ApprovalPolicy => AiToolApprovalPolicy.Auto;

    public Task<ToolResult> InvokeAsync(JsonElement args, AgentContext ctx, CancellationToken ct)
    {
        // TODO: via IFileStorage or ICodebaseExecutor (sandboxed)
        throw new NotImplementedException("TODO Phase 1.");
    }
}
