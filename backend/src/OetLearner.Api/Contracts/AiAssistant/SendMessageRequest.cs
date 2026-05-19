using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Contracts.AiAssistant;

public class SendMessageRequest
{
    [Required]
    public string Content { get; set; } = string.Empty;

    // TODO Phase 1: attachments are file refs resolved
    // // TODO: via IFileStorage or ICodebaseExecutor (sandboxed)
    public List<string>? AttachmentPaths { get; set; }

    // Optional per-message model override.
    [MaxLength(128)]
    public string? Model { get; set; }
}
