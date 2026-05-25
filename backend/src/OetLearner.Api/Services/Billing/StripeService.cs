using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using Stripe;
using Stripe.Checkout;

namespace OetLearner.Api.Services.Billing;

public sealed class StripeService : IStripeService
{
    private readonly StripeBillingOptions _opts;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StripeService> _logger;

    public StripeService(
        IOptions<BillingOptions> billingOptions,
        IServiceScopeFactory scopeFactory,
        ILogger<StripeService> logger)
    {
        _opts = billingOptions.Value.Stripe;
        _scopeFactory = scopeFactory;
        _logger = logger;

        if (!string.IsNullOrWhiteSpace(_opts.SecretKey))
            StripeConfiguration.ApiKey = _opts.SecretKey;
    }

    public async Task<string> EnsureCustomerAsync(string userId, string email, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        var user = await db.ApplicationUserAccounts
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
            throw new InvalidOperationException($"User {userId} not found.");

        if (!string.IsNullOrWhiteSpace(user.StripeCustomerId))
            return user.StripeCustomerId;

        if (string.IsNullOrWhiteSpace(_opts.SecretKey))
        {
            _logger.LogWarning("Stripe SecretKey not configured — returning sandbox customer ID.");
            return $"cus_sandbox_{userId}";
        }

        var service = new CustomerService();
        var customer = await service.CreateAsync(new CustomerCreateOptions
        {
            Email = email,
            Metadata = new Dictionary<string, string> { ["userId"] = userId }
        }, cancellationToken: ct);

        user.StripeCustomerId = customer.Id;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return customer.Id;
    }

    public async Task<(string SessionId, string Url)> CreateCheckoutSessionAsync(
        CreateCheckoutSessionRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opts.SecretKey))
        {
            _logger.LogWarning("Stripe SecretKey not configured — returning sandbox checkout session.");
            return ($"cs_sandbox_{Guid.NewGuid():N}", $"{request.SuccessUrl}?session_id=sandbox");
        }

        var lineItems = request.LineItems.Select(li => new SessionLineItemOptions
        {
            Price = li.StripePriceId,
            Quantity = li.Quantity
        }).ToList();

        var options = new SessionCreateOptions
        {
            Customer = request.StripeCustomerId,
            Mode = request.Mode,
            LineItems = lineItems,
            SuccessUrl = request.SuccessUrl,
            CancelUrl = request.CancelUrl,
            AutomaticTax = new SessionAutomaticTaxOptions { Enabled = request.AutomaticTax },
            Currency = request.Currency,
            Metadata = new Dictionary<string, string>
            {
                ["userId"] = request.UserId,
                ["userEmail"] = request.UserEmail
            }
        };

        if (!string.IsNullOrWhiteSpace(request.PromotionCodeId))
            options.Discounts = [new SessionDiscountOptions { PromotionCode = request.PromotionCodeId }];

        var requestOptions = request.IdempotencyKey is not null
            ? new RequestOptions { IdempotencyKey = request.IdempotencyKey }
            : null;

        var service = new SessionService();
        var session = await service.CreateAsync(options, requestOptions, ct);
        return (session.Id, session.Url);
    }

    public async Task<Session> RetrieveCheckoutSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var service = new SessionService();
        return await service.GetAsync(sessionId, cancellationToken: ct);
    }

    public async Task<string> CreatePortalSessionAsync(
        string stripeCustomerId, string returnUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opts.SecretKey))
            return returnUrl;

        var service = new Stripe.BillingPortal.SessionService();
        var session = await service.CreateAsync(new Stripe.BillingPortal.SessionCreateOptions
        {
            Customer = stripeCustomerId,
            ReturnUrl = returnUrl
        }, cancellationToken: ct);

        return session.Url;
    }

    public async Task<string> CreateRefundAsync(
        string paymentIntentId, long? amountCents, string? reason, CancellationToken ct = default)
    {
        // Use fully-qualified name to avoid collision with the local OetLearner RefundService.
        var service = new Stripe.RefundService();
        var refund = await service.CreateAsync(new RefundCreateOptions
        {
            PaymentIntent = paymentIntentId,
            Amount = amountCents,
            Reason = reason
        }, cancellationToken: ct);
        return refund.Id;
    }

    public Event ConstructWebhookEvent(string requestBody, string signatureHeader, string webhookSecret)
        => EventUtility.ConstructEvent(requestBody, signatureHeader, webhookSecret);

    public async Task<Stripe.Subscription> RetrieveSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
    {
        var service = new SubscriptionService();
        return await service.GetAsync(subscriptionId, cancellationToken: ct);
    }

    public async Task CancelSubscriptionAsync(
        string subscriptionId, bool cancelAtPeriodEnd = true, CancellationToken ct = default)
    {
        var service = new SubscriptionService();
        if (cancelAtPeriodEnd)
        {
            await service.UpdateAsync(subscriptionId,
                new SubscriptionUpdateOptions { CancelAtPeriodEnd = true }, cancellationToken: ct);
        }
        else
        {
            await service.CancelAsync(subscriptionId, cancellationToken: ct);
        }
    }

    public async Task UpdateSubscriptionAsync(
        string subscriptionId, string newPriceId, CancellationToken ct = default)
    {
        var service = new SubscriptionService();
        var sub = await service.GetAsync(subscriptionId, cancellationToken: ct);
        var itemId = sub.Items.Data.FirstOrDefault()?.Id
            ?? throw new InvalidOperationException("Subscription has no items.");

        await service.UpdateAsync(subscriptionId, new SubscriptionUpdateOptions
        {
            Items = [new SubscriptionItemOptions { Id = itemId, Price = newPriceId }],
            ProrationBehavior = "create_prorations"
        }, cancellationToken: ct);
    }

    public async Task PauseSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
    {
        var service = new SubscriptionService();
        await service.UpdateAsync(subscriptionId, new SubscriptionUpdateOptions
        {
            PauseCollection = new SubscriptionPauseCollectionOptions { Behavior = "void" }
        }, cancellationToken: ct);
    }

    public async Task ResumeSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
    {
        // Clearing pause_collection requires sending an explicit empty-string to the Stripe API.
        // PauseCollection is AnyOf<string, SubscriptionPauseCollectionOptions> in Stripe.net v47,
        // so assigning "" serialises as the empty-string sentinel that Stripe uses for removal.
        var service = new SubscriptionService();
        AnyOf<string, SubscriptionPauseCollectionOptions> clearPause = "";
        await service.UpdateAsync(subscriptionId, new SubscriptionUpdateOptions
        {
            PauseCollection = clearPause
        }, cancellationToken: ct);
    }

    public async Task<IEnumerable<Invoice>> ListInvoicesAsync(
        string stripeCustomerId, int limit = 24, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opts.SecretKey))
            return [];

        var service = new InvoiceService();
        var invoices = await service.ListAsync(new InvoiceListOptions
        {
            Customer = stripeCustomerId,
            Limit = limit
        }, cancellationToken: ct);
        return invoices.Data;
    }

    public async Task<string> CreateCouponAsync(CreateStripeCouponRequest request, CancellationToken ct = default)
    {
        var service = new CouponService();
        var coupon = await service.CreateAsync(new CouponCreateOptions
        {
            Name = request.Name,
            PercentOff = request.PercentOff,
            AmountOff = request.AmountOff,
            Currency = request.Currency,
            Duration = request.Duration,
            DurationInMonths = request.DurationInMonths
        }, cancellationToken: ct);
        return coupon.Id;
    }

    public async Task<string> CreatePromotionCodeAsync(
        string couponId, string code, CancellationToken ct = default)
    {
        var service = new PromotionCodeService();
        var promo = await service.CreateAsync(new PromotionCodeCreateOptions
        {
            Coupon = couponId,
            Code = code
        }, cancellationToken: ct);
        return promo.Id;
    }
}
