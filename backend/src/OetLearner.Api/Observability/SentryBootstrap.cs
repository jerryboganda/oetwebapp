using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sentry;
using Sentry.AspNetCore;
using Sentry.Extensibility;

namespace OetLearner.Api.Observability;

/// <summary>
/// Single entry point for wiring Sentry into the API host.
///
/// <para>
/// Env-gated: if <c>Sentry:Dsn</c> is empty / missing the SDK is not wired at all,
/// making local dev and the integration test suite completely free of network traffic
/// and stray spans. Do NOT change this to always-on - tests rely on no Sentry
/// scheduling hooks interfering with the WebApplicationFactory lifecycle.
/// </para>
///
/// <para>
/// Privacy contract (enforced here, not in config so it cannot be accidentally flipped
/// via appsettings):
///   - <see cref="SentryOptions.SendDefaultPii"/> is pinned to <c>false</c>.
///   - <see cref="SentryOptions.MaxRequestBodySize"/> is pinned to <see cref="RequestSize.None"/>
///     so request bodies (which can contain credentials / candidate answers / AI
///     transcripts) are never attached to events.
///   - <see cref="ScrubPii"/> runs as the only <see cref="SentryOptions.SetBeforeSend(Func{SentryEvent, SentryHint, SentryEvent})"/>
///     hook and removes email addresses, IP addresses, and the
///     <c>Authorization</c> / <c>Cookie</c> / <c>X-CSRF</c> headers from every event
///     before it leaves the process.
/// </para>
/// </summary>
public static class SentryBootstrap
{
    /// <summary>Config key that gates wiring. Empty/missing = Sentry disabled.</summary>
    public const string DsnConfigKey = "Sentry:Dsn";

    /// <summary>
    /// Wire Sentry into the web host if (and only if) a DSN is configured.
    /// Safe to call unconditionally; no-ops when disabled.
    /// </summary>
    public static WebApplicationBuilder AddSentryIfConfigured(this WebApplicationBuilder builder)
    {
        var dsn = builder.Configuration[DsnConfigKey];
        if (string.IsNullOrWhiteSpace(dsn))
        {
            return builder;
        }

        builder.WebHost.UseSentry(options =>
        {
            options.Dsn = dsn;
            options.Environment = builder.Configuration["Sentry:Environment"]
                ?? builder.Environment.EnvironmentName;
            options.Release = builder.Configuration["Sentry:Release"];

            // Traces + profiling: opt-in via config, defaults conservative so a fresh
            // DSN does not immediately eat the Sentry quota. 0.0 = off, 1.0 = every request.
            options.TracesSampleRate = ParseSampleRate(builder.Configuration["Sentry:TracesSampleRate"], 0.0);
            options.ProfilesSampleRate = ParseSampleRate(builder.Configuration["Sentry:ProfilesSampleRate"], 0.0);

            // Privacy pins - do NOT expose these via config.
            options.SendDefaultPii = false;
            options.MaxRequestBodySize = RequestSize.None;
            options.AttachStacktrace = true;

            options.SetBeforeSend((evt, _) => ScrubPii(evt));
        });

        return builder;
    }

    /// <summary>
    /// Remove obvious PII from a Sentry event before it is sent. Exposed as internal
    /// Remove obvious PII from a Sentry event before it is sent. Public so unit tests
    /// can assert the privacy contract without reflection - see SentryBootstrapTests.
    /// </summary>
    public static SentryEvent? ScrubPii(SentryEvent evt)
    {
        // User: drop email + IP + username; keep the opaque user id because that is
        // the only thing that makes grouping useful on the Sentry side.
        if (evt.User is { } user)
        {
            user.Email = null;
            user.IpAddress = null;
            user.Username = null;
        }

        // Request: drop auth-ish headers and cookies. Also drop the query string because
        // we occasionally put SSO state in there during auth flows.
        if (evt.Request is { } req)
        {
            req.Cookies = null;
            req.QueryString = null;

            if (req.Headers is { Count: > 0 })
            {
                foreach (var key in SensitiveHeaderNames)
                {
                    // Case-insensitive removal: match any header whose name equals key
                    // ignoring case, then remove by its actual stored key.
                    string? matched = null;
                    foreach (var existing in req.Headers.Keys)
                    {
                        if (string.Equals(existing, key, StringComparison.OrdinalIgnoreCase))
                        {
                            matched = existing;
                            break;
                        }
                    }
                    if (matched is not null)
                    {
                        req.Headers.Remove(matched);
                    }
                }
            }
        }

        return evt;
    }

    private static readonly string[] SensitiveHeaderNames =
    {
        "Authorization",
        "Cookie",
        "Set-Cookie",
        "X-CSRF",
        "X-CSRF-Token",
        "X-XSRF-Token",
        "X-Api-Key",
        "X-Forwarded-For",
        "X-Real-IP",
    };

    private static double ParseSampleRate(string? raw, double fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        if (!double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            return fallback;
        }
        if (value < 0) return 0.0;
        if (value > 1) return 1.0;
        return value;
    }
}
