using System.Net;
using System.Text;
using System.Text.Json;

namespace Fleet.Agent.Tests;

/// <summary>Drives the REAL HttpRemoteWorkerApi against an in-memory OET-RWP/1 server (conformance ids RW-0xx).</summary>
public sealed class ProtocolConformanceTests
{
    private static (HttpRemoteWorkerApi Api, FakeServerHandler Server, ProtocolNegotiator Negotiator) Create(IReadOnlyList<int>? supported = null)
    {
        var server = new FakeServerHandler();
        var http = new HttpClient(server) { BaseAddress = new Uri("https://api.example.test"), Timeout = Timeout.InfiniteTimeSpan };
        var negotiator = new ProtocolNegotiator(supported);
        return (new HttpRemoteWorkerApi(http, TestIds.NodeToken(), "1.0.0", negotiator), server, negotiator);
    }

    private static NodeHeartbeatRequest Heartbeat() => new()
    {
        InstanceId = Guid.NewGuid().ToString("D"),
        State = "ready",
        Agent = new AgentInfo { Version = "1.0.0", ImageDigest = TestIds.Digest, Protocol = 1, ProtocolsSupported = [1] },
    };

    private static ClaimRequest ClaimWith(string claimId) => new()
    {
        ClaimId = claimId,
        InstanceId = Guid.NewGuid().ToString("D"),
        Kinds = [new KindOffer { Kind = JobKinds.PdfExtract, SchemaVersions = [1], EngineVersion = EngineVersions.Pdf }],
        Capacity = new ClaimCapacity { CpuBudgetFreeMilli = 3000, MemBudgetFreeMiB = 5120, TmpFreeMiB = 3072, HeavySlotsFree = 2, EffectiveConcurrency = 2 },
        Agent = new AgentInfo { Version = "1.0.0", ImageDigest = TestIds.Digest, Protocol = 1, ProtocolsSupported = [1] },
    };

    [Fact]
    public async Task RW010_RW014_RW016_requests_carry_protocol_auth_and_correlation_but_never_client_version_headers()
    {
        var (api, server, _) = Create();
        var response = await api.NodeHeartbeatAsync(Heartbeat(), CancellationToken.None);

        Assert.Equal(ApiKind.Ok, response.Kind);
        var request = Assert.Single(server.Requests);
        Assert.Equal("Bearer " + TestIds.NodeToken(), request.Headers["Authorization"]);
        Assert.Equal("1", request.Headers[Wire.HeaderProtocol]);
        Assert.True(Guid.TryParse(request.Headers[Wire.HeaderCorrelation], out _));
        Assert.Equal("1.0.0", request.Headers[Wire.HeaderAgentVersion]);
        Assert.False(request.Headers.ContainsKey("X-Client-Platform"));
        Assert.False(request.Headers.ContainsKey("X-App-Version"));
        Assert.Equal(Wire.BasePath + "/workers/heartbeat", request.Path);
    }

    [Fact]
    public async Task RW023_claim_replay_with_the_same_claimId_returns_the_same_job_and_fence()
    {
        var (api, server, _) = Create();
        var pdf = CanaryPdf.Build();
        server.Enqueue(new ServerJob { Id = TestIds.Job(1), Inputs = { ["pdf"] = pdf } });
        var claimId = Guid.NewGuid().ToString("D");

        var first = await api.ClaimAsync(ClaimWith(claimId), CancellationToken.None);
        var replay = await api.ClaimAsync(ClaimWith(claimId), CancellationToken.None);
        var next = await api.ClaimAsync(ClaimWith(Guid.NewGuid().ToString("D")), CancellationToken.None);

        Assert.Equal(ApiKind.Ok, first.Kind);
        Assert.Equal(1, first.Value!.Job!.Fence);
        Assert.Equal(first.Value.Job.Id, replay.Value!.Job!.Id);
        Assert.Equal(1, replay.Value.Job.Fence);
        Assert.Equal(ApiKind.NoContent, next.Kind);
        Assert.Equal("no_work", next.NoContentReason);
        Assert.Equal(TimeSpan.FromSeconds(10), next.RetryAfter);
    }

    [Fact]
    public async Task claim_response_deserialises_limits_inputs_and_params()
    {
        var (api, server, _) = Create();
        var pdf = CanaryPdf.Build();
        server.Enqueue(new ServerJob { Id = TestIds.Job(2), Inputs = { ["pdf"] = pdf } });

        var response = await api.ClaimAsync(ClaimWith(Guid.NewGuid().ToString("D")), CancellationToken.None);

        var job = response.Value!.Job!;
        Assert.Equal(JobKinds.PdfExtract, job.Kind);
        Assert.Equal(120, job.Limits.TimeoutSeconds);
        Assert.Equal(1000, job.Limits.CpuMilli);
        Assert.Equal("pdf", Assert.Single(job.Inputs).Name);
        Assert.Equal(TestIds.Sha(pdf), job.Inputs[0].Sha256);
        Assert.Equal("flat", job.Params.GetProperty("mode").GetString());
    }

    [Fact]
    public async Task RW088_complete_body_hash_covers_the_exact_utf8_bytes_of_resultJson()
    {
        var (api, server, _) = Create();
        server.Enqueue(new ServerJob { Id = TestIds.Job(3), Inputs = { ["pdf"] = CanaryPdf.Build() } });
        var claimed = await api.ClaimAsync(ClaimWith(Guid.NewGuid().ToString("D")), CancellationToken.None);
        var job = claimed.Value!.Job!;

        // Non-ASCII, quotes, backslashes and control escapes: the outer JSON string must round-trip losslessly.
        var resultUtf8 = Encoding.UTF8.GetBytes("{\"text\":\"café \\\"quoted\\\" \\u0001   end\"}");
        var request = new CompleteRequest { Fence = job.Fence, ResultSha256 = TestIds.Sha(resultUtf8), ResultJson = Encoding.UTF8.GetString(resultUtf8) };
        var response = await api.CompleteAsync(job.Id, ProtocolJson.ToUtf8(request), CancellationToken.None);

        Assert.Equal(ApiKind.Ok, response.Kind);
        Assert.Equal("succeeded", response.Value!.Status);
        Assert.Equal(request.ResultSha256, server.Job(job.Id).CompletedHash);
        Assert.Equal(request.ResultJson, server.Job(job.Id).CompletedBody);
    }

    [Fact]
    public async Task RW074_stale_fence_complete_is_lease_lost()
    {
        var (api, server, _) = Create();
        server.Enqueue(new ServerJob { Id = TestIds.Job(4), Inputs = { ["pdf"] = CanaryPdf.Build() } });
        var job = (await api.ClaimAsync(ClaimWith(Guid.NewGuid().ToString("D")), CancellationToken.None)).Value!.Job!;
        var body = Encoding.UTF8.GetBytes("{}");

        var response = await api.CompleteAsync(job.Id, ProtocolJson.ToUtf8(new CompleteRequest { Fence = job.Fence + 5, ResultSha256 = TestIds.Sha(body), ResultJson = "{}" }), CancellationToken.None);

        Assert.True(response.IsLeaseLost);
        Assert.Equal("superseded", response.Reason);
    }

    [Fact]
    public async Task RW013_a_missing_protocol_header_is_route_absent_but_a_coded_404_is_not()
    {
        var (api, server, _) = Create();
        server.RouteAbsent = true;
        var absent = await api.NodeHeartbeatAsync(Heartbeat(), CancellationToken.None);
        Assert.Equal(ApiKind.RouteAbsent, absent.Kind);

        server.RouteAbsent = false;
        var coded = await api.JobHeartbeatAsync(TestIds.Job(99), new JobHeartbeatRequest { Fence = 1 }, CancellationToken.None);
        Assert.Equal(ApiKind.NotFound, coded.Kind);
        Assert.Equal("job_not_found", coded.Code);
    }

    [Fact]
    public async Task proxy_5xx_without_the_protocol_header_is_transient_not_route_absent()
    {
        var (api, server, _) = Create();
        server.Override = _ => new HttpResponseMessage(HttpStatusCode.BadGateway);

        var response = await api.ClaimAsync(ClaimWith(Guid.NewGuid().ToString("D")), CancellationToken.None);

        Assert.Equal(ApiKind.ServerError, response.Kind);
        Assert.True(response.Transient);
    }

    [Fact]
    public async Task rate_limit_answers_carry_retry_after()
    {
        var (api, server, _) = Create();
        server.Override = _ =>
        {
            var limited = FakeServerHandler.Problem((HttpStatusCode)429, "rate_limited");
            limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            return limited;
        };

        var response = await api.ClaimAsync(ClaimWith(Guid.NewGuid().ToString("D")), CancellationToken.None);

        Assert.Equal(ApiKind.RateLimited, response.Kind);
        Assert.Equal(TimeSpan.FromSeconds(7), response.RetryAfter);
        Assert.True(response.Transient);
    }

    [Fact]
    public async Task RW012_426_with_a_common_protocol_downgrades_once_and_retries()
    {
        var (api, server, negotiator) = Create(supported: [1, 2]);
        Assert.Equal(2, negotiator.Current);

        var response = await api.NodeHeartbeatAsync(Heartbeat(), CancellationToken.None);

        Assert.Equal(ApiKind.Ok, response.Kind);
        Assert.Equal(1, negotiator.Current);
        Assert.Equal(new[] { "2", "1" }, server.Requests.Select(r => r.Headers[Wire.HeaderProtocol]).ToArray());
    }

    [Fact]
    public async Task RW012_426_without_a_common_protocol_is_reported_as_a_mismatch()
    {
        var (api, server, negotiator) = Create();
        server.SupportedProtocols = [2];

        var response = await api.NodeHeartbeatAsync(Heartbeat(), CancellationToken.None);

        Assert.Equal(ApiKind.ProtocolUnsupported, response.Kind);
        Assert.Equal(new[] { 2 }, response.Supported);
        Assert.Equal(1, negotiator.Current);
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task wrong_token_is_unauthorized()
    {
        var (api, server, _) = Create();
        server.ExpectedToken = "orw1_" + new string('c', 16) + "_" + new string('D', 43);

        var response = await api.NodeHeartbeatAsync(Heartbeat(), CancellationToken.None);

        Assert.Equal(ApiKind.Unauthorized, response.Kind);
    }

    [Fact]
    public async Task RW060_RW063_inputs_are_streamed_with_headers_and_a_range_resumes_at_the_offset()
    {
        var (api, server, _) = Create();
        var pdf = CanaryPdf.Build();
        server.Enqueue(new ServerJob { Id = TestIds.Job(5), Inputs = { ["pdf"] = pdf } });
        var job = (await api.ClaimAsync(ClaimWith(Guid.NewGuid().ToString("D")), CancellationToken.None)).Value!.Job!;

        using var full = await api.OpenInputAsync(job.Id, "pdf", job.Fence, 0, null, CancellationToken.None);
        Assert.Equal(ApiKind.Ok, full.Info.Kind);
        Assert.False(full.Partial);
        Assert.Equal(TestIds.Sha(pdf), full.ContentSha256);
        Assert.Equal(TestIds.Sha(pdf), full.ETag);
        Assert.Equal((long?)pdf.Length, full.ContentLength);

        using var partial = await api.OpenInputAsync(job.Id, "pdf", job.Fence, 40, TestIds.Sha(pdf), CancellationToken.None);
        Assert.True(partial.Partial);
        Assert.Equal((long?)40, partial.RangeStart);
        using var memory = new MemoryStream();
        await partial.Body!.CopyToAsync(memory);
        Assert.Equal(pdf.AsSpan(40).ToArray(), memory.ToArray());

        var fenceRequest = server.Requests.Last(r => r.Path.EndsWith("/inputs/pdf", StringComparison.Ordinal));
        Assert.Equal(job.Fence.ToString(), fenceRequest.Headers[Wire.HeaderFence]);
        Assert.StartsWith("bytes=40-", fenceRequest.Headers["Range"]);
    }

    [Fact]
    public async Task RW066_outputs_are_hashed_and_a_wrong_declared_hash_is_rejected()
    {
        var (api, server, _) = Create();
        server.Enqueue(new ServerJob { Id = TestIds.Job(6), Kind = JobKinds.MediaAudioExtract, Inputs = { ["media"] = new byte[] { 1, 2, 3 } } });
        var job = (await api.ClaimAsync(ClaimWith(Guid.NewGuid().ToString("D")), CancellationToken.None)).Value!.Job!;
        using var dir = new TempDir();
        var path = dir.File("chunk-0000.mp3");
        await File.WriteAllBytesAsync(path, new byte[] { 9, 8, 7, 6 });

        var good = await api.PutOutputAsync(job.Id, "chunk-0000.mp3", job.Fence, TestIds.Sha(new byte[] { 9, 8, 7, 6 }), path, CancellationToken.None);
        var bad = await api.PutOutputAsync(job.Id, "chunk-0000.mp3", job.Fence, new string('0', 64), path, CancellationToken.None);

        Assert.Equal(ApiKind.Ok, good.Kind);
        Assert.Equal(4, good.Value!.SizeBytes);
        Assert.Equal(new byte[] { 9, 8, 7, 6 }, server.Outputs[job.Id]["chunk-0000.mp3"]);
        Assert.Equal(ApiKind.Unprocessable, bad.Kind);
        Assert.Equal("output_hash_mismatch", bad.Code);
        var request = server.Requests.First(r => r.Method == HttpMethod.Put);
        Assert.Equal("application/octet-stream", request.Headers["Content-Type"]);
    }

    [Fact]
    public async Task connection_failures_are_classified_as_network_errors()
    {
        var server = new FakeServerHandler { Override = _ => throw new HttpRequestException("boom") };
        var http = new HttpClient(server) { BaseAddress = new Uri("https://api.example.test") };
        var api = new HttpRemoteWorkerApi(http, TestIds.NodeToken(), "1.0.0", new ProtocolNegotiator());

        var response = await api.NodeHeartbeatAsync(Heartbeat(), CancellationToken.None);

        Assert.Equal(ApiKind.Network, response.Kind);
        Assert.True(response.Transient);
    }

    [Fact]
    public void problem_json_error_codes_map_to_the_catalogue_kinds()
    {
        // Appendix A: every documented status maps to one classification the agent branches on.
        var cases = new (HttpStatusCode Status, string Code, ApiKind Kind)[]
        {
            (HttpStatusCode.BadRequest, "bad_request", ApiKind.BadRequest),
            (HttpStatusCode.Forbidden, "node_quarantined", ApiKind.Forbidden),
            (HttpStatusCode.Conflict, "lease_lost", ApiKind.Conflict),
            (HttpStatusCode.RequestEntityTooLarge, "result_too_large", ApiKind.PayloadTooLarge),
            (HttpStatusCode.RequestedRangeNotSatisfiable, "range_not_satisfiable", ApiKind.RangeNotSatisfiable),
            ((HttpStatusCode)422, "result_invalid", ApiKind.Unprocessable),
            (HttpStatusCode.NotImplemented, "not_implemented", ApiKind.NotImplemented),
            (HttpStatusCode.ServiceUnavailable, "storage_unavailable", ApiKind.ServerError),
        };

        foreach (var (status, code, kind) in cases)
        {
            using var response = FakeServerHandler.Problem(status, code);
            var head = HttpRemoteWorkerApi.ClassifyAsync(response, CancellationToken.None).GetAwaiter().GetResult();
            Assert.Equal(kind, head.Kind);
            Assert.Equal(code, head.Code);
        }
    }

    [Fact]
    public void applies_frozen_503_is_not_transient()
    {
        using var response = FakeServerHandler.Problem(HttpStatusCode.ServiceUnavailable, "applies_frozen");
        var head = HttpRemoteWorkerApi.ClassifyAsync(response, CancellationToken.None).GetAwaiter().GetResult();

        Assert.Equal(ApiKind.ServerError, head.Kind);
        Assert.False(head.Transient);
    }

    [Fact]
    public void unknown_json_members_are_ignored_by_the_reader()
    {
        const string json = "{\"job\":null,\"serverTime\":\"2026-10-05T12:00:00.000Z\",\"somethingNew\":{\"a\":1},\"desired\":{\"revision\":9,\"extra\":true,\"allowedKinds\":[\"pdf.extract\"]}}";

        var parsed = JsonSerializer.Deserialize<ClaimResponse>(json, ProtocolJson.Options)!;

        Assert.Null(parsed.Job);
        Assert.Equal(9, parsed.Desired!.Revision);
        Assert.Equal(new[] { "pdf.extract" }, parsed.Desired.AllowedKinds);
    }
}
