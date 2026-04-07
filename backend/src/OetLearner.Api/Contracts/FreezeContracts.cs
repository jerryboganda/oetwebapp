namespace OetLearner.Api.Contracts;

public sealed record FreezeRequestRequest(
    DateTimeOffset? StartAt,
    DateTimeOffset? EndAt,
    string? Reason,
    bool? PauseEntitlementClock);

public sealed record FreezeActionRequest(
    string? Reason,
    string? InternalNotes);

public sealed record FreezePolicyRequest(
    bool IsEnabled,
    bool SelfServiceEnabled,
    string ApprovalMode,
    int MinDurationDays,
    int MaxDurationDays,
    bool AllowScheduling,
    string AccessMode,
    string EntitlementPauseMode,
    bool RequireReason,
    bool RequireInternalNotes,
    bool AllowActivePaid,
    bool AllowGracePeriod,
    bool AllowTrial,
    bool AllowComplimentary,
    bool AllowCancelled,
    bool AllowExpired,
    bool AllowReviewOnly,
    bool AllowPastDue,
    bool AllowSuspended,
    string? PolicyNotes,
    string? EligibilityReasonCodesJson);

public sealed record FreezeManualCreateRequest(
    string UserId,
    DateTimeOffset? StartAt,
    DateTimeOffset? EndAt,
    string? Reason,
    string? InternalNotes,
    bool? PauseEntitlementClock,
    bool? OverrideEligibility);
