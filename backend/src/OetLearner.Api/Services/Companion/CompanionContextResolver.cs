using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Companion;

public interface ICompanionContextResolver
{
    /// <summary>
    /// Builds the server-trusted context for one companion turn. The envelope is
    /// a hint; everything authoritative is re-read here.
    /// </summary>
    Task<CompanionTurnContext> ResolveAsync(
        string userId,
        CompanionContextEnvelope? envelope,
        CancellationToken ct);
}

/// <summary>
/// Assembles the companion's view of the learner from the systems that already
/// own each fact — no parallel truth store.
///
/// <list type="bullet">
///   <item>identity, profession, locale → <c>LearnerUser</c></item>
///   <item>exam date, target, country → <c>LearnerGoal</c></item>
///   <item>tier, packages, credits → <see cref="IEffectiveEntitlementResolver"/></item>
///   <item>credit balance → <see cref="IAiPackageCreditService"/></item>
///   <item>kill switches → <see cref="ICompanionFeatureFlags"/></item>
/// </list>
///
/// <para>
/// Exam mode is deliberately resolved here rather than trusted from the client:
/// a learner inside a protected attempt could otherwise simply not send the flag
/// and ask the companion for answers (F-155).
/// </para>
/// </summary>
public sealed class CompanionContextResolver(
    LearnerDbContext db,
    IEffectiveEntitlementResolver entitlements,
    IAiPackageCreditService credits,
    ICompanionFeatureFlags flags,
    ILogger<CompanionContextResolver> logger) : ICompanionContextResolver
{
    public async Task<CompanionTurnContext> ResolveAsync(
        string userId,
        CompanionContextEnvelope? envelope,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        var goal = await db.Goals
            .AsNoTracking()
            .Where(g => g.UserId == userId)
            .OrderByDescending(g => g.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        var snapshot = await entitlements.ResolveAsync(userId, ct);

        var professionId = goal?.ProfessionId ?? user?.ActiveProfessionId;
        var profession = ParseProfession(professionId);

        var examDate = goal?.TargetExamDate;
        int? daysUntilExam = examDate is null
            ? null
            : examDate.Value.DayNumber - DateOnly.FromDateTime(DateTime.UtcNow).DayNumber;

        var creditBalance = await ResolveCreditBalanceAsync(userId, snapshot, ct);

        // Resolve every switch once so a mid-turn flag flip cannot make the turn
        // behave inconsistently with itself.
        var retrieval = await flags.IsRetrievalEnabledAsync(ct);
        var actions = await flags.AreActionsEnabledAsync(ct);
        var creditConsumption = await flags.IsCreditConsumptionEnabledAsync(ct);
        var scoreDisplay = await flags.IsScoreDisplayEnabledAsync(ct);

        var safeEnvelope = envelope ?? new CompanionContextEnvelope();
        var examMode = await ResolveExamModeAsync(userId, safeEnvelope, ct);

        return new CompanionTurnContext
        {
            UserId = userId,
            Profession = profession,
            ProfessionId = professionId,
            ExamTypeCode = goal?.ExamTypeCode ?? user?.ActiveExamTypeCode,
            ExamDate = examDate,
            DaysUntilExam = daysUntilExam,
            TargetGrade = goal?.OverallGoal,
            TargetCountry = goal?.TargetCountry,
            Tier = snapshot.Tier,
            EntitlementScopes = BuildScopes(snapshot),
            HasEligibleSubscription = snapshot.HasEligibleSubscription,
            AiCreditsRemaining = creditBalance,
            Locale = NormaliseLocale(user?.Locale),
            // The client-sent ExamMode is discarded; only the server value survives.
            Envelope = safeEnvelope with { ExamMode = examMode },
            ExamMode = examMode,
            RetrievalEnabled = retrieval,
            ActionsEnabled = actions,
            CreditConsumptionEnabled = creditConsumption,
            ScoreDisplayEnabled = scoreDisplay,
        };
    }

    /// <summary>
    /// The content scopes retrieval may consider. Built from the entitlement
    /// snapshot only — never from anything the client sent.
    /// </summary>
    private static IReadOnlyList<string> BuildScopes(EffectiveEntitlementSnapshot snapshot)
    {
        var scopes = new List<string>();

        if (!string.IsNullOrWhiteSpace(snapshot.PlanCode)) scopes.Add(snapshot.PlanCode!);
        scopes.AddRange(snapshot.ActiveAddOnCodes);
        scopes.AddRange(snapshot.PackageScopes);
        scopes.AddRange(snapshot.EnabledModules);

        if (snapshot.TutorBookUnlocked) scopes.Add("tutor-book");
        if (snapshot.BasicEnglishUnlocked) scopes.Add("basic-english");

        return scopes
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<int> ResolveCreditBalanceAsync(
        string userId,
        EffectiveEntitlementSnapshot snapshot,
        CancellationToken ct)
    {
        try
        {
            var creditSnapshot = await credits.GetSnapshotAsync(userId, 0, ct);
            return creditSnapshot?.CreditsRemaining ?? snapshot.AiCreditsRemaining;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A balance read failure must not take the conversation down; it makes
            // chargeable actions unavailable, which the charge path enforces anyway.
            logger.LogWarning(ex, "Companion could not read the credit balance for {UserId}.", userId);
            return snapshot.AiCreditsRemaining;
        }
    }

    /// <summary>
    /// True when the learner has an attempt in flight that forbids assistance.
    /// Resolved from persisted attempt state, so omitting the client flag cannot
    /// unlock hints.
    /// </summary>
    private async Task<bool> ResolveExamModeAsync(
        string userId,
        CompanionContextEnvelope envelope,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(envelope.AttemptId)) return false;

        try
        {
            // Fail closed: if an attempt id was supplied and we cannot prove it is
            // finished, treat the learner as being inside a protected attempt.
            var state = await db.Attempts
                .AsNoTracking()
                .Where(a => a.Id == envelope.AttemptId && a.UserId == userId)
                .Select(a => (AttemptState?)a.State)
                .FirstOrDefaultAsync(ct);

            // Unknown attempt id -> fail closed rather than assume it is finished.
            if (state is null) return true;

            // Assistance is only safe once the attempt can no longer be changed.
            return state is not (AttemptState.Submitted
                or AttemptState.Evaluating
                or AttemptState.Completed
                or AttemptState.Failed
                or AttemptState.Abandoned);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Companion could not resolve exam mode for attempt {AttemptId}; failing closed.",
                envelope.AttemptId);
            return true;
        }
    }

    private static ExamProfession ParseProfession(string? professionId)
    {
        if (string.IsNullOrWhiteSpace(professionId)) return ExamProfession.Medicine;

        var normalised = professionId.Replace("-", string.Empty).Replace("_", string.Empty).Trim();
        return Enum.TryParse<ExamProfession>(normalised, ignoreCase: true, out var parsed)
            ? parsed
            : ExamProfession.Medicine;
    }

    private static string NormaliseLocale(string? locale) =>
        string.IsNullOrWhiteSpace(locale) ? "en" : locale.Split('-')[0].ToLowerInvariant();
}
