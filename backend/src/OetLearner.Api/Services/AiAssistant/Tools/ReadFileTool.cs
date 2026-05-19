using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OetLearner.Api.Domain.AiAssistant;
using OetLearner.Api.Services.AiAssistant.Orchestration;

namespace OetLearner.Api.Services.AiAssistant.Tools;

public sealed class ReadFileTool : IAgentTool
{
    public string Name => "read_file";
    public string Description => "Read the contents of a file in the OET repo (path is repo-relative).";
    public string JsonSchema => /*lang=json,strict*/ """
    { "type":"object", "properties": { "path": { "type":"string" }, "startLine": { "type":"integer" }, "endLine": { "type":"integer" } }, "required":["path"] }
    """;
    public AiToolApprovalPolicy ApprovalPolicy => AiToolApprovalPolicy.Auto;

    public Task<ToolResult> InvokeAsync(JsonElement args, AgentContext ctx, CancellationToken ct)
    {
        // TODO: via IFileStorage or ICodebaseExecutor (sandboxed)
        throw new NotImplementedException("TODO Phase 1.");
    }
}
