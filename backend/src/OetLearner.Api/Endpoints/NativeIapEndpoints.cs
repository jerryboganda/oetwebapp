using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Contracts;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class NativeIapEndpoints
{
    public static RouteGroupBuilder MapAdminNativeIapEndpoints(this RouteGroupBuilder admin)
    {
        var group = admin.MapGroup("/billing/iap-products");

        group.MapGet("", async ([FromQuery] string? platform, NativeIapService service, CancellationToken ct)
            => Results.Ok(await service.ListAdminMappingsAsync(platform, ct)))
            .WithAdminRead("AdminBillingRead");

        group.MapGet("/{id}", async (string id, NativeIapService service, CancellationToken ct)
            => Results.Ok(await service.GetAdminMappingAsync(id, ct)))
            .WithAdminRead("AdminBillingRead");

        group.MapPost("", async (HttpContext http, NativeIapProductMappingUpsertRequest request, NativeIapService service, CancellationToken ct)
            => Results.Ok(await service.CreateAdminMappingAsync(http.AdminId(), request, ct)))
            .WithAdminWrite("AdminBillingCatalogWrite");

        group.MapPut("/{id}", async (string id, HttpContext http, NativeIapProductMappingUpsertRequest request, NativeIapService service, CancellationToken ct)
            => Results.Ok(await service.UpdateAdminMappingAsync(http.AdminId(), id, request, ct)))
            .WithAdminWrite("AdminBillingCatalogWrite");

        group.MapDelete("/{id}", async Task<Results<NoContent, NotFound>> (string id, HttpContext http, NativeIapService service, CancellationToken ct) =>
        {
            await service.DeleteAdminMappingAsync(http.AdminId(), id, ct);
            return TypedResults.NoContent();
        })
            .WithAdminWrite("AdminBillingCatalogWrite");

        return admin;
    }

    public static RouteGroupBuilder MapLearnerNativeIapEndpoints(this RouteGroupBuilder billing)
    {
        var group = billing.MapGroup("/native-iap");

        group.MapGet("/products", async ([FromQuery] string platform, NativeIapService service, CancellationToken ct)
            => Results.Ok(await service.ListLearnerMappingsAsync(platform, ct)));

        group.MapPost("/receipts/validate", (NativeIapReceiptValidationRequest request, NativeIapService service)
            => Results.Ok(service.ValidateReceiptFailClosed(request)))
            .RequireRateLimiting("PerUserWrite");

        return billing;
    }

    private static string AdminId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated admin id is required.");
}
