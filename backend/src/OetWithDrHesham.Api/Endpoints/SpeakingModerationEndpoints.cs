using System.Security.Claims;
using OetWithDrHesham.Api.Contracts;
using OetWithDrHesham.Api.Services.Speaking;

namespace OetWithDrHesham.Api.Endpoints;

// OET Speaking module — double-marking + senior moderation routes
// (Developer Implementation Notes §15.4 / §15.5).
//
//   GET  /v1/expert/speaking/moderation/queue?professionId=
//   GET  /v1/expert/speaking/sessions/{id}/moderation
//   POST /v1/expert/speaking/sessions/{id}/moderation/open
//   POST /v1/expert/speaking/sessions/{id}/moderation/second-mark
//   POST /v1/expert/speaking/sessions/{id}/moderation/finalize
//
// Authorization: ExpertOnly policy. Separation of duties (second marker ≠
// first marker; moderator ≠ either human marker) is enforced inside
// SpeakingModerationService, so a moderator role is expressed through that
// business rule rather than a new RBAC role.
public static class SpeakingModerationEndpoints
{
    public static IEndpointRouteBuilder MapSpeakingModerationEndpoints(this IEndpointRouteBuilder app)
    {
        var expert = app.MapGroup("/v1/expert/speaking")
            .RequireAuthorization("ExpertOnly");

        expert.MapGet("/moderation/queue", async (
            HttpContext http,
            string? professionId,
            SpeakingModerationService svc,
            CancellationToken ct) =>
        {
            var items = await svc.ListQueueAsync(professionId ?? string.Empty, ct);
            return Results.Ok(new { items });
        });

        expert.MapGet("/sessions/{id}/moderation", async (
            HttpContext http,
            string id,
            SpeakingModerationService svc,
            CancellationToken ct) =>
        {
            var moderation = await svc.GetAsync(id, ct);
            return moderation is null ? Results.NotFound() : Results.Ok(moderation);
        });

        expert.MapPost("/sessions/{id}/moderation/open", async (
            HttpContext http,
            string id,
            OpenSpeakingModerationRequest? body,
            SpeakingModerationService svc,
            CancellationToken ct) =>
        {
            var moderation = await svc.OpenAsync(http.ExpertId(), id, body?.Reason, ct);
            return Results.Ok(moderation);
        });

        expert.MapPost("/sessions/{id}/moderation/second-mark", async (
            HttpContext http,
            string id,
            SpeakingSecondMarkRequest body,
            SpeakingModerationService svc,
            CancellationToken ct) =>
        {
            var moderation = await svc.SubmitSecondMarkAsync(
                http.ExpertId(), id, body, SpeakingModerationService.DefaultVarianceThreshold, ct);
            return Results.Ok(moderation);
        });

        expert.MapPost("/sessions/{id}/moderation/finalize", async (
            HttpContext http,
            string id,
            SpeakingModerationFinalizeRequest body,
            SpeakingModerationService svc,
            CancellationToken ct) =>
        {
            var moderation = await svc.FinalizeAsync(http.ExpertId(), id, body, ct);
            return Results.Ok(moderation);
        });

        return app;
    }

    private static string ExpertId(this HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated expert id is required.");
}
