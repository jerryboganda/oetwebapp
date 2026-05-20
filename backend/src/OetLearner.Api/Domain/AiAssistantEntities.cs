using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// A conversation thread owned by a single user. Threads are scoped by role
/// (admin/expert/learner) which determines available tools and system prompt.
/// </summary>
[Index(nameof(UserId), nameof(UpdatedAt))]
[Index(nameof(UserId), nameof(Role))]
public class AiAssistantThread
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    /// <summary>Role at thread creation: admin, expert, or learner.</summary>
    [MaxLength(16)]
    public string Role { get; set; } = default!;

    [MaxLength(256)]
    public string? Title { get; set; }

    /// <summary>Model override for this thread (null = use feature route default).</summary>
    [MaxLength(128)]
    public string? ModelOverride { get; set; }

    public bool IsArchived { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Single message in an assistant thread. Messages are ordered by CreatedAt
/// within a thread. Role is "user", "assistant", or "tool".
/// </summary>
[Index(nameof(ThreadId), nameof(CreatedAt))]
public class AiAssistantMessage
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ThreadId { get; set; } = default!;

    /// <summary>Message role: "user", "assistant", "system", or "tool".</summary>
    [MaxLength(16)]
    public string Role { get; set; } = default!;

    /// <summary>Text content. May be null for pure tool-call messages.</summary>
    public string? Content { get; set; }

    /// <summary>JSON-serialized tool calls (OpenAI format). Null for non-tool messages.</summary>
    public string? ToolCallsJson { get; set; }

    /// <summary>If this is a tool result message, the tool_call_id it responds to.</summary>
    [MaxLength(128)]
    public string? ToolCallId { get; set; }

    /// <summary>If this is a tool result, the tool name.</summary>
    [MaxLength(64)]
    public string? ToolName { get; set; }

    /// <summary>Token count for this message (prompt or completion tokens).</summary>
    public int TokenCount { get; set; }

    /// <summary>Model that generated this message (null for user messages).</summary>
    [MaxLength(128)]
    public string? Model { get; set; }

    /// <summary>Linked AiUsageRecord ID for assistant messages (for cost tracking).</summary>
    [MaxLength(64)]
    public string? AiUsageRecordId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public AiAssistantThread Thread { get; set; } = default!;
}

/// <summary>
/// Pre-write file backup for the safety floor. Every mutation tool invocation
/// snapshots the prior file content here before applying the write.
/// Retained for <c>AiAssistantOptions.BackupRetentionDays</c> (default 30).
/// </summary>
[Index(nameof(ThreadId), nameof(CreatedAt))]
[Index(nameof(FilePath), nameof(CreatedAt))]
public class AiFileBackup
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ThreadId { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string? MessageId { get; set; }

    /// <summary>Absolute file path that was about to be overwritten.</summary>
    [MaxLength(1024)]
    public string FilePath { get; set; } = default!;

    /// <summary>Original file content before the write. Stored as-is (not compressed).</summary>
    public string OriginalContent { get; set; } = default!;

    /// <summary>SHA-256 hash of the original content for dedup/integrity.</summary>
    [MaxLength(64)]
    public string ContentHash { get; set; } = default!;

    /// <summary>Size in bytes of the original content.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Git branch name where the autosave commit was created.</summary>
    [MaxLength(256)]
    public string? AutosaveBranch { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Codebase chunk for RAG search. Each row is a semantic unit of code
/// (function, class, block) with optional pgvector embedding for similarity search.
/// </summary>
[Index(nameof(FilePath))]
[Index(nameof(Language))]
public class AiCodebaseChunk
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>Relative file path from the repository root.</summary>
    [MaxLength(1024)]
    public string FilePath { get; set; } = default!;

    /// <summary>Programming language of the chunk (e.g., "typescript", "csharp").</summary>
    [MaxLength(32)]
    public string Language { get; set; } = default!;

    /// <summary>Semantic type: function, class, interface, module, block, comment.</summary>
    [MaxLength(32)]
    public string ChunkType { get; set; } = default!;

    /// <summary>Symbol name if applicable (function/class name).</summary>
    [MaxLength(256)]
    public string? SymbolName { get; set; }

    /// <summary>Start line number (1-based) in the source file.</summary>
    public int StartLine { get; set; }

    /// <summary>End line number (1-based) in the source file.</summary>
    public int EndLine { get; set; }

    /// <summary>The actual code text of the chunk.</summary>
    public string Content { get; set; } = default!;

    /// <summary>Token count of the content (for budget calculations).</summary>
    public int TokenCount { get; set; }

    /// <summary>SHA-256 hash of the content for change detection.</summary>
    [MaxLength(64)]
    public string ContentHash { get; set; } = default!;

    /// <summary>tsvector for BM25 full-text search (populated by Postgres trigger or app).</summary>
    [MaxLength(64)]
    public string? TsVectorConfig { get; set; }

    /// <summary>
    /// pgvector embedding (1536 dimensions for text-embedding-3-small).
    /// Null until the embedding service processes this chunk.
    /// Stored as float[] and mapped via Pgvector.EntityFrameworkCore.
    /// </summary>
    public float[]? Embedding { get; set; }

    public DateTimeOffset IndexedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EmbeddedAt { get; set; }
}
