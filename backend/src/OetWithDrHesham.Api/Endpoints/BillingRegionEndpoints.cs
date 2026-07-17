using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services;
using OetWithDrHesham.Api.Services.Billing;

namespace OetWithDrHesham.Api.Endpoints;

/// <summary>
/// Phase 1 international expansion endpoints. Exposes the billing region/profile
/// for the authenticated learner and admin CRUD for RegionPricing and
/// GatewayRoutingConfig.
/// </summary>
public static class BillingRegionEndpoints
{
    public static IEndpointRouteBuilder MapBillingRegionEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1");

        var billing = v1.MapGroup("/billing").RequireAuthorization();
        billing.MapGet("/profile", GetProfile);
        billing.MapPut("/profile", UpdateProfile);
        billing.MapGet("/region", DetectRegion);

        var admin = v1.MapGroup("/admin/billing");
        admin.MapGet("/region-pricings", ListRegionPricings).RequireAuthorization("AdminBillingRead");
        admin.MapPost("/region-pricings", UpsertRegionPricing).WithAdminWrite("AdminBillingCatalogWrite");
        admin.MapDelete("/region-pricings/{id}", DeleteRegionPricing).WithAdminWrite("AdminBillingCatalogWrite");

        admin.MapGet("/gateway-routes", ListGatewayRoutes).RequireAuthorization("AdminBillingRead");
        admin.MapPost("/gateway-routes", UpsertGatewayRoute).WithAdminWrite("AdminBillingCatalogWrite");
        admin.MapDelete("/gateway-routes/{id}", DeleteGatewayRoute).WithAdminWrite("AdminBillingCatalogWrite");

        return app;
    }

    private static async Task<Ok<BillingProfileResponse>> GetProfile(
        HttpContext http,
        LearnerDbContext db,
        IRegionDetector detector,
        CancellationToken ct)
    {
        var userId = http.UserId();
        var account = await db.ApplicationUserAccounts
            .Where(u => u.Id == userId)
            .Select(u => new { u.Country, u.PreferredCurrency, u.PreferredRegion })
            .FirstOrDefaultAsync(ct);

        var detection = detector.Detect(http, account?.Country, account?.PreferredRegion, account?.PreferredCurrency);
        return TypedResults.Ok(new BillingProfileResponse(
            Country: account?.Country,
            PreferredCurrency: account?.PreferredCurrency,
            PreferredRegion: account?.PreferredRegion,
            DetectedRegion: detection.Region,
            DetectedCountry: detection.Country,
            DetectedCurrency: detection.Currency,
            DetectionSource: detection.Source));
    }

    private static async Task<Results<Ok<BillingProfileResponse>, BadRequest<string>>> UpdateProfile(
        HttpContext http,
        BillingProfileUpdateRequest request,
        LearnerDbContext db,
        IRegionDetector detector,
        CancellationToken ct)
    {
        if (request.Country is not null && request.Country.Length != 2)
        {
            return TypedResults.BadRequest("country must be ISO 3166-1 alpha-2 (2 letters).");
        }
        if (request.PreferredCurrency is not null && request.PreferredCurrency.Length != 3)
        {
            return TypedResults.BadRequest("preferredCurrency must be ISO 4217 (3 letters).");
        }
        if (request.PreferredRegion is not null && !BillingRegions.All.Contains(request.PreferredRegion.ToUpperInvariant()))
        {
            return TypedResults.BadRequest($"preferredRegion must be one of: {string.Join(",", BillingRegions.All)}.");
        }

        var userId = http.UserId();
        var account = await db.ApplicationUserAccounts.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (account is null)
        {
            return TypedResults.BadRequest("User not found.");
        }

        if (request.Country is not null) account.Country = request.Country.ToUpperInvariant();
        if (request.PreferredCurrency is not null) account.PreferredCurrency = request.PreferredCurrency.ToUpperInvariant();
        if (request.PreferredRegion is not null) account.PreferredRegion = request.PreferredRegion.ToUpperInvariant();

        // If the user provided a country but no explicit region, derive it.
        if (request.PreferredRegion is null && request.Country is not null)
        {
            account.PreferredRegion = BillingRegions.FromCountry(request.Country);
        }

        account.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var detection = detector.Detect(http, account.Country, account.PreferredRegion, account.PreferredCurrency);
        return TypedResults.Ok(new BillingProfileResponse(
            account.Country, account.PreferredCurrency, account.PreferredRegion,
            detection.Region, detection.Country, detection.Currency, detection.Source));
    }

    private static Ok<RegionDetectionResponse> DetectRegion(HttpContext http, IRegionDetector detector)
    {
        var d = detector.Detect(http);
        return TypedResults.Ok(new RegionDetectionResponse(d.Region, d.Country, d.Currency, d.Source));
    }

    // ── Admin CRUD ─────────────────────────────────────────────────────

    private static async Task<Ok<List<RegionPricingDto>>> ListRegionPricings(LearnerDbContext db, [FromQuery] string? targetType, [FromQuery] string? targetId, [FromQuery] string? region, CancellationToken ct)
    {
        var query = db.RegionPricings.AsQueryable();
        if (!string.IsNullOrEmpty(targetType)) query = query.Where(r => r.TargetType == targetType);
        if (!string.IsNullOrEmpty(targetId)) query = query.Where(r => r.TargetId == targetId);
        if (!string.IsNullOrEmpty(region)) query = query.Where(r => r.Region == region.ToUpperInvariant());
        var rows = await query
            .OrderBy(r => r.TargetType).ThenBy(r => r.TargetId).ThenBy(r => r.Region)
            .Select(r => new RegionPricingDto(r.Id, r.TargetType, r.TargetId, r.Region, r.Currency, r.PriceAmount, r.IsActive, r.CreatedAt, r.UpdatedAt))
            .ToListAsync(ct);
        return TypedResults.Ok(rows);
    }

    private static async Task<Results<Ok<RegionPricingDto>, BadRequest<string>>> UpsertRegionPricing(HttpContext http, RegionPricingUpsertRequest request, LearnerDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.TargetType) || string.IsNullOrWhiteSpace(request.TargetId) || string.IsNullOrWhiteSpace(request.Region) || string.IsNullOrWhiteSpace(request.Currency))
        {
            return TypedResults.BadRequest("targetType, targetId, region, currency are required.");
        }
        if (request.PriceAmount < 0)
        {
            return TypedResults.BadRequest("priceAmount must be non-negative.");
        }

        var region = request.Region.ToUpperInvariant();
        var existing = await db.RegionPricings.FirstOrDefaultAsync(r =>
            r.TargetType == request.TargetType && r.TargetId == request.TargetId && r.Region == region, ct);

        var now = DateTimeOffset.UtcNow;
        var adminId = http.UserId();
        if (existing is null)
        {
            existing = new RegionPricing
            {
                Id = Guid.NewGuid().ToString("N"),
                TargetType = request.TargetType,
                TargetId = request.TargetId,
                Region = region,
                Currency = request.Currency.ToUpperInvariant(),
                PriceAmount = request.PriceAmount,
                IsActive = request.IsActive,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedByAdminId = adminId,
                UpdatedByAdminId = adminId,
            };
            db.RegionPricings.Add(existing);
        }
        else
        {
            existing.Currency = request.Currency.ToUpperInvariant();
            existing.PriceAmount = request.PriceAmount;
            existing.IsActive = request.IsActive;
            existing.UpdatedAt = now;
            existing.UpdatedByAdminId = adminId;
        }
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new RegionPricingDto(existing.Id, existing.TargetType, existing.TargetId, existing.Region, existing.Currency, existing.PriceAmount, existing.IsActive, existing.CreatedAt, existing.UpdatedAt));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteRegionPricing(string id, LearnerDbContext db, CancellationToken ct)
    {
        var row = await db.RegionPricings.FindAsync(new object?[] { id }, ct);
        if (row is null) return TypedResults.NotFound();
        db.RegionPricings.Remove(row);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<List<GatewayRouteDto>>> ListGatewayRoutes(LearnerDbContext db, CancellationToken ct)
    {
        var rows = await db.GatewayRoutingConfigs
            .OrderBy(r => r.Region).ThenBy(r => r.Priority)
            .Select(r => new GatewayRouteDto(r.Id, r.Region, r.Currency, r.ProductType, r.GatewayName, r.Priority, r.IsEnabled))
            .ToListAsync(ct);
        return TypedResults.Ok(rows);
    }

    private static async Task<Results<Ok<GatewayRouteDto>, BadRequest<string>>> UpsertGatewayRoute(HttpContext http, GatewayRouteUpsertRequest request, LearnerDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Region) || string.IsNullOrWhiteSpace(request.Currency) || string.IsNullOrWhiteSpace(request.ProductType) || string.IsNullOrWhiteSpace(request.GatewayName))
        {
            return TypedResults.BadRequest("region, currency, productType, gatewayName are required.");
        }

        var region = request.Region.ToUpperInvariant();
        var currency = request.Currency.ToUpperInvariant();
        var product = request.ProductType;
        var gateway = request.GatewayName.ToLowerInvariant();

        var existing = await db.GatewayRoutingConfigs.FirstOrDefaultAsync(r =>
            r.Region == region && r.Currency == currency && r.ProductType == product && r.GatewayName == gateway, ct);

        var now = DateTimeOffset.UtcNow;
        var adminId = http.UserId();
        if (existing is null)
        {
            existing = new GatewayRoutingConfig
            {
                Id = Guid.NewGuid().ToString("N"),
                Region = region, Currency = currency, ProductType = product, GatewayName = gateway,
                Priority = request.Priority, IsEnabled = request.IsEnabled,
                CreatedAt = now, UpdatedAt = now, UpdatedByAdminId = adminId,
            };
            db.GatewayRoutingConfigs.Add(existing);
        }
        else
        {
            existing.Priority = request.Priority;
            existing.IsEnabled = request.IsEnabled;
            existing.UpdatedAt = now;
            existing.UpdatedByAdminId = adminId;
        }
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new GatewayRouteDto(existing.Id, existing.Region, existing.Currency, existing.ProductType, existing.GatewayName, existing.Priority, existing.IsEnabled));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteGatewayRoute(string id, LearnerDbContext db, CancellationToken ct)
    {
        var row = await db.GatewayRoutingConfigs.FindAsync(new object?[] { id }, ct);
        if (row is null) return TypedResults.NotFound();
        db.GatewayRoutingConfigs.Remove(row);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }
}

public sealed record BillingProfileResponse(
    string? Country,
    string? PreferredCurrency,
    string? PreferredRegion,
    string DetectedRegion,
    string DetectedCountry,
    string DetectedCurrency,
    string DetectionSource);

public sealed record BillingProfileUpdateRequest(string? Country, string? PreferredCurrency, string? PreferredRegion);

public sealed record RegionDetectionResponse(string Region, string Country, string Currency, string Source);

public sealed record RegionPricingDto(
    string Id, string TargetType, string TargetId, string Region,
    string Currency, decimal PriceAmount, bool IsActive,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record RegionPricingUpsertRequest(
    string TargetType, string TargetId, string Region, string Currency,
    decimal PriceAmount, bool IsActive);

public sealed record GatewayRouteDto(
    string Id, string Region, string Currency, string ProductType,
    string GatewayName, int Priority, bool IsEnabled);

public sealed record GatewayRouteUpsertRequest(
    string Region, string Currency, string ProductType, string GatewayName,
    int Priority, bool IsEnabled);

file static class BillingRegionHttpContextExtensions
{
    internal static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
