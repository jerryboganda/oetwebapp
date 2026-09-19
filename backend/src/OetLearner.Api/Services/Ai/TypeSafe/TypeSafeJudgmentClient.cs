using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services.Ai.TypeSafe;

/// <summary>Transport-level failure. The message carries only the status and
/// a truncated provider body — never the request payload.</summary>
public sealed class TypeSafeHttpException(string message, int statusCode)
    : InvalidOperationException(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed record TypeSafeRawResponse(
    string Model,
    IReadOnlyDictionary<string, JevAnswer> Answers,
    int InputTokens,
    int OutputTokens);

/// <summary>
/// Builds the TypeSafe SystemOne wire payload from typed questions. Kept
/// separate from transport so the governed service can hash the EXACT bytes
/// it will send (the control-plane RequestHash is computed over this JSON —
/// never over a raw prompt kept anywhere).
/// </summary>
public static class TypeSafeRequestBuilder
{
    public static string BuildPayload(JevJudgmentRequest request, string model)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Questions.Count == 0)
            throw new InvalidOperationException("TypeSafe judgment request needs at least one question.");
        if ((request.StateText is null) == (request.StateJson is null))
            throw new InvalidOperationException(
                "TypeSafe judgment request needs exactly one of StateText or StateJson.");

        var questions = new Dictionary<string, object?>(request.Questions.Count);
        foreach (var q in request.Questions)
        {
            questions[q.Id] = q.Kind switch
            {
                JevQuestionKind.Noul => BuildNoul(q),
                JevQuestionKind.Choice => BuildChoice(q),
                JevQuestionKind.Score => BuildScore(q),
                _ => throw new InvalidOperationException($"Unknown Jev question kind '{q.Kind}' for '{q.Id}'."),
            };
        }

        var payload = new Dictionary<string, object?>
        {
            ["state"] = request.StateJson is { } json ? (object)json : request.StateText!,
            ["model"] = model,
            ["questions"] = questions,
        };
        return JsonSerializer.Serialize(payload);
    }

    private static Dictionary<string, object?> BuildNoul(JevQuestion q)
    {
        if (q.NoulCriteria is not { Count: 2 } criteria
            || !criteria.ContainsKey("true") || !criteria.ContainsKey("false"))
        {
            throw new InvalidOperationException(
                $"Noul question '{q.Id}' needs NoulCriteria with exactly the keys \"true\" and \"false\".");
        }

        return new Dictionary<string, object?>
        {
            ["type"] = "noul",
            ["instructions"] = q.Instructions,
            ["criteria"] = new Dictionary<string, object?>(criteria),
        };
    }

    private static Dictionary<string, object?> BuildChoice(JevQuestion q)
    {
        if (q.ChoiceCriteria is not { Count: > 0 } criteria)
            throw new InvalidOperationException($"Choice question '{q.Id}' needs ChoiceCriteria options.");

        return new Dictionary<string, object?>
        {
            ["type"] = "choice",
            ["instructions"] = q.Instructions,
            ["criteria"] = new Dictionary<string, object?>(criteria),
        };
    }

    private static Dictionary<string, object?> BuildScore(JevQuestion q)
    {
        if (q.ScoreLevels is not { Count: >= 2 } levels)
            throw new InvalidOperationException(
                $"Score question '{q.Id}' needs at least two ScoreLevels.");

        return new Dictionary<string, object?>
        {
            ["type"] = "score",
            ["instructions"] = q.Instructions,
            ["criteria"] = levels,
        };
    }
}

/// <summary>
/// Thin transport over <c>POST {BaseUrl}/v1/systemone</c>. Retries ONLY the
/// provider-designated transient statuses (429 Too Many Requests / 529
/// Overloaded) with exponential backoff, honouring Retry-After when present.
/// 401/422 are configuration or contract bugs — they throw immediately.
/// Recording, policy, and budgets live in <see cref="TypeSafeJudgmentService"/>;
/// this class deliberately knows nothing about them.
/// </summary>
public interface ITypeSafeJudgmentClient
{
    Task<TypeSafeRawResponse> SendAsync(string payloadJson, CancellationToken ct);
}

public sealed class TypeSafeJudgmentClient(
    IHttpClientFactory httpClientFactory,
    IOptions<TypeSafeOptions> options) : ITypeSafeJudgmentClient
{
    public const string HttpClientName = "TypeSafeJudgmentClient";

    private const string EndpointPath = "v1/systemone";
    private const int RetryableStatusTooManyRequests = 429;
    private const int RetryableStatusOverloaded = 529;
    private static readonly TimeSpan BaseBackoff = TimeSpan.FromMilliseconds(500);
    private const int MaxLoggedErrorBodyChars = 300;

    public async Task<TypeSafeRawResponse> SendAsync(string payloadJson, CancellationToken ct)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.ApiKey))
            throw new InvalidOperationException("TypeSafe is enabled but TypeSafe:ApiKey is empty.");

        var client = httpClientFactory.CreateClient(HttpClientName);
        var baseUri = opts.BaseUrl.TrimEnd('/');

        for (var attempt = 0; ; attempt++)
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUri}/{EndpointPath}");
            httpRequest.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opts.ApiKey);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, opts.TimeoutSeconds)));

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(httpRequest, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Our own timeout fired — retrying a judgment that already ate
                // its latency budget is pointless; surface as unavailable.
                throw new TypeSafeHttpException("TypeSafe judgment timed out.", statusCode: 0);
            }

            var status = (int)response.StatusCode;
            if (status is RetryableStatusTooManyRequests or RetryableStatusOverloaded
                && attempt < Math.Max(0, opts.MaxRetries))
            {
                var delay = BackoffDelay(response.Headers.RetryAfter, attempt);
                response.Dispose();
                await Task.Delay(delay, ct);
                continue;
            }

            using var _ = response;
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new TypeSafeHttpException(
                    $"TypeSafe judgment failed: HTTP {status} {response.ReasonPhrase}. {Truncate(body)}",
                    status);
            }

            return ParseResponse(body);
        }
    }

    private static TimeSpan BackoffDelay(RetryConditionHeaderValue? retryAfter, int attempt)
    {
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
            return delta;
        if (retryAfter?.Date is { } date)
        {
            var until = date - DateTimeOffset.UtcNow;
            if (until > TimeSpan.Zero) return until;
        }

        return TimeSpan.FromTicks(BaseBackoff.Ticks << Math.Min(attempt, 4));
    }

    internal static TypeSafeRawResponse ParseResponse(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var model = root.TryGetProperty("model", out var modelEl) && modelEl.ValueKind == JsonValueKind.String
            ? modelEl.GetString() ?? throw new TypeSafeHttpException("TypeSafe response missing model.", 0)
            : throw new TypeSafeHttpException("TypeSafe response missing model.", 0);

        var inputTokens = 0;
        var outputTokens = 0;
        if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            inputTokens = usage.TryGetProperty("input_tokens", out var inEl) && inEl.TryGetInt32(out var inTok) ? inTok : 0;
            outputTokens = usage.TryGetProperty("output_tokens", out var outEl) && outEl.TryGetInt32(out var outTok) ? outTok : 0;
        }

        if (!root.TryGetProperty("answers", out var answersEl) || answersEl.ValueKind != JsonValueKind.Object)
            throw new TypeSafeHttpException("TypeSafe response missing answers object.", 0);

        var answers = new Dictionary<string, JevAnswer>(StringComparer.Ordinal);
        foreach (var answerProperty in answersEl.EnumerateObject())
        {
            answers[answerProperty.Name] = ParseAnswer(answerProperty.Value);
        }

        return new TypeSafeRawResponse(model, answers, inputTokens, outputTokens);
    }

    private static JevAnswer ParseAnswer(JsonElement answer)
    {
        var type = answer.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String
            ? typeEl.GetString()
            : null;

        switch (type)
        {
            case "noul":
            {
                var noul = RequiredNumber(answer, "noul");
                return new JevAnswer(JevQuestionKind.Noul, new JevNoulAnswer(noul), null, null);
            }
            case "choice":
            {
                var choice = answer.TryGetProperty("choice", out var choiceEl) && choiceEl.ValueKind == JsonValueKind.String
                    ? choiceEl.GetString() ?? throw new TypeSafeHttpException("TypeSafe choice answer missing choice.", 0)
                    : throw new TypeSafeHttpException("TypeSafe choice answer missing choice.", 0);
                var probabilities = ParseProbabilities(answer);
                var confidence = RequiredNumber(answer, "confidence");
                return new JevAnswer(JevQuestionKind.Choice, null, new JevChoiceAnswer(choice, probabilities, confidence), null);
            }
            case "score":
            {
                var score = RequiredNumber(answer, "score");
                var probabilities = ParseProbabilities(answer);
                var confidence = RequiredNumber(answer, "confidence");
                return new JevAnswer(JevQuestionKind.Score, null, null, new JevScoreAnswer(score, probabilities, confidence));
            }
            default:
                throw new TypeSafeHttpException($"TypeSafe answer type '{type}' is not recognised — contract drift.", 0);
        }
    }

    private static IReadOnlyDictionary<string, double> ParseProbabilities(JsonElement answer)
    {
        if (!answer.TryGetProperty("probabilities", out var probs) || probs.ValueKind != JsonValueKind.Object)
            throw new TypeSafeHttpException("TypeSafe answer missing probabilities.", 0);

        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var p in probs.EnumerateObject())
        {
            result[p.Name] = p.Value.ValueKind == JsonValueKind.Number ? p.Value.GetDouble() : 0d;
        }

        return result;
    }

    private static double RequiredNumber(JsonElement answer, string propertyName)
        => answer.TryGetProperty(propertyName, out var el) && el.ValueKind == JsonValueKind.Number
            ? el.GetDouble()
            : throw new TypeSafeHttpException($"TypeSafe answer missing {propertyName}.", 0);

    private static string Truncate(string s) => s.Length <= MaxLoggedErrorBodyChars ? s : s[..MaxLoggedErrorBodyChars];
}
