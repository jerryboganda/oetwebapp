using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Security;

namespace OetLearner.Api.Services.OwnerAgent;

public static class OwnerAgentServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Owner Agent Console services. Options are bound lazily from the
    /// <c>OwnerAgent</c> section (env <c>OwnerAgent__*</c> in production) and are never
    /// read eagerly at startup, so a host without the console configured is unaffected.
    /// No hosted services are registered.
    /// </summary>
    public static IServiceCollection AddOwnerAgentConsole(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OwnerAgentOptions>().Bind(configuration.GetSection(OwnerAgentOptions.SectionName));

        services.AddScoped<IAuthorizationHandler, OwnerAgentAuthorizationHandler>();
        services.AddScoped<IOwnerAgentFeatureGate, OwnerAgentFeatureGate>();
        services.AddScoped<IOwnerAgentUnlockService, OwnerAgentUnlockService>();
        services.AddScoped<IOwnerAgentAuditService, OwnerAgentAuditService>();

        services.AddHttpClient<OwnerAgentClient>((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<OwnerAgentOptions>>().Value;
                if (options.TryGetBaseUri(out var baseUri))
                {
                    client.BaseAddress = baseUri;
                }

                // SSE streams live for hours; per-call deadlines are applied with
                // CancellationTokens inside OwnerAgentClient instead.
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.DefaultRequestHeaders.Clear();
            })
            // The header filter is part of the PRIMARY handler so it is always innermost:
            // whatever delegating handlers app-wide filters add around the typed client
            // (Sentry trace/baggage propagation, logging), the socket only ever sees the
            // headers OwnerAgentClient set.
            .ConfigurePrimaryHttpMessageHandler(() => new OwnerAgentOutboundHeaderFilter(new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                UseProxy = false,
                AutomaticDecompression = DecompressionMethods.None,
                // No trace-context headers to the sidecar: requests carry only what
                // OwnerAgentClient sets explicitly.
                ActivityHeadersPropagator = null,
                ConnectTimeout = TimeSpan.FromSeconds(5),
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            }));

        return services;
    }

    /// <summary>
    /// Per-account bucket for the long-polling console hub (see
    /// <see cref="OwnerAgentPolicies.HubRateLimit"/> for why <c>HubConnect</c> does not fit).
    /// </summary>
    public static RateLimiterOptions AddOwnerAgentHubRateLimit(this RateLimiterOptions options)
    {
        options.AddPolicy(OwnerAgentPolicies.HubRateLimit, httpContext =>
        {
            var key = OwnerAgentIdentity.GetAuthAccountId(httpContext.User)
                ?? httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "unknown";
            return RateLimitPartition.GetFixedWindowLimiter($"owner-agent-hub-{key}", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 600,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
        });
        return options;
    }
}
