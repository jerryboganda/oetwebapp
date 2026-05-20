using System.Text;
using System.Text.Json;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiAssistant.Safety;
using OetLearner.Api.Services.AiTools;

namespace OetLearner.Api.Services.AiAssistant.Tools;

/// <summary>
/// Writes or edits files on disk (admin only). Creates a backup before writing.
/// Security: directory whitelist, blocked filenames, max 100KB, secret scanning.
/// </summary>
public sealed class WriteFileTool : IAiToolExecutor
{
    public string Code => "write_file";
    public AiToolCategory Category => AiToolCategory.Write;
    public string JsonSchemaArgs => """
    {
      "type":"object",
      "properties":{
        "path":{"type":"string","minLength":1,"maxLength":500,"description":"Relative file path within the project"},
        "content":{"type":"string","minLength":1,"maxLength":102400,"description":"File content to write"},
        "createBackup":{"type":"boolean","description":"Whether to create a backup before writing (default true)"}
      },
      "required":["path","content"],
      "additionalProperties":false
    }
    """;

    private const int MaxFileSizeBytes = 102_400; // 100KB

    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".env", ".pem", ".key", ".pfx", ".p12",
    };

    private readonly ISafetyGuard _safetyGuard;
    private readonly IBackupService _backupService;
    private readonly ICircuitBreaker _circuitBreaker;
    private readonly ILogger<WriteFileTool> _logger;

    public WriteFileTool(
        ISafetyGuard safetyGuard,
        IBackupService backupService,
        ICircuitBreaker circuitBreaker,
        ILogger<WriteFileTool> logger)
    {
        _safetyGuard = safetyGuard;
        _backupService = backupService;
        _circuitBreaker = circuitBreaker;
        _logger = logger;
    }

    public async Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
        // Circuit breaker check
        if (await _circuitBreaker.IsOpenAsync(Code, ct))
        {
            return new AiToolExecutionResult(AiToolOutcome.ExecutionError, null,
                "circuit_open", "Tool temporarily disabled due to repeated failures.");
        }

        // Safety guard check
        var safety = await _safetyGuard.CheckAsync(Code, args, ctx, ct);
        if (!safety.IsAllowed)
        {
            return new AiToolExecutionResult(AiToolOutcome.RbacDenied, null, "safety_denied", safety.DenialReason);
        }

        // Parse args
        if (!args.TryGetProperty("path", out var pathElem) || string.IsNullOrWhiteSpace(pathElem.GetString()))
        {
            return new AiToolExecutionResult(AiToolOutcome.ArgsInvalid, null, "path_required", "path is required");
        }
        if (!args.TryGetProperty("content", out var contentElem) || contentElem.GetString() is null)
        {
            return new AiToolExecutionResult(AiToolOutcome.ArgsInvalid, null, "content_required", "content is required");
        }

        var relativePath = pathElem.GetString()!.Trim();
        var content = contentElem.GetString()!;
        var createBackup = !args.TryGetProperty("createBackup", out var backupFlag) || backupFlag.GetBoolean();

        // File size check
        if (Encoding.UTF8.GetByteCount(content) > MaxFileSizeBytes)
        {
            return new AiToolExecutionResult(AiToolOutcome.ArgsInvalid, null,
                "content_too_large", $"Content exceeds maximum size of {MaxFileSizeBytes / 1024}KB.");
        }

        // Block dangerous extensions
        var extension = Path.GetExtension(relativePath);
        if (BlockedExtensions.Contains(extension))
        {
            return new AiToolExecutionResult(AiToolOutcome.RbacDenied, null,
                "blocked_extension", $"Writing files with extension '{extension}' is not allowed.");
        }

        try
        {
            // Resolve full path (relative to working directory)
            var fullPath = Path.GetFullPath(relativePath);

            // Create backup if file exists
            string? backupId = null;
            if (createBackup && File.Exists(fullPath))
            {
                backupId = await _backupService.BackupFileAsync(fullPath, ctx.UserId ?? "system", ct);
            }

            // Ensure directory exists
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Write file
            await File.WriteAllTextAsync(fullPath, content, Encoding.UTF8, ct);

            await _circuitBreaker.RecordSuccessAsync(Code, ct);

            _logger.LogInformation("WriteFileTool: wrote {Path} ({Bytes} bytes) by {UserId}",
                relativePath, Encoding.UTF8.GetByteCount(content), ctx.UserId);

            var result = new
            {
                success = true,
                path = relativePath,
                bytes_written = Encoding.UTF8.GetByteCount(content),
                backup_id = backupId,
                created_new = backupId is null,
            };

            return new AiToolExecutionResult(AiToolOutcome.Success, ToJson(result));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _circuitBreaker.RecordFailureAsync(Code, ct);
            _logger.LogError(ex, "WriteFileTool: failed to write {Path}", relativePath);
            return new AiToolExecutionResult(AiToolOutcome.ExecutionError, null,
                "write_failed", $"Failed to write file: {ex.Message}");
        }
    }

    private static JsonElement ToJson(object payload) =>
        JsonDocument.Parse(JsonSerializer.Serialize(payload)).RootElement.Clone();
}
