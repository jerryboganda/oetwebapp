using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Listening;
using System.Security.Claims;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Listening V2 endpoint group. Mounted at <c>/v1/listening/v2/...</c>
/// alongside the legacy <c>/v1/listening-papers</c> tree so the V1 surface
/// keeps working through the migration window. Wave 2 §3.
/// </summary>
public static class ListeningV2Endpoints
{
    public sealed record AdvanceRequest(string ToState, string? ConfirmToken);
    public sealed record TechReadinessRequest(bool AudioOk, int DurationMs);
    public sealed record AudioResumeRequest(int CuePointMs);
    public sealed record SubmitRequest(Dictionary<string, string?>? Answers);
    public sealed record GradeRequest();
    public sealed record CreateClassRequest(string Name, string? Description);
    public sealed record AddMemberRequest(string MemberUserId);
    /// <summary>R08 annotations payload — opaque JSON capped at 64 KB. Owned
    /// by the frontend reducer that serialises highlights + strikethroughs
    /// per question.</summary>
    public sealed record SaveAnnotationsRequest(string? AnnotationsJson);
    public sealed record AnnotationsDto(string? AnnotationsJson);

    public sealed record CreateNoteRequest(string? ExtractId, int? TranscriptMs, string Text);
    public sealed record UpdateNoteRequest(string Text);
    public sealed record NoteDto(Guid Id, string? ExtractId, int? TranscriptMs, string Text, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

    public static IEndpointRouteBuilder MapListeningV2Endpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/listening/v2")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        var teacherGroup = app.MapGroup("/v1/listening/v2/teacher")
            .RequireAuthorization("TeachingStaffOnly")
            .RequireRateLimiting("PerUser");

        // ─── Session FSM ───
        group.MapGet("/attempts/{attemptId}/state", async (
            string attemptId, HttpContext http,
            ListeningSessionService session, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await session.GetStateAsync(attemptId, http.UserId(), ct));
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        })
        .WithName("GetListeningV2State")
        .WithSummary("Listening V2 — read FSM state for an attempt");

        // R06.10 — two-step confirm-token advance
        group.MapPost("/attempts/{attemptId}/advance", async (
            string attemptId, AdvanceRequest req, HttpContext http,
            ListeningSessionService session, CancellationToken ct) =>
        {
            try
            {
                var r = await session.AdvanceAsync(
                    attemptId, http.UserId(),
                    new AdvanceCommand(req.ToState, req.ConfirmToken), ct);
                return r.Outcome switch
                {
                    "applied" => Results.Ok(r),
                    "confirm-required" => Results.Json(r, statusCode: StatusCodes.Status412PreconditionFailed),
                    "rejected" => Results.UnprocessableEntity(r),
                    _ => Results.Ok(r),
                };
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("AdvanceListeningV2State")
        .WithSummary("Listening V2 — advance the FSM (two-step confirm protocol)")
        .Produces<AdvanceResultDto>(StatusCodes.Status200OK)
        .Produces<AdvanceResultDto>(StatusCodes.Status412PreconditionFailed)
        .Produces<AdvanceResultDto>(StatusCodes.Status422UnprocessableEntity)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/attempts/{attemptId}/tech-readiness", async (
            string attemptId, TechReadinessRequest req, HttpContext http,
            ListeningSessionService session, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await session.RecordTechReadinessAsync(
                    attemptId,
                    http.UserId(),
                    new TechReadinessCommand(req.AudioOk, req.DurationMs),
                    ct));
            }
            catch (ArgumentException) { return Results.BadRequest(); }
            catch (InvalidOperationException ex)
            {
                return Results.UnprocessableEntity(new { reason = "attempt-not-in-progress", detail = ex.Message });
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("RecordListeningV2TechReadiness")
        .WithSummary("Listening V2 — record R10 tech readiness before strict attempt start")
        .Produces<TechReadinessDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status422UnprocessableEntity);

        // R08 — persist learner highlights + strikethroughs. Frontend hook
        // `useListeningAnnotations` debounces 400 ms and PUTs the full
        // payload. Server enforces a 64 KB cap and JSON-shape validation.
        group.MapPut("/attempts/{attemptId}/annotations", async (
            string attemptId, SaveAnnotationsRequest req, HttpContext http,
            ListeningSessionService session, CancellationToken ct) =>
        {
            try
            {
                await session.SaveAnnotationsAsync(
                    attemptId, http.UserId(), req?.AnnotationsJson, ct);
                return Results.NoContent();
            }
            catch (ApiException ex)
            {
                return Results.Json(
                    new { errorCode = ex.ErrorCode, message = ex.Message },
                    statusCode: (int)ex.StatusCode);
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("SaveListeningV2Annotations")
        .WithSummary("Listening V2 — persist R08 highlights + strikethroughs (≤ 64 KB).")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);

        group.MapGet("/attempts/{attemptId}/annotations", async (
            string attemptId, HttpContext http,
            ListeningSessionService session, CancellationToken ct) =>
        {
            try
            {
                var json = await session.GetAnnotationsAsync(attemptId, http.UserId(), ct);
                return Results.Ok(new AnnotationsDto(json));
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        })
        .WithName("GetListeningV2Annotations")
        .WithSummary("Listening V2 — read the learner's saved annotations payload.");

        // ─── Attempt Notes ───
        group.MapGet("/attempts/{attemptId}/notes", async (
            string attemptId, HttpContext http,
            Data.LearnerDbContext db, CancellationToken ct) =>
        {
            var userId = http.UserId();
            var attempt = await db.Set<Domain.ListeningAttempt>()
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == attemptId, ct);
            if (attempt is null) return Results.NotFound();
            if (attempt.UserId != userId) return Results.Forbid();

            var rawNotes = await db.ListeningAttemptNotes
                .AsNoTracking()
                .Where(n => n.ListeningAttemptId == attemptId)
                .OrderBy(n => n.CreatedAt)
                .ToListAsync(ct);

            var notes = rawNotes.Select(n => new NoteDto(
                Guid.Parse(n.Id),
                string.IsNullOrEmpty(n.ListeningExtractId) ? null : n.ListeningExtractId,
                n.TranscriptMs,
                n.Text,
                n.CreatedAt,
                n.UpdatedAt)).ToList();

            return Results.Ok(notes);
        })
        .WithName("GetListeningV2Notes")
        .WithSummary("Listening V2 — list learner notes for an attempt");

        group.MapPost("/attempts/{attemptId}/notes", async (
            string attemptId, CreateNoteRequest req, HttpContext http,
            Data.LearnerDbContext db, CancellationToken ct) =>
        {
            var userId = http.UserId();
            var attempt = await db.Set<Domain.ListeningAttempt>()
                .FirstOrDefaultAsync(a => a.Id == attemptId, ct);
            if (attempt is null) return Results.NotFound();
            if (attempt.UserId != userId) return Results.Forbid();

            if (req.Text is null || req.Text.Length > 4096)
                return Results.BadRequest(new { error = "Text must be between 1 and 4096 characters." });

            var now = DateTimeOffset.UtcNow;
            var note = new Domain.ListeningAttemptNote
            {
                Id = Guid.NewGuid().ToString(),
                ListeningAttemptId = attemptId,
                ListeningExtractId = req.ExtractId ?? string.Empty,
                TranscriptMs = req.TranscriptMs,
                Text = req.Text,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.ListeningAttemptNotes.Add(note);

            db.Set<AuditEvent>().Add(new AuditEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                OccurredAt = now,
                ActorId = userId,
                ActorName = userId,
                Action = "listening.note.created",
                ResourceType = "ListeningAttemptNote",
                ResourceId = note.Id,
                Details = JsonSerializer.Serialize(new { attemptId, extractId = req.ExtractId, transcriptMs = req.TranscriptMs, textLength = req.Text.Length }),
            });

            await db.SaveChangesAsync(ct);

            var dto = new NoteDto(
                Guid.Parse(note.Id),
                string.IsNullOrEmpty(note.ListeningExtractId) ? null : note.ListeningExtractId,
                note.TranscriptMs,
                note.Text,
                note.CreatedAt,
                note.UpdatedAt);

            return Results.Created($"/v1/listening/v2/attempts/{attemptId}/notes/{note.Id}", dto);
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("CreateListeningV2Note")
        .WithSummary("Listening V2 — create a learner note on an attempt")
        .Produces<NoteDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/attempts/{attemptId}/notes/{noteId}", async (
            string attemptId, string noteId, UpdateNoteRequest req, HttpContext http,
            Data.LearnerDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(noteId, out _)) return Results.NotFound();
            var userId = http.UserId();
            var attempt = await db.Set<Domain.ListeningAttempt>()
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == attemptId, ct);
            if (attempt is null) return Results.NotFound();
            if (attempt.UserId != userId) return Results.Forbid();

            if (req.Text is null || req.Text.Length > 4096)
                return Results.BadRequest(new { error = "Text must be between 1 and 4096 characters." });

            var note = await db.ListeningAttemptNotes
                .FirstOrDefaultAsync(n => n.Id == noteId && n.ListeningAttemptId == attemptId, ct);
            if (note is null) return Results.NotFound();

            note.Text = req.Text;
            note.UpdatedAt = DateTimeOffset.UtcNow;

            db.Set<AuditEvent>().Add(new AuditEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                OccurredAt = note.UpdatedAt,
                ActorId = userId,
                ActorName = userId,
                Action = "listening.note.updated",
                ResourceType = "ListeningAttemptNote",
                ResourceId = noteId,
                Details = JsonSerializer.Serialize(new { attemptId, textLength = req.Text.Length }),
            });

            await db.SaveChangesAsync(ct);

            return Results.Ok(new NoteDto(
                Guid.Parse(note.Id),
                string.IsNullOrEmpty(note.ListeningExtractId) ? null : note.ListeningExtractId,
                note.TranscriptMs,
                note.Text,
                note.CreatedAt,
                note.UpdatedAt));
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("UpdateListeningV2Note")
        .WithSummary("Listening V2 — update the text of a learner note")
        .Produces<NoteDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/attempts/{attemptId}/notes/{noteId}", async (
            string attemptId, string noteId, HttpContext http,
            Data.LearnerDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(noteId, out _)) return Results.NotFound();
            var userId = http.UserId();
            var attempt = await db.Set<Domain.ListeningAttempt>()
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == attemptId, ct);
            if (attempt is null) return Results.NotFound();
            if (attempt.UserId != userId) return Results.Forbid();

            var note = await db.ListeningAttemptNotes
                .FirstOrDefaultAsync(n => n.Id == noteId && n.ListeningAttemptId == attemptId, ct);
            if (note is null) return Results.NotFound();

            db.ListeningAttemptNotes.Remove(note);

            db.Set<AuditEvent>().Add(new AuditEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                OccurredAt = DateTimeOffset.UtcNow,
                ActorId = userId,
                ActorName = userId,
                Action = "listening.note.deleted",
                ResourceType = "ListeningAttemptNote",
                ResourceId = noteId,
                Details = JsonSerializer.Serialize(new { attemptId }),
            });

            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("DeleteListeningV2Note")
        .WithSummary("Listening V2 — delete a learner note")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/attempts/{attemptId}/audio-resume", async (
            string attemptId, AudioResumeRequest req, HttpContext http,
            ListeningSessionService session, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await session.AudioResumeAsync(
                    attemptId, http.UserId(), req.CuePointMs, ct));
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("AudioResumeListeningV2")
        .WithSummary("Listening V2 — resume audio after a transient disconnection");

        group.MapPut("/attempts/{attemptId}/answers/{questionId}", async (
            string attemptId,
            string questionId,
            ListeningAnswerSaveRequest req,
            HttpContext http,
            ListeningLearnerService learner,
            CancellationToken ct) =>
        {
            await learner.SaveAnswerAsync(http.UserId(), attemptId, questionId, req, ct);
            return Results.NoContent();
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("SaveListeningV2Answer")
        .WithSummary("Listening V2 — save one answer through the relational attempt facade")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/attempts/{attemptId}/submit", async (
            string attemptId,
            SubmitRequest? req,
            HttpContext http,
            ListeningLearnerService learner,
            ListeningPathwayProgressService pathway,
            CancellationToken ct) =>
        {
            var userId = http.UserId();
            var review = await learner.SubmitAsync(userId, attemptId, req?.Answers, ct);
            await pathway.RecomputeAsync(userId, ct);
            return Results.Ok(review);
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("SubmitListeningV2Attempt")
        .WithSummary("Listening V2 — submit final answers and return the full learner review DTO")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        // ─── Grading ───
        group.MapPost("/attempts/{attemptId}/grade", async (
            string attemptId, HttpContext http,
            ListeningGradingService grading,
            ListeningPathwayProgressService pathway,
            CancellationToken ct) =>
        {
            try
            {
                var result = await grading.GradeAsync(attemptId, http.UserId(), ct);
                await pathway.RecomputeAsync(http.UserId(), ct);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("GradeListeningV2Attempt")
        .WithSummary("Listening V2 — grade an attempt + recompute pathway");

        // ─── Pathway ───
        group.MapGet("/me/pathway", async (
            HttpContext http,
            Data.LearnerDbContext db,
            IContentEntitlementService entitlements,
            ListeningPathwayProgressService pathway,
            CancellationToken ct) =>
        {
            var userId = http.UserId();
            await pathway.RecomputeAsync(userId, ct);
            var launchPaperId = await ResolvePathwayLaunchPaperIdAsync(db, entitlements, userId, ct);
            var rows = await db.ListeningPathwayProgress
                .AsNoTracking()
                .Where(p => p.UserId == userId)
                .ToListAsync(ct);
            // Project to a learner-safe view that ships only the canonical
            // 12 stages in declared order.
            var byStage = rows.ToDictionary(r => r.StageCode, r => r);
            var view = ListeningPathwayProgressService.PathwayStages.Select(stage =>
            {
                byStage.TryGetValue(stage, out var row);
                return new
                {
                    stage,
                    status = (row?.Status ?? Domain.ListeningPathwayStageStatus.Locked).ToString(),
                    scaledScore = row?.ScaledScore,
                    completedAt = row?.CompletedAt,
                    actionHref = ListeningPathwayLaunchTargets.BuildActionHref(
                        stage,
                        row?.Status ?? Domain.ListeningPathwayStageStatus.Locked,
                        launchPaperId),
                };
            });
            return Results.Ok(view);
        })
        .WithName("GetListeningV2Pathway")
        .WithSummary("Listening V2 — 12-stage pathway snapshot");

        // ─── Teacher classes (cross-skill) ───
        teacherGroup.MapGet("/classes", async (
            HttpContext http, TeacherClassService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListMineAsync(http.UserId(), ct)))
            .WithName("ListMyTeacherClasses");

        teacherGroup.MapPost("/classes", async (
            CreateClassRequest req, HttpContext http,
            TeacherClassService svc, CancellationToken ct) =>
            Results.Ok(await svc.CreateAsync(http.UserId(), req.Name, req.Description, ct)))
            .RequireRateLimiting("PerUserWrite")
            .WithName("CreateTeacherClass");

        teacherGroup.MapDelete("/classes/{classId}", async (
            string classId, HttpContext http,
            TeacherClassService svc, CancellationToken ct) =>
        {
            try { await svc.DeleteAsync(http.UserId(), classId, ct); return Results.NoContent(); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("DeleteTeacherClass");

        teacherGroup.MapPost("/classes/{classId}/members", async (
            string classId, AddMemberRequest req, HttpContext http,
            TeacherClassService svc, CancellationToken ct) =>
        {
            try { await svc.AddMemberAsync(http.UserId(), classId, req.MemberUserId, ct); return Results.NoContent(); }
            catch (ArgumentException) { return Results.BadRequest(); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("AddTeacherClassMember");

        teacherGroup.MapDelete("/classes/{classId}/members/{memberUserId}", async (
            string classId, string memberUserId, HttpContext http,
            TeacherClassService svc, CancellationToken ct) =>
        {
            try { await svc.RemoveMemberAsync(http.UserId(), classId, memberUserId, ct); return Results.NoContent(); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("RemoveTeacherClassMember");

        teacherGroup.MapGet("/classes/{classId}/analytics", async (
            string classId,
            int? days,
            HttpContext http,
            IListeningAnalyticsService analytics,
            CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await analytics.GetClassAnalyticsAsync(
                    http.UserId(), classId, days ?? 30, ct));
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        })
        .WithName("GetTeacherClassListeningAnalytics")
        .WithSummary("Listening V2 — owner-scoped teacher class analytics");

        return app;
    }

    private static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");

    private static async Task<string?> ResolvePathwayLaunchPaperIdAsync(
        Data.LearnerDbContext db,
        IContentEntitlementService entitlements,
        string userId,
        CancellationToken ct)
    {
        var profession = await db.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.ActiveProfessionId)
            .SingleOrDefaultAsync(ct);

        var candidates = await db.ContentPapers.AsNoTracking()
            .Where(p => p.Status == ContentStatus.Published
                && p.SubtestCode == "listening"
                && (p.AppliesToAllProfessions
                    || (!string.IsNullOrWhiteSpace(profession) && p.ProfessionId == profession)))
            .OrderByDescending(p => p.Priority)
            .ThenByDescending(p => p.PublishedAt)
            .ThenBy(p => p.Title)
            .Take(20)
            .ToListAsync(ct);

        foreach (var candidate in candidates)
        {
            // A listening paper is a valid Part A/B/C launch target when it
            // exposes graded questions in EITHER store the full-exam path
            // accepts (see ListeningLearnerService.BuildPaperSourceAsync): the
            // relational ListeningQuestions table OR JSON-authored questions on
            // ContentPaper.ExtractedTextJson. Requiring only the relational
            // store made a JSON-authored paper play as a full exam yet stay
            // invisible to part practice (2026-07-05 prod bug — every part
            // showed "No Part X listening paper is available yet").
            var hasQuestions = await db.ListeningQuestions.AnyAsync(q => q.PaperId == candidate.Id, ct)
                || PaperHasJsonAuthoredQuestions(candidate.ExtractedTextJson);
            if (!hasQuestions)
            {
                continue;
            }

            try
            {
                var access = await entitlements.AllowAccessAsync(userId, candidate, ct);
                if (access.Allowed)
                {
                    return candidate.Id;
                }
            }
            catch
            {
                // Keep the pathway snapshot available even if one candidate's
                // entitlement lookup fails; attempt start still enforces access.
            }
        }

        return null;
    }

    /// <summary>
    /// True when a paper carries JSON-authored listening questions on
    /// <see cref="Domain.ContentPaper.ExtractedTextJson"/>, mirroring the JSON
    /// fallback branch of <c>ListeningLearnerService.BuildPaperSourceAsync</c>
    /// (keys <c>listeningQuestions</c> / <c>questions</c>). Malformed or empty
    /// JSON counts as "no questions" rather than throwing.
    /// </summary>
    private static bool PaperHasJsonAuthoredQuestions(string? extractedTextJson)
    {
        if (string.IsNullOrWhiteSpace(extractedTextJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(extractedTextJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var key in new[] { "listeningQuestions", "questions" })
            {
                if (document.RootElement.TryGetProperty(key, out var array)
                    && array.ValueKind == JsonValueKind.Array
                    && array.GetArrayLength() > 0)
                {
                    return true;
                }
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
