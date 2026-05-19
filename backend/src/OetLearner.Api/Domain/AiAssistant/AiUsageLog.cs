using System;
using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain.AiAssistant;

public class AiUsageLog
{
    [Key]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public Guid? ThreadId { get; set; }
    public Guid? ProviderConfigId { get; set; }

    [MaxLength(128)]
    public string? Model { get; set; }

    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public decimal? EstimatedCostUsd { get; set; }

    [MaxLength(64)]
    public string? Outcome { get; set; } // success, error, refused

    public DateTimeOffset OccurredAt { get; set; }
}
