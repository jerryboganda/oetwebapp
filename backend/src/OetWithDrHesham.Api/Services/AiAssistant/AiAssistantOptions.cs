namespace OetWithDrHesham.Api.Services.AiAssistant;

/// <summary>
/// Configuration options for the AI Assistant subsystem.
/// Bound from appsettings section "AiAssistant".
/// </summary>
public sealed class AiAssistantOptions
{
    public const string Section = "AiAssistant";

    /// <summary>Global kill switch. When false, all assistant features are disabled.</summary>
    public bool GlobalEnabled { get; set; } = false;

    /// <summary>When true, every write operation requires explicit user approval
    /// before being applied. Defaults to true for safety.</summary>
    public bool RequireApprovalAlways { get; set; } = true;

    /// <summary>Maximum ReAct loop iterations before forcing a response.</summary>
    public int MaxIterations { get; set; } = 10;

    /// <summary>Maximum messages to include in context window.</summary>
    public int MaxContextMessages { get; set; } = 50;

    /// <summary>Days to retain file backups before cleanup.</summary>
    public int BackupRetentionDays { get; set; } = 30;

    /// <summary>Maximum file size (bytes) that can be written in a single operation.</summary>
    public long MaxWriteFileSizeBytes { get; set; } = 1_048_576; // 1 MB

    /// <summary>Command execution timeout in seconds.</summary>
    public int CommandTimeoutSeconds { get; set; } = 300; // 5 minutes

    /// <summary>Allowed root paths for file operations (default: /opt/oetwebapp).</summary>
    public string[] AllowedRoots { get; set; } = ["/opt/oetwebapp"];

    /// <summary>Circuit breaker: max failures within FailureWindowSeconds before pause.</summary>
    public int CircuitBreakerMaxFailures { get; set; } = 3;

    /// <summary>Circuit breaker: failure counting window in seconds.</summary>
    public int CircuitBreakerFailureWindowSeconds { get; set; } = 60;

    /// <summary>Circuit breaker: max writes within WriteWindowSeconds before pause.</summary>
    public int CircuitBreakerMaxWrites { get; set; } = 10;

    /// <summary>Circuit breaker: write counting window in seconds.</summary>
    public int CircuitBreakerWriteWindowSeconds { get; set; } = 300;

    /// <summary>Glob patterns to exclude from codebase indexing.</summary>
    public string[] IndexExcludePatterns { get; set; } =
    [
        "node_modules/**", "dist/**", "bin/**", "obj/**",
        ".git/**", "coverage/**", "*.log", ".next/**",
    ];

    /// <summary>Embedding model to use for codebase indexing.</summary>
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";

    /// <summary>Maximum chunk size in tokens for tree-sitter splitting.</summary>
    public int MaxChunkTokens { get; set; } = 512;
}
