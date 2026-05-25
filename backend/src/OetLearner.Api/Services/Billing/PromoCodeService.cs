using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Billing;

public sealed class PromoCodeService : IPromoCodeService
{
    private readonly LearnerDbContext _db;
    private readonly IStripeService _stripe;

    public PromoCodeService(LearnerDbContext db, IStripeService stripe)
    {
        _db = db;
        _stripe = stripe;
    }

    public async Task<PromoCodeValidationResult> ValidateAsync(
        string code, string? userId = null, CancellationToken ct = default)
    {
        var coupon = await _db.BillingCoupons
            .Where(c => c.Code == code)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

        if (coupon is null)
            return new PromoCodeValidationResult(false, "Code not found.", null, null, null);

        if (coupon.Status != BillingCouponStatus.Active)
            return new PromoCodeValidationResult(false, "Code is not active.", null, null, null);

        var now = DateTimeOffset.UtcNow;
        if (coupon.StartsAt.HasValue && coupon.StartsAt.Value > now)
            return new PromoCodeValidationResult(false, "Code is not yet valid.", null, null, null);

        if (coupon.EndsAt.HasValue && coupon.EndsAt.Value < now)
            return new PromoCodeValidationResult(false, "Code has expired.", null, null, null);

        decimal? discountPercent = coupon.DiscountType == BillingDiscountType.Percentage
            ? coupon.DiscountValue : null;
        decimal? discountAmount = coupon.DiscountType == BillingDiscountType.FixedAmount
            ? coupon.DiscountValue : null;

        return new PromoCodeValidationResult(true, null, discountPercent, discountAmount, coupon.Currency);
    }

    public async Task<string> CreatePromoCodeAsync(CreatePromoCodeRequest request, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var discountType = request.PercentOff.HasValue ? BillingDiscountType.Percentage : BillingDiscountType.FixedAmount;
        var discountValue = request.PercentOff ?? request.AmountOff ?? 0m;

        var coupon = new BillingCoupon
        {
            Id = Guid.NewGuid().ToString("N"),
            Code = request.Code,
            Name = request.Name ?? request.Code,
            Description = string.Empty,
            DiscountType = discountType,
            DiscountValue = discountValue,
            Currency = request.Currency ?? "AUD",
            Status = BillingCouponStatus.Active,
            EndsAt = request.ExpiresAt,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.BillingCoupons.Add(coupon);
        await _db.SaveChangesAsync(ct);

        // Sync to Stripe
        var stripeCouponId = await _stripe.CreateCouponAsync(new CreateStripeCouponRequest(
            Name: request.Name,
            PercentOff: request.PercentOff,
            AmountOff: request.AmountOff.HasValue ? (long?)(request.AmountOff.Value * 100) : null,
            Currency: request.Currency,
            Duration: "once",
            DurationInMonths: null
        ), ct);

        var promoCodeId = await _stripe.CreatePromotionCodeAsync(stripeCouponId, request.Code, ct);
        return promoCodeId;
    }

    public async Task DeactivateAsync(string code, CancellationToken ct = default)
    {
        var coupon = await _db.BillingCoupons
            .FirstOrDefaultAsync(c => c.Code == code, ct);

        if (coupon is null) return;

        coupon.Status = BillingCouponStatus.Inactive;
        coupon.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }
}
