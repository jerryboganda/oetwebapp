using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Services.Conversation.Tts;
using OetLearner.Api.Services.VoiceDesign;

namespace OetLearner.Api.Endpoints;

public static class VoiceDesignAdminEndpoints
{
    public static IEndpointRouteBuilder MapVoiceDesignAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/voice-design")
            .RequireAuthorization("admin")
            .WithTags("Admin – Voice Design");

        // GET /config — current global voice design configuration
        group.MapGet("/config", async (
            IConversationOptionsProvider optionsProvider,
            CancellationToken ct) =>
        {
            var opts = await optionsProvider.GetCurrentAsync(ct);
            return Results.Ok(new AdminVoiceDesignConfigResponse(
                ModelVariant: opts.Qwen3ModelVariant ?? "flash",
                VoiceId: opts.Qwen3VoiceId ?? "Cherry",
                VoiceInstructions: opts.Qwen3VoiceInstructions ?? "",
                Speed: opts.Qwen3Speed ?? 1.0,
                Pitch: opts.Qwen3Pitch ?? 0,
                Emotion: opts.Qwen3Emotion ?? "",
                LastUpdatedAt: null,
                LastUpdatedBy: null));
        });

        // PUT /config — save voice design config globally
        group.MapPut("/config", async (
            [FromBody] AdminVoiceDesignConfigRequest request,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var row = await db.ConversationSettings.FirstOrDefaultAsync(ct)
                ?? new Domain.ConversationSettingsRow();

            if (request.ModelVariant is not null) row.Qwen3ModelVariant = request.ModelVariant;
            if (request.VoiceId is not null) row.Qwen3VoiceId = request.VoiceId;
            if (request.Instructions is not null)
                row.Qwen3VoiceInstructions = request.Instructions.Length > 1000
                    ? request.Instructions[..1000] : request.Instructions;
            if (request.Speed is not null) row.Qwen3Speed = request.Speed;
            if (request.Pitch is not null) row.Qwen3Pitch = request.Pitch;
            if (request.Emotion is not null) row.Qwen3Emotion = request.Emotion;

            if (row.Id == default) db.ConversationSettings.Add(row);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { saved = true });
        });

        // POST /preview — preview voice with full params
        group.MapPost("/preview", async (
            [FromBody] AdminVoiceDesignPreviewRequest request,
            IConversationTtsProviderSelector selector,
            CancellationToken ct) =>
        {
            var provider = await selector.TrySelectAsync("digitalocean-qwen3-tts", ct);
            if (provider is null)
                return Results.Problem("Qwen3 TTS provider not configured or unavailable.");

            var ttsReq = new ConversationTtsRequest(
                Text: request.Text ?? "Good morning. I'm going to check your vitals today.",
                Voice: request.VoiceId ?? "Cherry",
                Locale: request.Locale ?? "en-GB",
                Rate: request.Speed,
                Pitch: request.Pitch,
                ModelVariant: request.ModelVariant ?? "flash",
                Instructions: request.Instructions);

            var result = await provider.SynthesizeAsync(ttsReq, ct);
            return Results.File(result.Audio, result.MimeType, "preview.wav");
        });

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
        });

        // GET /batches — list all regeneration batches
        group.MapGet("/batches", async (
            IVoiceDesignRegenerationService regenService,
            CancellationToken ct) =>
        {
            var batches = await regenService.GetBatchesAsync(ct);
            return Results.Ok(new { batches });
        });

        // GET /batches/{batchId} — get batch progress
        group.MapGet("/batches/{batchId}", async (
            string batchId,
            IVoiceDesignRegenerationService regenService,
            CancellationToken ct) =>
        {
            var batch = await regenService.GetBatchProgressAsync(batchId, ct);
            return batch is null ? Results.NotFound() : Results.Ok(batch);
        });

        // POST /batches/{batchId}/cancel — cancel an in-progress batch
        group.MapPost("/batches/{batchId}/cancel", async (
            string batchId,
            IVoiceDesignRegenerationService regenService,
            CancellationToken ct) =>
        {
            var cancelled = await regenService.CancelBatchAsync(batchId, ct);
            return Results.Ok(new { cancelled });
        });

        return app;
    }
}
