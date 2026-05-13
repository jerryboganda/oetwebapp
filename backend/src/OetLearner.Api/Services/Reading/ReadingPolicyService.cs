using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Reading;

// ═════════════════════════════════════════════════════════════════════════════
// Reading policy resolver — Slice R6 (built early because R4+R5 depend on it)
//
// Singleton ReadingPolicy row, read-through cached for 15s. All read paths
// go through here; direct DbSet access is a code smell in this subsystem.
//
// Per-user overrides applied on top: extra-time entitlement, block flag.
// ═════════════════════════════════════════════════════════════════════════════

public interface IReadingPolicyService
{
    Task<ReadingPolicy> GetGlobalAsync(CancellationToken ct);
    Task<ReadingUserPolicyOverride?> GetUserOverrideAsync(string userId, CancellationToken ct);
    Task<ReadingResolvedPolicy> ResolveForUserAsync(string? userId, CancellationToken ct);
    Task<ReadingPolicy> UpsertGlobalAsync(ReadingPolicy next, string adminId, CancellationToken ct);
    Task<ReadingUserPolicyOverride> UpsertUserOverrideAsync(
        string userId, ReadingUserPolicyOverride next, string adminId, CancellationToken ct);
}

/// <summary>Effective policy for a specific user. Read-only snapshot.
/// Serialised onto <see cref="ReadingAttempt.PolicySnapshotJson"/> on start
/// so in-flight attempts are insulated from policy changes.</summary>
public sealed record ReadingResolvedPolicy(
    int AttemptsPerPaperPerUser,
    int AttemptCooldownMinutes,
    string PartATimerStrictness,
    int PartATimerMinutes,
    int PartBCTimerMinutes,
    int GracePeriodSeconds,
    string OnExpirySubmitPolicy,
    IReadOnlyList<int> CountdownWarnings,
    IReadOnlyList<string> EnabledQuestionTypes,
    string ShortAnswerNormalisation,
    bool ShortAnswerAcceptSynonyms,
    bool MatchingAllowPartialCredit,
    string UnknownTypeFallbackPolicy,
    bool ShowExplanationsAfterSubmit,
    bool ShowExplanationsOnlyIfWrong,
    bool ShowCorrectAnswerOnReview,
    int SubmitRateLimitPerMinute,
    int AutosaveRateLimitPerMinute,
    int ExtraTimeEntitlementPct,
    bool AllowMultipleConcurrentAttempts,
    bool AllowPausingAttempt,
    bool AllowResumeAfterExpiry,
    bool AllowPaperReadingMode);

public sealed class ReadingPolicyService(LearnerDbContext db, Microsoft.Extensions.Caching.Memory.IMemoryCache cache)
    : IReadingPolicyService
{
    private const string GlobalCacheKey = "reading:policy:global";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(15);

    public async Task<ReadingPolicy> GetGlobalAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(GlobalCacheKey, out ReadingPolicy? cached) && cached is not null) return cached;
        var row = await db.ReadingPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == "global", ct);
        if (row is null)
        {
            row = new ReadingPolicy { Id = "global", UpdatedAt = DateTimeOffset.UtcNow };
            db.ReadingPolicies.Add(row);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                row = await db.ReadingPolicies.AsNoTracking().FirstAsync(p => p.Id == "global", ct);
            }
        }
        cache.Set(GlobalCacheKey, row, CacheTtl);
        return row;
    }

    public Task<ReadingUserPolicyOverride?> GetUserOverrideAsync(string userId, CancellationToken ct)
        => db.ReadingUserPolicyOverrides.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, ct);

    public async Task<ReadingResolvedPolicy> ResolveForUserAsync(string? userId, CancellationToken ct)
    {
        var g = await GetGlobalAsync(ct);
        var o = string.IsNullOrWhiteSpace(userId) ? null : await GetUserOverrideAsync(userId, ct);

        // Extra-time entitlement bumps timers.
        var extraPct = o?.ExtraTimeEntitlementPct ?? 0;
        int Bump(int minutes) => extraPct > 0 ? (int)Math.Ceiling(minutes * (1m + extraPct / 100m)) : minutes;

        return new ReadingResolvedPolicy(
            AttemptsPerPaperPerUser: g.AttemptsPerPaperPerUser,
            AttemptCooldownMinutes: g.AttemptCooldownMinutes,
            PartATimerStrictness: g.PartATimerStrictness,
            PartATimerMinutes: Bump(g.PartATimerMinutes),
            PartBCTimerMinutes: Bump(g.PartBCTimerMinutes),
            GracePeriodSeconds: g.GracePeriodSeconds,
            OnExpirySubmitPolicy: g.OnExpirySubmitPolicy,
            CountdownWarnings: ParseIntArray(g.CountdownWarningsJson),
            EnabledQuestionTypes: ParseStringArray(g.EnabledQuestionTypesJson),
            ShortAnswerNormalisation: "trim_only",
            ShortAnswerAcceptSynonyms: false,
            MatchingAllowPartialCredit: false,
            UnknownTypeFallbackPolicy: g.UnknownTypeFallbackPolicy,
            ShowExplanationsAfterSubmit: g.ShowExplanationsAfterSubmit,
            ShowExplanationsOnlyIfWrong: g.ShowExplanationsOnlyIfWrong,
            ShowCorrectAnswerOnReview: g.ShowCorrectAnswerOnReview,
            SubmitRateLimitPerMinute: g.SubmitRateLimitPerMinute,
            AutosaveRateLimitPerMinute: g.AutosaveRateLimitPerMinute,
            ExtraTimeEntitlementPct: extraPct,
            AllowMultipleConcurrentAttempts: g.AllowMultipleConcurrentAttempts,
            AllowPausingAttempt: g.AllowPausingAttempt,
            AllowResumeAfterExpiry: g.AllowResumeAfterExpiry,
            AllowPaperReadingMode: g.AllowPaperReadingMode);
    }

    public async Task<ReadingPolicy> UpsertGlobalAsync(ReadingPolicy next, string adminId, CancellationToken ct)
    {
        var row = await db.ReadingPolicies.FirstOrDefaultAsync(p => p.Id == "global", ct);
        if (row is null)
        {
            row = new ReadingPolicy { Id = "global" };
            db.ReadingPolicies.Add(row);
        }
        // Copy every configurable field. Concurrency token enforces admin
        // collision detection.
        row.AttemptsPerPaperPerUser = next.AttemptsPerPaperPerUser;
        row.AttemptCooldownMinutes = next.AttemptCooldownMinutes;
        row.BestScoreDisplay = next.BestScoreDisplay;
        row.ShowPastAttempts = next.ShowPastAttempts;
        row.AllowAttemptOnArchivedPaper = next.AllowAttemptOnArchivedPaper;
        row.PartATimerStrictness = next.PartATimerStrictness;
        row.PartATimerMinutes = next.PartATimerMinutes;
        row.PartBCTimerMinutes = next.PartBCTimerMinutes;
        row.GracePeriodSeconds = next.GracePeriodSeconds;
        row.OnExpirySubmitPolicy = next.OnExpirySubmitPolicy;
        row.CountdownWarningsJson = next.CountdownWarningsJson;
        row.EnabledQuestionTypesJson = next.EnabledQuestionTypesJson;
        row.ShortAnswerNormalisation = "trim_only";
        row.ShortAnswerAcceptSynonyms = false;
        row.MatchingAllowPartialCredit = false;
        row.SentenceCompletionStrictness = next.SentenceCompletionStrictness;
        row.UnknownTypeFallbackPolicy = next.UnknownTypeFallbackPolicy;
        row.ShowExplanationsAfterSubmit = next.ShowExplanationsAfterSubmit;
        row.ShowExplanationsOnlyIfWrong = next.ShowExplanationsOnlyIfWrong;
        row.ShowCorrectAnswerOnReview = next.ShowCorrectAnswerOnReview;
        row.AllowResultDownload = next.AllowResultDownload;
        row.AllowResultSharing = next.AllowResultSharing;
        row.AiExtractionEnabled = next.AiExtractionEnabled;
        row.AiExtractionRequireHumanApproval = next.AiExtractionRequireHumanApproval;
        row.AiExtractionMaxRetriesPerPaper = next.AiExtractionMaxRetriesPerPaper;
        row.AiExtractionModelOverride = next.AiExtractionModelOverride;
        row.AiExtractionStrictSchemaMode = next.AiExtractionStrictSchemaMode;
        row.QuestionBankEnabled = next.QuestionBankEnabled;
        row.AssemblyStrategy = next.AssemblyStrategy;
        row.AllowLearnerRandomisation = next.AllowLearnerRandomisation;
        row.FontScaleUserControl = next.FontScaleUserControl;
        row.HighContrastMode = next.HighContrastMode;
        row.ScreenReaderOptimised = next.ScreenReaderOptimised;
        row.AllowPaperReadingMode = next.AllowPaperReadingMode;
        row.ExtraTimeApprovalWorkflow = next.ExtraTimeApprovalWorkflow;
        row.RequireFreshAuthForSubmit = next.RequireFreshAuthForSubmit;
        row.AllowMultipleConcurrentAttempts = next.AllowMultipleConcurrentAttempts;
        row.AttemptIpPinning = next.AttemptIpPinning;
        row.SubmitRateLimitPerMinute = next.SubmitRateLimitPerMinute;
        row.AutosaveRateLimitPerMinute = next.AutosaveRateLimitPerMinute;
        row.PreventMultipleTabs = next.PreventMultipleTabs;
        row.RetainAnswerRowsDays = next.RetainAnswerRowsDays;
        row.RetainAttemptHeadersDays = next.RetainAttemptHeadersDays;
        row.AnonymiseOnAccountDelete = next.AnonymiseOnAccountDelete;
        row.ShareAnonymousAnalytics = next.ShareAnonymousAnalytics;
        row.AllowPausingAttempt = next.AllowPausingAttempt;
        row.AutoExpireWorkerEnabled = next.AutoExpireWorkerEnabled;
        row.AutoExpireAfterMinutes = next.AutoExpireAfterMinutes;
        row.AllowResumeAfterExpiry = next.AllowResumeAfterExpiry;

        row.RowVersion += 1;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.UpdatedByAdminId = adminId;

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = adminId,
            ActorName = adminId,
            Action = "ReadingPolicyUpdated",
            ResourceType = "ReadingPolicy",
            ResourceId = "global",
            Details = null,
        });
        await db.SaveChangesAsync(ct);
        cache.Remove(GlobalCacheKey);
        return row;
    }

    public async Task<ReadingUserPolicyOverride> UpsertUserOverrideAsync(
        string userId, ReadingUserPolicyOverride next, string adminId, CancellationToken ct)
    {
        var row = await db.ReadingUserPolicyOverrides.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        var now = DateTimeOffset.UtcNow;
        if (row is null)
        {
            row = new ReadingUserPolicyOverride { UserId = userId, CreatedAt = now };
            db.ReadingUserPolicyOverrides.Add(row);
        }
        row.ExtraTimeEntitlementPct = Math.Clamp(next.ExtraTimeEntitlementPct, 0, 100);
        row.BlockAttempts = next.BlockAttempts;
        row.Reason = next.Reason;
        row.GrantedByAdminId = adminId;
        row.ExpiresAt = next.ExpiresAt;
        row.UpdatedAt = now;
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = now, ActorId = adminId, ActorName = adminId,
            Action = "ReadingUserPolicyOverrideUpserted",
            ResourceType = "ReadingUserPolicyOverride",
            ResourceId = userId, Details = next.Reason,
        });
        await db.SaveChangesAsync(ct);
        return row;
    }

    private static IReadOnlyList<int> ParseIntArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<int>();
        try { return JsonSerializer.Deserialize<int[]>(json) ?? Array.Empty<int>(); }
        catch (JsonException) { return Array.Empty<int>(); }
    }

    private static IReadOnlyList<string> ParseStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try { return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>(); }
        catch (JsonException) { return Array.Empty<string>(); }
    }
}
