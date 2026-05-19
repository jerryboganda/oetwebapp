using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OetLearner.Api.Domain.AiAssistant;
using OetLearner.Api.Services.AiAssistant.Orchestration;

namespace OetLearner.Api.Services.AiAssistant.Tools;

public sealed class SearchCodebaseTool : IAgentTool
{
    public string Name => "search_codebase";
    public string Description => "Semantic + grep search over the indexed OET codebase.";
    public string JsonSchema => /*lang=json,strict*/ """
    { "type":"object", "properties": { "query": { "type":"string" }, "topK": { "type":"integer" } }, "required":["query"] }
    """;
    public AiToolApprovalPolicy ApprovalPolicy => AiToolApprovalPolicy.Auto;

    public Task<ToolResult> InvokeAsync(JsonElement args, AgentContext ctx, CancellationToken ct)
    {
        // TODO Phase 2: ICodebaseIndexer hybrid search.
        throw new NotImplementedException("TODO Phase 2.");
    }
}
