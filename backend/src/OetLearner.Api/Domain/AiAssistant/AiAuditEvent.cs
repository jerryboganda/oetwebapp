using System;
using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain.AiAssistant;

public class AiAuditEvent
{
    [Key]
    public Guid Id { get; set; }

    public Guid ActorUserId { get; set; }
    public AiAuditAction Action { get; set; }

    // TODO Phase 1: serialize event-specific metadata (target ids,
    // changed-key list, etc) as JSON. NEVER store secret values.
    public string? MetadataJson { get; set; }

    [MaxLength(64)]
    public string? IpAddress { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
