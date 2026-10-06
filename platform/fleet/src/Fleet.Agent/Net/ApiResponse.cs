namespace Fleet.Agent;

/// <summary>How the agent classifies any API answer (protocol sections 2.4, 2.7 and Appendix A).</summary>
internal enum ApiKind
{
    Ok,
    NoContent,
    BadRequest,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    PayloadTooLarge,
    RangeNotSatisfiable,
    Unprocessable,
    ProtocolUnsupported,
    RateLimited,
    ServerError,
    NotImplemented,
    /// <summary>Framework 404/405 or a missing X-Remote-Protocol header: the API predates the feature (rollback skew, 5.6).</summary>
    RouteAbsent,
    OtherClientError,
    Network,
    Timeout,
    Canceled,
}

internal class ApiResponse
{
    public ApiKind Kind { get; init; }
    public int Status { get; init; }
    public string? Code { get; init; }
    public string? Reason { get; init; }
    public TimeSpan? RetryAfter { get; init; }
    public long? DesiredRevision { get; init; }
    public int[]? Supported { get; init; }

    /// <summary>Value of X-Remote-Reason on a 204 (section 4.1.3).</summary>
    public string? NoContentReason { get; init; }

    public bool Success => Kind is ApiKind.Ok or ApiKind.NoContent;

    /// <summary>Worth retrying with backoff: network, timeout, 429 and 5xx except the non-retryable applies_frozen.</summary>
    public bool Transient =>
        Kind is ApiKind.Network or ApiKind.Timeout or ApiKind.RateLimited
        || (Kind == ApiKind.ServerError && Code != "applies_frozen");

    public bool IsLeaseLost => Kind == ApiKind.Conflict && Code == "lease_lost";

    public static ApiResponse Failure(ApiKind kind, int status = 0, string? code = null) =>
        new() { Kind = kind, Status = status, Code = code };
}

internal sealed class ApiResponse<T> : ApiResponse
{
    public T? Value { get; init; }

    public static ApiResponse<T> From(ApiResponse head, T? value) => new()
    {
        Kind = head.Kind,
        Status = head.Status,
        Code = head.Code,
        Reason = head.Reason,
        RetryAfter = head.RetryAfter,
        DesiredRevision = head.DesiredRevision,
        Supported = head.Supported,
        NoContentReason = head.NoContentReason,
        Value = value,
    };

    public static new ApiResponse<T> Failure(ApiKind kind, int status = 0, string? code = null) =>
        new() { Kind = kind, Status = status, Code = code };
}

/// <summary>An opened input stream (200 or 206) or a classified failure. Dispose releases the HTTP response.</summary>
internal sealed class InputResponse : IDisposable
{
    private readonly IDisposable? _owner;

    public InputResponse(ApiResponse info, Stream? body, long? contentLength, string? contentSha256, string? etag, bool partial, long? rangeStart, IDisposable? owner)
    {
        Info = info;
        Body = body;
        ContentLength = contentLength;
        ContentSha256 = contentSha256;
        ETag = etag;
        Partial = partial;
        RangeStart = rangeStart;
        _owner = owner;
    }

    public ApiResponse Info { get; }
    public Stream? Body { get; }
    public long? ContentLength { get; }
    public string? ContentSha256 { get; }
    public string? ETag { get; }
    public bool Partial { get; }
    public long? RangeStart { get; }

    public static InputResponse Failed(ApiResponse info) => new(info, null, null, null, null, false, null, null);

    public void Dispose()
    {
        Body?.Dispose();
        _owner?.Dispose();
    }
}
