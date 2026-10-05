using System.Text.Json;
using System.Threading.Channels;
using Fleet.Core.Placement;
using Fleet.Core.Policy;
using Fleet.Core.Validation;
using Fleet.Manager.Api;
using Fleet.Manager.Auth;
using Fleet.Manager.Configuration;
using Fleet.Manager.Hosting;
using Fleet.Manager.Infrastructure;
using Fleet.Manager.Monitoring;
using Fleet.Manager.Operations;
using Fleet.Manager.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;

namespace Fleet.Manager.Endpoints;

// Request bodies. Every field is nullable so a missing property is a 422 from validation, never a 500.
public sealed record AddHostBody(string? NodeRef, string? DisplayName, string? Address, int? SshPort, string? Region, string? Provider);

public sealed record FingerprintBody(string? Fingerprint);

public sealed record OwnerCredentialBody(string? User, string? PrivateKey);

public sealed record RemoveBody(bool Force);

public sealed record DigestBody(string? Digest);

public sealed record PlacementBody(
    string? Kind,
    int? SchemaVersion,
    string? EngineVersion,
    int? Weight,
    int? CpuMilli,
    int? MemMiB,
    int? TmpMiB,
    string? Purpose,
    string? TargetNodeId,
    int? WaitedMinutes);

/// <summary>Turns the application layer's exceptions into stable JSON errors. The catch-all in Program maps anything else to a bare 500.</summary>
public sealed class ErrorMappingFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (FleetValidationException ex)
        {
            return Results.Json(new { code = "validation_failed", message = "The request is not valid.", issues = ex.Issues }, statusCode: StatusCodes.Status422UnprocessableEntity);
        }
        catch (FleetOperationException ex)
        {
            return Results.Json(new { code = ex.Code, message = ex.Message }, statusCode: StatusCodes.Status409Conflict);
        }
        catch (FleetNotFoundException ex)
        {
            return Results.Json(new { code = "not_found", message = ex.Message }, statusCode: StatusCodes.Status404NotFound);
        }
        catch (FleetApiException ex)
        {
            return Results.Json(
                new { code = ex.Code, message = "The OET API refused or could not complete the request.", apiStatus = (int)ex.Status },
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}

/// <summary>Every state-changing API call must carry the antiforgery header (the cookie alone, however SameSite, is never enough).</summary>
public sealed class AntiforgeryFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && !HttpMethods.IsOptions(request.Method))
        {
            var antiforgery = context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
            try
            {
                await antiforgery.ValidateRequestAsync(context.HttpContext);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.Json(new { code = "csrf_invalid", message = "Fetch /api/v1/csrf and send its token in the " + ServiceRegistration.CsrfHeaderName + " header." }, statusCode: StatusCodes.Status400BadRequest);
            }
        }

        return await next(context);
    }
}

/// <summary>
/// TOTP step-up for privileged actions (enroll, rotate, revoke, delete, drain, approve digest, token rotate, owner key
/// submit; OET-RWP/1 section 8.8). The code travels in <c>X-Fleet-Totp</c> and can be used once.
/// </summary>
public sealed class StepUpFilter : IEndpointFilter
{
    public const string HeaderName = "X-Fleet-Totp";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var owner = http.RequestServices.GetRequiredService<OwnerAccountService>();
        if (!await owner.VerifyStepUpAsync(http.Request.Headers[HeaderName].ToString(), http.RequestAborted))
        {
            return Results.Json(
                new { code = "step_up_required", message = "Enter a fresh authenticator code (header " + HeaderName + "). A code can be used once." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(context);
    }
}

/// <summary>
/// The JSON API the dashboard (a later track) calls. Every action is a typed application-service method; these
/// handlers only translate HTTP. All routes need the owner session; state changes also need the antiforgery
/// header, and privileged ones a TOTP step-up.
/// </summary>
public static class FleetEndpoints
{
    public static void MapFleetEndpoints(this WebApplication app)
    {
        app.MapGet("/healthz", async (HealthReporter health, CancellationToken ct) =>
        {
            var report = await health.GetAsync(ct);
            return report.Ok
                ? Results.Json(new { status = "ok" })
                : Results.Json(new { status = "degraded" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }).AllowAnonymous();

        app.MapGet("/api/v1/events", StreamEventsAsync).RequireAuthorization();

        app.MapGet("/metrics", async (HttpContext http, MetricsService metrics, IOptions<FleetOptions> options, CancellationToken ct) =>
        {
            var allowed = http.User.Identity?.IsAuthenticated == true;
            if (!allowed)
            {
                var secrets = options.Value.Secrets;
                var expected = SecretFile.TryRead(Path.Combine(secrets.Directory, secrets.MetricsTokenFile));
                var header = http.Request.Headers.Authorization.ToString();
                const string prefix = "Bearer ";
                allowed = header.StartsWith(prefix, StringComparison.Ordinal)
                    && SecretFile.FixedTimeEquals(header[prefix.Length..], expected);
            }

            if (!allowed)
            {
                return Results.Unauthorized();
            }

            return Results.Text(await metrics.RenderAsync(ct), "text/plain; version=0.0.4; charset=utf-8");
        }).AllowAnonymous();

        // CI sync (OET-RWP/1 section 8.7): release record + a per-run registry token, authenticated by a file secret. Off unless that file exists.
        app.MapPost("/internal/sync", async (HttpContext http, SyncPayload payload, ReleaseService releases, IOptions<FleetOptions> options, CancellationToken ct) =>
        {
            var secrets = options.Value.Secrets;
            var expected = SecretFile.TryRead(Path.Combine(secrets.Directory, secrets.SyncTokenFile));
            if (expected is null)
            {
                return Results.NotFound();
            }

            var header = http.Request.Headers.Authorization.ToString();
            const string prefix = "Bearer ";
            if (!header.StartsWith(prefix, StringComparison.Ordinal) || !SecretFile.FixedTimeEquals(header[prefix.Length..], expected))
            {
                return Results.Unauthorized();
            }

            try
            {
                var view = await releases.SyncAsync(payload, "ci-sync", ct);
                return Results.Json(new { id = view.Id, approved = view.Approved });
            }
            catch (FleetValidationException ex)
            {
                return Results.Json(new { code = "validation_failed", issues = ex.Issues }, statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        }).AllowAnonymous().RequireRateLimiting("api");

        var api = app.MapGroup("/api/v1")
            .RequireAuthorization()
            .RequireRateLimiting("api")
            .AddEndpointFilter<ErrorMappingFilter>()
            .AddEndpointFilter<AntiforgeryFilter>();

        api.MapGet("/csrf", (HttpContext http, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(http);
            return Results.Json(new { headerName = ServiceRegistration.CsrfHeaderName, token = tokens.RequestToken });
        });

        api.MapGet("/health", (HealthReporter health, CancellationToken ct) => health.GetAsync(ct));

        // ---- hosts and operations ----
        api.MapGet("/hosts", (HostService hosts, CancellationToken ct) => hosts.ListHostsAsync(ct));
        api.MapGet("/hosts/{id}", (string id, HostService hosts, CancellationToken ct) => hosts.GetHostAsync(id, includeNode: true, ct));

        api.MapPost("/hosts", async (AddHostBody body, EnrollmentService enrollment, HttpContext http, CancellationToken ct) =>
        {
            var result = await enrollment.AddHostAsync(
                new AddHostRequest(body.NodeRef ?? string.Empty, body.DisplayName ?? string.Empty, body.Address ?? string.Empty, body.SshPort ?? 22, body.Region, body.Provider),
                Actor(http),
                http.TraceIdentifier,
                ct);
            return Results.Json(result, statusCode: result.AlreadyExisted ? StatusCodes.Status200OK : StatusCodes.Status201Created);
        }).AddEndpointFilter<StepUpFilter>();

        api.MapGet("/operations", (HostService hosts, int? take, string? hostId, CancellationToken ct) => hosts.ListOperationsAsync(take ?? 50, hostId, ct));
        api.MapGet("/operations/{id}", (string id, HostService hosts, CancellationToken ct) => hosts.GetOperationAsync(id, ct));

        api.MapPost("/operations/{id}/confirm-host-key", (string id, FingerprintBody body, EnrollmentService enrollment, HttpContext http, CancellationToken ct) =>
            enrollment.ConfirmHostKeyAsync(id, body.Fingerprint, Actor(http), ct)).AddEndpointFilter<StepUpFilter>();

        api.MapPost("/operations/{id}/owner-credential", (string id, OwnerCredentialBody body, EnrollmentService enrollment, HttpContext http, CancellationToken ct) =>
            enrollment.SubmitOwnerCredentialAsync(id, body.User, body.PrivateKey, Actor(http), ct)).AddEndpointFilter<StepUpFilter>();

        api.MapPost("/operations/{id}/retry", (string id, EnrollmentService enrollment, HttpContext http, CancellationToken ct) =>
            enrollment.RetryAsync(id, Actor(http), ct));

        api.MapPost("/operations/{id}/cancel", (string id, EnrollmentService enrollment, HttpContext http, CancellationToken ct) =>
            enrollment.CancelAsync(id, Actor(http), ct));

        api.MapPost("/hosts/{id}/drain", (string id, HostService hosts, HttpContext http, CancellationToken ct) =>
            hosts.StartDrainAsync(id, Actor(http), ct)).AddEndpointFilter<StepUpFilter>();
        api.MapPost("/hosts/{id}/disable", (string id, HostService hosts, HttpContext http, CancellationToken ct) =>
            hosts.StartDisableAsync(id, Actor(http), ct)).AddEndpointFilter<StepUpFilter>();
        api.MapPost("/hosts/{id}/enable", (string id, HostService hosts, HttpContext http, CancellationToken ct) =>
            hosts.StartEnableAsync(id, Actor(http), ct)).AddEndpointFilter<StepUpFilter>();
        api.MapPost("/hosts/{id}/rotate-token", (string id, HostService hosts, HttpContext http, CancellationToken ct) =>
            hosts.StartRotateTokenAsync(id, Actor(http), ct)).AddEndpointFilter<StepUpFilter>();
        api.MapPost("/hosts/{id}/repair", (string id, HostService hosts, HttpContext http, CancellationToken ct) =>
            hosts.StartRepairAsync(id, Actor(http), ct)).AddEndpointFilter<StepUpFilter>();
        api.MapPost("/hosts/{id}/remove", (string id, RemoveBody? body, HostService hosts, HttpContext http, CancellationToken ct) =>
            hosts.StartRemoveAsync(id, body?.Force ?? false, Actor(http), ct)).AddEndpointFilter<StepUpFilter>();

        api.MapPost("/hosts/{id}/repin", async (string id, HostSecurityService security, HttpContext http, CancellationToken ct) =>
            Results.Json(new { candidates = await security.BeginRepinAsync(id, Actor(http), ct) }));
        api.MapPost("/hosts/{id}/repin/confirm", async (string id, FingerprintBody body, HostSecurityService security, HttpContext http, CancellationToken ct) =>
        {
            await security.ConfirmRepinAsync(id, body.Fingerprint ?? string.Empty, Actor(http), ct);
            return Results.NoContent();
        }).AddEndpointFilter<StepUpFilter>();

        // ---- policy ----
        api.MapGet("/policy", (PolicyService policies, CancellationToken ct) => policies.GetGlobalAsync(ct));
        api.MapPut("/policy", async (NodePolicy policy, PolicyService policies, HttpContext http, CancellationToken ct) =>
        {
            var stored = await policies.SetGlobalAsync(policy, Actor(http), ct);
            return Results.Json(new { policy = stored, pushed = await policies.PushAllAsync(Actor(http), ct) });
        }).AddEndpointFilter<StepUpFilter>();
        api.MapPut("/hosts/{id}/policy", async (string id, NodePolicy policy, PolicyService policies, HostStore hosts, HttpContext http, CancellationToken ct) =>
        {
            var stored = await policies.SetHostOverrideAsync(id, policy, Actor(http), ct);
            var host = await hosts.GetAsync(id, ct) ?? throw new FleetNotFoundException("The host");
            var pushed = host.ApiNodeId is null ? null : (int?)await policies.PushAsync(host, ct);
            return Results.Json(new { policy = stored, revision = pushed });
        }).AddEndpointFilter<StepUpFilter>();

        // ---- releases and rollouts ----
        api.MapGet("/releases", (ReleaseService releases, CancellationToken ct) => releases.ListAsync(ct));
        api.MapPost("/releases", (ReleaseRecordInput input, ReleaseService releases, HttpContext http, CancellationToken ct) =>
            releases.IngestAsync(input, Actor(http), ct));
        api.MapPost("/releases/{id}/approve", async (string id, ReleaseService releases, PolicyService policies, HttpContext http, CancellationToken ct) =>
        {
            var release = await releases.ApproveAsync(id, Actor(http), ct);
            // Nodes accept an image only once its digest is in their approvedDigests window: push it now.
            var pushed = await policies.PushAllAsync(Actor(http), ct);
            return Results.Json(new { release, pushed });
        }).AddEndpointFilter<StepUpFilter>();
        api.MapPost("/rollouts", (DigestBody body, HostService hosts, HttpContext http, CancellationToken ct) =>
            hosts.StartRolloutAsync(body.Digest ?? string.Empty, Actor(http), ct)).AddEndpointFilter<StepUpFilter>();

        // ---- fleet picture, placement, audit ----
        api.MapGet("/nodes", (FleetState state) => Results.Json(new { reachable = state.ApiReachable, polledAt = state.LastPollAt, nodes = state.Nodes }));
        api.MapPost("/placement/decide", async (PlacementBody body, PlacementService placement, CancellationToken ct) =>
        {
            var request = new PlacementRequest(
                body.Kind ?? string.Empty,
                body.SchemaVersion ?? 1,
                body.EngineVersion ?? string.Empty,
                Math.Max(1, body.Weight ?? 1),
                Math.Max(0, body.CpuMilli ?? 0),
                Math.Max(0, body.MemMiB ?? 0),
                Math.Max(0, body.TmpMiB ?? 0),
                body.Purpose ?? "apply",
                body.TargetNodeId);
            var decision = await placement.DecideAsync(request, TimeSpan.FromMinutes(Math.Max(0, body.WaitedMinutes ?? 0)), ct);
            return Results.Json(decision);
        });
        api.MapPost("/placement/release/{reservationId}", (string reservationId, PlacementService placement) =>
            Results.Json(new { released = placement.Release(reservationId) }));
        api.MapGet("/audit", async (IAuditService audit, int? take, CancellationToken ct) =>
            Results.Json(new { items = await audit.ListAsync(take ?? 100, ct) }));
        api.MapGet("/audit/verify", async (IAuditService audit, CancellationToken ct) => Results.Json(await audit.VerifyAsync(ct)));
    }

    private static string Actor(HttpContext http) => http.User.Identity?.Name ?? "owner";

    /// <summary>Server-Sent Events: <c>event: &lt;type&gt;</c> + <c>data: &lt;json&gt;</c>, a comment keep-alive every 15 seconds.</summary>
    private static async Task StreamEventsAsync(HttpContext http, IEventBus bus, CancellationToken cancellationToken)
    {
        http.Response.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers["X-Accel-Buffering"] = "no";
        using var subscription = bus.Subscribe();
        await http.Response.WriteAsync(": connected\n\n", cancellationToken);
        await http.Response.Body.FlushAsync(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            wait.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                var fleetEvent = await subscription.Reader.ReadAsync(wait.Token);
                await http.Response.WriteAsync("event: " + fleetEvent.Type + "\ndata: " + fleetEvent.DataJson + "\n\n", cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await http.Response.WriteAsync(": keepalive\n\n", cancellationToken);
            }
            catch (ChannelClosedException)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await http.Response.Body.FlushAsync(cancellationToken);
        }
    }
}
