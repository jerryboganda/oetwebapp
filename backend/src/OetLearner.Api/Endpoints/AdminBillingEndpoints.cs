using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Domain.Billing;
using OetLearner.Api.Services.Billing;
using Microsoft.Extensions.Options;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Wave B4 — admin-only billing surface. Mounted at <c>/v1/admin/billing</c>
/// with finance / ops scopes. Provides:
/// <list type="bullet">
///   <item>Revenue / MRR / churn / LTV roll-ups (read-only).</item>
///   <item>Refund issuance + list (RefundService-backed).</item>
///   <item>Product + coupon CRUD against the local DB. The Stripe side is
///         reconciled separately by the StripeProductSeeder CLI — these
///         endpoints intentionally do not push to Stripe.</item>
///   <item>Stripe Tax registration list/create.</item>
/// </list>
/// </summary>
public static class AdminBillingEndpoints
{
    public static IEndpointRouteBuilder MapAdminBillingEndpoints(this IEndpointRouteBuilder app)
    {
        var billing = app.MapGroup("/v1/admin/billing").RequireAuthorization("AdminBillingRead");

        // ── Analytics ──────────────────────────────────────────────────────────
        billing.MapGet("/analytics", GetAnalytics);
        billing.MapGet("/revenue", GetRevenue);
        billing.MapGet("/mrr", GetMrr);
        billing.MapGet("/churn", GetChurn);
        billing.MapGet("/ltv", GetLtv);

        // ── Refunds ────────────────────────────────────────────────────────────
        // Mounted directly at /v1/admin/refunds per the §10 contract; reads
        // require AdminBillingRead, writes require AdminBillingRefundWrite.
        var refunds = app.MapGroup("/v1/admin/refunds");
        refunds.MapGet("/", ListRefunds).RequireAuthorization("AdminBillingRead");
        refunds.MapPost("/", IssueRefund).WithAdminWrite("AdminBillingRefundWrite");

        // ── Products (DB-only — Stripe sync is the seeder's job) ───────────────
        var products = app.MapGroup("/v1/admin/products");
        products.MapGet("/", ListProducts).RequireAuthorization("AdminBillingRead");
        products.MapPost("/", CreateProduct).WithAdminWrite("AdminBillingCatalogWrite");
        products.MapPatch("/{productCode}", PatchProduct).WithAdminWrite("AdminBillingCatalogWrite");
        products.MapDelete("/{productCode}", ArchiveProduct).WithAdminWrite("AdminBillingCatalogWrite");

        // ── Coupons ────────────────────────────────────────────────────────────
        var coupons = app.MapGroup("/v1/admin/coupons");
        coupons.MapGet("/", ListCoupons).RequireAuthorization("AdminBillingRead");
        coupons.MapPost("/", CreateCoupon).WithAdminWrite("AdminBillingCatalogWrite");
        coupons.MapPatch("/{code}", PatchCoupon).WithAdminWrite("AdminBillingCatalogWrite");
        coupons.MapDelete("/{code}", ArchiveCoupon).WithAdminWrite("AdminBillingCatalogWrite");

        // ── Stripe Tax registrations ───────────────────────────────────────────
        var tax = billing.MapGroup("/stripe-tax/registrations");
        tax.MapGet("/", ListTaxRegistrations);
        tax.MapPost("/", CreateTaxRegistration).WithAdminWrite("AdminBillingCatalogWrite");

        return app;
    }

    // ─────────────────────── Analytics ───────────────────────

    private static async Task<Ok<AdminBillingAnalyticsResponse>> GetAnalytics(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var fromTs = from ?? DateTimeOffset.UtcNow.AddDays(-30);
        var toTs = to ?? DateTimeOffset.UtcNow;
        if (toTs < fromTs)
        {
            (fromTs, toTs) = (toTs, fromTs);
        }

        var mrr = (await GetMrr(db, ct)).Value!;
        var churn = (await GetChurn(null, db, ct)).Value!;
        var ltv = (await GetLtv(null, db, ct)).Value!;

        return TypedResults.Ok(new AdminBillingAnalyticsResponse(
            Mrr: [new AdminBillingAnalyticsSeriesPoint(toTs, decimal.Round(mrr.MrrCents / 100m, 2))],
            ChurnRate: [new AdminBillingAnalyticsSeriesPoint(toTs, churn.ChurnPercent)],
            Ltv: [new AdminBillingAnalyticsSeriesPoint(toTs, ltv.AverageLtv)],
            Currency: "AUD",
            Available: true));
    }

    private static async Task<Ok<RevenueResponse>> GetRevenue(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var fromTs = from ?? DateTimeOffset.UtcNow.AddDays(-30);
        var toTs = to ?? DateTimeOffset.UtcNow;
        if (toTs < fromTs)
        {
            (fromTs, toTs) = (toTs, fromTs);
        }

        var grossQuery = db.PaymentTransactions
            .Where(p => p.CreatedAt >= fromTs && p.CreatedAt <= toTs
                        && (p.Status == "completed" || p.Status == "refunded"));
        var refundedQuery = db.PaymentTransactions
            .Where(p => p.CreatedAt >= fromTs && p.CreatedAt <= toTs && p.Status == "refunded");

        var gross = await grossQuery.SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
        var refunded = await refundedQuery.SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
        var net = gross - refunded;

        // BillingEvent ledger sum for "revenue"-typed events; used for parity
        // checks against the metrics rollup.
        var ledger = await db.BillingEvents
            .Where(e => e.OccurredAt >= fromTs && e.OccurredAt <= toTs
                        && (e.EventType == "payment_completed" || e.EventType == "subscription_payment_completed"))
            .CountAsync(ct);

        return TypedResults.Ok(new RevenueResponse(
            fromTs, toTs, gross, refunded, net, ledger));
    }

    private static async Task<Ok<MrrResponse>> GetMrr(
        LearnerDbContext db,
        CancellationToken ct)
    {
        // Sum monthly + annual subscriptions, normalised to monthly cents.
        // CustomerSubscription.StripePriceId carries the live price; we join
        // to BillingPrice so the rollup reflects the canonical USD amount.
        // Fall back to the legacy Subscription table when CustomerSubscription
        // rows are not yet populated (gradual roll-out from Wave A4).
        var customerSubMrr = await db.CustomerSubscriptions
            .Where(s => s.Status == "active" || s.Status == "trialing")
            .Join(db.BillingPrices.Where(p => p.IsActive),
                s => s.StripePriceId,
                p => p.StripePriceId,
                (s, p) => new { p.Amount, p.Interval })
            .ToListAsync(ct);

        decimal mrr = 0m;
        foreach (var row in customerSubMrr)
        {
            mrr += NormaliseToMonthly(row.Amount, row.Interval);
        }

        if (mrr == 0m)
        {
            // Legacy path — sum the original Subscription table while we still
            // run dual-write during Wave A4 → B4 migration.
            var legacy = await db.Subscriptions
                .Where(s => s.Status == SubscriptionStatus.Active)
                .Select(s => new { s.PriceAmount, s.Interval })
                .ToListAsync(ct);
            foreach (var row in legacy)
            {
                mrr += NormaliseToMonthly(row.PriceAmount, row.Interval);
            }
        }

        return TypedResults.Ok(new MrrResponse(
            MrrCents: (long)decimal.Round(mrr, 0),
            ArrCents: (long)decimal.Round(mrr * 12m, 0),
            ComputedAt: DateTimeOffset.UtcNow));
    }

    private static async Task<Ok<ChurnResponse>> GetChurn(
        [FromQuery] string? period,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var window = ParsePeriod(period, fallback: TimeSpan.FromDays(30));
        var windowStart = DateTimeOffset.UtcNow - window;

        var activeAtStart = await db.CustomerSubscriptions
            .Where(s => s.CreatedAt <= windowStart
                        && (s.CanceledAt == null || s.CanceledAt > windowStart))
            .CountAsync(ct);
        var canceledInWindow = await db.CustomerSubscriptions
            .Where(s => s.CanceledAt != null && s.CanceledAt >= windowStart)
            .CountAsync(ct);

        if (activeAtStart == 0 && canceledInWindow == 0)
        {
            // Fall back to legacy Subscriptions for the same window.
            activeAtStart = await db.Subscriptions
                .Where(s => s.StartedAt <= windowStart
                            && (s.Status == SubscriptionStatus.Active
                                || (s.Status == SubscriptionStatus.Cancelled && s.ChangedAt > windowStart)))
                .CountAsync(ct);
            canceledInWindow = await db.Subscriptions
                .Where(s => s.Status == SubscriptionStatus.Cancelled && s.ChangedAt >= windowStart)
                .CountAsync(ct);
        }

        var pct = activeAtStart == 0
            ? 0m
            : decimal.Round((decimal)canceledInWindow / activeAtStart * 100m, 4);
        return TypedResults.Ok(new ChurnResponse(window, activeAtStart, canceledInWindow, pct));
    }

    private static async Task<Ok<LtvResponse>> GetLtv(
        [FromQuery] string? segment,
        LearnerDbContext db,
        CancellationToken ct)
    {
        // Rough LTV: sum of all completed payments per user, then average.
        // Segment filter restricts to a transaction.ProductType slice.
        var payments = db.PaymentTransactions
            .Where(p => p.Status == "completed");
        if (!string.IsNullOrWhiteSpace(segment))
        {
            payments = payments.Where(p => p.ProductType == segment);
        }

        var perUser = await payments
            .GroupBy(p => p.LearnerUserId)
            .Select(g => g.Sum(p => (decimal?)p.Amount) ?? 0m)
            .ToListAsync(ct);

        if (perUser.Count == 0)
        {
            return TypedResults.Ok(new LtvResponse(segment, 0, 0m, 0m));
        }
        var avg = decimal.Round(perUser.Average(), 4);
        var total = perUser.Sum();
        return TypedResults.Ok(new LtvResponse(segment, perUser.Count, avg, total));
    }

    private static decimal NormaliseToMonthly(decimal amount, string? interval)
    {
        return (interval ?? string.Empty).ToLowerInvariant() switch
        {
            "year" or "annual" or "yearly" => amount / 12m,
            "week" or "weekly" => amount * 4.345m,
            "day" or "daily" => amount * 30m,
            // month, monthly, null, "one_time" — already monthly or excluded.
            _ => amount
        };
    }

    private static TimeSpan ParsePeriod(string? period, TimeSpan fallback)
    {
        if (string.IsNullOrWhiteSpace(period)) return fallback;
        var trimmed = period.Trim().ToLowerInvariant();
        var digits = new string(trimmed.TakeWhile(char.IsDigit).ToArray());
        if (digits.Length == 0 || !int.TryParse(digits, out var value)) return fallback;
        var unit = trimmed[digits.Length..];
        return unit switch
        {
            "d" or "day" or "days" => TimeSpan.FromDays(value),
            "w" or "wk" or "week" or "weeks" => TimeSpan.FromDays(value * 7),
            "m" or "mo" or "month" or "months" => TimeSpan.FromDays(value * 30),
            "h" or "hr" or "hour" or "hours" => TimeSpan.FromHours(value),
            _ => fallback
        };
    }

    // ─────────────────────── Refunds ───────────────────────

    private static async Task<Results<Ok<RefundResponse>, ProblemHttpResult>> IssueRefund(
        [FromBody] IssueRefundRequest request,
        RefundService refundService,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CheckoutSessionId))
        {
            return TypedResults.Problem(statusCode: 400, title: "checkoutSessionId is required.");
        }
        if (request.AmountCents <= 0)
        {
            return TypedResults.Problem(statusCode: 400, title: "amountCents must be positive.");
        }

        var amount = decimal.Divide(request.AmountCents, 100m);
        var idempotency = request.IdempotencyKey ?? $"admin-refund:{request.CheckoutSessionId}:{request.AmountCents}";

        try
        {
            var result = await refundService.IssueRefundAsync(new RefundRequest(
                PaymentTransactionId: request.CheckoutSessionId,
                Amount: amount,
                Reason: request.Reason ?? "requested_by_customer",
                IdempotencyKey: idempotency,
                AdminId: user.FindFirstValue(ClaimTypes.NameIdentifier),
                AdminName: user.FindFirstValue(ClaimTypes.Name),
                AdminNote: request.AdminNote), ct);
            return TypedResults.Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.Problem(statusCode: 400, title: ex.Message);
        }
    }

    private static async Task<Ok<IReadOnlyList<RefundSummary>>> ListRefunds(
        [FromQuery] string? status,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var query = db.OrderRefunds.AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(r => r.Status == status);
        }
        if (from.HasValue)
        {
            var f = from.Value;
            query = query.Where(r => r.CreatedAt >= f);
        }
        if (to.HasValue)
        {
            var t = to.Value;
            query = query.Where(r => r.CreatedAt <= t);
        }

        var rows = await query
            .OrderByDescending(r => r.CreatedAt)
            .Take(500)
            .Select(r => new RefundSummary(
                r.Id,
                r.PaymentTransactionId,
                r.LearnerUserId,
                r.Gateway,
                r.Status,
                r.RefundType,
                r.Amount,
                r.Currency,
                r.Reason,
                r.RequestedByAdminId,
                r.CreatedAt))
            .ToListAsync(ct);
        return TypedResults.Ok<IReadOnlyList<RefundSummary>>(rows);
    }

    // ─────────────────────── Products ───────────────────────

    private static async Task<Ok<IReadOnlyList<AdminProductDto>>> ListProducts(
        [FromQuery] bool? includeInactive,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var query = db.BillingProducts.Include(p => p.Prices).AsQueryable();
        if (includeInactive != true)
        {
            query = query.Where(p => p.IsActive);
        }
        var rows = await query
            .OrderBy(p => p.ProductType)
            .ThenBy(p => p.Name)
            .ToListAsync(ct);
        return TypedResults.Ok<IReadOnlyList<AdminProductDto>>(rows.Select(MapProduct).ToList());
    }

    private static async Task<Results<Created<AdminProductDto>, Conflict<string>, ProblemHttpResult>> CreateProduct(
        [FromBody] AdminCreateProductRequest request,
        LearnerDbContext db,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return TypedResults.Problem(statusCode: 400, title: "code is required.");
        }
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return TypedResults.Problem(statusCode: 400, title: "name is required.");
        }
        var existing = await db.BillingProducts.FirstOrDefaultAsync(p => p.Code == request.Code, ct);
        if (existing is not null)
        {
            return TypedResults.Conflict($"Product '{request.Code}' already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        var product = new BillingProduct
        {
            Id = Guid.NewGuid(),
            Code = request.Code,
            Name = request.Name,
            Description = request.Description,
            ProductType = string.IsNullOrWhiteSpace(request.ProductType) ? "addon" : request.ProductType,
            StripeProductId = request.StripeProductId,
            IsActive = true,
            MetadataJson = request.MetadataJson,
            CreatedAt = now,
            UpdatedAt = now
        };
        foreach (var p in request.Prices ?? new())
        {
            product.Prices.Add(new BillingPrice
            {
                Id = Guid.NewGuid(),
                BillingProductId = product.Id,
                Currency = p.Currency,
                Amount = p.Amount,
                Interval = p.Interval,
                IntervalCount = p.IntervalCount ?? 1,
                StripePriceId = p.StripePriceId,
                Country = p.Country,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        db.BillingProducts.Add(product);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/v1/admin/products/{product.Code}", MapProduct(product));
    }

    private static async Task<Results<Ok<AdminProductDto>, NotFound>> PatchProduct(
        string productCode,
        [FromBody] AdminPatchProductRequest request,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var product = await db.BillingProducts
            .Include(p => p.Prices)
            .FirstOrDefaultAsync(p => p.Code == productCode, ct);
        if (product is null) return TypedResults.NotFound();

        if (request.Name is not null) product.Name = request.Name;
        if (request.Description is not null) product.Description = request.Description;
        if (request.ProductType is not null) product.ProductType = request.ProductType;
        if (request.StripeProductId is not null) product.StripeProductId = request.StripeProductId;
        if (request.IsActive.HasValue) product.IsActive = request.IsActive.Value;
        if (request.MetadataJson is not null) product.MetadataJson = request.MetadataJson;
        product.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(MapProduct(product));
    }

    private static async Task<Results<NoContent, NotFound>> ArchiveProduct(
        string productCode,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var product = await db.BillingProducts.FirstOrDefaultAsync(p => p.Code == productCode, ct);
        if (product is null) return TypedResults.NotFound();
        product.IsActive = false;
        product.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static AdminProductDto MapProduct(BillingProduct p) => new(
        p.Id,
        p.Code,
        p.Name,
        p.Description,
        p.ProductType,
        p.StripeProductId,
        p.IsActive,
        p.MetadataJson,
        p.Prices.Select(pr => new AdminPriceDto(
            pr.Id,
            pr.StripePriceId,
            pr.Currency,
            pr.Amount,
            pr.Interval,
            pr.IntervalCount,
            pr.Country,
            pr.IsActive)).ToList());

    // ─────────────────────── Coupons ───────────────────────

    private static async Task<Ok<IReadOnlyList<AdminCouponDto>>> ListCoupons(
        [FromQuery] string? status,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var query = db.BillingCoupons.AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)
            && Enum.TryParse<BillingCouponStatus>(status, ignoreCase: true, out var parsed))
        {
            query = query.Where(c => c.Status == parsed);
        }
        var rows = await query
            .OrderByDescending(c => c.UpdatedAt)
            .Take(500)
            .ToListAsync(ct);
        return TypedResults.Ok<IReadOnlyList<AdminCouponDto>>(rows.Select(MapCoupon).ToList());
    }

    private static async Task<Results<Created<AdminCouponDto>, Conflict<string>, ProblemHttpResult>> CreateCoupon(
        [FromBody] AdminCreateCouponRequest request,
        LearnerDbContext db,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return TypedResults.Problem(statusCode: 400, title: "code is required.");
        }
        if (request.DiscountValue < 0)
        {
            return TypedResults.Problem(statusCode: 400, title: "discountValue must be non-negative.");
        }
        if (!TryParseEnum(request.DiscountType, BillingDiscountType.Percentage, out BillingDiscountType discountType))
        {
            return TypedResults.Problem(statusCode: 400, title: $"discountType '{request.DiscountType}' is not a valid BillingDiscountType.");
        }
        if (!TryParseEnum(request.Status, BillingCouponStatus.Active, out BillingCouponStatus status))
        {
            return TypedResults.Problem(statusCode: 400, title: $"status '{request.Status}' is not a valid BillingCouponStatus.");
        }
        var exists = await db.BillingCoupons.AnyAsync(c => c.Code == request.Code, ct);
        if (exists)
        {
            return TypedResults.Conflict($"Coupon '{request.Code}' already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        var coupon = new BillingCoupon
        {
            Id = $"coupon_{Guid.NewGuid():N}",
            Code = request.Code,
            Name = string.IsNullOrWhiteSpace(request.Name) ? request.Code : request.Name,
            Description = request.Description ?? string.Empty,
            DiscountType = discountType,
            DiscountValue = request.DiscountValue,
            Currency = request.Currency ?? "USD",
            Status = status,
            StartsAt = request.StartsAt,
            EndsAt = request.EndsAt,
            UsageLimitTotal = request.UsageLimitTotal,
            UsageLimitPerUser = request.UsageLimitPerUser,
            MinimumSubtotal = request.MinimumSubtotal,
            ApplicablePlanCodesJson = request.ApplicablePlanCodesJson ?? "[]",
            ApplicableAddOnCodesJson = request.ApplicableAddOnCodesJson ?? "[]",
            EligibleCountriesJson = request.EligibleCountriesJson ?? "[]",
            VariantMetadataJson = request.VariantMetadataJson ?? "{}",
            CouponVariant = request.CouponVariant ?? "percent_off",
            IsStackable = request.IsStackable ?? false,
            NewUsersOnly = request.NewUsersOnly ?? false,
            ExistingUsersOnly = request.ExistingUsersOnly ?? false,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.BillingCoupons.Add(coupon);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/v1/admin/coupons/{coupon.Code}", MapCoupon(coupon));
    }

    private static async Task<Results<Ok<AdminCouponDto>, NotFound, ProblemHttpResult>> PatchCoupon(
        string code,
        [FromBody] AdminPatchCouponRequest request,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var coupon = await db.BillingCoupons.FirstOrDefaultAsync(c => c.Code == code, ct);
        if (coupon is null) return TypedResults.NotFound();

        if (request.Name is not null) coupon.Name = request.Name;
        if (request.Description is not null) coupon.Description = request.Description;
        if (request.DiscountType is not null)
        {
            if (!TryParseEnum(request.DiscountType, coupon.DiscountType, out BillingDiscountType dt))
            {
                return TypedResults.Problem(statusCode: 400, title: $"discountType '{request.DiscountType}' is invalid.");
            }
            coupon.DiscountType = dt;
        }
        if (request.DiscountValue.HasValue) coupon.DiscountValue = request.DiscountValue.Value;
        if (request.Currency is not null) coupon.Currency = request.Currency;
        if (request.Status is not null)
        {
            if (!TryParseEnum(request.Status, coupon.Status, out BillingCouponStatus st))
            {
                return TypedResults.Problem(statusCode: 400, title: $"status '{request.Status}' is invalid.");
            }
            coupon.Status = st;
        }
        if (request.StartsAt.HasValue) coupon.StartsAt = request.StartsAt.Value;
        if (request.EndsAt.HasValue) coupon.EndsAt = request.EndsAt.Value;
        if (request.UsageLimitTotal.HasValue) coupon.UsageLimitTotal = request.UsageLimitTotal.Value;
        if (request.UsageLimitPerUser.HasValue) coupon.UsageLimitPerUser = request.UsageLimitPerUser.Value;
        if (request.MinimumSubtotal.HasValue) coupon.MinimumSubtotal = request.MinimumSubtotal.Value;
        if (request.ApplicablePlanCodesJson is not null) coupon.ApplicablePlanCodesJson = request.ApplicablePlanCodesJson;
        if (request.ApplicableAddOnCodesJson is not null) coupon.ApplicableAddOnCodesJson = request.ApplicableAddOnCodesJson;
        if (request.IsStackable.HasValue) coupon.IsStackable = request.IsStackable.Value;
        coupon.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(MapCoupon(coupon));
    }

    private static bool TryParseEnum<T>(string? value, T fallback, out T parsed) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            parsed = fallback;
            return true;
        }
        if (Enum.TryParse<T>(value, ignoreCase: true, out var result))
        {
            parsed = result;
            return true;
        }
        parsed = fallback;
        return false;
    }

    private static async Task<Results<NoContent, NotFound>> ArchiveCoupon(
        string code,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var coupon = await db.BillingCoupons.FirstOrDefaultAsync(c => c.Code == code, ct);
        if (coupon is null) return TypedResults.NotFound();
        coupon.Status = BillingCouponStatus.Archived;
        coupon.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static AdminCouponDto MapCoupon(BillingCoupon c) => new(
        c.Id,
        c.Code,
        c.Name,
        c.Description,
        c.DiscountType.ToString(),
        c.DiscountValue,
        c.Currency,
        c.Status.ToString(),
        c.StartsAt,
        c.EndsAt,
        c.UsageLimitTotal,
        c.UsageLimitPerUser,
        c.RedemptionCount,
        c.IsStackable,
        c.CouponVariant);

    // ─────────────────────── Stripe Tax ───────────────────────

    private static async Task<Results<Ok<TaxRegistrationListResponse>, ProblemHttpResult>> ListTaxRegistrations(
        [FromQuery] string? status,
        IOptions<BillingOptions> billingOptions,
        ILogger<TaxRegistrationsLogTag> logger,
        CancellationToken ct)
    {
        var key = billingOptions.Value.Stripe.SecretKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            return TypedResults.Ok(new TaxRegistrationListResponse(new List<TaxRegistrationDto>(), "stripe_not_configured"));
        }

        try
        {
            Stripe.StripeConfiguration.ApiKey = key;
            var service = new Stripe.Tax.RegistrationService();
            var options = new Stripe.Tax.RegistrationListOptions { Limit = 100 };
            if (!string.IsNullOrWhiteSpace(status))
            {
                options.Status = status;
            }
            var page = await service.ListAsync(options, cancellationToken: ct);
            var rows = page.Data.Select(r => new TaxRegistrationDto(
                r.Id,
                r.Country,
                r.Status,
                ToOffset(r.ActiveFrom),
                ToOffset(r.ExpiresAt),
                r.Livemode)).ToList();
            return TypedResults.Ok(new TaxRegistrationListResponse(rows, "ok"));
        }
        catch (Stripe.StripeException ex)
        {
            logger.LogWarning(ex, "Stripe Tax list failed.");
            return TypedResults.Problem(statusCode: 502, title: "Stripe Tax list failed.", detail: ex.Message);
        }
    }

    private static async Task<Results<Created<TaxRegistrationDto>, ProblemHttpResult, StatusCodeHttpResult>> CreateTaxRegistration(
        [FromBody] CreateTaxRegistrationRequest request,
        IOptions<BillingOptions> billingOptions,
        ILogger<TaxRegistrationsLogTag> logger,
        CancellationToken ct)
    {
        // Stripe.Tax.RegistrationCreateOptions requires "country_options" to be
        // populated with type-specific fields per jurisdiction. That payload
        // shape is country-dependent and routinely changes; until we wire up a
        // typed builder per (country, type), return 501 so the admin UI can
        // expose the surface without committing to a partial schema.
        //
        // TODO(B4-followup): wire country_options builders per the live
        // Stripe Tax schema (e.g. CountryOptionsUs.State, CountryOptionsGb,
        // etc.). Tracking via the same Wave B4 follow-up issue as the
        // Stripe.net SDK version bump.
        await Task.CompletedTask;
        _ = request;
        _ = billingOptions;
        _ = logger;
        return TypedResults.StatusCode(StatusCodes.Status501NotImplemented);
    }

    private sealed class TaxRegistrationsLogTag { }

    private static DateTimeOffset? ToOffset(object? source)
    {
        // Stripe.net surfaces these fields as either DateTime or DateTime?
        // depending on the SDK revision. Normalise both shapes here so the
        // endpoint compiles cleanly against a moving SDK target.
        switch (source)
        {
            case null: return null;
            case DateTime dt when dt == default: return null;
            case DateTime dt: return new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc), TimeSpan.Zero);
            case DateTimeOffset dto: return dto;
            default: return null;
        }
    }

    // ─────────────────────── DTOs ───────────────────────

    public sealed record RevenueResponse(
        DateTimeOffset From,
        DateTimeOffset To,
        decimal GrossAmount,
        decimal RefundedAmount,
        decimal NetAmount,
        int EventCount);

    public sealed record MrrResponse(long MrrCents, long ArrCents, DateTimeOffset ComputedAt);

    public sealed record ChurnResponse(TimeSpan Window, int ActiveAtStart, int CanceledInWindow, decimal ChurnPercent);

    public sealed record LtvResponse(string? Segment, int Cohort, decimal AverageLtv, decimal TotalLtv);

    public sealed record AdminBillingAnalyticsSeriesPoint(DateTimeOffset Date, decimal Value);

    public sealed record AdminBillingAnalyticsResponse(
        IReadOnlyList<AdminBillingAnalyticsSeriesPoint> Mrr,
        IReadOnlyList<AdminBillingAnalyticsSeriesPoint> ChurnRate,
        IReadOnlyList<AdminBillingAnalyticsSeriesPoint> Ltv,
        string Currency,
        bool Available);

    public sealed record IssueRefundRequest(
        string CheckoutSessionId,
        long AmountCents,
        string? Reason,
        string? AdminNote,
        string? IdempotencyKey);

    public sealed record RefundSummary(
        Guid Id,
        string PaymentTransactionId,
        string LearnerUserId,
        string Gateway,
        string Status,
        string RefundType,
        decimal Amount,
        string Currency,
        string? Reason,
        string? RequestedByAdminId,
        DateTimeOffset CreatedAt);

    public sealed record AdminProductDto(
        Guid Id,
        string Code,
        string Name,
        string? Description,
        string ProductType,
        string? StripeProductId,
        bool IsActive,
        string? MetadataJson,
        IReadOnlyList<AdminPriceDto> Prices);

    public sealed record AdminPriceDto(
        Guid Id,
        string? StripePriceId,
        string Currency,
        decimal Amount,
        string? Interval,
        int IntervalCount,
        string? Country,
        bool IsActive);

    public sealed record AdminCreateProductRequest(
        string Code,
        string Name,
        string? Description,
        string? ProductType,
        string? StripeProductId,
        string? MetadataJson,
        List<AdminCreatePriceRequest>? Prices);

    public sealed record AdminCreatePriceRequest(
        string Currency,
        decimal Amount,
        string? Interval,
        int? IntervalCount,
        string? Country,
        string? StripePriceId);

    public sealed record AdminPatchProductRequest(
        string? Name,
        string? Description,
        string? ProductType,
        string? StripeProductId,
        bool? IsActive,
        string? MetadataJson);

    public sealed record AdminCouponDto(
        string Id,
        string Code,
        string Name,
        string Description,
        string DiscountType,
        decimal DiscountValue,
        string Currency,
        string Status,
        DateTimeOffset? StartsAt,
        DateTimeOffset? EndsAt,
        int? UsageLimitTotal,
        int? UsageLimitPerUser,
        int RedemptionCount,
        bool IsStackable,
        string CouponVariant);

    public sealed record AdminCreateCouponRequest(
        string Code,
        string? Name,
        string? Description,
        string? DiscountType,
        decimal DiscountValue,
        string? Currency,
        string? Status,
        DateTimeOffset? StartsAt,
        DateTimeOffset? EndsAt,
        int? UsageLimitTotal,
        int? UsageLimitPerUser,
        decimal? MinimumSubtotal,
        string? ApplicablePlanCodesJson,
        string? ApplicableAddOnCodesJson,
        string? EligibleCountriesJson,
        string? VariantMetadataJson,
        string? CouponVariant,
        bool? IsStackable,
        bool? NewUsersOnly,
        bool? ExistingUsersOnly);

    public sealed record AdminPatchCouponRequest(
        string? Name,
        string? Description,
        string? DiscountType,
        decimal? DiscountValue,
        string? Currency,
        string? Status,
        DateTimeOffset? StartsAt,
        DateTimeOffset? EndsAt,
        int? UsageLimitTotal,
        int? UsageLimitPerUser,
        decimal? MinimumSubtotal,
        string? ApplicablePlanCodesJson,
        string? ApplicableAddOnCodesJson,
        bool? IsStackable);

    public sealed record TaxRegistrationListResponse(IReadOnlyList<TaxRegistrationDto> Registrations, string Mode);

    public sealed record TaxRegistrationDto(
        string Id,
        string Country,
        string Status,
        DateTimeOffset? ActiveFrom,
        DateTimeOffset? ExpiresAt,
        bool Livemode);

    public sealed record CreateTaxRegistrationRequest(string Country, DateTimeOffset ActiveFrom, string Type);
}
