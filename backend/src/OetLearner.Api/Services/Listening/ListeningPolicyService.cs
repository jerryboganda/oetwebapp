using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// Listening policy resolver — mirrors ReadingPolicyService (Slice R6 pattern).
//
// Singleton ListeningPolicy row (id = "global"), read-through cached for 15 s.
// All read paths go through here; direct DbSet access is a code smell.
//
// Per-user overrides applied on top: extra-time entitlement, block flag,
// accessibility mode.
// ═════════════════════════════════════════════════════════════════════════════

public interface IListeningPolicyService
{
    Task<ListeningPolicy> GetGlobalAsync(CancellationToken ct);
    Task<ListeningUserPolicyOverride?> GetUserOverrideAsync(string userId, CancellationToken ct);
    Task<ListeningPolicy> UpsertGlobalAsync(ListeningPolicy next, string adminId, CancellationToken ct);
    Task<ListeningUserPolicyOverride> UpsertUserOverrideAsync(
        string userId, ListeningUserPolicyOverride next, string adminId, CancellationToken ct);
}

public sealed class ListeningPolicyService(LearnerDbContext db, IMemoryCache cache)
    : IListeningPolicyService
{
    private const string GlobalCacheKey = "ListeningPolicy:global";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(15);
    private static readonly HashSet<string> AllowedNormalisationProfiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "exact",
        "trim_only",
        "trim_collapse",
        "trim_collapse_case_insensitive",
    };

    /// <summary>
    /// Parse the owner-configured whole-attempt countdown warning thresholds.
    /// Invalid policy JSON fails closed to no client-side warning thresholds;
    /// the server deadline and expiry behaviour remain authoritative.
    /// </summary>
    public static IReadOnlyList<int> ParseCountdownWarnings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<int>();
        try
        {
            var values = JsonSerializer.Deserialize<int[]>(json);
            return values is null
                ? Array.Empty<int>()
                : values
                    .Where(value => value > 0 && value <= 86_400)
                    .Distinct()
                    .OrderByDescending(value => value)
                    .ToArray();
        }
        catch (JsonException)
        {
            return Array.Empty<int>();
        }
    }

    public async Task<ListeningPolicy> GetGlobalAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(GlobalCacheKey, out ListeningPolicy? cached) && cached is not null) return cached;
        var row = await db.ListeningPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == "global", ct);
        if (row is null)
        {
            row = new ListeningPolicy { Id = "global", UpdatedAt = DateTimeOffset.UtcNow };
            db.ListeningPolicies.Add(row);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                row = await db.ListeningPolicies.AsNoTracking().FirstAsync(p => p.Id == "global", ct);
            }
        }
        cache.Set(GlobalCacheKey, row, CacheTtl);
        return row;
    }

    public Task<ListeningUserPolicyOverride?> GetUserOverrideAsync(string userId, CancellationToken ct)
        => db.ListeningUserPolicyOverrides.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, ct);

    public async Task<ListeningPolicy> UpsertGlobalAsync(ListeningPolicy next, string adminId, CancellationToken ct)
    {
        if (next.AiExtractionMaxRetriesPerPaper < 0)
            throw ApiException.Validation(
                "listening_policy_invalid",
                "AI extraction maximum retries per paper cannot be negative; use 0 for unlimited.");
        if (string.IsNullOrWhiteSpace(next.ShortAnswerNormalisation)
            || !AllowedNormalisationProfiles.Contains(next.ShortAnswerNormalisation.Trim()))
        {
            throw ApiException.Validation(
                "listening_policy_invalid",
                "Short-answer normalisation must be exact, trim_only, trim_collapse, or trim_collapse_case_insensitive; fuzzy matching is not permitted.");
        }

        var row = await db.ListeningPolicies.FirstOrDefaultAsync(p => p.Id == "global", ct);
        if (row is null)
        {
            row = new ListeningPolicy { Id = "global" };
            db.ListeningPolicies.Add(row);
        }

        // §1 Retry
        row.AttemptsPerPaperPerUser = next.AttemptsPerPaperPerUser;
        row.AttemptCooldownMinutes = next.AttemptCooldownMinutes;
        row.BestScoreDisplay = next.BestScoreDisplay;
        row.ShowPastAttempts = next.ShowPastAttempts;

        // §2 Timer
        row.FullPaperTimerMinutes = next.FullPaperTimerMinutes;
        row.GracePeriodSeconds = next.GracePeriodSeconds;
        row.OnExpirySubmitPolicy = next.OnExpirySubmitPolicy;
        row.CountdownWarningsJson = next.CountdownWarningsJson;

        // §3 Audio replay
        row.ExamReplayAllowed = next.ExamReplayAllowed;
        row.LearningReplayAllowed = next.LearningReplayAllowed;
        row.LearningEvidenceLoopEnabled = next.LearningEvidenceLoopEnabled;

        // §4 Grading
        row.ShortAnswerNormalisation = next.ShortAnswerNormalisation;
        row.ShortAnswerAcceptSynonyms = next.ShortAnswerAcceptSynonyms;

        // §5 AI extraction
        row.AiExtractionEnabled = next.AiExtractionEnabled;
        // Listening extraction is always staged as Pending and can only be
        // applied through the explicit admin approval path. Keep this
        // invariant server-owned even if an older client submits false.
        row.AiExtractionRequireHumanApproval = true;
        // 0 means unlimited; negative values were rejected above.
        row.AiExtractionMaxRetriesPerPaper = next.AiExtractionMaxRetriesPerPaper;

        // §6 Review
        row.ShowExplanationsAfterSubmit = next.ShowExplanationsAfterSubmit;
        row.ShowExplanationsOnlyIfWrong = next.ShowExplanationsOnlyIfWrong;
        row.ShowCorrectAnswerOnReview = next.ShowCorrectAnswerOnReview;

        // §7 Accessibility
        row.DefaultExtraTimePct = next.DefaultExtraTimePct;
        row.ScreenReaderOptimised = next.ScreenReaderOptimised;

        // §8 Lifecycle
        row.AutoExpireWorkerEnabled = next.AutoExpireWorkerEnabled;
        row.AutoExpireAfterMinutes = next.AutoExpireAfterMinutes;
        row.AllowResumeAfterExpiry = next.AllowResumeAfterExpiry;

        // §9 Retention
        row.RetainAnswerRowsDays = next.RetainAnswerRowsDays;
        row.RetainAttemptHeadersDays = next.RetainAttemptHeadersDays;
        row.AnonymiseOnAccountDelete = next.AnonymiseOnAccountDelete;

        // Listening V2 — R05 preview/review windows
        row.PreviewWindowMsA1 = next.PreviewWindowMsA1;
        row.PreviewWindowMsA2 = next.PreviewWindowMsA2;
        row.PreviewWindowMsC1 = next.PreviewWindowMsC1;
        row.PreviewWindowMsC2 = next.PreviewWindowMsC2;
        row.ReviewWindowMsA1 = next.ReviewWindowMsA1;
        row.ReviewWindowMsA2 = next.ReviewWindowMsA2;
        row.ReviewWindowMsC1 = next.ReviewWindowMsC1;
        row.ReviewWindowMsC2FinalCbt = next.ReviewWindowMsC2FinalCbt;
        row.ReviewWindowMsC2FinalPaper = next.ReviewWindowMsC2FinalPaper;

        // R06 — between-section + Part B
        row.BetweenSectionTransitionMs = next.BetweenSectionTransitionMs;
        row.PartBQuestionWindowMs = next.PartBQuestionWindowMs;
        row.OneWayLocksEnabled = next.OneWayLocksEnabled;
        row.ConfirmDialogRequired = next.ConfirmDialogRequired;
        row.UnansweredWarningRequired = next.UnansweredWarningRequired;
        row.ConfirmTokenTtlMs = next.ConfirmTokenTtlMs;

        // R08 — annotations
        row.HighlightingEnabledPartA = next.HighlightingEnabledPartA;
        row.HighlightingEnabledPartBC = next.HighlightingEnabledPartBC;
        row.OptionStrikethroughEnabled = next.OptionStrikethroughEnabled;
        row.InAppZoomEnabled = next.InAppZoomEnabled;
        row.CtrlZoomBlocked = next.CtrlZoomBlocked;
        row.AnnotationsPersistOnAdvance = next.AnnotationsPersistOnAdvance;

        // R10 — tech-readiness
        row.TechReadinessRequired = next.TechReadinessRequired;
        row.TechReadinessTtlMs = next.TechReadinessTtlMs;

        // R07.3 — paper-mode final review
        row.FinalReviewAllPartsMsPaper = next.FinalReviewAllPartsMsPaper;

        row.RowVersion += 1;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.UpdatedByAdminId = adminId;

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = adminId,
            ActorName = adminId,
            Action = "ListeningPolicyUpdated",
            ResourceType = "ListeningPolicy",
            ResourceId = "global",
            Details = null,
        });
        await db.SaveChangesAsync(ct);
        cache.Remove(GlobalCacheKey);
        return row;
    }

    public async Task<ListeningUserPolicyOverride> UpsertUserOverrideAsync(
        string userId, ListeningUserPolicyOverride next, string adminId, CancellationToken ct)
    {
        var row = await db.ListeningUserPolicyOverrides.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        var now = DateTimeOffset.UtcNow;
        if (row is null)
        {
            row = new ListeningUserPolicyOverride { UserId = userId, CreatedAt = now };
            db.ListeningUserPolicyOverrides.Add(row);
        }
        row.ExtraTimeEntitlementPct = Math.Clamp(next.ExtraTimeEntitlementPct, 0, 100);
        row.BlockAttempts = next.BlockAttempts;
        row.AccessibilityModeEnabled = next.AccessibilityModeEnabled;
        row.Reason = next.Reason;
        row.GrantedByAdminId = adminId;
        row.ExpiresAt = next.ExpiresAt;
        row.UpdatedAt = now;
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = now, ActorId = adminId, ActorName = adminId,
            Action = "ListeningUserPolicyOverrideUpserted",
            ResourceType = "ListeningUserPolicyOverride",
            ResourceId = userId, Details = next.Reason,
        });
        await db.SaveChangesAsync(ct);
        return row;
    }
}
