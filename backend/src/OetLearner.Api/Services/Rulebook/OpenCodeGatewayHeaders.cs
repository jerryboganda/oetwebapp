using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// Identity headers the OpenCode inference gateway expects on every request: an explicit
/// <c>User-Agent</c> and a stable per-conversation <c>x-opencode-session</c> (the Go plan
/// requires it, so it is always sent). Shared by the runtime adapter and the admin probe so the
/// probe predicts runtime behaviour. Never applied to any other host.
/// <para>
/// The session value is a pseudonym: <c>oet-</c> plus the first 24 lowercase hex characters of
/// the SHA-256 of the raw conversation id, so the gateway never sees a thread id. With no
/// conversation id it is <c>oet-</c> plus 24 hex characters of a per-process random GUID.
/// </para>
/// </summary>
public static class OpenCodeGatewayHeaders
{
    public const string SessionHeaderName = "x-opencode-session";

    private const string SessionPrefix = "oet-";
    private const int SessionHexChars = 24;

    private static readonly string ProcessSession =
        SessionPrefix + Guid.NewGuid().ToString("N")[..SessionHexChars];

    private static readonly string UserAgentValue =
        "OET-Platform/" + (typeof(OpenCodeGatewayHeaders).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");

    /// <summary>Sets <c>User-Agent</c> and <c>x-opencode-session</c> (replacing any existing value).</summary>
    public static void Apply(HttpRequestHeaders headers, string? sessionKey)
    {
        headers.Remove("User-Agent");
        headers.TryAddWithoutValidation("User-Agent", UserAgentValue);
        headers.Remove(SessionHeaderName);
        headers.TryAddWithoutValidation(SessionHeaderName, SessionValue(sessionKey));
    }

    private static string SessionValue(string? sessionKey)
    {
        if (string.IsNullOrWhiteSpace(sessionKey)) return ProcessSession;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sessionKey));
        return SessionPrefix + Convert.ToHexStringLower(hash)[..SessionHexChars];
    }
}
