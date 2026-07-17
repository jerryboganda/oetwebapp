using OetWithDrHesham.Api.Services.Settings;

namespace OetWithDrHesham.Api.Endpoints;

/// <summary>
/// Wave 5 (frontend runtime-config) — a SECRET-FREE public projection of the
/// effective runtime settings so the browser no longer has to depend on
/// build-time <c>NEXT_PUBLIC_*</c> values baked into the bundle. An admin can
/// drive these boot values from the DB (RuntimeSettingsRow) and the frontend
/// picks them up at runtime via <c>GET /v1/public/runtime-config</c>.
///
/// <para>
/// SECURITY CONTRACT: this endpoint is ANONYMOUS (no auth) and is therefore
/// allowed to expose <b>only</b> genuinely public boot values:
/// <list type="bullet">
///   <item>Sentry DSN — a publishable client ingest key (NOT a secret).</item>
///   <item>Soketi AppKey — the public Pusher-protocol key (NOT AppSecret).</item>
///   <item>VAPID public key — the web-push public key (NEVER the private key).</item>
///   <item>Platform public web/api base URLs.</item>
/// </list>
/// It MUST NEVER include any <c>*Secret</c> / <c>*Encrypted</c> / private-key /
/// API-key / token / password material. <see cref="PublicRuntimeConfigEndpointTests"/>
/// asserts this both by field name and by value.
/// </para>
/// </summary>
public static class PublicRuntimeConfigEndpoints
{
    /// <summary>
    /// Wire <c>GET /runtime-config</c> onto the supplied <b>public</b> route
    /// group (the anonymous <c>/v1/public</c> group built in
    /// <see cref="LearnerEndpoints.MapLearnerEndpoints"/>). No
    /// <c>RequireAuthorization</c> is attached so the endpoint is reachable
    /// before the user signs in (first paint).
    /// </summary>
    public static IEndpointRouteBuilder MapPublicRuntimeConfig(this IEndpointRouteBuilder publicGroup)
    {
        publicGroup.MapGet("/runtime-config", async (
            IRuntimeSettingsProvider runtimeSettings,
            CancellationToken ct) =>
        {
            var settings = await runtimeSettings.GetAsync(ct);
            return Results.Ok(BuildPublicConfig(settings));
        });

        return publicGroup;
    }

    /// <summary>
    /// Build the secret-free public projection. Only non-secret, browser-safe
    /// boot values are included. Keep this in sync with the values the frontend
    /// reads from <c>NEXT_PUBLIC_*</c> (lib/runtime-config.ts).
    /// </summary>
    internal static object BuildPublicConfig(EffectiveSettings settings)
        => new
        {
            // Publishable client ingest key — safe to expose. Performance
            // sample rate is a plain number.
            sentry = new
            {
                dsn = settings.Sentry.Dsn,
                environment = settings.Sentry.Environment,
                sampleRate = settings.Sentry.SampleRate,
            },
            // AppKey is the public Pusher-protocol key; AppSecret / AppId are
            // intentionally excluded (server-side dispatch only).
            soketi = new
            {
                host = settings.Soketi.Host,
                port = settings.Soketi.Port,
                appKey = settings.Soketi.AppKey,
                useTls = settings.Soketi.UseTls,
                enabled = settings.Soketi.Enabled,
            },
            // Public VAPID key + subject only. The private key never leaves the host.
            webPush = new
            {
                vapidPublicKey = settings.Push.VapidPublicKey,
                vapidSubject = settings.Push.VapidSubject,
                enabled = settings.Push.WebPushEnabled,
            },
            platform = new
            {
                publicWebBaseUrl = settings.Platform.PublicWebBaseUrl,
                publicApiBaseUrl = settings.Platform.PublicApiBaseUrl,
            },
        };
}
