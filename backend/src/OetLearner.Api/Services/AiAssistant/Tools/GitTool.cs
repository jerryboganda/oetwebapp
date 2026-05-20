using System.Diagnostics;
using System.Text.Json;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiAssistant.Safety;
using OetLearner.Api.Services.AiTools;

namespace OetLearner.Api.Services.AiAssistant.Tools;

/// <summary>
/// Git operations tool (admin only).
/// Read operations (status, diff, log, branch): always allowed.
/// Write operations (commit): require explicit confirmation.
/// NEVER allows push, force, or rebase operations.
/// </summary>
public sealed class GitTool : IAiToolExecutor
{
    public string Code => "git_operations";
    public AiToolCategory Category => AiToolCategory.Write;
    public string JsonSchemaArgs => """
    {
      "type":"object",
      "properties":{
        "operation":{"type":"string","enum":["status","diff","log","branch","commit"],"description":"Git operation to perform"},
        "message":{"type":"string","minLength":1,"maxLength":200,"description":"Commit message (required for commit)"},
        "files":{"type":"array","items":{"type":"string","maxLength":500},"maxItems":50,"description":"Files to stage (for commit)"},
        "confirmed":{"type":"boolean","description":"Explicit confirmation required for write operations"}
      },
      "required":["operation"],
      "additionalProperties":false
    }
    """;

    private static readonly HashSet<string> ReadOperations = new(StringComparer.OrdinalIgnoreCase)
    {
        "status", "diff", "log", "branch"
    };

    private static readonly HashSet<string> WriteOperations = new(StringComparer.OrdinalIgnoreCase)
    {
        "commit"
    };

    // Operations that are NEVER allowed regardless of role
    private static readonly HashSet<string> ForbiddenOperations = new(StringComparer.OrdinalIgnoreCase)
    {
        "push", "force-push", "rebase", "reset", "clean",
    };

    private readonly ISafetyGuard _safetyGuard;
    private readonly ICircuitBreaker _circuitBreaker;
    private readonly ILogger<GitTool> _logger;

    public GitTool(
        ISafetyGuard safetyGuard,
        ICircuitBreaker circuitBreaker,
        ILogger<GitTool> logger)
    {
        _safetyGuard = safetyGuard;
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

        // Parse operation
        if (!args.TryGetProperty("operation", out var opElem) || string.IsNullOrWhiteSpace(opElem.GetString()))
        {
            return new AiToolExecutionResult(AiToolOutcome.ArgsInvalid, null, "operation_required", "operation is required");
        }

        var operation = opElem.GetString()!.Trim().ToLowerInvariant();

        // Absolute block on forbidden operations
        if (ForbiddenOperations.Contains(operation))
        {
            return new AiToolExecutionResult(AiToolOutcome.RbacDenied, null,
                "operation_forbidden", $"Operation '{operation}' is permanently forbidden. Push, force, and rebase are never allowed via this tool.");
        }

        // Validate operation is known
        if (!ReadOperations.Contains(operation) && !WriteOperations.Contains(operation))
        {
            return new AiToolExecutionResult(AiToolOutcome.ArgsInvalid, null,
                "operation_unknown", $"Unknown operation '{operation}'. Allowed: status, diff, log, branch, commit.");
        }

        // Write operations require confirmation
        if (WriteOperations.Contains(operation))
        {
            var confirmed = args.TryGetProperty("confirmed", out var confirmElem) && confirmElem.GetBoolean();
            if (!confirmed)
            {
                return new AiToolExecutionResult(AiToolOutcome.RbacDenied, null,
                    "confirmation_required", $"Write operation '{operation}' requires \"confirmed\": true in args.");
            }
        }

        try
        {
            var result = operation switch
            {
                "status" => await RunGitAsync("status --porcelain", ct),
                "diff" => await RunGitAsync("diff --stat", ct),
                "log" => await RunGitAsync("log --oneline -20", ct),
                "branch" => await RunGitAsync("branch -a", ct),
                "commit" => await HandleCommitAsync(args, ct),
                _ => throw new InvalidOperationException($"Unhandled operation: {operation}"),
            };

            await _circuitBreaker.RecordSuccessAsync(Code, ct);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _circuitBreaker.RecordFailureAsync(Code, ct);
            _logger.LogError(ex, "GitTool: failed operation '{Operation}'", operation);
            return new AiToolExecutionResult(AiToolOutcome.ExecutionError, null,
                "git_failed", $"Git operation failed: {ex.Message}");
        }
    }

    private async Task<AiToolExecutionResult> HandleCommitAsync(JsonElement args, CancellationToken ct)
    {
        // Commit requires a message
        if (!args.TryGetProperty("message", out var msgElem) || string.IsNullOrWhiteSpace(msgElem.GetString()))
        {
            return new AiToolExecutionResult(AiToolOutcome.ArgsInvalid, null,
                "message_required", "Commit requires a 'message' argument.");
        }

        var message = msgElem.GetString()!.Trim();

        // Stage files if specified, otherwise stage all
        if (args.TryGetProperty("files", out var filesElem) && filesElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var fileElem in filesElem.EnumerateArray())
            {
                var file = fileElem.GetString();
                if (!string.IsNullOrWhiteSpace(file))
                {
                    // Validate no path traversal
                    if (file.Contains(".."))
                    {
                        return new AiToolExecutionResult(AiToolOutcome.RbacDenied, null,
                            "path_traversal", "Path traversal (..) not allowed in file paths.");
                    }
                    await RunGitRawAsync($"add \"{file.Replace("\"", "\\\"")}\"", ct);
                }
            }
        }
        else
        {
            await RunGitRawAsync("add -A", ct);
        }

        // Commit with sanitized message
        var sanitizedMsg = message.Replace("\"", "'").Replace("`", "'");
        var (exitCode, stdout, stderr) = await RunGitRawAsync($"commit -m \"{sanitizedMsg}\"", ct);

        var result = new
        {
            operation = "commit",
            exitCode,
            output = stdout,
            error = stderr,
            message = sanitizedMsg,
        };

        return new AiToolExecutionResult(AiToolOutcome.Success, ToJson(result));
    }

    private static async Task<AiToolExecutionResult> RunGitAsync(string gitArgs, CancellationToken ct)
    {
        var (exitCode, stdout, stderr) = await RunGitRawAsync(gitArgs, ct);

        // Truncate for safety
        const int maxLen = 8000;
        if (stdout.Length > maxLen) stdout = stdout[..maxLen] + "\n... [truncated]";
        if (stderr.Length > maxLen) stderr = stderr[..maxLen] + "\n... [truncated]";

        var result = new
        {
            operation = gitArgs.Split(' ')[0],
            exitCode,
            output = stdout,
            error = stderr,
        };

        return new AiToolExecutionResult(AiToolOutcome.Success, ToJson(result));
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunGitRawAsync(string gitArgs, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = gitArgs,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        var completed = process.WaitForExit(30_000); // 30s max for git
        if (!completed)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return (process.ExitCode, stdout, stderr);
    }

    private static JsonElement ToJson(object payload) =>
        JsonDocument.Parse(JsonSerializer.Serialize(payload)).RootElement.Clone();
}
