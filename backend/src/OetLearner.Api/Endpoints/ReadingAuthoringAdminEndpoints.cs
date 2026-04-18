using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Reading;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin endpoints for the Reading Authoring subsystem. See
/// <c>docs/READING-AUTHORING-PLAN.md</c>.
///
/// Authorisation: requires <c>AdminContentWrite</c> (already used for
/// content papers). Publish actions go through the existing paper publish
/// flow; this surface just builds the Reading structure.
/// </summary>
public static class ReadingAuthoringAdminEndpoints
{
    public static IEndpointRouteBuilder MapReadingAuthoringAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/papers/{paperId}/reading")
            .RequireAuthorization("AdminContentWrite")
            .RequireRateLimiting("PerUserWrite");

        // Full structure (admin view — includes correct answers)
        group.MapGet("/structure", async (
            string paperId, IReadingStructureService svc, CancellationToken ct) =>
        {
            var structure = await svc.GetAdminStructureAsync(paperId, ct);
            return Results.Ok(structure);
        });

        // Bootstrap canonical A/B/C parts (idempotent)
        group.MapPost("/ensure-canonical", async (
            string paperId, IReadingStructureService svc, CancellationToken ct) =>
        {
            await svc.EnsureCanonicalPartsAsync(paperId, ct);
            return Results.NoContent();
        });

        group.MapPut("/parts/{partCode}", async (
            string paperId, string partCode,
            ReadingPartUpsertDto dto,
            IReadingStructureService svc, HttpContext http, CancellationToken ct) =>
        {
            if (!Enum.TryParse<ReadingPartCode>(partCode, ignoreCase: true, out var code))
                return Results.BadRequest(new { error = "Invalid part code." });
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var part = await svc.UpsertPartAsync(new ReadingPartUpsert(
                paperId, code, dto.TimeLimitMinutes, dto.Instructions), adminId, ct);
            return Results.Ok(part);
        });

        group.MapPost("/texts", async (
            string paperId, ReadingTextUpsertDto dto,
            IReadingStructureService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var text = await svc.UpsertTextAsync(new ReadingTextUpsert(
                dto.Id, dto.ReadingPartId, dto.DisplayOrder, dto.Title, dto.Source,
                dto.BodyHtml, dto.WordCount, dto.TopicTag), adminId, ct);
            return Results.Ok(text);
        });

        group.MapDelete("/texts/{textId}", async (
            string textId, IReadingStructureService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var removed = await svc.RemoveTextAsync(textId, adminId, ct);
            return removed ? Results.NoContent() : Results.NotFound();
        });

        group.MapPost("/questions", async (
            string paperId, ReadingQuestionUpsertDto dto,
            IReadingStructureService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            try
            {
                var q = await svc.UpsertQuestionAsync(new ReadingQuestionUpsert(
                    dto.Id, dto.ReadingPartId, dto.ReadingTextId, dto.DisplayOrder,
                    dto.Points, dto.QuestionType, dto.Stem, dto.OptionsJson,
                    dto.CorrectAnswerJson, dto.AcceptedSynonymsJson, dto.CaseSensitive,
                    dto.ExplanationMarkdown, dto.SkillTag), adminId, ct);
                return Results.Ok(q);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapDelete("/questions/{questionId}", async (
            string questionId, IReadingStructureService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var removed = await svc.RemoveQuestionAsync(questionId, adminId, ct);
            return removed ? Results.NoContent() : Results.NotFound();
        });

        group.MapPost("/parts/{partId}/reorder-texts", async (
            string partId, ReorderDto dto,
            IReadingStructureService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            await svc.ReorderTextsAsync(partId, dto.OrderedIds, adminId, ct);
            return Results.NoContent();
        });

        group.MapPost("/parts/{partId}/reorder-questions", async (
            string partId, ReorderDto dto,
            IReadingStructureService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            await svc.ReorderQuestionsAsync(partId, dto.OrderedIds, adminId, ct);
            return Results.NoContent();
        });

        group.MapGet("/validate", async (
            string paperId, IReadingStructureService svc, CancellationToken ct) =>
        {
            var report = await svc.ValidatePaperAsync(paperId, ct);
            return Results.Ok(report);
        });

        return app;
    }
}

public sealed record ReadingPartUpsertDto(int? TimeLimitMinutes, string? Instructions);

public sealed record ReadingTextUpsertDto(
    string? Id,
    string ReadingPartId,
    int DisplayOrder,
    string Title,
    string? Source,
    string BodyHtml,
    int WordCount,
    string? TopicTag);

public sealed record ReadingQuestionUpsertDto(
    string? Id,
    string ReadingPartId,
    string? ReadingTextId,
    int DisplayOrder,
    int Points,
    ReadingQuestionType QuestionType,
    string Stem,
    string OptionsJson,
    string CorrectAnswerJson,
    string? AcceptedSynonymsJson,
    bool CaseSensitive,
    string? ExplanationMarkdown,
    string? SkillTag);

public sealed record ReorderDto(IReadOnlyList<string> OrderedIds);
