using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fleet.Core.Policy;
using Fleet.Manager.Configuration;
using Fleet.Manager.Infrastructure;
using Microsoft.Extensions.Options;

namespace Fleet.Manager.Api;

/// <summary>
/// HTTPS client for the OET API service plane (<c>/v1/internal/fleet</c>, OET-RWP/1 section 7.1).
/// Presents the fleet-service credential as a bearer token, sends <c>X-Remote-Protocol</c> and a
/// fresh <c>X-Correlation-Id</c>, never follows redirects (a redirect could carry the credential to
/// another host), and never logs a request or response body. Reads (GET) are retried with backoff;
/// mutations are not, because a repeated rotate or register must be a deliberate decision.
/// </summary>
public sealed class HttpFleetApi : IFleetApi
{
    private const string Root = "/v1/internal/fleet";
    private const int MaxReadAttempts = 3;
    private static readonly Regex NodeIdPattern = new(@"\Arw_[0-9a-z]{26}\z", RegexOptions.CultureInvariant);

    private readonly HttpClient _http;
    private readonly IApiCredentialProvider _credential;
    private readonly IOptions<FleetOptions> _options;
    private readonly IDelay _delay;
    private readonly ILogger<HttpFleetApi> _logger;

    public HttpFleetApi(
        HttpClient http,
        IApiCredentialProvider credential,
        IOptions<FleetOptions> options,
        IDelay delay,
        ILogger<HttpFleetApi> logger)
    {
        _http = http;
        _credential = credential;
        _options = options;
        _delay = delay;
        _logger = logger;
    }

    public async Task<RegisterNodeResult> RegisterNodeAsync(RegisterNodeRequest request, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, Root + "/nodes", request, retryReads: false, cancellationToken);
        var body = await ReadAsync<RegisterEnvelope>(response, cancellationToken);
        return new RegisterNodeResult(body.Node, body.Token, response.StatusCode == HttpStatusCode.Created);
    }

    public async Task<IReadOnlyList<ApiNode>> ListNodesAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, Root + "/nodes?limit=200", null, retryReads: true, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseNodeList(text);
    }

    /// <summary>The list route may answer a bare array or an object with <c>items</c>/<c>nodes</c>; both are accepted.</summary>
    internal static IReadOnlyList<ApiNode> ParseNodeList(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        JsonElement array = default;
        if (root.ValueKind == JsonValueKind.Array)
        {
            array = root;
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                array = items;
            }
            else if (root.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array)
            {
                array = nodes;
            }
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ApiNode>();
        }

        var result = new List<ApiNode>();
        foreach (var element in array.EnumerateArray())
        {
            var node = element.Deserialize<ApiNode>(FleetJson.Options);
            if (node is not null)
            {
                result.Add(node);
            }
        }

        return result;
    }

    public async Task<ApiNode?> GetNodeAsync(string nodeId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Get, NodePath(nodeId), null, retryReads: true, cancellationToken);
            return await ReadAsync<ApiNode>(response, cancellationToken);
        }
        catch (FleetApiException ex) when (ex.Status == HttpStatusCode.NotFound && ex.Code == "node_not_found")
        {
            return null;
        }
    }

    public async Task<int> PutPolicyAsync(string nodeId, NodePolicy policy, int expectedRevision, CancellationToken cancellationToken)
    {
        var body = new PutPolicyBody(
            expectedRevision,
            policy.AllowedKinds,
            policy.MaxConcurrency,
            policy.PerKind,
            policy.Budgets,
            policy.Pressure,
            policy.PollSeconds,
            policy.AgentImage);
        using var response = await SendAsync(HttpMethod.Put, NodePath(nodeId) + "/policy", body, retryReads: false, cancellationToken);
        var result = await ReadAsync<RevisionEnvelope>(response, cancellationToken);
        return result.Revision;
    }

    public Task EnableAsync(string nodeId, CancellationToken cancellationToken) => PostActionAsync(nodeId, "enable", cancellationToken);

    public Task DrainAsync(string nodeId, CancellationToken cancellationToken) => PostActionAsync(nodeId, "drain", cancellationToken);

    public Task DisableAsync(string nodeId, CancellationToken cancellationToken) => PostActionAsync(nodeId, "disable", cancellationToken);

    public Task QuarantineAsync(string nodeId, CancellationToken cancellationToken) => PostActionAsync(nodeId, "quarantine", cancellationToken);

    public Task RevokeAsync(string nodeId, CancellationToken cancellationToken) => PostActionAsync(nodeId, "revoke", cancellationToken);

    public async Task<ApiToken> RotateTokenAsync(string nodeId, int graceSeconds, int ttlDays, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            NodePath(nodeId) + "/tokens/rotate",
            new RotateBody(graceSeconds, ttlDays),
            retryReads: false,
            cancellationToken);
        var body = await ReadAsync<TokenEnvelope>(response, cancellationToken);
        return body.Token ?? throw new FleetApiException(response.StatusCode, "invalid_response", null, false, "The API did not return a token.");
    }

    public async Task<string> EnqueueCanaryAsync(string nodeId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, NodePath(nodeId) + "/canary", new { }, retryReads: false, cancellationToken);
        var body = await ReadAsync<JobEnvelope>(response, cancellationToken);
        return body.JobId ?? throw new FleetApiException(response.StatusCode, "invalid_response", null, false, "The API did not return a job id.");
    }

    public async Task<ApiStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, Root + "/status", null, retryReads: true, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseStatus(text);
    }

    public async Task<JsonElement> GetStatsAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, Root + "/stats", null, retryReads: true, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    public async Task<JsonElement> GetJobsAsync(string? state, string? kind, string? nodeId, int limit, CancellationToken cancellationToken)
    {
        var query = new List<string> { "limit=" + Math.Clamp(limit, 1, 200).ToString(System.Globalization.CultureInfo.InvariantCulture) };
        if (!string.IsNullOrWhiteSpace(state))
        {
            query.Add("state=" + Uri.EscapeDataString(state));
        }

        if (!string.IsNullOrWhiteSpace(kind))
        {
            query.Add("kind=" + Uri.EscapeDataString(kind));
        }

        if (!string.IsNullOrWhiteSpace(nodeId))
        {
            query.Add("nodeId=" + Uri.EscapeDataString(nodeId));
        }

        using var response = await SendAsync(HttpMethod.Get, Root + "/jobs?" + string.Join("&", query), null, retryReads: true, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    public async Task<JsonElement?> GetJobAsync(string jobId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Get, JobPath(jobId), null, retryReads: true, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (FleetApiException ex) when (ex.Status == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task PostJobActionAsync(string action, string jobId, CancellationToken cancellationToken)
    {
        if (action is not ("requeue" or "force-local" or "cancel"))
        {
            throw new ArgumentException("Not a known job action.", nameof(action));
        }

        using var response = await SendAsync(HttpMethod.Post, JobPath(jobId) + "/" + action, new { }, retryReads: false, cancellationToken);
    }

    /// <summary>Job ids are opaque tokens the API issued; the path guard only has to stop URL injection.</summary>
    private static string JobPath(string jobId)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(jobId, @"\A[A-Za-z0-9_-]{1,80}\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
        {
            throw new ArgumentException("Not a valid job id.", nameof(jobId));
        }

        return Root + "/jobs/" + jobId;
    }

    /// <summary>Tolerant parse: the registry lives under <c>kinds</c> (strings or objects with a <c>kind</c> member).</summary>
    internal static ApiStatus ParseStatus(string json)
    {
        var kinds = new List<string>();
        int? current = null;
        int? minimum = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("kinds", out var kindArray) && kindArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in kindArray.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String && item.GetString() is { } name)
                        {
                            kinds.Add(name);
                        }
                        else if (item.ValueKind == JsonValueKind.Object
                                 && item.TryGetProperty("kind", out var kindName)
                                 && kindName.ValueKind == JsonValueKind.String)
                        {
                            kinds.Add(kindName.GetString()!);
                        }
                    }
                }

                if (root.TryGetProperty("protocol", out var protocol) && protocol.ValueKind == JsonValueKind.Object)
                {
                    current = ReadInt(protocol, "current");
                    minimum = ReadInt(protocol, "minimum") ?? ReadInt(protocol, "min");
                }
                else
                {
                    current = ReadInt(root, "protocol") ?? ReadInt(root, "currentProtocol");
                    minimum = ReadInt(root, "protocolMin") ?? ReadInt(root, "minProtocol");
                }
            }
        }
        catch (JsonException)
        {
            // A body that is not JSON carries no registry; callers treat an empty list as "unknown".
        }

        return new ApiStatus(kinds, current, minimum);
    }

    private static int? ReadInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    private async Task PostActionAsync(string nodeId, string action, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, NodePath(nodeId) + "/" + action, new { }, retryReads: false, cancellationToken);
    }

    private static string NodePath(string nodeId)
    {
        if (!NodeIdPattern.IsMatch(nodeId))
        {
            throw new ArgumentException("Not a valid node id.", nameof(nodeId));
        }

        return Root + "/nodes/" + nodeId;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        bool retryReads,
        CancellationToken cancellationToken)
    {
        var bearer = _credential.GetBearer();
        if (string.IsNullOrEmpty(bearer))
        {
            throw new FleetApiException(HttpStatusCode.Unauthorized, FleetApiException.CredentialMissing, null, false, "The fleet-service credential is not provisioned.");
        }

        var attempts = retryReads && method == HttpMethod.Get ? MaxReadAttempts : 1;
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Add("X-Remote-Protocol", _options.Value.Api.ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            request.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString("D"));
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, body.GetType(), options: FleetJson.Options);
            }

            HttpResponseMessage? response = null;
            try
            {
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                if (attempt >= attempts)
                {
                    throw new FleetApiException(0, FleetApiException.Unreachable, null, true, "The OET API could not be reached.");
                }
            }

            if (response is not null)
            {
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("OET API {Method} {Path} answered {Status}.", method.Method, path, (int)response.StatusCode);
                    return response;
                }

                var error = await ToExceptionAsync(response, cancellationToken);
                response.Dispose();
                if (attempt >= attempts || !error.Retryable)
                {
                    throw error;
                }
            }

            await _delay.DelayAsync(TimeSpan.FromSeconds(1 << (attempt - 1)), cancellationToken);
        }
    }

    private static async Task<FleetApiException> ToExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string? code = null;
        string? reason = null;
        string? message = null;
        var retryable = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout or HttpStatusCode.InternalServerError;
        try
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (text.Length > 0 && text.Length <= 65_536)
            {
                using var document = JsonDocument.Parse(text);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                    reason = root.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
                    message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
                    if (root.TryGetProperty("retryable", out var flag) && flag.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        retryable = flag.GetBoolean();
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Not a problem+json body (for example a proxy error page): fall back to the status code.
        }

        var numeric = (int)response.StatusCode;
        return new FleetApiException(
            response.StatusCode,
            code ?? "http_" + numeric.ToString(System.Globalization.CultureInfo.InvariantCulture),
            reason,
            retryable,
            Fleet.Core.Audit.LogScrubber.Scrub(message ?? ("The API answered " + numeric + "."), 200));
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(FleetJson.Options, cancellationToken);
        return value ?? throw new FleetApiException(response.StatusCode, "invalid_response", null, false, "The API returned an empty body.");
    }

    private sealed record RegisterEnvelope(ApiNodeRef Node, ApiToken? Token);

    private sealed record RevisionEnvelope(int Revision);

    private sealed record TokenEnvelope(ApiToken? Token);

    private sealed record JobEnvelope(string? JobId);

    private sealed record RotateBody(int GraceSeconds, int TtlDays);

    private sealed record PutPolicyBody(
        int ExpectedRevision,
        IReadOnlyList<string> AllowedKinds,
        int MaxConcurrency,
        IReadOnlyDictionary<string, int> PerKind,
        Budgets Budgets,
        PressureSettings Pressure,
        PollSettings PollSeconds,
        AgentImagePolicy AgentImage);
}
