using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Speaking;

/// <summary>One of the four live-patient voices (gender by age band) as configured, with what the owner needs to judge it.</summary>
public sealed record LiveVoicePreviewCell(
    string Key,
    string Gender,
    string AgeBand,
    string OpenAiVoice,
    string GeminiVoice,
    /// <summary>The accent GPT-Live documents for the default OpenAI voice; empty for an overridden voice. Not verified by ear.</summary>
    string AccentNote);

public sealed record LiveVoicePreviewList(
    string PrimaryProvider,
    IReadOnlyList<string> CandidateOrder,
    IReadOnlyList<LiveVoicePreviewCell> Cells,
    string SampleText,
    int MaxSeconds);

public sealed record LiveVoicePreviewOfferRequest(string? Cell, string? Sdp);

public sealed record LiveVoicePreviewOfferResponse(
    string Cell,
    string Voice,
    string Model,
    string ProviderSessionId,
    string AnswerSdp,
    string SampleText,
    int MaxSeconds);

/// <summary>
/// Lets the owner hear the four OpenAI live-patient voices (owner decision 2026-10-05: OpenAI is the primary provider with
/// the quartz / willow / ripple / vesper pool) before approving them, without running a mock test. An admin browser opens a
/// short GPT-Live WebRTC session for one voice; the server only relays the SDP offer with the provider key (never returned) and
/// the session ends after <see cref="MaxSeconds"/>. A preview is a live-voice provider session, so it is covered by the same
/// carve-out as learners' live sessions (no <c>AiUsageRecord</c>, outside the AI budget caps; docs/speaking/live-voice.md) and is
/// audited instead. It never feeds the provider's circuit breaker: an admin test must not shut learners out.
/// </summary>
public sealed class LiveVoicePreviewService(
    IHttpClientFactory httpFactory,
    IOptions<LiveVoiceOptions> options,
    LiveVoiceProviderProbeState probe,
    LearnerDbContext db,
    TimeProvider clock)
{
    public const string SampleText =
        "Good morning, doctor. I have had this pain in my chest for about two days now, and it is worse when I climb the stairs.";

    public const int MaxSeconds = 20;

    private static readonly (string Key, string Gender, string AgeBand)[] CellDefinitions =
    [
        ("female-younger", "Female", "Under 45"),
        ("female-older", "Female", "45 and over"),
        ("male-younger", "Male", "Under 45"),
        ("male-older", "Male", "45 and over"),
    ];

    private static readonly Dictionary<string, string> DefaultAccents = new(StringComparer.OrdinalIgnoreCase)
    {
        ["quartz"] = "Australian",
        ["ripple"] = "Australian",
        ["willow"] = "Irish",
        ["vesper"] = "British",
    };

    public LiveVoicePreviewList List()
    {
        var o = options.Value;
        var health = probe.Snapshot(o);
        var primary = o.ProviderOrder().FirstOrDefault() ?? o.LegacyProviderOrder()[0];
        var cells = CellDefinitions
            .Select(c =>
            {
                var openAi = OpenAiVoice(o, c.Key);
                return new LiveVoicePreviewCell(
                    c.Key, c.Gender, c.AgeBand, openAi, GeminiVoice(o, c.Key),
                    DefaultAccents.TryGetValue(openAi, out var accent) ? accent : string.Empty);
            })
            .ToList();
        return new LiveVoicePreviewList(primary, health.CandidateOrder, cells, SampleText, MaxSeconds);
    }

    public async Task<LiveVoicePreviewOfferResponse> CreateOfferAsync(
        string adminId, string adminName, LiveVoicePreviewOfferRequest request, CancellationToken ct)
    {
        var o = options.Value;
        if (!o.IsOpenAiConfigured)
        {
            throw ApiException.ServiceUnavailable(
                "live_voice_provider_not_configured",
                "The OpenAI realtime voice provider is not configured for this environment.");
        }

        var cell = (request?.Cell ?? string.Empty).Trim().ToLowerInvariant();
        if (!CellDefinitions.Any(c => c.Key == cell))
        {
            throw ApiException.Validation("live_voice_preview_cell_invalid", "Choose one of the four voices.");
        }

        if (string.IsNullOrWhiteSpace(request?.Sdp))
        {
            throw ApiException.Validation("live_voice_sdp_required", "A WebRTC SDP offer is required.");
        }

        if (request.Sdp.Length > 128_000)
        {
            throw ApiException.Validation("live_voice_sdp_too_large", "The WebRTC SDP offer is too large.");
        }

        var voice = OpenAiVoice(o, cell);
        var instructions =
            "This is a voice preview, not a role-play. As soon as the session starts, say exactly the following sentence once, "
            + "naturally, the way a patient in a clinic would say it, and then stay silent: \"" + SampleText + "\"";
        object session = string.IsNullOrWhiteSpace(voice)
            ? new { model = o.OpenAiModel, instructions }
            : new { model = o.OpenAiModel, instructions, audio = new { output = new { voice = voice.Trim() } } };
        var payload = new { session, transport = new { type = "webrtc", sdp = request.Sdp } };

        using var message = new HttpRequestMessage(HttpMethod.Post, o.OpenAiBaseUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.OpenAiApiKey);

        string body;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(o.ProviderRequestTimeout());
            using var response = await httpFactory.CreateClient("LiveVoiceProvider").SendAsync(message, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                // The provider's own message is never shown: only the status.
                throw ApiException.ServiceUnavailable(
                    "live_voice_preview_failed",
                    $"The provider refused the preview session (HTTP {(int)response.StatusCode}).");
            }

            body = await response.Content.ReadAsStringAsync(timeout.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw ApiException.ServiceUnavailable("live_voice_preview_failed", "The provider did not answer the preview session in time.");
        }

        if (!TryReadAnswer(body, out var providerSessionId, out var answerSdp))
        {
            throw ApiException.ServiceUnavailable("live_voice_preview_failed", "The provider returned an invalid preview session response.");
        }

        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"audit-{Guid.NewGuid():N}",
            OccurredAt = clock.GetUtcNow(),
            ActorId = adminId,
            ActorName = string.IsNullOrWhiteSpace(adminName) ? adminId : adminName,
            Action = "LiveVoicePreviewStarted",
            ResourceType = "LiveVoiceProvider",
            ResourceId = LiveVoiceProviders.OpenAi,
            Details = JsonSerializer.Serialize(new { cell, voice, model = o.OpenAiModel, characters = SampleText.Length }),
        });
        await db.SaveChangesAsync(ct);

        return new LiveVoicePreviewOfferResponse(cell, voice, o.OpenAiModel, providerSessionId, answerSdp, SampleText, MaxSeconds);
    }

    internal static string OpenAiVoice(LiveVoiceOptions o, string cell) => cell switch
    {
        "female-younger" => o.OpenAiVoiceFemaleYounger,
        "female-older" => o.OpenAiVoiceFemaleOlder,
        "male-younger" => o.OpenAiVoiceMaleYounger,
        "male-older" => o.OpenAiVoiceMaleOlder,
        _ => string.Empty,
    };

    private static string GeminiVoice(LiveVoiceOptions o, string cell) => cell switch
    {
        "female-younger" => o.GeminiVoiceFemaleYounger,
        "female-older" => o.GeminiVoiceFemaleOlder,
        "male-younger" => o.GeminiVoiceMaleYounger,
        "male-older" => o.GeminiVoiceMaleOlder,
        _ => string.Empty,
    };

    private static bool TryReadAnswer(string body, out string providerSessionId, out string answerSdp)
    {
        providerSessionId = string.Empty;
        answerSdp = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (!root.TryGetProperty("session", out var session) || !session.TryGetProperty("id", out var id)
                || !root.TryGetProperty("transport", out var transport) || !transport.TryGetProperty("sdp", out var sdp)
                || id.ValueKind != JsonValueKind.String || sdp.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(id.GetString()) || string.IsNullOrWhiteSpace(sdp.GetString()))
            {
                return false;
            }

            providerSessionId = id.GetString()!;
            answerSdp = sdp.GetString()!;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
