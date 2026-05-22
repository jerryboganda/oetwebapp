using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// Phase 9 — billing-event notification template, versioned per channel + locale.
/// Distinct from the existing NotificationEvent / NotificationInboxItem stream
/// (which is for in-app notifications); this table covers transactional billing
/// templates rendered for email / SMS / WhatsApp dispatch.
/// </summary>
[Index(nameof(Code), nameof(Channel), nameof(LocaleTag), IsUnique = true)]
public class BillingNotificationTemplate
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>Stable event code (trial_started, payment_failed, dunning_day3, refund_completed, ...).</summary>
    [MaxLength(64)]
    public string Code { get; set; } = default!;

    /// <summary>email | sms | whatsapp | inapp.</summary>
    [MaxLength(16)]
    public string Channel { get; set; } = default!;

    [MaxLength(16)]
    public string LocaleTag { get; set; } = "en";

    [MaxLength(256)]
    public string? Subject { get; set; }

    [MaxLength(8192)]
    public string BodyTemplate { get; set; } = string.Empty;

    /// <summary>JSON array of variable names available to the template.</summary>
    [MaxLength(1024)]
    public string VariablesJson { get; set; } = "[]";

    public int Version { get; set; } = 1;
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Audit log of every billing notification dispatched. Used for idempotency + diagnostics.</summary>
[Index(nameof(UserId), nameof(EventCode), nameof(EventId), nameof(TemplateCode), IsUnique = true)]
[Index(nameof(UserId), nameof(SentAt))]
public class BillingNotificationDispatchLog
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string EventCode { get; set; } = default!;

    [MaxLength(64)]
    public string EventId { get; set; } = default!;

    [MaxLength(64)]
    public string TemplateCode { get; set; } = default!;

    [MaxLength(16)]
    public string Channel { get; set; } = default!;

    /// <summary>queued | sent | failed | suppressed.</summary>
    [MaxLength(16)]
    public string Status { get; set; } = "queued";

    [MaxLength(512)]
    public string? FailureReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
}
