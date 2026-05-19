using System;
using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain.AiAssistant;

public class AiToolInvocation
{
    [Key]
    public Guid Id { get; set; }

    public Guid ThreadId { get; set; }
    public Guid MessageId { get; set; }

    [Required, MaxLength(128)]
    public string ToolName { get; set; } = string.Empty;

    public string ArgsJson { get; set; } = "{}";

    public AiToolApprovalPolicy ApprovalPolicy { get; set; }

    // null = pending, true = approved, false = rejected
    public bool? ApprovalDecision { get; set; }
    public Guid? ApprovedByUserId { get; set; }

    [MaxLength(512)]
    public string? RejectionReason { get; set; }

    public string? ResultJson { get; set; }
    public bool DidSucceed { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
