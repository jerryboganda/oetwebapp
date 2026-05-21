using System.Security.Claims;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Endpoints;

// Phase 6 of the OET Speaking module roadmap.
//
// Admin CRUD + tutor read/complete for interlocutor training modules.
//
// Admin routes (require `AdminContentWrite`):
//   GET   /v1/admin/speaking/interlocutor-training/modules
//   POST  /v1/admin/speaking/interlocutor-training/modules
//   GET   /v1/admin/speaking/interlocutor-training/modules/{id}
//   PATCH /v1/admin/speaking/interlocutor-training/modules/{id}
//   POST  /v1/admin/speaking/interlocutor-training/modules/{id}/publish
//   POST  /v1/admin/speaking/interlocutor-training/modules/{id}/archive
//
// Tutor routes (require any teaching role):
//   GET   /v1/expert/speaking/interlocutor-training
//   POST  /v1/expert/speaking/interlocutor-training/modules/{id}/complete
public static class InterlocutorTrainingEndpoints
{
    public static IEndpointRouteBuilder MapInterlocutorTrainingEndpoints(this IEndpointRouteBuilder app)
    {
        // ── Admin ──────────────────────────────────────────────────────────
        var admin = app.MapGroup("/v1/admin/speaking/interlocutor-training/modules")
            .RequireAuthorization("AdminContentRead")
            .RequireRateLimiting("PerUser");

        admin.MapGet("", async (
            string? stage,
            string? status,
            InterlocutorTrainingService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.ListModulesAsync(stage, status, ct)));

        admin.MapGet("/{id}", async (
            string id,
            InterlocutorTrainingService svc,
            CancellationToken ct) =>
        {
            var row = await svc.GetModuleAsync(id, ct);
            return row is null ? Results.NotFound() : Results.Ok(row);
        });

        admin.MapPost("", async (
            HttpContext http,
            CreateModuleRequest body,
            InterlocutorTrainingService svc,
            CancellationToken ct) =>
        {
            var (adminId, adminName) = http.AdminIdentity();
            var row = await svc.CreateModuleAsync(
                adminId,
                adminName,
                body.Title,
                body.ContentMarkdown ?? string.Empty,
                body.Stage ?? "Onboarding",
                body.OrderIndex,
                body.RequiredForCalibration,
                ct);
            return Results.Ok(row);
        });

        admin.MapPatch("/{id}", async (
            HttpContext http,
            string id,
            UpdateModuleRequest body,
            InterlocutorTrainingService svc,
            CancellationToken ct) =>
        {
            var (adminId, adminName) = http.AdminIdentity();
            var row = await svc.UpdateModuleAsync(
                adminId,
                adminName,
                id,
                body.Title,
                body.ContentMarkdown,
                body.Stage,
                body.OrderIndex,
                body.RequiredForCalibration,
                ct);
            return Results.Ok(row);
        });

        admin.MapPost("/{id}/publish", async (
            HttpContext http,
            string id,
            InterlocutorTrainingService svc,
            CancellationToken ct) =>
        {
            var (adminId, adminName) = http.AdminIdentity();
            var row = await svc.PublishModuleAsync(adminId, adminName, id, ct);
            return Results.Ok(row);
        });

        admin.MapPost("/{id}/archive", async (
            HttpContext http,
            string id,
            InterlocutorTrainingService svc,
            CancellationToken ct) =>
        {
            var (adminId, adminName) = http.AdminIdentity();
            var row = await svc.ArchiveModuleAsync(adminId, adminName, id, ct);
            return Results.Ok(row);
        });

        // ── Tutor / Expert ─────────────────────────────────────────────────
        var tutor = app.MapGroup("/v1/expert/speaking/interlocutor-training")
            .RequireAuthorization("TeachingStaffOnly")
            .RequireRateLimiting("PerUser");

        tutor.MapGet("", async (
            HttpContext http,
            InterlocutorTrainingService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.ListForTutorAsync(http.UserId(), ct)));

        tutor.MapPost("/modules/{id}/complete", async (
            HttpContext http,
            string id,
            CompleteModuleRequest? body,
            InterlocutorTrainingService svc,
            CancellationToken ct) =>
        {
            var row = await svc.MarkCompleteAsync(http.UserId(), id, body?.QuizScore, ct);
            return Results.Ok(row);
        });

        return app;
    }

    public sealed record CreateModuleRequest(
        string Title,
        string? ContentMarkdown,
        string? Stage,
        int OrderIndex,
        bool RequiredForCalibration);

    public sealed record UpdateModuleRequest(
        string? Title,
        string? ContentMarkdown,
        string? Stage,
        int? OrderIndex,
        bool? RequiredForCalibration);

    public sealed record CompleteModuleRequest(int? QuizScore);
}

file static class InterlocutorTrainingHttpContextExtensions
{
    internal static string UserId(this HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");

    internal static (string id, string name) AdminIdentity(this HttpContext http)
    {
        var id = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
        var name = http.User.FindFirstValue(ClaimTypes.Name)
                   ?? http.User.FindFirstValue(ClaimTypes.GivenName)
                   ?? id;
        return (id, name);
    }
}
