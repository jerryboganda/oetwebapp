namespace OetLearner.Api.Services.AiTools;

/// <summary>
/// Phase 5 — bound to the <c>AiTool</c> options section in
/// <c>appsettings.json</c>. Defaults are conservative; production overrides
/// come from <c>AiTool__*</c> environment variables.
/// </summary>
public sealed class AiToolOptions
{
    /// <summary>Hard cap on the number of tool calls accepted within a
    /// single completion. Beyond this the gateway breaks the loop and
    /// returns whatever text it has.</summary>
    public int MaxToolCallsPerCompletion { get; set; } = 4;

    /// <summary>How long the per-feature grant lookup is cached
    /// in-memory. Mutations from the admin endpoints invalidate
    /// proactively, so this is just a cap on staleness when the cache is
    /// warmed via <c>IMemoryCache</c>.</summary>
    public int FeatureGrantCacheSeconds { get; set; } = 30;

    /// <summary>External-host allowlist for <c>ExternalNetwork</c> tools.
    /// Hosts only; no scheme, no path. Lookups are case-insensitive and
    /// host-suffix is NOT permitted (must be exact host).</summary>
    public string[] AllowedExternalHosts { get; set; } = new[]
    {
        "api.dictionaryapi.dev",
    };

    /// <summary>Per-user daily call budget for <c>ExternalNetwork</c>
    /// tools (across all such tools, all features). 0 = disabled.</summary>
    public int ExternalNetworkPerUserDailyCalls { get; set; } = 200;

    /// <summary>Per-call timeout for external-network tool HTTP requests.</summary>
    public int ExternalNetworkTimeoutMilliseconds { get; set; } = 4000;

    /// <summary>Max bytes of HTTP response body the external-network
    /// tools will read before giving up. Defends against unbounded
    /// downloads.</summary>
    public int ExternalNetworkMaxResponseBytes { get; set; } = 64 * 1024;
}
