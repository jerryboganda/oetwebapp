using Microsoft.AspNetCore.Mvc;
using OetWithDrHesham.Api.Services.Writing;

namespace OetWithDrHesham.Api.Endpoints;

public static class WritingMistakeEndpoints
{
    public static IEndpointRouteBuilder MapWritingMistakeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/writing/mistakes")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        group.MapGet("/", async (
            [FromQuery] string? category,
            [FromQuery] string? subSkill,
            HttpContext http,
            IWritingMistakeService service,
            CancellationToken ct)
            => Results.Ok(await service.ListCommonMistakesAsync(http.WritingV2UserId(), category, subSkill, ct)))
            .WithName("ListWritingCommonMistakes");

        // Static path registered before {id:guid} to avoid the literal being parsed as a Guid.
        group.MapGet("/mine", async (
            HttpContext http,
            IWritingMistakeService service,
            CancellationToken ct)
            => Results.Ok(await service.ListMyMistakesAsync(http.WritingV2UserId(), ct)))
            .WithName("ListMyWritingCommonMistakes");

        group.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IWritingMistakeService service,
            CancellationToken ct) =>
        {
            var mistake = await service.GetCommonMistakeAsync(http.WritingV2UserId(), id, ct);
            return mistake is null ? Results.NotFound() : Results.Ok(mistake);
        })
        .WithName("GetWritingCommonMistake");

        return app;
    }
}
