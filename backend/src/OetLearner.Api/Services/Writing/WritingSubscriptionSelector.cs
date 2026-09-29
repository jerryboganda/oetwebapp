using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Services.Writing;

/// <summary>
/// Chooses which subscription-backed provider serves the next Writing AI call,
/// implementing the owner directive of 2026-09-29: Claude Opus 5.5 (high) on the
/// dedicated Max 5x subscription is primary; the Codex subscription (gpt-6-sol,
/// high) is the seamless fallback; a candidate submission must never fail merely
/// because Claude reached its weekly limit.
///
/// Modes (admin-selectable, persisted on the <see cref="RuntimeSettingsRow"/>
/// singleton — see <c>WritingAiProviderMode</c>):
/// <list type="bullet">
///   <item><b>auto</b> — Claude until weekly utilisation reaches the failover
///   threshold (default 90%) or Claude reports a quota/rate-limit signal, then
///   Codex. Returns to Claude after the weekly window resets and utilisation
///   drops back below the warn threshold (hysteresis prevents flapping).</item>
///   <item><b>claude</b> — force Claude.</item>
///   <item><b>codex</b> — force Codex.</item>
/// </list>
///
/// This service is read-only/decision-only: it never calls a provider itself.
/// The writing pipeline calls <see cref="DecideAsync"/> before the AI call and
/// feeds <see cref="RecordClaudeQuotaSignal"/> when Claude refuses, so failover
/// stays inside one coordinated <c>AiOperation</c> and the learner's credit is
/// debited exactly once.
/// </summary>
public interface IWritingSubscriptionSelector
{
    Task<WritingSubscriptionDecision> DecideAsync(CancellationToken ct);
    /// <summary>Record that the Claude sidecar reported quota/rate exhaustion
    /// (its <c>quota_exceeded</c> signal). Switches auto mode onto Codex until the
    /// weekly reset so subsequent calls don't keep hammering a saturated limit.</summary>
    Task RecordClaudeQuotaSignalAsync(CancellationToken ct);
}

public sealed record WritingSubscriptionDecision(
    string ProviderCode,         // WritingSubscriptionProviders.Claude | .Codex
    string Model,
    string Reason,               // machine-readable: "auto_primary", "auto_quota_failover", "forced_claude", ...
    double? UtilizationPct,
    bool IsFallback);

public static class WritingSubscriptionProviders
{
    // Level 1 — dedicated Claude Max 5x subscription (sidecar over the claude CLI).
    public const string Claude = "writing-claude-sub";
    // Level 2 — Claude API (pay-as-you-go Anthropic key on the `anthropic` row).
    // Used when the subscription hits its weekly cap or errors, before Codex.
    public const string ClaudeApi = "anthropic";
    // Level 3 — Codex subscription (sidecar over the codex CLI).
    public const string Codex = "writing-codex-sub";
    public const string ClaudeModel = "claude-opus-5-5";
    public const string CodexModel = "gpt-6-sol";

    public const string ModeAuto = "auto";
    public const string ModeClaude = "claude";
    public const string ModeCodex = "codex";

    public const double DefaultWarnPct = 80.0;
    public const double DefaultFailoverPct = 90.0;
}

public sealed class WritingSubscriptionSelector(
    IWritingSubscriptionQuotaService quota,
    IRuntimeSettingsProvider settings,
    IServiceScopeFactory scopeFactory,
    ILogger<WritingSubscriptionSelector> logger) : IWritingSubscriptionSelector
{
    public async Task<WritingSubscriptionDecision> DecideAsync(CancellationToken ct)
    {
        var row = await settings.GetRawAsync(ct);
        var mode = NormaliseMode(row.WritingAiProviderMode);
        var warnPct = ClampPct(row.WritingAiWarnPct, WritingSubscriptionProviders.DefaultWarnPct);
        var failoverPct = ClampPct(row.WritingAiFailoverPct, WritingSubscriptionProviders.DefaultFailoverPct);

        if (mode == WritingSubscriptionProviders.ModeClaude)
            return Claude("forced_claude", null);
        if (mode == WritingSubscriptionProviders.ModeCodex)
            return Codex("forced_codex", null, isFallback: false);

        // auto
        var snapshot = await quota.GetSnapshotAsync(ct);
        var util = snapshot.UtilizationPct;

        // Sticky quota-override: a hard Claude refusal recorded earlier this
        // window keeps the SUBSCRIPTION off until the reset. The chain still goes
        // to the Claude API (level 2) first — Codex is the last resort.
        if (row.WritingAiClaudeQuotaExceededUntil is { } until && until > DateTimeOffset.UtcNow)
        {
            return ClaudeApiRoute("auto_quota_signal", util);
        }

        if (util is double u && u >= failoverPct)
            return ClaudeApiRoute("auto_threshold_failover", util);

        return Claude("auto_primary", util);
    }

    public async Task RecordClaudeQuotaSignalAsync(CancellationToken ct)
    {
        // Persist a short-lived "Claude is exhausted" marker so the very next
        // grading call (and all others until the weekly reset) fail over without
        // each first paying for a doomed Claude attempt. Cleared automatically
        // once the weekly window rolls past ResetsAt.
        try
        {
            var snapshot = await quota.GetSnapshotAsync(ct);
            var until = snapshot.ResetsAt ?? DateTimeOffset.UtcNow.AddDays(7);
            // Resolve a scoped DbContext for the write (selector is scoped, but a
            // dedicated scope keeps this write isolated from the request's tracked
            // graph and its SaveChanges lifecycle).
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var row = await db.RuntimeSettings.FirstOrDefaultAsync(r => r.Id == "default", ct);
            if (row is null)
            {
                row = new RuntimeSettingsRow { Id = "default", UpdatedAt = DateTimeOffset.UtcNow };
                db.RuntimeSettings.Add(row);
            }
            row.WritingAiClaudeQuotaExceededUntil = until;
            await db.SaveChangesAsync(ct);
            settings.Invalidate();
            logger.LogInformation(
                "Writing AI: Claude subscription reported quota exhaustion; failing over to Codex until {Until}.", until);
        }
        catch (Exception ex)
        {
            // Never let quota bookkeeping break a grading call.
            logger.LogWarning(ex, "Writing AI: failed to persist Claude quota-exhaustion marker.");
        }
    }

    private static WritingSubscriptionDecision Claude(string reason, double? util)
        => new(WritingSubscriptionProviders.Claude, WritingSubscriptionProviders.ClaudeModel, reason, util, IsFallback: false);

    private static WritingSubscriptionDecision ClaudeApiRoute(string reason, double? util)
        => new(WritingSubscriptionProviders.ClaudeApi, WritingSubscriptionProviders.ClaudeModel, reason, util, IsFallback: true);

    private static WritingSubscriptionDecision Codex(string reason, double? util, bool isFallback)
        => new(WritingSubscriptionProviders.Codex, WritingSubscriptionProviders.CodexModel, reason, util, IsFallback: isFallback);

    private static string NormaliseMode(string? mode)
        => (mode ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            WritingSubscriptionProviders.ModeClaude => WritingSubscriptionProviders.ModeClaude,
            WritingSubscriptionProviders.ModeCodex => WritingSubscriptionProviders.ModeCodex,
            _ => WritingSubscriptionProviders.ModeAuto,
        };

    private static double ClampPct(double? value, double fallback)
        => value is double v && v > 0 && v <= 100 ? v : fallback;
}
