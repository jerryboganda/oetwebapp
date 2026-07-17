using OetWithDrHesham.Api.Contracts;
using OetWithDrHesham.Api.Services.Writing;

namespace OetWithDrHesham.Api.Endpoints;

public static class WritingCoachV2Endpoints
{
    public static IEndpointRouteBuilder MapWritingCoachV2Endpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/writing/coach")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("writing-coach");

        group.MapPost("/hints", async (
            WritingCoachHintRequest request,
            HttpContext http,
            IWritingCoachServiceV2 service,
            CancellationToken ct) =>
        {
            var hints = await service.RequestHintsAsync(http.WritingV2UserId(), request, ct);
            return Results.Ok(new WritingCoachHintsResponse(hints));
        })
        .WithName("RequestWritingCoachHints");

        return app;
    }
}
