namespace OetLearner.Api.Services.Rulebook;

internal static class AiProviderErrorMessages
{
    public static string HttpFailure(string providerName, int statusCode, string? reasonPhrase)
    {
        var reason = string.IsNullOrWhiteSpace(reasonPhrase) ? "provider error" : reasonPhrase.Trim();
        return $"{providerName} call failed: HTTP {statusCode} {reason}.";
    }

    public static string InvalidResponse(string providerName, string detail)
        => $"{providerName} returned an invalid response: {detail}.";
}
