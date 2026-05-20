using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;

namespace OetLearner.Api.Endpoints;

public static class CopilotExtensionEndpoints
{
    private static readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)> RateLimits = new();
    private const int MaxRequestsPerMinute = 60;

    public static void MapCopilotExtensionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/copilot")
            .WithTags("Copilot Extension")
            .AddEndpointFilter<CopilotAuthFilter>();

        group.MapPost("/search", SearchAsync);
        group.MapPost("/explain", ExplainAsync);
        group.MapPost("/test", RunTestsAsync);
        group.MapGet("/deploy/status", GetDeployStatusAsync);
    }

    // --- Search ---

    private static async Task<IResult> SearchAsync(
        [FromBody] CopilotSearchRequest request,
        HttpContext ctx,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return Results.BadRequest(new { error = "Query is required" });

        // Simple file search across known source directories
        var results = await Task.Run(() => SearchFiles(request.Query), ct);
        return Results.Ok(new { results });
    }

    private static List<SearchResult> SearchFiles(string query)
    {
        var results = new List<SearchResult>();
        var searchDirs = new[]
        {
            "app", "components", "lib", "backend/src", "pages"
        };

        var queryTerms = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var dir in searchDirs)
        {
            if (!Directory.Exists(dir)) continue;

            try
            {
                var files = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".ts") || f.EndsWith(".tsx") || f.EndsWith(".cs") || f.EndsWith(".js"))
                    .Where(f => queryTerms.Any(t => Path.GetFileName(f).ToLowerInvariant().Contains(t)))
                    .Take(20);

                foreach (var file in files)
                {
                    var snippet = TryGetSnippet(file, queryTerms);
                    results.Add(new SearchResult(file.Replace('\\', '/'), snippet));

                    if (results.Count >= 10) return results;
                }
            }
            catch
            {
                // Skip directories we can't read
            }
        }

        return results;
    }

    private static string? TryGetSnippet(string filePath, string[] queryTerms)
    {
        try
        {
            var lines = File.ReadLines(filePath).Take(200).ToArray();
            for (int i = 0; i < lines.Length; i++)
            {
                if (queryTerms.Any(t => lines[i].ToLowerInvariant().Contains(t)))
                {
                    var start = Math.Max(0, i - 2);
                    var end = Math.Min(lines.Length, i + 3);
                    return string.Join('\n', lines[start..end]);
                }
            }
        }
        catch { }
        return null;
    }

    // --- Explain ---

    private static async Task<IResult> ExplainAsync(
        [FromBody] CopilotExplainRequest request,
        HttpContext ctx,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.FilePath))
            return Results.BadRequest(new { error = "FilePath is required" });

        var safePath = request.FilePath.Replace("..", "").TrimStart('/').TrimStart('\\');

        if (!File.Exists(safePath))
            return Results.NotFound(new { error = $"File not found: {safePath}" });

        var content = await File.ReadAllTextAsync(safePath, ct);
        var truncated = content.Length > 5000 ? content[..5000] + "\n// ... truncated" : content;

        // Generate a basic structural explanation
        var explanation = GenerateExplanation(safePath, truncated);
        return Results.Ok(new { explanation });
    }

    private static string GenerateExplanation(string path, string content)
    {
        var ext = Path.GetExtension(path);
        var lines = content.Split('\n');
        var lineCount = lines.Length;

        var explanation = $"## {Path.GetFileName(path)}\n\n";
        explanation += $"**Path:** `{path}`\n";
        explanation += $"**Type:** {GetFileType(ext)}\n";
        explanation += $"**Lines:** {lineCount}\n\n";

        // Extract exports/classes/functions
        var exports = lines.Where(l => l.TrimStart().StartsWith("export ")).Take(10).ToArray();
        if (exports.Length > 0)
        {
            explanation += "### Exports\n";
            foreach (var e in exports)
                explanation += $"- `{e.Trim()}`\n";
        }

        return explanation;
    }

    private static string GetFileType(string ext) => ext switch
    {
        ".ts" or ".tsx" => "TypeScript",
        ".js" or ".jsx" => "JavaScript",
        ".cs" => "C#",
        ".css" => "CSS",
        ".json" => "JSON",
        _ => "Unknown"
    };

    // --- Tests ---

    private static async Task<IResult> RunTestsAsync(
        [FromBody] CopilotTestRequest request,
        HttpContext ctx,
        CancellationToken ct)
    {
        var scope = string.IsNullOrWhiteSpace(request.Scope) ? "all" : request.Scope;

        // Return info about how to run tests (we don't actually execute them for safety)
        var command = scope.ToLowerInvariant() switch
        {
            "backend" => "dotnet test backend/",
            "frontend" => "npx vitest run",
            "all" => "dotnet test backend/ && npx vitest run",
            _ => $"npx vitest run {scope}"
        };

        await Task.CompletedTask;

        return Results.Ok(new
        {
            status = "info",
            message = $"To run tests for scope '{scope}', use the following command:",
            command,
            output = $"Test command: {command}\nNote: Tests should be triggered via CI/CD or locally.",
            passed = (int?)null,
            failed = (int?)null,
            skipped = (int?)null
        });
    }

    // --- Deploy Status ---

    private static async Task<IResult> GetDeployStatusAsync(HttpContext ctx, CancellationToken ct)
    {
        await Task.CompletedTask;

        // Return deployment status from environment or a static view
        var environments = new[]
        {
            new DeployEnvironment("production", "healthy", Environment.GetEnvironmentVariable("APP_VERSION") ?? "unknown", DateTime.UtcNow.ToString("u")),
            new DeployEnvironment("staging", "healthy", "latest", DateTime.UtcNow.ToString("u")),
        };

        return Results.Ok(new { environments });
    }

    // --- Auth Filter ---

    private class CopilotAuthFilter : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var httpContext = context.HttpContext;
            var token = httpContext.Request.Headers["X-Copilot-Token"].FirstOrDefault();
            var expectedToken = Environment.GetEnvironmentVariable("COPILOT_BACKEND_TOKEN");

            // If no token is configured, allow in development
            if (!string.IsNullOrEmpty(expectedToken))
            {
                if (string.IsNullOrEmpty(token) || token != expectedToken)
                {
                    return Results.Unauthorized();
                }
            }

            // Rate limiting
            var clientId = token ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (!CheckRateLimit(clientId))
            {
                return Results.StatusCode(429);
            }

            return await next(context);
        }

        private static bool CheckRateLimit(string clientId)
        {
            var now = DateTime.UtcNow;
            var entry = RateLimits.GetOrAdd(clientId, _ => (0, now));

            if ((now - entry.WindowStart).TotalMinutes >= 1)
            {
                RateLimits[clientId] = (1, now);
                return true;
            }

            if (entry.Count >= MaxRequestsPerMinute)
                return false;

            RateLimits[clientId] = (entry.Count + 1, entry.WindowStart);
            return true;
        }
    }

    // --- DTOs ---

    private record CopilotSearchRequest(string Query);
    private record CopilotExplainRequest(string FilePath);
    private record CopilotTestRequest(string? Scope);
    private record SearchResult(string Path, string? Snippet);
    private record DeployEnvironment(string Name, string Status, string? Version, string? LastDeployed);
}
