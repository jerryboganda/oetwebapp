using System;

namespace OetLearner.Api.Contracts.AiAssistant;

public class ProviderConfigDto
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Endpoint { get; set; }
    public string? AllowedModelsCsv { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsDefault { get; set; }
    public bool HasSecret { get; set; } // never return the secret itself
}
