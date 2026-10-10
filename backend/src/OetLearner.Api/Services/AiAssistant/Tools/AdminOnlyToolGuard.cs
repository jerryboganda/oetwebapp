using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.AiAssistant.Tools;

/// <summary>
/// Single gate for the tools that read this application's source tree (owner directive 2026-10-09).
///
/// <para>
/// <b>Why this exists.</b> Source is about to be mounted into the production API container so the
/// admin assistant can genuinely search it. The tool GRANTS already restrict those tools to
/// <c>ai_assistant.admin</c>, but grants are seeded data that a future edit could widen, and the
/// tools themselves were feature-code agnostic: any caller that obtained one could read server
/// source. Production source on a learner-reachable surface would be a disclosure incident, so the
/// restriction is enforced in the tool as well as in the grant table.
/// </para>
///
/// <para>
/// Defence in depth, not a substitute: the grant check still runs, and this runs first because it
/// is cheaper and gives a precise reason.
/// </para>
/// </summary>
internal static class AdminOnlyToolGuard
{
    /// <summary>Feature codes whose users may read the application source tree.</summary>
    private static readonly HashSet<string> AllowedFeatureCodes =
        new([AiFeatureCodes.AiAssistantAdmin], StringComparer.OrdinalIgnoreCase);

    internal static bool IsAllowed(AiToolContext ctx)
        => ctx.IsAdmin || (ctx.FeatureCode is not null && AllowedFeatureCodes.Contains(ctx.FeatureCode));

    /// <summary>
    /// The refusal to return when the caller is not entitled. <see langword="null"/> means allowed.
    /// </summary>
    internal static AiToolExecutionResult? Refusal(AiToolContext ctx, string toolCode)
        => IsAllowed(ctx)
            ? null
            : new AiToolExecutionResult(
                AiToolOutcome.RbacDenied, null, "tool_not_permitted_for_role",
                $"{toolCode} reads this application's source tree and is restricted to the admin "
                + $"assistant. The current feature code is '{ctx.FeatureCode}'.");
}
