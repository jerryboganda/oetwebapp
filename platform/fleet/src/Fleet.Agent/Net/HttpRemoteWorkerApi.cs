using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Fleet.Agent;

/// <summary>The job plane as the agent uses it. Faked in loop tests; the HTTP implementation is exercised against a fake handler.</summary>
internal interface IRemoteWorkerApi
{
    int CurrentProtocol { get; }

    Task<ApiResponse<NodeHeartbeatResponse>> NodeHeartbeatAsync(NodeHeartbeatRequest request, CancellationToken ct);

    Task<ApiResponse<ClaimResponse>> ClaimAsync(ClaimRequest request, CancellationToken ct);

    Task<ApiResponse<JobHeartbeatResponse>> JobHeartbeatAsync(string jobId, JobHeartbeatRequest request, CancellationToken ct);

    /// <summary>Complete with a pre-serialised body so every retry is byte-identical (RW-087).</summary>
    Task<ApiResponse<CompleteResponse>> CompleteAsync(string jobId, byte[] body, CancellationToken ct);

    Task<ApiResponse<FailResponse>> FailAsync(string jobId, FailRequest request, TimeSpan? timeout, CancellationToken ct);

    Task<InputResponse> OpenInputAsync(string jobId, string name, long fence, long offset, string? ifRangeEtag, CancellationToken ct);

    Task<ApiResponse<OutputAck>> PutOutputAsync(string jobId, string name, long fence, string sha256Hex, string filePath, CancellationToken ct);
}

/// <summary>
/// HTTPS client for /v1/internal/remote-worker. Sends the node token and the protocol headers, NEVER the client-version
/// headers (RW-014), never follows redirects (H7), and classifies every answer into <see cref="ApiKind"/>.
/// </summary>
internal sealed class HttpRemoteWorkerApi : IRemoteWorkerApi, IDisposable
{
    private static readonly MediaTypeHeaderValue JsonType = new("application/json") { CharSet = "utf-8" };
    private static readonly MediaTypeHeaderValue OctetType = new("application/octet-stream");
    private const int ProblemBodyCap = 64 * 1024;

    private readonly HttpClient _http;
    private readonly string _token;
    private readonly string _agentVersion;
    private readonly ProtocolNegotiator _negotiator;
    private readonly SemaphoreSlim _streams = new(4, 4);

    public HttpRemoteWorkerApi(HttpClient http, string nodeToken, string agentVersion, ProtocolNegotiator negotiator)
    {
        _http = http;
        _token = nodeToken;
        _agentVersion = agentVersion;
        _negotiator = negotiator;
    }

    public int CurrentProtocol => _negotiator.Current;

    /// <summary>The production client: no proxy, no redirects, no cookies, no automatic decompression of octet streams.</summary>
    public static HttpClient CreateHttpClient(Uri baseAddress)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(10),
        };
        return new HttpClient(handler, disposeHandler: true) { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
    }

    // ---- endpoints -----------------------------------------------------------------------------------------

    public Task<ApiResponse<NodeHeartbeatResponse>> NodeHeartbeatAsync(NodeHeartbeatRequest request, CancellationToken ct) =>
        SendJsonAsync<NodeHeartbeatResponse>(HttpMethod.Post, Wire.BasePath + "/workers/heartbeat", ProtocolJson.ToUtf8(request), Wire.HeartbeatTimeout, null, ct);

    public Task<ApiResponse<ClaimResponse>> ClaimAsync(ClaimRequest request, CancellationToken ct) =>
        SendJsonAsync<ClaimResponse>(HttpMethod.Post, Wire.BasePath + "/claim", ProtocolJson.ToUtf8(request), Wire.ClaimTimeout, null, ct);

    public Task<ApiResponse<JobHeartbeatResponse>> JobHeartbeatAsync(string jobId, JobHeartbeatRequest request, CancellationToken ct) =>
        SendJsonAsync<JobHeartbeatResponse>(HttpMethod.Post, JobPath(jobId, "heartbeat"), ProtocolJson.ToUtf8(request), Wire.HeartbeatTimeout, null, ct);

    public Task<ApiResponse<CompleteResponse>> CompleteAsync(string jobId, byte[] body, CancellationToken ct) =>
        SendJsonAsync<CompleteResponse>(HttpMethod.Post, JobPath(jobId, "complete"), body, Wire.CompleteTimeout, null, ct);

    public Task<ApiResponse<FailResponse>> FailAsync(string jobId, FailRequest request, TimeSpan? timeout, CancellationToken ct) =>
        SendJsonAsync<FailResponse>(HttpMethod.Post, JobPath(jobId, "fail"), ProtocolJson.ToUtf8(request), timeout ?? Wire.CompleteTimeout, null, ct);

    public async Task<InputResponse> OpenInputAsync(string jobId, string name, long fence, long offset, string? ifRangeEtag, CancellationToken ct)
    {
        await _streams.WaitAsync(ct).ConfigureAwait(false);
        var released = false;
        try
        {
            for (var pass = 0; ; pass++)
            {
                using var request = BuildRequest(HttpMethod.Get, JobPath(jobId, "inputs/" + Uri.EscapeDataString(name)), _negotiator.Current, fence);
                if (offset > 0)
                {
                    request.Headers.Range = new RangeHeaderValue(offset, null);
                    if (!string.IsNullOrEmpty(ifRangeEtag)) request.Headers.IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue("\"" + ifRangeEtag + "\""));
                }

                HttpResponseMessage response;
                try
                {
                    using var headerBudget = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    headerBudget.CancelAfter(Wire.InputIdleTimeout);
                    response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headerBudget.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    return InputResponse.Failed(ApiResponse.Failure(ApiKind.Timeout));
                }
                catch (OperationCanceledException)
                {
                    return InputResponse.Failed(ApiResponse.Failure(ApiKind.Canceled));
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException)
                {
                    return InputResponse.Failed(ApiResponse.Failure(ApiKind.Network));
                }

                var head = await ClassifyAsync(response, ct).ConfigureAwait(false);
                if (head.Kind == ApiKind.ProtocolUnsupported && pass == 0 && _negotiator.TryDowngrade(head.Supported))
                {
                    response.Dispose();
                    continue;
                }

                if (head.Kind != ApiKind.Ok)
                {
                    response.Dispose();
                    return InputResponse.Failed(head);
                }

                Stream body;
                try
                {
                    body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    response.Dispose();
                    return InputResponse.Failed(ApiResponse.Failure(ct.IsCancellationRequested ? ApiKind.Canceled : ApiKind.Timeout));
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException)
                {
                    response.Dispose();
                    return InputResponse.Failed(ApiResponse.Failure(ApiKind.Network));
                }

                var etag = response.Headers.ETag?.Tag?.Trim('"');
                var sha = FirstHeader(response, Wire.HeaderContentSha);
                var partial = response.StatusCode == HttpStatusCode.PartialContent;
                var rangeStart = response.Content.Headers.ContentRange?.From;
                var owner = new ResponseOwner(response, _streams);
                released = true; // the semaphore is now released by the owner when the caller disposes the response
                return new InputResponse(head, body, response.Content.Headers.ContentLength, sha, etag, partial, rangeStart, owner);
            }
        }
        finally
        {
            if (!released) _streams.Release();
        }
    }

    public async Task<ApiResponse<OutputAck>> PutOutputAsync(string jobId, string name, long fence, string sha256Hex, string filePath, CancellationToken ct)
    {
        await _streams.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            for (var pass = 0; ; pass++)
            {
                await using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan | FileOptions.Asynchronous);
                using var request = BuildRequest(HttpMethod.Put, JobPath(jobId, "outputs/" + Uri.EscapeDataString(name)), _negotiator.Current, fence);
                request.Headers.TryAddWithoutValidation(Wire.HeaderContentSha, sha256Hex);
                var content = new StreamContent(file, 81920);
                content.Headers.ContentLength = file.Length;
                content.Headers.ContentType = OctetType;
                request.Content = content;

                // Idle budget plus one second per MiB, a stand-in for a per-write idle timer.
                var budget = Wire.OutputIdleTimeout + TimeSpan.FromSeconds(file.Length / (1024 * 1024));
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(budget);
                try
                {
                    using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                    var head = await ClassifyAsync(response, cts.Token).ConfigureAwait(false);
                    if (head.Kind == ApiKind.ProtocolUnsupported && pass == 0 && _negotiator.TryDowngrade(head.Supported)) continue;
                    if (head.Kind != ApiKind.Ok) return ApiResponse<OutputAck>.From(head, null);
                    var ack = await ReadJsonAsync<OutputAck>(response, cts.Token).ConfigureAwait(false);
                    return ApiResponse<OutputAck>.From(head, ack);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    return ApiResponse<OutputAck>.Failure(ApiKind.Timeout);
                }
                catch (OperationCanceledException)
                {
                    return ApiResponse<OutputAck>.Failure(ApiKind.Canceled);
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException)
                {
                    return ApiResponse<OutputAck>.Failure(ApiKind.Network);
                }
            }
        }
        finally
        {
            _streams.Release();
        }
    }

    // ---- plumbing ------------------------------------------------------------------------------------------

    private static string JobPath(string jobId, string tail) => Wire.BasePath + "/jobs/" + jobId + "/" + tail;

    private HttpRequestMessage BuildRequest(HttpMethod method, string path, int protocol, long? fence)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _token);
        request.Headers.TryAddWithoutValidation(Wire.HeaderProtocol, protocol.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation(Wire.HeaderAgentVersion, _agentVersion);
        request.Headers.TryAddWithoutValidation(Wire.HeaderCorrelation, Guid.NewGuid().ToString("D"));
        request.Headers.TryAddWithoutValidation("User-Agent", "oet-fleet-agent/" + _agentVersion);
        if (fence is { } value) request.Headers.TryAddWithoutValidation(Wire.HeaderFence, value.ToString(CultureInfo.InvariantCulture));
        return request;
    }

    private async Task<ApiResponse<T>> SendJsonAsync<T>(HttpMethod method, string path, byte[]? body, TimeSpan timeout, long? fence, CancellationToken ct)
        where T : class
    {
        for (var pass = 0; ; pass++)
        {
            using var request = BuildRequest(method, path, _negotiator.Current, fence);
            if (body is not null)
            {
                var content = new ByteArrayContent(body);
                content.Headers.ContentType = JsonType;
                request.Content = content;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            try
            {
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                var head = await ClassifyAsync(response, cts.Token).ConfigureAwait(false);
                if (head.Kind == ApiKind.ProtocolUnsupported && pass == 0 && _negotiator.TryDowngrade(head.Supported)) continue;
                if (head.Kind != ApiKind.Ok) return ApiResponse<T>.From(head, null);
                var value = await ReadJsonAsync<T>(response, cts.Token).ConfigureAwait(false);
                return ApiResponse<T>.From(head, value);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return ApiResponse<T>.Failure(ApiKind.Timeout);
            }
            catch (OperationCanceledException)
            {
                return ApiResponse<T>.Failure(ApiKind.Canceled);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                return ApiResponse<T>.Failure(ApiKind.Network);
            }
            catch (JsonException)
            {
                // A 200 with an unreadable body: treat as a server fault, retryable by the caller's policy.
                return ApiResponse<T>.Failure(ApiKind.ServerError, 200, "unreadable_body");
            }
        }
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken ct) where T : class
    {
        if (response.Content.Headers.ContentLength == 0) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, ProtocolJson.Options, ct).ConfigureAwait(false);
    }

    /// <summary>Turns a response into an <see cref="ApiResponse"/> head (section 2.4 skew rules, Appendix A catalogue).</summary>
    internal static async Task<ApiResponse> ClassifyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var status = (int)response.StatusCode;
        var hasProtocol = response.Headers.Contains(Wire.HeaderProtocol);
        var retryAfter = response.Headers.RetryAfter?.Delta;
        long? desired = long.TryParse(FirstHeader(response, Wire.HeaderDesiredRevision), NumberStyles.None, CultureInfo.InvariantCulture, out var revision) ? revision : null;
        var reason = FirstHeader(response, Wire.HeaderReason);

        ApiResponse Make(ApiKind kind, string? code = null, string? why = null, int[]? supported = null) => new()
        {
            Kind = kind,
            Status = status,
            Code = code,
            Reason = why,
            RetryAfter = retryAfter,
            DesiredRevision = desired,
            Supported = supported,
            NoContentReason = reason,
        };

        // A reverse proxy answering during an API redeploy has no X-Remote-Protocol: 5xx is transient, anything else below 500
        // means the route is not served by an API that speaks this protocol (rollback skew, 2.4 / 5.6).
        if (!hasProtocol)
        {
            if (status == 429) return Make(ApiKind.RateLimited);
            if (status >= 500 && status != 501) return Make(ApiKind.ServerError);
            return Make(ApiKind.RouteAbsent);
        }

        if (status is 200 or 201 or 206) return Make(ApiKind.Ok);
        if (status == 204) return Make(ApiKind.NoContent);

        var problem = await ReadProblemAsync(response, ct).ConfigureAwait(false);
        var code = problem?.Code;
        var why = problem?.Reason;
        if (problem?.RetryAfterSeconds is { } seconds && retryAfter is null) retryAfter = TimeSpan.FromSeconds(seconds);

        return status switch
        {
            400 => Make(ApiKind.BadRequest, code, why),
            401 => Make(ApiKind.Unauthorized, code, why),
            403 => Make(ApiKind.Forbidden, code, why),
            404 => code is null ? Make(ApiKind.RouteAbsent) : Make(ApiKind.NotFound, code, why),
            405 => Make(ApiKind.RouteAbsent),
            409 => Make(ApiKind.Conflict, code, why),
            413 => Make(ApiKind.PayloadTooLarge, code, why),
            416 => Make(ApiKind.RangeNotSatisfiable, code, why),
            422 => Make(ApiKind.Unprocessable, code, why),
            426 => Make(ApiKind.ProtocolUnsupported, code ?? "protocol_unsupported", why, problem?.Supported),
            429 => Make(ApiKind.RateLimited, code, why),
            501 => Make(ApiKind.NotImplemented, code, why),
            >= 500 => Make(ApiKind.ServerError, code, why),
            _ => Make(ApiKind.OtherClientError, code, why),
        };
    }

    private static async Task<ProblemBody?> ReadProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            if (response.Content.Headers.ContentLength is > ProblemBodyCap) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[4096];
            int read;
            while (buffer.Length < ProblemBodyCap && (read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
            {
                buffer.Write(chunk, 0, read);
            }

            return buffer.Length == 0 ? null : JsonSerializer.Deserialize<ProblemBody>(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), ProtocolJson.Options);
        }
        catch (Exception ex) when (ex is JsonException or IOException or HttpRequestException or InvalidOperationException or NotSupportedException or OperationCanceledException)
        {
            return null;
        }
    }

    private static string? FirstHeader(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var values)) return values.FirstOrDefault();
        return response.Content.Headers.TryGetValues(name, out var contentValues) ? contentValues.FirstOrDefault() : null;
    }

    public void Dispose()
    {
        _streams.Dispose();
        _http.Dispose();
    }

    private sealed class ResponseOwner : IDisposable
    {
        private readonly HttpResponseMessage _response;
        private readonly SemaphoreSlim _streams;
        private int _disposed;

        public ResponseOwner(HttpResponseMessage response, SemaphoreSlim streams)
        {
            _response = response;
            _streams = streams;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _response.Dispose();
            _streams.Release();
        }
    }
}
