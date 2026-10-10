using System.Text.Json;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiTools;

namespace OetLearner.Api.Services.AiAssistant.Tools;

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

    private static readonly string[] AllowedPrefixes = RepoRootResolver.AllowedPrefixes;
    private static readonly string[] BlockedSegments = RepoRootResolver.BlockedSegments;
    private const int DefaultMaxDepth = 2;
    private const int AbsoluteMaxDepth = 3;

    private readonly IConfiguration _configuration;
    private readonly ILogger<ListDirectoryTool> _logger;

    public ListDirectoryTool(IConfiguration configuration, ILogger<ListDirectoryTool> logger)
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

        var resolution = RepoRootResolver.Resolve(_configuration);

        // No source mounted: say so plainly. The old code resolved the root to the publish
        // directory and happily listed ~60 DLLs, which is what an admin assistant sees today.
        if (!resolution.HasSource)
        {
            _logger.LogWarning("list_directory refused: no project source available. {Reason}", resolution.Reason);
            return Task.FromResult(new AiToolExecutionResult(
                AiToolOutcome.ProviderError, null, "codebase_source_unavailable",
                "No project source is available in this deployment. " + resolution.Reason));
        }

        var repoRoot = resolution.Root!;
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, path));

        if (!fullPath.StartsWith(repoRoot, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new AiToolExecutionResult(
                AiToolOutcome.RbacDenied, null, "path_escape",
                "Resolved path escapes the repository root."));
        }

        // A missing path is a FAILURE, not a success that happens to say "not found"
        // (owner directive 2026-10-09). As a Success it was indistinguishable from an empty
        // directory, so the model kept probing prefixes that do not exist in this deployment.
        // The response names which prefixes DO exist, so it stops guessing.
        if (!Directory.Exists(fullPath))
        {
            return Task.FromResult(new AiToolExecutionResult(
                AiToolOutcome.ProviderError, null, "directory_not_found",
                $"'{path}' does not exist in this checkout. Available source roots are: "
                + string.Join(", ", resolution.SearchablePrefixes) + "."));
        }

        try
        {
            var entries = new List<object>();
            CollectEntries(fullPath, repoRoot, entries, recursive, maxDepth, currentDepth: 0, out var truncated);

            return Task.FromResult(new AiToolExecutionResult(
                AiToolOutcome.Success,
                ToJson(new
                {
                    found = true,
                    path,
                    entries,
                    truncated,
                    // Say so when the listing was cut short. Without this the model cannot tell a
                    // complete listing from a partial one and may conclude a file is absent.
                    note = truncated ? $"Listing truncated at {MaxEntries} entries; list a subdirectory for more." : null
                })));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ListDirectoryTool failed for path {Path}", path);
            return Task.FromResult(new AiToolExecutionResult(
                AiToolOutcome.ProviderError, null, "list_error", ex.Message));
        }
    }

    private const int MaxEntries = 200;

    private static void CollectEntries(
        string dirPath, string repoRoot, List<object> entries, bool recursive, int maxDepth, int currentDepth, out bool truncated)
    {
        truncated = false;
        if (currentDepth > maxDepth) return;

        foreach (var dir in Directory.GetDirectories(dirPath))
        {
            if (entries.Count >= MaxEntries) { truncated = true; return; }
            var name = Path.GetFileName(dir);
            if (BlockedSegments.Any(b => name.Equals(b, StringComparison.OrdinalIgnoreCase))) continue;

            var relativePath = Path.GetRelativePath(repoRoot, dir).Replace('\\', '/');
            entries.Add(new { name = relativePath, type = "directory", size = (long?)null });

            if (recursive && currentDepth < maxDepth)
            {
                CollectEntries(dir, repoRoot, entries, recursive, maxDepth, currentDepth + 1, out var deeperTruncated);
                if (deeperTruncated) { truncated = true; return; }
            }
        }

        foreach (var file in Directory.GetFiles(dirPath))
        {
            if (entries.Count >= MaxEntries) { truncated = true; return; }
            var fileName = Path.GetFileName(file);
            if (BlockedSegments.Any(b => fileName.StartsWith(b, StringComparison.OrdinalIgnoreCase))) continue;

            var relativePath = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            var fi = new FileInfo(file);
            entries.Add(new { name = relativePath, type = "file", size = (long?)fi.Length });
        }
    }

    private static JsonElement ToJson(object payload) =>
        JsonDocument.Parse(JsonSerializer.Serialize(payload)).RootElement.Clone();
}
