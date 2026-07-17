using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain.Billing;

namespace OetWithDrHesham.Api.Services.Billing;

public sealed class SubscriptionService : ISubscriptionService
{
    private readonly LearnerDbContext _db;
    private readonly IStripeService _stripe;

    public SubscriptionService(LearnerDbContext db, IStripeService stripe)
    {
        _db = db;
        _stripe = stripe;
    }

    public async Task<CustomerSubscriptionDto?> GetActiveSubscriptionAsync(string userId, CancellationToken ct = default)
    {
        var sub = await _db.CustomerSubscriptions
            .Where(s => s.UserId == userId && (s.Status == "active" || s.Status == "trialing" || s.Status == "past_due"))
            .OrderByDescending(s => s.CreatedAt)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

        return sub is null ? null : MapDto(sub);
    }

    public async Task<CustomerSubscriptionDto> CreateFromStripeAsync(
        string userId, string stripeSubscriptionId, CancellationToken ct = default)
    {
        var existing = await _db.CustomerSubscriptions
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubscriptionId, ct);

        if (existing is not null) return MapDto(existing);

        var stripeSub = await _stripe.RetrieveSubscriptionAsync(stripeSubscriptionId, ct);

        var sub = new CustomerSubscription
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            StripeSubscriptionId = stripeSubscriptionId,
            StripePriceId = stripeSub.Items.Data.FirstOrDefault()?.Price?.Id ?? string.Empty,
            Status = stripeSub.Status,
            CurrentPeriodStart = new DateTimeOffset(stripeSub.CurrentPeriodStart, TimeSpan.Zero),
            CurrentPeriodEnd = new DateTimeOffset(stripeSub.CurrentPeriodEnd, TimeSpan.Zero),
            CancelAtPeriodEnd = stripeSub.CancelAtPeriodEnd,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _db.CustomerSubscriptions.Add(sub);
        await _db.SaveChangesAsync(ct);
        return MapDto(sub);
    }

    public async Task SyncFromStripeAsync(string stripeSubscriptionId, CancellationToken ct = default)
    {
        var sub = await _db.CustomerSubscriptions
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubscriptionId, ct);

        if (sub is null) return;

        var stripeSub = await _stripe.RetrieveSubscriptionAsync(stripeSubscriptionId, ct);
        sub.Status = stripeSub.Status;
        sub.StripePriceId = stripeSub.Items.Data.FirstOrDefault()?.Price?.Id ?? sub.StripePriceId;
        sub.CurrentPeriodStart = new DateTimeOffset(stripeSub.CurrentPeriodStart, TimeSpan.Zero);
        sub.CurrentPeriodEnd = new DateTimeOffset(stripeSub.CurrentPeriodEnd, TimeSpan.Zero);
        sub.CancelAtPeriodEnd = stripeSub.CancelAtPeriodEnd;
        sub.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    public Task CancelAsync(string userId, bool cancelAtPeriodEnd = true, CancellationToken ct = default)
        => CancelInternalAsync(userId, cancelAtPeriodEnd, reason: null, ct);

    public Task CancelAsync(string userId, bool cancelAtPeriodEnd, string reason, CancellationToken ct = default)
        => CancelInternalAsync(userId, cancelAtPeriodEnd, reason, ct);

    private async Task CancelInternalAsync(string userId, bool cancelAtPeriodEnd, string? reason, CancellationToken ct)
    {
        var sub = await GetActiveSubForUserAsync(userId, ct);
        await _stripe.CancelSubscriptionAsync(sub.StripeSubscriptionId, cancelAtPeriodEnd, ct);
        sub.CancelAtPeriodEnd = cancelAtPeriodEnd;
        if (!cancelAtPeriodEnd)
        {
            sub.Status = "canceled";
            sub.CanceledAt = DateTimeOffset.UtcNow;
        }
        sub.UpdatedAt = DateTimeOffset.UtcNow;

        // Wave A5: record the cancellation reason on the billing event log so
        // analytics can attribute canceled subs to dunning-exhausted vs.
        // voluntary cancellations.
        if (!string.IsNullOrWhiteSpace(reason))
        {
            _db.BillingEvents.Add(new OetWithDrHesham.Api.Domain.BillingEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = userId,
                SubscriptionId = sub.StripeSubscriptionId,
                EventType = "customer.subscription.deleted",
                EntityType = "subscription",
                EntityId = sub.StripeSubscriptionId,
                OccurredAt = DateTimeOffset.UtcNow,
                PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { reason })
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    public Task ChangePlanAsync(string userId, string newStripePriceId, CancellationToken ct = default)
        => ChangePlanAsync(userId, newStripePriceId, prorate: true, ct);

    public async Task ChangePlanAsync(string userId, string newStripePriceId, bool prorate, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newStripePriceId))
            throw new ArgumentException("newStripePriceId is required.", nameof(newStripePriceId));

        var sub = await GetActiveSubForUserAsync(userId, ct);
        var previousPriceId = sub.StripePriceId;
        await _stripe.UpdateSubscriptionAsync(sub.StripeSubscriptionId, newStripePriceId, prorate, ct);
        sub.StripePriceId = newStripePriceId;
        sub.UpdatedAt = DateTimeOffset.UtcNow;

        _db.BillingEvents.Add(new OetWithDrHesham.Api.Domain.BillingEvent
        {
            Id = $"bill-evt-{Guid.NewGuid():N}",
            UserId = userId,
            SubscriptionId = sub.StripeSubscriptionId,
            EventType = "subscription.plan_changed",
            EntityType = "subscription",
            EntityId = sub.StripeSubscriptionId,
            OccurredAt = DateTimeOffset.UtcNow,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                previousPriceId,
                newStripePriceId,
                prorate
            })
        });

        await _db.SaveChangesAsync(ct);
    }

    public Task PauseAsync(string userId, CancellationToken ct = default)
        => PauseAsync(userId, pauseUntil: null, ct);

    public async Task PauseAsync(string userId, DateTimeOffset? pauseUntil, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        if (pauseUntil.HasValue)
        {
            // Policy: pause window is capped at 90 days. Clamp silently to keep
            // the API call idempotent — callers can verify the returned DTO if
            // they care about the actual resume timestamp.
            var max = now.AddDays(90);
            if (pauseUntil.Value > max)
            {
                pauseUntil = max;
            }
            if (pauseUntil.Value <= now)
            {
                throw new ArgumentException("pauseUntil must be in the future.", nameof(pauseUntil));
            }
        }

        var sub = await GetActiveSubForUserAsync(userId, ct);
        await _stripe.PauseSubscriptionAsync(sub.StripeSubscriptionId, pauseUntil, ct);
        sub.Status = "paused";
        sub.PausedAt = now;
        sub.UpdatedAt = now;

        _db.BillingEvents.Add(new OetWithDrHesham.Api.Domain.BillingEvent
        {
            Id = $"bill-evt-{Guid.NewGuid():N}",
            UserId = userId,
            SubscriptionId = sub.StripeSubscriptionId,
            EventType = "subscription.paused",
            EntityType = "subscription",
            EntityId = sub.StripeSubscriptionId,
            OccurredAt = now,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                pauseUntil = pauseUntil?.ToString("o")
            })
        });

        await _db.SaveChangesAsync(ct);
    }

    public async Task ResumeAsync(string userId, CancellationToken ct = default)
    {
        var sub = await _db.CustomerSubscriptions
            .Where(s => s.UserId == userId && s.Status == "paused")
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("No paused subscription found.");
        await _stripe.ResumeSubscriptionAsync(sub.StripeSubscriptionId, ct);
        var now = DateTimeOffset.UtcNow;
        sub.Status = "active";
        sub.PausedAt = null;
        sub.UpdatedAt = now;

        _db.BillingEvents.Add(new OetWithDrHesham.Api.Domain.BillingEvent
        {
            Id = $"bill-evt-{Guid.NewGuid():N}",
            UserId = userId,
            SubscriptionId = sub.StripeSubscriptionId,
            EventType = "subscription.resumed",
            EntityType = "subscription",
            EntityId = sub.StripeSubscriptionId,
            OccurredAt = now,
            PayloadJson = "{}"
        });

        await _db.SaveChangesAsync(ct);
    }

    public async Task ApplyDiscountAsync(string userId, string? couponId, CancellationToken ct = default)
    {
        var sub = await GetActiveSubForUserAsync(userId, ct);
        await _stripe.ApplyCouponToSubscriptionAsync(sub.StripeSubscriptionId, couponId, ct);
        var now = DateTimeOffset.UtcNow;
        sub.UpdatedAt = now;

        _db.BillingEvents.Add(new OetWithDrHesham.Api.Domain.BillingEvent
        {
            Id = $"bill-evt-{Guid.NewGuid():N}",
            UserId = userId,
            SubscriptionId = sub.StripeSubscriptionId,
            EventType = string.IsNullOrEmpty(couponId)
                ? "subscription.discount_cleared"
                : "subscription.discount_applied",
            EntityType = "subscription",
            EntityId = sub.StripeSubscriptionId,
            OccurredAt = now,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { couponId })
        });

        await _db.SaveChangesAsync(ct);
    }

    public async Task<IEnumerable<SubscriptionInvoiceDto>> ListInvoicesAsync(
        string userId, CancellationToken ct = default)
    {
        var user = await _db.ApplicationUserAccounts
            .Where(u => u.Id == userId)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

        if (user?.StripeCustomerId is null) return [];

        var invoices = await _stripe.ListInvoicesAsync(user.StripeCustomerId, 24, ct);
        return invoices.Select(inv => new SubscriptionInvoiceDto(
            inv.Id,
            inv.Status ?? "unknown",
            inv.AmountDue,
            inv.Currency,
            new DateTimeOffset(inv.Created, TimeSpan.Zero),
            inv.InvoicePdf,
            inv.HostedInvoiceUrl
        ));
    }

    public async Task<string> CreatePortalSessionAsync(string userId, string returnUrl, CancellationToken ct = default)
    {
        var user = await _db.ApplicationUserAccounts
            .Where(u => u.Id == userId)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(user?.StripeCustomerId))
            throw new InvalidOperationException("User has no Stripe customer ID.");

        return await _stripe.CreatePortalSessionAsync(user.StripeCustomerId, returnUrl, ct);
    }

    private async Task<CustomerSubscription> GetActiveSubForUserAsync(string userId, CancellationToken ct)
        => await _db.CustomerSubscriptions
            .Where(s => s.UserId == userId && (s.Status == "active" || s.Status == "trialing"))
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("No active subscription found for this user.");

    private static CustomerSubscriptionDto MapDto(CustomerSubscription s) => new(
        s.Id, s.UserId, s.StripeSubscriptionId, s.StripePriceId,
        s.Status, s.CurrentPeriodStart, s.CurrentPeriodEnd,
        s.CancelAtPeriodEnd, s.CanceledAt, s.PausedAt);
}
