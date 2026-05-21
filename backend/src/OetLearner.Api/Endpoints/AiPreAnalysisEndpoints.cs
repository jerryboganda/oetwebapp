using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Services.Speaking;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin-only triggers for the AI pre-analysis pipelines. These endpoints
/// are intentionally lightweight: they kick off a single pre-fill pass on
/// demand and return the heuristic bands so an admin or expert can
/// inspect the suggested defaults before the reviewer opens the draft.
///
/// IMPORTANT: results returned here are advisory pre-fills, not
/// authoritative scores. The expert reviewer remains the source of truth.
///
/// Routes:
///   POST /v1/admin/ai-pre-analysis/speaking/{sessionId}
///   POST /v1/admin/ai-pre-analysis/writing/{attemptId}
///
/// Wire-up note: <see cref="MapAiPreAnalysisEndpoints"/> is defined here
/// but NOT registered in <c>Program.cs</c> from this agent's side —
/// Agent W2-A owns service registration AND the corresponding
/// <c>app.MapAiPreAnalysisEndpoints()</c> call after the other admin
/// endpoints. (See plan file
/// <c>~/.claude/plans/1-what-the-oet-radiant-axolotl.md</c>.)
/// </summary>
public static class AiPreAnalysisEndpoints
{
    /// <summary>
    /// Register the AI pre-analysis admin endpoints.
    ///
    /// NOTE FOR AGENT W2-A: please invoke <c>app.MapAiPreAnalysisEndpoints();</c>
    /// in <c>Program.cs</c> alongside the other admin endpoint maps. This
    /// agent (W2-E) intentionally does not touch <c>Program.cs</c>.
    /// </summary>
    public static WebApplication MapAiPreAnalysisEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/v1/admin/ai-pre-analysis")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser");

        admin.MapPost("/speaking/{sessionId}", async (
                string sessionId,
                [FromServices] ISpeakingPreAnalysisService service,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    return Results.BadRequest(new
                    {
                        error = "sessionId is required.",
                        code = "validation_error",
                    });
                }

                var result = await service.AnalyseAsync(sessionId, ct);
                return Results.Ok(new
                {
                    sessionId,
                    authoritative = false,
                    bands = new
                    {
                        fluency = result.FluencyScore,
                        intelligibility = result.IntelligibilityScore,
                        appropriateness = result.AppropriatenessScore,
                        patientPerspective = result.PatientPerspectiveScore,
                    },
                    notes = result.Notes,
                });
            })
            .WithAdminWrite("AdminAiConfig")
            .WithName("AdminAiPreAnalysisSpeaking")
            .WithSummary("Run Speaking pre-analysis (pre-fill only, not authoritative).");

        admin.MapPost("/writing/{attemptId}", async (
                string attemptId,
                [FromServices] IWritingPreScoreService service,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(attemptId))
                {
                    return Results.BadRequest(new
                    {
                        error = "attemptId is required.",
                        code = "validation_error",
                    });
                }

                var result = await service.ScoreAsync(attemptId, ct);
                return Results.Ok(new
                {
                    attemptId,
                    authoritative = false,
                    bands = new
                    {
                        purpose = result.Purpose,
                        content = result.Content,
                        concisenessAndClarity = result.ConcisenessAndClarity,
                        genreAndStyle = result.GenreAndStyle,
                        organisationAndLayout = result.OrganisationAndLayout,
                        language = result.Language,
                    },
                    rationale = result.Rationale,
                });
            })
            .WithAdminWrite("AdminAiConfig")
            .WithName("AdminAiPreAnalysisWriting")
            .WithSummary("Run Writing pre-score (pre-fill only, not authoritative).");

        return app;
    }
}
