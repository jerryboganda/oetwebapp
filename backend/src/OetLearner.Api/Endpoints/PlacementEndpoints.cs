using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
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
            HttpContext http, IRuntimeSettingsProvider settings, PlacementGateway gateway,
            LearnerDbContext db, CancellationToken ct) =>
        {
            var placement = (await settings.GetAsync()).Placement;
            var access = placement.BetaOnly
                ? (placement.IsBetaEmail(http.User.FindFirstValue(ClaimTypes.Email)
                    ?? http.User.FindFirst("email")?.Value)
                    ? "granted"
                    : "not_in_beta")
                : "granted";
            // Extra time is admin-approved only; the candidate UI just needs to
            // know THAT (and how much) it was approved. The approver is never
            // exposed on a learner route.
            var grant = await ActiveAccommodationAsync(db, http.UserId(), ct);
            return Results.Ok(new
            {
                enabled = placement.PlacementEnabled,
                betaOnly = placement.BetaOnly,
                access,
                extraTimePercent = grant?.ExtraTimePercent,
            });
        });

        // ── Session lifecycle ────────────────────────────────────────────
        placement.MapPost("/session", async (
            HttpContext http, PlacementGateway gateway, LearnerDbContext db, TimeProvider clock,
            ILoggerFactory loggerFactory, [FromBody] PlacementCreateSessionRequest request, CancellationToken ct) =>
        {
            var learnerId = http.UserId();
            var body = new Dictionary<string, object?>
            {
                ["target_goal"] = string.IsNullOrWhiteSpace(request.TargetGoal) ? "General" : request.TargetGoal,
                ["device_class"] = request.DeviceClass,
                ["candidate_uid"] = learnerId,
            };

            // Extra time comes ONLY from an admin-approved grant looked up
            // here by the authenticated learner id. The request DTO has no
            // accommodations member, so nothing the client sends can reach
            // the engine's accommodations block.
            var grant = await ActiveAccommodationAsync(db, learnerId, ct);
            if (grant is not null)
            {
                body["accommodations"] = new
                {
                    extra_time_percent = grant.ExtraTimePercent,
                    extended_time = true,
                    // Stored engine-side for audit. The engine echoes this block
                    // on GET session state, which the learner route strips.
                    extra_time_approval = new
                    {
                        approval_id = grant.Id,
                        approved_by = grant.ApprovedByUserId,
                        approved_by_name = grant.ApprovedByName,
                        approved_at = Iso(grant.ApprovedAt),
                    },
                };
            }

            using var created = await gateway.CreateSessionAsync(learnerId, body, ct);
            var createdRoot = created.RootElement;
            var sessionId = createdRoot.ValueKind == JsonValueKind.Object
                && createdRoot.TryGetProperty("session_id", out var sessionIdElement)
                && sessionIdElement.ValueKind == JsonValueKind.String
                    ? sessionIdElement.GetString()
                    : null;
            var logger = loggerFactory.CreateLogger("Placement.Session");

            // Fail closed: an approved grant must be confirmed by the engine's
            // accommodations_applied echo (same percent AND approval id). A
            // missing echo (older engine), a null echo or a different value
            // means the attempt would run WITHOUT the approved extra time, so
            // it is refused rather than silently handed a standard clock. With
            // no grant the echo is ignored. The engine session that was just
            // created is never returned to the learner, so it can never be
            // started; no use record is written for it.
            if (grant is not null && !AccommodationEchoMatches(createdRoot, grant, out var echoed))
            {
                logger.LogError(
                    "PLACEMENT_ACCOMMODATION_NOT_APPLIED session={SessionId} accommodation={AccommodationId} expectedPercent={ExpectedPercent} expectedApprovalId={ExpectedApprovalId} echoed={Echoed}",
                    sessionId ?? "unknown", grant.Id, grant.ExtraTimePercent, grant.Id, echoed);
                // retryable: false — the browser client auto-retries retryable 5xx
                // responses, and every retry would create another engine session
                // (each carrying the approved extra time, with no use record).
                throw ApiException.ServiceUnavailable(
                    "placement_accommodation_not_applied",
                    "Your approved extra time could not be applied to this attempt, so it was not started. Please try again, or contact support if this keeps happening.",
                    retryable: false);
            }

            // Record which attempt used the grant (idempotent on session id).
            if (grant is not null && sessionId is { Length: > 0 })
            {
                try
                {
                    if (!await db.PlacementAccommodationUses.AnyAsync(u => u.SessionId == sessionId, ct))
                    {
                        db.PlacementAccommodationUses.Add(new PlacementAccommodationUse
                        {
                            Id = $"pacu_{Guid.NewGuid():N}",
                            AccommodationId = grant.Id,
                            LearnerUserId = learnerId,
                            SessionId = sessionId,
                            ExtraTimePercent = grant.ExtraTimePercent,
                            AppliedAt = clock.GetUtcNow(),
                        });
                        await db.SaveChangesAsync(ct);
                    }
                }
                catch (Exception ex)
                {
                    // The engine session already exists with extra time applied.
                    // Failing the request here would make the client retry and
                    // create a SECOND extra-time session with no use record, so
                    // swallow, shout, and return the created session. The engine
                    // session carries approval_id for reconciliation.
                    logger.LogError(
                        ex,
                        "PLACEMENT_ACCOMMODATION_USE_NOT_RECORDED session={SessionId} accommodation={AccommodationId}",
                        sessionId, grant.Id);
                }
            }

            // The engine-issued candidate bearer is never handed to the browser:
            // every later call goes through this proxy, which authenticates with
            // the service token and the OET learner id.
            var node = JsonNode.Parse(createdRoot.GetRawText());
            if (node is JsonObject createdObject)
            {
                createdObject.Remove("token");
                // Same stance as the session-state route: the learner sees THAT
                // (and how much) extra time applies, never the approval record.
                if (createdObject["accommodations_applied"] is JsonObject appliedObject)
                {
                    appliedObject.Remove("approval_id");
                }
            }
            return Results.Ok(node);
        });

        // The engine echoes the stored accommodations block on session state,
        // including the approving admin's id and name (extraTimeApproval).
        // That record is staff-only, so it is stripped before the candidate
        // sees it; extraTimePercent / extendedTime stay for the UI.
        placement.MapGet("/session/{sessionId}", async (
            HttpContext http, PlacementGateway gateway, string sessionId, CancellationToken ct) =>
        {
            using var state = await gateway.GetSessionStateAsync(http.UserId(), sessionId, ct);
            var node = JsonNode.Parse(state.RootElement.GetRawText());
            if (node is JsonObject root && root["accommodations"] is JsonObject accommodations)
            {
                accommodations.Remove("extraTimeApproval");
                accommodations.Remove("extra_time_approval");
            }
            return Results.Ok(node);
        });

        placement.MapPost("/session/{sessionId}/module/{module}/start", async (
            HttpContext http, PlacementGateway gateway, string sessionId, string module, CancellationToken ct) =>
            Results.Ok((await gateway.StartModuleAsync(http.UserId(), sessionId, module, ct)).RootElement));

        placement.MapPost("/session/{sessionId}/responses", async (
            HttpContext http, PlacementGateway gateway, string sessionId,
            [FromBody] JsonElement body, CancellationToken ct) =>
            Results.Ok((await gateway.SubmitResponsesAsync(http.UserId(), sessionId, body, ct)).RootElement));

        // Starts the server clock on the currently delivered unit. The client
        // calls it once the unit is actually usable (for Listening: once the
        // audio has loaded and begun), so buffering and permission prompts
        // are never charged to the candidate. Idempotent engine-side.
        placement.MapPost("/session/{sessionId}/module/{module}/unit/start", async (
            HttpContext http, PlacementGateway gateway, string sessionId, string module, CancellationToken ct) =>
            Results.Ok((await gateway.StartUnitAsync(http.UserId(), sessionId, module, ct)).RootElement));

        // A unit whose media could not be delivered is excluded from scoring
        // (never counted as wrong) and the engine serves a replacement.
        placement.MapPost("/session/{sessionId}/module/{module}/unit/technical", async (
            HttpContext http, PlacementGateway gateway, string sessionId, string module,
            [FromBody] PlacementUnitTechnicalRequest request, CancellationToken ct) =>
        {
            if (request.Reason is null || !PlacementUnitTechnicalRequest.AllowedReasons.Contains(request.Reason))
            {
                throw ApiException.Validation("placement_technical_reason_invalid", "Unknown technical failure reason.");
            }
            return Results.Ok((await gateway.ReportUnitTechnicalAsync(http.UserId(), sessionId, module, request.Reason, ct)).RootElement);
        });

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
                ? new ApiErrorResult(404, "placement_result_not_found", "Placement result not found.")
                : Results.Content(row.ResultJson, "application/json");
        });

        // ── Listening stimulus audio (proxied, learner-gated) ───────────
        placement.MapGet("/audio/{fileName}", async (
            HttpContext http, PlacementGateway gateway, string fileName, CancellationToken ct) =>
        {
            var (contentType, bytes) = await gateway.GetAudioAsync(http.UserId(), fileName, ct);
            return Results.File(bytes, contentType, enableRangeProcessing: true);
        });

        // ── Speaking ─────────────────────────────────────────────────────
        placement.MapGet("/session/{sessionId}/speaking/tasks", async (
            HttpContext http, PlacementGateway gateway, string sessionId, CancellationToken ct) =>
            Results.Ok((await gateway.GetSpeakingTasksAsync(http.UserId(), sessionId, ct)).RootElement));

        placement.MapPost("/upload", async (
            HttpContext http, PlacementGateway gateway, ILoggerFactory loggerFactory, IFormFile file, CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("Placement.Upload");
            if (file is null || file.Length == 0)
            {
                throw ApiException.Validation("placement_recording_required", "A recording file is required.");
            }
            if (file.Length > 20 * 1024 * 1024)
            {
                logger.LogWarning("Placement recording rejected: {Bytes} bytes ({ContentType}) exceeds 20 MB", file.Length, file.ContentType);
                throw ApiException.Validation("placement_recording_too_large", "Recordings are limited to 20 MB.");
            }
            await using var stream = file.OpenReadStream();
            var uploaded = await gateway.UploadRecordingAsync(
                http.UserId(), stream, file.FileName, file.ContentType, ct);

            // Owner spec §6.2 debugging trail: browser MIME, size and the
            // engine's server-measured audio metrics (no audio content, no ids).
            var metrics = uploaded.RootElement.TryGetProperty("metrics", out var m) && m.ValueKind == JsonValueKind.Object ? m : default;
            logger.LogInformation(
                "Placement recording stored: {ContentType}, {Bytes} bytes, duration {DurationSec}s, container {Container}, provenance {Provenance}",
                file.ContentType,
                file.Length,
                metrics.ValueKind == JsonValueKind.Object && metrics.TryGetProperty("duration_sec", out var d) ? d.ToString() : "unknown",
                metrics.ValueKind == JsonValueKind.Object && metrics.TryGetProperty("container", out var c) ? c.ToString() : "unknown",
                metrics.ValueKind == JsonValueKind.Object && metrics.TryGetProperty("metrics_provenance", out var p) ? p.ToString() : "unknown");
            return Results.Ok(uploaded.RootElement);
        }).RequireRateLimiting("PerUserWrite").DisableAntiforgery();

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

        // Granular permissions (the group-level AdminOnly alone is not enough;
        // AdminEndpointAuthorizationInventoryTests). Every console route maps
        // to ReviewOps, matching lib/admin-permissions.ts for /admin/placement:
        // the page loads health + inventory alongside the queue, so a
        // ReviewOps-only reviewer must be able to read them (system_admin
        // passes every policy).
        adminPlacement.MapGet("/health", async (PlacementGateway gateway, CancellationToken ct) =>
            Results.Ok((await gateway.GetReadyZAsync(ct)).RootElement))
            .WithAdminRead("AdminReviewOps");

        // Active item counts by skill × CEFR band / route, so empty route
        // cells are visible before public launch (owner spec §8.1).
        adminPlacement.MapGet("/inventory", async (PlacementGateway gateway, CancellationToken ct) =>
            Results.Ok((await gateway.GetInventoryAsync(ct)).RootElement))
            .WithAdminRead("AdminReviewOps");

        adminPlacement.MapGet("/review/queue", async (PlacementGateway gateway, CancellationToken ct) =>
            Results.Ok((await gateway.GetReviewQueueAsync(ct)).RootElement))
            .WithAdminRead("AdminReviewOps");

        // Per-task review: an optional ?taskId= selects one submitted task and is
        // forwarded to the engine as ?task_id=. Engine-issued task ids are short
        // slugs, so anything else is refused here and never reaches the engine.
        adminPlacement.MapGet("/review/{sessionId}", async (
            PlacementGateway gateway, string sessionId, string? taskId, CancellationToken ct) =>
        {
            if (taskId is not null && !IsValidReviewTaskId(taskId))
            {
                throw ApiException.Validation("placement_review_task_invalid",
                    "taskId must be 1 to 64 characters: letters, digits, underscore, dot or hyphen.");
            }
            return Results.Ok((await gateway.GetReviewSessionAsync(sessionId, taskId, ct)).RootElement);
        }).WithAdminRead("AdminReviewOps");

        adminPlacement.MapGet("/review/{sessionId}/audio/{taskId}", async (
            PlacementGateway gateway, string sessionId, string taskId, CancellationToken ct) =>
        {
            var (contentType, bytes) = await gateway.GetReviewAudioAsync(sessionId, taskId, ct);
            return Results.File(bytes, contentType, enableRangeProcessing: true);
        }).WithAdminRead("AdminReviewOps");

        adminPlacement.MapPost("/review/{sessionId}/rescore", async (
            PlacementGateway gateway, string sessionId, [FromBody] JsonElement body, CancellationToken ct) =>
            Results.Ok((await gateway.RescoreAsync(sessionId, body, ct)).RootElement))
            .WithAdminWrite("AdminReviewOps");

        adminPlacement.MapPost("/review/{sessionId}/human-score", async (
            PlacementGateway gateway, string sessionId, [FromBody] JsonElement body, CancellationToken ct) =>
            Results.Ok((await gateway.HumanScoreAsync(sessionId, body, ct)).RootElement))
            .WithAdminWrite("AdminReviewOps");

        // ── Extra-time accommodations (admin-approved only) ─────────────
        // Candidates cannot enable extra time themselves; an admin grants it
        // to a learner account and every grant/revoke is audited. The grant
        // is applied server-side when the learner starts a session.
        adminPlacement.MapPost("/accommodations", async Task<IResult> (
            HttpContext http, LearnerDbContext db, TimeProvider clock,
            [FromBody] PlacementAccommodationGrantRequest request, CancellationToken ct) =>
        {
            if (request.ExtraTimePercent is < 1 or > 100)
            {
                throw ApiException.Validation("placement_extra_time_invalid",
                    "Extra time must be a whole percentage from 1 to 100.");
            }
            var reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim();
            if (reference is { Length: > 200 })
            {
                throw ApiException.Validation("placement_accommodation_reference_too_long",
                    "The reference must be 200 characters or fewer.");
            }
            var learner = await FindLearnerAsync(db, request.LearnerUserId, request.LearnerEmail, ct)
                ?? throw ApiException.NotFound("placement_learner_not_found", "No learner matches that email or id.");

            var adminId = http.UserId();
            var adminName = http.AdminName();
            var now = clock.GetUtcNow();

            // One active grant per learner: the new grant supersedes any
            // existing one (kept, revoked, for the audit trail).
            var superseded = await db.PlacementAccommodations
                .Where(a => a.LearnerUserId == learner.Id && a.RevokedAt == null)
                .ToListAsync(ct);
            foreach (var previous in superseded)
            {
                previous.RevokedByUserId = adminId;
                previous.RevokedByName = adminName;
                previous.RevokedAt = now;
                previous.RevokedReason = "superseded";
            }

            var grant = new PlacementAccommodation
            {
                Id = $"pacc_{Guid.NewGuid():N}",
                LearnerUserId = learner.Id,
                ExtraTimePercent = request.ExtraTimePercent,
                Reference = reference,
                ApprovedByUserId = adminId,
                ApprovedByName = adminName,
                ApprovedAt = now,
            };
            db.PlacementAccommodations.Add(grant);
            AddAccommodationAudit(db, http, "PlacementAccommodationGranted", grant.Id, now, new
            {
                learnerUserId = learner.Id,
                extraTimePercent = grant.ExtraTimePercent,
                reference = grant.Reference,
                supersededIds = superseded.Select(a => a.Id).ToArray(),
            });
            await db.SaveChangesAsync(ct);

            var dto = (await BuildAccommodationDtosAsync(db, [grant], ct))[0];
            return Results.Created($"/v1/admin/placement/accommodations?learner={Uri.EscapeDataString(learner.Id)}", dto);
        }).WithAdminWrite("AdminLearnerWrite");

        adminPlacement.MapGet("/accommodations", async Task<IResult> (
            LearnerDbContext db, string? learner, bool? includeRevoked, CancellationToken ct) =>
        {
            IQueryable<PlacementAccommodation> query = db.PlacementAccommodations.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(learner))
            {
                var needle = learner.Trim();
                var learnerId = needle.Contains('@')
                    ? (await FindLearnerAsync(db, null, needle, ct))?.Id
                    : needle;
                if (learnerId is null)
                {
                    return Results.Ok(Array.Empty<PlacementAccommodationDto>());
                }
                query = query.Where(a => a.LearnerUserId == learnerId);
            }
            if (includeRevoked != true)
            {
                query = query.Where(a => a.RevokedAt == null);
            }
            // ponytail: hard cap instead of paging; add paging if grants ever outgrow it.
            var grants = await query
                .OrderByDescending(a => a.ApprovedAt)
                .ThenByDescending(a => a.Id)
                .Take(200)
                .ToListAsync(ct);
            return Results.Ok(await BuildAccommodationDtosAsync(db, grants, ct));
        }).WithAdminRead("AdminLearnerRead");

        adminPlacement.MapPost("/accommodations/{id}/revoke", async Task<IResult> (
            HttpContext http, LearnerDbContext db, TimeProvider clock, string id,
            [FromBody] PlacementAccommodationRevokeRequest? request, CancellationToken ct) =>
        {
            var reason = request?.Reason?.Trim();
            if (string.IsNullOrEmpty(reason))
            {
                reason = null;
            }
            if (reason is { Length: > 200 })
            {
                throw ApiException.Validation("placement_accommodation_reason_too_long",
                    "The reason must be 200 characters or fewer.");
            }
            var grant = await db.PlacementAccommodations.SingleOrDefaultAsync(a => a.Id == id, ct)
                ?? throw ApiException.NotFound("placement_accommodation_not_found", "That accommodation does not exist.");

            // Idempotent: revoking an already-revoked grant changes nothing.
            if (grant.RevokedAt is null)
            {
                var now = clock.GetUtcNow();
                grant.RevokedByUserId = http.UserId();
                grant.RevokedByName = http.AdminName();
                grant.RevokedAt = now;
                grant.RevokedReason = reason;
                AddAccommodationAudit(db, http, "PlacementAccommodationRevoked", grant.Id, now, new
                {
                    learnerUserId = grant.LearnerUserId,
                    extraTimePercent = grant.ExtraTimePercent,
                    reason,
                });
                await db.SaveChangesAsync(ct);
            }
            return Results.Ok((await BuildAccommodationDtosAsync(db, [grant], ct))[0]);
        }).WithAdminWrite("AdminLearnerWrite");

        return app;
    }

    // ── Accommodation helpers ────────────────────────────────────────

    /// <summary>True when the engine's create-session <c>accommodations_applied</c>
    /// echo carries the grant's percent and approval id. <paramref name="echoed"/>
    /// is the raw echo (or "missing") for the failure log.</summary>
    private static bool AccommodationEchoMatches(JsonElement created, PlacementAccommodation grant, out string echoed)
    {
        echoed = "missing";
        if (created.ValueKind != JsonValueKind.Object
            || !created.TryGetProperty("accommodations_applied", out var applied))
        {
            return false;
        }
        echoed = applied.GetRawText();
        return applied.ValueKind == JsonValueKind.Object
            && applied.TryGetProperty("extra_time_percent", out var percent)
            && percent.ValueKind == JsonValueKind.Number
            && percent.TryGetDouble(out var percentValue)
            && percentValue == grant.ExtraTimePercent
            && applied.TryGetProperty("approval_id", out var approval)
            && approval.ValueKind == JsonValueKind.String
            && string.Equals(approval.GetString(), grant.Id, StringComparison.Ordinal);
    }

    /// <summary>Engine task ids match <c>^[A-Za-z0-9_.-]{1,64}$</c> (spelled out
    /// without a regex so a trailing newline can never slip past <c>$</c>).</summary>
    private static bool IsValidReviewTaskId(string taskId)
        => taskId.Length is >= 1 and <= 64
           && taskId.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-');

    /// <summary>The learner's active (unrevoked) extra-time grant, if any.</summary>
    private static Task<PlacementAccommodation?> ActiveAccommodationAsync(
        LearnerDbContext db, string learnerId, CancellationToken ct)
        => db.PlacementAccommodations
            .AsNoTracking()
            .Where(a => a.LearnerUserId == learnerId && a.RevokedAt == null)
            .OrderByDescending(a => a.ApprovedAt)
            .FirstOrDefaultAsync(ct);

    /// <summary>Resolve a learner by id and/or email (normalized-email lookup
    /// through the auth account, like the other admin user lookups). When both
    /// are supplied they must agree.</summary>
    private static async Task<LearnerUser?> FindLearnerAsync(
        LearnerDbContext db, string? userId, string? email, CancellationToken ct)
    {
        var id = string.IsNullOrWhiteSpace(userId) ? null : userId.Trim();
        var normalizedEmail = string.IsNullOrWhiteSpace(email) ? null : AuthEmailAddress.NormalizeOrThrow(email);
        if (id is null && normalizedEmail is null)
        {
            throw ApiException.Validation("placement_learner_required", "Provide learnerEmail or learnerUserId.");
        }

        var query = db.Users.AsNoTracking().Where(u => u.Role == ApplicationUserRoles.Learner);
        if (id is not null)
        {
            query = query.Where(u => u.Id == id);
        }
        if (normalizedEmail is not null)
        {
            query = query.Where(u => db.ApplicationUserAccounts.Any(a =>
                a.Id == u.AuthAccountId && a.NormalizedEmail == normalizedEmail && a.DeletedAt == null));
        }
        return await query.FirstOrDefaultAsync(ct);
    }

    private static void AddAccommodationAudit(
        LearnerDbContext db, HttpContext http, string action, string resourceId, DateTimeOffset at, object details)
        => db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            ActorId = http.UserId(),
            ActorName = http.AdminName(),
            Action = action,
            ResourceType = "PlacementAccommodation",
            ResourceId = resourceId,
            Details = JsonSupport.Serialize(details),
            OccurredAt = at,
        });

    private static async Task<List<PlacementAccommodationDto>> BuildAccommodationDtosAsync(
        LearnerDbContext db, IReadOnlyList<PlacementAccommodation> grants, CancellationToken ct)
    {
        if (grants.Count == 0)
        {
            return [];
        }
        var learnerIds = grants.Select(g => g.LearnerUserId).Distinct().ToList();
        var grantIds = grants.Select(g => g.Id).ToList();
        var learners = await db.Users
            .AsNoTracking()
            .Where(u => learnerIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Email, u.DisplayName })
            .ToDictionaryAsync(u => u.Id, ct);
        var uses = (await db.PlacementAccommodationUses
            .AsNoTracking()
            .Where(u => grantIds.Contains(u.AccommodationId))
            .OrderBy(u => u.AppliedAt)
            .ToListAsync(ct))
            .ToLookup(u => u.AccommodationId);

        return grants.Select(g =>
        {
            learners.TryGetValue(g.LearnerUserId, out var learner);
            return new PlacementAccommodationDto(
                g.Id,
                g.LearnerUserId,
                learner?.Email ?? string.Empty,
                learner?.DisplayName,
                g.ExtraTimePercent,
                g.Reference,
                g.RevokedAt is null ? "active" : "revoked",
                g.ApprovedByUserId,
                g.ApprovedByName,
                Iso(g.ApprovedAt),
                g.RevokedByUserId,
                g.RevokedByName,
                g.RevokedAt is { } revokedAt ? Iso(revokedAt) : null,
                g.RevokedReason,
                uses[g.Id]
                    .Select(u => new PlacementAccommodationUseDto(u.SessionId, Iso(u.AppliedAt), u.ExtraTimePercent))
                    .ToList());
        }).ToList();
    }

    private static string Iso(DateTimeOffset value)
        => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

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

    private static string AdminName(this HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.Name) ?? "Admin";
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
            return new ApiErrorResult(404, "placement_disabled", "The placement test is not available yet.");
        }

        // Controlled beta: enabled, but narrowed to the allowlist. Everyone
        // outside it still sees the route as absent (404) — no feature
        // enumeration, no half-available UX.
        if (snapshot.Placement.BetaOnly)
        {
            var email = context.HttpContext.User.FindFirstValue(ClaimTypes.Email)
                ?? context.HttpContext.User.FindFirst("email")?.Value;
            if (!snapshot.Placement.IsBetaEmail(email))
            {
                return new ApiErrorResult(404, "placement_disabled", "The placement test is not available yet.");
            }
        }

        return await next(context);
    }
}

// ── DTOs ─────────────────────────────────────────────────────────────

public sealed record PlacementCreateSessionRequest(string? TargetGoal, string? DeviceClass);
public sealed record PlacementSpeakingSubmitRequest(string? StoragePath);
public sealed record PlacementWritingTextRequest(string Text);
public sealed record PlacementUnitTechnicalRequest(string? Reason)
{
    /// <summary>Mirrors the engine's accepted reasons; anything else is
    /// rejected here rather than forwarded.</summary>
    public static readonly IReadOnlySet<string> AllowedReasons = new HashSet<string>(StringComparer.Ordinal)
    {
        "audio_unavailable", "audio_decode_error", "audio_zero_duration", "media_timeout",
    };
}
public sealed record PlacementHistoryItem(
    string Id, string SessionId, string RulesetVersion, string Status, DateTime CreatedAt);

/// <summary>Admin request to grant extra time. Identify the learner by
/// <c>learnerEmail</c> and/or <c>learnerUserId</c>.</summary>
public sealed record PlacementAccommodationGrantRequest(
    string? LearnerEmail, string? LearnerUserId, int ExtraTimePercent, string? Reference);
public sealed record PlacementAccommodationRevokeRequest(string? Reason);
public sealed record PlacementAccommodationUseDto(string SessionId, string AppliedAt, int ExtraTimePercent);
public sealed record PlacementAccommodationDto(
    string Id,
    string LearnerUserId,
    string LearnerEmail,
    string? LearnerName,
    int ExtraTimePercent,
    string? Reference,
    string Status,
    string ApprovedByUserId,
    string ApprovedByName,
    string ApprovedAt,
    string? RevokedByUserId,
    string? RevokedByName,
    string? RevokedAt,
    string? RevokedReason,
    IReadOnlyList<PlacementAccommodationUseDto> Uses);
