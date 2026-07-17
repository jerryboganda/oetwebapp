using OetWithDrHesham.Api.Services;

namespace OetWithDrHesham.Api.Services.Conversation.Tts;

public static class ElevenLabsApiEndpoint
{
    public const string DefaultBaseUrl = "https://api.elevenlabs.io/v1";

    public static string NormalizeBaseUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return DefaultBaseUrl;

        var trimmed = value.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            throw ApiException.Validation("elevenlabs_tts_base_url_invalid", "ElevenLabs API base URL must be the official HTTPS ElevenLabs v1 API URL.");
        }

        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort ||
            !string.Equals(uri.IdnHost, "api.elevenlabs.io", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation("elevenlabs_tts_base_url_invalid", "ElevenLabs API base URL must use https://api.elevenlabs.io/v1.");
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.Length == 0) path = "/v1";
        if (!string.Equals(path, "/v1", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation("elevenlabs_tts_base_url_invalid", "ElevenLabs API base URL must use the /v1 API path.");
        }

        return DefaultBaseUrl;
    }
}