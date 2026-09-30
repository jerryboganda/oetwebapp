using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// Provider-agnostic failure class derived from a provider HTTP error. Stable
/// codes (<see cref="AiProviderErrorClassExtensions.ToCode"/>) feed the gateway
/// usage rows, the circuit breaker and the Speaking grade / live-voice failover
/// decisions, so the wire text of any single vendor never leaks into them.
/// </summary>
public enum AiProviderErrorClass
{
    Unknown = 0,
    /// <summary>Billing / credit balance / spend cap exhausted. Retrying cannot help until an operator acts.</summary>
    QuotaExhausted,
    /// <summary>Transient per-minute style limit. Retrying after a pause can help.</summary>
    RateLimited,
    /// <summary>The request itself was rejected (bad parameter, unknown model, oversized).</summary>
    InvalidRequest,
    /// <summary>Credential missing, invalid, revoked or not permitted.</summary>
    Auth,
    /// <summary>Provider capacity (HTTP 503 / 529).</summary>
    Overloaded,
    /// <summary>Provider-side failure (HTTP 5xx, timeout status).</summary>
    ServerError,
    /// <summary>No usable HTTP answer (DNS, connect, reset, timeout).</summary>
    Network,
}

/// <summary>Which vendor's error envelope a body most likely uses.</summary>
public enum AiProviderErrorDialect
{
    Generic,
    OpenAi,
    Anthropic,
    Gemini,
}

/// <summary>
/// What could be safely learned from a provider error response. Only allow-listed
/// tokens (<see cref="Type"/>, <see cref="Code"/>, <see cref="RequestId"/>) and a
/// redacted, single-line, at most 300 character <see cref="Message"/> survive.
/// <see cref="Message"/> is provider text: it may go to one structured server log
/// line and nowhere else (never an exception message, a database row or a client
/// response).
/// </summary>
public sealed record AiProviderError(
    AiProviderErrorClass Class,
    int? HttpStatus,
    string? Type,
    string? Code,
    string? Message,
    string? RequestId,
    TimeSpan? RetryAfter);

public static class AiProviderErrorClassExtensions
{
    /// <summary>Stable lower-case code: quota_exhausted, rate_limited, invalid_request, auth, overloaded, server_error, network or unknown.</summary>
    public static string ToCode(this AiProviderErrorClass errorClass) => errorClass switch
    {
        AiProviderErrorClass.QuotaExhausted => "quota_exhausted",
        AiProviderErrorClass.RateLimited => "rate_limited",
        AiProviderErrorClass.InvalidRequest => "invalid_request",
        AiProviderErrorClass.Auth => "auth",
        AiProviderErrorClass.Overloaded => "overloaded",
        AiProviderErrorClass.ServerError => "server_error",
        AiProviderErrorClass.Network => "network",
        _ => "unknown",
    };
}

/// <summary>
/// Parses and classifies provider HTTP errors without ever throwing. Nothing
/// here trusts the provider: the JSON is size and depth bounded, only allow-listed
/// tokens are kept, and free text is redacted (before truncation), stripped of
/// control characters and capped. Precedence when several signals disagree:
/// quota, auth, overloaded, rate limited, server, invalid request.
/// </summary>
public static class AiProviderErrorParser
{
    private const int MaxJsonBodyChars = 64 * 1024;
    private const int MaxRawMessageChars = 2000;
    private const int MaxMessageChars = 300;
    private const int MaxHeadOnlyChars = 60;
    private const int MaxQuotaIds = 8;
    private const int MaxQuotaIdChars = 128;
    private const int MaxDetails = 16;
    private const int MaxChainDepth = 8;
    private const double MaxRetryDelaySeconds = 24 * 60 * 60;

    private static readonly JsonDocumentOptions JsonOptions = new() { MaxDepth = 16 };

    private static readonly Regex TokenPattern = new(
        @"\A[a-z0-9_.\-]{1,64}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    private static readonly Regex RequestIdPattern = new(
        @"\A[A-Za-z0-9_\-]{1,128}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    private static readonly Regex ControlOrAnglePattern = new(
        @"[\p{C}\s<>]+", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    // Same shape AiRetryPolicy reads from AiProviderHttpException.Message ("... HTTP 429 ...").
    private static readonly Regex LegacyStatusPattern = new(
        @"\bHTTP\s+(\d{3})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    private static readonly string[] RequestIdHeaders = ["request-id", "x-request-id"];

    private static readonly HashSet<string> QuotaTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "insufficient_quota", "billing_hard_limit_reached", "billing_not_active",
        "billing_error", "quota_exceeded", "credit_balance_too_low",
    };

    // A rate token blocks the phrase based quota match: OpenAI rate limit text mentions billing.
    private static readonly HashSet<string> RateGuardTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "rate_limit_exceeded", "rate_limit_error", "requests", "tokens",
    };

    private static readonly HashSet<string> AuthTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "authentication_error", "permission_error", "invalid_api_key", "incorrect_api_key",
        "invalid_authentication", "unauthenticated", "permission_denied",
        "unsupported_country_region_territory", "api_key_invalid",
    };

    private static readonly HashSet<string> OverloadedTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "overloaded_error", "overloaded", "unavailable",
    };

    private static readonly HashSet<string> RateTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "rate_limit_error", "rate_limit_exceeded", "requests", "tokens", "too_many_requests", "resource_exhausted",
    };

    private static readonly HashSet<string> ServerTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "api_error", "server_error", "timeout_error", "internal", "deadline_exceeded", "engine_error",
    };

    private static readonly HashSet<string> InvalidTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "invalid_request_error", "not_found_error", "request_too_large", "invalid_argument",
        "failed_precondition", "not_found", "model_not_found", "invalid_model", "bad_request",
    };

    private static readonly string[] QuotaPhrases =
    [
        "credit balance is too low", "reached your specified api usage limits", "usage limits",
        "monthly spend limit", "spend limit", "out of credits", "billing hard limit", "exceeded your current quota",
    ];

    // Gemini repeats "exceeded your current quota" for per-minute limits too; its quota ids decide instead.
    private static readonly string[] GeminiQuotaPhrases =
    [
        "credit balance is too low", "reached your specified api usage limits", "usage limits",
        "monthly spend limit", "spend limit", "out of credits", "billing hard limit",
    ];

    private static readonly string[] AuthPhrases =
    [
        "api key not valid", "organization has been disabled", "account has been disabled",
    ];

    private static readonly IReadOnlyList<string> NoQuotaIds = Array.Empty<string>();

    /// <summary>
    /// Builds an <see cref="AiProviderError"/> from a non-success response. Never throws: any
    /// parse problem degrades to a status-only classification with a null message. For the OpenAI
    /// and Gemini dialects the message phrases (credit balance, usage limits, ...) are ignored on
    /// HTTP 400, 413, 415 and 422 because those replies can echo learner-controlled request content;
    /// see <see cref="PhraseRulesApply"/>.
    /// </summary>
    /// <param name="retainProviderText">True only for first-party vendor hosts. When false only
    /// the text before the first colon (at most 60 characters) is kept, so a sidecar's
    /// "Claude subscription quota/rate limit: &lt;CLI output tail&gt;" cannot carry transcript text.</param>
    public static AiProviderError Parse(
        AiProviderErrorDialect dialect,
        int httpStatus,
        string? body,
        HttpResponseHeaders? headers,
        string? apiKey,
        bool retainProviderText)
    {
        var env = new Envelope();
        try { env.RequestId = ReadRequestIdHeader(headers); } catch { /* header text is best effort */ }
        try { env.RetryAfter = ReadRetryAfterHeader(headers); } catch { /* header text is best effort */ }

        try
        {
            if (LooksLikeJsonObject(body))
            {
                using var doc = JsonDocument.Parse(body!, JsonOptions);
                ReadEnvelope(doc.RootElement, env);
            }
        }
        catch
        {
            // Not JSON, too deep, or an unexpected shape: status-only below.
        }

        var typeToken = ToToken(env.Type);
        var codeToken = ToToken(env.Code);
        var gemini = dialect == AiProviderErrorDialect.Gemini || env.QuotaIds.Count > 0 || env.GeminiReason is not null;
        var errorClass = ClassifyCore(
            httpStatus, typeToken, codeToken, env.Message, ToToken(env.GeminiReason), env.QuotaIds, gemini,
            phraseRules: PhraseRulesApply(dialect, httpStatus));

        return new AiProviderError(
            errorClass,
            httpStatus > 0 ? httpStatus : null,
            typeToken,
            codeToken,
            SanitizeMessage(env.Message, apiKey, headOnly: !retainProviderText),
            env.RequestId,
            env.RetryAfter);
    }

    /// <summary>
    /// Classifies any exception a provider call can throw. A typed
    /// <see cref="AiProviderHttpException"/> wins; otherwise transport failures are
    /// <see cref="AiProviderErrorClass.Network"/>, and the legacy "HTTP nnn" message
    /// convention plus the same phrase rules classify adapters that still throw plain
    /// <see cref="InvalidOperationException"/>. Never throws.
    /// </summary>
    public static AiProviderErrorClass Classify(Exception ex)
    {
        try
        {
            if (ex is null) return AiProviderErrorClass.Unknown;
            if (FindHttpException(ex) is { } typed) return typed.ErrorClass;

            var depth = 0;
            for (var current = ex; current is not null && depth < MaxChainDepth; current = current.InnerException, depth++)
            {
                if (current is HttpRequestException { StatusCode: { } status })
                    return ClassifyStatus((int)status);
            }

            depth = 0;
            for (var current = ex; current is not null && depth < MaxChainDepth; current = current.InnerException, depth++)
            {
                if (current is HttpRequestException or SocketException or IOException or TimeoutException)
                    return AiProviderErrorClass.Network;
                // A timeout surfaces as TaskCanceledException; a cancelled caller token is not a provider failure.
                if (current is OperationCanceledException canceled && !canceled.CancellationToken.IsCancellationRequested)
                    return AiProviderErrorClass.Network;
            }

            depth = 0;
            for (var current = ex; current is not null && depth < MaxChainDepth; current = current.InnerException, depth++)
            {
                var message = current.Message ?? string.Empty;
                var match = LegacyStatusPattern.Match(message);
                var status = match.Success && int.TryParse(match.Groups[1].Value, out var parsed) ? parsed : 0;
                var errorClass = ClassifyCore(status, null, null, message, null, NoQuotaIds, gemini: false);
                if (errorClass != AiProviderErrorClass.Unknown) return errorClass;
            }

            return AiProviderErrorClass.Unknown;
        }
        catch
        {
            return AiProviderErrorClass.Unknown;
        }
    }

    /// <summary>Status-only classification (no body available).</summary>
    internal static AiProviderErrorClass ClassifyStatus(int status)
        => ClassifyCore(status, null, null, null, null, NoQuotaIds, gemini: false);

    /// <summary>The first typed provider HTTP failure in the exception chain, if any.</summary>
    internal static AiProviderHttpException? FindHttpException(Exception? ex)
    {
        for (var depth = 0; ex is not null && depth < MaxChainDepth; ex = ex.InnerException, depth++)
        {
            if (ex is AiProviderHttpException typed) return typed;
        }

        return null;
    }

    /// <summary>
    /// Redacts (before any truncation), optionally reduces to the head before the first colon,
    /// removes control characters and angle brackets, collapses whitespace and caps the text
    /// at 300 characters. Fails closed (null) if redaction cannot finish.
    /// </summary>
    internal static string? SanitizeMessage(string? raw, string? apiKey, bool headOnly)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            // Redact the WHOLE text first: cutting to MaxRawMessageChars before redacting could leave a
            // secret that straddles the cut as a fragment shorter than the 20 characters the patterns need
            // (or as a partial literal key) and let it reach the output. Running it over the whole text
            // is cheap: the parser never reads a body over 64 KiB, and the redaction regex has a 50 ms
            // timeout that fails closed below.
            var text = AiProviderConnectionTester.RedactSecrets(raw, apiKey) ?? string.Empty;
            if (text.Length > MaxRawMessageChars) text = text[..MaxRawMessageChars];
            if (headOnly)
            {
                var colon = text.IndexOf(':');
                if (colon >= 0) text = text[..colon];
                if (text.Length > MaxHeadOnlyChars) text = text[..MaxHeadOnlyChars];
            }

            text = ControlOrAnglePattern.Replace(text, " ").Trim();
            if (text.Length > MaxMessageChars) text = text[..MaxMessageChars].TrimEnd();
            return text.Length == 0 ? null : text;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    /// <summary>
    /// False when the message text must not decide the class: an OpenAI or Gemini "the request was
    /// rejected" reply (HTTP 400, 413, 415 or 422) can echo request content, and on the live-voice
    /// calls that content is learner-controlled (an SDP offer, the instructions), so a crafted fragment
    /// such as "spend limit" could otherwise hard-open the shared provider breaker for every learner.
    /// Those classes come from the status and the provider's own type / code tokens instead.
    /// Anthropic keeps its phrases (its 400 credit-balance error IS a message), as do statuses
    /// 402, 429 and 5xx.
    /// </summary>
    private static bool PhraseRulesApply(AiProviderErrorDialect dialect, int status)
        => !(dialect is AiProviderErrorDialect.OpenAi or AiProviderErrorDialect.Gemini
             && status is 400 or 413 or 415 or 422);

    private static AiProviderErrorClass ClassifyCore(
        int status,
        string? type,
        string? code,
        string? message,
        string? geminiReason,
        IReadOnlyList<string> quotaIds,
        bool gemini,
        bool phraseRules = true)
    {
        // R1: quota / billing. A decisive quota token wins even next to a rate token (the
        // sidecar reports code quota_exceeded with type rate_limit_error).
        if (status == 402 || Has(QuotaTokens, type, code)) return AiProviderErrorClass.QuotaExhausted;

        var perMinute = false;
        foreach (var quotaId in quotaIds)
        {
            if (quotaId.Contains("perday", StringComparison.OrdinalIgnoreCase)
                || quotaId.Contains("daily", StringComparison.OrdinalIgnoreCase))
                return AiProviderErrorClass.QuotaExhausted;
            if (quotaId.Contains("perminute", StringComparison.OrdinalIgnoreCase)) perMinute = true;
        }

        if (gemini
            && string.Equals(type, "failed_precondition", StringComparison.OrdinalIgnoreCase)
            && ContainsPhrase(message, "billing"))
            return AiProviderErrorClass.QuotaExhausted;

        // Free-text phrase rules (quota, auth, overloaded) read this; see PhraseRulesApply. The Gemini
        // failed_precondition rule above is anchored on the provider's own status token, so it keeps
        // reading the message.
        var phraseText = phraseRules ? message : null;

        var rateGuard = perMinute || Has(RateGuardTokens, type, code);
        if (!rateGuard && ContainsAnyPhrase(phraseText, gemini ? GeminiQuotaPhrases : QuotaPhrases))
            return AiProviderErrorClass.QuotaExhausted;

        // R2: credentials.
        if (status is 401 or 403
            || Has(AuthTokens, type, code)
            || string.Equals(geminiReason, "api_key_invalid", StringComparison.OrdinalIgnoreCase)
            || string.Equals(geminiReason, "api_key_expired", StringComparison.OrdinalIgnoreCase)
            || ContainsAnyPhrase(phraseText, AuthPhrases))
            return AiProviderErrorClass.Auth;

        // R3: capacity.
        if (status is 503 or 529 || Has(OverloadedTokens, type, code) || ContainsPhrase(phraseText, "overloaded"))
            return AiProviderErrorClass.Overloaded;

        // R4: rate limit.
        if (status == 429 || Has(RateTokens, type, code)) return AiProviderErrorClass.RateLimited;

        // R5: provider fault.
        if (status >= 500 || status == 408 || Has(ServerTokens, type, code)) return AiProviderErrorClass.ServerError;

        // R6: the request itself.
        if (status is >= 400 and < 500 || Has(InvalidTokens, type, code)) return AiProviderErrorClass.InvalidRequest;

        return AiProviderErrorClass.Unknown;
    }

    private static bool Has(HashSet<string> tokens, string? type, string? code)
        => (type is not null && tokens.Contains(type)) || (code is not null && tokens.Contains(code));

    private static bool ContainsPhrase(string? message, string phrase)
        => message is not null && message.Contains(phrase, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsAnyPhrase(string? message, string[] phrases)
    {
        if (message is null) return false;
        foreach (var phrase in phrases)
        {
            if (message.Contains(phrase, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private static bool LooksLikeJsonObject(string? body)
        => !string.IsNullOrEmpty(body)
           && body.Length <= MaxJsonBodyChars
           && body.AsSpan().TrimStart().StartsWith("{", StringComparison.Ordinal);

    private static void ReadEnvelope(JsonElement root, Envelope env)
    {
        if (root.ValueKind != JsonValueKind.Object) return;

        if (root.TryGetProperty("error", out var error))
        {
            if (error.ValueKind == JsonValueKind.Object) ReadErrorObject(error, env);
            else if (error.ValueKind == JsonValueKind.String) env.Message = error.GetString();
        }
        else
        {
            // FastAPI style {"detail": "..."}.
            env.Message = ReadString(root, "detail");
        }

        // Anthropic puts request_id next to the error object.
        env.RequestId ??= ToRequestId(ReadString(root, "request_id"));
    }

    private static void ReadErrorObject(JsonElement error, Envelope env)
    {
        env.Type = ReadString(error, "type");
        env.Code = ReadString(error, "code");
        env.Message = ReadString(error, "message");
        // Gemini: error.status is the type (RESOURCE_EXHAUSTED, INVALID_ARGUMENT, ...); its numeric error.code is ignored.
        env.Type ??= ReadString(error, "status");

        if (error.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            ReadGeminiDetails(details, env);

        env.Code ??= env.GeminiReason ?? env.QuotaIds.FirstOrDefault();
    }

    private static void ReadGeminiDetails(JsonElement details, Envelope env)
    {
        foreach (var detail in details.EnumerateArray().Take(MaxDetails))
        {
            var kind = ReadString(detail, "@type") ?? string.Empty;
            if (kind.EndsWith("ErrorInfo", StringComparison.Ordinal))
            {
                env.GeminiReason ??= ReadString(detail, "reason");
            }
            else if (kind.EndsWith("QuotaFailure", StringComparison.Ordinal))
            {
                if (detail.ValueKind != JsonValueKind.Object
                    || !detail.TryGetProperty("violations", out var violations)
                    || violations.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var violation in violations.EnumerateArray().Take(MaxQuotaIds))
                {
                    var quotaId = ReadString(violation, "quotaId");
                    if (string.IsNullOrWhiteSpace(quotaId)) continue;
                    quotaId = quotaId.Trim();
                    env.QuotaIds.Add(quotaId.Length <= MaxQuotaIdChars ? quotaId : quotaId[..MaxQuotaIdChars]);
                }
            }
            else if (kind.EndsWith("RetryInfo", StringComparison.Ordinal))
            {
                env.RetryAfter ??= ParseProtoDuration(ReadString(detail, "retryDelay"));
            }
        }
    }

    private static string? ReadString(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Lower-cases and validates an allow-listed token ([a-z0-9_.-], 1 to 64 characters); null when it does not fit.</summary>
    internal static string? ToToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var lower = value.Trim().ToLowerInvariant();
        try
        {
            return TokenPattern.IsMatch(lower) ? lower : null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    private static string? ToRequestId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        try
        {
            return RequestIdPattern.IsMatch(trimmed) ? trimmed : null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    private static string? ReadRequestIdHeader(HttpResponseHeaders? headers)
    {
        if (headers is null) return null;
        foreach (var name in RequestIdHeaders)
        {
            if (!headers.TryGetValues(name, out var values)) continue;
            var id = ToRequestId(values.FirstOrDefault());
            if (id is not null) return id;
        }

        return null;
    }

    private static TimeSpan? ReadRetryAfterHeader(HttpResponseHeaders? headers)
    {
        var header = headers?.RetryAfter;
        if (header is null) return null;
        if (header.Delta is { } delta && delta > TimeSpan.Zero) return delta;
        if (header.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero) return wait;
        }

        return null;
    }

    // google.protobuf.Duration JSON form, e.g. "33s" or "0.5s".
    private static TimeSpan? ParseProtoDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (text.EndsWith('s')) text = text[..^1];
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)) return null;
        if (double.IsNaN(seconds) || seconds <= 0 || seconds > MaxRetryDelaySeconds) return null;
        return TimeSpan.FromSeconds(seconds);
    }

    private sealed class Envelope
    {
        public string? Type { get; set; }
        public string? Code { get; set; }
        public string? Message { get; set; }
        public string? RequestId { get; set; }
        public string? GeminiReason { get; set; }
        public TimeSpan? RetryAfter { get; set; }
        public List<string> QuotaIds { get; } = new();
    }
}
