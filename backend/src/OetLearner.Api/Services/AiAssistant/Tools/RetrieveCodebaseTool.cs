using System.Text;
using System.Text.Json;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiAssistant.Indexing;
using OetLearner.Api.Services.AiTools;

namespace OetLearner.Api.Services.AiAssistant.Tools;

/// <summary>
/// AI tool that the assistant can call to search the codebase semantically.
/// Uses ICodebaseRetriever for hybrid vector + keyword search.
/// </summary>
public sealed class RetrieveCodebaseTool : IAiToolExecutor
{
    private readonly ICodebaseRetriever _retriever;
    private readonly ICodebaseIndexer _indexer;
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
        ICodebaseIndexer indexer,
        ILogger<RetrieveCodebaseTool> logger)
    {
        _retriever = retriever;
        _indexer = indexer;
        _logger = logger;
    }

public async Task<AiToolExecutionResult> ExecuteAsync(
    JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
    // Source tree is admin-only, enforced here and not only by the grant table.
    var refusal = AdminOnlyToolGuard.Refusal(ctx, Code);
    if (refusal is not null) return refusal;

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
                // An EMPTY INDEX and a search that genuinely matched nothing are different states and
                // must not both read as "No relevant code chunks found" (owner directive 2026-10-09).
                // With no source mounted the index is always empty, so the old message told the model
                // the code did not contain the answer when in fact nobody had ever looked.
                var status = await _indexer.GetStatusAsync(ct);
                if (!status.SourceAvailable)
                {
                    _logger.LogWarning("retrieve_codebase refused: no source available. {Reason}", status.SourceRootReason);
                    return new AiToolExecutionResult(
                        AiToolOutcome.ProviderError, null, "codebase_source_unavailable",
                        "Codebase retrieval is unavailable in this deployment: "
                        + (status.SourceRootReason ?? "no project source could be resolved")
                        + " Nothing has been indexed, so this is NOT evidence that the code lacks the answer. "
                        + "Say the source is unavailable here rather than guessing.");
                }

                var emptyResult = JsonSerializer.SerializeToElement(new
                {
                    message = "No relevant code chunks found for the query. The index is populated, so this means the indexed source genuinely did not match.",
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
