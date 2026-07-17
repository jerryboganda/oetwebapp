using System.Security.Claims;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Security;
using OetWithDrHesham.Api.Services;

namespace OetWithDrHesham.Api.Endpoints;

public static class SpeakingPdfEndpoints
{
    public static IEndpointRouteBuilder MapSpeakingPdfEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/speaking/evaluations/{evaluationId}/pdf",
            async (
                string evaluationId,
                HttpContext http,
                ISpeakingPdfService service,
                CancellationToken ct) =>
            {
                var (userId, isExpertReviewer, isAdminReviewer) = ResolveCaller(http);
                var artifact = await service.GenerateEvaluationPdfAsync(evaluationId, userId, isExpertReviewer, isAdminReviewer, ct);
                WritePdfHeaders(http, artifact.Filename);
                return Results.File(artifact.Bytes, "application/pdf", artifact.Filename);
            })
            .RequireAuthorization()
            .WithName("DownloadSpeakingEvaluationPdf");

        return app;
    }

    private static (string UserId, bool IsExpertReviewer, bool IsAdminReviewer) ResolveCaller(HttpContext http)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw ApiException.Unauthorized("authentication_required", "You must be signed in.");
        var role = http.User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        var isExpertReviewer = string.Equals(role, ApplicationUserRoles.Expert, StringComparison.OrdinalIgnoreCase);
        var permissionsClaim = http.User.FindFirstValue(AuthTokenService.AdminPermissionsClaimType);
        var isAdminReviewer = string.Equals(role, ApplicationUserRoles.Admin, StringComparison.OrdinalIgnoreCase)
            && AdminPermissionEvaluator.HasAny(
                permissionsClaim,
                AdminPermissions.SystemAdmin,
                AdminPermissions.ReviewOps,
                AdminPermissions.LearnerRead);
        return (userId, isExpertReviewer, isAdminReviewer);
    }

    private static void WritePdfHeaders(HttpContext http, string filename)
    {
        http.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{filename}\"";
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        http.Response.Headers["Cache-Control"] = "private, no-store";
    }
}