using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services.Conversation.Tts;

public sealed class CosyVoiceConversationTtsProvider(
    IHttpClientFactory httpClientFactory,
    IConversationOptionsProvider optionsProvider,
    ILogger<CosyVoiceConversationTtsProvider> logger) : IConversationTtsProvider
{
    private ConversationOptions ReadOptions() => optionsProvider.GetAsync().GetAwaiter().GetResult();
    public string Name => "cosyvoice";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ReadOptions().CosyVoiceBaseUrl);

    public async Task<ConversationTtsResult> SynthesizeAsync(ConversationTtsRequest request, CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("CosyVoice not configured.");

        var client = httpClientFactory.CreateClient("ConversationCosyVoiceClient");
        if (!string.IsNullOrWhiteSpace(ReadOptions().CosyVoiceApiKey))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ReadOptions().CosyVoiceApiKey);

        var url = $"{ReadOptions().CosyVoiceBaseUrl.TrimEnd('/')}/tts";
        var payload = JsonSerializer.Serialize(new
        {
            text = request.Text,
            voice = string.IsNullOrWhiteSpace(request.Voice) ? ReadOptions().CosyVoiceDefaultVoice : request.Voice,
            language = request.Locale,
            rate = request.Rate ?? 1.0,
            pitch = request.Pitch ?? 0.0,
            format = "mp3",
        });
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        using var response = await client.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("CosyVoice {Status}: {Err}", (int)response.StatusCode, err);
            throw new ConversationTtsException("cosyvoice_error", $"CosyVoice {(int)response.StatusCode}");
        }
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var mime = response.Content.Headers.ContentType?.MediaType ?? "audio/mpeg";
        return new ConversationTtsResult(bytes, mime,
            AzureConversationTtsProvider.ApproxDurationMs(request.Text), Name, "cosyvoice");
    }
}

public sealed class ChatTtsConversationTtsProvider(
    IHttpClientFactory httpClientFactory,
    IConversationOptionsProvider optionsProvider,
    ILogger<ChatTtsConversationTtsProvider> logger) : IConversationTtsProvider
{
    private ConversationOptions ReadOptions() => optionsProvider.GetAsync().GetAwaiter().GetResult();
    public string Name => "chattts";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ReadOptions().ChatTtsBaseUrl);

    public async Task<ConversationTtsResult> SynthesizeAsync(ConversationTtsRequest request, CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("ChatTTS not configured.");

        var client = httpClientFactory.CreateClient("ConversationChatTtsClient");
        if (!string.IsNullOrWhiteSpace(ReadOptions().ChatTtsApiKey))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ReadOptions().ChatTtsApiKey);

        var url = $"{ReadOptions().ChatTtsBaseUrl.TrimEnd('/')}/tts";
        var payload = JsonSerializer.Serialize(new
        {
            text = request.Text,
            voice = string.IsNullOrWhiteSpace(request.Voice) ? ReadOptions().ChatTtsDefaultVoice : request.Voice,
            language = request.Locale,
            format = "mp3",
        });
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        using var response = await client.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("ChatTTS {Status}: {Err}", (int)response.StatusCode, err);
            throw new ConversationTtsException("chattts_error", $"ChatTTS {(int)response.StatusCode}");
        }
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var mime = response.Content.Headers.ContentType?.MediaType ?? "audio/mpeg";
        return new ConversationTtsResult(bytes, mime,
            AzureConversationTtsProvider.ApproxDurationMs(request.Text), Name, "chattts");
    }
}

/// <summary>
/// OpenAI-compatible TTS adapter for DigitalOcean Serverless Inference's Qwen3
/// TTS deployment. Supports BOTH model variants:
///   <list type="bullet">
///     <item><c>flash</c> → preset voice catalogue (deterministic, consistent).
///       Sends <c>{model: "qwen3-tts-flash-realtime", voice: "&lt;preset&gt;", input}</c>.</item>
///     <item><c>voicedesign</c> → free-form prompt model.
///       Sends <c>{model: "qwen3-tts-vd-realtime", voice: "default", input, instructions}</c>.</item>
///   </list>
/// The provider reuses the admin-editable ChatTTS endpoint fields (base URL,
/// API key) so the credentials match the existing conversation settings page.
/// The model variant + voice id + instructions live on dedicated Qwen3* options
/// (writable from the Voice Studio panel) and can be overridden per-call via
/// <see cref="ConversationTtsRequest.ModelVariant"/>, <see cref="ConversationTtsRequest.Voice"/>
/// and <see cref="ConversationTtsRequest.Instructions"/> for previews +
/// vocabulary regeneration jobs.
/// </summary>
public sealed class DigitalOceanQwen3TtsConversationProvider(
    IHttpClientFactory httpClientFactory,
    IConversationOptionsProvider optionsProvider,
    ILogger<DigitalOceanQwen3TtsConversationProvider> logger) : IConversationTtsProvider
{
    /// <summary>
    /// Authoritative list of Qwen3-TTS-Flash preset voice ids (case-sensitive).
    /// Sourced from Alibaba DashScope Qwen3-TTS documentation 2026-05. Used by
    /// the admin /tts/qwen3/voices/probe endpoint as the candidate set; each id
    /// is verified live via a 1-character synthesis attempt.
    /// </summary>
    public static readonly IReadOnlyList<Qwen3VoicePreset> KnownPresetVoices = new[]
    {
        new Qwen3VoicePreset("Cherry",      "Cherry — sunny, friendly young woman",       "female"),
        new Qwen3VoicePreset("Serena",      "Serena — gentle young woman",                "female"),
        new Qwen3VoicePreset("Ethan",       "Ethan — warm, energetic young man",          "male"),
        new Qwen3VoicePreset("Chelsie",     "Chelsie — two-dimensional virtual girl",     "female"),
        new Qwen3VoicePreset("Momo",        "Momo — playful, mischievous",                "female"),
        new Qwen3VoicePreset("Vivian",      "Vivian — confident, cute, slightly feisty",  "female"),
        new Qwen3VoicePreset("Moon",        "Moon — bold and handsome man (Yuebai)",      "male"),
        new Qwen3VoicePreset("Maia",        "Maia — intellect and gentleness blended",    "female"),
        new Qwen3VoicePreset("Kai",         "Kai — soothing audio spa for your ears",     "male"),
        new Qwen3VoicePreset("Nofish",      "Nofish — quirky designer",                   "male"),
        new Qwen3VoicePreset("Bella",       "Bella — bubbly, mischievous young woman",    "female"),
        new Qwen3VoicePreset("Jennifer",    "Jennifer — premium American English female", "female"),
        new Qwen3VoicePreset("Ryan",        "Ryan — dramatic flair, rhythmic",            "male"),
        new Qwen3VoicePreset("Katerina",    "Katerina — mature, sophisticated woman",     "female"),
        new Qwen3VoicePreset("Aiden",       "Aiden — friendly American young man",        "male"),
        new Qwen3VoicePreset("Eldric Sage", "Eldric Sage — calm and wise elder",          "female"),
        new Qwen3VoicePreset("Mia",         "Mia — gentle, delicate young woman",         "female"),
        new Qwen3VoicePreset("Mochi",       "Mochi — clever, quick-witted young adult",   "male"),
        new Qwen3VoicePreset("Bellona",     "Bellona — powerful, heroic woman",           "female"),
        new Qwen3VoicePreset("Vincent",     "Vincent — raspy, smoky, mysterious",         "male"),
        new Qwen3VoicePreset("Bunny",       "Bunny — little girl overflowing with cute",  "female"),
        new Qwen3VoicePreset("Neil",        "Neil — professional news anchor",            "female"),
        new Qwen3VoicePreset("Elias",       "Elias — academic instructor",                "female"),
        new Qwen3VoicePreset("Arthur",      "Arthur — earthy, village storyteller",       "male"),
        new Qwen3VoicePreset("Nini",        "Nini — soft, clingy, affectionate",          "female"),
        new Qwen3VoicePreset("Seren",       "Seren — gentle, soothing, sleep voice",      "female"),
        new Qwen3VoicePreset("Pip",         "Pip — playful boy, childlike wonder",        "male"),
        new Qwen3VoicePreset("Stella",      "Stella — youthful, earnest teenage girl",    "female"),
        new Qwen3VoicePreset("Bodega",      "Bodega — passionate Spanish man",            "male"),
        new Qwen3VoicePreset("Sonrisa",     "Sonrisa — cheerful Latin American woman",    "female"),
        new Qwen3VoicePreset("Alek",        "Alek — cold + warm Russian man",             "male"),
        new Qwen3VoicePreset("Dolce",       "Dolce — laid-back Italian man",              "male"),
        new Qwen3VoicePreset("Sohee",       "Sohee — warm, expressive Korean unnie",      "female"),
        new Qwen3VoicePreset("Ono Anna",    "Ono Anna — clever, spirited childhood friend", "female"),
        new Qwen3VoicePreset("Lenn",        "Lenn — rebellious German youth",             "male"),
        new Qwen3VoicePreset("Emilien",     "Emilien — romantic French big brother",      "male"),
        new Qwen3VoicePreset("Andre",       "Andre — magnetic, steady male voice",        "male"),
        new Qwen3VoicePreset("Radio Gol",   "Radio Gol — football commentary poet",       "male"),
        new Qwen3VoicePreset("Jada",        "Jada — Shanghai auntie (energetic)",         "female"),
        new Qwen3VoicePreset("Dylan",       "Dylan — Beijing hutong young man",           "male"),
        new Qwen3VoicePreset("Li",          "Li — Nanjing yoga teacher",                  "male"),
        new Qwen3VoicePreset("Marcus",      "Marcus — Shaanxi sincere man",               "male"),
        new Qwen3VoicePreset("Roy",         "Roy — Taiwanese humorous guy",               "male"),
        new Qwen3VoicePreset("Peter",       "Peter — Tianjin crosstalk foil",             "male"),
        new Qwen3VoicePreset("Sunny",       "Sunny — Sichuanese voice",                   "female"),
        new Qwen3VoicePreset("Eric",        "Eric — Chengdu charismatic man",             "male"),
        new Qwen3VoicePreset("Rocky",       "Rocky — Cantonese witty A Qiang",            "male"),
        new Qwen3VoicePreset("Kiki",        "Kiki — Hong Kong best-friend energy",        "female"),
    };

    private ConversationOptions ReadOptions() => optionsProvider.GetAsync().GetAwaiter().GetResult();
    public string Name => "digitalocean-qwen3-tts";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ReadOptions().ChatTtsBaseUrl)
                                && !string.IsNullOrWhiteSpace(ReadOptions().ChatTtsApiKey);

    public async Task<ConversationTtsResult> SynthesizeAsync(ConversationTtsRequest request, CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("DigitalOcean Qwen3 TTS not configured.");

        var options = ReadOptions();
        var client = httpClientFactory.CreateClient("ConversationDigitalOceanQwenTtsClient");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ChatTtsApiKey);

        var url = $"{options.ChatTtsBaseUrl.TrimEnd('/')}/audio/speech";

        // Per-call overrides win over admin-configured options. This is how the
        // Voice Studio "Preview" button + the vocabulary regenerate worker pin
        // a specific voice for a single synthesis without mutating settings.
        var variant = NormaliseVariant(request.ModelVariant ?? options.Qwen3ModelVariant);
        string payload;
        string modelTag;
        // Speed: per-call override → admin option → default 1.0 (range 0.25–4.0).
        // Pitch is NOT natively supported by the Qwen3 OpenAI-compatible endpoint;
        // for voicedesign variant it can be influenced via instructions text.
        var speed = request.Rate ?? options.Qwen3Speed;

        if (variant == "voicedesign")
        {
            // qwen3-tts-vd-realtime: prompt-driven, voice MUST be "default".
            var instructions = !string.IsNullOrWhiteSpace(request.Instructions)
                ? request.Instructions!
                : !string.IsNullOrWhiteSpace(options.Qwen3VoiceInstructions)
                    ? options.Qwen3VoiceInstructions
                    : "A clear, calm, professional English voice with neutral accent suitable for medical and clinical vocabulary pronunciation.";
            modelTag = "qwen3-tts-vd-realtime";
            payload = JsonSerializer.Serialize(new
            {
                model = modelTag,
                input = request.Text,
                voice = "default",
                instructions,
                speed,
            });
        }
        else
        {
            // qwen3-tts-flash-realtime: preset voice catalogue.
            // request.Voice may be either a preset id (probe / preview path) OR
            // legacy free-text from the old admin UI — in the latter case fall
            // back to the configured preset.
            var voiceId = !string.IsNullOrWhiteSpace(request.Voice) && IsKnownPreset(request.Voice)
                ? request.Voice!
                : !string.IsNullOrWhiteSpace(options.Qwen3VoiceId) && IsKnownPreset(options.Qwen3VoiceId)
                    ? options.Qwen3VoiceId
                    : "Cherry";
            modelTag = "qwen3-tts-flash-realtime";
            payload = JsonSerializer.Serialize(new
            {
                model = modelTag,
                input = request.Text,
                voice = voiceId,
                speed,
            });
        }
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        using var response = await client.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("DigitalOcean Qwen3 TTS {Status} ({Variant}): {Err}", (int)response.StatusCode, variant, err);
            throw new ConversationTtsException("digitalocean_qwen3_tts_error", $"DigitalOcean Qwen3 TTS {(int)response.StatusCode}");
        }
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var ct2 = response.Content.Headers.ContentType?.MediaType;
        // Qwen3 returns WAV (RIFF) bytes even when content-type is octet-stream.
        var mime = string.IsNullOrWhiteSpace(ct2) || ct2 == "application/octet-stream" ? "audio/wav" : ct2;
        return new ConversationTtsResult(bytes, mime,
            AzureConversationTtsProvider.ApproxDurationMs(request.Text), Name, modelTag);
    }

    private static string NormaliseVariant(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return "flash";
        var lower = v.Trim().ToLowerInvariant();
        return lower switch
        {
            "voicedesign" or "voice-design" or "vd" or "qwen3-tts-vd-realtime" or "qwen3-tts-voicedesign" => "voicedesign",
            _ => "flash",
        };
    }

    private static bool IsKnownPreset(string voiceId)
    {
        foreach (var v in KnownPresetVoices)
            if (string.Equals(v.Id, voiceId, StringComparison.Ordinal)) return true;
        return false;
    }
}

/// <summary>Qwen3 flash preset voice descriptor — used by the admin /probe endpoint.</summary>
public sealed record Qwen3VoicePreset(string Id, string Label, string Gender);

public sealed class GptSoVitsConversationTtsProvider(
    IHttpClientFactory httpClientFactory,
    IConversationOptionsProvider optionsProvider,
    ILogger<GptSoVitsConversationTtsProvider> logger) : IConversationTtsProvider
{
    private ConversationOptions ReadOptions() => optionsProvider.GetAsync().GetAwaiter().GetResult();
    public string Name => "gptsovits";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ReadOptions().GptSoVitsBaseUrl);

    public async Task<ConversationTtsResult> SynthesizeAsync(ConversationTtsRequest request, CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("GPT-SoVITS not configured.");

        var client = httpClientFactory.CreateClient("ConversationGptSoVitsClient");
        if (!string.IsNullOrWhiteSpace(ReadOptions().GptSoVitsApiKey))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ReadOptions().GptSoVitsApiKey);

        var url = $"{ReadOptions().GptSoVitsBaseUrl.TrimEnd('/')}/tts";
        var payload = JsonSerializer.Serialize(new
        {
            text = request.Text,
            voice = string.IsNullOrWhiteSpace(request.Voice) ? ReadOptions().GptSoVitsDefaultVoice : request.Voice,
            language = request.Locale,
            format = "mp3",
        });
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        using var response = await client.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("GPT-SoVITS {Status}: {Err}", (int)response.StatusCode, err);
            throw new ConversationTtsException("gptsovits_error", $"GPT-SoVITS {(int)response.StatusCode}");
        }
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var mime = response.Content.Headers.ContentType?.MediaType ?? "audio/mpeg";
        return new ConversationTtsResult(bytes, mime,
            AzureConversationTtsProvider.ApproxDurationMs(request.Text), Name, "gptsovits");
    }
}
