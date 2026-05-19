using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain.AiAssistant;

public class AiChatThread
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public Guid OwnerUserId { get; set; }

    [MaxLength(256)]
    public string Title { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // TODO Phase 1: provider/model selection per-thread.
    public Guid? ProviderConfigId { get; set; }

    [MaxLength(128)]
    public string? Model { get; set; }

    public bool IsArchived { get; set; }

    public ICollection<AiChatMessage> Messages { get; set; } = new List<AiChatMessage>();
}
