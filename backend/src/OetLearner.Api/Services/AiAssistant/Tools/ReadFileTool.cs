using System.Text;
using System.Text.Json;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiTools;

namespace OetLearner.Api.Services.AiAssistant.Tools;

/// <summary>
/// Reads a file from the project directory (admin/expert use).
/// Enforces a directory whitelist and blocks sensitive paths.
/// Max 500 lines per read; returns content with line numbers.
/// </summary>
public sealed class ReadFileTool : IAiToolExecutor
{
    public string Code => "read_file";
    public AiToolCategory Category => AiToolCategory.Read;
    public string JsonSchemaArgs => """
    {
      "type":"object",
      "properties":{
        "path":{"type":"string","minLength":1,"maxLength":500},
        "startLine":{"type":"integer","minimum":1},
        "endLine":{"type":"integer","minimum":1}
      },
      "required":["path"],
      "additionalProperties":false
    }
    """;

    private static readonly string[] AllowedPrefixes = RepoRootResolver.AllowedPrefixes;
    private static readonly string[] BlockedSegments = RepoRootResolver.BlockedSegments;
    private const int MaxLines = 500;

    private readonly IConfiguration _configuration;
    private readonly ILogger<ReadFileTool> _logger;

    public ReadFileTool(IConfiguration configuration, ILogger<ReadFileTool> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

public Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
    // Source tree is admin-only, enforced here and not only by the grant table.
    var refusal = AdminOnlyToolGuard.Refusal(ctx, Code);
    if (refusal is not null) return Task.FromResult(refusal);

    var path = args.GetProperty("path").GetString()!.Trim();
        var startLine = args.TryGetProperty("startLine", out var s) ? s.GetInt32() : 1;
        var endLine = args.TryGetProperty("endLine", out var e) ? e.GetInt32() : (int?)null;

        // Normalize path separators
        path = path.Replace('\\', '/').TrimStart('/');

        // Security: check allowed prefixes
        if (!AllowedPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult(new AiToolExecutionResult(
                AiToolOutcome.RbacDenied, null, "path_denied",
                $"Path must start with one of: {string.Join(", ", AllowedPrefixes)}"));
        }

        // Security: block sensitive segments
        var segments = path.Split('/');
        foreach (var segment in segments)
        {
            if (BlockedSegments.Any(b => segment.Equals(b, StringComparison.OrdinalIgnoreCase) ||
                                         segment.StartsWith(b, StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult(new AiToolExecutionResult(
                    AiToolOutcome.RbacDenied, null, "path_blocked",
                    "Access to this path is blocked for security reasons."));
            }
        }

        // Block path traversal
        if (path.Contains(".."))
        {
            return Task.FromResult(new AiToolExecutionResult(
                AiToolOutcome.RbacDenied, null, "path_traversal",
                "Path traversal is not allowed."));
        }

        // Resolve relative to repository root
        var resolution = RepoRootResolver.Resolve(_configuration);
        if (!resolution.HasSource)
        {
            return Task.FromResult(new AiToolExecutionResult(
                AiToolOutcome.ProviderError, null, "codebase_source_unavailable",
                "No project source is available in this deployment. " + resolution.Reason));
        }

        var repoRoot = resolution.Root!;
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, path));

        // Ensure resolved path is still within repo
        if (!fullPath.StartsWith(repoRoot, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new AiToolExecutionResult(
                AiToolOutcome.RbacDenied, null, "path_escape",
                "Resolved path escapes the repository root."));
        }

        if (!File.Exists(fullPath))
        {
            return Task.FromResult(new AiToolExecutionResult(
                AiToolOutcome.Success,
                ToJson(new { found = false, path, error = "File not found." })));
        }

        try
        {
            var allLines = File.ReadAllLines(fullPath);
            if (startLine < 1) startLine = 1;
            if (endLine == null || endLine > allLines.Length) endLine = allLines.Length;
            if (startLine > allLines.Length)
            {
                return Task.FromResult(new AiToolExecutionResult(
                    AiToolOutcome.Success,
                    ToJson(new { found = true, path, totalLines = allLines.Length, content = "", note = "startLine exceeds file length." })));
            }

            // Enforce max lines
            var lineCount = endLine.Value - startLine + 1;
            if (lineCount > MaxLines)
            {
                endLine = startLine + MaxLines - 1;
                lineCount = MaxLines;
            }

            var sb = new StringBuilder();
            for (var i = startLine - 1; i < endLine.Value && i < allLines.Length; i++)
            {
                sb.AppendLine($"{i + 1}. {allLines[i]}");
            }

            return Task.FromResult(new AiToolExecutionResult(
                AiToolOutcome.Success,
                ToJson(new
                {
                    found = true,
                    path,
                    totalLines = allLines.Length,
                    startLine,
                    endLine = endLine.Value,
                    content = sb.ToString()
                })));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ReadFileTool failed for path {Path}", path);
            return Task.FromResult(new AiToolExecutionResult(
                AiToolOutcome.ProviderError, null, "read_error", ex.Message));
        }
    }

    private static JsonElement ToJson(object payload) =>
        JsonDocument.Parse(JsonSerializer.Serialize(payload)).RootElement.Clone();
}
