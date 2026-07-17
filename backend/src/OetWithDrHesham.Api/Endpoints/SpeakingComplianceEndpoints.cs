using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OetWithDrHesham.Api.Contracts;
using OetWithDrHesham.Api.Services.Speaking;

namespace OetWithDrHesham.Api.Endpoints;

// Phase 7 (B.8) of the OET Speaking module plan.
//
// All five compliance routes:
//   * POST   /v1/speaking/consents
//   * GET    /v1/speaking/consents/me
//   * POST   /v1/speaking/consents/{type}/revoke
//   * DELETE /v1/speaking/recordings/{id}                 (learner GDPR erasure)
//   * POST   /v1/admin/speaking/recordings/{id}/access    (admin/tutor audit log)
//
// The first four are authenticated as the learner; the fifth requires a
// privileged role (admin OR expert).
public static class SpeakingComplianceEndpoints
{
    public static IEndpointRouteBuilder MapSpeakingComplianceEndpoints(this IEndpointRouteBuilder app)
    {
        var learner = app.MapGroup("/v1/speaking").RequireAuthorization("LearnerOnly");

        learner.MapPost("/consents", async (
            HttpContext http,
            RecordConsentRequest body,
            SpeakingComplianceService svc,
            CancellationToken ct) =>
        {
            var ip = http.ResolveClientIp();
            var ua = http.Request.Headers.UserAgent.ToString();
            var record = await svc.RecordConsentAsync(http.UserId(), body, ip, ua, ct);
            return Results.Ok(record);
        });

        learner.MapGet("/consents/me", async (
            HttpContext http,
            SpeakingComplianceService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.GetConsentHistoryAsync(http.UserId(), ct)));

        learner.MapPost("/consents/{type}/revoke", async (
            HttpContext http,
            string type,
            SpeakingComplianceService svc,
            CancellationToken ct) =>
        {
            var revoked = await svc.RevokeConsentAsync(http.UserId(), type, ct);
            return Results.Ok(new { revoked });
        });

        learner.MapDelete("/recordings/{id}", async (
            HttpContext http,
            string id,
            SpeakingComplianceService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.DeleteRecordingAsync(http.UserId(), id, ct)));

        // Phase 10 P10.1 — learner self-management list.
        learner.MapGet("/recordings/mine", async (
            HttpContext http,
            SpeakingComplianceService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.GetMyRecordingsAsync(http.UserId(), ct)));

        // Phase 10 P10.3 — erasure preflight (no deletion).
        learner.MapGet("/recordings/erasure-preflight", async (
            HttpContext http,
            SpeakingComplianceService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.GetErasurePreflightAsync(http.UserId(), ct)));

        // Admin/tutor non-owner access. TeachingStaffOnly covers Expert + Admin.
        var teaching = app.MapGroup("/v1/admin/speaking")
            .RequireAuthorization("TeachingStaffOnly");

        teaching.MapPost("/recordings/{id}/access", async (
            HttpContext http,
            string id,
            AdminRecordingAccessRequest body,
            SpeakingComplianceService svc,
            CancellationToken ct) =>
        {
            var name = http.User.FindFirstValue(ClaimTypes.Name)
                ?? http.User.FindFirstValue(ClaimTypes.GivenName)
                ?? http.UserId();
            var recording = await svc.AdminAccessRecordingAsync(
                http.UserId(), name, id, body.Purpose, ct);
            return Results.Ok(new
            {
                recordingId = recording.Id,
                sessionId = recording.SpeakingSessionId,
                isArchived = recording.IsArchived,
            });
        })
        .RequireRateLimiting("PerUserWrite");

        // Phase 10 P10.2 — admin recording-access audit viewer.
        teaching.MapGet("/recordings/audit", async (
            [AsParameters] SpeakingAccessAuditFilter filter,
            SpeakingComplianceService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.GetAccessAuditAsync(filter, ct)));

        return app;
    }
}

file static class SpeakingComplianceHttpContextExtensions
{
    internal static string UserId(this HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");

    internal static string? ResolveClientIp(this HttpContext http)
    {
        // Prefer X-Forwarded-For when present (proxy / load-balancer
        // friendly); otherwise fall back to the raw remote IP.
        var fwd = http.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(fwd))
        {
            var first = fwd.Split(',').FirstOrDefault()?.Trim();
            if (!string.IsNullOrEmpty(first))
            {
                return first;
            }
        }
        return http.Connection.RemoteIpAddress?.ToString();
    }
}
