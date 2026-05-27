using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Services.Grammar;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Learner-facing Grammar API. Surfaces the canonical rulebook content from
/// rulebooks/grammar/{profession}/rulebook.v1.json so the existing app/grammar/
/// UI is no longer reading static stub content.
///
/// The 2026-05-27 audit flagged that this endpoint was MISSING despite 6
/// profession rulebooks existing on disk. This file closes that gap.
/// </summary>
public static class GrammarEndpoints
{
    public static IEndpointRouteBuilder MapGrammarEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1").RequireAuthorization("LearnerOnly");
        var grammar = v1.MapGroup("/grammar");

        grammar.MapGet("/topics", (
            [FromQuery] string? profession,
            IGrammarRulebookService svc) =>
        {
            var prof = ParseProfession(profession);
            return Results.Ok(svc.GetTopics(prof));
        });

        grammar.MapGet("/topics/{sectionId}/lessons", (
            string sectionId,
            [FromQuery] string? profession,
            IGrammarRulebookService svc) =>
        {
            var prof = ParseProfession(profession);
            try
            {
                return Results.Ok(svc.GetLessonsForTopic(prof, sectionId));
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        grammar.MapGet("/lessons/{ruleId}", (
            string ruleId,
            [FromQuery] string? profession,
            IGrammarRulebookService svc) =>
        {
            var prof = ParseProfession(profession);
            var lesson = svc.GetLesson(prof, ruleId);
            return lesson is null
                ? Results.NotFound(new { error = $"Grammar lesson '{ruleId}' not found for profession {prof}." })
                : Results.Ok(lesson);
        });

        return app;
    }

    private static ExamProfession ParseProfession(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ExamProfession.Medicine;
        var parts = raw.Split('-', StringSplitOptions.RemoveEmptyEntries);
        var pascal = string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));
        return Enum.TryParse<ExamProfession>(pascal, ignoreCase: true, out var v) ? v : ExamProfession.Medicine;
    }
}
