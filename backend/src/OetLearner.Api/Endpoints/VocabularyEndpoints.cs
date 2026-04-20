using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Contracts;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class VocabularyEndpoints
{
    public static IEndpointRouteBuilder MapVocabularyEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1").RequireAuthorization("LearnerOnly");
        var vocab = v1.MapGroup("/vocabulary");

        // ── Term browse (auth required) ──────────────────────────────────
        vocab.MapGet("/terms", async (
            [FromQuery] string? examTypeCode,
            [FromQuery] string? category,
            [FromQuery] string? profession,
            [FromQuery] string? search,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            VocabularyService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetTermsAsync(
                string.IsNullOrWhiteSpace(examTypeCode) ? "oet" : examTypeCode,
                category, profession, search,
                page <= 0 ? 1 : page,
                pageSize <= 0 ? 20 : Math.Min(pageSize, 100),
                ct)));

        vocab.MapGet("/terms/lookup", async (
            [FromQuery] string q,
            [FromQuery] string? examTypeCode,
            VocabularyService svc, CancellationToken ct) =>
            Results.Ok(await svc.LookupAsync(q, examTypeCode, ct)));

        vocab.MapGet("/terms/{termId}", async (string termId, VocabularyService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetTermAsync(termId, ct)));

        vocab.MapGet("/categories", async (
            [FromQuery] string? examTypeCode,
            [FromQuery] string? profession,
            VocabularyService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetCategoriesAsync(examTypeCode, profession, ct)));

        // ── Learner list ─────────────────────────────────────────────────
        vocab.MapGet("/my-list", async (HttpContext http, [FromQuery] string? mastery, VocabularyService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetMyVocabularyAsync(http.UserId(), mastery, ct)));

        vocab.MapPost("/my-list/{termId}", async (HttpContext http, string termId, AddToMyVocabularyRequest? body, VocabularyService svc, CancellationToken ct) =>
        {
            var isPremium = http.IsPremium();
            return Results.Ok(await svc.AddToMyVocabularyAsync(http.UserId(), termId, body?.SourceRef, isPremium, ct));
        });

        vocab.MapDelete("/my-list/{termId}", async (HttpContext http, string termId, VocabularyService svc, CancellationToken ct) =>
            Results.Ok(await svc.RemoveFromMyVocabularyAsync(http.UserId(), termId, ct)));

        // ── Flashcards ───────────────────────────────────────────────────
        vocab.MapGet("/flashcards/due", async (HttpContext http, [FromQuery] int limit, VocabularyService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetDueFlashcardsAsync(http.UserId(), limit <= 0 ? 20 : Math.Min(limit, 100), ct)));

        vocab.MapPost("/flashcards/{lvId}/review", async (HttpContext http, Guid lvId, FlashcardReviewRequestV2 request, VocabularyService svc, CancellationToken ct) =>
            Results.Ok(await svc.SubmitFlashcardReviewAsync(http.UserId(), lvId, request.Quality, ct)));

        // ── Daily set ────────────────────────────────────────────────────
        vocab.MapGet("/daily-set", async (HttpContext http, [FromQuery] int count, VocabularyService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetDailySetAsync(http.UserId(), count <= 0 ? 10 : Math.Min(count, 50), ct)));

        // ── Stats ────────────────────────────────────────────────────────
        vocab.MapGet("/stats", async (HttpContext http, VocabularyService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetStatsAsync(http.UserId(), ct)));

        // ── Quiz ─────────────────────────────────────────────────────────
        vocab.MapGet("/quiz", async (HttpContext http, [FromQuery] int count, [FromQuery] string? format, VocabularyService svc, CancellationToken ct) =>
        {
            var resolvedFormat = string.IsNullOrWhiteSpace(format) ? "definition_match" : format.ToLowerInvariant();
            if (!http.IsPremium() && resolvedFormat != "definition_match")
            {
                return Results.Json(new
                {
                    errorCode = "VOCAB_PREMIUM_REQUIRED",
                    error = "This quiz format is part of the premium plan.",
                    freeFormat = "definition_match",
                }, statusCode: 402);
            }
            return Results.Ok(await svc.GetQuizAsync(http.UserId(), count <= 0 ? 10 : Math.Min(count, 25), resolvedFormat, ct));
        });

        vocab.MapPost("/quiz/submit", async (HttpContext http, VocabQuizSubmissionV2 submission, VocabularyService svc, CancellationToken ct) =>
            Results.Ok(await svc.SubmitQuizAsync(http.UserId(), submission, ct)));

        vocab.MapGet("/quiz/history", async (HttpContext http, [FromQuery] int page, [FromQuery] int pageSize, VocabularyService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetQuizHistoryAsync(
                http.UserId(),
                page <= 0 ? 1 : page,
                pageSize <= 0 ? 20 : Math.Min(pageSize, 100),
                ct)));

        // ── Gloss (grounded AI) ──────────────────────────────────────────
        vocab.MapPost("/gloss", async (HttpContext http, VocabularyGlossRequest request, VocabularyGlossService svc, CancellationToken ct) =>
            Results.Ok(await svc.GlossAsync(http.UserId(), request, ct)))
            .RequireRateLimiting("PerUserWrite");

        return app;
    }
}

file static class VocabularyHttpContextExtensions
{
    internal static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");

    /// <summary>
    /// Heuristic premium check that tolerates environments where the premium
    /// claim isn't wired yet. Accepts any of:
    ///   - claim "subscription_tier" in {premium, pro, sponsor}
    ///   - claim "entitlements" containing "vocabulary_premium"
    ///   - role "admin" (admins bypass the premium gate)
    /// Falls back to <c>false</c> when nothing matches.
    /// </summary>
    internal static bool IsPremium(this HttpContext httpContext)
    {
        var user = httpContext.User;
        if (user.IsInRole("admin") || user.IsInRole("Admin")) return true;
        var tier = user.FindFirstValue("subscription_tier")?.ToLowerInvariant();
        if (tier is "premium" or "pro" or "sponsor" or "paid") return true;
        var entitlements = user.FindAll("entitlement").Select(c => c.Value)
            .Concat(user.FindAll("entitlements").SelectMany(c => (c.Value ?? "").Split(',')))
            .Select(s => s.Trim())
            .ToList();
        return entitlements.Contains("vocabulary_premium", StringComparer.OrdinalIgnoreCase);
    }
}
