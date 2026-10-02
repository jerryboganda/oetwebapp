namespace OetLearner.Api.Services.Writing;

/// <summary>
/// Chooses the route a Writing grading run starts on. Owner hard rule MAX-ALWAYS-ON (2 Oct 2026):
/// every run — first grade, automatic re-queue or manual Retry — starts on the Claude Max
/// subscription. No persisted or computed state (a quota marker, a utilisation estimate, a provider
/// mode, a readiness probe) may switch Max off or skip it. Failover to the Anthropic API and then
/// Codex happens only inside the same run, after Max actually returned an error
/// (<see cref="WritingGradeChain"/>), and nothing carries over to the next run.
///
/// The interface stays so a test host can route grading to its own stub provider.
/// </summary>
public interface IWritingSubscriptionSelector
{
    Task<WritingSubscriptionDecision> DecideAsync(CancellationToken ct);
}

public sealed record WritingSubscriptionDecision(
    string ProviderCode,
    string Model,
    string Reason,
    double? UtilizationPct,
    bool IsFallback);

public static class WritingSubscriptionProviders
{
    // Level 1 — dedicated Claude Max 5x subscription (sidecar over the claude CLI).
    public const string Claude = "writing-claude-sub";
    // Level 2 — Claude API (pay-as-you-go Anthropic key on the `anthropic` row).
    public const string ClaudeApi = "anthropic";
    // Level 3 — Codex subscription (sidecar over the codex CLI).
    public const string Codex = "writing-codex-sub";
    public const string ClaudeModel = "claude-opus-5-5";
    // Owner decision 2 Oct 2026: GPT-6.1 Sol High (no silent fallback to gpt-6-sol).
    public const string CodexModel = "gpt-6.1-sol";

    // The admin "provider mode" and warn/failover percentages are display-only since
    // MAX-ALWAYS-ON: routing never reads them.
    public const string ModeAuto = "auto";
    public const string ModeClaude = "claude";
    public const string ModeCodex = "codex";

    public const double DefaultWarnPct = 80.0;
    public const double DefaultFailoverPct = 90.0;
}

public sealed class WritingSubscriptionSelector : IWritingSubscriptionSelector
{
    public const string MaxAlwaysFirst = "max_always_first";

    private static readonly WritingSubscriptionDecision Max = new(
        WritingSubscriptionProviders.Claude,
        WritingSubscriptionProviders.ClaudeModel,
        MaxAlwaysFirst,
        UtilizationPct: null,
        IsFallback: false);

    public Task<WritingSubscriptionDecision> DecideAsync(CancellationToken ct) => Task.FromResult(Max);
}
