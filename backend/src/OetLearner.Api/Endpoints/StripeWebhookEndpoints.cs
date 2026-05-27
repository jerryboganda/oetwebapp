using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class StripeWebhookEndpoints
{
    public static IEndpointRouteBuilder MapStripeWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/webhooks/stripe", HandleStripeWebhook).AllowAnonymous();
        return app;
    }

    private static async Task<IResult> HandleStripeWebhook(
        HttpContext http,
        CancellationToken ct)
    {
        var service = http.RequestServices.GetRequiredService<LearnerService>();
        var payload = await new StreamReader(http.Request.Body).ReadToEndAsync(ct);
        var headers = http.Request.Headers.ToDictionary(
            header => header.Key,
            header => header.Value.ToString(),
            StringComparer.OrdinalIgnoreCase);
        var outcome = await service.HandleStripeWebhookAsync(payload, headers, ct);
        return LearnerService.IsRejectedWebhookOutcome(outcome)
            ? Results.StatusCode(StatusCodes.Status400BadRequest)
            : Results.Ok(outcome);
    }
}
