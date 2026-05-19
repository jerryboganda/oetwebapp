using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OetLearner.Api.Domain.AiAssistant;
using OetLearner.Api.Services.AiAssistant.Orchestration;

namespace OetLearner.Api.Services.AiAssistant.Tools;

public sealed class ReindexCodebaseTool : IAgentTool
{
    public string Name => "reindex_codebase";
    public string Description => "Trigger a full or incremental reindex of the codebase vector store.";
    public string JsonSchema => /*lang=json,strict*/ """
    { "type":"object", "properties": { "full": { "type":"boolean" } } }
    """;
    public AiToolApprovalPolicy ApprovalPolicy => AiToolApprovalPolicy.RequireAdmin;

    public Task<ToolResult> InvokeAsync(JsonElement args, AgentContext ctx, CancellationToken ct)
    {
        // TODO Phase 2: enqueue ICodebaseIndexer job.
        // TODO: write AuditEvent
        throw new NotImplementedException("TODO Phase 2.");
    }
}
