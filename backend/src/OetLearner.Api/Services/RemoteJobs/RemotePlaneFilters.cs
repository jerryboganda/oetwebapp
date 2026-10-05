using System.Globalization;
using System.Net;
using Microsoft.Extensions.Options;
using Npgsql;
using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services.RemoteJobs;

/// <summary>Header names of the protocol (OET-RWP/1 section 2.3).</summary>
public static class RemoteHeaders
{
    public const string Protocol = "X-Remote-Protocol";
    public const string ProtocolMin = "X-Remote-Protocol-Min";
    public const string AgentVersion = "X-Remote-Agent-Version";
    public const string Fence = "X-Remote-Fence";
    public const string ContentSha256 = "X-Content-SHA256";
    public const string Reason = "X-Remote-Reason";
    public const string DesiredRevision = "X-Remote-Desired-Revision";

    /// <summary>HttpContext.Items key holding the protocol number the caller spoke for this request.</summary>
    public const string RequestProtocolItem = "oet.remote.protocol";
}

/// <summary>
/// Normalised view of <see cref="RemoteJobsOptions"/>. <c>IOptionsMonitor</c> hands back the same
/// instance until configuration reloads, so the clamped copy is rebuilt only then.
/// </summary>
public sealed class RemoteJobsSettings(IOptionsMonitor<RemoteJobsOptions> monitor)
{
    private sealed record Cached(RemoteJobsOptions Raw, RemoteJobsOptions Normalized);

    private volatile Cached? _cached;

    public RemoteJobsOptions Current
    {
        get
        {
            var raw = monitor.CurrentValue;
            var cached = _cached;
            if (cached is not null && ReferenceEquals(cached.Raw, raw)) return cached.Normalized;

            var normalized = raw.Normalized();
            _cached = new Cached(raw, normalized);
            return normalized;
        }
    }

    /// <summary>The protocol numbers the API accepts, ascending.</summary>
    public IReadOnlyList<int> SupportedProtocols
    {
        get
        {
            var options = Current;
            return Enumerable.Range(options.MinProtocol, options.CurrentProtocol - options.MinProtocol + 1).ToArray();
        }
    }
}

public static class RemoteWorkerMiddlewareExtensions
{
    public const string JobPlanePrefix = "/v1/internal/remote-worker";
    public const string ServicePlanePrefix = "/v1/internal/fleet";

    /// <summary>
    /// Adds <c>X-Remote-Protocol</c>, <c>X-Remote-Protocol-Min</c> and <c>Cache-Control: no-store</c> to EVERY
    /// response of the two remote planes, including 401/429/5xx and responses the exception handler rewrites
    /// (set in <c>OnStarting</c>, which runs after any response reset). Must sit before authentication.
    /// </summary>
    public static IApplicationBuilder UseRemoteWorkerProtocolHeaders(this IApplicationBuilder app)
        => app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            if (path.StartsWithSegments(JobPlanePrefix, StringComparison.OrdinalIgnoreCase)
                || path.StartsWithSegments(ServicePlanePrefix, StringComparison.OrdinalIgnoreCase))
            {
                var settings = context.RequestServices.GetService<RemoteJobsSettings>();
                var options = settings?.Current;
                context.Response.OnStarting(() =>
                {
                    var headers = context.Response.Headers;
                    headers[RemoteHeaders.Protocol] = (options?.CurrentProtocol ?? 1).ToString(CultureInfo.InvariantCulture);
                    headers[RemoteHeaders.ProtocolMin] = (options?.MinProtocol ?? 1).ToString(CultureInfo.InvariantCulture);
                    headers.CacheControl = "no-store";
                    return Task.CompletedTask;
                });
            }

            await next();
        });
}

/// <summary>Shared steps of both plane filters: protocol negotiation and exception mapping.</summary>
internal static class RemotePlaneCommon
{
    /// <summary>
    /// Returns an error result when <c>X-Remote-Protocol</c> is missing (400) or outside [N-1, N] (426), else null.
    /// </summary>
    public static RemoteProblemResult? CheckProtocol(HttpContext http, RemoteJobsSettings settings)
    {
        var raw = http.Request.Headers[RemoteHeaders.Protocol].ToString();
        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var protocol) || protocol < 1)
        {
            return RemoteProblems.BadRequest("The X-Remote-Protocol header is required.");
        }

        var options = settings.Current;
        if (protocol < options.MinProtocol || protocol > options.CurrentProtocol)
        {
            return RemoteProblems.UpgradeRequired(settings.SupportedProtocols);
        }

        http.Items[RemoteHeaders.RequestProtocolItem] = protocol;
        return null;
    }

    /// <summary>Maps an exception escaping a handler to the protocol's error envelope.</summary>
    public static RemoteProblemResult MapException(Exception exception, ILogger logger, HttpContext http)
    {
        switch (exception)
        {
            case BadHttpRequestException bad when bad.StatusCode == StatusCodes.Status413PayloadTooLarge:
                return RemoteProblems.PayloadTooLarge("result_too_large", "The request body is too large.");
            case BadHttpRequestException:
                return RemoteProblems.BadRequest("The request body is invalid.");
            case NpgsqlException or TimeoutException:
                logger.LogWarning(exception, "Remote route {Path} hit a transient data-store failure.", http.Request.Path.Value);
                return RemoteProblems.ServiceUnavailable("service_unavailable", "The service is temporarily unavailable.");
            default:
                logger.LogError(exception, "Remote route {Path} failed.", http.Request.Path.Value);
                return RemoteProblems.InternalError();
        }
    }
}

/// <summary>
/// Job-plane (<c>/v1/internal/remote-worker</c>) endpoint filter. Runs AFTER authentication, so the node is
/// verified: negotiates the protocol, applies the per-node (and per-job) rate limits keyed by the VERIFIED node
/// id, and turns exceptions into the protocol's error envelope. It never logs request or response bodies.
/// </summary>
public sealed class RemoteJobPlaneFilter(
    RemoteJobsSettings settings,
    RemoteRateLimits limits,
    ILogger<RemoteJobPlaneFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        if (RemotePlaneCommon.CheckProtocol(http, settings) is { } protocolError) return protocolError;

        var node = RemoteWorkerAuth.NodeOf(http);
        var bucket = http.GetEndpoint()?.Metadata.GetMetadata<RemoteRateBucketMetadata>();
        if (node is not null && bucket is not null)
        {
            var perNodeLimit = bucket.Bucket == "claim" ? settings.Current.ClaimRatePerMinute : bucket.PerNodePerMinute;
            if (!limits.TryConsume(bucket.Bucket, node.Id, perNodeLimit, out var retryAfter))
            {
                return RemoteProblems.RateLimited(retryAfter);
            }

            if (bucket.PerJobPerMinute is int perJob
                && http.Request.RouteValues["id"] is string jobId
                && !limits.TryConsume(bucket.Bucket + "-job", node.Id + ":" + jobId, perJob, out retryAfter))
            {
                return RemoteProblems.RateLimited(retryAfter);
            }
        }

        try
        {
            return await next(context);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return RemotePlaneCommon.MapException(ex, logger, http);
        }
    }
}

/// <summary>
/// Service-plane (<c>/v1/internal/fleet</c>) endpoint filter: the <c>remote_fleet_service_enabled</c> flag
/// (off = 404 with a problem body, fail closed), the optional source-CIDR allow-list, protocol negotiation,
/// a 120/min limit per credential, and exception mapping.
/// </summary>
public sealed class RemoteFleetPlaneFilter(
    RemoteJobsSettings settings,
    IRemoteJobFlags flags,
    RemoteRateLimits limits,
    ILogger<RemoteFleetPlaneFilter> logger) : IEndpointFilter
{
    public const int RequestsPerMinute = 120;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        var snapshot = await flags.GetAsync(http.RequestAborted);
        if (!snapshot.FleetService)
        {
            return RemoteProblems.NotFound("fleet_service_disabled", "The fleet service plane is disabled.");
        }

        if (!IsAllowedSource(http.Connection.RemoteIpAddress, settings.Current.FleetAllowedCidrs))
        {
            return RemoteProblems.Forbidden("forbidden_ip", "This address may not call the fleet service plane.");
        }

        if (RemotePlaneCommon.CheckProtocol(http, settings) is { } protocolError) return protocolError;

        var credentialId = RemoteWorkerAuth.CredentialOf(http)?.TokenId;
        if (credentialId is not null
            && !limits.TryConsume("fleet", credentialId, RequestsPerMinute, out var retryAfter))
        {
            return RemoteProblems.RateLimited(retryAfter);
        }

        try
        {
            return await next(context);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return RemotePlaneCommon.MapException(ex, logger, http);
        }
    }

    /// <summary>True when no CIDR is configured, or <paramref name="address"/> lies inside one of them.</summary>
    public static bool IsAllowedSource(IPAddress? address, IReadOnlyList<string> cidrs)
    {
        var configured = cidrs.Where(c => !string.IsNullOrWhiteSpace(c)).ToArray();
        if (configured.Length == 0) return true;
        if (address is null) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        foreach (var cidr in configured)
        {
            if (IPNetwork.TryParse(cidr.Trim(), out var network) && network.Contains(address)) return true;
        }

        return false;
    }
}
