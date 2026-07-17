using OetWithDrHesham.Api.Services.Professions;
using OetWithDrHesham.Api.Services.Settings;

namespace OetWithDrHesham.Api.Endpoints;

/// <summary>
/// Public reads that every surface needs before a learner is authenticated:
/// the canonical profession taxonomy (so nothing hardcodes a profession list)
/// and the support WhatsApp number (rendered next to every package, including
/// logged-out checkout states).
/// </summary>
public static class ProfessionCatalogEndpoints
{
    public static IEndpointRouteBuilder MapProfessionCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/professions/catalog", async (IProfessionCatalogService catalog, CancellationToken ct) =>
            {
                // Archived entries are returned too, flagged via isActive: a learner
                // registered under a since-archived profession still needs its label
                // rendered. Clients that offer a choice must filter on isActive.
                var entries = await catalog.GetAllAsync(ct);
                return Results.Ok(new ProfessionCatalogResponse(entries
                    .Select(item => new ProfessionCatalogItemResponse(
                        item.Id,
                        item.Label,
                        item.Description,
                        item.IsActive))
                    .ToList()));
            })
            .AllowAnonymous();

        return app;
    }

    public static IEndpointRouteBuilder MapPublicSupportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/support/whatsapp", async (IRuntimeSettingsProvider settings, CancellationToken ct) =>
            {
                // Only the two support fields — never the settings row, which carries
                // gateway credentials.
                var support = (await settings.GetAsync(ct)).Support;
                return Results.Ok(new SupportWhatsAppResponse(
                    support.WhatsAppNumber,
                    support.WhatsAppProofTemplate));
            })
            .AllowAnonymous();

        return app;
    }
}

public sealed record ProfessionCatalogResponse(IReadOnlyList<ProfessionCatalogItemResponse> Professions);

public sealed record ProfessionCatalogItemResponse(
    string Id,
    string Label,
    string Description,
    bool IsActive);

/// <summary>Public support number; null when no admin has configured one.</summary>
public sealed record SupportWhatsAppResponse(
    string? WhatsAppNumber,
    string? WhatsAppProofTemplate);
