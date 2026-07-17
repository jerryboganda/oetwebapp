using System.Text.Json;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.AiTools;

namespace OetWithDrHesham.Api.Services.AiAssistant.Tools;

/// <summary>
/// Lists files and directories within the project.
/// Enforces a directory whitelist and max recursive depth of 3.
/// Returns array of { name, type, size } objects.
/// </summary>
public sealed class ListDirectoryTool : IAiToolExecutor
{
    public string Code => "list_directory";
    public AiToolCategory Category => AiToolCategory.Read;
    public string JsonSchemaArgs => """
    {
      "type":"object",
      "properties":{
        "path":{"type":"string","minLength":1,"maxLength":500},
        "recursive":{"type":"boolean"},
        "maxDepth":{"type":"integer","minimum":1,"maximum":3}
      },
      "required":["path"],
      "additionalProperties":false
    }
    """;

    private static readonly string[] AllowedPrefixes = { "app/", "components/", "lib/", "backend/src/" };
    private static readonly string[] BlockedSegments = { ".env", "secrets", "node_modules", ".git" };
    private const int DefaultMaxDepth = 2;
    private const int AbsoluteMaxDepth = 3;

    private readonly ILogger<ListDirectoryTool> _logger;

    public ListDirectoryTool(ILogger<ListDirectoryTool> logger)
    {
        _logger = logger;
    }

    public Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
        var path = args.GetProperty("path").GetString()!.Trim();
        var recursive = args.TryGetProperty("recursive", out var r) && r.GetBoolean();
        var maxDepth = args.TryGetProperty("maxDepth", out var d) ? d.GetInt32() : DefaultMaxDepth;
        if (maxDepth > AbsoluteMaxDepth) maxDepth = AbsoluteMaxDepth;
        if (maxDepth < 1) maxDepth = 1;

        // Normalize path
        path = path.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrEmpty(path)) path = ".";

        // Block path traversal
        if (path.Contains(".."))
        {
            return Task.FromResult(new AiToolExecutionResult(
                AiToolOutcome.RbacDenied, null, "path_traversal",
                "Path traversal is not allowed."));
        }

        // Security: check allowed prefixes (allow "." to list root-level allowed dirs)
        if (path != "." && !AllowedPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult(new AiToolExecutionResult(
                AiToolOutcome.RbacDenied, null, "path_denied",
                $"Path must start with one of: {string.Join(", ", AllowedPrefixes)}"));
        }

        // Security: block sensitive segments
        var segments = path.Split('/');
        foreach (var segment in segments)
        {
            if (BlockedSegments.Any(b => segment.Equals(b, StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult(new AiToolExecutionResult(
                    AiToolOutcome.RbacDenied, null, "path_blocked",
                    "Access to this path is blocked for security reasons."));
            }
        }

        var repoRoot = FindRepoRoot();
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, path));

        if (!fullPath.StartsWith(repoRoot, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new AiToolExecutionResult(
                AiToolOutcome.RbacDenied, null, "path_escape",
                "Resolved path escapes the repository root."));
        }

        if (!Directory.Exists(fullPath))
        {
            return Task.FromResult(new AiToolExecutionResult(
                AiToolOutcome.Success,
                ToJson(new { found = false, path, error = "Directory not found." })));
        }

        try
        {
            var entries = new List<object>();
            CollectEntries(fullPath, repoRoot, entries, recursive, maxDepth, currentDepth: 0);

            return Task.FromResult(new AiToolExecutionResult(
                AiToolOutcome.Success,
                ToJson(new { found = true, path, entries })));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ListDirectoryTool failed for path {Path}", path);
            return Task.FromResult(new AiToolExecutionResult(
                AiToolOutcome.ProviderError, null, "list_error", ex.Message));
        }
    }

    private static void CollectEntries(string dirPath, string repoRoot, List<object> entries, bool recursive, int maxDepth, int currentDepth)
    {
        if (currentDepth > maxDepth) return;

        foreach (var dir in Directory.GetDirectories(dirPath))
        {
            var name = Path.GetFileName(dir);
            if (BlockedSegments.Any(b => name.Equals(b, StringComparison.OrdinalIgnoreCase))) continue;

            var relativePath = Path.GetRelativePath(repoRoot, dir).Replace('\\', '/');
            entries.Add(new { name = relativePath, type = "directory", size = (long?)null });

            if (recursive && currentDepth < maxDepth)
            {
                CollectEntries(dir, repoRoot, entries, recursive, maxDepth, currentDepth + 1);
            }
        }

        foreach (var file in Directory.GetFiles(dirPath))
        {
            var fileName = Path.GetFileName(file);
            if (BlockedSegments.Any(b => fileName.StartsWith(b, StringComparison.OrdinalIgnoreCase))) continue;

            var relativePath = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            var fi = new FileInfo(file);
            entries.Add(new { name = relativePath, type = "file", size = (long?)fi.Length });
        }
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git"))) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return Directory.GetCurrentDirectory();
    }

    private static JsonElement ToJson(object payload) =>
        JsonDocument.Parse(JsonSerializer.Serialize(payload)).RootElement.Clone();
}
