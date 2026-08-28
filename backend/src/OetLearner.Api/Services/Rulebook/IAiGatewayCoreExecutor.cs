namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// W2 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01) —
/// the *core* gateway contract: grounding checks, feature-policy gate,
/// provider routing, the bounded tool loop, usage accounting and credit
/// debit. <see cref="AiGatewayService"/> is the one and only implementation.
///
/// <para>
/// This exists purely to fix the dependency direction. Production resolves
/// <see cref="IAiGatewayService"/> to
/// <c>Services/Ai/CoordinatedAiGatewayService.cs</c>, which owns the durable
/// <c>AiOperation</c> lifecycle and then delegates the physical call
/// downwards. If the coordinator injected <see cref="IAiGatewayService"/> it
/// would resolve the facade and recurse forever; injecting
/// <see cref="IAiGatewayCoreExecutor"/> instead makes the layering a DAG that
/// the composition tests assert structurally
/// (<c>AiGatewayCompositionTests</c>).
/// </para>
///
/// <para>
/// The member list is intentionally identical to <see cref="IAiGatewayService"/>
/// so <see cref="AiGatewayService"/> satisfies both with one implementation
/// and stays directly constructible by the existing unit tests.
/// </para>
/// </summary>
public interface IAiGatewayCoreExecutor
{
    Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default);

    AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context);
}
