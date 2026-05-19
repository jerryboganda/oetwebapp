using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OetLearner.Api.Domain.AiAssistant;
using OetLearner.Api.Services.AiAssistant.Orchestration;

namespace OetLearner.Api.Services.AiAssistant.Tools;

public sealed class ToolResult
{
    public bool Success { get; init; }
    public string? ResultJson { get; init; }
    public string? ErrorMessage { get; init; }
}

public interface IAgentTool
{
    string Name { get; }
    string Description { get; }

    // JSON schema (OpenAI function-calling style) used by the orchestrator
    // when constructing the tool list for the provider.
    string JsonSchema { get; }

    AiToolApprovalPolicy ApprovalPolicy { get; }

    // TODO: write AuditEvent for every invocation (success + failure).
    Task<ToolResult> InvokeAsync(JsonElement args, AgentContext ctx, CancellationToken ct);
}
