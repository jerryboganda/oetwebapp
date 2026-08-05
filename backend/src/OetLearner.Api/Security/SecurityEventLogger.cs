using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Security;

/// <summary>
/// Writes <see cref="SecurityEvent"/> rows from its own short-lived DB scope
/// (via <see cref="IServiceScopeFactory"/>) rather than the caller's request-scoped
/// <c>LearnerDbContext</c>. This is deliberate: <see cref="AuthService"/> and
/// friends call <c>TryLogAsync</c> at points scattered throughout a method,
/// often before that method's own <c>SaveChangesAsync</c>. Sharing the tracked
/// context would either (a) force-flush unrelated pending changes early, or
/// (b) lose the event entirely if an exception is thrown before the caller's
/// own save runs. An isolated scope makes every log call atomic and
/// independent of the surrounding business transaction, matching the
/// "never throws, never interferes" contract on <see cref="ISecurityEventLogger"/>.
/// </summary>
public sealed class SecurityEventLogger(
    IServiceScopeFactory scopeFactory,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider timeProvider,
    ILogger<SecurityEventLogger> logger) : ISecurityEventLogger
{
    public async Task TryLogAsync(
        string? authAccountId,
        string kind,
        Guid? sessionFamilyId = null,
        string? deviceId = null,
        object? details = null,
        string? severity = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!SecurityEventKinds.All.Contains(kind))
            {
                // Programming error, not a runtime condition — keep logging (an
                // unrecognised kind is still useful forensic signal) but surface
                // it loudly so the typo gets fixed.
                logger.LogWarning("SecurityEventLogger: {Kind} is not in SecurityEventKinds.All", kind);
            }

            var (ip, userAgent, platform, country) = ResolveRequestContext(httpContextAccessor.HttpContext);

            // Intentionally CancellationToken.None: if the inbound request is
            // aborted, the security event we're recording (e.g. a sign-in that
            // already succeeded) should still be persisted.
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            db.SecurityEvents.Add(new SecurityEvent
            {
                Id = Guid.NewGuid(),
                OccurredAt = timeProvider.GetUtcNow(),
                AuthAccountId = authAccountId,
                Kind = kind,
                Severity = severity ?? SecurityEventKinds.DefaultSeverity(kind),
                IpAddress = ip,
                CountryCode = country,
                UserAgent = userAgent,
                Platform = platform,
                SessionFamilyId = sessionFamilyId,
                DeviceId = Truncate(deviceId, 128),
                DetailsJson = details is null ? null : JsonSerializer.Serialize(details),
            });
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SecurityEventLogger: failed to log {Kind}", kind);
        }
    }

    private static (string? Ip, string? UserAgent, string? Platform, string? Country) ResolveRequestContext(
        HttpContext? httpContext)
    {
        if (httpContext is null)
        {
            return (null, null, null, null);
        }

        // CF-Connecting-IP is Cloudflare's authoritative client IP (same trust
        // anchor IRegionDetector already uses for CF-IPCountry); RemoteIpAddress
        // is the fallback once ForwardedHeadersMiddleware has applied the
        // configured trusted-proxy allowlist (see Program.cs Proxy:KnownNetworks).
        var ip = httpContext.Request.Headers["CF-Connecting-IP"].ToString();
        if (string.IsNullOrWhiteSpace(ip))
        {
            ip = httpContext.Connection.RemoteIpAddress?.ToString();
        }

        var userAgent = httpContext.Request.Headers.UserAgent.ToString();
        var platform = httpContext.Request.Headers["X-OET-Client-Platform"].ToString();
        var country = httpContext.Request.Headers["CF-IPCountry"].ToString();

        return (
            Truncate(ip, 64),
            Truncate(string.IsNullOrWhiteSpace(userAgent) ? null : userAgent, 256),
            Truncate(string.IsNullOrWhiteSpace(platform) ? null : platform, 32),
            Truncate(string.IsNullOrWhiteSpace(country) ? null : country, 8));
    }

    private static string? Truncate(string? value, int maxLength)
        => value is null ? null : (value.Length > maxLength ? value[..maxLength] : value);
}
