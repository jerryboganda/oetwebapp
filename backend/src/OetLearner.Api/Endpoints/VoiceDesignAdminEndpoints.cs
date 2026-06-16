using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Conversation;
using OetLearner.Api.Services.Conversation.Tts;
using OetLearner.Api.Services.VoiceDesign;

namespace OetLearner.Api.Endpoints;

public static class VoiceDesignAdminEndpoints
{
    public static IEndpointRouteBuilder MapVoiceDesignAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/voice-design")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser")
            .WithTags("Admin – Voice Design");

        // GET /config — current global voice design configuration
        group.MapGet("/config", async (
            LearnerDbContext db,
            IConversationOptionsProvider optionsProvider,
            CancellationToken ct) =>
        {
            var opts = await optionsProvider.GetAsync(ct);
            var row = await db.ConversationSettings.AsNoTracking().FirstOrDefaultAsync(r => r.Id == "default", ct);
            return Results.Ok(new AdminVoiceDesignConfigResponse(
                ElevenLabsTtsBaseUrl: opts.ElevenLabsTtsBaseUrl,
                ElevenLabsDefaultVoiceId: opts.ElevenLabsDefaultVoiceId,
                ElevenLabsModel: opts.ElevenLabsModel,
                ElevenLabsOutputFormat: opts.ElevenLabsOutputFormat,
                ElevenLabsPronunciationDictionaryId: opts.ElevenLabsPronunciationDictionaryId,
                ElevenLabsPronunciationDictionaryVersionId: opts.ElevenLabsPronunciationDictionaryVersionId,
                ElevenLabsStability: opts.ElevenLabsStability,
                ElevenLabsSimilarityBoost: opts.ElevenLabsSimilarityBoost,
                ElevenLabsStyle: opts.ElevenLabsStyle,
                ElevenLabsUseSpeakerBoost: opts.ElevenLabsUseSpeakerBoost,
                ElevenLabsApiKeyPresent: !string.IsNullOrWhiteSpace(opts.ElevenLabsApiKey),
                LastUpdatedAt: row?.UpdatedAt.ToString("o"),
                LastUpdatedBy: row?.UpdatedByUserName ?? row?.UpdatedByUserId));
        }).WithAdminRead("AdminAiConfig");

        // PUT /config — save voice design config globally
        group.MapPut("/config", async (
            [FromBody] AdminVoiceDesignConfigRequest request,
            LearnerDbContext db,
            HttpContext httpContext,
            IConversationOptionsProvider optionsProvider,
            CancellationToken ct) =>
        {
            var row = await db.ConversationSettings.FirstOrDefaultAsync(r => r.Id == "default", ct);
            if (row is null)
            {
                row = new Domain.ConversationSettingsRow { Id = "default" };
                db.ConversationSettings.Add(row);
            }

            if (request.ElevenLabsTtsBaseUrl is not null) row.ElevenLabsTtsBaseUrl = ElevenLabsApiEndpoint.NormalizeBaseUrl(request.ElevenLabsTtsBaseUrl);
            if (request.ElevenLabsDefaultVoiceId is not null) row.ElevenLabsDefaultVoiceId = request.ElevenLabsDefaultVoiceId;
            if (request.ElevenLabsModel is not null) row.ElevenLabsModel = request.ElevenLabsModel;
            if (request.ElevenLabsOutputFormat is not null) row.ElevenLabsOutputFormat = NormalizeMp3OutputFormat(request.ElevenLabsOutputFormat);
            if (request.ElevenLabsPronunciationDictionaryId is not null) row.ElevenLabsPronunciationDictionaryId = request.ElevenLabsPronunciationDictionaryId;
            if (request.ElevenLabsPronunciationDictionaryVersionId is not null) row.ElevenLabsPronunciationDictionaryVersionId = request.ElevenLabsPronunciationDictionaryVersionId;
            if (request.ElevenLabsStability.HasValue) row.ElevenLabsStability = request.ElevenLabsStability;
            if (request.ElevenLabsSimilarityBoost.HasValue) row.ElevenLabsSimilarityBoost = request.ElevenLabsSimilarityBoost;
            if (request.ElevenLabsStyle.HasValue) row.ElevenLabsStyle = request.ElevenLabsStyle;
            if (request.ElevenLabsUseSpeakerBoost.HasValue) row.ElevenLabsUseSpeakerBoost = request.ElevenLabsUseSpeakerBoost;
            if (request.ElevenLabsApiKey is not null)
            {
                var provider = (ConversationOptionsProvider)optionsProvider;
                row.ElevenLabsApiKeyEncrypted = request.ElevenLabsApiKey.Length == 0
                    ? null
                    : provider.Protect(request.ElevenLabsApiKey);
            }
            row.UpdatedAt = DateTimeOffset.UtcNow;
            row.UpdatedByUserId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? httpContext.User.FindFirst("sub")?.Value;
            row.UpdatedByUserName = httpContext.User.Identity?.Name ?? row.UpdatedByUserId;

            db.AuditEvents.Add(new Domain.AuditEvent
            {
                Id = $"AUD-{Guid.NewGuid():N}",
                OccurredAt = DateTimeOffset.UtcNow,
                ActorId = row.UpdatedByUserId ?? "system",
                ActorName = row.UpdatedByUserName ?? "system",
                Action = "VoiceDesignSettingsUpdated",
                ResourceType = "ConversationSettings",
                ResourceId = row.Id,
                Details = JsonSerializer.Serialize(new
                {
                    changed = "voice-design-config",
                    elevenLabsApiKeyProvided = request.ElevenLabsApiKey is not null && request.ElevenLabsApiKey.Length > 0,
                }),
            });

            await db.SaveChangesAsync(ct);
            optionsProvider.Invalidate();

            return Results.Ok(new { saved = true });
        }).WithAdminWrite("AdminAiConfig");

        group.MapPost("/elevenlabs/dictionary", async (
            IFormFile file,
            [FromForm] string? name,
            LearnerDbContext db,
            IHttpClientFactory httpClientFactory,
            IConversationOptionsProvider optionsProvider,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            if (file.Length <= 0) return Results.BadRequest(new { code = "empty_dictionary_file" });
            if (file.Length > 1024 * 1024) return Results.BadRequest(new { code = "dictionary_file_too_large" });
            if (!string.Equals(Path.GetExtension(file.FileName), ".pls", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { code = "dictionary_file_must_be_pls" });

            var plsValidation = await ValidatePlsAsync(file, ct);
            if (plsValidation is not null) return Results.BadRequest(new { code = plsValidation });

            var options = await optionsProvider.GetAsync(ct);
            if (string.IsNullOrWhiteSpace(options.ElevenLabsApiKey))
                return Results.Problem("ElevenLabs API key is not configured.", statusCode: StatusCodes.Status409Conflict);

            using var form = new MultipartFormDataContent();
            await using var stream = file.OpenReadStream();
            using var fileContent = new StreamContent(stream);
            // ElevenLabs add-from-file expects the lexicon as a binary upload.
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            form.Add(fileContent, "file", file.FileName);
            form.Add(new StringContent(string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(file.FileName) : name.Trim()), "name");

            var client = httpClientFactory.CreateClient("ConversationElevenLabsClient");
            var baseUrl = ElevenLabsApiEndpoint.NormalizeBaseUrl(options.ElevenLabsTtsBaseUrl);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/pronunciation-dictionaries/add-from-file");
            request.Headers.Add("xi-api-key", options.ElevenLabsApiKey);
            request.Content = form;
            using var response = await client.SendAsync(request, ct);
            var responseText = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                // Surface the actual ElevenLabs error so the admin can act on it
                // (previously this returned a generic "failed" with no detail).
                return Results.Problem(
                    detail: $"ElevenLabs dictionary upload failed ({(int)response.StatusCode}): {Truncate(responseText, 1000)}",
                    statusCode: StatusCodes.Status502BadGateway,
                    title: "elevenlabs_dictionary_upload_failed");

            var dictionaryId = TryReadString(responseText, "id")
                ?? TryReadString(responseText, "pronunciation_dictionary_id");
            var versionId = TryReadString(responseText, "version_id")
                ?? TryReadNestedString(responseText, "version", "id")
                ?? TryReadNestedString(responseText, "latest_version", "id");
            if (string.IsNullOrWhiteSpace(dictionaryId))
                return Results.Problem(
                    detail: $"ElevenLabs dictionary upload response did not include an id: {Truncate(responseText, 1000)}",
                    statusCode: StatusCodes.Status502BadGateway,
                    title: "elevenlabs_dictionary_upload_no_id");

            var row = await db.ConversationSettings.FirstOrDefaultAsync(r => r.Id == "default", ct);
            if (row is null)
            {
                row = new Domain.ConversationSettingsRow { Id = "default" };
                db.ConversationSettings.Add(row);
            }
            row.ElevenLabsPronunciationDictionaryId = dictionaryId;
            row.ElevenLabsPronunciationDictionaryVersionId = versionId;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            row.UpdatedByUserId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? httpContext.User.FindFirst("sub")?.Value;
            row.UpdatedByUserName = httpContext.User.Identity?.Name ?? row.UpdatedByUserId;
            db.AuditEvents.Add(new Domain.AuditEvent
            {
                Id = $"AUD-{Guid.NewGuid():N}",
                OccurredAt = DateTimeOffset.UtcNow,
                ActorId = row.UpdatedByUserId ?? "system",
                ActorName = row.UpdatedByUserName ?? "system",
                Action = "ElevenLabsPronunciationDictionaryUploaded",
                ResourceType = "ConversationSettings",
                ResourceId = row.Id,
                Details = JsonSerializer.Serialize(new { dictionaryId, versionId, filename = file.FileName }),
            });
            await db.SaveChangesAsync(ct);
            optionsProvider.Invalidate();

            return Results.Ok(new { dictionaryId, versionId });
        }).DisableAntiforgery().WithAdminWrite("AdminAiConfig");

        // GET /elevenlabs/voices — list the ElevenLabs voice catalogue so admins
        // can browse + select the default voice id.
        group.MapGet("/elevenlabs/voices", async (
            IHttpClientFactory httpClientFactory,
            IConversationOptionsProvider optionsProvider,
            CancellationToken ct) =>
        {
            var options = await optionsProvider.GetAsync(ct);
            if (string.IsNullOrWhiteSpace(options.ElevenLabsApiKey))
                return Results.Problem("ElevenLabs API key is not configured.", statusCode: StatusCodes.Status409Conflict);

            var client = httpClientFactory.CreateClient("ConversationElevenLabsClient");
            var baseUrl = ElevenLabsApiEndpoint.NormalizeBaseUrl(options.ElevenLabsTtsBaseUrl);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/voices");
            request.Headers.Add("xi-api-key", options.ElevenLabsApiKey);
            using var response = await client.SendAsync(request, ct);
            var responseText = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                return Results.Problem(
                    $"ElevenLabs voices request failed ({(int)response.StatusCode}): {Truncate(responseText, 1000)}",
                    statusCode: StatusCodes.Status502BadGateway);

            var voices = ParseElevenLabsVoices(responseText);
            return Results.Ok(new { voices });
        }).WithAdminRead("AdminAiConfig");

        // POST /preview — synthesize a short preview clip via ElevenLabs.
        group.MapPost("/preview", async (
            [FromBody] AdminVoiceDesignPreviewRequest request,
            IConversationTtsProviderSelector selector,
            CancellationToken ct) =>
        {
            var provider = await selector.TrySelectAsync("elevenlabs", ct);
            if (provider is null)
                return Results.Problem("ElevenLabs TTS provider not configured or unavailable.");

            var ttsReq = new ConversationTtsRequest(
                Text: request.Text ?? "Good morning. I'm going to check your vitals today.",
                Voice: request.VoiceId ?? string.Empty,
                Locale: request.Locale ?? "en-GB");

            var result = await provider.SynthesizeAsync(ttsReq, ct);
            return Results.File(result.Audio, result.MimeType, "preview.mp3");
            // POST mutation verb: needs the per-user write limiter even though
            // authorization is read-scoped (it only previews audio, no mutation).
        }).WithAdminRead("AdminAiConfig").RequireRateLimiting("PerUserWrite");

        // POST /regenerate — bulk regenerate audio across platform
        group.MapPost("/regenerate", async (
            [FromBody] AdminAudioRegenerateRequest request,
            IVoiceDesignRegenerationService regenService,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userId = httpContext.User.FindFirst("sub")?.Value ?? "system";
            var result = await regenService.EnqueueBulkRegenerationAsync(request, userId, ct);
            return Results.Ok(result);
        }).WithAdminWrite("AdminAiConfig");

        // GET /batches — list all regeneration batches
        group.MapGet("/batches", async (
            IVoiceDesignRegenerationService regenService,
            CancellationToken ct) =>
        {
            var batches = await regenService.GetBatchesAsync(ct);
            return Results.Ok(new { batches });
        }).WithAdminRead("AdminAiConfig");

        // GET /batches/{batchId} — get batch progress
        group.MapGet("/batches/{batchId}", async (
            string batchId,
            IVoiceDesignRegenerationService regenService,
            CancellationToken ct) =>
        {
            var batch = await regenService.GetBatchProgressAsync(batchId, ct);
            return batch is null ? Results.NotFound() : Results.Ok(batch);
        }).WithAdminRead("AdminAiConfig");

        // POST /batches/{batchId}/cancel — cancel an in-progress batch
        group.MapPost("/batches/{batchId}/cancel", async (
            string batchId,
            IVoiceDesignRegenerationService regenService,
            CancellationToken ct) =>
        {
            var cancelled = await regenService.CancelBatchAsync(batchId, ct);
            return Results.Ok(new { cancelled });
        }).WithAdminWrite("AdminAiConfig");

        // POST /batches/{batchId}/retry — re-enqueue failed or incomplete recall audio jobs
        group.MapPost("/batches/{batchId}/retry", async (
            string batchId,
            IVoiceDesignRegenerationService regenService,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userId = httpContext.User.FindFirst("sub")?.Value ?? "system";
            var batch = await regenService.RetryFailedBatchAsync(batchId, userId, ct);
            return batch is null ? Results.NotFound() : Results.Ok(batch);
        }).WithAdminWrite("AdminAiConfig");

        return app;
    }

    private static string? TryReadString(string json, string property)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryReadNestedString(string json, string parent, string child)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(parent, out var parentValue)
                && parentValue.ValueKind == JsonValueKind.Object
                && parentValue.TryGetProperty(child, out var childValue)
                && childValue.ValueKind == JsonValueKind.String
                ? childValue.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string NormalizeMp3OutputFormat(string? outputFormat)
        => !string.IsNullOrWhiteSpace(outputFormat)
           && outputFormat.StartsWith("mp3_", StringComparison.OrdinalIgnoreCase)
            ? outputFormat.Trim()
            : "mp3_44100_128";

    internal static string Truncate(string? value, int max)
        => string.IsNullOrEmpty(value) ? string.Empty : (value.Length <= max ? value : value[..max]);

    internal static List<AdminElevenLabsVoiceDto> ParseElevenLabsVoices(string json)
    {
        var result = new List<AdminElevenLabsVoiceDto>();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("voices", out var voices) || voices.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var v in voices.EnumerateArray())
            {
                var voiceId = v.TryGetProperty("voice_id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
                if (string.IsNullOrWhiteSpace(voiceId)) continue;
                var name = v.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? voiceId : voiceId;
                var category = v.TryGetProperty("category", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                var previewUrl = v.TryGetProperty("preview_url", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

                Dictionary<string, string>? labels = null;
                if (v.TryGetProperty("labels", out var lbl) && lbl.ValueKind == JsonValueKind.Object)
                {
                    labels = new Dictionary<string, string>();
                    foreach (var prop in lbl.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.String)
                            labels[prop.Name] = prop.Value.GetString() ?? string.Empty;
                    }
                }

                result.Add(new AdminElevenLabsVoiceDto(voiceId!, name, category, previewUrl, labels));
            }
        }
        catch (JsonException)
        {
            // Malformed payloads surface as an empty catalogue rather than a 500.
        }
        return result;
    }

    private static async Task<string?> ValidatePlsAsync(IFormFile file, CancellationToken ct)
    {
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 1024 * 1024,
            };
            await using var stream = file.OpenReadStream();
            using var reader = XmlReader.Create(stream, settings);
            var document = await XDocument.LoadAsync(reader, LoadOptions.None, ct);
            if (!string.Equals(document.Root?.Name.LocalName, "lexicon", StringComparison.OrdinalIgnoreCase))
                return "dictionary_file_invalid_pls_root";
            if (document.Descendants().Count() > 10_000)
                return "dictionary_file_too_many_entries";
            return null;
        }
        catch (XmlException)
        {
            return "dictionary_file_invalid_xml";
        }
    }
}
