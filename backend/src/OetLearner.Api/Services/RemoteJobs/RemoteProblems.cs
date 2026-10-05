using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace OetLearner.Api.Services.RemoteJobs;

/// <summary>JSON options shared by every remote-worker route (requests are strict, responses camelCase).</summary>
public static class RemoteJson
{
    /// <summary>Request parsing: camelCase, case-insensitive, strict numbers (a string where a number belongs is a 400).</summary>
    public static readonly JsonSerializerOptions Request = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.Strict,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
    };

    /// <summary>Response writing: camelCase, nulls written (the envelope documents <c>reason: null</c>).</summary>
    public static readonly JsonSerializerOptions Response = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
}

/// <summary>
/// The repository error envelope with the protocol's two optional additions (<c>reason</c>,
/// <c>retryAfterSeconds</c>) as an <see cref="IResult"/>, so authentication challenges, endpoint
/// filters and handlers all emit the identical shape (OET-RWP/1 sections 2.5 and Appendix A).
/// Messages never carry content, paths or another node's identifiers.
/// </summary>
public sealed class RemoteProblemResult(
    int statusCode,
    string code,
    string message,
    bool retryable = false,
    string? reason = null,
    int? retryAfterSeconds = null,
    IReadOnlyDictionary<string, object?>? extra = null) : IResult
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public string? Reason { get; } = reason;

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var response = httpContext.Response;
        if (response.HasStarted) return;

        response.StatusCode = StatusCode;
        response.ContentType = "application/problem+json; charset=utf-8";
        if (retryAfterSeconds is int retryAfter)
        {
            response.Headers.RetryAfter = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var body = new Dictionary<string, object?>
        {
            ["code"] = Code,
            ["message"] = message,
            ["retryable"] = retryable,
            ["reason"] = Reason,
            ["retryAfterSeconds"] = retryAfterSeconds,
            ["correlationId"] = httpContext.Items.TryGetValue("CorrelationId", out var correlationId)
                ? correlationId as string
                : null,
        };

        if (extra is not null)
        {
            foreach (var (key, value) in extra) body[key] = value;
        }

        await JsonSerializer.SerializeAsync(response.Body, body, RemoteJson.Response, httpContext.RequestAborted);
    }
}

/// <summary>Factories for every status the job and service planes answer with (Appendix A).</summary>
public static class RemoteProblems
{
    public static RemoteProblemResult BadRequest(string message)
        => new(StatusCodes.Status400BadRequest, "bad_request", message);

    public static RemoteProblemResult Unauthorized()
        => new(StatusCodes.Status401Unauthorized, "unauthorized", "Authentication failed.");

    public static RemoteProblemResult Forbidden(string code, string message)
        => new(StatusCodes.Status403Forbidden, code, message);

    public static RemoteProblemResult NotFound(string code, string message)
        => new(StatusCodes.Status404NotFound, code, message);

    public static RemoteProblemResult JobNotFound()
        => NotFound("job_not_found", "The job was not found.");

    public static RemoteProblemResult NodeNotFound()
        => NotFound("node_not_found", "The node was not found.");

    public static RemoteProblemResult Conflict(string code, string message, string? reason = null)
        => new(StatusCodes.Status409Conflict, code, message, reason: reason);

    public static RemoteProblemResult LeaseLost(string reason)
        => Conflict("lease_lost", "The lease for this job is no longer held by this node.", reason);

    public static RemoteProblemResult PayloadTooLarge(string code, string message)
        => new(StatusCodes.Status413PayloadTooLarge, code, message);

    public static RemoteProblemResult RangeNotSatisfiable(long size)
        => new(
            StatusCodes.Status416RangeNotSatisfiable,
            "range_not_satisfiable",
            "The requested range cannot be satisfied.",
            extra: new Dictionary<string, object?> { ["size"] = size });

    public static RemoteProblemResult Unprocessable(string code, string message)
        => new(StatusCodes.Status422UnprocessableEntity, code, message);

    public static RemoteProblemResult UpgradeRequired(IReadOnlyList<int> supported)
        => new(
            StatusCodes.Status426UpgradeRequired,
            "protocol_unsupported",
            "The protocol version is not supported.",
            extra: new Dictionary<string, object?> { ["supported"] = supported });

    public static RemoteProblemResult RateLimited(int retryAfterSeconds)
        => new(
            StatusCodes.Status429TooManyRequests,
            "rate_limited",
            "Too many requests.",
            retryable: true,
            retryAfterSeconds: retryAfterSeconds);

    public static RemoteProblemResult ServiceUnavailable(string code, string message, bool retryable = true, int? retryAfterSeconds = 5)
        => new(StatusCodes.Status503ServiceUnavailable, code, message, retryable, retryAfterSeconds: retryAfterSeconds);

    public static RemoteProblemResult InternalError()
        => new(
            StatusCodes.Status500InternalServerError,
            "internal_error",
            "An unexpected error occurred.",
            retryable: true);

    public static RemoteProblemResult NotImplemented(string message)
        => new(StatusCodes.Status501NotImplemented, "not_implemented", message);
}
