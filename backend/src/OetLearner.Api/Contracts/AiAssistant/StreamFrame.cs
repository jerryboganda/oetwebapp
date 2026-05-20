using System;

namespace OetLearner.Api.Contracts.AiAssistant;

// Tagged-union frame pushed over the SignalR hub to the admin UI.
// TODO Phase 1: serialise with a `type` discriminator field.
public abstract class StreamFrame
{
    public string Type { get; init; } = string.Empty;
    public Guid ThreadId { get; init; }
    public Guid MessageId { get; init; }
}

public sealed class MessageStartFrame : StreamFrame { public string Role { get; init; } = "assistant"; }
public sealed class TokenDeltaFrame : StreamFrame { public string Delta { get; init; } = string.Empty; }
public sealed class ToolCallStartFrame : StreamFrame { public Guid InvocationId { get; init; } public string ToolName { get; init; } = string.Empty; }
public sealed class ToolCallDeltaFrame : StreamFrame { public Guid InvocationId { get; init; } public string Delta { get; init; } = string.Empty; }
public sealed class ToolCallResultFrame : StreamFrame { public Guid InvocationId { get; init; } public bool Success { get; init; } public string? ResultJson { get; init; } }
public sealed class ApprovalRequestFrame : StreamFrame { public Guid InvocationId { get; init; } public string ToolName { get; init; } = string.Empty; public string ArgsJson { get; init; } = "{}"; }
public sealed class MessageEndFrame : StreamFrame { public int? PromptTokens { get; init; } public int? CompletionTokens { get; init; } }
public sealed class ErrorFrame : StreamFrame { public string Message { get; init; } = string.Empty; public string? Code { get; init; } }
