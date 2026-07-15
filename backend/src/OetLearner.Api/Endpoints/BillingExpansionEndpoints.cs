using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Content;

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
        adminMp.MapGet("/{id}/proof", GetManualPaymentProof).RequireAuthorization("AdminBillingRead");
        adminMp.MapPost("/{id}/approve", ApproveManualPayment).WithAdminWrite("AdminBillingRefundWrite");
        adminMp.MapPost("/{id}/reject", RejectManualPayment).WithAdminWrite("AdminBillingRefundWrite");
        adminMp.MapPost("/{id}/status", SetManualPaymentStatus).WithAdminWrite("AdminBillingRefundWrite");
        adminMp.MapPost("/{id}/waive-proof", WaiveManualPaymentProof).WithAdminWrite("AdminBillingRefundWrite");
        adminMp.MapPost("/{id}/reopen", ReopenManualPayment).WithAdminWrite("AdminBillingRefundWrite");

        // ── Admin: manual fulfilment queue ─────────────────────────
        var adminFul = v1.MapGroup("/admin/billing/fulfilment");
        adminFul.MapGet("/", ListPendingFulfilment).RequireAuthorization("AdminBillingRead");
        adminFul.MapPost("/subscriptions/{id}/mark-fulfilled", MarkSubscriptionFulfilled).WithAdminWrite("AdminBillingRefundWrite");

        // ── Admin: scholarships ────────────────────────────────────
        var adminSc = v1.MapGroup("/admin/billing/scholarships");
        adminSc.MapGet("/", ListScholarships).RequireAuthorization("AdminBillingRead");
        adminSc.MapPost("/", GrantScholarship).WithAdminWrite("AdminBillingCatalogWrite");
        adminSc.MapPost("/{id}/revoke", RevokeScholarship).WithAdminWrite("AdminBillingCatalogWrite");

        // ── Admin: affiliates ──────────────────────────────────────
        var adminAf = v1.MapGroup("/admin/billing/affiliates");
        adminAf.MapGet("/", ListAffiliates).RequireAuthorization("AdminBillingRead");
        adminAf.MapPost("/", CreateAffiliate).WithAdminWrite("AdminBillingCatalogWrite");
        adminAf.MapPut("/{id}", UpdateAffiliate).WithAdminWrite("AdminBillingCatalogWrite");

        // ── Admin: dunning + metrics ───────────────────────────────
        var adminDun = v1.MapGroup("/admin/billing/dunning");
        adminDun.MapGet("/", ListDunningCampaigns).RequireAuthorization("AdminBillingRead");

        var adminMetrics = v1.MapGroup("/admin/billing/metrics");
        adminMetrics.MapGet("/", ReadMetrics).RequireAuthorization("AdminBillingRead");
        adminMetrics.MapPost("/rollup", RollupMetrics).WithAdminWrite("AdminBillingRead");

        return app;
    }

    // ── Manual payments ────────────────────────────────────────────

    private static async Task<Results<Ok<ManualPaymentDto>, BadRequest<string>>> SubmitManualPayment(
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
        try
        {
            var row = await service.SubmitAsync(userId, new ManualPaymentSubmitRequest(
                request.QuoteId,
                request.AmountAmount,
                request.Currency,
                request.Method,
                request.Reference,
                request.ProofUrl,
                request.CandidateFullName,
                request.CandidateEmail,
                request.CandidateWhatsApp,
                request.CourseName,
                request.CourseId,
                request.PaymentCategory), proofBytes, ct);
            return TypedResults.Ok(ManualPaymentDto.FromEntity(row));
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    private static async Task<Ok<List<ManualPaymentDto>>> ListOwnManualPayments(HttpContext http, LearnerDbContext db, CancellationToken ct)
    {
        var userId = http.UserId();
        var rows = await db.ManualPaymentRequests
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.SubmittedAt)
            .ToListAsync(ct);
        var fulfilment = await LoadFulfilmentStatusesAsync(db, rows, ct);
        return TypedResults.Ok(rows.Select(r => ManualPaymentDto.FromEntity(r, Lookup(fulfilment, r))).ToList());
    }

    private static async Task<Ok<ManualPaymentListResponse>> ListManualPayments(
        LearnerDbContext db,
        [FromQuery] string? status,
        [FromQuery] string? kind,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var q = db.ManualPaymentRequests.AsQueryable();
        if (!string.IsNullOrEmpty(status)) q = q.Where(r => r.Status == status);
        if (!string.IsNullOrEmpty(kind)) q = q.Where(r => r.Kind == kind);

        var currentPage = Math.Max(1, page ?? 1);
        var size = Math.Clamp(pageSize ?? 50, 1, 200);
        var total = await q.CountAsync(ct);
        var rows = await q
            .OrderByDescending(r => r.SubmittedAt)
            .Skip((currentPage - 1) * size)
            .Take(size)
            .ToListAsync(ct);
        var fulfilment = await LoadFulfilmentStatusesAsync(db, rows, ct);
        return TypedResults.Ok(new ManualPaymentListResponse(
            total,
            currentPage,
            size,
            rows.Select(r => ManualPaymentDto.FromEntity(r, Lookup(fulfilment, r))).ToList()));
    }

    /// <summary>Fulfilment status of the subscription each proof granted, for the page of
    /// rows being returned — one extra query, never one per row.</summary>
    private static async Task<Dictionary<string, string>> LoadFulfilmentStatusesAsync(
        LearnerDbContext db,
        IReadOnlyCollection<ManualPaymentRequest> rows,
        CancellationToken ct)
    {
        var ids = rows
            .Select(r => r.AccessGrantedSubscriptionId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct()
            .ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
        return await db.Subscriptions
            .Where(s => ids.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.FulfilmentStatus, StringComparer.Ordinal, ct);
    }

    private static string? Lookup(Dictionary<string, string> fulfilment, ManualPaymentRequest row)
        => row.AccessGrantedSubscriptionId is { Length: > 0 } id && fulfilment.TryGetValue(id, out var value)
            ? value
            : null;

    private static async Task<Results<Ok<ManualPaymentDto>, BadRequest<string>>> ApproveManualPayment(string id, HttpContext http, ApproveRejectRequest request, IManualPaymentService service, CancellationToken ct)
    {
        try
        {
            return TypedResults.Ok(ManualPaymentDto.FromEntity(await service.ApproveAsync(id, http.UserId(), request.Notes, ct)));
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    private static async Task<Results<Ok<ManualPaymentDto>, BadRequest<string>>> RejectManualPayment(string id, HttpContext http, ApproveRejectRequest request, IManualPaymentService service, CancellationToken ct)
    {
        try
        {
            return TypedResults.Ok(ManualPaymentDto.FromEntity(await service.RejectAsync(id, http.UserId(), request.Notes ?? "Rejected by admin.", ct)));
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    private static async Task<Results<Ok<ManualPaymentDto>, BadRequest<string>>> SetManualPaymentStatus(string id, HttpContext http, ManualPaymentStatusRequest request, IManualPaymentService service, CancellationToken ct)
    {
        try
        {
            return TypedResults.Ok(ManualPaymentDto.FromEntity(await service.SetStatusAsync(id, http.UserId(), request.Status, request.Notes, ct)));
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    /// <summary>Release a pending offline order whose payment was confirmed out-of-band
    /// (owner saw the transfer land) without waiting for the learner to upload a file.</summary>
    private static async Task<Results<Ok<ManualPaymentDto>, BadRequest<string>>> WaiveManualPaymentProof(string id, HttpContext http, ManualPaymentWaiveProofRequest request, IManualPaymentService service, CancellationToken ct)
    {
        try
        {
            var row = await service.WaiveProofAsync(id, http.AdminId(), http.AdminName(), request.Reason ?? string.Empty, ct);
            return TypedResults.Ok(ManualPaymentDto.FromEntity(row));
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    /// <summary>Undo a mis-clicked Reject: rejected → pending.</summary>
    private static async Task<Results<Ok<ManualPaymentDto>, BadRequest<string>>> ReopenManualPayment(string id, HttpContext http, ApproveRejectRequest request, IManualPaymentService service, CancellationToken ct)
    {
        try
        {
            var row = await service.SetStatusAsync(id, http.UserId(), "pending", request.Notes, ct);
            return TypedResults.Ok(ManualPaymentDto.FromEntity(row));
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    // ── Manual fulfilment queue ────────────────────────────────────

    /// <summary>Orders that are paid but await an admin hand-over (Telegram invite,
    /// manual web release, physical material). These subscriptions stay Pending, so the
    /// entitlement resolver grants nothing until <see cref="MarkSubscriptionFulfilled"/>.
    /// </summary>
    private static async Task<Ok<List<PendingFulfilmentDto>>> ListPendingFulfilment(LearnerDbContext db, CancellationToken ct)
    {
        var subscriptions = await db.Subscriptions
            .Where(s => s.FulfilmentStatus == FulfilmentStatuses.PendingManual)
            .OrderBy(s => s.ChangedAt)
            .Take(200)
            .ToListAsync(ct);
        if (subscriptions.Count == 0)
        {
            return TypedResults.Ok(new List<PendingFulfilmentDto>());
        }

        var planCodes = subscriptions.Select(s => s.PlanId).Distinct().ToList();
        var plans = await db.BillingPlans
            .Where(p => planCodes.Contains(p.Code))
            .ToDictionaryAsync(p => p.Code, StringComparer.Ordinal, ct);

        var subscriptionIds = subscriptions.Select(s => s.Id).ToList();
        var proofs = await db.ManualPaymentRequests
            .Where(r => r.AccessGrantedSubscriptionId != null && subscriptionIds.Contains(r.AccessGrantedSubscriptionId!))
            .ToListAsync(ct);
        var proofBySubscription = proofs
            .GroupBy(r => r.AccessGrantedSubscriptionId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.SubmittedAt).First(), StringComparer.Ordinal);

        var userIds = subscriptions.Select(s => s.UserId).Distinct().ToList();
        var users = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.Email })
            .ToListAsync(ct);
        var userById = users.ToDictionary(u => u.Id, StringComparer.Ordinal);

        var items = subscriptions.Select(s =>
        {
            plans.TryGetValue(s.PlanId, out var plan);
            proofBySubscription.TryGetValue(s.Id, out var proof);
            userById.TryGetValue(s.UserId, out var user);
            return new PendingFulfilmentDto(
                s.Id,
                s.UserId,
                user?.DisplayName ?? proof?.CandidateFullName ?? string.Empty,
                user?.Email ?? proof?.CandidateEmail ?? string.Empty,
                s.PlanId,
                plan?.Name ?? proof?.CourseName ?? s.PlanId,
                plan?.DeliveryMethod ?? DeliveryMethods.ManualWeb,
                plan?.TelegramInviteUrl,
                plan?.DeliveryInstructions,
                s.Status.ToString(),
                s.FulfilmentStatus,
                s.StartedAt,
                s.ChangedAt,
                proof is null ? null : ManualPaymentDto.FromEntity(proof, s.FulfilmentStatus));
        }).ToList();

        return TypedResults.Ok(items);
    }

    private static async Task<Results<Ok<PendingFulfilmentDto>, NotFound, BadRequest<string>>> MarkSubscriptionFulfilled(
        string id,
        HttpContext http,
        ApproveRejectRequest request,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var subscription = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (subscription is null)
        {
            return TypedResults.NotFound();
        }
        if (subscription.FulfilmentStatus == FulfilmentStatuses.Fulfilled)
        {
            return TypedResults.BadRequest("This order has already been marked fulfilled.");
        }
        if (subscription.FulfilmentStatus != FulfilmentStatuses.PendingManual)
        {
            return TypedResults.BadRequest("Only an order awaiting manual fulfilment can be marked fulfilled.");
        }

        var now = DateTimeOffset.UtcNow;
        try
        {
            SubscriptionStateMachine.Transition(subscription, SubscriptionStatus.Active, "manual_fulfilment_completed");
        }
        catch (ApiException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
        subscription.FulfilmentStatus = FulfilmentStatuses.Fulfilled;
        subscription.ChangedAt = now;

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = now,
            ActorId = http.AdminId(),
            ActorName = http.AdminName(),
            Action = "subscription.mark_fulfilled",
            ResourceType = "Subscription",
            ResourceId = subscription.Id,
            Details = $"Marked {subscription.PlanId} fulfilled for {subscription.UserId}; access released."
                      + (string.IsNullOrWhiteSpace(request.Notes) ? string.Empty : $" Notes: {request.Notes}"),
        });
        await db.SaveChangesAsync(ct);

        var plan = await db.BillingPlans.FirstOrDefaultAsync(p => p.Code == subscription.PlanId, ct);
        var user = await db.Users
            .Where(u => u.Id == subscription.UserId)
            .Select(u => new { u.DisplayName, u.Email })
            .FirstOrDefaultAsync(ct);
        return TypedResults.Ok(new PendingFulfilmentDto(
            subscription.Id,
            subscription.UserId,
            user?.DisplayName ?? string.Empty,
            user?.Email ?? string.Empty,
            subscription.PlanId,
            plan?.Name ?? subscription.PlanId,
            plan?.DeliveryMethod ?? DeliveryMethods.ManualWeb,
            plan?.TelegramInviteUrl,
            plan?.DeliveryInstructions,
            subscription.Status.ToString(),
            subscription.FulfilmentStatus,
            subscription.StartedAt,
            subscription.ChangedAt,
            null));
    }

    /// <summary>
    /// Stream a manual-payment proof file inline for the admin verification
    /// dashboard. Proofs are stored as opaque blobs with no recorded MIME, so the
    /// content type is detected from magic bytes. Authorised by
    /// <c>AdminBillingRead</c>.
    /// </summary>
    private static async Task<IResult> GetManualPaymentProof(string id, HttpContext http, LearnerDbContext db, IFileStorage storage, CancellationToken ct)
    {
        var proofKey = await db.ManualPaymentRequests
            .Where(r => r.Id == id)
            .Select(r => r.ProofUrl)
            .FirstOrDefaultAsync(ct);
        if (proofKey is null)
        {
            return Results.NotFound();
        }
        if (string.IsNullOrWhiteSpace(proofKey) || !await storage.ExistsAsync(proofKey, ct))
        {
            return Results.NotFound();
        }

        byte[] bytes;
        await using (var source = await storage.OpenReadAsync(proofKey, ct))
        await using (var buffer = new MemoryStream())
        {
            await source.CopyToAsync(buffer, ct);
            bytes = buffer.ToArray();
        }

        var contentType = ManualPaymentProof.SniffContentType(bytes);
        // Proofs contain candidate PII — keep them out of shared/proxy caches.
        http.Response.Headers.CacheControl = "private, no-store";
        http.Response.Headers.Vary = "Authorization";
        // Serve user-uploaded bytes with the sniffed type only — never let the
        // browser MIME-sniff a disguised payload (submit already restricts to
        // image/pdf magic bytes; this is defense-in-depth).
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        // No download filename → inline display in the admin viewer.
        return Results.File(bytes, contentType);
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
    string ProofBase64,
    string CandidateFullName,
    string CandidateEmail,
    string CandidateWhatsApp,
    string CourseName,
    string? CourseId,
    string PaymentCategory);

/// <summary>Client view of a proof row. Carries <see cref="HasProof"/> rather than the
/// storage key — <c>ProofUrl</c> is an internal <c>IFileStorage</c> path and handing it to
/// a client leaks the layout of the proof bucket. Fetch the file itself from
/// <c>GET /v1/admin/billing/manual-payments/{id}/proof</c>.</summary>
public sealed record ManualPaymentDto(
    string Id,
    string UserId,
    decimal AmountAmount,
    string Currency,
    string Method,
    string Reference,
    bool HasProof,
    string Kind,
    string? Gateway,
    string? ProfessionId,
    DateTimeOffset? ProofWaivedAt,
    string? ProofWaiverReason,
    string Status,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? ReviewedAt,
    string? AdminNotes,
    string CandidateFullName,
    string CandidateEmail,
    string CandidateWhatsApp,
    string CourseName,
    string? CourseId,
    string PaymentCategory,
    string? AccessGrantedSubscriptionId,
    string? FulfilmentStatus)
{
    public static ManualPaymentDto FromEntity(ManualPaymentRequest r, string? fulfilmentStatus = null) => new(
        r.Id,
        r.UserId,
        r.AmountAmount,
        r.Currency,
        r.Method,
        r.Reference,
        !string.IsNullOrWhiteSpace(r.ProofUrl),
        r.Kind,
        r.Gateway,
        r.ProfessionId,
        r.ProofWaivedAt,
        r.ProofWaiverReason,
        r.Status,
        r.SubmittedAt,
        r.ReviewedAt,
        r.AdminNotes,
        r.CandidateFullName,
        r.CandidateEmail,
        r.CandidateWhatsApp,
        r.CourseName,
        r.CourseId,
        r.PaymentCategory,
        r.AccessGrantedSubscriptionId,
        fulfilmentStatus);
}

public sealed record ManualPaymentListResponse(
    int Total,
    int Page,
    int PageSize,
    IReadOnlyList<ManualPaymentDto> Items);

/// <summary>A paid order awaiting an admin hand-over, with everything the admin needs to
/// complete it (the Telegram invite / delivery instructions) and the proof behind it.</summary>
public sealed record PendingFulfilmentDto(
    string SubscriptionId,
    string UserId,
    string DisplayName,
    string Email,
    string PlanCode,
    string PlanName,
    string DeliveryMethod,
    string? TelegramInviteUrl,
    string? DeliveryInstructions,
    string Status,
    string FulfilmentStatus,
    DateTimeOffset StartedAt,
    DateTimeOffset ChangedAt,
    ManualPaymentDto? Proof);

public sealed record ApproveRejectRequest(string? Notes);

public sealed record ManualPaymentStatusRequest(string Status, string? Notes);

public sealed record ManualPaymentWaiveProofRequest(string? Reason);

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

    internal static string AdminId(this HttpContext httpContext)
        => httpContext.UserId();

    internal static string AdminName(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.Name) ?? "Admin";
}
