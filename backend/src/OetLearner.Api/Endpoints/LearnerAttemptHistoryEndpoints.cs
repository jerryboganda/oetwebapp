using System.Security.Claims;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class LearnerAttemptHistoryEndpoints
{
    public static IEndpointRouteBuilder MapLearnerAttemptHistoryEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1");

        v1.MapGet("/me/attempts", async (HttpContext http, ILearnerAttemptHistoryService service, CancellationToken ct, int? limit, string? subtest) =>
                Results.Ok(await service.GetHistoryAsync(http.UserId(), limit ?? 100, subtest, ct)))
            .RequireAuthorization();

        return app;
    }

    private static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? httpContext.User.FindFirstValue("sub")
           ?? httpContext.User.Identity?.Name
           ?? string.Empty;
}
