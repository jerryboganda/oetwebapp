namespace OetLearner.Api.Services.Rulebook;

internal static class AiProviderErrorMessages
{
    public static string HttpFailure(string providerName, int statusCode, string? reasonPhrase, string? detail = null)
    {
        var reason = string.IsNullOrWhiteSpace(reasonPhrase) ? "provider error" : reasonPhrase.Trim();
        var baseMessage = $"{providerName} call failed: HTTP {statusCode} {reason}.";
        return string.IsNullOrWhiteSpace(detail) ? baseMessage : $"{baseMessage} {detail.Trim()}";
    }

    public static string InvalidResponse(string providerName, string detail)
        => $"{providerName} returned an invalid response: {detail}.";
}
