using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Phase 8 — affiliate-facing endpoints for the self-serve portal at /affiliate.
/// Also exposes /v1/affiliates/track for the middleware-set cookie to call on
/// signup so a first-click AffiliateAttribution row is created.
/// </summary>
public static class AffiliatePortalEndpoints
{
    public static IEndpointRouteBuilder MapAffiliatePortalEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1");

        // Authenticated track endpoint (called from signup once a user is established).
        var aff = v1.MapGroup("/affiliates").RequireAuthorization();
        aff.MapPost("/track", TrackAttribution);
        aff.MapGet("/me", GetSelfStats);

        return app;
    }

    private static async Task<Results<NoContent, BadRequest<string>>> TrackAttribution(HttpContext http, AffiliateTrackRequest request, IAffiliateService affiliateService, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.AffiliateCode))
        {
            return TypedResults.BadRequest("affiliateCode is required.");
        }
        await affiliateService.AttributeUserAsync(http.UserId(), request.AffiliateCode, ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<AffiliateStatsResponse>, NotFound>> GetSelfStats(HttpContext http, LearnerDbContext db, CancellationToken ct)
    {
        var userId = http.UserId();

        // Strategy: resolve affiliate by matching ContactEmail to the user's
        // ApplicationUserAccount email. Production may swap this for an
        // explicit AffiliateOwnerUserId column.
        var account = await db.ApplicationUserAccounts.FirstOrDefaultAsync(a => a.Id == userId, ct);
        if (account is null) return TypedResults.NotFound();

        var affiliate = await db.Affiliates.FirstOrDefaultAsync(a => a.ContactEmail.ToLower() == account.Email.ToLower(), ct);
        if (affiliate is null) return TypedResults.NotFound();

        var attributions = await db.AffiliateAttributions.Where(a => a.AffiliateId == affiliate.Id).ToListAsync(ct);
        var commissions = await db.AffiliateCommissions
            .Where(c => c.AffiliateId == affiliate.Id)
            .OrderByDescending(c => c.AccruedAt)
            .ToListAsync(ct);

        var paid = commissions.Where(c => c.Status == "paid").Sum(c => c.AmountAmount);
        var pending = commissions.Where(c => c.Status == "accrued" || c.Status == "pending_payout").Sum(c => c.AmountAmount);

        return TypedResults.Ok(new AffiliateStatsResponse(
            AffiliateCode: affiliate.Code,
            OwnerName: affiliate.OwnerName,
            TotalClicks: attributions.Count,
            TotalSignups: attributions.Count,
            TotalConversions: attributions.Count(a => a.ConvertedAt != null),
            TotalEarningsAmount: paid + pending,
            PayoutCurrency: affiliate.PayoutCurrency,
            PendingPayoutAmount: pending,
            PaidPayoutAmount: paid,
            Commissions: commissions.Take(50).Select(c => new AffiliateCommissionRow(
                c.Id, c.PaymentTransactionId, c.AmountAmount, c.Currency, c.Status, c.AccruedAt, c.PaidAt)).ToList()));
    }
}

public sealed record AffiliateTrackRequest(string AffiliateCode);

public sealed record AffiliateStatsResponse(
    string AffiliateCode,
    string OwnerName,
    int TotalClicks,
    int TotalSignups,
    int TotalConversions,
    decimal TotalEarningsAmount,
    string PayoutCurrency,
    decimal PendingPayoutAmount,
    decimal PaidPayoutAmount,
    List<AffiliateCommissionRow> Commissions);

public sealed record AffiliateCommissionRow(
    string Id,
    string PaymentTransactionId,
    decimal AmountAmount,
    string Currency,
    string Status,
    DateTimeOffset AccruedAt,
    DateTimeOffset? PaidAt);

file static class AffiliatePortalHttpContextExtensions
{
    internal static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
