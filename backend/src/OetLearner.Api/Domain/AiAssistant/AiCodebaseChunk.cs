using System;
using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain.AiAssistant;

public class AiCodebaseChunk
{
    [Key]
    public Guid Id { get; set; }

    [Required, MaxLength(1024)]
    public string RepoRelativePath { get; set; } = string.Empty;

    [MaxLength(64)]
    public string Language { get; set; } = string.Empty;

    public int StartLine { get; set; }
    public int EndLine { get; set; }

    public string Content { get; set; } = string.Empty;

    // SHA-256 of file content for cache invalidation.
    [MaxLength(64)]
    public string FileContentHash { get; set; } = string.Empty;

    // TODO Phase 2: vector(1536) Embedding column added via raw SQL migration.
    // EF property is intentionally omitted here.

    public DateTimeOffset IndexedAt { get; set; }
}
