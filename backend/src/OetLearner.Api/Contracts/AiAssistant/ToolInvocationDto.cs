using System;

namespace OetLearner.Api.Contracts.AiAssistant;

public class ToolInvocationDto
{
    public Guid Id { get; set; }
    public Guid ThreadId { get; set; }
    public Guid MessageId { get; set; }
    public string ToolName { get; set; } = string.Empty;
    public string ArgsJson { get; set; } = "{}";
    public string ApprovalPolicy { get; set; } = string.Empty;
    public bool? ApprovalDecision { get; set; }
    public string? RejectionReason { get; set; }
    public bool DidSucceed { get; set; }
    public string? ResultJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
