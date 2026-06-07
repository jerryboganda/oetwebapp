using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// OET 2026 catalogue endpoints — surfaces the 27-SKU portfolio to buyers
/// (public pricing matrix), to learners (add-on eligibility quote) and to
/// admins (eligibility matrix audit).
///
/// <para>All routes follow the existing v1 grouping. Auth requirements:</para>
/// <list type="bullet">
///   <item><c>GET /v1/catalog/pricing</c> — public, no auth.</item>
///   <item><c>POST /v1/billing/quote/addon</c> — learner auth required.</item>
///   <item><c>GET /admin/billing/eligibility/matrix</c> — AdminBillingRead policy.</item>
/// </list>
/// </summary>
public static class Oet2026CatalogEndpoints
{
    public static IEndpointRouteBuilder MapOet2026CatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1");

        // Public pricing matrix — anonymous access for the marketing landing page.
        v1.MapGet("/catalog/pricing", PublicCatalogPricing).AllowAnonymous();

        // Add-on eligibility quote — learner auth required.
        var billing = v1.MapGroup("/billing").RequireAuthorization();
        billing.MapPost("/quote/addon", QuoteAddonEligibility);

        // Per-user entitlement snapshot — drives dashboard module visibility +
        // add-on widget gating (Wave 4.6).
        var me = v1.MapGroup("/me").RequireAuthorization();
        me.MapGet("/entitlement-snapshot", MyEntitlementSnapshot);

        // Admin eligibility matrix.
        var admin = v1.MapGroup("/admin/billing");
        admin.MapGet("/eligibility/matrix", AdminEligibilityMatrix).RequireAuthorization("AdminBillingRead");
        // Re-seed catalog button (Wave 3.4).
        admin.MapPost("/catalog/seed-oet-2026", AdminReseedOet2026Catalog).RequireAuthorization("AdminBillingCatalogWrite");

        return app;
    }

    // ── Per-user entitlement snapshot ────────────────────────────────────

    private sealed record EntitlementSnapshotResponse(
        bool HasEligibleSubscription,
        string Tier,
        string? PlanCode,
        string? ProductCategory,
        IReadOnlyList<string> EnabledModules,
        bool WritingAddonsEnabled,
        bool SpeakingAddonsEnabled,
        bool TutorBookDiscountEnabled,
        int WritingAssessmentsRemaining,
        int SpeakingSessionsRemaining,
        int AiCreditsRemaining,
        bool TutorBookUnlocked,
        bool BasicEnglishUnlocked,
        DateTimeOffset? ExpiresAt,
        bool IsFrozen);

    private static async Task<Ok<EntitlementSnapshotResponse>> MyEntitlementSnapshot(
        HttpContext http,
        IEffectiveEntitlementResolver resolver,
        CancellationToken ct)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var snapshot = await resolver.ResolveAsync(userId, ct);
        return TypedResults.Ok(new EntitlementSnapshotResponse(
            snapshot.HasEligibleSubscription,
            snapshot.Tier,
            snapshot.PlanCode,
            snapshot.ProductCategory,
            snapshot.EnabledModules,
            snapshot.WritingAddonsEnabled,
            snapshot.SpeakingAddonsEnabled,
            snapshot.TutorBookDiscountEnabled,
            snapshot.WritingAssessmentsRemaining,
            snapshot.SpeakingSessionsRemaining,
            snapshot.AiCreditsRemaining,
            snapshot.TutorBookUnlocked,
            snapshot.BasicEnglishUnlocked,
            snapshot.ExpiresAt,
            snapshot.IsFrozen));
    }

    private sealed record ReseedResponse(int PlansCreated, int PlansUpdated, int AddOnsCreated, int AddOnsUpdated, int PackagesCreated, int PackagesUpdated);

    private static async Task<Results<Ok<ReseedResponse>, BadRequest<string>>> AdminReseedOet2026Catalog(
        Oet2026CatalogSeeder seeder,
        CancellationToken ct)
    {
        try
        {
            var result = await seeder.SeedAsync(ct);
            return TypedResults.Ok(new ReseedResponse(
                result.PlansCreated,
                result.PlansUpdated,
                result.AddOnsCreated,
                result.AddOnsUpdated,
                result.PackagesCreated,
                result.PackagesUpdated));
        }
        catch (Exception ex)
        {
            return TypedResults.BadRequest($"Seed failed: {ex.Message}");
        }
    }

    // ── Public catalog pricing ────────────────────────────────────────────

    private sealed record PublicCatalogResponse(
        IReadOnlyList<PublicPlanRow> Plans,
        IReadOnlyList<PublicAddOnRow> AddOns,
        string Currency);

    private sealed record PublicPlanRow(
        string Code,
        string Name,
        string? Description,
        decimal Price,
        decimal? OriginalPrice,
        string Currency,
        int AccessDurationDays,
        string ProductCategory,
        string Profession,
        bool WritingAddonsEnabled,
        bool SpeakingAddonsEnabled,
        bool TutorBookDiscountEnabled,
        int BundledWritingAssessments,
        int BundledSpeakingSessions,
        int BundledAiCredits,
        bool BundledTutorBook,
        bool BundledBasicEnglish,
        IReadOnlyList<string> DashboardModules,
        int DisplayOrder,
        // Spec-aligned public id. The two standalone Speaking-session plans keep the
        // stable DB codes `speaking-1session-plan`/`speaking-2sessions-plan` (the bare
        // `speaking-1session`/`speaking-2sessions` codes belong to the add-on SKUs),
        // so the spec's bare product id is surfaced here for catalogue display/URLs
        // without a risky rename of live codes. For every other plan PublicSlug == Code.
        string PublicSlug);

    private sealed record PublicAddOnRow(
        string Code,
        string Name,
        string? Description,
        decimal Price,
        decimal? OriginalPrice,
        string Currency,
        string AddonKind,
        string EligibilityFlag,
        int LettersGranted,
        int SessionsGranted,
        bool IsStackable,
        int DisplayOrder);

    private static async Task<Ok<PublicCatalogResponse>> PublicCatalogPricing(
        LearnerDbContext db,
        CancellationToken ct)
    {
        var plans = await db.BillingPlans.AsNoTracking()
            .Where(p => p.Status == BillingPlanStatus.Active && p.IsVisible && !p.IsDraft)
            .OrderBy(p => p.DisplayOrder)
            .ThenBy(p => p.Price)
            .ToListAsync(ct);

        var addOns = await db.BillingAddOns.AsNoTracking()
            .Where(a => a.Status == BillingAddOnStatus.Active)
            .OrderBy(a => a.DisplayOrder)
            .ToListAsync(ct);

        var planRows = plans.Select(p => new PublicPlanRow(
            p.Code,
            p.Name,
            p.Description,
            p.Price,
            p.OriginalPriceGbp,
            string.IsNullOrEmpty(p.Currency) ? "GBP" : p.Currency,
            p.AccessDurationDays,
            string.IsNullOrEmpty(p.ProductCategory) ? "standalone" : p.ProductCategory,
            string.IsNullOrEmpty(p.Profession) ? "all" : p.Profession,
            p.WritingAddonsEnabled,
            p.SpeakingAddonsEnabled,
            p.TutorBookDiscountEnabled,
            p.BundledWritingAssessments,
            p.BundledSpeakingSessions,
            p.BundledAiCredits,
            p.BundledTutorBook,
            p.BundledBasicEnglish,
            DeserializeStringArray(p.DashboardModulesJson),
            p.DisplayOrder,
            p.Code.EndsWith("-plan", StringComparison.Ordinal) ? p.Code[..^5] : p.Code)).ToList();

        var addOnRows = addOns.Select(a => new PublicAddOnRow(
            a.Code,
            a.Name,
            a.Description,
            a.Price,
            a.OriginalPriceGbp,
            string.IsNullOrEmpty(a.Currency) ? "GBP" : a.Currency,
            a.AddonKind ?? string.Empty,
            a.EligibilityFlag ?? string.Empty,
            a.LettersGranted,
            a.SessionsGranted,
            a.IsStackable,
            a.DisplayOrder)).ToList();

        return TypedResults.Ok(new PublicCatalogResponse(planRows, addOnRows, "GBP"));
    }

    // ── Add-on quote ─────────────────────────────────────────────────────

    private sealed record AddonQuoteRequest(string AddOnCode);

    private sealed record AddonQuoteResponse(
        bool Eligible,
        string AddOnCode,
        string AddOnName,
        string? AddonKind,
        string? RequiredFlag,
        IReadOnlyList<EligibleParentEnrolment> EligibleParents,
        string? Reason,
        string? RedirectSku);

    private static async Task<Ok<AddonQuoteResponse>> QuoteAddonEligibility(
        HttpContext http,
        AddonQuoteRequest request,
        IAddonEligibilityService eligibility,
        CancellationToken ct)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Authenticated user id is required.");

        var result = await eligibility.ResolveAsync(userId, request.AddOnCode, ct);

        var payload = new AddonQuoteResponse(
            result.Eligible,
            result.AddOnCode,
            result.AddOnName,
            result.AddonKind,
            result.RequiredFlag,
            result.EligibleParents,
            result.Reason,
            result.RedirectSku);

        // Always return 200; frontend routes on `eligible` flag.
        // This avoids losing the ineligibility payload through HTTP error plumbing.
        return TypedResults.Ok(payload);
    }

    // ── Admin eligibility matrix ─────────────────────────────────────────

    private sealed record EligibilityMatrixRow(
        string Code,
        string Name,
        string Profession,
        string ProductCategory,
        bool IsDraft,
        bool IsVisible,
        bool WritingAddonsEnabled,
        bool SpeakingAddonsEnabled,
        bool TutorBookDiscountEnabled,
        IReadOnlyList<string> EligibleAddOnCodes);

    private sealed record EligibilityMatrixResponse(IReadOnlyList<EligibilityMatrixRow> Plans);

    private static async Task<Ok<EligibilityMatrixResponse>> AdminEligibilityMatrix(
        LearnerDbContext db,
        CancellationToken ct)
    {
        var plans = await db.BillingPlans.AsNoTracking()
            .Where(p => p.Status != BillingPlanStatus.Archived)
            .OrderBy(p => p.DisplayOrder)
            .ThenBy(p => p.Name)
            .ToListAsync(ct);

        var addOns = await db.BillingAddOns.AsNoTracking()
            .Where(a => a.Status == BillingAddOnStatus.Active && a.RequiresEligibleParent)
            .Select(a => new { a.Code, a.EligibilityFlag })
            .ToListAsync(ct);

        var rows = plans.Select(p =>
        {
            var eligible = addOns
                .Where(a => MatchesFlag(p, a.EligibilityFlag))
                .Select(a => a.Code)
                .ToList();
            return new EligibilityMatrixRow(
                p.Code,
                p.Name,
                string.IsNullOrEmpty(p.Profession) ? "all" : p.Profession,
                string.IsNullOrEmpty(p.ProductCategory) ? "standalone" : p.ProductCategory,
                p.IsDraft,
                p.IsVisible,
                p.WritingAddonsEnabled,
                p.SpeakingAddonsEnabled,
                p.TutorBookDiscountEnabled,
                eligible);
        }).ToList();

        return TypedResults.Ok(new EligibilityMatrixResponse(rows));
    }

    private static bool MatchesFlag(BillingPlan plan, string flag) => flag?.ToLowerInvariant() switch
    {
        "writing_addons" => plan.WritingAddonsEnabled,
        "speaking_addons" => plan.SpeakingAddonsEnabled,
        "tutor_book_discount" => plan.TutorBookDiscountEnabled,
        _ => false
    };

    private static IReadOnlyList<string> DeserializeStringArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
            var list = new List<string>(doc.RootElement.GetArrayLength());
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    var value = element.GetString();
                    if (!string.IsNullOrWhiteSpace(value)) list.Add(value.Trim());
                }
            }
            return list;
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}
