using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Endpoints for the Phase 4-10 surfaces: manual payments, scholarships,
/// affiliates, dunning campaigns, billing metrics. Backed by services in
/// <c>OetLearner.Api.Services.Billing</c>.
/// </summary>
public static class BillingExpansionEndpoints
{
    public static IEndpointRouteBuilder MapBillingExpansionEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1");

        // ── Learner-facing ─────────────────────────────────────────
        var billing = v1.MapGroup("/billing").RequireAuthorization();
        billing.MapPost("/manual-payments", SubmitManualPayment);
        billing.MapGet("/manual-payments/mine", ListOwnManualPayments);

        // ── Admin: manual payments ─────────────────────────────────
        var adminMp = v1.MapGroup("/admin/billing/manual-payments");
        adminMp.MapGet("/", ListManualPayments).RequireAuthorization("AdminBillingRead");
        adminMp.MapPost("/{id}/approve", ApproveManualPayment).RequireAuthorization("AdminBillingRefundWrite");
        adminMp.MapPost("/{id}/reject", RejectManualPayment).RequireAuthorization("AdminBillingRefundWrite");

        // ── Admin: scholarships ────────────────────────────────────
        var adminSc = v1.MapGroup("/admin/billing/scholarships");
        adminSc.MapGet("/", ListScholarships).RequireAuthorization("AdminBillingRead");
        adminSc.MapPost("/", GrantScholarship).RequireAuthorization("AdminBillingCatalogWrite");
        adminSc.MapPost("/{id}/revoke", RevokeScholarship).RequireAuthorization("AdminBillingCatalogWrite");

        // ── Admin: affiliates ──────────────────────────────────────
        var adminAf = v1.MapGroup("/admin/billing/affiliates");
        adminAf.MapGet("/", ListAffiliates).RequireAuthorization("AdminBillingRead");
        adminAf.MapPost("/", CreateAffiliate).RequireAuthorization("AdminBillingCatalogWrite");
        adminAf.MapPut("/{id}", UpdateAffiliate).RequireAuthorization("AdminBillingCatalogWrite");

        // ── Admin: dunning + metrics ───────────────────────────────
        var adminDun = v1.MapGroup("/admin/billing/dunning");
        adminDun.MapGet("/", ListDunningCampaigns).RequireAuthorization("AdminBillingRead");

        var adminMetrics = v1.MapGroup("/admin/billing/metrics");
        adminMetrics.MapGet("/", ReadMetrics).RequireAuthorization("AdminBillingRead");
        adminMetrics.MapPost("/rollup", RollupMetrics).RequireAuthorization("AdminBillingRead");

        return app;
    }

    // ── Manual payments ────────────────────────────────────────────

    private static async Task<Results<Ok<ManualPaymentRequest>, BadRequest<string>>> SubmitManualPayment(
        HttpContext http,
        ManualPaymentSubmitRequestDto request,
        IManualPaymentService service,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ProofBase64))
        {
            return TypedResults.BadRequest("proofBase64 is required.");
        }

        byte[] proofBytes;
        try
        {
            proofBytes = Convert.FromBase64String(request.ProofBase64);
        }
        catch (FormatException)
        {
            return TypedResults.BadRequest("proofBase64 is not valid base64.");
        }

        var userId = http.UserId();
        var row = await service.SubmitAsync(userId, new ManualPaymentSubmitRequest(
            request.QuoteId,
            request.AmountAmount,
            request.Currency,
            request.Method,
            request.Reference,
            request.ProofUrl), proofBytes, ct);
        return TypedResults.Ok(row);
    }

    private static async Task<Ok<List<ManualPaymentDto>>> ListOwnManualPayments(HttpContext http, LearnerDbContext db, CancellationToken ct)
    {
        var userId = http.UserId();
        var rows = await db.ManualPaymentRequests
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.SubmittedAt)
            .Select(r => new ManualPaymentDto(r.Id, r.UserId, r.AmountAmount, r.Currency, r.Method, r.Reference, r.ProofUrl, r.Status, r.SubmittedAt, r.ReviewedAt, r.AdminNotes))
            .ToListAsync(ct);
        return TypedResults.Ok(rows);
    }

    private static async Task<Ok<List<ManualPaymentDto>>> ListManualPayments(LearnerDbContext db, [FromQuery] string? status, CancellationToken ct)
    {
        var q = db.ManualPaymentRequests.AsQueryable();
        if (!string.IsNullOrEmpty(status)) q = q.Where(r => r.Status == status);
        var rows = await q
            .OrderByDescending(r => r.SubmittedAt)
            .Select(r => new ManualPaymentDto(r.Id, r.UserId, r.AmountAmount, r.Currency, r.Method, r.Reference, r.ProofUrl, r.Status, r.SubmittedAt, r.ReviewedAt, r.AdminNotes))
            .Take(200)
            .ToListAsync(ct);
        return TypedResults.Ok(rows);
    }

    private static async Task<Results<Ok<ManualPaymentRequest>, BadRequest<string>>> ApproveManualPayment(string id, HttpContext http, ApproveRejectRequest request, IManualPaymentService service, CancellationToken ct)
    {
        try
        {
            return TypedResults.Ok(await service.ApproveAsync(id, http.UserId(), request.Notes, ct));
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    private static async Task<Results<Ok<ManualPaymentRequest>, BadRequest<string>>> RejectManualPayment(string id, HttpContext http, ApproveRejectRequest request, IManualPaymentService service, CancellationToken ct)
    {
        try
        {
            return TypedResults.Ok(await service.RejectAsync(id, http.UserId(), request.Notes ?? "Rejected by admin.", ct));
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    // ── Scholarships ───────────────────────────────────────────────

    private static async Task<Ok<List<Scholarship>>> ListScholarships(LearnerDbContext db, [FromQuery] string? status, CancellationToken ct)
    {
        var q = db.Scholarships.AsQueryable();
        if (!string.IsNullOrEmpty(status)) q = q.Where(s => s.Status == status);
        var rows = await q.OrderByDescending(s => s.GrantedAt).Take(200).ToListAsync(ct);
        return TypedResults.Ok(rows);
    }

    private static async Task<Ok<Scholarship>> GrantScholarship(HttpContext http, ScholarshipGrantRequest request, LearnerDbContext db, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var scholarship = new Scholarship
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = request.UserId,
            GrantedByAdminId = http.UserId(),
            Reason = request.Reason,
            AccessTier = request.AccessTier,
            EntitlementsJson = string.IsNullOrEmpty(request.EntitlementsJson) ? "{}" : request.EntitlementsJson,
            GrantedAt = now,
            ExpiresAt = request.ExpiresAt,
            Status = "active",
            AdminNotes = request.AdminNotes,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Scholarships.Add(scholarship);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(scholarship);
    }

    private static async Task<Results<Ok<Scholarship>, NotFound>> RevokeScholarship(string id, HttpContext http, LearnerDbContext db, CancellationToken ct)
    {
        var sc = await db.Scholarships.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sc is null) return TypedResults.NotFound();
        sc.Status = "revoked";
        sc.RevokedAt = DateTimeOffset.UtcNow;
        sc.RevokedByAdminId = http.UserId();
        sc.UpdatedAt = sc.RevokedAt.Value;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(sc);
    }

    // ── Affiliates ─────────────────────────────────────────────────

    private static async Task<Ok<List<Affiliate>>> ListAffiliates(LearnerDbContext db, CancellationToken ct)
    {
        var rows = await db.Affiliates.OrderBy(a => a.OwnerName).ToListAsync(ct);
        return TypedResults.Ok(rows);
    }

    private static async Task<Results<Ok<Affiliate>, BadRequest<string>>> CreateAffiliate(AffiliateUpsertRequest request, LearnerDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.OwnerName))
        {
            return TypedResults.BadRequest("Code and ownerName are required.");
        }
        var dup = await db.Affiliates.AnyAsync(a => a.Code == request.Code, ct);
        if (dup) return TypedResults.BadRequest("Affiliate code already in use.");

        var now = DateTimeOffset.UtcNow;
        var affiliate = new Affiliate
        {
            Id = Guid.NewGuid().ToString("N"),
            Code = request.Code,
            OwnerName = request.OwnerName,
            ContactEmail = request.ContactEmail,
            CommissionPercent = request.CommissionPercent,
            CookieDays = request.CookieDays ?? 30,
            PayoutThresholdAmount = request.PayoutThresholdAmount,
            PayoutCurrency = (request.PayoutCurrency ?? "USD").ToUpperInvariant(),
            PayoutMethod = request.PayoutMethod ?? "bank_transfer",
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Affiliates.Add(affiliate);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(affiliate);
    }

    private static async Task<Results<Ok<Affiliate>, NotFound>> UpdateAffiliate(string id, AffiliateUpsertRequest request, LearnerDbContext db, CancellationToken ct)
    {
        var row = await db.Affiliates.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (row is null) return TypedResults.NotFound();
        row.OwnerName = request.OwnerName;
        row.ContactEmail = request.ContactEmail;
        row.CommissionPercent = request.CommissionPercent;
        row.CookieDays = request.CookieDays ?? row.CookieDays;
        row.PayoutThresholdAmount = request.PayoutThresholdAmount;
        row.PayoutCurrency = (request.PayoutCurrency ?? row.PayoutCurrency).ToUpperInvariant();
        row.PayoutMethod = request.PayoutMethod ?? row.PayoutMethod;
        row.Status = request.Status ?? row.Status;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(row);
    }

    // ── Dunning + metrics (read-only) ──────────────────────────────

    private static async Task<Ok<List<DunningCampaign>>> ListDunningCampaigns(LearnerDbContext db, [FromQuery] string? status, CancellationToken ct)
    {
        var q = db.DunningCampaigns.AsQueryable();
        if (!string.IsNullOrEmpty(status)) q = q.Where(c => c.Status == status);
        return TypedResults.Ok(await q.OrderByDescending(c => c.StartedAt).Take(200).ToListAsync(ct));
    }

    private static async Task<Ok<List<BillingMetricDaily>>> ReadMetrics(IBillingMetricsService service, [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] string? code, [FromQuery] string? region, CancellationToken ct)
    {
        var rows = await service.ReadAsync(from, to, code, region, ct);
        return TypedResults.Ok(rows.ToList());
    }

    private static async Task<Ok<string>> RollupMetrics(IBillingMetricsService service, [FromQuery] DateOnly? date, CancellationToken ct)
    {
        var target = date ?? DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-1));
        await service.RollupAsync(target, ct);
        return TypedResults.Ok($"Rolled up {target:yyyy-MM-dd}.");
    }
}

// ── DTOs ────────────────────────────────────────────────────────────

public sealed record ManualPaymentSubmitRequestDto(
    string? QuoteId,
    decimal AmountAmount,
    string Currency,
    string Method,
    string Reference,
    string ProofUrl,
    string ProofBase64);

public sealed record ManualPaymentDto(
    string Id,
    string UserId,
    decimal AmountAmount,
    string Currency,
    string Method,
    string Reference,
    string ProofUrl,
    string Status,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? ReviewedAt,
    string? AdminNotes);

public sealed record ApproveRejectRequest(string? Notes);

public sealed record ScholarshipGrantRequest(
    string UserId,
    string Reason,
    string AccessTier,
    string? EntitlementsJson,
    DateTimeOffset? ExpiresAt,
    string? AdminNotes);

public sealed record AffiliateUpsertRequest(
    string Code,
    string OwnerName,
    string ContactEmail,
    decimal CommissionPercent,
    int? CookieDays,
    decimal PayoutThresholdAmount,
    string? PayoutCurrency,
    string? PayoutMethod,
    string? Status);

file static class BillingExpansionHttpContextExtensions
{
    internal static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
