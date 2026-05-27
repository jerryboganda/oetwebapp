using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Services.Remediation;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Learner-facing Remediation API. Surfaces the 7-day plan persisted in
/// RemediationTask plus the canonical rulebook context so the UI can show
/// why each task is recommended.
///
/// The audit flagged the Remediation module had NO backend endpoints; this
/// file closes that gap.
/// </summary>
public static class RemediationEndpoints
{
    public static IEndpointRouteBuilder MapRemediationEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1").RequireAuthorization("LearnerOnly");
        var rem = v1.MapGroup("/remediation");

        rem.MapGet("/plan", async (
            HttpContext http,
            [FromQuery] string? profession,
            IRemediationApiService svc,
            CancellationToken ct) =>
        {
            var prof = ParseProfession(profession);
            var plan = await svc.GetActivePlanAsync(ResolveUserId(http), prof, ct);
            return Results.Ok(plan);
        });

        rem.MapPost("/tasks/{taskId}/status", async (
            HttpContext http,
            string taskId,
            RemediationStatusUpdateRequest body,
            IRemediationApiService svc,
            CancellationToken ct) =>
        {
            try
            {
                var ok = await svc.MarkTaskAsync(ResolveUserId(http), taskId, body.Status, ct);
                return ok
                    ? Results.Ok(new { taskId, status = body.Status })
                    : Results.NotFound(new { error = $"Remediation task '{taskId}' not found for this learner." });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        rem.MapGet("/rulebook", (
            [FromQuery] string? profession,
            IRemediationApiService svc) =>
        {
            var prof = ParseProfession(profession);
            return Results.Ok(svc.GetRulebookContext(prof));
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

    /// <summary>
    /// Inline UserId resolver. Cannot use an extension method on HttpContext
    /// because every endpoint file in this namespace already defines its own
    /// `internal static UserId(this HttpContext)` and adding another would
    /// produce an ambiguous-call diagnostic.
    /// </summary>
    private static string ResolveUserId(HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}

public sealed record RemediationStatusUpdateRequest(string Status);
