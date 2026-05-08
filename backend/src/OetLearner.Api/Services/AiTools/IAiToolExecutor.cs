using System.Text.Json;

namespace OetLearner.Api.Services.AiTools;

/// <summary>
/// Phase 5 — Tool calling. One implementation per <c>AiTool</c> row in the
/// catalog. Implementations are scoped DI services so they can pull in
/// <c>LearnerDbContext</c>, <c>IHttpClientFactory</c>, etc.
///
/// The registry resolves <c>IAiToolExecutor.Code</c> to an instance; the
/// invoker calls <see cref="ExecuteAsync"/> after RBAC + arg validation.
/// </summary>
public interface IAiToolExecutor
{
    /// <summary>Stable code matching <c>AiTool.Code</c>.</summary>
    string Code { get; }

    OetLearner.Api.Domain.AiToolCategory Category { get; }

    /// <summary>JSON Schema 2020-12 string. Loaded once; static per process.</summary>
    string JsonSchemaArgs { get; }

    Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct);
}

/// <summary>
/// Per-call context handed to the tool executor. The invoker fills this
/// from the gateway's request context; tools must NEVER reach back into
/// HTTP / claims principals — only fields here are available.
/// </summary>
public sealed record AiToolContext(
    string FeatureCode,
    string? UserId,
    string? AuthAccountId,
    string AiUsageRecordId,
    int TurnIndex);

public sealed record AiToolExecutionResult(
    OetLearner.Api.Domain.AiToolOutcome Outcome,
    JsonElement? ResultJson,
    string? ErrorCode = null,
    string? ErrorMessage = null);

/// <summary>
/// Definition of a tool that the gateway exposes to the model. Includes
/// only what the OpenAI <c>tools</c> array needs.
/// </summary>
public sealed record AiToolDefinition(
    string Code,
    string Name,
    string Description,
    OetLearner.Api.Domain.AiToolCategory Category,
    string JsonSchemaArgs);
