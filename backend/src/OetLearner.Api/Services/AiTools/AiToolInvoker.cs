using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.AiTools;

/// <summary>
/// Executes a tool call with RBAC + arg validation + external-network
/// budget + audit. Always writes an <see cref="AiToolInvocation"/> row,
/// regardless of outcome. Never throws to the caller — failures are
/// surfaced via <see cref="AiToolExecutionResult.Outcome"/> so the
/// gateway can feed the error back to the model as a tool result.
/// </summary>
public interface IAiToolInvoker
{
    Task<AiToolExecutionResult> InvokeAsync(
        string toolCode,
        JsonElement argsJson,
        AiToolContext ctx,
        CancellationToken ct);

    Task<AiToolExecutionResult> InvokeAsync(
        OetLearner.Api.Services.Rulebook.AiToolCall call,
        AiToolContext ctx,
        CancellationToken ct);
}

public sealed class AiToolInvoker : IAiToolInvoker
{
    private readonly IAiToolRegistry _registry;
    private readonly IServiceProvider _sp;
    private readonly LearnerDbContext _db;
    private readonly IOptionsMonitor<AiToolOptions> _options;
    private readonly ILogger<AiToolInvoker> _logger;
    private readonly IReadOnlyDictionary<string, IAiToolExecutor> _executors;

    public AiToolInvoker(
        IAiToolRegistry registry,
        IServiceProvider sp,
        LearnerDbContext db,
        IOptionsMonitor<AiToolOptions> options,
        ILogger<AiToolInvoker> logger,
        IEnumerable<IAiToolExecutor> executors)
    {
        _registry = registry;
        _sp = sp;
        _db = db;
        _options = options;
        _logger = logger;
        _executors = executors.ToDictionary(e => e.Code, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<AiToolExecutionResult> InvokeAsync(
        OetLearner.Api.Services.Rulebook.AiToolCall call,
        AiToolContext ctx,
        CancellationToken ct)
    {
        JsonElement argsJson;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.ArgsJson) ? "{}" : call.ArgsJson);
            argsJson = doc.RootElement.Clone();
        }
        catch (JsonException jex)
        {
            // Persist + return ArgsInvalid for unparsable JSON.
            using var emptyDoc = JsonDocument.Parse("{}");
            await PersistAsync(ctx, call.ToolCode, AiToolCategory.Read, AiToolOutcome.ArgsInvalid,
                emptyDoc.RootElement.Clone(), resultJson: null,
                errorCode: "args_unparsable", errorMessage: jex.Message,
                Stopwatch.StartNew(), ct);
            return new AiToolExecutionResult(AiToolOutcome.ArgsInvalid, null, "args_unparsable", jex.Message);
        }
        return await InvokeAsync(call.ToolCode, argsJson, ctx, ct);
    }

    public async Task<AiToolExecutionResult> InvokeAsync(
        string toolCode,
        JsonElement argsJson,
        AiToolContext ctx,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        // 1. RBAC check.
        var allowed = await _registry.ResolveForFeatureAsync(ctx.FeatureCode, ct);
        var def = allowed.FirstOrDefault(d => string.Equals(d.Code, toolCode, StringComparison.OrdinalIgnoreCase));
        if (def is null)
        {
            await PersistAsync(ctx, toolCode, AiToolCategory.Read, AiToolOutcome.RbacDenied,
                argsJson, resultJson: null, errorCode: "rbac_denied",
                errorMessage: $"feature '{ctx.FeatureCode}' is not granted tool '{toolCode}'", sw, ct);
            return new AiToolExecutionResult(AiToolOutcome.RbacDenied, null, "rbac_denied",
                $"feature '{ctx.FeatureCode}' is not granted tool '{toolCode}'");
        }

        // 2. Schema validation.
        var validation = AiToolArgValidator.Validate(argsJson, def.JsonSchemaArgs);
        if (!validation.Ok)
        {
            await PersistAsync(ctx, toolCode, def.Category, AiToolOutcome.ArgsInvalid,
                argsJson, resultJson: null, errorCode: validation.ErrorCode,
                errorMessage: validation.Message, sw, ct);
            return new AiToolExecutionResult(AiToolOutcome.ArgsInvalid, null, validation.ErrorCode, validation.Message);
        }

        // 3. External-network budget gate.
        if (def.Category == AiToolCategory.ExternalNetwork)
        {
            var opts = _options.CurrentValue;
            if (opts.ExternalNetworkPerUserDailyCalls > 0 && !string.IsNullOrEmpty(ctx.UserId))
            {
                var since = DateTimeOffset.UtcNow.Date;
                var used = await _db.AiToolInvocations
                    .CountAsync(i => i.UserId == ctx.UserId
                                     && i.Category == AiToolCategory.ExternalNetwork
                                     && i.Outcome == AiToolOutcome.Success
                                     && i.CreatedAt >= since, ct);
                if (used >= opts.ExternalNetworkPerUserDailyCalls)
                {
                    var msg = $"daily external-network budget exhausted ({opts.ExternalNetworkPerUserDailyCalls})";
                    await PersistAsync(ctx, toolCode, def.Category, AiToolOutcome.BudgetExceeded,
                        argsJson, resultJson: null, errorCode: "external_budget",
                        errorMessage: msg, sw, ct);
                    return new AiToolExecutionResult(AiToolOutcome.BudgetExceeded, null, "external_budget", msg);
                }
            }
        }

        // 4. Dispatch.
        if (!_executors.TryGetValue(toolCode, out var executor))
        {
            await PersistAsync(ctx, toolCode, def.Category, AiToolOutcome.ExecutionError,
                argsJson, resultJson: null, errorCode: "executor_missing",
                errorMessage: "no IAiToolExecutor registered", sw, ct);
            return new AiToolExecutionResult(AiToolOutcome.ExecutionError, null, "executor_missing",
                "no IAiToolExecutor registered");
        }

        try
        {
            var result = await executor.ExecuteAsync(argsJson, ctx, ct);
            await PersistAsync(ctx, toolCode, def.Category, result.Outcome,
                argsJson, result.ResultJson, result.ErrorCode, result.ErrorMessage, sw, ct);
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AiToolInvoker: executor threw for tool {Tool} feature {Feature}", toolCode, ctx.FeatureCode);
            var msg = TruncateMessage(ex.Message);
            await PersistAsync(ctx, toolCode, def.Category, AiToolOutcome.ExecutionError,
                argsJson, resultJson: null, errorCode: "execution_error",
                errorMessage: msg, sw, ct);
            return new AiToolExecutionResult(AiToolOutcome.ExecutionError, null, "execution_error", msg);
        }
    }

    private async Task PersistAsync(
        AiToolContext ctx,
        string toolCode,
        AiToolCategory category,
        AiToolOutcome outcome,
        JsonElement argsJson,
        JsonElement? resultJson,
        string? errorCode,
        string? errorMessage,
        Stopwatch sw,
        CancellationToken ct)
    {
        sw.Stop();
        var row = new AiToolInvocation
        {
            Id = Guid.NewGuid().ToString("N"),
            AiUsageRecordId = ctx.AiUsageRecordId,
            FeatureCode = ctx.FeatureCode,
            ToolCode = toolCode,
            Category = category,
            UserId = ctx.UserId,
            TurnIndex = ctx.TurnIndex,
            ArgsHash = HashJson(argsJson),
            ResultHash = resultJson is { } r ? HashJson(r) : "",
            Outcome = outcome,
            ErrorCode = TruncateCode(errorCode),
            ErrorMessage = TruncateMessage(errorMessage),
            LatencyMs = (int)Math.Min(int.MaxValue, sw.ElapsedMilliseconds),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.AiToolInvocations.Add(row);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AiToolInvoker: failed to persist invocation row for tool {Tool}", toolCode);
        }
    }

    private static string HashJson(JsonElement json)
    {
        var raw = json.ValueKind == JsonValueKind.Undefined ? "" : json.GetRawText();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string? TruncateCode(string? s) =>
        string.IsNullOrEmpty(s) ? s : s.Length <= 64 ? s : s[..64];

    private static string? TruncateMessage(string? s) =>
        string.IsNullOrEmpty(s) ? s : s.Length <= 512 ? s : s[..512];
}
