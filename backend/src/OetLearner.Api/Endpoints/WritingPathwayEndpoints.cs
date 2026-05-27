using System.Security.Claims;
using OetLearner.Api.Contracts;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Endpoints;

public static class WritingPathwayEndpoints
{
    public static IEndpointRouteBuilder MapWritingPathwayEndpoints(this IEndpointRouteBuilder app)
    {
        MapRoutes(app.MapGroup("/v1/writing-pathway").RequireAuthorization("LearnerOnly").RequireRateLimiting("PerUser"));
        return app;
    }

    private static void MapRoutes(RouteGroupBuilder group)
    {
        group.MapGet("/profile", async (HttpContext http, IWritingLearnerPathwayService service, CancellationToken ct)
            => Results.Ok(await service.GetProfileAsync(http.UserId(), ct)));

        group.MapPost("/onboarding", async (WritingStartOnboardingRequest request, HttpContext http, IWritingLearnerPathwayService service, CancellationToken ct)
            => Results.Ok(await service.SaveOnboardingAsync(http.UserId(), request, ct)));

        group.MapGet("/pathway", async (HttpContext http, IWritingLearnerPathwayService service, CancellationToken ct)
            => Results.Ok(await service.GetPathwayAsync(http.UserId(), ct)));

        group.MapGet("/plan/today", async (HttpContext http, IWritingLearnerPathwayService service, CancellationToken ct)
            => Results.Ok(await service.GetTodayPlanAsync(http.UserId(), ct)));

        group.MapGet("/today", async (HttpContext http, IWritingLearnerPathwayService service, CancellationToken ct)
            => Results.Ok(await service.GetTodayPlanAsync(http.UserId(), ct)));

        group.MapPost("/plan/items/{id:guid}/start", async (Guid id, HttpContext http, IWritingLearnerPathwayService service, CancellationToken ct) =>
        {
            await service.StartPlanItemAsync(http.UserId(), id, ct);
            return Results.NoContent();
        });

        group.MapPost("/plan/items/{id:guid}/complete", async (Guid id, HttpContext http, IWritingLearnerPathwayService service, CancellationToken ct) =>
        {
            await service.CompletePlanItemAsync(http.UserId(), id, ct);
            return Results.NoContent();
        });

        group.MapPost("/plan/items/{id:guid}/skip", async (Guid id, WritingPlanItemSkipRequest? request, HttpContext http, IWritingLearnerPathwayService service, CancellationToken ct) =>
        {
            await service.SkipPlanItemAsync(http.UserId(), id, request?.Reason, ct);
            return Results.NoContent();
        });

        group.MapGet("/canon", async (HttpContext http, IWritingLearnerPathwayService service, CancellationToken ct, string? search, string? severity)
            => Results.Ok(await service.GetCanonAsync(http.UserId(), search, severity, ct)));

        group.MapGet("/lessons", async (HttpContext http, IWritingLessonService service, CancellationToken ct)
            => Results.Ok(await service.ListLessonsAsync(http.UserId(), ct)));

        group.MapGet("/lessons/{slug}", async (string slug, HttpContext http, IWritingLessonService service, CancellationToken ct) =>
        {
            var lesson = await service.GetLessonAsync(http.UserId(), slug, ct);
            return lesson is null ? Results.NotFound() : Results.Ok(lesson);
        });

        group.MapPost("/lessons/{slug}/progress", async (string slug, WritingLessonProgressRequest request, HttpContext http, IWritingLessonService service, CancellationToken ct)
            => Results.Ok(await service.UpdateProgressAsync(http.UserId(), slug, request, ct)));

        group.MapGet("/drills", async (HttpContext http, IWritingDrillService service, CancellationToken ct, string? skill)
            => Results.Ok(await service.ListDrillsAsync(http.UserId(), skill, ct)));

        group.MapGet("/drills/{id:guid}", async (Guid id, HttpContext http, IWritingDrillService service, CancellationToken ct) =>
        {
            var drill = await service.GetDrillAsync(http.UserId(), id, ct);
            return drill is null ? Results.NotFound() : Results.Ok(drill);
        });

        group.MapPost("/drills/{id:guid}/attempts", async (Guid id, WritingDrillAttemptRequest request, HttpContext http, IWritingDrillService service, CancellationToken ct)
            => Results.Ok(await service.SubmitDrillAsync(http.UserId(), id, request, ct)));

        group.MapGet("/case-note-drills", async (HttpContext http, IWritingCaseNoteDrillService service, CancellationToken ct) =>
        {
            var rows = await service.ListAsync(http.UserId(), null, null, ct);
            return Results.Ok(rows.Select(row => new WritingCaseNoteDrillSummaryResponse(
                row.Id,
                row.Title,
                row.Profession,
                row.LetterType,
                row.Difficulty,
                row.Sentences.Count,
                0)));
        });

        group.MapGet("/case-note-drills/{id:guid}", async (Guid id, HttpContext http, IWritingCaseNoteDrillService service, CancellationToken ct) =>
        {
            var drill = await service.GetAsync(http.UserId(), id, ct);
            return drill is null ? Results.NotFound() : Results.Ok(new WritingCaseNoteDrillDetailResponse(
                drill.Id,
                drill.Title,
                drill.Profession,
                drill.LetterType,
                drill.Format,
                drill.CaseNotesMarkdown,
                drill.Difficulty,
                drill.Sentences.Count,
                drill.Sentences.Select(sentence => new WritingCaseNoteDrillSentenceResponse(sentence.Id, sentence.Ordinal, sentence.SentenceText)).ToList(),
                0));
        });

        group.MapPost("/case-note-drills/{id:guid}/attempts", async (Guid id, OetLearner.Api.Contracts.WritingCaseNoteDrillAttemptRequest request, HttpContext http, IWritingCaseNoteDrillService service, CancellationToken ct) =>
        {
            var result = await service.SubmitAttemptAsync(http.UserId(), id, new OetLearner.Api.Services.Writing.WritingCaseNoteDrillAttemptRequest(request.Responses, request.TimeSpentSeconds), ct);
            return Results.Ok(new WritingCaseNoteDrillAttemptResponse(
                result.AttemptId,
                result.CorrectCount,
                result.TotalCount,
                result.ScorePercent,
                result.PerSentence.Select(entry => new WritingCaseNoteDrillFeedbackResponse(entry.SentenceId, entry.IsCorrect, entry.CorrectLabel, entry.Rationale)).ToList()));
        });
    }
}

file static class WritingPathwayHttpContextExtensions
{
    internal static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}