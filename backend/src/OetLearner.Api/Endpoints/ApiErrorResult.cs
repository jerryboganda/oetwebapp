using System.Text.Json.Serialization;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Error response for endpoints that return (rather than throw) an error. Writes the SAME
/// body, content type and serializer as the central <c>ApiException</c> branch of
/// <c>UseExceptionHandler</c> in Program.cs:
/// <c>{ code, message, fieldErrors, retryable, supportHint, correlationId }</c>.
/// Where an endpoint can simply throw, prefer <c>throw ApiException.X(code, message)</c>.
/// This carries no extra fields: an error body that also returns data (replacementRoute,
/// knownProfessions, valid, issues...) stays on <c>Results.Json(new { code, message, data })</c>.
/// </summary>
/// <param name="Retryable">Null = derive from status (408/429/5xx), which is exactly what
/// lib/api/client.ts assumed for the legacy bodies that carried no <c>retryable</c>.</param>
/// <param name="LegacyErrorAlias">Also emit <c>error</c> = message. Only for endpoints whose
/// frontend still reads <c>detail.error</c>; remove once that frontend change ships.</param>
public sealed record ApiErrorResult(
    int StatusCode,
    string Code,
    string Message,
    bool? Retryable = null,
    IReadOnlyList<ApiFieldError>? FieldErrors = null,
    string? SupportHint = null,
    bool LegacyErrorAlias = false) : IResult, IStatusCodeHttpResult
{
    /// <summary>Caught exception behind this error. Logged (never echoed) when it is a 5xx or
    /// was not thrown by this assembly; its text never reaches the response body.</summary>
    public Exception? Exception { get; init; }

    int? IStatusCodeHttpResult.StatusCode => StatusCode;

    /// <summary><paramref name="ex"/>.Message when this assembly threw it (a crafted domain
    /// message), otherwise <paramref name="fallback"/> (framework/EF/provider text).</summary>
    public static string SafeMessage(Exception ex, string fallback)
        => IsOwn(ex) ? ex.Message : fallback;

    public Task ExecuteAsync(HttpContext httpContext)
    {
        var correlationId = httpContext.Items.TryGetValue("CorrelationId", out var cid) ? cid as string : null;

        if (Exception is { } ex && (StatusCode >= 500 || !IsOwn(ex)))
        {
            httpContext.RequestServices?.GetService<ILoggerFactory>()?.CreateLogger<ApiErrorResult>().Log(
                StatusCode >= 500 ? LogLevel.Error : LogLevel.Warning,
                ex,
                "{Method} {Path} returned {Code} ({Status}). CorrelationId: {CorrelationId}",
                httpContext.Request.Method,
                httpContext.Request.Path,
                Code,
                StatusCode,
                correlationId ?? "missing");
        }

        httpContext.Response.StatusCode = StatusCode;
        httpContext.Response.ContentType = "application/problem+json";
        return httpContext.Response.WriteAsync(JsonSupport.Serialize(new Body(
            Code,
            Message,
            FieldErrors ?? [],
            Retryable ?? StatusCode is 408 or 429 or >= 500,
            SupportHint,
            correlationId,
            LegacyErrorAlias ? Message : null)));
    }

    private static bool IsOwn(Exception ex)
        => ex.TargetSite?.DeclaringType?.Assembly == typeof(ApiErrorResult).Assembly;

    private sealed record Body(
        string Code,
        string Message,
        IReadOnlyList<ApiFieldError> FieldErrors,
        bool Retryable,
        string? SupportHint,
        string? CorrelationId,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Error);
}
