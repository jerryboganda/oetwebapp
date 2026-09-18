using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Placement;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Learner + reviewer surface for the free General-English placement test
/// (private GEPA engine). Everything here is gated behind the
/// <c>Placement.Enabled</c> runtime flag; the engine itself additionally
/// enforces its approved-form readiness gate and per-request session
/// ownership against the OET learner id.
/// </summary>
public static class PlacementEndpoints
{
    public static IEndpointRouteBuilder MapPlacementEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1");

        var placement = v1.MapGroup("/placement")
            .RequireAuthorization("LearnerOnly")
            .AddEndpointFilter<PlacementEnabledFilter>();

        // ── Availability (used by the web app to reveal the entry) ──────
        placement.MapGet("/status", async (
            IRuntimeSettingsProvider settings, PlacementGateway gateway, CancellationToken ct) =>
        {
            var enabled = (await settings.GetAsync()).Placement.PlacementEnabled;
            return Results.Ok(new { enabled });
        });

        // ── Session lifecycle ────────────────────────────────────────────
        placement.MapPost("/session", async (
            HttpContext http, PlacementGateway gateway,
            [FromBody] PlacementCreateSessionRequest request, CancellationToken ct) =>
        {
            var created = await gateway.CreateSessionAsync(http.UserId(), new
            {
                target_goal = string.IsNullOrWhiteSpace(request.TargetGoal) ? "General" : request.TargetGoal,
                device_class = request.DeviceClass,
                candidate_uid = http.UserId(),
            }, ct);
            return Results.Ok(created.RootElement);
        });

        placement.MapGet("/session/{sessionId}", async (
            HttpContext http, PlacementGateway gateway, string sessionId, CancellationToken ct) =>
            Results.Ok((await gateway.GetSessionStateAsync(http.UserId(), sessionId, ct)).RootElement));

        placement.MapPost("/session/{sessionId}/module/{module}/start", async (
            HttpContext http, PlacementGateway gateway, string sessionId, string module, CancellationToken ct) =>
            Results.Ok((await gateway.StartModuleAsync(http.UserId(), sessionId, module, ct)).RootElement));

        placement.MapPost("/session/{sessionId}/responses", async (
            HttpContext http, PlacementGateway gateway, string sessionId,
            [FromBody] JsonElement body, CancellationToken ct) =>
            Results.Ok((await gateway.SubmitResponsesAsync(http.UserId(), sessionId, body, ct)).RootElement));

        // ── Results ──────────────────────────────────────────────────────
        placement.MapGet("/session/{sessionId}/result/receptive", async (
            HttpContext http, PlacementGateway gateway, string sessionId, CancellationToken ct) =>
            Results.Ok((await gateway.GetReceptiveResultAsync(http.UserId(), sessionId, ct)).RootElement));

        placement.MapGet("/session/{sessionId}/result/status", async (
            HttpContext http, PlacementGateway gateway, string sessionId, CancellationToken ct) =>
            Results.Ok((await gateway.GetResultsStatusAsync(http.UserId(), sessionId, ct)).RootElement));

        // Full result: proxied AND persisted into the OET-owned history so
        // engine-side retention can never erase the learner's verifiable
        // result. Upsert keeps the freshest engine assembly (incl. human
        // rescores) under the same unique session id.
        placement.MapGet("/session/{sessionId}/result/full", async (
            HttpContext http, PlacementGateway gateway, LearnerDbContext db, string sessionId, CancellationToken ct) =>
        {
            var learnerId = http.UserId();
            var report = await gateway.GetFullResultAsync(learnerId, sessionId, ct);
            await UpsertPlacementResultAsync(db, learnerId, sessionId, report.RootElement, ct);
            return Results.Ok(report.RootElement);
        });

        // ── Learner's result history (account-linked, survives engine
        //    retention) ──────────────────────────────────────────────────
        placement.MapGet("/history", async (
            HttpContext http, LearnerDbContext db, CancellationToken ct) =>
        {
            var learnerId = http.UserId();
            var rows = await db.PlacementResults
                .AsNoTracking()
                .Where(r => r.LearnerUserId == learnerId)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new PlacementHistoryItem(
                    r.Id, r.SessionId, r.RulesetVersion, r.Status, r.CreatedAt))
                .ToListAsync(ct);
            return Results.Ok(rows);
        });

        placement.MapGet("/history/{resultId}", async (
            HttpContext http, LearnerDbContext db, string resultId, CancellationToken ct) =>
        {
            var learnerId = http.UserId();
            var row = await db.PlacementResults
                .AsNoTracking()
                .SingleOrDefaultAsync(r => r.Id == resultId && r.LearnerUserId == learnerId, ct);
            return row is null
                ? Results.NotFound(new { error = "placement_result_not_found" })
                : Results.Content(row.ResultJson, "application/json");
        });

        // ── Listening stimulus audio (proxied, learner-gated) ───────────
        placement.MapGet("/audio/{fileName}", async (
            HttpContext http, PlacementGateway gateway, string fileName, CancellationToken ct) =>
        {
            var (contentType, bytes) = await gateway.GetAudioAsync(http.UserId(), fileName, ct);
            return Results.File(bytes, contentType);
        });

        // ── Speaking ─────────────────────────────────────────────────────
        placement.MapGet("/session/{sessionId}/speaking/tasks", async (
            HttpContext http, PlacementGateway gateway, string sessionId, CancellationToken ct) =>
            Results.Ok((await gateway.GetSpeakingTasksAsync(http.UserId(), sessionId, ct)).RootElement));

        placement.MapPost("/upload", async (
            HttpContext http, PlacementGateway gateway, IFormFile file, CancellationToken ct) =>
        {
            if (file is null || file.Length == 0)
            {
                throw ApiException.Validation("placement_recording_required", "A recording file is required.");
            }
            if (file.Length > 20 * 1024 * 1024)
            {
                throw ApiException.Validation("placement_recording_too_large", "Recordings are limited to 20 MB.");
            }
            await using var stream = file.OpenReadStream();
            var uploaded = await gateway.UploadRecordingAsync(
                http.UserId(), stream, file.FileName, file.ContentType, ct);
            return Results.Ok(uploaded.RootElement);
        }).DisableAntiforgery();

        placement.MapPost("/session/{sessionId}/speaking/{taskId}/submit", async (
            HttpContext http, PlacementGateway gateway, string sessionId, string taskId,
            [FromBody] PlacementSpeakingSubmitRequest request, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.StoragePath))
            {
                throw ApiException.Validation("placement_recording_required",
                    "Upload the recording via /v1/placement/upload before submitting.");
            }
            return Results.Ok((await gateway.SubmitSpeakingAsync(http.UserId(), sessionId, taskId, request.StoragePath, ct)).RootElement);
        });

        // ── Writing ──────────────────────────────────────────────────────
        placement.MapGet("/session/{sessionId}/writing/tasks", async (
            HttpContext http, PlacementGateway gateway, string sessionId, CancellationToken ct) =>
            Results.Ok((await gateway.GetWritingTasksAsync(http.UserId(), sessionId, ct)).RootElement));

        placement.MapPut("/session/{sessionId}/writing/{taskId}/draft", async (
            HttpContext http, PlacementGateway gateway, string sessionId, string taskId,
            [FromBody] PlacementWritingTextRequest request, CancellationToken ct) =>
            Results.Ok((await gateway.SaveWritingDraftAsync(http.UserId(), sessionId, taskId, request.Text, ct)).RootElement));

        placement.MapPost("/session/{sessionId}/writing/{taskId}/submit", async (
            HttpContext http, PlacementGateway gateway, string sessionId, string taskId,
            [FromBody] PlacementWritingTextRequest request, CancellationToken ct) =>
            Results.Ok((await gateway.SubmitWritingAsync(http.UserId(), sessionId, taskId, request.Text, ct)).RootElement));

        // ── Reviewer / admin surface (staff never log into the engine) ──
        var adminPlacement = v1.MapGroup("/admin/placement")
            .RequireAuthorization("AdminOnly");

        adminPlacement.MapGet("/health", async (PlacementGateway gateway, CancellationToken ct) =>
            Results.Ok((await gateway.GetReadyZAsync(ct)).RootElement));

        adminPlacement.MapGet("/review/queue", async (PlacementGateway gateway, CancellationToken ct) =>
            Results.Ok((await gateway.GetReviewQueueAsync(ct)).RootElement));

        adminPlacement.MapGet("/review/{sessionId}", async (PlacementGateway gateway, string sessionId, CancellationToken ct) =>
            Results.Ok((await gateway.GetReviewSessionAsync(sessionId, ct)).RootElement));

        adminPlacement.MapGet("/review/{sessionId}/audio/{taskId}", async (
            PlacementGateway gateway, string sessionId, string taskId, CancellationToken ct) =>
        {
            var (contentType, bytes) = await gateway.GetReviewAudioAsync(sessionId, taskId, ct);
            return Results.File(bytes, contentType);
        });

        adminPlacement.MapPost("/review/{sessionId}/rescore", async (
            PlacementGateway gateway, string sessionId, [FromBody] JsonElement body, CancellationToken ct) =>
            Results.Ok((await gateway.RescoreAsync(sessionId, body, ct)).RootElement));

        adminPlacement.MapPost("/review/{sessionId}/human-score", async (
            PlacementGateway gateway, string sessionId, [FromBody] JsonElement body, CancellationToken ct) =>
            Results.Ok((await gateway.HumanScoreAsync(sessionId, body, ct)).RootElement));

        return app;
    }

    /// <summary>Persist (or refresh) the OET-owned result history row for a
    /// completed placement session.</summary>
    private static async Task UpsertPlacementResultAsync(
        LearnerDbContext db, string learnerId, string sessionId, JsonElement report, CancellationToken ct)
    {
        var resultJson = report.GetRawText();
        var allSkillsMeasured = true;
        if (report.TryGetProperty("skills", out var skills) && skills.ValueKind == JsonValueKind.Array)
        {
            foreach (var skill in skills.EnumerateArray())
            {
                if (skill.TryGetProperty("status", out var status)
                    && !string.Equals(status.GetString(), "measured", StringComparison.Ordinal))
                {
                    allSkillsMeasured = false;
                    break;
                }
            }
        }
        // The report's wording/writing-policy version is the closest
        // report-level provenance marker. The engine serializes report
        // fields in camelCase (verified live), with snake_case fallbacks
        // for safety; the routing-ruleset version itself rides on the
        // session create/state responses.
        var rulesetVersion = report.TryGetProperty("wordingVersion", out var wv) && wv.ValueKind == JsonValueKind.String
            ? (wv.GetString() ?? "unknown")
            : report.TryGetProperty("wording_version", out var wv2) && wv2.ValueKind == JsonValueKind.String
                ? (wv2.GetString() ?? "unknown")
                : "unknown";

        var now = DateTime.UtcNow;
        var existing = await db.PlacementResults
            .SingleOrDefaultAsync(r => r.SessionId == sessionId, ct);
        if (existing is not null)
        {
            existing.ResultJson = resultJson;
            existing.Status = allSkillsMeasured ? "completed" : "partial";
            existing.UpdatedAt = now;
        }
        else
        {
            db.PlacementResults.Add(new PlacementResult
            {
                Id = $"place_{Guid.NewGuid():N}",
                LearnerUserId = learnerId,
                SessionId = sessionId,
                RulesetVersion = rulesetVersion,
                ResultJson = resultJson,
                Status = allSkillsMeasured ? "completed" : "partial",
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        await db.SaveChangesAsync(ct);
    }

    private static string UserId(this HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}

/// <summary>Endpoint filter: every placement route 404s (as if absent) while
/// the <c>Placement.Enabled</c> flag is off — no feature enumeration for
/// candidates before rollout.</summary>
public sealed class PlacementEnabledFilter : IEndpointFilter
{
    private readonly IRuntimeSettingsProvider _settings;

    public PlacementEnabledFilter(IRuntimeSettingsProvider settings) => _settings = settings;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var snapshot = await _settings.GetAsync();
        if (!snapshot.Placement.PlacementEnabled)
        {
            return Results.Json(new { error = "placement_disabled", message = "The placement test is not available yet." },
                statusCode: StatusCodes.Status404NotFound);
        }
        return await next(context);
    }
}

// ── DTOs ─────────────────────────────────────────────────────────────

public sealed record PlacementCreateSessionRequest(string? TargetGoal, string? DeviceClass);
public sealed record PlacementSpeakingSubmitRequest(string? StoragePath);
public sealed record PlacementWritingTextRequest(string Text);
public sealed record PlacementHistoryItem(
    string Id, string SessionId, string RulesetVersion, string Status, DateTime CreatedAt);
