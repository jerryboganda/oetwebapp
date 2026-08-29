namespace OetLearner.Api.Services.Ai;

/// <summary>
/// Maps provider failures onto a redacted ErrorClass. Never persist raw
/// provider bodies, credentials, or candidate text on <c>AiUsageRecord.ErrorMessage</c>.
/// </summary>
public static class AiProviderErrorSanitizer
{
    public static string Sanitize(int? statusCode, string? errorClass = null)
    {
        var cls = string.IsNullOrWhiteSpace(errorClass) ? "provider_error" : errorClass.Trim();
        return statusCode is int code ? $"{cls}:http_{code}" : cls;
    }
}
