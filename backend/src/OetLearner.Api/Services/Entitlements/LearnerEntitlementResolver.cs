using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Entitlements;

public static class LearnerEntitlementResources
{
    public const string AiQuota = "ai-quota";
    public const string Content = "content";
    public const string Conversation = "conversation";
    public const string Grammar = "grammar";
    public const string Media = "media";
    public const string Pronunciation = "pronunciation";
    public const string Vocabulary = "vocabulary";
}

public enum LearnerEntitlementTier
{
    Anonymous,
    Free,
    Paid,
    Trial,
    SponsorSeat
}

public enum LearnerEntitlementSource
{
    Anonymous,
    Free,
    DirectActiveSubscription,
    DirectTrialSubscription,
    ActiveAddOn,
    CurrentFreeze,
    ActiveSponsorship,
    ConsentedSponsorLink
}

public interface ILearnerEntitlementResolver
{
    Task<LearnerEntitlementResolution> ResolveAsync(string? userId, string resource, CancellationToken ct);
}

public sealed record DirectSubscriptionEntitlement(
    string Id,
    string PlanId,
    SubscriptionStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset NextRenewalAt);

public sealed record ActiveAddOnEntitlement(
    string SubscriptionItemId,
    string SubscriptionId,
    string ItemCode,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt);

public sealed record CurrentFreezeEntitlement(
    string Id,
    FreezeStatus Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt);

public sealed record SponsorSeatEntitlement(
    LearnerEntitlementSource Source,
    string EvidenceId,
    string? SponsorUserId,
    string? SponsorAccountId,
    DateTimeOffset GrantedAt);

public sealed record LearnerEntitlementResolution(
    string? UserId,
    string Resource,
    LearnerEntitlementTier Tier,
    IReadOnlyCollection<LearnerEntitlementSource> Sources,
    DirectSubscriptionEntitlement? DirectSubscription,
    IReadOnlyList<ActiveAddOnEntitlement> ActiveAddOns,
    CurrentFreezeEntitlement? CurrentFreeze,
    SponsorSeatEntitlement? SponsorSeat)
{
    public bool IsAnonymous => Tier == LearnerEntitlementTier.Anonymous;
    public bool HasDirectActiveSubscription => DirectSubscription?.Status == SubscriptionStatus.Active;
    public bool HasDirectTrialSubscription => DirectSubscription?.Status == SubscriptionStatus.Trial;
    public bool HasActiveAddOn => ActiveAddOns.Count > 0;
    public bool HasCurrentFreeze => CurrentFreeze is not null;
    public bool HasSponsorSeat => SponsorSeat is not null;
    public bool HasPaidOrSponsoredAccess => HasDirectActiveSubscription || HasDirectTrialSubscription || HasSponsorSeat;
}

public sealed class LearnerEntitlementResolver(LearnerDbContext db) : ILearnerEntitlementResolver
{
    public async Task<LearnerEntitlementResolution> ResolveAsync(string? userId, string resource, CancellationToken ct)
    {
        var normalizedResource = string.IsNullOrWhiteSpace(resource)
            ? "unknown"
            : resource.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(userId))
        {
            return new LearnerEntitlementResolution(
                UserId: null,
                Resource: normalizedResource,
                Tier: LearnerEntitlementTier.Anonymous,
                Sources: new[] { LearnerEntitlementSource.Anonymous },
                DirectSubscription: null,
                ActiveAddOns: Array.Empty<ActiveAddOnEntitlement>(),
                CurrentFreeze: null,
                SponsorSeat: null);
        }

        var normalizedUserId = userId.Trim();
        var now = DateTimeOffset.UtcNow;

        var directSubscription = await db.Subscriptions
            .AsNoTracking()
            .Where(subscription => subscription.UserId == normalizedUserId
                && (subscription.Status == SubscriptionStatus.Active || subscription.Status == SubscriptionStatus.Trial))
            .OrderByDescending(subscription => subscription.Status == SubscriptionStatus.Active)
            .ThenByDescending(subscription => subscription.StartedAt)
            .Select(subscription => new DirectSubscriptionEntitlement(
                subscription.Id,
                subscription.PlanId,
                subscription.Status,
                subscription.StartedAt,
                subscription.NextRenewalAt))
            .FirstOrDefaultAsync(ct);

        var activeAddOns = await (
                from item in db.SubscriptionItems.AsNoTracking()
                join subscription in db.Subscriptions.AsNoTracking()
                    on item.SubscriptionId equals subscription.Id
                where subscription.UserId == normalizedUserId
                    && item.ItemType == "addon"
                    && item.Status == SubscriptionItemStatus.Active
                    && item.StartsAt <= now
                    && (item.EndsAt == null || item.EndsAt > now)
                orderby item.StartsAt descending
                select new ActiveAddOnEntitlement(
                    item.Id,
                    item.SubscriptionId,
                    item.ItemCode,
                    item.StartsAt,
                    item.EndsAt))
            .ToListAsync(ct);

        var currentFreeze = await db.AccountFreezeRecords
            .AsNoTracking()
            .Where(freeze => freeze.UserId == normalizedUserId
                && freeze.IsCurrent
                && freeze.Status == FreezeStatus.Active
                && (freeze.StartedAt == null || freeze.StartedAt <= now)
                && (freeze.EndedAt == null || freeze.EndedAt > now))
            .OrderByDescending(freeze => freeze.StartedAt ?? freeze.RequestedAt)
            .Select(freeze => new CurrentFreezeEntitlement(
                freeze.Id,
                freeze.Status,
                freeze.StartedAt,
                freeze.EndedAt))
            .FirstOrDefaultAsync(ct);

        var sponsorSeat = await ResolveSponsorSeatAsync(normalizedUserId, ct);
        var sources = BuildSources(directSubscription, activeAddOns, currentFreeze, sponsorSeat);

        return new LearnerEntitlementResolution(
            UserId: normalizedUserId,
            Resource: normalizedResource,
            Tier: ResolveTier(directSubscription, sponsorSeat),
            Sources: sources,
            DirectSubscription: directSubscription,
            ActiveAddOns: activeAddOns,
            CurrentFreeze: currentFreeze,
            SponsorSeat: sponsorSeat);
    }

    private async Task<SponsorSeatEntitlement?> ResolveSponsorSeatAsync(string userId, CancellationToken ct)
    {
        var activeSponsorship = await db.Sponsorships
            .AsNoTracking()
            .Where(sponsorship => sponsorship.LearnerUserId == userId
                && sponsorship.Status == "Active")
            .OrderByDescending(sponsorship => sponsorship.CreatedAt)
            .Select(sponsorship => new SponsorSeatEntitlement(
                LearnerEntitlementSource.ActiveSponsorship,
                sponsorship.Id.ToString(),
                sponsorship.SponsorUserId,
                null,
                sponsorship.CreatedAt))
            .FirstOrDefaultAsync(ct);

        if (activeSponsorship is not null)
        {
            return activeSponsorship;
        }

        return await (
                from link in db.SponsorLearnerLinks.AsNoTracking()
                join sponsor in db.SponsorAccounts.AsNoTracking()
                    on link.SponsorId equals sponsor.Id
                where link.LearnerId == userId
                    && link.LearnerConsented
                    && (sponsor.Status == "active" || sponsor.Status == "Active")
                orderby (link.ConsentedAt ?? link.LinkedAt) descending
                select new SponsorSeatEntitlement(
                    LearnerEntitlementSource.ConsentedSponsorLink,
                    link.Id.ToString(),
                    sponsor.AuthAccountId,
                    sponsor.Id,
                    link.ConsentedAt ?? link.LinkedAt))
            .FirstOrDefaultAsync(ct);
    }

    private static LearnerEntitlementTier ResolveTier(
        DirectSubscriptionEntitlement? directSubscription,
        SponsorSeatEntitlement? sponsorSeat)
    {
        if (directSubscription?.Status == SubscriptionStatus.Active)
        {
            return LearnerEntitlementTier.Paid;
        }

        if (directSubscription?.Status == SubscriptionStatus.Trial)
        {
            return LearnerEntitlementTier.Trial;
        }

        return sponsorSeat is not null
            ? LearnerEntitlementTier.SponsorSeat
            : LearnerEntitlementTier.Free;
    }

    private static IReadOnlyCollection<LearnerEntitlementSource> BuildSources(
        DirectSubscriptionEntitlement? directSubscription,
        IReadOnlyList<ActiveAddOnEntitlement> activeAddOns,
        CurrentFreezeEntitlement? currentFreeze,
        SponsorSeatEntitlement? sponsorSeat)
    {
        var sources = new List<LearnerEntitlementSource>();

        if (directSubscription?.Status == SubscriptionStatus.Active)
        {
            sources.Add(LearnerEntitlementSource.DirectActiveSubscription);
        }
        else if (directSubscription?.Status == SubscriptionStatus.Trial)
        {
            sources.Add(LearnerEntitlementSource.DirectTrialSubscription);
        }

        if (activeAddOns.Count > 0)
        {
            sources.Add(LearnerEntitlementSource.ActiveAddOn);
        }

        if (currentFreeze is not null)
        {
            sources.Add(LearnerEntitlementSource.CurrentFreeze);
        }

        if (sponsorSeat is not null)
        {
            sources.Add(sponsorSeat.Source);
        }

        if (sources.Count == 0)
        {
            sources.Add(LearnerEntitlementSource.Free);
        }

        return sources;
    }
}