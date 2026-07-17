using OetWithDrHesham.Api.Services.Rulebook;

namespace OetWithDrHesham.Api.Tests.Writing;

/// <summary>
/// Deterministic <see cref="IAiGatewayService"/> stub for Writing tests. Every
/// member throws, which makes it a guard: any code path that reaches the AI
/// gateway while it is supposed to be disabled (e.g. the heuristic
/// pre-assessment with <c>Writing:PreAssessmentLlmEnabled=false</c>) fails the
/// test loudly instead of silently making a network call.
/// </summary>
internal sealed class StubAiGateway : IAiGatewayService
{
    private static InvalidOperationException Fail(string member) =>
        new($"StubAiGateway.{member} must not be called (the AI gateway is disabled in these tests).");

    public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        => throw Fail(nameof(CompleteAsync));

    public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
        => throw Fail(nameof(BuildGroundedPrompt));
}
