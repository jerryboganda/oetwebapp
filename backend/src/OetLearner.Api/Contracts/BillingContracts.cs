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

public record AdminBillingCatalogSubjectResponse(
    string Kind,
    string Id,
    string Code,
    string Name,
    string? ActiveVersionId,
    int? ActiveVersionNumber,
    string? LatestVersionId,
    int? LatestVersionNumber,
    int VersionCount);

public record AdminBillingCatalogVersionResponse(
    string Id,
    string ParentId,
    int VersionNumber,
    string Code,
    string Name,
    string Description,
    string Status,
    bool IsActive,
    bool IsLatest,
    string? CreatedByAdminId,
    string? CreatedByAdminName,
    DateTimeOffset CreatedAt,
    Dictionary<string, object?> Summary);

public record AdminBillingCatalogVersionHistoryResponse(
    AdminBillingCatalogSubjectResponse Subject,
    IReadOnlyList<AdminBillingCatalogVersionResponse> Items);

public record AdminBillingEntitlementDiagnosticsSummaryResponse(
    int InvalidAiQuotaMappings,
    int MissingPlanSubscriptions,
    int FallbackMappings,
    int LegacyContentShape,
    int TotalWarnings);

public record AdminBillingEntitlementDiagnosticExampleResponse(
    string SubjectType,
    string SubjectId,
    string SubjectCode,
    string SubjectName,
    string Message,
    Dictionary<string, string> Metadata);

public record AdminBillingEntitlementDiagnosticCheckResponse(
    string Key,
    string Label,
    string Severity,
    int Count,
    IReadOnlyList<AdminBillingEntitlementDiagnosticExampleResponse> Examples);

public record AdminBillingEntitlementDiagnosticsResponse(
    DateTimeOffset GeneratedAt,
    AdminBillingEntitlementDiagnosticsSummaryResponse Summary,
    IReadOnlyList<AdminBillingEntitlementDiagnosticCheckResponse> Checks);

public record AdminBillingInvoiceEvidenceInvoiceResponse(
    string Id,
    string UserId,
    string UserName,
    decimal Amount,
    string Currency,
    string Status,
    string Description,
    DateTimeOffset IssuedAt,
    string? PlanVersionId,
    Dictionary<string, string> AddOnVersionIds,
    string? CouponVersionId,
    string? QuoteId,
    string? CheckoutSessionId);

public record AdminBillingInvoiceEvidenceQuoteResponse(
    string Id,
    string Status,
    string Currency,
    decimal SubtotalAmount,
    decimal DiscountAmount,
    decimal TotalAmount,
    string? PlanCode,
    string? CouponCode,
    IReadOnlyList<string> AddOnCodes,
    IReadOnlyList<BillingQuoteLineItem> Items,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string? CheckoutSessionId,
    string? PlanVersionId,
    Dictionary<string, string> AddOnVersionIds,
    string? CouponVersionId,
    string Summary);

public record AdminBillingInvoiceEvidencePaymentResponse(
    string Id,
    string Gateway,
    string GatewayTransactionId,
    string TransactionType,
    string Status,
    decimal Amount,
    string Currency,
    string ProductType,
    string ProductId,
    string? QuoteId,
    string? PlanVersionId,
    Dictionary<string, string> AddOnVersionIds,
    string? CouponVersionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record AdminBillingInvoiceEvidenceRedemptionResponse(
    string Id,
    string CouponCode,
    string? CouponId,
    string? CouponVersionId,
    string UserId,
    string? QuoteId,
    string? CheckoutSessionId,
    string? SubscriptionId,
    decimal DiscountAmount,
    string Currency,
    string Status,
    DateTimeOffset RedeemedAt);

public record AdminBillingInvoiceEvidenceSubscriptionItemResponse(
    string Id,
    string SubscriptionId,
    string ItemType,
    string ItemCode,
    string? AddOnVersionId,
    int Quantity,
    string Status,
    string? QuoteId,
    string? CheckoutSessionId,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt);

public record AdminBillingInvoiceEvidenceEventResponse(
    string Id,
    string EventType,
    string EntityType,
    string EntityId,
    string? SubscriptionId,
    string? QuoteId,
    DateTimeOffset OccurredAt);

public record AdminBillingInvoiceEvidenceCatalogAnchorResponse(
    string? PlanVersionId,
    Dictionary<string, string> AddOnVersionIds,
    string? CouponVersionId,
    string Source);

public record AdminBillingInvoiceEvidenceResponse(
    AdminBillingInvoiceEvidenceInvoiceResponse Invoice,
    AdminBillingInvoiceEvidenceQuoteResponse? Quote,
    IReadOnlyList<AdminBillingInvoiceEvidencePaymentResponse> Payments,
    IReadOnlyList<AdminBillingInvoiceEvidenceRedemptionResponse> Redemptions,
    IReadOnlyList<AdminBillingInvoiceEvidenceSubscriptionItemResponse> SubscriptionItems,
    IReadOnlyList<AdminBillingInvoiceEvidenceEventResponse> Events,
    AdminBillingInvoiceEvidenceCatalogAnchorResponse CatalogAnchors,
    IReadOnlyList<string> NotRecorded,
    IReadOnlyList<string> IntegrityFlags);

public record AdminBillingPaymentTransactionResponse(
    string Id,
    string LearnerUserId,
    string LearnerName,
    string Gateway,
    string GatewayTransactionId,
    string TransactionType,
    string Status,
    decimal Amount,
    string Currency,
    string ProductType,
    string ProductId,
    string? QuoteId,
    string? PlanVersionId,
    Dictionary<string, string> AddOnVersionIds,
    string? CouponVersionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record AdminBillingPaymentTransactionListResponse(
    int Total,
    int Page,
    int PageSize,
    IReadOnlyList<AdminBillingPaymentTransactionResponse> Items);

public record AdminBillingProviderLifecycleSignalsSummaryResponse(
    int TotalSignals,
    int FailedSignals,
    int UnverifiedSignals,
    int UnmatchedSignals,
    int RefundSignals,
    int DisputeSignals,
    int CancellationSignals);

public record AdminBillingProviderLifecycleLocalIdsResponse(
    IReadOnlyList<string> PaymentTransactionIds,
    IReadOnlyList<string> InvoiceIds,
    IReadOnlyList<string> QuoteIds,
    IReadOnlyList<string> SubscriptionIds,
    IReadOnlyList<string> BillingEventIds);

public record AdminBillingProviderLifecycleSignalResponse(
    string Id,
    string Source,
    string Category,
    string CorrelationStatus,
    string Confidence,
    string Gateway,
    string EventType,
    string MaskedProviderEventId,
    string? MaskedProviderTransactionId,
    string ProcessingStatus,
    string VerificationStatus,
    string? NormalizedStatus,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? ProcessedAt,
    AdminBillingProviderLifecycleLocalIdsResponse LinkedLocalIds,
    int BillingEventCount,
    IReadOnlyList<string> IntegrityFlags);

public record AdminBillingProviderLifecycleSignalsResponse(
    int Total,
    int Page,
    int PageSize,
    AdminBillingProviderLifecycleSignalsSummaryResponse Summary,
    IReadOnlyList<AdminBillingProviderLifecycleSignalResponse> Items);
