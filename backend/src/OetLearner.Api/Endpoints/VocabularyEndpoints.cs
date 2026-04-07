using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class VocabularyEndpoints
{
    public static IEndpointRouteBuilder MapVocabularyEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1").RequireAuthorization("LearnerOnly");
        var vocab = v1.MapGroup("/vocabulary");

        // ── Term browse (public within auth) ────────────────────────────
        vocab.MapGet("/terms", async (
            [FromQuery] string? examTypeCode,
            [FromQuery] string? category,
            [FromQuery] string? search,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            VocabularyService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetTermsAsync(examTypeCode, category, search, page <= 0 ? 1 : page, pageSize <= 0 ? 20 : pageSize, ct)));

        vocab.MapGet("/terms/{termId}", async (string termId, VocabularyService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetTermAsync(termId, ct)));

        // ── Learner vocabulary list ───────────────────────────────────────
        vocab.MapGet("/my-list", async (HttpContext http, [FromQuery] string? mastery, VocabularyService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetMyVocabularyAsync(http.UserId(), mastery, ct)));

        vocab.MapPost("/my-list/{termId}", async (HttpContext http, string termId, VocabularyService svc, CancellationToken ct) =>
            Results.Ok(await svc.AddToMyVocabularyAsync(http.UserId(), termId, ct)));

        vocab.MapDelete("/my-list/{termId}", async (HttpContext http, string termId, VocabularyService svc, CancellationToken ct) =>
            Results.Ok(await svc.RemoveFromMyVocabularyAsync(http.UserId(), termId, ct)));

        // ── Flashcards ───────────────────────────────────────────────────
        vocab.MapGet("/flashcards/due", async (HttpContext http, [FromQuery] int limit, VocabularyService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetDueFlashcardsAsync(http.UserId(), limit <= 0 ? 20 : limit, ct)));

        vocab.MapPost("/flashcards/{lvId}/review", async (HttpContext http, Guid lvId, FlashcardReviewRequest request, VocabularyService svc, CancellationToken ct) =>
            Results.Ok(await svc.SubmitFlashcardReviewAsync(http.UserId(), lvId, request.Quality, ct)));

        // ── Quizzes ──────────────────────────────────────────────────────
        vocab.MapGet("/quiz", async (HttpContext http, [FromQuery] int count, VocabularyService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetQuizAsync(http.UserId(), count <= 0 ? 10 : count, ct)));

        vocab.MapPost("/quiz/submit", async (HttpContext http, VocabQuizSubmission submission, VocabularyService svc, CancellationToken ct) =>
            Results.Ok(await svc.SubmitQuizAsync(http.UserId(), submission, ct)));

        return app;
    }
}

public record FlashcardReviewRequest(int Quality);

file static class VocabularyHttpContextExtensions
{
    internal static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
