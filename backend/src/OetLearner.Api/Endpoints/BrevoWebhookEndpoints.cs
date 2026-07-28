using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class BrevoWebhookEndpoints
{
    public static IEndpointRouteBuilder MapBrevoWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/webhooks/brevo", HandleBrevoWebhook).AllowAnonymous();
        return app;
    }

    private static async Task<IResult> HandleBrevoWebhook(
        HttpContext http,
        NotificationService notificationService,
        CancellationToken ct)
    {
        var payload = await new StreamReader(http.Request.Body).ReadToEndAsync(ct);
        var secret = http.Request.Query["secret"].ToString();

        try
        {
            var suppressedCount = await notificationService.HandleBrevoWebhookEventsAsync(payload, secret, ct);
            return Results.Ok(new { suppressed = suppressedCount });
        }
        catch (ApiException ex) when (ex.StatusCode == StatusCodes.Status401Unauthorized)
        {
            return Results.Unauthorized();
        }
    }
}
