using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.Ai;

public enum AiRetryDisposition
{
    Retry = 0,
    Indeterminate = 1,
    Quarantine = 2,
    Terminal = 3,
}

public sealed record AiRetryClassification(
    AiRetryDisposition Disposition,
    int MaxRetries,
    bool AllowLocalRepair,
    TimeSpan? SuggestedDelay = null);

/// <summary>
/// Pure classification of provider-call failures. No I/O, no DB. W3 of the
/// AI cost/reliability remediation (incident INC-2026-CLAUDE-01).
/// </summary>
public static class AiRetryPolicy
{
    public const int MaxProviderRetries = 3;
    public const int MaxSchemaRepairRetries = 1;

    private static readonly Regex HttpStatusRegex = new(
        @"\bHTTP\s+(\d{3})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static AiRetryClassification Classify(Exception exception, bool requestLikelySent)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (IsSchemaInvalid(exception))
        {
            return new AiRetryClassification(
                AiRetryDisposition.Retry,
                MaxSchemaRepairRetries,
                AllowLocalRepair: true);
        }

        var status = TryGetHttpStatus(exception);
        if (status is 401 or 403 or 402 || IsInvalidCredentialOrModel(exception))
        {
            return new AiRetryClassification(AiRetryDisposition.Quarantine, 0, false);
        }

        if (status is 429 || (status is >= 500 and <= 599))
        {
            return new AiRetryClassification(
                AiRetryDisposition.Retry,
                MaxProviderRetries,
                false,
                SuggestedDelay: ComputeRetryDelay(attemptNumber: 1, retryAfter: TryGetRetryAfter(exception)));
        }

        if (!requestLikelySent && IsPreSendConnectionFailure(exception, status))
        {
            return new AiRetryClassification(AiRetryDisposition.Retry, MaxProviderRetries, false);
        }

        if (requestLikelySent && IsTimeout(exception))
        {
            return new AiRetryClassification(AiRetryDisposition.Indeterminate, 0, false);
        }

        return new AiRetryClassification(AiRetryDisposition.Terminal, 0, false);
    }

    /// <summary>
    /// Honour a provider Retry-After when present; otherwise exponential
    /// backoff with deterministic jitter derived from the attempt number so
    /// tests do not flake and concurrent callers do not thundering-herd on
    /// the same millisecond.
    /// </summary>
    public static TimeSpan ComputeRetryDelay(int attemptNumber, TimeSpan? retryAfter, double jitterFactor = 0.25)
    {
        var attempt = Math.Clamp(attemptNumber, 1, MaxProviderRetries);
        jitterFactor = Math.Clamp(jitterFactor, 0, 1);

        if (retryAfter is { } ra && ra > TimeSpan.Zero)
        {
            var jitterMs = ra.TotalMilliseconds * jitterFactor * ((attempt % 5) / 5d);
            return ra + TimeSpan.FromMilliseconds(jitterMs);
        }

        var baseMs = 200d * Math.Pow(2, attempt - 1);
        var jitter = baseMs * jitterFactor * ((attempt % 5) / 5d);
        return TimeSpan.FromMilliseconds(baseMs + jitter);
    }

    private static bool IsSchemaInvalid(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is JsonException) return true;
            var message = current.Message ?? string.Empty;
            if (message.Contains("schema_invalid", StringComparison.OrdinalIgnoreCase)
                || message.Contains("invalid_schema", StringComparison.OrdinalIgnoreCase)
                || message.Contains("schema-invalid", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInvalidCredentialOrModel(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var message = current.Message ?? string.Empty;
            if (message.Contains("invalid_model", StringComparison.OrdinalIgnoreCase)
                || message.Contains("invalid_config", StringComparison.OrdinalIgnoreCase)
                || message.Contains("invalid model", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPreSendConnectionFailure(Exception exception, int? status)
    {
        if (status is not null) return false;

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SocketException or HttpRequestException or IOException)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTimeout(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is TimeoutException) return true;
            if (current is TaskCanceledException or OperationCanceledException)
            {
                return true;
            }
        }

        return false;
    }

    private static int? TryGetHttpStatus(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException http && http.StatusCode is { } code)
            {
                return (int)code;
            }

            var match = HttpStatusRegex.Match(current.Message ?? string.Empty);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static TimeSpan? TryGetRetryAfter(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Data["Retry-After"] is TimeSpan ts) return ts;
            if (current.Data["Retry-After"] is int seconds) return TimeSpan.FromSeconds(seconds);
            if (current.Data["Retry-After"] is string text
                && int.TryParse(text, out var parsed))
            {
                return TimeSpan.FromSeconds(parsed);
            }
        }

        return null;
    }
}
