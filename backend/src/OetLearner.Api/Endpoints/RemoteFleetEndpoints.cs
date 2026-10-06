using System.Security.Claims;
using OetLearner.Api.Domain;
using OetLearner.Api.Security;
using OetLearner.Api.Services.RemoteJobs;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Service plane of the remote-worker boundary: <c>/v1/internal/fleet</c> (OET-RWP/1 section 7.1), called by the fleet manager with
/// the fleet-service credential. Answers <c>404 fleet_service_disabled</c> while <c>remote_fleet_service_enabled</c> is off. Node
/// registration is idempotent on <c>nodeRef</c>, the node token is returned exactly once, and every mutation writes one audit row
/// (no secret in it) attributed to <c>fleet:&lt;credentialId&gt;</c>.
/// </summary>
public static class RemoteFleetEndpoints
{
    private const long BodyLimit = 64 * 1024;
    private const string ActorName = "fleet-manager";

    public static IEndpointRouteBuilder MapRemoteFleetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RemoteWorkerMiddlewareExtensions.ServicePlanePrefix)
            .RequireAuthorization(RemoteWorkerAuth.FleetPolicy)
            .AddEndpointFilter<RemoteFleetPlaneFilter>()
            .WithTags("Remote Fleet");

        group.MapPost("/nodes", RegisterAsync);
        group.MapGet("/nodes", ListNodesAsync);
        group.MapGet("/nodes/{id}", GetNodeAsync);
        group.MapPut("/nodes/{id}/policy", UpdatePolicyAsync);
        group.MapPost("/nodes/{id}/enable", (string id, HttpContext http, RemoteWorkerService nodes, CancellationToken ct)
            => TransitionAsync("enable", id, http, nodes, ct));
        group.MapPost("/nodes/{id}/drain", (string id, HttpContext http, RemoteWorkerService nodes, CancellationToken ct)
            => TransitionAsync("drain", id, http, nodes, ct));
        group.MapPost("/nodes/{id}/disable", (string id, HttpContext http, RemoteWorkerService nodes, CancellationToken ct)
            => TransitionAsync("disable", id, http, nodes, ct));
        group.MapPost("/nodes/{id}/quarantine", (string id, HttpContext http, RemoteWorkerService nodes, CancellationToken ct)
            => TransitionAsync("quarantine", id, http, nodes, ct));
        group.MapPost("/nodes/{id}/revoke", (string id, HttpContext http, RemoteWorkerService nodes, CancellationToken ct)
            => TransitionAsync("revoke", id, http, nodes, ct));
        group.MapPost("/nodes/{id}/tokens/rotate", RotateAsync);
        group.MapPost("/nodes/{id}/canary", CanaryAsync);

        group.MapGet("/jobs", ListJobsAsync);
        group.MapGet("/jobs/{id}", GetJobAsync);
        group.MapPost("/jobs/{id}/requeue", (string id, HttpContext http, RemoteFleetJobsService jobs, CancellationToken ct)
            => JobActionAsync("requeue", id, http, jobs, ct));
        group.MapPost("/jobs/{id}/force-local", (string id, HttpContext http, RemoteFleetJobsService jobs, CancellationToken ct)
            => JobActionAsync("force-local", id, http, jobs, ct));
        group.MapPost("/jobs/{id}/cancel", (string id, HttpContext http, RemoteFleetJobsService jobs, CancellationToken ct)
            => JobActionAsync("cancel", id, http, jobs, ct));

        group.MapGet("/stats", async (RemoteFleetJobsService jobs, CancellationToken ct)
            => (IResult)new RemoteJsonResult(await jobs.StatsAsync(ct)));
        group.MapGet("/status", async (RemoteFleetJobsService jobs, CancellationToken ct)
            => (IResult)new RemoteJsonResult(await jobs.StatusAsync(ct)));

        return app;
    }

    private static string ActorId(HttpContext http)
        => RemoteAudit.FleetActor(RemoteWorkerAuth.CredentialOf(http)?.TokenId ?? "unknown");

    // ── nodes ────────────────────────────────────────────────────────────────

    private static async Task<IResult> RegisterAsync(HttpContext http, RemoteWorkerService nodes, CancellationToken ct)
    {
        var (body, error) = await RemoteRequestBody.ReadJsonAsync<RemoteRegisterRequestDto>(http, BodyLimit, ct);
        if (error is not null) return error;

        var result = await nodes.RegisterAsync(body!, ActorId(http), ActorName, ct);
        if (result.Error is not null) return result.Error;

        var response = new Dictionary<string, object?>
        {
            ["node"] = result.Node,
            ["token"] = result.Token is null
                ? null
                : new Dictionary<string, object?>
                {
                    ["id"] = result.Token.Id,
                    ["value"] = result.Token.Value,
                    ["expiresAt"] = RemoteIds.FormatTime(result.Token.ExpiresAt),
                },
        };
        return new RemoteJsonResult(response, result.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK);
    }

    private static async Task<IResult> ListNodesAsync(string? status, int? limit, RemoteWorkerService nodes, CancellationToken ct)
    {
        if (status is not null && !RemoteNodeStatus.All.Contains(status))
        {
            return RemoteProblems.BadRequest("status is not a known node status.");
        }

        var views = await nodes.ListViewsAsync(status, limit ?? 100, ct);
        return new RemoteJsonResult(new Dictionary<string, object?> { ["nodes"] = views });
    }

    private static async Task<IResult> GetNodeAsync(string id, RemoteWorkerService nodes, CancellationToken ct)
    {
        if (!RemoteIds.IsNodeId(id)) return RemoteProblems.NodeNotFound();
        var view = await nodes.GetViewAsync(id, ct);
        if (view is null) return RemoteProblems.NodeNotFound();
        return new RemoteJsonResult(view);
    }

    private static async Task<IResult> UpdatePolicyAsync(string id, HttpContext http, RemoteWorkerService nodes, CancellationToken ct)
    {
        if (!RemoteIds.IsNodeId(id)) return RemoteProblems.NodeNotFound();
        var (body, error) = await RemoteRequestBody.ReadJsonAsync<RemotePolicyUpdateDto>(http, BodyLimit, ct);
        if (error is not null) return error;

        var (revision, failure) = await nodes.UpdatePolicyAsync(id, body!, ActorId(http), ActorName, ct);
        if (failure is not null) return failure;
        return new RemoteJsonResult(new Dictionary<string, object?> { ["revision"] = revision });
    }

    private static async Task<IResult> TransitionAsync(string action, string id, HttpContext http, RemoteWorkerService nodes, CancellationToken ct)
    {
        if (!RemoteIds.IsNodeId(id)) return RemoteProblems.NodeNotFound();
        var reason = await ReadOptionalReasonAsync(http, ct);
        var result = await nodes.TransitionAsync(id, action, reason, ActorId(http), ActorName, ct);
        if (result.Error is not null) return result.Error;
        return new RemoteJsonResult(result.Node!);
    }

    private static async Task<IResult> RotateAsync(string id, HttpContext http, RemoteWorkerService nodes, CancellationToken ct)
    {
        if (!RemoteIds.IsNodeId(id)) return RemoteProblems.NodeNotFound();
        RemoteRotateRequestDto? request = null;
        if (http.Request.ContentLength is > 0)
        {
            var (body, error) = await RemoteRequestBody.ReadJsonAsync<RemoteRotateRequestDto>(http, BodyLimit, ct);
            if (error is not null) return error;
            request = body;
        }

        var (token, failure) = await nodes.RotateTokenAsync(id, request, ActorId(http), ActorName, ct);
        if (failure is not null) return failure;

        return new RemoteJsonResult(
            new Dictionary<string, object?>
            {
                ["token"] = new Dictionary<string, object?>
                {
                    ["id"] = token!.Id,
                    ["value"] = token.Value,
                    ["expiresAt"] = RemoteIds.FormatTime(token.ExpiresAt),
                },
            },
            StatusCodes.Status201Created);
    }

    private static async Task<IResult> CanaryAsync(string id, HttpContext http, RemoteWorkerService nodes, CancellationToken ct)
    {
        if (!RemoteIds.IsNodeId(id)) return RemoteProblems.NodeNotFound();
        var (jobId, failure) = await nodes.EnqueueCanaryAsync(id, ActorId(http), ActorName, ct);
        if (failure is not null) return failure;
        return new RemoteJsonResult(new Dictionary<string, object?> { ["jobId"] = jobId }, StatusCodes.Status201Created);
    }

    // ── jobs ─────────────────────────────────────────────────────────────────

    private static async Task<IResult> ListJobsAsync(
        string? state,
        string? kind,
        string? nodeId,
        int? limit,
        RemoteFleetJobsService jobs,
        CancellationToken ct)
    {
        if (state is not null && !RemoteJobState.Terminal.Contains(state)
            && state != RemoteJobState.Queued && state != RemoteJobState.Leased)
        {
            return RemoteProblems.BadRequest("state is not a known job state.");
        }

        if (nodeId is not null && !RemoteIds.IsNodeId(nodeId)) return RemoteProblems.BadRequest("nodeId is invalid.");
        var rows = await jobs.ListAsync(state, kind, nodeId, limit ?? 100, ct);
        return new RemoteJsonResult(new Dictionary<string, object?> { ["jobs"] = rows });
    }

    private static async Task<IResult> GetJobAsync(string id, RemoteFleetJobsService jobs, CancellationToken ct)
    {
        if (!RemoteIds.IsJobId(id)) return RemoteProblems.JobNotFound();
        var view = await jobs.GetAsync(id, ct);
        if (view is null) return RemoteProblems.JobNotFound();
        return new RemoteJsonResult(view);
    }

    private static async Task<IResult> JobActionAsync(string action, string id, HttpContext http, RemoteFleetJobsService jobs, CancellationToken ct)
    {
        if (!RemoteIds.IsJobId(id)) return RemoteProblems.JobNotFound();
        var actor = ActorId(http);
        var (state, failure) = action switch
        {
            "requeue" => await jobs.RequeueAsync(id, actor, ActorName, ct),
            "force-local" => await jobs.ForceLocalAsync(id, actor, ActorName, ct),
            _ => await jobs.CancelAsync(id, actor, ActorName, ct),
        };
        if (failure is not null) return failure;
        return new RemoteJsonResult(new Dictionary<string, object?> { ["id"] = id, ["state"] = state });
    }

    /// <summary>An optional <c>{ "reason": "..." }</c> body: bounded, printable, never required.</summary>
    private static async Task<string?> ReadOptionalReasonAsync(HttpContext http, CancellationToken ct)
    {
        if (http.Request.ContentLength is not > 0) return null;
        var (body, error) = await RemoteRequestBody.ReadJsonAsync<RemoteReasonRequestDto>(http, BodyLimit, ct);
        if (error is not null || body?.Reason is null) return null;
        var reason = body.Reason.Trim();
        return reason.Length is 0 or > 200 || reason.Any(char.IsControl) ? null : reason;
    }
}

/// <summary>
/// Owner break-glass on the remote workers: <c>/v1/admin/remote-workers</c> (OET-RWP/1 section 7.2). Every route needs the platform
/// system-admin permission AND the owner's unlock ticket (<c>OwnerAgent</c> policy). The fleet credential is minted or rotated here
/// (shown once, to the owner, who places it in the manager's secret file); <c>disable</c> and <c>revoke</c> work without the manager.
/// </summary>
public static class RemoteWorkerAdminEndpoints
{
    public static IEndpointRouteBuilder MapRemoteWorkerAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/remote-workers")
            .RequireAuthorization("AdminSystemAdmin")
            .RequireAuthorization(OwnerAgentPolicies.Unlocked)
            .RequireRateLimiting("PerUser")
            .WithTags("Remote Workers Admin");

        group.MapPost("/fleet-credential", IssueFleetCredentialAsync).RequireRateLimiting("PerUserWrite");
        group.MapGet("/nodes", ListNodesAsync);
        group.MapPost("/nodes/{id}/disable", (string id, ClaimsPrincipal principal, RemoteWorkerService nodes, CancellationToken ct)
            => TransitionAsync("disable", id, principal, nodes, ct)).RequireRateLimiting("PerUserWrite");
        group.MapPost("/nodes/{id}/revoke", (string id, ClaimsPrincipal principal, RemoteWorkerService nodes, CancellationToken ct)
            => TransitionAsync("revoke", id, principal, nodes, ct)).RequireRateLimiting("PerUserWrite");
        return app;
    }

    private static string Actor(ClaimsPrincipal principal)
        => principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown-admin";

    private static async Task<IResult> IssueFleetCredentialAsync(
        RemoteFleetCredentialRequestDto? request,
        ClaimsPrincipal principal,
        RemoteFleetCredentialService credentials,
        CancellationToken ct)
    {
        var (token, failure) = await credentials.IssueAsync(request?.GraceSeconds, request?.TtlDays, Actor(principal), principal.Identity?.Name ?? Actor(principal), ct);
        if (failure is not null) return failure;

        return new RemoteJsonResult(
            new Dictionary<string, object?>
            {
                ["token"] = new Dictionary<string, object?>
                {
                    ["id"] = token!.Id,
                    ["value"] = token.Value,
                    ["expiresAt"] = RemoteIds.FormatTime(token.ExpiresAt),
                },
                ["note"] = "Shown once. Place it in the manager's /run/secrets/fleet_api_credential.",
            },
            StatusCodes.Status201Created);
    }

    private static async Task<IResult> ListNodesAsync(RemoteWorkerService nodes, IRemoteJobFlags flags, CancellationToken ct)
    {
        // Read-only break-glass view; it follows the service-plane flag (OET-RWP/1 section 9.2).
        if (!(await flags.GetAsync(ct)).FleetService)
        {
            return RemoteProblems.NotFound("fleet_service_disabled", "The fleet service plane is disabled.");
        }

        return new RemoteJsonResult(new Dictionary<string, object?> { ["nodes"] = await nodes.ListViewsAsync(null, 200, ct) });
    }

    private static async Task<IResult> TransitionAsync(string action, string id, ClaimsPrincipal principal, RemoteWorkerService nodes, CancellationToken ct)
    {
        if (!RemoteIds.IsNodeId(id)) return RemoteProblems.NodeNotFound();
        var result = await nodes.TransitionAsync(id, action, "owner break-glass", Actor(principal), principal.Identity?.Name ?? Actor(principal), ct);
        if (result.Error is not null) return result.Error;
        return new RemoteJsonResult(result.Node!);
    }
}
