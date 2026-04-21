using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Conversation;

public interface IConversationEntitlementService
{
    Task<ConversationEntitlement> CheckAsync(string? userId, CancellationToken ct);
}

public sealed record ConversationEntitlement(
    bool Allowed, string Tier, int Remaining, int LimitPerWindow,
    int WindowDays, DateTimeOffset? ResetAt, string Reason);

public sealed class ConversationEntitlementService(
    LearnerDbContext db,
    IOptions<ConversationOptions> options) : IConversationEntitlementService
{
    public async Task<ConversationEntitlement> CheckAsync(string? userId, CancellationToken ct)
    {
        var opts = options.Value;
        var windowDays = opts.FreeTierWindowDays <= 0 ? 7 : opts.FreeTierWindowDays;

        if (!opts.Enabled)
            return new ConversationEntitlement(false, "disabled", 0, 0, windowDays, null,
                "AI Conversation is currently disabled.");

        if (string.IsNullOrWhiteSpace(userId))
            return new ConversationEntitlement(false, "anonymous", 0, 0, windowDays, null,
                "Sign in to practise with the AI partner.");

        var activeSub = await db.Subscriptions
            .Where(s => s.UserId == userId && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial))
            .FirstOrDefaultAsync(ct);
        if (activeSub is not null)
            return new ConversationEntitlement(true,
                activeSub.Status == SubscriptionStatus.Trial ? "trial" : "paid",
                int.MaxValue, int.MaxValue, windowDays, null,
                "Active subscription — unlimited practice.");

        if (opts.FreeTierSessionsLimit < 0)
            return new ConversationEntitlement(true, "free", int.MaxValue, int.MaxValue,
                windowDays, null, "Free tier is unlimited.");

        var now = DateTimeOffset.UtcNow;
        var windowStart = now - TimeSpan.FromDays(windowDays);
        var sessions = await db.ConversationSessions
            .Where(s => s.UserId == userId && s.CreatedAt >= windowStart)
            .OrderBy(s => s.CreatedAt)
            .Select(s => s.CreatedAt).ToListAsync(ct);

        var remaining = Math.Max(0, opts.FreeTierSessionsLimit - sessions.Count);
        var earliest = sessions.Count > 0 ? (DateTimeOffset?)sessions[0] : null;
        var resetAt = earliest.HasValue ? earliest.Value + TimeSpan.FromDays(windowDays) : (DateTimeOffset?)null;

        if (remaining <= 0)
            return new ConversationEntitlement(false, "free", 0, opts.FreeTierSessionsLimit,
                windowDays, resetAt,
                $"Free tier allows {opts.FreeTierSessionsLimit} sessions every {windowDays} days. Upgrade for unlimited practice.");

        return new ConversationEntitlement(true, "free", remaining, opts.FreeTierSessionsLimit,
            windowDays, resetAt,
            $"{remaining} of {opts.FreeTierSessionsLimit} free sessions remaining.");
    }
}
