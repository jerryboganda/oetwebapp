namespace OetLearner.Api.Configuration;

/// <summary>
/// Owner Agent Console wiring (agent-console/CONTRACT.md §3/§5). Bound from the
/// <c>OwnerAgent</c> configuration section — in production that is the
/// <c>OwnerAgent__*</c> environment variables on the API slots ONLY.
///
/// <para>
/// These values are deliberately <b>not</b> part of Runtime Settings and have no
/// database override: the owner allow-list and the sidecar control token are trust
/// anchors, and anything an admin can edit from <c>/admin/settings</c> could be used
/// by a non-owner admin to add themselves to the list. Keep them out of
/// <c>appsettings*.json</c> as well so the only way to change them is a redeploy
/// with a new environment.
/// </para>
/// </summary>
public sealed class OwnerAgentOptions
{
    public const string SectionName = "OwnerAgent";

    /// <summary>Minimum length of <see cref="InternalToken"/>; mirrors the sidecar's own start-up refusal.</summary>
    public const int MinimumInternalTokenLength = 32;

    /// <summary>Master environment switch. When false every console route answers 503.</summary>
    public bool Enabled { get; set; }

    /// <summary>Sidecar control server base URL (internal network only).</summary>
    public string? BaseUrl { get; set; } = "http://oet-agent-console:8410";

    /// <summary>Shared secret sent as <c>X-Oet-Internal-Token</c>. Never logged, never returned.</summary>
    public string? InternalToken { get; set; }

    /// <summary>Comma (or semicolon / whitespace) separated <c>auth_account_id</c> allow-list.</summary>
    public string? OwnerAccountIds { get; set; }

    // Single reference so concurrent readers never see a torn (source, ids) pair.
    private ParsedOwnerIds? _parsed;

    /// <summary>The parsed owner allow-list (ordinal, exact ids).</summary>
    public IReadOnlySet<string> OwnerAccountIdSet
    {
        get
        {
            var source = OwnerAccountIds ?? string.Empty;
            var cached = Volatile.Read(ref _parsed);
            if (cached is not null && string.Equals(cached.Source, source, StringComparison.Ordinal))
            {
                return cached.Ids;
            }

            var fresh = new ParsedOwnerIds(source, ParseOwnerAccountIds(source));
            Volatile.Write(ref _parsed, fresh);
            return fresh.Ids;
        }
    }

    private sealed record ParsedOwnerIds(string Source, IReadOnlySet<string> Ids);

    /// <summary>True when <paramref name="authAccountId"/> is on the env-only owner allow-list.</summary>
    public bool IsOwnerAccount(string? authAccountId)
        => !string.IsNullOrWhiteSpace(authAccountId) && OwnerAccountIdSet.Contains(authAccountId.Trim());

    /// <summary>True when the sidecar can actually be called: enabled, absolute http(s) URL and a long enough token.</summary>
    public bool IsSidecarConfigured
        => Enabled
           && TryGetBaseUri(out _)
           && !string.IsNullOrEmpty(InternalToken)
           && InternalToken.Length >= MinimumInternalTokenLength;

    /// <summary>
    /// Parses <see cref="BaseUrl"/> into an absolute http(s) URI without user-info,
    /// normalised to end with <c>/</c> so relative sidecar paths resolve under it.
    /// </summary>
    public bool TryGetBaseUri(out Uri baseUri)
    {
        baseUri = null!;
        if (string.IsNullOrWhiteSpace(BaseUrl)
            || !Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(parsed.UserInfo)
            || !string.IsNullOrEmpty(parsed.Query)
            || !string.IsNullOrEmpty(parsed.Fragment))
        {
            return false;
        }

        var text = parsed.GetLeftPart(UriPartial.Path);
        baseUri = new Uri(text.EndsWith('/') ? text : text + "/", UriKind.Absolute);
        return true;
    }

    public static IReadOnlySet<string> ParseOwnerAccountIds(string? raw)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return set;
        }

        foreach (var part in raw.Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Length is > 0 and <= 64)
            {
                set.Add(part);
            }
        }

        return set;
    }
}
