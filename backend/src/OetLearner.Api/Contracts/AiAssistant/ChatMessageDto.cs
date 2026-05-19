using System;

namespace OetLearner.Api.Contracts.AiAssistant;

public class ChatMessageDto
{
    public Guid Id { get; set; }
    public Guid ThreadId { get; set; }
    public string Role { get; set; } = string.Empty; // system|user|assistant|tool
    public string Content { get; set; } = string.Empty;
    public string? ToolPayloadJson { get; set; }
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
