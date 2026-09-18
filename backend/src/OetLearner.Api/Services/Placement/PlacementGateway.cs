using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Services.Placement;

/// <summary>
/// Server-side client for the private GEPA assessment engine (General
/// English Placement Assessment). The engine is NEVER exposed publicly:
/// every candidate/reviewer request flows through the OET API, which
/// authenticates the OET user, enforces the <c>Placement.Enabled</c> flag,
/// and forwards the request with a service token plus the
/// <c>X-GEPA-Candidate-Uid</c> header naming the authenticated learner.
/// The engine independently verifies session ownership against that uid.
/// </summary>
public sealed class PlacementGateway
{
    private readonly HttpClient _http;
    private readonly IRuntimeSettingsProvider _runtimeSettings;
    private readonly ILogger<PlacementGateway> _logger;

    public const string OnBehalfOfHeader = "X-GEPA-Candidate-Uid";

    public PlacementGateway(HttpClient http, IRuntimeSettingsProvider runtimeSettings, ILogger<PlacementGateway> logger)
    {
        _http = http;
        _runtimeSettings = runtimeSettings;
        _logger = logger;
    }

    /// <summary>Build a service-authenticated request. The service token is
    /// injected per call from configuration (rotating it only needs a
    /// restart, not a gateway change). Session-scoped calls carry the
    /// on-behalf-of candidate uid; reviewer/admin calls pass empty.</summary>
    private async Task<HttpRequestMessage> AuthenticatedAsync(HttpMethod method, string path, string candidateUid, HttpContent? content = null)
    {
        var token = _http.DefaultRequestHeaders.Authorization?.Parameter;
        if (string.IsNullOrEmpty(token))
        {
            token = Environment.GetEnvironmentVariable("GEPA_SERVICE_TOKEN");
        }
        if (string.IsNullOrEmpty(token))
        {
            throw ApiException.ServiceUnavailable(
                "placement_engine_not_configured",
                "The placement engine connection is not configured yet — contact support.");
        }
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrEmpty(candidateUid))
        {
            request.Headers.Add(OnBehalfOfHeader, candidateUid);
        }
        return await Task.FromResult(request);
    }

    private ApiException MapUpstreamFailure(string path, HttpResponseMessage response, string body)
    {
        _logger.LogWarning("Placement engine {Status} on {Path}: {Body}", (int)response.StatusCode, path, body);
        return (int)response.StatusCode switch
        {
            404 => ApiException.NotFound("placement_session_not_found", "That placement session does not exist."),
            403 => ApiException.Forbidden("placement_not_your_session", "That placement session belongs to another account."),
            409 => ApiException.Conflict("placement_stale_submission", body.Length > 300 ? "Your submission arrived after the session moved on — refresh and continue." : body),
            400 => ApiException.Validation("placement_engine_rejected", body.Length > 300 ? "The placement engine rejected the request." : body),
            _ => ApiException.ServiceUnavailable("placement_engine_unavailable",
                "The placement engine is not responding — please try again shortly."),
        };
    }

    private async Task<JsonDocument> SendForJsonAsync(HttpRequestMessage request, string path, CancellationToken ct)
    {
        // NB: do NOT touch request.RequestUri for logging — it stays
        // relative until the HttpClient combines it with BaseAddress at
        // send time (PathAndQuery throws for relative URIs).
        try
        {
            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                throw MapUpstreamFailure(path, response, body);
            }
            return JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Placement engine unreachable on {Path}", path);
            throw ApiException.ServiceUnavailable("placement_engine_unavailable",
                "The placement engine is not responding — please try again shortly.");
        }
    }

    // ── Session lifecycle ────────────────────────────────────────────

    /// <summary>Create a session owned by <paramref name="candidateUid"/>
    /// (the OET learner id). Fails 503 when the engine's approved-form
    /// readiness gate refuses new sessions.</summary>
    public Task<JsonDocument> CreateSessionAsync(string candidateUid, object body, CancellationToken ct)
        => SendAsync(HttpMethod.Post, "/api/sessions", candidateUid, JsonContent.Create(body), ct);

    public Task<JsonDocument> GetSessionStateAsync(string candidateUid, string sessionId, CancellationToken ct)
        => SendAsync(HttpMethod.Get, $"/api/sessions/{Uri.EscapeDataString(sessionId)}", candidateUid, null, ct);

    public Task<JsonDocument> StartModuleAsync(string candidateUid, string sessionId, string module, CancellationToken ct)
        => SendAsync(HttpMethod.Post, $"/api/sessions/{Uri.EscapeDataString(sessionId)}/modules/{Uri.EscapeDataString(module)}/start", candidateUid, null, ct);

    public Task<JsonDocument> SubmitResponsesAsync(string candidateUid, string sessionId, object body, CancellationToken ct)
        => SendAsync(HttpMethod.Post, $"/api/sessions/{Uri.EscapeDataString(sessionId)}/responses", candidateUid, JsonContent.Create(body), ct);

    public Task<JsonDocument> GetReceptiveResultAsync(string candidateUid, string sessionId, CancellationToken ct)
        => SendAsync(HttpMethod.Get, $"/api/sessions/{Uri.EscapeDataString(sessionId)}/results/receptive", candidateUid, null, ct);

    public Task<JsonDocument> GetResultsStatusAsync(string candidateUid, string sessionId, CancellationToken ct)
        => SendAsync(HttpMethod.Get, $"/api/sessions/{Uri.EscapeDataString(sessionId)}/results/status", candidateUid, null, ct);

    public Task<JsonDocument> GetFullResultAsync(string candidateUid, string sessionId, CancellationToken ct)
        => SendAsync(HttpMethod.Get, $"/api/sessions/{Uri.EscapeDataString(sessionId)}/results/full", candidateUid, null, ct);

    // ── Speaking ─────────────────────────────────────────────────────

    public Task<JsonDocument> GetSpeakingTasksAsync(string candidateUid, string sessionId, CancellationToken ct)
        => SendAsync(HttpMethod.Get, $"/api/sessions/{Uri.EscapeDataString(sessionId)}/speaking/start", candidateUid, null, ct);

    public Task<JsonDocument> SubmitSpeakingAsync(string candidateUid, string sessionId, string taskId, string storagePath, CancellationToken ct)
        => SendAsync(
            HttpMethod.Post,
            $"/api/sessions/{Uri.EscapeDataString(sessionId)}/speaking/{Uri.EscapeDataString(taskId)}/submit",
            candidateUid,
            JsonContent.Create(new { storage_path = storagePath }),
            ct);

    /// <summary>Upload a recording to the engine. Returns the engine's JSON
    /// (storage_path + server-measured metrics).</summary>
    public Task<JsonDocument> UploadRecordingAsync(string candidateUid, Stream fileStream, string fileName, string contentType, CancellationToken ct)
    {
        var multipart = new MultipartFormDataContent();
        var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        multipart.Add(streamContent, "file", fileName);
        return SendAsync(HttpMethod.Post, "/api/media/upload", candidateUid, multipart, ct);
    }

    // ── Writing ──────────────────────────────────────────────────────

    public Task<JsonDocument> GetWritingTasksAsync(string candidateUid, string sessionId, CancellationToken ct)
        => SendAsync(HttpMethod.Get, $"/api/sessions/{Uri.EscapeDataString(sessionId)}/writing/start", candidateUid, null, ct);

    public Task<JsonDocument> SaveWritingDraftAsync(string candidateUid, string sessionId, string taskId, string text, CancellationToken ct)
        => SendAsync(
            HttpMethod.Put,
            $"/api/sessions/{Uri.EscapeDataString(sessionId)}/writing/{Uri.EscapeDataString(taskId)}/draft",
            candidateUid,
            JsonContent.Create(new { text }),
            ct);

    public Task<JsonDocument> SubmitWritingAsync(string candidateUid, string sessionId, string taskId, string text, CancellationToken ct)
        => SendAsync(
            HttpMethod.Post,
            $"/api/sessions/{Uri.EscapeDataString(sessionId)}/writing/{Uri.EscapeDataString(taskId)}/submit",
            candidateUid,
            JsonContent.Create(new { text }),
            ct);

    // ── Binary media (listening stimulus audio) ──────────────────────

    /// <summary>Stream a stimulus audio file through this API with learner
    /// authorization; the engine checks the candidate uid itself.</summary>
    public async Task<(string ContentType, byte[] Bytes)> GetAudioAsync(string candidateUid, string fileName, CancellationToken ct)
    {
        using var response = await _http.SendAsync(
            await AuthenticatedAsync(HttpMethod.Get, $"/api/media/audio/{Uri.EscapeDataString(fileName)}", candidateUid), ct);
        if (!response.IsSuccessStatusCode)
        {
            throw ApiException.NotFound("placement_audio_unavailable", "That audio is not available.");
        }
        return (response.Content.Headers.ContentType?.MediaType ?? "audio/mpeg",
            await response.Content.ReadAsByteArrayAsync(ct));
    }

    // ── Reviewer surface (OET admin/expert proxy) ────────────────────

    public Task<JsonDocument> GetReviewQueueAsync(CancellationToken ct)
        => SendAsync(HttpMethod.Get, "/api/review/queue", string.Empty, null, ct);

    public Task<JsonDocument> GetReviewSessionAsync(string sessionId, CancellationToken ct)
        => SendAsync(HttpMethod.Get, $"/api/review/sessions/{Uri.EscapeDataString(sessionId)}", string.Empty, null, ct);

    public async Task<(string ContentType, byte[] Bytes)> GetReviewAudioAsync(string sessionId, string taskId, CancellationToken ct)
    {
        using var response = await _http.SendAsync(
            await AuthenticatedAsync(HttpMethod.Get, $"/api/review/sessions/{Uri.EscapeDataString(sessionId)}/audio/{Uri.EscapeDataString(taskId)}", string.Empty), ct);
        if (!response.IsSuccessStatusCode)
        {
            throw ApiException.NotFound("placement_audio_unavailable", "That recording is not available.");
        }
        return (response.Content.Headers.ContentType?.MediaType ?? "audio/webm",
            await response.Content.ReadAsByteArrayAsync(ct));
    }

    public Task<JsonDocument> RescoreAsync(string sessionId, object body, CancellationToken ct)
        => SendAsync(HttpMethod.Post, $"/api/review/sessions/{Uri.EscapeDataString(sessionId)}/rescore", string.Empty, JsonContent.Create(body), ct);

    public Task<JsonDocument> HumanScoreAsync(string sessionId, object body, CancellationToken ct)
        => SendAsync(HttpMethod.Post, $"/api/review/sessions/{Uri.EscapeDataString(sessionId)}/human-score", string.Empty, JsonContent.Create(body), ct);

    // ── Ops ──────────────────────────────────────────────────────────

    public async Task<JsonDocument> GetReadyZAsync(CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync("/readyz", ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            return JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return JsonDocument.Parse("{}");
        }
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, string candidateUid, HttpContent? content, CancellationToken ct)
    {
        using var request = await AuthenticatedAsync(method, path, candidateUid, content);
        return await SendForJsonAsync(request, path, ct);
    }
}
