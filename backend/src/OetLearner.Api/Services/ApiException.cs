namespace OetLearner.Api.Services;

public sealed record ApiFieldError(string Field, string Code, string Message);

public sealed class ApiException : Exception
{
    public int StatusCode { get; }
    public string ErrorCode { get; }
    public IReadOnlyList<ApiFieldError> FieldErrors { get; }
    public bool Retryable { get; }
    public string? SupportHint { get; }

    private ApiException(
        int statusCode,
        string errorCode,
        string message,
        IEnumerable<ApiFieldError>? fieldErrors = null,
        bool retryable = false,
        string? supportHint = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        FieldErrors = fieldErrors?.ToArray() ?? [];
        Retryable = retryable;
        SupportHint = supportHint;
    }

    public static ApiException Validation(string errorCode, string message, IEnumerable<ApiFieldError>? fieldErrors = null)
        => new(StatusCodes.Status400BadRequest, errorCode, message, fieldErrors);

    public static ApiException Conflict(string errorCode, string message, IEnumerable<ApiFieldError>? fieldErrors = null)
        => new(StatusCodes.Status409Conflict, errorCode, message, fieldErrors);

    public static ApiException NotFound(string errorCode, string message)
        => new(StatusCodes.Status404NotFound, errorCode, message);

    public static ApiException Forbidden(string errorCode, string message)
        => new(StatusCodes.Status403Forbidden, errorCode, message);
}
