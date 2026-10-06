using System.Globalization;
using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.RemoteJobs;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Job plane of the remote-worker boundary: <c>/v1/internal/remote-worker</c> (OET-RWP/1 section 4). Helper agents call these routes
/// with a per-node bearer token; the learner JWT scheme cannot reach them and a node token cannot reach any learner route.
/// Endpoints stay thin: protocol negotiation, per-node rate limits and error mapping live in <see cref="RemoteJobPlaneFilter"/>,
/// the state machine in the services under <c>Services/RemoteJobs</c>. Routes are mapped only on PostgreSQL and never on the
/// <c>ai-worker</c> (the claim/CAS/reaper SQL has no SQLite path).
/// </summary>
public static class RemoteWorkerEndpoints
{
    private const int StreamsPerNode = 4;
    private const long ClaimBodyLimit = 64 * 1024;
    private const long HeartbeatBodyLimit = 512 * 1024;
    private const long FailBodyLimit = 16 * 1024;

    /// <summary>The largest <c>complete</c> body any kind may send: the biggest <c>maxResultBytes</c> plus the 64 KiB envelope allowance.</summary>
    private static readonly long CompleteBodyLimit = RemoteJobKinds.All.Max(spec => spec.Limits.MaxResultBytes) + 65536;

    private static readonly long OutputBodyLimit = RemoteJobKinds.All.Max(spec => spec.Limits.MaxOutputBytes);

    public static IEndpointRouteBuilder MapRemoteWorkerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RemoteWorkerMiddlewareExtensions.JobPlanePrefix)
            .RequireAuthorization(RemoteWorkerAuth.NodePolicy)
            .AddEndpointFilter<RemoteJobPlaneFilter>()
            .WithTags("Remote Worker");

        group.MapPost("/claim", ClaimAsync)
            .WithMetadata(new RemoteRateBucketMetadata("claim", 60));

        // The two heartbeat routes are the only ones allowed to authenticate from a <=5 s cache (section 2.2).
        group.MapPost("/workers/heartbeat", NodeHeartbeatAsync)
            .WithMetadata(RemoteAuthCacheableMetadata.Instance, new RemoteRateBucketMetadata("node-heartbeat", 12));
        group.MapPost("/jobs/{id}/heartbeat", JobHeartbeatAsync)
            .WithMetadata(RemoteAuthCacheableMetadata.Instance, new RemoteRateBucketMetadata("job-heartbeat", 240, 12));

        group.MapMethods("/jobs/{id}/inputs/{name}", ["GET", "HEAD"], GetInputAsync)
            .WithMetadata(new RemoteRateBucketMetadata("inputs", 120));
        group.MapPut("/jobs/{id}/outputs/{name}", PutOutputAsync)
            .WithMetadata(new RemoteRateBucketMetadata("outputs", 120));

        group.MapPost("/jobs/{id}/complete", CompleteAsync)
            .WithMetadata(new RemoteRateBucketMetadata("complete", 60));
        group.MapPost("/jobs/{id}/fail", FailAsync)
            .WithMetadata(new RemoteRateBucketMetadata("fail", 60));

        return app;
    }

    // ── claim ────────────────────────────────────────────────────────────────

    private static async Task<IResult> ClaimAsync(
        HttpContext http,
        RemoteClaimService claims,
        RemoteJobsSettings settings,
        CancellationToken ct)
    {
        var node = RemoteWorkerAuth.NodeOf(http)!;
        var (body, error) = await RemoteRequestBody.ReadJsonAsync<RemoteClaimRequestDto>(http, ClaimBodyLimit, ct);
        if (error is not null) return error;
        var invalid = RemoteWireValidation.Claim(body);
        if (invalid is not null) return RemoteProblems.BadRequest(invalid);

        var outcome = await claims.ClaimAsync(node, body!, ct);
        if (outcome.Error is not null) return outcome.Error;

        var revision = outcome.Node?.PolicyRevision ?? node.PolicyRevision;
        if (outcome.Leased is { } leased)
        {
            var current = outcome.Node ?? node;
            return new RemoteJsonResult(
                new Dictionary<string, object?>
                {
                    ["job"] = RemoteJobWire.ToClaimJob(leased.Job, leased.LeaseRemainingMs, settings.Current),
                    ["serverTime"] = RemoteIds.FormatTime(DateTimeOffset.UtcNow),
                    ["desired"] = RemoteDesiredState.Build(current),
                },
                StatusCodes.Status200OK,
                new Dictionary<string, string> { [RemoteHeaders.DesiredRevision] = revision.ToString(CultureInfo.InvariantCulture) });
        }

        return new RemoteNoContentResult(outcome.NoContentReason ?? "no_work", revision);
    }

    // ── node heartbeat ───────────────────────────────────────────────────────

    private static async Task<IResult> NodeHeartbeatAsync(
        HttpContext http,
        RemoteWorkerService nodes,
        CancellationToken ct)
    {
        var node = RemoteWorkerAuth.NodeOf(http)!;
        var (body, error) = await RemoteRequestBody.ReadJsonAsync<RemoteNodeHeartbeatRequestDto>(http, HeartbeatBodyLimit, ct);
        if (error is not null) return error;
        var invalid = RemoteWireValidation.NodeHeartbeat(body);
        if (invalid is not null) return RemoteProblems.BadRequest(invalid);

        var outcome = await nodes.HeartbeatAsync(node, body!, ct);
        if (outcome.Error is not null) return outcome.Error;

        return new RemoteJsonResult(
            outcome.Body!,
            StatusCodes.Status200OK,
            new Dictionary<string, string> { [RemoteHeaders.DesiredRevision] = outcome.DesiredRevision.ToString(CultureInfo.InvariantCulture) });
    }

    // ── job heartbeat ────────────────────────────────────────────────────────

    private static async Task<IResult> JobHeartbeatAsync(
        string id,
        HttpContext http,
        RemoteJobLifecycleService jobs,
        CancellationToken ct)
    {
        if (!RemoteIds.IsJobId(id)) return RemoteProblems.JobNotFound();
        var node = RemoteWorkerAuth.NodeOf(http)!;
        if (await GateAsync(node, id, jobs, ct) is { } denied) return denied;

        var (body, error) = await RemoteRequestBody.ReadJsonAsync<RemoteJobHeartbeatRequestDto>(http, ClaimBodyLimit, ct);
        if (error is not null) return error;
        var invalid = RemoteWireValidation.JobHeartbeat(body);
        if (invalid is not null) return RemoteProblems.BadRequest(invalid);

        var result = await jobs.HeartbeatAsync(id, node.Id, body!, ct);
        if (result.Error is not null) return result.Error;

        return new RemoteJsonResult(new Dictionary<string, object?>
        {
            ["leaseExpiresAt"] = RemoteIds.FormatTime(result.LeaseExpiresAt),
            ["leaseRemainingMs"] = result.LeaseRemainingMs,
            ["serverTime"] = RemoteIds.FormatTime(result.ServerTime),
            ["desiredRevision"] = node.PolicyRevision,
        });
    }

    // ── inputs ───────────────────────────────────────────────────────────────

    private static async Task<IResult> GetInputAsync(
        string id,
        string name,
        HttpContext http,
        RemoteJobLifecycleService jobs,
        RemoteInputOutputService io,
        RemoteRateLimits limits,
        CancellationToken ct)
    {
        if (!RemoteIds.IsJobId(id)) return RemoteProblems.JobNotFound();
        var node = RemoteWorkerAuth.NodeOf(http)!;
        if (await GateAsync(node, id, jobs, ct) is { } denied) return denied;
        if (!TryReadFence(http, out var fence)) return RemoteProblems.BadRequest("X-Remote-Fence is required.");
        if (name.Length is 0 or > 64) return RemoteProblems.NotFound("input_not_found", "No such input.");

        var permit = limits.TryAcquireStream(node.Id, StreamsPerNode);
        if (permit is null) return RemoteProblems.RateLimited(5);

        RemoteInputOpen opened;
        try
        {
            opened = await io.OpenInputAsync(node.Id, id, fence, name, HttpMethods.IsHead(http.Request.Method), ct);
        }
        catch
        {
            permit.Dispose();
            throw;
        }

        if (opened.Error is not null)
        {
            permit.Dispose();
            return opened.Error;
        }

        // From here the result owns the stream and the permit; no database connection is held while bytes flow.
        return new RemoteStreamResult(opened.Stream, opened.Length, opened.Sha256, opened.ContentType, HttpMethods.IsHead(http.Request.Method), permit);
    }

    // ── outputs ──────────────────────────────────────────────────────────────

    private static async Task<IResult> PutOutputAsync(
        string id,
        string name,
        HttpContext http,
        RemoteJobLifecycleService jobs,
        RemoteInputOutputService io,
        RemoteRateLimits limits,
        CancellationToken ct)
    {
        if (!RemoteIds.IsJobId(id)) return RemoteProblems.JobNotFound();
        var node = RemoteWorkerAuth.NodeOf(http)!;
        if (await GateAsync(node, id, jobs, ct) is { } denied) return denied;
        if (!TryReadFence(http, out var fence)) return RemoteProblems.BadRequest("X-Remote-Fence is required.");

        // Kestrel's default cap is sized for learner uploads; outputs may legitimately be larger (never over the registry maximum).
        var sizeFeature = http.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false }) sizeFeature.MaxRequestBodySize = OutputBodyLimit;

        using var permit = limits.TryAcquireStream(node.Id, StreamsPerNode);
        if (permit is null) return RemoteProblems.RateLimited(5);

        var declaredSha = http.Request.Headers[RemoteHeaders.ContentSha256].ToString();
        var result = await io.PutOutputAsync(node.Id, id, fence, name, declaredSha, http.Request.ContentLength, http.Request.Body, ct);
        if (result.Error is not null) return result.Error;

        return new RemoteJsonResult(new Dictionary<string, object?>
        {
            ["name"] = result.Name,
            ["sizeBytes"] = result.SizeBytes,
            ["sha256"] = result.Sha256,
            ["replaced"] = result.Replaced,
        });
    }

    // ── complete / fail ──────────────────────────────────────────────────────

    private static async Task<IResult> CompleteAsync(
        string id,
        HttpContext http,
        RemoteJobLifecycleService jobs,
        RemoteCompletionService completion,
        CancellationToken ct)
    {
        if (!RemoteIds.IsJobId(id)) return RemoteProblems.JobNotFound();
        var node = RemoteWorkerAuth.NodeOf(http)!;
        if (await GateAsync(node, id, jobs, ct) is { } denied) return denied;

        // The WHOLE body is read before any transaction opens; an oversize body is a 413 with no further work.
        var (body, error) = await RemoteRequestBody.ReadAsync(http, CompleteBodyLimit, ct);
        if (error is not null || body is null) return error ?? RemoteProblems.BadRequest("A JSON body is required.");

        var outcome = await completion.CompleteAsync(node, id, body, ct);
        if (outcome.Error is not null) return outcome.Error;

        var response = new Dictionary<string, object?>
        {
            ["status"] = outcome.Status,
            ["replayed"] = outcome.Replayed,
            ["serverTime"] = RemoteIds.FormatTime(DateTimeOffset.UtcNow),
        };
        if (outcome.Outcome is not null) response["outcome"] = outcome.Outcome;
        if (outcome.Code is not null) response["code"] = outcome.Code;
        return new RemoteJsonResult(response);
    }

    private static async Task<IResult> FailAsync(
        string id,
        HttpContext http,
        RemoteJobLifecycleService jobs,
        CancellationToken ct)
    {
        if (!RemoteIds.IsJobId(id)) return RemoteProblems.JobNotFound();
        var node = RemoteWorkerAuth.NodeOf(http)!;
        if (await GateAsync(node, id, jobs, ct) is { } denied) return denied;

        var (body, error) = await RemoteRequestBody.ReadJsonAsync<RemoteFailRequestDto>(http, FailBodyLimit, ct);
        if (error is not null) return error;
        var invalid = RemoteWireValidation.Fail(body);
        if (invalid is not null) return RemoteProblems.BadRequest(invalid);
        if (!RemoteFailCodes.IsKnown(body!.Code)) return RemoteProblems.BadRequest("code is not a known failure code.");

        var (status, replayed, failure) = await jobs.FailAsync(id, node.Id, body, ct);
        if (failure is not null) return failure;

        return new RemoteJsonResult(new Dictionary<string, object?> { ["status"] = status, ["replayed"] = replayed });
    }

    // ── shared ───────────────────────────────────────────────────────────────

    private static bool TryReadFence(HttpContext http, out long fence)
        => long.TryParse(http.Request.Headers[RemoteHeaders.Fence].ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out fence);

    /// <summary>
    /// Which node statuses may call the per-job routes (table 4.0): Pending 403 <c>node_not_active</c>; Quarantined 403
    /// <c>node_quarantined</c> EXCEPT for the known-answer canary targeted at that very node (a quarantined node must be able to
    /// prove itself, and a canary carries no data); everything else, including Draining and Disabled, may finish in-flight leases.
    /// </summary>
    private static async Task<RemoteProblemResult?> GateAsync(RemoteWorker node, string jobId, RemoteJobLifecycleService jobs, CancellationToken ct)
    {
        switch (node.Status)
        {
            case RemoteNodeStatus.Revoked:
                return RemoteProblems.Unauthorized();
            case RemoteNodeStatus.Pending:
                return RemoteProblems.Forbidden("node_not_active", "This node has not been activated.");
            case RemoteNodeStatus.Quarantined:
                var snapshot = await jobs.GetSnapshotAsync(jobId, ct);
                var canaryForThisNode = snapshot is not null
                    && snapshot.Row.Purpose == RemoteJobPurpose.Canary
                    && snapshot.Row.TargetNodeId == node.Id;
                return canaryForThisNode
                    ? null
                    : RemoteProblems.Forbidden("node_quarantined", "This node is quarantined.");
            default:
                return null;
        }
    }
}
