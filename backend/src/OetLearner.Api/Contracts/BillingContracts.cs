using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Contracts;

public record BillingQuoteRequest(
    string ProductType,
    int Quantity,
    string? PriceId,
    string? CouponCode = null,
    List<string>? AddOnCodes = null);

public record BillingQuoteLineItem(
    string Kind,
    string Code,
    string Name,
    decimal Amount,
    string Currency,
    int Quantity,
    string? Description = null);

public record BillingQuoteResponse(
    string QuoteId,
    string Status,
    string Currency,
    decimal SubtotalAmount,
    decimal DiscountAmount,
    decimal TotalAmount,
    string? PlanCode,
    string? CouponCode,
    IReadOnlyList<string> AddOnCodes,
    IReadOnlyList<BillingQuoteLineItem> Items,
    DateTimeOffset ExpiresAt,
    string Summary,
    Dictionary<string, object?> Validation);

public record BillingPlanUpsertRequest(
    [property: Required, MaxLength(64)] string Code,
    [property: Required, MaxLength(128)] string Name,
    [property: Required, MaxLength(1024)] string Description,
    decimal Price,
    [property: Required, MaxLength(8)] string Currency,
    [property: Required, MaxLength(16)] string Interval,
    int DurationMonths,
    int IncludedCredits,
    int DisplayOrder,
    bool IsVisible,
    bool IsRenewable,
    int TrialDays,
    string? Status = null,
    string? IncludedSubtestsJson = null,
    string? EntitlementsJson = null);

public record BillingAddOnUpsertRequest(
    [property: Required, MaxLength(64)] string Code,
    [property: Required, MaxLength(128)] string Name,
    [property: Required, MaxLength(1024)] string Description,
    decimal Price,
    [property: Required, MaxLength(8)] string Currency,
    [property: Required, MaxLength(32)] string Interval,
    int DurationDays,
    int GrantCredits,
    int DisplayOrder,
    bool IsRecurring,
    bool AppliesToAllPlans,
    bool IsStackable,
    int QuantityStep,
    int? MaxQuantity,
    string? Status = null,
    string? CompatiblePlanCodesJson = null,
    string? GrantEntitlementsJson = null);

public record BillingCouponUpsertRequest(
    [property: Required, MaxLength(64)] string Code,
    [property: Required, MaxLength(128)] string Name,
    [property: Required, MaxLength(1024)] string Description,
    string DiscountType,
    decimal DiscountValue,
    [property: Required, MaxLength(8)] string Currency,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    int? UsageLimitTotal,
    int? UsageLimitPerUser,
    decimal? MinimumSubtotal,
    bool IsStackable,
    string? Status = null,
    string? ApplicablePlanCodesJson = null,
    string? ApplicableAddOnCodesJson = null,
    string? Notes = null);