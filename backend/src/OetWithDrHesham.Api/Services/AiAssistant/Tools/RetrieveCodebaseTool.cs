using System.Text;
using System.Text.Json;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.AiAssistant.Indexing;
using OetWithDrHesham.Api.Services.AiTools;

namespace OetWithDrHesham.Api.Services.AiAssistant.Tools;

/// <summary>
/// AI tool that the assistant can call to search the codebase semantically.
/// Uses ICodebaseRetriever for hybrid vector + keyword search.
/// </summary>
public sealed class RetrieveCodebaseTool : IAiToolExecutor
{
    private readonly ICodebaseRetriever _retriever;
    private readonly ILogger<RetrieveCodebaseTool> _logger;

    public string Code => "retrieve_codebase";

    public AiToolCategory Category => AiToolCategory.Read;

    public string JsonSchemaArgs => """
        {
            "type": "object",
            "properties": {
                "query": {
                    "type": "string",
                    "description": "Natural language or code search query to find relevant codebase chunks"
                },
                "maxResults": {
                    "type": "integer",
                    "description": "Maximum number of results to return (default: 10, max: 30)",
                    "minimum": 1,
                    "maximum": 30,
                    "default": 10
                }
            },
            "required": ["query"],
            "additionalProperties": false
        }
        """;

    public RetrieveCodebaseTool(
        ICodebaseRetriever retriever,
        ILogger<RetrieveCodebaseTool> logger)
    {
        _retriever = retriever ?? throw new ArgumentNullException(nameof(retriever));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AiToolExecutionResult> ExecuteAsync(
        JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
        string? query = null;
        int maxResults = 10;

        if (args.TryGetProperty("query", out var queryProp))
            query = queryProp.GetString();

        if (args.TryGetProperty("maxResults", out var maxProp) && maxProp.ValueKind == JsonValueKind.Number)
            maxResults = Math.Clamp(maxProp.GetInt32(), 1, 30);

        if (string.IsNullOrWhiteSpace(query))
        {
            return new AiToolExecutionResult(
                AiToolOutcome.ArgsInvalid,
                null,
                "MISSING_QUERY",
                "The 'query' argument is required and must be a non-empty string.");
        }

        try
        {
            var results = await _retriever.RetrieveAsync(query, maxResults, ct);

            if (results.Count == 0)
            {
                var emptyResult = JsonSerializer.SerializeToElement(new
                {
                    message = "No relevant code chunks found for the query.",
                    query,
                    results = Array.Empty<object>()
                });

                return new AiToolExecutionResult(AiToolOutcome.Success, emptyResult);
            }

            var formattedResults = results.Select(r => new
            {
                filePath = r.FilePath,
                startLine = r.StartLine,
                endLine = r.EndLine,
                symbol = r.Symbol,
                score = MathF.Round(r.Score, 4),
                content = TruncateContent(r.Content, 1500)
            }).ToList();

            var resultJson = JsonSerializer.SerializeToElement(new
            {
                query,
                totalResults = formattedResults.Count,
                results = formattedResults
            });

            _logger.LogDebug("RetrieveCodebase: query=\"{Query}\" returned {Count} results.", query, results.Count);

            return new AiToolExecutionResult(AiToolOutcome.Success, resultJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing retrieve_codebase tool for query: {Query}", query);

            return new AiToolExecutionResult(
                AiToolOutcome.ProviderError,
                null,
                "RETRIEVAL_ERROR",
                "An error occurred while searching the codebase. Please try again.");
        }
    }

    private static string TruncateContent(string content, int maxLength)
    {
        if (string.IsNullOrEmpty(content) || content.Length <= maxLength)
            return content;

        return content[..maxLength] + "\n... (truncated)";
    }
}
