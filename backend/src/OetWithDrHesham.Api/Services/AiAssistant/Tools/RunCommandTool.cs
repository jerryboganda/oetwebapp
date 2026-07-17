using System.Diagnostics;
using System.Text.Json;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.AiAssistant.Safety;
using OetWithDrHesham.Api.Services.AiTools;

namespace OetWithDrHesham.Api.Services.AiAssistant.Tools;

/// <summary>
/// Executes shell commands (admin only, heavily restricted).
/// Only allowlisted commands are permitted. Max timeout 120 seconds.
/// </summary>
public sealed class RunCommandTool : IAiToolExecutor
{
    public string Code => "run_command";
    public AiToolCategory Category => AiToolCategory.Write;
    public string JsonSchemaArgs => """
    {
      "type":"object",
      "properties":{
        "command":{"type":"string","minLength":1,"maxLength":500,"description":"The command to execute (must be in allowlist)"},
        "workingDirectory":{"type":"string","maxLength":500,"description":"Working directory (optional)"},
        "timeout":{"type":"integer","minimum":1,"maximum":120,"description":"Timeout in seconds (default 60, max 120)"}
      },
      "required":["command"],
      "additionalProperties":false
    }
    """;

    private const int MaxTimeoutSeconds = 120;
    private const int DefaultTimeoutSeconds = 60;

    /// <summary>
    /// Allowlist of commands. Only exact matches (or prefix matches for parameterized commands) are allowed.
    /// </summary>
    private static readonly string[] AllowedCommands =
    [
        "pnpm test",
        "pnpm run lint",
        "pnpm run build",
        "pnpm exec tsc --noEmit",
        "dotnet build",
        "dotnet test",
        "git status",
        "git log",
        "git diff",
    ];

    private readonly ISafetyGuard _safetyGuard;
    private readonly ICircuitBreaker _circuitBreaker;
    private readonly ILogger<RunCommandTool> _logger;

    public RunCommandTool(
        ISafetyGuard safetyGuard,
        ICircuitBreaker circuitBreaker,
        ILogger<RunCommandTool> logger)
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

        // Parse args
        if (!args.TryGetProperty("command", out var cmdElem) || string.IsNullOrWhiteSpace(cmdElem.GetString()))
        {
            return new AiToolExecutionResult(AiToolOutcome.ArgsInvalid, null, "command_required", "command is required");
        }

        var command = cmdElem.GetString()!.Trim();
        var workingDir = args.TryGetProperty("workingDirectory", out var wdElem) ? wdElem.GetString() : null;
        var timeout = args.TryGetProperty("timeout", out var toElem) ? toElem.GetInt32() : DefaultTimeoutSeconds;
        timeout = Math.Clamp(timeout, 1, MaxTimeoutSeconds);

        // Allowlist check
        if (!IsCommandAllowed(command))
        {
            _logger.LogWarning("RunCommandTool: blocked command '{Command}' by {UserId}", command, ctx.UserId);
            return new AiToolExecutionResult(AiToolOutcome.RbacDenied, null,
                "command_not_allowed", $"Command '{command}' is not in the allowlist. Allowed: {string.Join(", ", AllowedCommands)}");
        }

        try
        {
            var (exitCode, stdout, stderr, timedOut) = await RunProcessAsync(command, workingDir, timeout, ct);

            await _circuitBreaker.RecordSuccessAsync(Code, ct);

            // Truncate output to avoid massive payloads
            const int maxOutputLen = 8000;
            if (stdout.Length > maxOutputLen) stdout = stdout[..maxOutputLen] + "\n... [truncated]";
            if (stderr.Length > maxOutputLen) stderr = stderr[..maxOutputLen] + "\n... [truncated]";

            var result = new
            {
                exitCode,
                stdout,
                stderr,
                timedOut,
                command,
                timeout_seconds = timeout,
            };

            return new AiToolExecutionResult(AiToolOutcome.Success, ToJson(result));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _circuitBreaker.RecordFailureAsync(Code, ct);
            _logger.LogError(ex, "RunCommandTool: failed executing '{Command}'", command);
            return new AiToolExecutionResult(AiToolOutcome.ExecutionError, null,
                "execution_failed", $"Command execution failed: {ex.Message}");
        }
    }

    private static bool IsCommandAllowed(string command)
    {
        return AllowedCommands.Any(allowed =>
            command.Equals(allowed, StringComparison.OrdinalIgnoreCase) ||
            command.StartsWith(allowed + " ", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr, bool TimedOut)> RunProcessAsync(
        string command, string? workingDirectory, int timeoutSeconds, CancellationToken ct)
    {
        var isWindows = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "cmd.exe" : "/bin/sh",
            Arguments = isWindows ? $"/c {command}" : $"-c \"{command.Replace("\"", "\\\"")}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (!string.IsNullOrEmpty(workingDirectory) && Directory.Exists(workingDirectory))
        {
            psi.WorkingDirectory = workingDirectory;
        }

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        var completed = process.WaitForExit(timeoutSeconds * 1000);
        if (!completed)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            return (-1, stdout, stderr, true);
        }

        return (process.ExitCode, await stdoutTask, await stderrTask, false);
    }

    private static JsonElement ToJson(object payload) =>
        JsonDocument.Parse(JsonSerializer.Serialize(payload)).RootElement.Clone();
}
