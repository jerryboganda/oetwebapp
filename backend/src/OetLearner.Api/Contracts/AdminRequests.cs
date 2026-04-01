namespace OetLearner.Api.Contracts;

// ── Content ──

public record AdminContentCreateRequest(
    string Title,
    string ContentType,
    string SubtestCode,
    string? ProfessionId,
    string? Difficulty,
    int? EstimatedDurationMinutes,
    string? Description,
    string? ModelAnswer,
    string? CriteriaFocus,
    string? CaseNotes);

public record AdminContentUpdateRequest(
    string? Title,
    string? ContentType,
    string? SubtestCode,
    string? ProfessionId,
    string? Difficulty,
    int? EstimatedDurationMinutes,
    string? Description,
    string? ModelAnswer,
    string? CriteriaFocus,
    string? CaseNotes,
    string? ChangeNote);

public record AdminContentStatusRequest(string? Reason);

// ── Taxonomy ──

public record AdminTaxonomyCreateRequest(
    string Label,
    string Code,
    string? Type,
    string? Description);

public record AdminTaxonomyUpdateRequest(
    string? Label,
    string? Code,
    string? Type,
    string? Status);

// ── Criteria ──

public record AdminCriterionCreateRequest(
    string Name,
    string SubtestCode,
    int Weight,
    string? Description);

public record AdminCriterionUpdateRequest(
    string? Name,
    int? Weight,
    string? Status,
    string? Description);

// ── AI Config ──

public record AdminAIConfigCreateRequest(
    string Model,
    string Provider,
    string TaskType,
    string? Status,
    double Accuracy,
    double ConfidenceThreshold,
    string? RoutingRule,
    string? ExperimentFlag,
    string? PromptLabel);

public record AdminAIConfigUpdateRequest(
    string? Model,
    string? Provider,
    string? TaskType,
    string? Status,
    double? Accuracy,
    double? ConfidenceThreshold,
    string? RoutingRule,
    string? ExperimentFlag,
    string? PromptLabel);

// ── Feature Flags ──

public record AdminFlagCreateRequest(
    string Name,
    string Key,
    string? FlagType,
    bool Enabled,
    int RolloutPercentage,
    string? Description,
    string? Owner);

public record AdminFlagUpdateRequest(
    string? Name,
    string? Key,
    string? FlagType,
    bool? Enabled,
    int? RolloutPercentage,
    string? Description,
    string? Owner);

// ── Users ──

public record AdminUserInviteRequest(
    string Name,
    string Email,
    string Role,
    string? ProfessionId);

public record AdminUserStatusRequest(string Status, string? Reason);

public record AdminUserCreditsRequest(int Amount, string? Reason);

public record AdminUserLifecycleRequest(string? Reason);

// ── Billing ──

public record AdminBillingPlanCreateRequest(
    string Code,
    string Name,
    string Description,
    decimal Price,
    string Currency,
    string Interval,
    int DurationMonths,
    int IncludedCredits,
    int DisplayOrder,
    bool IsVisible,
    bool IsRenewable,
    int TrialDays,
    string? Status = null,
    string? IncludedSubtestsJson = null,
    string? EntitlementsJson = null);

public record AdminBillingPlanUpdateRequest(
    string Code,
    string Name,
    string Description,
    decimal Price,
    string Currency,
    string Interval,
    int DurationMonths,
    int IncludedCredits,
    int DisplayOrder,
    bool IsVisible,
    bool IsRenewable,
    int TrialDays,
    string? Status = null,
    string? IncludedSubtestsJson = null,
    string? EntitlementsJson = null);

public record AdminBillingAddOnCreateRequest(
    string Code,
    string Name,
    string Description,
    decimal Price,
    string Currency,
    string Interval,
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

public record AdminBillingAddOnUpdateRequest(
    string Code,
    string Name,
    string Description,
    decimal Price,
    string Currency,
    string Interval,
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

public record AdminBillingCouponCreateRequest(
    string Code,
    string Name,
    string Description,
    string DiscountType,
    decimal DiscountValue,
    string Currency,
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

public record AdminBillingCouponUpdateRequest(
    string Code,
    string Name,
    string Description,
    string DiscountType,
    decimal DiscountValue,
    string Currency,
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

// ── Review Ops ──

public record AdminReviewAssignRequest(string ExpertId, string? Reason);

public record AdminReviewCancelRequest(string Reason);

public record AdminReviewReopenRequest(string? Reason);

// ── Bulk Actions ──

public record AdminBulkActionRequest(
    string Action,
    string[] ContentIds,
    bool DryRun = false);
