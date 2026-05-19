using System;
using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain.AiAssistant;

public class AiChatMessage
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public Guid ThreadId { get; set; }
    public AiChatThread? Thread { get; set; }

    public AiChatMessageRole Role { get; set; }

    // TODO Phase 1: large content -> consider jsonb columns / chunking.
    public string Content { get; set; } = string.Empty;

    // TODO Phase 1: tool_call payload JSON when Role == Tool/Assistant tool-call.
    public string? ToolPayloadJson { get; set; }

    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
