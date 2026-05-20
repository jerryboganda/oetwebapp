using System.Text.Json;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiTools;

namespace OetLearner.Api.Services.AiAssistant.Tools;

/// <summary>
/// Text search across the codebase using simple string matching.
/// Enforces the same directory whitelist. Returns matches with file path,
/// line number, and surrounding context. Max 50 results.
/// </summary>
public sealed class SearchCodebaseTool : IAiToolExecutor
{
    public string Code => "search_codebase";
    public AiToolCategory Category => AiToolCategory.Read;
    public string JsonSchemaArgs => """
    {
      "type":"object",
      "properties":{
        "query":{"type":"string","minLength":1,"maxLength":200},
        "filePattern":{"type":"string","maxLength":100},
        "maxResults":{"type":"integer","minimum":1,"maximum":50}
      },
      "required":["query"],
      "additionalProperties":false
    }
    """;

    private static readonly string[] AllowedPrefixes = { "app/", "components/", "lib/", "backend/src/" };
    private static readonly string[] BlockedSegments = { ".env", "secrets", "node_modules", ".git", "bin", "obj" };
    private const int DefaultMaxResults = 20;
    private const int AbsoluteMaxResults = 50;
    private const int ContextLines = 2;

    private readonly ILogger<SearchCodebaseTool> _logger;

    public SearchCodebaseTool(ILogger<SearchCodebaseTool> logger)
    {
        _logger = logger;
    }

    public Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
        var query = args.GetProperty("query").GetString()!;
        var filePattern = args.TryGetProperty("filePattern", out var fp) ? fp.GetString() : null;
        var maxResults = args.TryGetProperty("maxResults", out var mr) ? mr.GetInt32() : DefaultMaxResults;
        if (maxResults > AbsoluteMaxResults) maxResults = AbsoluteMaxResults;
        if (maxResults < 1) maxResults = 1;

        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult(new AiToolExecutionResult(
                AiToolOutcome.ArgsInvalid, null, "empty_query", "Query cannot be empty."));
        }

        var repoRoot = FindRepoRoot();
        var matches = new List<object>();

        try
        {
            foreach (var prefix in AllowedPrefixes)
            {
                if (ct.IsCancellationRequested) break;
                if (matches.Count >= maxResults) break;

                var searchDir = Path.Combine(repoRoot, prefix.TrimEnd('/'));
                if (!Directory.Exists(searchDir)) continue;

                SearchDirectory(searchDir, repoRoot, query, filePattern, matches, maxResults, ct);
            }

            return Task.FromResult(new AiToolExecutionResult(
                AiToolOutcome.Success,
                ToJson(new { query, totalMatches = matches.Count, matches })));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(new AiToolExecutionResult(
                AiToolOutcome.Success,
                ToJson(new { query, totalMatches = matches.Count, matches, truncated = true })));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SearchCodebaseTool failed for query {Query}", query);
            return Task.FromResult(new AiToolExecutionResult(
                AiToolOutcome.ProviderError, null, "search_error", ex.Message));
        }
    }

    private static void SearchDirectory(string dirPath, string repoRoot, string query, string? filePattern, List<object> matches, int maxResults, CancellationToken ct)
    {
        // Skip blocked directories
        var dirName = Path.GetFileName(dirPath);
        if (BlockedSegments.Any(b => dirName.Equals(b, StringComparison.OrdinalIgnoreCase))) return;

        IEnumerable<string> files;
        try
        {
            var pattern = string.IsNullOrWhiteSpace(filePattern) ? "*" : filePattern;
            files = Directory.EnumerateFiles(dirPath, pattern);
        }
        catch
        {
            return;
        }

        foreach (var file in files)
        {
            if (ct.IsCancellationRequested || matches.Count >= maxResults) return;

            var fileName = Path.GetFileName(file);
            if (BlockedSegments.Any(b => fileName.StartsWith(b, StringComparison.OrdinalIgnoreCase))) continue;

            // Skip binary files by extension
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (IsBinaryExtension(ext)) continue;

            try
            {
                var lines = File.ReadAllLines(file);
                var relativePath = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');

                for (var i = 0; i < lines.Length && matches.Count < maxResults; i++)
                {
                    if (lines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        var contextStart = Math.Max(0, i - ContextLines);
                        var contextEnd = Math.Min(lines.Length - 1, i + ContextLines);
                        var context = new List<string>();
                        for (var j = contextStart; j <= contextEnd; j++)
                        {
                            context.Add($"{j + 1}. {lines[j]}");
                        }

                        matches.Add(new
                        {
                            file = relativePath,
                            line = i + 1,
                            matchedLine = lines[i].Trim(),
                            context = string.Join("\n", context)
                        });
                    }
                }
            }
            catch
            {
                // Skip files that can't be read
            }
        }

        // Recurse into subdirectories
        try
        {
            foreach (var subDir in Directory.EnumerateDirectories(dirPath))
            {
                if (ct.IsCancellationRequested || matches.Count >= maxResults) return;
                SearchDirectory(subDir, repoRoot, query, filePattern, matches, maxResults, ct);
            }
        }
        catch
        {
            // Skip directories that can't be read
        }
    }

    private static bool IsBinaryExtension(string ext) => ext switch
    {
        ".dll" or ".exe" or ".bin" or ".obj" or ".pdb" or ".png" or ".jpg" or ".jpeg" or
        ".gif" or ".ico" or ".woff" or ".woff2" or ".ttf" or ".eot" or ".zip" or ".gz" or
        ".tar" or ".pdf" or ".mp3" or ".mp4" or ".avi" or ".mov" => true,
        _ => false
    };

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
