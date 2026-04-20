using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Conversation;

/// <summary>
/// Conversation entitlement gating. Business rules:
///
///   - Unauthenticated: blocked (learner routes require auth anyway).
///   - Authenticated free tier: capped at <c>FreeTierSessionsLimit</c>
///     completed sessions per rolling <c>FreeTierWindowDays</c>-day window.
///   - Authenticated subscribers (Active or Trial): unlimited.
///   - Admin can disable the cap by setting <c>FreeTierSessionsLimit = -1</c>.
///
/// Evaluated on every <c>POST /v1/conversations</c> so learners see the
/// paywall before they start a session, not after.
/// </summary>
public interface IConversationEntitlementService
{
    Task<ConversationEntitlement> CheckAsync(string? userId, CancellationToken ct);
}

public sealed record ConversationEntitlement(
    bool Allowed,
    string Tier,
    int Remaining,
    int LimitPerWindow,
    int WindowDays,
    DateTimeOffset? ResetAt,
    string Reason);

public sealed class ConversationEntitlementService(
    LearnerDbContext db,
    IOptions<ConversationOptions> options) : IConversationEntitlementService
{
    public async Task<ConversationEntitlement> CheckAsync(string? userId, CancellationToken ct)
    {
        var opts = options.Value;
        var windowDays = opts.FreeTierWindowDays <= 0 ? 7 : opts.FreeTierWindowDays;

        if (!opts.Enabled)
        {
            return new ConversationEntitlement(
                Allowed: false,
                Tier: "disabled",
                Remaining: 0,
                LimitPerWindow: 0,
                WindowDays: windowDays,
                ResetAt: null,
                Reason: "AI Conversation is currently disabled by the administrator.");
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return new ConversationEntitlement(
                Allowed: false,
                Tier: "anonymous",
                Remaining: 0,
                LimitPerWindow: 0,
                WindowDays: windowDays,
                ResetAt: null,
                Reason: "Sign in to practise with the AI partner.");
        }

        var now = DateTimeOffset.UtcNow;

        var activeSub = await db.Subscriptions
            .Where(s => s.UserId == userId
                && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial))
            .FirstOrDefaultAsync(ct);
        if (activeSub is not null)
        {
            return new ConversationEntitlement(
                Allowed: true,
                Tier: activeSub.Status == SubscriptionStatus.Trial ? "trial" : "paid",
                Remaining: int.MaxValue,
                LimitPerWindow: int.MaxValue,
                WindowDays: windowDays,
                ResetAt: null,
                Reason: "Active subscription — unlimited AI conversation practice.");
        }

        if (opts.FreeTierSessionsLimit < 0)
        {
            return new ConversationEntitlement(
                Allowed: true,
                Tier: "free",
                Remaining: int.MaxValue,
                LimitPerWindow: int.MaxValue,
                WindowDays: windowDays,
                ResetAt: null,
                Reason: "Free tier is currently unlimited.");
        }

        var windowStart = now - TimeSpan.FromDays(windowDays);
        var sessionsInWindowRows = await db.ConversationSessions
            .Where(s => s.UserId == userId && s.CreatedAt >= windowStart)
            .OrderBy(s => s.CreatedAt)
            .Select(s => s.CreatedAt)
            .ToListAsync(ct);
        var sessionsInWindow = sessionsInWindowRows.Count;
        var remaining = Math.Max(0, opts.FreeTierSessionsLimit - sessionsInWindow);
        var earliest = sessionsInWindowRows.Count > 0 ? (DateTimeOffset?)sessionsInWindowRows[0] : null;
        var resetAt = earliest.HasValue ? earliest.Value + TimeSpan.FromDays(windowDays) : (DateTimeOffset?)null;

        if (remaining <= 0)
        {
            return new ConversationEntitlement(
                Allowed: false,
                Tier: "free",
                Remaining: 0,
                LimitPerWindow: opts.FreeTierSessionsLimit,
                WindowDays: windowDays,
                ResetAt: resetAt,
                Reason: $"Free tier allows {opts.FreeTierSessionsLimit} conversation sessions every {windowDays} days. Upgrade for unlimited practice.");
        }

        return new ConversationEntitlement(
            Allowed: true,
            Tier: "free",
            Remaining: remaining,
            LimitPerWindow: opts.FreeTierSessionsLimit,
            WindowDays: windowDays,
            ResetAt: resetAt,
            Reason: $"{remaining} of {opts.FreeTierSessionsLimit} free sessions remaining.");
    }
}
