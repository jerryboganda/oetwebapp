using System.Security.Claims;
using OetLearner.Api.Services.Progress;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin endpoints for the Progress v2 policy singleton. All routes under
/// <c>/v1/admin/progress-policy/*</c>. Reads require
/// <c>AdminProgressPolicyRead</c>; writes require
/// <c>AdminProgressPolicyWrite</c>. Every mutation writes an AuditEvent.
/// </summary>
public static class ProgressAdminEndpoints
{
    public static IEndpointRouteBuilder MapProgressAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var root = app.MapGroup("/v1/admin/progress-policy")
            .RequireAuthorization("AdminProgressPolicyRead")
            .RequireRateLimiting("PerUser");

        root.MapGet("/{examFamilyCode}", async (
            string examFamilyCode, IProgressService svc, CancellationToken ct) =>
        {
            var policy = await svc.GetPolicyAsync(examFamilyCode, ct);
            return Results.Ok(policy);
        });

        root.MapPut("/{examFamilyCode}", async (
            string examFamilyCode, ProgressPolicyUpdate dto,
            IProgressService svc, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            try
            {
                var policy = await svc.UpdatePolicyAsync(examFamilyCode, dto, adminId, ct);
                return Results.Ok(policy);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .RequireAuthorization("AdminProgressPolicyWrite")
        .RequireRateLimiting("PerUserWrite");

        return app;
    }
}
