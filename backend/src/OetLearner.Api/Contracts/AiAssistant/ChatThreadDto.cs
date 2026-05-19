using System;

namespace OetLearner.Api.Contracts.AiAssistant;

public class ChatThreadDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public Guid? ProviderConfigId { get; set; }
    public string? Model { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int MessageCount { get; set; }
}
