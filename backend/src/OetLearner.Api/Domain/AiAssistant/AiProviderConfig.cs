using System;
using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain.AiAssistant;

public class AiProviderConfig
{
    [Key]
    public Guid Id { get; set; }

    public AiProviderKind Kind { get; set; }

    [Required, MaxLength(128)]
    public string DisplayName { get; set; } = string.Empty;

    // TODO Phase 1: do NOT store raw API keys here.
    // Reference the runtime-settings key name; actual secret resolved
    // // TODO: via IRuntimeSettingsProvider
    [MaxLength(256)]
    public string? SecretKeyRef { get; set; }

    [MaxLength(512)]
    public string? Endpoint { get; set; }

    // CSV of allowed model ids. Keep short; large lists -> separate table.
    [MaxLength(2048)]
    public string? AllowedModelsCsv { get; set; }

    public bool IsEnabled { get; set; }
    public bool IsDefault { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
