using OetWithDrHesham.Api.Services;

namespace OetWithDrHesham.Api.Middleware;

/// <summary>
/// Server-side enforcement of the forced-update policy. When an app shell
/// (desktop/mobile) sends <c>X-Client-Platform</c> + <c>X-App-Version</c> and
/// its version is below the configured minimum for that platform — or the
/// platform's ForceUpdate flag is set — the request is rejected with
/// <c>426 Upgrade Required</c> so an out-of-date shell literally cannot use the
/// API.
///
/// Safety design (a misconfiguration must never brick the website or lock
/// everyone out):
///  * Inert unless the admin flips <c>EnforceClientVersionGate</c> ON (the
///    service returns not-blocked otherwise).
///  * Only shell clients are considered — a request without BOTH headers
///    (every plain web browser and every server-to-server call) is passed
///    through untouched.
///  * A hard-coded allowlist keeps the version-check, public runtime-config,
///    health, and auth endpoints reachable, so a blocked client can still learn
///    it must update and can still authenticate.
///  * Any exception while evaluating fails OPEN (request proceeds).
/// </summary>
public static class ClientVersionGateMiddleware
{
    private const string PlatformHeader = "X-Client-Platform";
    private const string VersionHeader = "X-App-Version";

    private static readonly string[] ExemptPrefixes =
    {
        "/v1/app-release",
        "/v1/public/runtime-config",
        "/v1/auth",
    };

    public static IApplicationBuilder UseClientVersionGate(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var request = context.Request;

            // Only shell clients carry both headers; browsers/website never do.
            var platform = request.Headers[PlatformHeader].FirstOrDefault();
            var version = request.Headers[VersionHeader].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(platform) || string.IsNullOrWhiteSpace(version))
            {
                await next();
                return;
            }

            // Never gate the website even if a stray header appears.
            if (string.Equals(platform.Trim(), "web", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            // Only guard versioned API traffic; skip preflight and exempt routes.
            if (HttpMethods.IsOptions(request.Method)
                || !request.Path.StartsWithSegments("/v1", StringComparison.OrdinalIgnoreCase)
                || IsExempt(request.Path))
            {
                await next();
                return;
            }

            try
            {
                var service = context.RequestServices.GetRequiredService<ILaunchReadinessService>();
                var decision = await service.EvaluateClientAsync(platform, version, context.RequestAborted);
                if (decision.Blocked)
                {
                    context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        code = "UPGRADE_REQUIRED",
                        minVersion = decision.MinVersion,
                        storeUrl = decision.StoreUrl,
                        updateFeedUrl = decision.UpdateFeedUrl,
                    }, context.RequestAborted);
                    return;
                }
            }
            catch
            {
                // Fail open: a DB/service hiccup must never lock clients out.
            }

            await next();
        });
    }

    private static bool IsExempt(PathString path)
    {
        foreach (var prefix in ExemptPrefixes)
        {
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
