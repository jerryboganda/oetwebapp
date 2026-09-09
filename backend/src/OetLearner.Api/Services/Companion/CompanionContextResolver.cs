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
    ICompanionFeatureFlags flags,
    ILogger<CompanionContextResolver> logger,
    // Optional on purpose: the credit balance only decorates the prompt with the
    // learner's remaining allowance. A turn must still work without it, and the
    // charge path enforces the balance for real — so nothing here depends on it.
    IAiPackageCreditService? credits = null) : ICompanionContextResolver
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

        // Absent row = defaults. A learner who never opened the settings still
        // gets a working companion, so this is never required to exist.
        var preferences = await db.CompanionPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct)
            ?? new CompanionPreference { UserId = userId };

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
            PackageScopes = snapshot.PackageScopes,
            HasEligibleSubscription = snapshot.HasEligibleSubscription,
            AiCreditsRemaining = creditBalance,
            Locale = NormaliseLocale(user?.Locale),
            Preferences = preferences,
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
        if (credits is null) return snapshot.AiCreditsRemaining;

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
    /// True when the learner has any attempt in flight that forbids assistance.
    ///
    /// <para>
    /// <b>This does not consult the envelope at all.</b> An earlier version keyed
    /// off <c>envelope.AttemptId</c>, which made the whole guard trivially
    /// bypassable — and, worse, dead: nothing on the wire ever populated the
    /// envelope, so exam mode was permanently false in production. The question
    /// "is this learner sitting an attempt right now?" is answerable from the
    /// database alone, so it is answered there, and the client cannot influence
    /// it by omission or by lying.
    /// </para>
    ///
    /// <para>
    /// The scan is bounded to attempts started recently: an <c>InProgress</c> row
    /// abandoned months ago is stale data, not a live exam, and letting it pin a
    /// learner into permanent refusal would be its own bug.
    /// </para>
    /// </summary>
    private async Task<bool> ResolveExamModeAsync(
        string userId,
        CompanionContextEnvelope envelope,
        CancellationToken ct)
    {
        var since = DateTimeOffset.UtcNow - MaxLiveAttemptAge;

        try
        {
            var hasLiveAttempt = await db.Attempts
                .AsNoTracking()
                .AnyAsync(
                    a => a.UserId == userId
                         && a.StartedAt >= since
                         && (a.State == AttemptState.InProgress
                             || a.State == AttemptState.Paused
                             || a.State == AttemptState.NotStarted),
                    ct);

            if (hasLiveAttempt) return true;

            // A client-supplied attempt id can only ever ADD protection: if the
            // surface says the learner is on an attempt page, honour it even when
            // the row does not look live, and fail closed on an id we cannot find.
            if (string.IsNullOrWhiteSpace(envelope.AttemptId)) return false;

            var state = await db.Attempts
                .AsNoTracking()
                .Where(a => a.Id == envelope.AttemptId && a.UserId == userId)
                .Select(a => (AttemptState?)a.State)
                .FirstOrDefaultAsync(ct);

            if (state is null) return true;

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
                "Companion could not resolve exam mode for {UserId}; failing closed.",
                userId);
            return true;
        }
    }

    /// <summary>
    /// How long an unfinished attempt still counts as "in progress". Longer than
    /// any real sitting, short enough that an abandoned row does not lock the
    /// companion out forever.
    /// </summary>
    private static readonly TimeSpan MaxLiveAttemptAge = TimeSpan.FromHours(8);

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
