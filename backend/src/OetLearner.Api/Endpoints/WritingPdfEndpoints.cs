using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Watermarked Writing PDF download. Allows the attempt's owner (learner) plus
/// expert (tutor) and admin reviewers — all roles authenticated via JWT. The
/// service layer enforces ownership / privilege, rate limit and audit.
/// </summary>
public static class WritingPdfEndpoints
{
    public static IEndpointRouteBuilder MapWritingPdfEndpoints(this IEndpointRouteBuilder app)
    {
        // Direct: caller already knows the writing Attempt id.
        app.MapGet("/v1/writing/attempts/{attemptId}/pdf",
            async (
                string attemptId,
                HttpContext http,
                IWritingPdfService service,
                CancellationToken ct) =>
            {
                var (userId, isPrivileged) = ResolveCaller(http);
                var artifact = await service.GenerateAttemptPdfAsync(attemptId, userId, isPrivileged, ct);
                WritePdfHeaders(http, artifact.Filename);
                return Results.File(artifact.Bytes, "application/pdf", artifact.Filename);
            })
            .RequireAuthorization()
            .WithName("DownloadWritingAttemptPdf");

        // Convenience: resolve the writing section attempt from a mock attempt id.
        // Used by the mock report card which only carries mockAttemptId.
        app.MapGet("/v1/mocks/attempts/{mockAttemptId}/sections/writing/pdf",
            async (
                string mockAttemptId,
                HttpContext http,
                LearnerDbContext db,
                IWritingPdfService service,
                CancellationToken ct) =>
            {
                var (userId, isPrivileged) = ResolveCaller(http);

                var section = await db.Set<MockSectionAttempt>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s =>
                        s.MockAttemptId == mockAttemptId
                        && s.SubtestCode == "writing", ct)
                    ?? throw ApiException.NotFound("writing_section_not_found", "Writing section was not found for this mock.");

                if (string.IsNullOrWhiteSpace(section.ContentAttemptId))
                {
                    throw ApiException.NotFound(
                        "writing_attempt_not_submitted",
                        "The writing section has no submitted response to export.");
                }

                var artifact = await service.GenerateAttemptPdfAsync(
                    section.ContentAttemptId,
                    userId,
                    isPrivileged,
                    ct);
                WritePdfHeaders(http, artifact.Filename);
                return Results.File(artifact.Bytes, "application/pdf", artifact.Filename);
            })
            .RequireAuthorization()
            .WithName("DownloadMockWritingPdf");

        return app;
    }

    private static (string UserId, bool IsPrivileged) ResolveCaller(HttpContext http)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw ApiException.Unauthorized("authentication_required", "You must be signed in.");
        var role = http.User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        var isPrivileged = string.Equals(role, ApplicationUserRoles.Expert, StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, ApplicationUserRoles.Admin, StringComparison.OrdinalIgnoreCase);
        return (userId, isPrivileged);
    }

    private static void WritePdfHeaders(HttpContext http, string filename)
    {
        http.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{filename}\"";
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        http.Response.Headers["Cache-Control"] = "private, no-store";
    }
}

