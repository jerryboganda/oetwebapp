using System.Net;
using System.Net.Http.Headers;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services.OwnerAgent;

/// <summary>Raw sidecar answer: status code + a JSON body that has been parsed once for validity.</summary>
public sealed record OwnerAgentSidecarResponse(int StatusCode, string Json)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;

    public JsonElement? TryParse()
    {
        try
        {
            using var document = JsonDocument.Parse(Json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>The sidecar could not be reached, rejected our credentials, or answered garbage.</summary>
public sealed class OwnerAgentSidecarException(string code, string message, int? upstreamStatus = null, Exception? inner = null)
    : Exception(message, inner)
{
    public const string NotConfigured = "owner_agent_not_configured";
    public const string Unavailable = "owner_agent_sidecar_unavailable";
    public const string Timeout = "owner_agent_sidecar_timeout";
    public const string Rejected = "owner_agent_sidecar_rejected";
    public const string BadResponse = "owner_agent_sidecar_bad_response";
    public const string ResponseTooLarge = "owner_agent_sidecar_response_too_large";

    public string Code { get; } = code;
    public int? UpstreamStatus { get; } = upstreamStatus;
}

/// <summary>
/// Typed HttpClient for the internal sidecar control server (CONTRACT.md §3).
///
/// <list type="bullet">
/// <item><b>Fresh requests only.</b> Every call builds a new <see cref="HttpRequestMessage"/>
/// carrying exactly <c>X-Oet-Internal-Token</c>, <c>X-Oet-Owner-Account</c>, an optional
/// sanitized <c>X-Request-Id</c>, <c>Accept</c> and (for bodies) <c>Content-Type</c>. Browser
/// headers (cookies, Authorization, unlock/step-up tickets, CSRF, forwarding headers) are never
/// copied — the client has no access to the inbound request at all.</item>
/// <item>Paths are built only from constants plus values validated by <see cref="OwnerAgentIds"/>.</item>
/// <item>Request bodies are re-serialized from typed/anonymous objects (whitelisted fields).</item>
/// <item>SSE: <see cref="HttpCompletionOption.ResponseHeadersRead"/>, infinite client timeout,
/// cancellation-driven lifetime, parsed with <see cref="SseParser"/>.</item>
/// </list>
/// The internal token is never logged and never placed in an exception message.
/// </summary>
public sealed partial class OwnerAgentClient(
    HttpClient httpClient,
    IOptions<OwnerAgentOptions> options,
    ILogger<OwnerAgentClient> logger)
{
    public const string InternalTokenHeader = "X-Oet-Internal-Token";
    public const string OwnerAccountHeader = "X-Oet-Owner-Account";
    public const string RequestIdHeader = "X-Request-Id";

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan AdminTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Largest JSON body accepted from the sidecar (diff patches are capped at 2 MB there).</summary>
    public const int MaxResponseBytes = 8 * 1024 * 1024;

    /// <summary>Largest single SSE event forwarded to the hub.</summary>
    public const int MaxEventBytes = 4 * 1024 * 1024;

    private static readonly JsonSerializerOptions BodyJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [GeneratedRegex("^[A-Za-z0-9_.:-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex RequestIdPattern();

    public async Task<OwnerAgentSidecarResponse> SendAsync(
        HttpMethod method,
        string relativePath,
        object? body,
        string ownerAccountId,
        string? requestId,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        using var request = BuildRequest(method, relativePath, ownerAccountId, requestId, "application/json");
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, BodyJson), Encoding.UTF8, "application/json");
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout ?? DefaultTimeout);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OwnerAgentSidecarException(OwnerAgentSidecarException.Timeout, "The agent console did not respond in time.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Owner agent sidecar unreachable for {Method} {Path}: {Error}", method, relativePath, ex.HttpRequestError);
            throw new OwnerAgentSidecarException(OwnerAgentSidecarException.Unavailable, "The agent console is not reachable.", inner: ex);
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                // Our control token / owner header was refused: a deployment problem, never
                // something the browser should interpret as its own 401/403.
                logger.LogError("Owner agent sidecar rejected the API's credentials ({Status}) for {Method} {Path}.", status, method, relativePath);
                throw new OwnerAgentSidecarException(OwnerAgentSidecarException.Rejected, "The agent console rejected the API's credentials.", status);
            }

            string json;
            try
            {
                json = await ReadBoundedAsync(response.Content, MaxResponseBytes, timeoutSource.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new OwnerAgentSidecarException(OwnerAgentSidecarException.Timeout, "The agent console did not respond in time.", status);
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                json = "null";
            }

            try
            {
                using var _ = JsonDocument.Parse(json);
            }
            catch (JsonException)
            {
                throw new OwnerAgentSidecarException(OwnerAgentSidecarException.BadResponse, "The agent console returned an unreadable response.", status);
            }

            return new OwnerAgentSidecarResponse(status, json);
        }
    }

    /// <summary>
    /// Relays <c>GET /v1/sessions/{id}/events?after={seq}</c>. Each yielded element is the
    /// parsed <c>AgentEvent</c> JSON (heartbeats included). Ends when the sidecar closes the
    /// stream or <paramref name="cancellationToken"/> fires.
    /// </summary>
    public async IAsyncEnumerable<JsonElement> StreamEventsAsync(
        string sessionId,
        long afterSeq,
        string ownerAccountId,
        string? requestId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var path = OwnerAgentSidecarRoutes.SessionEvents(OwnerAgentIds.RequireUlid(sessionId, "sessionId"), Math.Max(0, afterSeq));
        using var request = BuildRequest(HttpMethod.Get, path, ownerAccountId, requestId, "text/event-stream");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Owner agent sidecar stream unreachable: {Error}", ex.HttpRequestError);
            throw new OwnerAgentSidecarException(OwnerAgentSidecarException.Unavailable, "The agent console is not reachable.", inner: ex);
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                logger.LogError("Owner agent sidecar rejected the API's credentials ({Status}) for the event stream.", status);
                throw new OwnerAgentSidecarException(OwnerAgentSidecarException.Rejected, "The agent console rejected the API's credentials.", status);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new OwnerAgentSidecarException("owner_agent_session_not_found", "The agent session was not found.", status);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new OwnerAgentSidecarException(OwnerAgentSidecarException.BadResponse, "The agent console refused the event stream.", status);
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (!string.Equals(mediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
            {
                throw new OwnerAgentSidecarException(OwnerAgentSidecarException.BadResponse, "The agent console did not return an event stream.", status);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var parser = SseParser.Create(stream);
            await foreach (var item in parser.EnumerateAsync(cancellationToken))
            {
                if (string.IsNullOrWhiteSpace(item.Data))
                {
                    continue;
                }

                if (item.Data.Length > MaxEventBytes)
                {
                    logger.LogWarning("Owner agent event of {Length} chars dropped (limit {Limit}).", item.Data.Length, MaxEventBytes);
                    continue;
                }

                JsonElement element;
                try
                {
                    using var document = JsonDocument.Parse(item.Data);
                    element = document.RootElement.Clone();
                }
                catch (JsonException)
                {
                    logger.LogWarning("Owner agent event with unparseable data dropped (event type {EventType}).", item.EventType);
                    continue;
                }

                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                yield return element;
            }
        }
    }

    private HttpRequestMessage BuildRequest(
        HttpMethod method,
        string relativePath,
        string ownerAccountId,
        string? requestId,
        string accept)
    {
        var settings = options.Value;
        if (!settings.IsSidecarConfigured)
        {
            throw new OwnerAgentSidecarException(OwnerAgentSidecarException.NotConfigured, "The agent console is not configured on this server.");
        }

        if (string.IsNullOrWhiteSpace(ownerAccountId) || !settings.IsOwnerAccount(ownerAccountId))
        {
            // Defence in depth: the endpoint policy already enforced this.
            throw new OwnerAgentSidecarException(OwnerAgentSidecarException.NotConfigured, "The caller is not an allow-listed owner.");
        }

        if (relativePath.StartsWith('/') || relativePath.Contains("..", StringComparison.Ordinal) || relativePath.Contains("://", StringComparison.Ordinal))
        {
            throw new ArgumentException("Sidecar paths must be relative route constants.", nameof(relativePath));
        }

        var baseUri = httpClient.BaseAddress;
        if (baseUri is null)
        {
            if (!settings.TryGetBaseUri(out var configured))
            {
                throw new OwnerAgentSidecarException(OwnerAgentSidecarException.NotConfigured, "The agent console is not configured on this server.");
            }

            baseUri = configured;
        }
        else if (!baseUri.AbsoluteUri.EndsWith('/'))
        {
            baseUri = new Uri(baseUri.AbsoluteUri + "/", UriKind.Absolute);
        }

        var request = new HttpRequestMessage(method, new Uri(baseUri, relativePath))
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        request.Headers.TryAddWithoutValidation(InternalTokenHeader, settings.InternalToken);
        request.Headers.TryAddWithoutValidation(OwnerAccountHeader, ownerAccountId);
        if (!string.IsNullOrWhiteSpace(requestId) && RequestIdPattern().IsMatch(requestId))
        {
            request.Headers.TryAddWithoutValidation(RequestIdHeader, requestId);
        }

        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        return request;
    }

    private static async Task<string> ReadBoundedAsync(HttpContent content, int maxBytes, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is { } declared && declared > maxBytes)
        {
            throw new OwnerAgentSidecarException(OwnerAgentSidecarException.ResponseTooLarge, "The agent console response was too large.");
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw new OwnerAgentSidecarException(OwnerAgentSidecarException.ResponseTooLarge, "The agent console response was too large.");
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}

/// <summary>
/// Innermost handler in front of the sidecar socket: drops every request header that
/// <see cref="OwnerAgentClient"/> did not set itself. <see cref="OwnerAgentClient"/> already
/// builds fresh requests, but app-wide <c>IHttpClientFactory</c> filters (e.g. Sentry's
/// outgoing-request handler, which continues the browser's <c>sentry-trace</c>/<c>baggage</c>)
/// run after it; this keeps the sidecar request to the documented header set no matter
/// which delegating handlers are registered around the typed client.
/// </summary>
public sealed class OwnerAgentOutboundHeaderFilter : DelegatingHandler
{
    private static readonly HashSet<string> AllowedRequestHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        OwnerAgentClient.InternalTokenHeader,
        OwnerAgentClient.OwnerAccountHeader,
        OwnerAgentClient.RequestIdHeader,
        "Accept",
        "Host",
    };

    private static readonly HashSet<string> AllowedContentHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content-Type",
        "Content-Length",
    };

    public OwnerAgentOutboundHeaderFilter()
    {
    }

    public OwnerAgentOutboundHeaderFilter(HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Strip(request);
        return base.SendAsync(request, cancellationToken);
    }

    /// <summary>Removes every header outside the sidecar allow-list (exposed for unit tests).</summary>
    public static void Strip(HttpRequestMessage request)
    {
        foreach (var name in request.Headers.Select(header => header.Key).ToArray())
        {
            if (!AllowedRequestHeaders.Contains(name))
            {
                request.Headers.Remove(name);
            }
        }

        if (request.Content is { } content)
        {
            foreach (var name in content.Headers.Select(header => header.Key).ToArray())
            {
                if (!AllowedContentHeaders.Contains(name))
                {
                    content.Headers.Remove(name);
                }
            }
        }
    }
}

/// <summary>Sidecar route builders (CONTRACT.md §3). Callers pass only validated ids.</summary>
public static class OwnerAgentSidecarRoutes
{
    public const string Status = "v1/status";
    public const string Lease = "v1/lease";
    public const string GithubTokens = "v1/github-tokens";
    public const string Sessions = "v1/sessions";
    public const string StopAll = "v1/admin/stop-all";
    public const string Drain = "v1/admin/drain";
    public const string ApplyUpdate = "v1/admin/apply-update";

    public static string SessionsList(bool includeArchived) => $"v1/sessions?includeArchived={(includeArchived ? "true" : "false")}";
    public static string AuthConnect(string engine) => $"v1/auth/{Escape(engine)}/connect";
    public static string AuthFlow(string engine, string flowId) => $"v1/auth/{Escape(engine)}/flows/{Escape(flowId)}";
    public static string AuthCode(string engine) => $"v1/auth/{Escape(engine)}/code";
    public static string AuthCancel(string engine) => $"v1/auth/{Escape(engine)}/cancel";
    public static string AuthLogout(string engine) => $"v1/auth/{Escape(engine)}/logout";
    public static string Session(string sessionId) => $"v1/sessions/{Escape(sessionId)}";
    public static string SessionMessages(string sessionId) => $"{Session(sessionId)}/messages";
    public static string SessionInterrupt(string sessionId) => $"{Session(sessionId)}/interrupt";
    public static string SessionHandoff(string sessionId) => $"{Session(sessionId)}/handoff";
    public static string SessionApproval(string sessionId, string approvalId) => $"{Session(sessionId)}/approvals/{Escape(approvalId)}";
    public static string SessionDiff(string sessionId) => $"{Session(sessionId)}/diff";
    public static string SessionShip(string sessionId) => $"{Session(sessionId)}/ship";
    public static string SessionEvents(string sessionId, long afterSeq) => $"{Session(sessionId)}/events?after={afterSeq.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    public const int SessionListMaxQueryChars = 100;
    public const int SessionListMaxLimit = 200;
    public static readonly IReadOnlySet<string> SessionStatuses = new HashSet<string>(StringComparer.Ordinal)
    {
        "idle", "running", "awaiting_approval", "interrupted", "error", "archived",
    };

    /// <summary>
    /// <c>GET v1/sessions</c> with the caller's list filters (CONTRACT.md §3 v1.2). Only the
    /// whitelisted keys <c>q</c>, <c>engine</c>, <c>status</c>, <c>includeArchived</c>, <c>before</c>
    /// and <c>limit</c> are forwarded — each validated, normalized and URL-encoded; any other key is
    /// dropped. A bad value is a 400 <c>invalid_{key}</c>. No filters ⇒ plain <c>v1/sessions</c>.
    /// </summary>
    public static string SessionsList(IQueryCollection query)
    {
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        var forwarded = new List<string>();

        string? ReadOne(string key)
        {
            if (!query.TryGetValue(key, out var values) || values.Count == 0)
            {
                return null;
            }
            if (values.Count > 1)
            {
                throw ApiException.Validation($"invalid_{key}", $"'{key}' may be given only once.");
            }
            var value = values[0]?.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }

        void Add(string key, string value) => forwarded.Add($"{key}={Escape(value)}");

        if (ReadOne("q") is { } q)
        {
            if (q.Length > SessionListMaxQueryChars || q.Any(char.IsControl))
            {
                throw ApiException.Validation("invalid_q", $"'q' must be at most {SessionListMaxQueryChars} printable characters.");
            }
            Add("q", q);
        }
        if (ReadOne("engine") is { } engine)
        {
            Add("engine", OwnerAgentIds.RequireEngine(engine));
        }
        if (ReadOne("status") is { } status)
        {
            if (!SessionStatuses.Contains(status))
            {
                throw ApiException.Validation("invalid_status", $"'status' must be one of: {string.Join(", ", SessionStatuses)}.");
            }
            Add("status", status);
        }
        if (ReadOne("includeArchived") is { } includeArchived)
        {
            if (!bool.TryParse(includeArchived, out var include))
            {
                throw ApiException.Validation("invalid_includeArchived", "'includeArchived' must be true or false.");
            }
            Add("includeArchived", include ? "true" : "false");
        }
        if (ReadOne("before") is { } before)
        {
            if (before.Length > 40 || !DateTimeOffset.TryParse(
                    before,
                    invariant,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out var cursor))
            {
                throw ApiException.Validation("invalid_before", "'before' must be an ISO-8601 timestamp.");
            }
            // The sidecar stores updatedAt as JS toISOString() (UTC, millisecond precision).
            Add("before", cursor.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", invariant));
        }
        if (ReadOne("limit") is { } limitText)
        {
            if (!int.TryParse(limitText, System.Globalization.NumberStyles.None, invariant, out var limit)
                || limit < 1 || limit > SessionListMaxLimit)
            {
                throw ApiException.Validation("invalid_limit", $"'limit' must be an integer from 1 to {SessionListMaxLimit}.");
            }
            Add("limit", limit.ToString(invariant));
        }

        return forwarded.Count == 0 ? Sessions : $"{Sessions}?{string.Join('&', forwarded)}";
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);
}
