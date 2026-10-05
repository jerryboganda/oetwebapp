using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Fleet.Agent.Tests;

internal sealed record RecordedRequest(HttpMethod Method, string Path, Dictionary<string, string> Headers, byte[] Body);

internal sealed class ServerJob
{
    public required string Id { get; init; }
    public string Kind { get; init; } = JobKinds.PdfExtract;
    public string EngineVersion { get; init; } = EngineVersions.Pdf;
    public string ParamsJson { get; init; } = "{\"mode\":\"flat\",\"minTextLength\":5,\"provider\":\"auto\",\"includePages\":true}";
    public Dictionary<string, byte[]> Inputs { get; init; } = new();
    public long Fence { get; set; }
    public string? ClaimNonce { get; set; }
    public bool Leased { get; set; }
    public string? CompletedHash { get; set; }
    public string? CompletedBody { get; set; }
    public string? FailedCode { get; set; }
}

/// <summary>
/// A faithful-enough in-memory API for OET-RWP/1: bearer auth, protocol numbers with 426, problem+json errors, claim with
/// idempotent replay and fence increments, job heartbeat, ranged inputs, hashed outputs, complete with replay and fail. It lets
/// the REAL HttpRemoteWorkerApi be exercised through HttpClient without a socket.
/// </summary>
internal sealed class FakeServerHandler : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ServerJob> _jobs = new();
    private readonly Queue<string> _queue = new();
    private readonly Dictionary<string, (string JobId, long Fence)> _claimNonces = new();

    public string ExpectedToken { get; set; } = TestIds.NodeToken();
    public int[] SupportedProtocols { get; set; } = [1];
    public bool RouteAbsent { get; set; }

    /// <summary>Number of complete answers to swallow AFTER the server processed them (a lost success response).</summary>
    public int DropCompleteResponses { get; set; }
    public long DesiredRevision { get; set; } = 1;
    public string NodeStatus { get; set; } = "Active";
    public Func<HttpRequestMessage, HttpResponseMessage?>? Override { get; set; }
    public List<RecordedRequest> Requests { get; } = [];
    public Dictionary<string, Dictionary<string, byte[]>> Outputs { get; } = new();

    public void Enqueue(ServerJob job)
    {
        lock (_gate)
        {
            _jobs[job.Id] = job;
            _queue.Enqueue(job.Id);
        }
    }

    public ServerJob Job(string id)
    {
        lock (_gate) return _jobs[id];
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? Array.Empty<byte>() : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers) headers[header.Key] = string.Join(",", header.Value);
        if (request.Content is not null)
        {
            foreach (var header in request.Content.Headers) headers[header.Key] = string.Join(",", header.Value);
        }

        var path = request.RequestUri!.AbsolutePath;
        lock (_gate) Requests.Add(new RecordedRequest(request.Method, path, headers, body));

        if (Override?.Invoke(request) is { } injected) return injected;
        if (RouteAbsent) return new HttpResponseMessage(HttpStatusCode.NotFound);

        if (request.Headers.Authorization is not { Scheme: "Bearer" } auth || auth.Parameter != ExpectedToken)
        {
            return Problem(HttpStatusCode.Unauthorized, "unauthorized");
        }

        if (!headers.TryGetValue(Wire.HeaderProtocol, out var protocolText) || !int.TryParse(protocolText, out var protocol))
        {
            return Problem(HttpStatusCode.BadRequest, "bad_request");
        }

        if (!SupportedProtocols.Contains(protocol))
        {
            return Problem(HttpStatusCode.UpgradeRequired, "protocol_unsupported", supported: SupportedProtocols);
        }

        lock (_gate) return Route(request, path, body, headers);
    }

    private HttpResponseMessage Route(HttpRequestMessage request, string path, byte[] body, Dictionary<string, string> headers)
    {
        var tail = path.StartsWith(Wire.BasePath + "/", StringComparison.Ordinal) ? path[(Wire.BasePath.Length + 1)..] : "";
        if (tail == "workers/heartbeat") return NodeHeartbeat();
        if (tail == "claim") return Claim(body);

        var parts = tail.Split('/');
        if (parts.Length >= 3 && parts[0] == "jobs")
        {
            if (!_jobs.TryGetValue(parts[1], out var job)) return Problem(HttpStatusCode.NotFound, "job_not_found");
            switch (parts[2])
            {
                case "heartbeat": return JobHeartbeat(job, body);
                case "inputs" when parts.Length == 4: return Input(request, job, parts[3], headers);
                case "outputs" when parts.Length == 4: return Output(job, parts[3], body, headers);
                case "complete": return Complete(job, body);
                case "fail": return Fail(job, body);
            }
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private HttpResponseMessage NodeHeartbeat()
    {
        var desired = Api.Desired(DesiredRevision);
        var payload = new
        {
            serverTime = TimeFormat.Rfc3339(DateTimeOffset.UtcNow),
            node = new { id = TestIds.NodeId, status = NodeStatus },
            features = new { enabled = true },
            desired,
            leaseAudit = Array.Empty<object>(),
            nextHeartbeatSeconds = 15,
        };
        return Json(HttpStatusCode.OK, payload);
    }

    private HttpResponseMessage Claim(byte[] body)
    {
        var request = JsonSerializer.Deserialize<ClaimRequest>(body, ProtocolJson.Options)!;
        if (_claimNonces.TryGetValue(request.ClaimId, out var replay) && _jobs[replay.JobId] is { Leased: true } replayed && replayed.Fence == replay.Fence)
        {
            return ClaimResponseFor(replayed);
        }

        while (_queue.Count > 0)
        {
            var job = _jobs[_queue.Dequeue()];
            if (job.Leased || job.CompletedHash is not null) continue;
            job.Leased = true;
            job.Fence++;
            job.ClaimNonce = request.ClaimId;
            _claimNonces[request.ClaimId] = (job.Id, job.Fence);
            return ClaimResponseFor(job);
        }

        var empty = new HttpResponseMessage(HttpStatusCode.NoContent);
        Stamp(empty);
        empty.Headers.TryAddWithoutValidation(Wire.HeaderReason, "no_work");
        empty.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(10));
        return empty;
    }

    private HttpResponseMessage ClaimResponseFor(ServerJob job)
    {
        var inputs = job.Inputs.Select(i => new { name = i.Key, sizeBytes = i.Value.Length, sha256 = TestIds.Sha(i.Value), contentType = "application/pdf" }).ToArray();
        var payload = new
        {
            job = new
            {
                id = job.Id,
                kind = job.Kind,
                schemaVersion = 1,
                purpose = "apply",
                engineVersion = job.EngineVersion,
                attempt = 1,
                maxAttempts = 3,
                fence = job.Fence,
                leaseExpiresAt = TimeFormat.Rfc3339(DateTimeOffset.UtcNow.AddSeconds(120)),
                leaseRemainingMs = 120_000,
                heartbeatEverySeconds = 20,
                deadlineAt = TimeFormat.Rfc3339(DateTimeOffset.UtcNow.AddSeconds(300)),
                deadlineSeconds = 300,
                inputs,
                @params = JsonDocument.Parse(job.ParamsJson).RootElement,
                limits = new
                {
                    weight = 1, cpuMilli = 1000, memMiB = 2048, tmpMiB = 256, timeoutSeconds = 120,
                    maxInputBytes = 104_857_600, maxResultBytes = 8_388_608, maxOutputBytes = 0, maxOutputs = 0,
                },
            },
            serverTime = TimeFormat.Rfc3339(DateTimeOffset.UtcNow),
            desired = Api.Desired(DesiredRevision),
        };
        return Json(HttpStatusCode.OK, payload);
    }

    private HttpResponseMessage JobHeartbeat(ServerJob job, byte[] body)
    {
        var request = JsonSerializer.Deserialize<JobHeartbeatRequest>(body, ProtocolJson.Options)!;
        if (!job.Leased || request.Fence != job.Fence) return Problem(HttpStatusCode.Conflict, "lease_lost", "superseded");
        return Json(HttpStatusCode.OK, new { leaseExpiresAt = TimeFormat.Rfc3339(DateTimeOffset.UtcNow.AddSeconds(120)), leaseRemainingMs = 120_000, serverTime = TimeFormat.Rfc3339(DateTimeOffset.UtcNow), desiredRevision = DesiredRevision });
    }

    private HttpResponseMessage Input(HttpRequestMessage request, ServerJob job, string name, Dictionary<string, string> headers)
    {
        if (!headers.TryGetValue(Wire.HeaderFence, out var fenceText) || !long.TryParse(fenceText, out var fence) || fence != job.Fence || !job.Leased)
        {
            return Problem(HttpStatusCode.Conflict, "lease_lost", "superseded");
        }

        if (!job.Inputs.TryGetValue(Uri.UnescapeDataString(name), out var bytes)) return Problem(HttpStatusCode.NotFound, "input_not_found");
        var sha = TestIds.Sha(bytes);
        var range = request.Headers.Range?.Ranges.FirstOrDefault();
        var offset = range?.From ?? 0;
        var slice = bytes.AsMemory((int)offset).ToArray();
        var response = new HttpResponseMessage(range is null ? HttpStatusCode.OK : HttpStatusCode.PartialContent) { Content = new ByteArrayContent(slice) };
        Stamp(response);
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        response.Headers.TryAddWithoutValidation(Wire.HeaderContentSha, sha);
        response.Headers.ETag = new EntityTagHeaderValue("\"" + sha + "\"");
        if (range is not null) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, bytes.Length - 1, bytes.Length);
        return response;
    }

    private HttpResponseMessage Output(ServerJob job, string name, byte[] body, Dictionary<string, string> headers)
    {
        if (!job.Leased) return Problem(HttpStatusCode.Conflict, "lease_lost", "superseded");
        var sha = TestIds.Sha(body);
        if (!headers.TryGetValue(Wire.HeaderContentSha, out var declared) || declared != sha) return Problem((HttpStatusCode)422, "output_hash_mismatch");
        if (!Outputs.TryGetValue(job.Id, out var bucket)) Outputs[job.Id] = bucket = new Dictionary<string, byte[]>();
        bucket[name] = body;
        return Json(HttpStatusCode.OK, new { name, sizeBytes = body.Length, sha256 = sha, replaced = false });
    }

    private HttpResponseMessage Complete(ServerJob job, byte[] body)
    {
        var request = JsonSerializer.Deserialize<CompleteRequest>(body, ProtocolJson.Options)!;
        var computed = TestIds.Sha(Encoding.UTF8.GetBytes(request.ResultJson));
        if (computed != request.ResultSha256) return Problem((HttpStatusCode)422, "result_hash_mismatch");
        if (job.CompletedHash is not null && job.CompletedHash == request.ResultSha256 && job.Fence == request.Fence)
        {
            return Json(HttpStatusCode.OK, new { status = "succeeded", outcome = "Applied", replayed = true });
        }

        if (!job.Leased || request.Fence != job.Fence) return Problem(HttpStatusCode.Conflict, "lease_lost", "superseded");
        job.Leased = false;
        job.CompletedHash = request.ResultSha256;
        job.CompletedBody = request.ResultJson;
        if (DropCompleteResponses > 0)
        {
            DropCompleteResponses--;
            return Problem(HttpStatusCode.ServiceUnavailable, "service_unavailable");
        }

        return Json(HttpStatusCode.OK, new { status = "succeeded", outcome = "Applied", replayed = false });
    }

    private HttpResponseMessage Fail(ServerJob job, byte[] body)
    {
        var request = JsonSerializer.Deserialize<FailRequest>(body, ProtocolJson.Options)!;
        if (!job.Leased || request.Fence != job.Fence) return Problem(HttpStatusCode.Conflict, "lease_lost", "superseded");
        job.Leased = false;
        job.FailedCode = request.Code;
        return Json(HttpStatusCode.OK, new { status = request.Retryable ? "queued" : "failed", replayed = false });
    }

    // ---- response helpers ----------------------------------------------------------------------------------

    private static void Stamp(HttpResponseMessage response)
    {
        response.Headers.TryAddWithoutValidation(Wire.HeaderProtocol, "1");
        response.Headers.TryAddWithoutValidation(Wire.HeaderProtocolMin, "1");
        response.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
    }

    private HttpResponseMessage Json(HttpStatusCode status, object payload)
    {
        var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(payload, ProtocolJson.Options)) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        Stamp(response);
        response.Headers.TryAddWithoutValidation(Wire.HeaderDesiredRevision, DesiredRevision.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return response;
    }

    public static HttpResponseMessage Problem(HttpStatusCode status, string code, string? reason = null, int[]? supported = null)
    {
        var payload = new { code, message = "test", retryable = false, reason, supported, correlationId = Guid.NewGuid().ToString("D") };
        var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(payload, ProtocolJson.Options)) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/problem+json");
        Stamp(response);
        return response;
    }
}
