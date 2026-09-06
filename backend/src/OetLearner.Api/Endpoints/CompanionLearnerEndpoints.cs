using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiManagement;
using OetLearner.Api.Services.Companion;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Learner-facing AI Learning Companion endpoints.
///
/// <para>
/// Two jobs, both of which the chat stream itself cannot do:
/// </para>
///
/// <list type="number">
///   <item><b>Session capability</b> — what this learner may do <i>before</i> they
///   type. The SignalR turn can only report a quota refusal after a message has
///   been sent, as a sentence in the transcript. That is the wrong shape for a
///   paywall: the surface needs capability, reason and the upgrade route up
///   front, so it can show an upgrade card instead of an unusable input box
///   (F-135, F-139…F-142).</item>
///   <item><b>Memory controls</b> — the companion's <c>save_user_note</c> and
///   <c>bookmark_recall_term</c> tools write rows on the learner's behalf, and
///   until now nothing let the learner see or delete them. A companion that can
///   remember things about you and never show you what it remembered is not
///   acceptable (F-047).</item>
/// </list>
///
/// <para>
/// UserId always comes from the JWT and is never accepted from input.
/// </para>
/// </summary>
public static class CompanionLearnerEndpoints
{
    /// <summary>
    /// Feature codes whose written rows count as companion memory. A note the
    /// learner wrote themselves, or one authored by a different AI feature (the
    /// Writing coach, say), is not the companion's to delete.
    /// </summary>
    private static readonly string[] CompanionFeatureCodes =
    [
        AiFeatureCodes.AiAssistantLearner,
        AiFeatureCodes.CompanionChat,
        AiFeatureCodes.CompanionAction,
    ];

    public static IEndpointRouteBuilder MapCompanionLearnerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/companion")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        group.MapGet("/session", GetSessionAsync);
        group.MapGet("/memory", GetMemoryAsync);
        group.MapDelete("/memory/notes/{noteId}", DeleteNoteAsync);
        group.MapDelete("/memory/bookmarks/{bookmarkId}", DeleteBookmarkAsync);
        group.MapDelete("/memory", ResetMemoryAsync);

        return app;
    }

    private static async Task<IResult> GetSessionAsync(
        HttpContext http,
        LearnerDbContext db,
        ICompanionFeatureFlags flags,
        ICompanionContextResolver contexts,
        ICompanionDestinationRegistry destinations,
        IAiQuotaService quota,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();

        var enabled = await flags.IsEnabledAsync(ct);
        var context = await contexts.ResolveAsync(userId, null, ct);

        var access = enabled
            ? await ResolveAccessAsync(userId, db, quota, ct)
            : new CompanionAccess(false, "companion_disabled", null, null);

        // The upgrade route is resolved through the registry like every other
        // link, so the paywall cannot point somewhere the learner cannot open.
        var upgrade = access.CanChat ? null : await destinations.ResolveAsync("pricing", context, ct);
        var topUp = await destinations.ResolveAsync("ai.packages", context, ct);

        return Results.Ok(new
        {
            enabled,
            persona = ResolvePersona(configuration),
            retrievalEnabled = context.RetrievalEnabled,
            actionsEnabled = context.ActionsEnabled,
            creditConsumptionEnabled = context.CreditConsumptionEnabled,
            // TV-006 / TV-007. The client must not render a numeric band while
            // this is false, and the prompt independently forbids producing one.
            scoreDisplayEnabled = context.ScoreDisplayEnabled,
            tier = context.Tier,
            hasSubscription = context.HasEligibleSubscription,
            professionId = context.ProfessionId,
            examTypeCode = context.ExamTypeCode,
            examDate = context.ExamDate?.ToString("yyyy-MM-dd"),
            daysUntilExam = context.DaysUntilExam,
            locale = context.Locale,
            aiCreditsRemaining = context.AiCreditsRemaining,
            access = new
            {
                canChat = access.CanChat,
                reason = access.Reason,
                planCode = access.PlanCode,
                planName = access.PlanName,
                upgradeUrl = upgrade?.Url,
            },
            topUpUrl = topUp.Allowed ? topUp.Url : null,
        });
    }

    /// <summary>
    /// Mirrors what <c>AiQuotaService.TryReserveAsync</c> would decide, without
    /// reserving anything — this is a display query, so it must not consume
    /// allowance. It deliberately reports the <i>first</i> reason the learner is
    /// blocked, in the same order the gateway applies them.
    /// </summary>
    private static async Task<CompanionAccess> ResolveAccessAsync(
        string userId,
        LearnerDbContext db,
        IAiQuotaService quota,
        CancellationToken ct)
    {
        AiUserPolicySnapshot policy;
        try
        {
            policy = await quota.GetUserPolicyAsync(userId, ct);
        }
        catch (Exception)
        {
            // Fail closed on an unreadable policy: better a paywall the learner
            // can question than a chat box that errors on every message.
            return new CompanionAccess(false, "policy_unavailable", null, null);
        }

        if (policy.AiDisabled) return new CompanionAccess(false, "ai_disabled", policy.PlanCode, policy.PlanName);
        // Either scope blocks the companion: its feature codes are in
        // AiCredentialResolver.PlatformOnlyFeatures, so it is never BYOK-funded
        // and PlatformKeysOnly stops it just as surely as AllCalls.
        if (policy.KillSwitchActive)
        {
            return new CompanionAccess(false, "kill_switch", policy.PlanCode, policy.PlanName);
        }

        // An empty allow-list means "every feature"; a populated one is exhaustive.
        var allowedCsv = await db.AiQuotaPlans
            .AsNoTracking()
            .Where(p => p.Code == policy.PlanCode && p.IsActive)
            .Select(p => p.AllowedFeaturesCsv)
            .FirstOrDefaultAsync(ct);

        if (!string.IsNullOrWhiteSpace(allowedCsv))
        {
            var allowed = allowedCsv
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!CompanionFeatureCodes.Any(allowed.Contains))
            {
                return new CompanionAccess(false, "plan_excludes_companion", policy.PlanCode, policy.PlanName);
            }
        }

        if (policy.MonthlyTokenCap > 0 && policy.TokensUsedThisMonth >= policy.MonthlyTokenCap)
        {
            return new CompanionAccess(false, "monthly_cap_reached", policy.PlanCode, policy.PlanName);
        }

        if (policy.DailyTokenCap > 0 && policy.TokensUsedToday >= policy.DailyTokenCap)
        {
            return new CompanionAccess(false, "daily_cap_reached", policy.PlanCode, policy.PlanName);
        }

        return new CompanionAccess(true, "ok", policy.PlanCode, policy.PlanName);
    }

    private static async Task<IResult> GetMemoryAsync(
        HttpContext http,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();

        var notes = await db.UserNotes
            .AsNoTracking()
            .Where(n => n.UserId == userId
                        && n.CreatedByFeatureCode != null
                        && CompanionFeatureCodes.Contains(n.CreatedByFeatureCode))
            .OrderByDescending(n => n.CreatedAt)
            .Take(200)
            .Select(n => new
            {
                id = n.Id,
                title = n.Title,
                body = n.BodyMarkdown,
                createdAt = n.CreatedAt,
            })
            .ToListAsync(ct);

        var bookmarks = await (
            from b in db.RecallBookmarks.AsNoTracking()
            join t in db.VocabularyTerms.AsNoTracking() on b.VocabularyTermId equals t.Id into tj
            from t in tj.DefaultIfEmpty()
            where b.UserId == userId
                  && b.CreatedByFeatureCode != null
                  && CompanionFeatureCodes.Contains(b.CreatedByFeatureCode)
            orderby b.CreatedAt descending
            select new
            {
                id = b.Id,
                term = t != null ? t.Term : b.VocabularyTermId,
                createdAt = b.CreatedAt,
            })
            .Take(200)
            .ToListAsync(ct);

        return Results.Ok(new { notes, bookmarks });
    }

    private static async Task<IResult> DeleteNoteAsync(
        string noteId,
        HttpContext http,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();

        // Scoped by UserId as well as id: an id from another learner must read as
        // "not found", never as a successful delete.
        var deleted = await db.UserNotes
            .Where(n => n.Id == noteId
                        && n.UserId == userId
                        && n.CreatedByFeatureCode != null
                        && CompanionFeatureCodes.Contains(n.CreatedByFeatureCode))
            .ExecuteDeleteAsync(ct);

        return deleted == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static async Task<IResult> DeleteBookmarkAsync(
        string bookmarkId,
        HttpContext http,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();

        var deleted = await db.RecallBookmarks
            .Where(b => b.Id == bookmarkId
                        && b.UserId == userId
                        && b.CreatedByFeatureCode != null
                        && CompanionFeatureCodes.Contains(b.CreatedByFeatureCode))
            .ExecuteDeleteAsync(ct);

        return deleted == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static async Task<IResult> ResetMemoryAsync(
        HttpContext http,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();

        var notes = await db.UserNotes
            .Where(n => n.UserId == userId
                        && n.CreatedByFeatureCode != null
                        && CompanionFeatureCodes.Contains(n.CreatedByFeatureCode))
            .ExecuteDeleteAsync(ct);

        var bookmarks = await db.RecallBookmarks
            .Where(b => b.UserId == userId
                        && b.CreatedByFeatureCode != null
                        && CompanionFeatureCodes.Contains(b.CreatedByFeatureCode))
            .ExecuteDeleteAsync(ct);

        return Results.Ok(new { notesDeleted = notes, bookmarksDeleted = bookmarks });
    }

    private static string ResolvePersona(IConfiguration configuration)
    {
        var configured = configuration[CompanionPromptComposer.PersonaSettingKey];
        return string.IsNullOrWhiteSpace(configured)
            ? CompanionPromptComposer.DefaultPersona
            : configured.Trim();
    }

    private sealed record CompanionAccess(bool CanChat, string Reason, string? PlanCode, string? PlanName);
}
