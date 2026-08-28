using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Ai;

/// <summary>
/// Kill switch for the W2 coordinated-gateway path. Fail-safe and
/// DEFAULT-ENABLED: absent configuration means coordination is ON, which is
/// the behaviour this release ships. Setting <c>Ai:Coordination:Enabled</c> to
/// false degrades to the pre-W2 direct-core path — the operations escape hatch
/// if the control plane misbehaves in production — and the core gateway still
/// records one usage row per physical provider turn either way.
/// </summary>
public sealed class AiExecutionCoordinationOptions
{
    public const string SectionName = "Ai:Coordination";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How long a COMPLETED operation keeps absorbing identical requests as
    /// duplicates. Inside the window a replay returns the existing operation
    /// and never calls a provider (the double-submit / retry-storm guard);
    /// outside it, the same canonical action is treated as legitimate new work
    /// and gets a fresh operation via the replay discriminator. Bounded by
    /// <see cref="AiOperationReplayPolicy.MaxReplayWindow"/> so a mis-set value
    /// cannot recreate the permanent-lockout defect.
    /// </summary>
    public int ReplayWindowSeconds { get; set; } = (int)AiOperationReplayPolicy.DefaultReplayWindow.TotalSeconds;

    /// <summary>Maximum replay-discriminator bumps attempted before the caller
    /// is told the truth about the last predecessor. Bounded by
    /// <see cref="AiOperationReplayPolicy.MaxReplayRoundsCeiling"/>.</summary>
    public int MaxReplayRounds { get; set; } = AiOperationReplayPolicy.DefaultMaxReplayRounds;
}

/// <summary>
/// W2 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01) —
/// the production <see cref="IAiGatewayService"/>.
///
/// <para>
/// <b>Why a facade.</b> Every existing caller injects
/// <see cref="IAiGatewayService"/>. Rather than editing ~40 call sites (and
/// their tests) to learn about operations, production DI resolves that
/// interface to this decorator, which opens/closes a durable
/// <see cref="Domain.AiOperation"/> around the real call and then delegates to
/// <see cref="IAiGatewayCoreExecutor"/> — implemented by the unchanged
/// <see cref="AiGatewayService"/>. Caller signatures are untouched.
/// </para>
///
/// <para>
/// <b>Recursion fence.</b> <see cref="AiGatewayRequest.OperationId"/> is the
/// permanent, structural guard: the coordinator stamps it before invoking the
/// core, so any request that re-enters this facade already carrying one is
/// dispatched straight to the core. Combined with the coordinator injecting
/// <see cref="IAiGatewayCoreExecutor"/> (never <see cref="IAiGatewayService"/>),
/// there is no cycle even if a future caller re-wraps the facade.
/// </para>
///
/// <para>
/// <b>Truthfulness.</b> When this instance owns the operation the caller gets
/// the real gateway result. When the operation is a duplicate that already
/// reached a terminal state, W2 cannot reconstruct the original completion
/// body from the control plane (only a <c>ResultRef</c> pointer is stored), so
/// the caller gets
/// <see cref="AiOperationDuplicateResultUnavailableException"/> rather than a
/// fabricated success — and, critically, no second provider charge.
/// </para>
/// </summary>
public sealed class CoordinatedAiGatewayService(
    IAiGatewayCoreExecutor core,
    IAiExecutionCoordinator coordinator,
    IOptions<AiExecutionCoordinationOptions>? options = null,
    ILogger<CoordinatedAiGatewayService>? logger = null,
    IOptions<OetLearner.Api.Services.AiTools.AiToolOptions>? toolOptions = null) : IAiGatewayService
{
    private readonly bool _enabled = options?.Value?.Enabled ?? true;

    /// <summary>
    /// Truthful upper bound on the number of PHYSICAL provider turns one
    /// coordinated operation can make. The gateway's tool loop runs up to
    /// <see cref="OetLearner.Api.Services.AiTools.AiToolOptions.MaxToolCallsPerCompletion"/>
    /// turns for a single logical call, and each turn is a real, separately
    /// billed invocation with its own usage row. Stamping 1 here would tell W4
    /// (and any operator reading the control plane) that a 4-turn tool loop had
    /// overrun its attempt budget when it had not.
    /// </summary>
    private readonly int _attemptLimit = Math.Max(
        1,
        toolOptions?.Value?.MaxToolCallsPerCompletion ?? new OetLearner.Api.Services.AiTools.AiToolOptions().MaxToolCallsPerCompletion);

    public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
        => core.BuildGroundedPrompt(context);

    public async Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Recursion fence + kill switch. Both land on the identical core call,
        // which still writes one usage row per physical provider turn.
        if (!_enabled || !string.IsNullOrWhiteSpace(request.OperationId))
        {
            return await core.CompleteAsync(request, ct);
        }

        var operationRequest = new AiOperationRequest
        {
            RequestHash = BuildRequestHash(request),
            ResourceId = request.ResourceId,
            ResourceType = request.ResourceType,
            ResourceVersion = request.ResourceVersion,
            PromptVersion = request.PromptTemplateId,
            RulebookVersion = request.Prompt?.Metadata.RulebookVersion,
            ModelRoute = BuildModelRoute(request),
            // Physical provider turns this one operation may legitimately make
            // (the bounded tool loop), NOT a retry budget — W4 owns retries.
            AttemptLimit = _attemptLimit,
            GatewayRequest = request,
        };

        var outcome = await coordinator.ExecuteAsync(operationRequest, ct);

        if (outcome.GatewayResult is not null) return outcome.GatewayResult;

        logger?.LogWarning(
            "AI request for feature {FeatureCode} resolved to existing operation {OperationId} in state {State}; no second provider call was made.",
            request.FeatureCode, outcome.Operation.Id, outcome.State);

        throw new AiOperationDuplicateResultUnavailableException(
            outcome.Operation.Id, outcome.State, outcome.ResultRef);
    }

    private static string BuildModelRoute(AiGatewayRequest request)
    {
        var provider = string.IsNullOrWhiteSpace(request.Provider) ? "auto" : request.Provider.Trim().ToLowerInvariant();
        var model = string.IsNullOrWhiteSpace(request.Model) ? "auto" : request.Model.Trim().ToLowerInvariant();
        return $"{provider}:{model}";
    }

    /// <summary>
    /// Deterministic fingerprint of the grounded request. Every field that can
    /// change what the provider is asked to do participates, so two calls share
    /// a hash only when they really are the same action.
    ///
    /// <para>
    /// The raw prompt is NEVER persisted: the system prompt, user input and
    /// task instruction are folded in as SHA-256 digests, and the returned
    /// value is itself a digest. Learner content therefore never reaches the
    /// control plane, but a byte-level change still produces a different hash.
    /// </para>
    /// </summary>
    internal static string BuildRequestHash(AiGatewayRequest request)
    {
        var canonical = new StringBuilder()
            .Append(Norm(request.FeatureCode)).Append('|')
            .Append(Norm(request.UserId)).Append('|')
            .Append(Norm(request.TenantId)).Append('|')
            .Append(Norm(request.Provider)).Append('|')
            .Append(Norm(request.Model)).Append('|')
            .Append(request.Temperature.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)).Append('|')
            .Append(request.MaxTokens?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty).Append('|')
            .Append(request.AssessmentContext).Append('|')
            .Append(Norm(request.PromptTemplateId)).Append('|')
            .Append(Norm(request.Prompt?.Metadata.RulebookVersion)).Append('|')
            .Append(request.Prompt?.Metadata.AppliedRulesCount.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty).Append('|')
            .Append(Norm(request.ResourceId)).Append('|')
            .Append(Norm(request.ResourceType)).Append('|')
            .Append(request.ResourceVersion?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty).Append('|')
            .Append(Digest(request.Prompt?.SystemPrompt)).Append('|')
            .Append(Digest(request.Prompt?.TaskInstruction)).Append('|')
            .Append(Digest(request.UserInput)).Append('|')
            .Append(request.AudioAttachments?.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0")
            .ToString();

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string Norm(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    private static string Digest(string? value)
        => string.IsNullOrEmpty(value)
            ? string.Empty
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
