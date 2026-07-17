using System.Text.Json;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.AiTools;

namespace OetWithDrHesham.Api.Services.AiAssistant.Tools;

/// <summary>
/// Abstraction for a web search provider. Implement this interface
/// when a real search API (e.g. Bing, Google, Brave) is configured.
/// </summary>
public interface IWebSearchProvider
{
    /// <summary>
    /// Performs a web search and returns structured results.
    /// </summary>
    Task<WebSearchResult> SearchAsync(string query, int maxResults, CancellationToken ct);

    /// <summary>Whether the provider is configured and ready to use.</summary>
    bool IsConfigured { get; }
}

/// <summary>
/// Structured result from a web search provider.
/// </summary>
public sealed record WebSearchResult(
    bool Success,
    IReadOnlyList<WebSearchItem> Items,
    string? ErrorMessage = null);

/// <summary>
/// A single web search result item.
/// </summary>
public sealed record WebSearchItem(
    string Title,
    string Url,
    string Snippet);

/// <summary>
/// Stub web search provider that always returns "not configured".
/// Replace with a real implementation when a search API key is available.
/// </summary>
public sealed class StubWebSearchProvider : IWebSearchProvider
{
    public bool IsConfigured => false;

    public Task<WebSearchResult> SearchAsync(string query, int maxResults, CancellationToken ct)
    {
        return Task.FromResult(new WebSearchResult(
            Success: false,
            Items: Array.Empty<WebSearchItem>(),
            ErrorMessage: "Web search provider is not configured. Please add a search API key to enable this feature."));
    }
}

/// <summary>
/// Searches the web for OET-related information.
/// Currently stubbed — returns a message indicating configuration is required.
/// When a real provider is registered, it will delegate to <see cref="IWebSearchProvider"/>.
/// </summary>
public sealed class WebSearchTool : IAiToolExecutor
{
    public string Code => "web_search";
    public AiToolCategory Category => AiToolCategory.ExternalNetwork;
    public string JsonSchemaArgs => """
    {
      "type":"object",
      "properties":{
        "query":{"type":"string","minLength":1,"maxLength":300},
        "maxResults":{"type":"integer","minimum":1,"maximum":10}
      },
      "required":["query"],
      "additionalProperties":false
    }
    """;

    private const int DefaultMaxResults = 5;

    private readonly IWebSearchProvider _provider;
    private readonly ILogger<WebSearchTool> _logger;

    public WebSearchTool(IWebSearchProvider provider, ILogger<WebSearchTool> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    public async Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
        var query = args.GetProperty("query").GetString()!.Trim();
        var maxResults = args.TryGetProperty("maxResults", out var mr) ? mr.GetInt32() : DefaultMaxResults;
        if (maxResults < 1) maxResults = 1;
        if (maxResults > 10) maxResults = 10;

        if (string.IsNullOrWhiteSpace(query))
        {
            return new AiToolExecutionResult(
                AiToolOutcome.ArgsInvalid, null, "empty_query",
                "Search query cannot be empty.");
        }

        // Check if provider is configured
        if (!_provider.IsConfigured)
        {
            return new AiToolExecutionResult(
                AiToolOutcome.Success,
                ToJson(new
                {
                    available = false,
                    message = "Web search is not yet configured. A search API provider (Bing, Google, or Brave) must be configured to enable this tool.",
                    query,
                    suggestion = "For OET-related information, try asking directly — the assistant has substantial built-in knowledge about OET exam preparation."
                }));
        }

        try
        {
            var result = await _provider.SearchAsync(query, maxResults, ct);

            if (!result.Success)
            {
                return new AiToolExecutionResult(
                    AiToolOutcome.ProviderError, null, "search_failed",
                    result.ErrorMessage ?? "Search failed.");
            }

            var items = result.Items.Select(i => new
            {
                title = i.Title,
                url = i.Url,
                snippet = i.Snippet
            }).ToList();

            return new AiToolExecutionResult(
                AiToolOutcome.Success,
                ToJson(new
                {
                    available = true,
                    query,
                    resultCount = items.Count,
                    results = items
                }));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WebSearchTool failed for query: {Query}", query);
            return new AiToolExecutionResult(
                AiToolOutcome.ProviderError, null, "search_error",
                $"Web search failed: {ex.Message}");
        }
    }

    private static JsonElement ToJson(object payload) =>
        JsonDocument.Parse(JsonSerializer.Serialize(payload)).RootElement.Clone();
}
