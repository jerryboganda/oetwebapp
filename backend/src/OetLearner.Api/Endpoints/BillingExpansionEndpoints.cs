using System.Data;
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

    /// <summary>Orders that are paid but await an admin hand-over (WhatsApp access,
    /// manual web release, physical material). New purchases stay Pending; legacy orders
    /// are selected by paid fulfilment state rather than subscription status.
    /// </summary>
    private static async Task<Ok<List<PendingFulfilmentDto>>> ListPendingFulfilment(LearnerDbContext db, CancellationToken ct)
    {
        var gatewayPaidSubscriptionIds =
                from quote in db.BillingQuotes
                join payment in db.PaymentTransactions on quote.Id equals payment.QuoteId
                where quote.SubscriptionId != null && payment.Status == "completed"
                select quote.SubscriptionId!;
        var proofPaidSubscriptionIds = db.ManualPaymentRequests
            .Where(proof => proof.AccessGrantedSubscriptionId != null
                && (proof.Status == "paid" || proof.Status == "approved"))
            .Select(proof => proof.AccessGrantedSubscriptionId!);
        var paidSubscriptionIds = gatewayPaidSubscriptionIds
            .Union(proofPaidSubscriptionIds);

        var subscriptions = await db.Subscriptions
            .Where(s => s.Status != SubscriptionStatus.Draft
                && (s.FulfilmentStatus == FulfilmentStatuses.PendingManual
                    || s.FulfilmentStatus == FulfilmentStatuses.PendingVerification)
                && paidSubscriptionIds.Contains(s.Id))
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
        var userIds = subscriptions.Select(s => s.UserId).Distinct().ToList();

        var proofs = await db.ManualPaymentRequests
            .Where(r => (r.AccessGrantedSubscriptionId != null && subscriptionIds.Contains(r.AccessGrantedSubscriptionId!))
                || userIds.Contains(r.UserId))
            .ToListAsync(ct);
        var proofBySubscription = new Dictionary<string, ManualPaymentRequest>(StringComparer.Ordinal);
        foreach (var s in subscriptions)
        {
            var match = proofs.FirstOrDefault(r => string.Equals(r.AccessGrantedSubscriptionId, s.Id, StringComparison.Ordinal))
                ?? proofs.OrderByDescending(r => r.SubmittedAt).FirstOrDefault(r => r.UserId == s.UserId && (r.CourseId == s.PlanId || r.CourseName == s.PlanId));
            if (match is not null)
            {
                proofBySubscription[s.Id] = match;
            }
        }

        var users = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.Email })
            .ToListAsync(ct);
        var userById = users.ToDictionary(u => u.Id, StringComparer.Ordinal);
        var versionIds = subscriptions
            .Select(s => s.PlanVersionId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var versions = versionIds.Count == 0
            ? new Dictionary<string, BillingPlanVersion>(StringComparer.Ordinal)
            : await db.BillingPlanVersions
                .Where(v => versionIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, StringComparer.Ordinal, ct);

        var quoteRows = await db.BillingQuotes.AsNoTracking()
            .Where(q => q.SubscriptionId != null && subscriptionIds.Contains(q.SubscriptionId!))
            .OrderByDescending(q => q.CreatedAt)
            .ToListAsync(ct);
        var quoteIds = quoteRows.Select(q => q.Id).ToList();
        var transactions = quoteIds.Count == 0
            ? new List<PaymentTransaction>()
            : await db.PaymentTransactions.AsNoTracking()
                .Where(t => t.QuoteId != null && quoteIds.Contains(t.QuoteId!) && t.Status == "completed")
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync(ct);
        var txByQuote = transactions
            .Where(transaction => transaction.QuoteId is not null)
            .GroupBy(transaction => transaction.QuoteId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var items = subscriptions.Select(s =>
        {
            plans.TryGetValue(s.PlanId, out var plan);
            proofBySubscription.TryGetValue(s.Id, out var proof);
            userById.TryGetValue(s.UserId, out var user);
            versions.TryGetValue(s.PlanVersionId ?? string.Empty, out var version);
            var subscriptionQuotes = quoteRows.Where(row => row.SubscriptionId == s.Id);
            var quote = !string.IsNullOrWhiteSpace(proof?.QuoteId)
                ? subscriptionQuotes.FirstOrDefault(row => row.Id == proof.QuoteId)
                : subscriptionQuotes.FirstOrDefault(row => txByQuote.ContainsKey(row.Id));
            var tx = proof?.PaymentTransactionId is { } proofTransactionId
                ? transactions.FirstOrDefault(row => row.Id == proofTransactionId)
                : quote is not null && txByQuote.TryGetValue(quote.Id, out var quoteTransaction)
                    ? quoteTransaction
                    : null;
            var externalOnly = version is not null
                ? ManualDeliveryPolicy.IsExternalOnly(version)
                : string.IsNullOrWhiteSpace(s.PlanVersionId) && ManualDeliveryPolicy.IsExternalOnly(plan);
            var amount = proof?.AmountAmount ?? tx?.Amount ?? quote?.TotalAmount ?? s.PriceAmount;
            var currency = proof?.Currency ?? tx?.Currency ?? quote?.Currency ?? s.Currency;
            var gateway = proof?.Gateway ?? tx?.Gateway ?? "online";
            var transactionId = proof?.Reference ?? tx?.GatewayTransactionId ?? tx?.CaptureId ?? quote?.CheckoutSessionId ?? string.Empty;
            var paymentMethod = proof?.Method ?? tx?.Gateway ?? "online";
            DateTimeOffset? paidAt = proof?.SubmittedAt ?? tx?.CreatedAt ?? quote?.CreatedAt ?? s.StartedAt;
            return new PendingFulfilmentDto(
                s.Id,
                s.UserId,
                user?.DisplayName ?? proof?.CandidateFullName ?? string.Empty,
                user?.Email ?? proof?.CandidateEmail ?? string.Empty,
                s.PlanId,
                version?.Name ?? plan?.Name ?? proof?.CourseName ?? s.PlanId,
                version?.DeliveryMethod ?? plan?.DeliveryMethod ?? DeliveryMethods.ManualWeb,
                version?.TelegramInviteUrl ?? plan?.TelegramInviteUrl,
                version?.DeliveryInstructions ?? plan?.DeliveryInstructions,
                s.Status.ToString(),
                s.FulfilmentStatus,
                s.StartedAt,
                s.ChangedAt,
                proof is null ? null : ManualPaymentDto.FromEntity(proof, s.FulfilmentStatus),
                false,
                externalOnly,
                amount,
                currency,
                gateway,
                transactionId,
                paymentMethod,
                paidAt,
                quote?.Id,
                tx?.Status ?? (proof?.Status == "paid" ? "completed" : "verified"));
        }).ToList();

        return TypedResults.Ok(items);
    }

    private static async Task<Results<Ok<PendingFulfilmentDto>, NotFound, BadRequest<string>>> MarkSubscriptionFulfilled(
        string id,
        HttpContext http,
        ApproveRejectRequest request,
        LearnerDbContext db,
        IAiPackageCreditService? aiPackageCredits,
        IManualPaymentService manualPayments,
        CancellationToken ct)
    {
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
            : null;

        var subscription = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (subscription is null)
        {
            return TypedResults.NotFound();
        }
        if (subscription.FulfilmentStatus == FulfilmentStatuses.Fulfilled)
        {
            return TypedResults.Ok(await BuildPendingFulfilmentResultAsync(subscription, db, webAccessReleased: subscription.Status == SubscriptionStatus.Active, ct));
        }
        if (subscription.FulfilmentStatus != FulfilmentStatuses.PendingManual
            && subscription.FulfilmentStatus != FulfilmentStatuses.PendingVerification)
        {
            return TypedResults.BadRequest("Only an order awaiting fulfilment can be marked fulfilled.");
        }

        var plan = await db.BillingPlans.FirstOrDefaultAsync(p => p.Code == subscription.PlanId, ct);
        BillingPlanVersion? purchasedVersion = null;
        if (!string.IsNullOrWhiteSpace(subscription.PlanVersionId))
        {
            purchasedVersion = await db.BillingPlanVersions
                .FirstOrDefaultAsync(v => v.Id == subscription.PlanVersionId, ct);
            if (purchasedVersion is null)
            {
                return TypedResults.BadRequest("The purchased plan version is missing. Delivery was not marked and no access was released.");
            }
        }
        if (db.Database.IsRelational())
        {
            var claimed = await db.Subscriptions
                .Where(row => row.Id == subscription.Id
                    && (row.FulfilmentStatus == FulfilmentStatuses.PendingManual
                        || row.FulfilmentStatus == FulfilmentStatuses.PendingVerification))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.FulfilmentStatus, FulfilmentStatuses.Processing)
                    .SetProperty(row => row.ChangedAt, DateTimeOffset.UtcNow), ct);
            if (claimed == 0)
            {
                var current = await db.Subscriptions.AsNoTracking()
                    .FirstOrDefaultAsync(row => row.Id == subscription.Id, ct);
                if (current?.FulfilmentStatus == FulfilmentStatuses.Fulfilled)
                {
                    return TypedResults.Ok(await BuildPendingFulfilmentResultAsync(
                        current,
                        db,
                        webAccessReleased: current.Status == SubscriptionStatus.Active,
                        ct: ct));
                }
                return TypedResults.BadRequest("This order is already being fulfilled.");
            }
        }
        subscription.FulfilmentStatus = FulfilmentStatuses.Processing;

        var externalOnly = purchasedVersion is not null
            ? ManualDeliveryPolicy.IsExternalOnly(purchasedVersion)
            : ManualDeliveryPolicy.IsExternalOnly(plan);
        var now = DateTimeOffset.UtcNow;
        var gatewayReceipt = await db.ManualPaymentRequests
            .Where(row => row.AccessGrantedSubscriptionId == subscription.Id
                && row.Kind == PaymentProofKinds.GatewayReceipt
                && row.Status == "paid")
            .OrderByDescending(row => row.SubmittedAt)
            .FirstOrDefaultAsync(ct);
        var gatewayReceiptWasApprovedInThisPass = false;
        if (gatewayReceipt is not null)
        {
            await manualPayments.ApproveAsync(gatewayReceipt.Id, http.AdminId(), request.Notes, ct);
            gatewayReceiptWasApprovedInThisPass = true;
        }

        if (!externalOnly)
        {
            var isPlanOrder = await (
                from proof in db.ManualPaymentRequests
                join payment in db.PaymentTransactions
                    on proof.PaymentTransactionId equals payment.Id
                where proof.AccessGrantedSubscriptionId == subscription.Id
                    && payment.ProductType == "plan"
                select payment.Id).AnyAsync(ct);
            if (isPlanOrder)
            {
                var replacedSubscriptions = await db.Subscriptions
                    .Where(row => row.UserId == subscription.UserId
                        && row.Id != subscription.Id
                        && SubscriptionStateMachine.CurrentOwnershipStatuses.Contains(row.Status))
                    .ToListAsync(ct);
                foreach (var replaced in replacedSubscriptions)
                {
                    SubscriptionStateMachine.Transition(
                        replaced,
                        SubscriptionStatus.Cancelled,
                        "replaced_by_verified_plan_purchase");
                    replaced.ChangedAt = now;
                }
            }

            try
            {
                SubscriptionStateMachine.Transition(subscription, SubscriptionStatus.Active, "manual_fulfilment_completed");
            }
            catch (ApiException ex)
            {
                return TypedResults.BadRequest(ex.Message);
            }
        }
        subscription.FulfilmentStatus = FulfilmentStatuses.Fulfilled;
        subscription.ChangedAt = now;

        var targetPlanCode = plan?.Code ?? purchasedVersion?.Code ?? subscription.PlanId;
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == subscription.UserId, ct);
        if (user is not null && !string.IsNullOrWhiteSpace(targetPlanCode))
        {
            user.CurrentPlanId = targetPlanCode;
        }

        if (!gatewayReceiptWasApprovedInThisPass)
        {
            // Grant included review credits (wallet credits)
            var includedCredits = purchasedVersion?.IncludedCredits ?? plan?.IncludedCredits ?? 0;
            if (includedCredits > 0)
            {
                var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == subscription.UserId, ct);
                if (wallet is null)
                {
                    wallet = new Wallet
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        UserId = subscription.UserId,
                        CreditBalance = 0,
                        LedgerSummaryJson = "[]",
                        LastUpdatedAt = now,
                    };
                    db.Wallets.Add(wallet);
                    await db.SaveChangesAsync(ct);
                }

                var existingWalletGrant = await db.WalletTransactions.FirstOrDefaultAsync(
                    x => x.WalletId == wallet.Id
                         && x.TransactionType == "plan_grant"
                         && x.ReferenceType == "subscription"
                         && x.ReferenceId == subscription.Id, ct);
                if (existingWalletGrant is null)
                {
                    wallet.CreditBalance += includedCredits;
                    wallet.LastUpdatedAt = now;
                    db.WalletTransactions.Add(new WalletTransaction
                    {
                        Id = Guid.NewGuid(),
                        WalletId = wallet.Id,
                        TransactionType = "plan_grant",
                        Amount = includedCredits,
                        BalanceAfter = wallet.CreditBalance,
                        ReferenceType = "subscription",
                        ReferenceId = subscription.Id,
                        Description = $"Included credits for {plan?.Name ?? subscription.PlanId}",
                        CreatedBy = http.AdminId(),
                        CreatedAt = now,
                    });
                }
            }

            // Grant bundled AI credits (gift credits)
            var aiCredits = purchasedVersion?.BundledAiCredits ?? plan?.BundledAiCredits ?? 0;
            if (aiCredits > 0 && aiPackageCredits is not null)
            {
                var durationMonths = plan?.DurationMonths ?? purchasedVersion?.DurationMonths ?? 0;
                var giftExpiry = durationMonths > 0 ? now.AddMonths(durationMonths) : now.AddDays(180);
                await aiPackageCredits.GrantCourseGiftCreditsAsync(
                    subscription.UserId,
                    targetPlanCode,
                    plan?.Name ?? subscription.PlanId,
                    aiCredits,
                     $"plan:{subscription.Id}:{targetPlanCode}",
                     giftExpiry,
                     ct,
                     AiPackageCreditSources.Plan(subscription.Id, targetPlanCode),
                     subscription.StartedAt);
            }
        }

        // Mark linked ManualPaymentRequest rows as paid
        var linkedProofs = await db.ManualPaymentRequests
            .Where(r => r.AccessGrantedSubscriptionId == subscription.Id
                || (r.UserId == subscription.UserId && (r.CourseId == targetPlanCode || r.CourseName == targetPlanCode) && r.Status == "pending"))
            .ToListAsync(ct);
        foreach (var pr in linkedProofs)
        {
            pr.Status = "paid";
            pr.AccessGrantedSubscriptionId = subscription.Id;
            pr.ReviewedAt = now;
            pr.ReviewedByAdminId = http.AdminId();
            if (!string.IsNullOrWhiteSpace(request.Notes))
            {
                pr.AdminNotes = request.Notes;
            }
            pr.UpdatedAt = now;
        }

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = now,
            ActorId = http.AdminId(),
            ActorName = http.AdminName(),
            Action = "subscription.mark_fulfilled",
            ResourceType = "Subscription",
            ResourceId = subscription.Id,
            Details = (externalOnly
                ? $"Marked {subscription.PlanId} externally delivered for {subscription.UserId}; no platform access released."
                : $"Marked {subscription.PlanId} fulfilled for {subscription.UserId}; access released.")
                      + (string.IsNullOrWhiteSpace(request.Notes) ? string.Empty : $" Notes: {request.Notes}"),
        });
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
        }

        return TypedResults.Ok(await BuildPendingFulfilmentResultAsync(subscription, db, !externalOnly, ct));
    }

    private static async Task<PendingFulfilmentDto> BuildPendingFulfilmentResultAsync(
        Subscription subscription,
        LearnerDbContext db,
        bool webAccessReleased,
        CancellationToken ct)
    {
        var plan = await db.BillingPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Code == subscription.PlanId, ct);
        var version = string.IsNullOrWhiteSpace(subscription.PlanVersionId)
            ? null
            : await db.BillingPlanVersions.AsNoTracking()
                .FirstOrDefaultAsync(row => row.Id == subscription.PlanVersionId, ct);
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == subscription.UserId)
            .Select(u => new { u.DisplayName, u.Email })
            .FirstOrDefaultAsync(ct);
        var proof = await db.ManualPaymentRequests.AsNoTracking()
            .Where(row => row.AccessGrantedSubscriptionId == subscription.Id)
            .OrderByDescending(row => row.SubmittedAt)
            .FirstOrDefaultAsync(ct);
        var quote = !string.IsNullOrWhiteSpace(proof?.QuoteId)
            ? await db.BillingQuotes.AsNoTracking()
                .FirstOrDefaultAsync(row =>
                    row.Id == proof.QuoteId && row.SubscriptionId == subscription.Id, ct)
            : await (
                    from order in db.BillingQuotes.AsNoTracking()
                    join paid in db.PaymentTransactions.AsNoTracking() on order.Id equals paid.QuoteId
                    where order.SubscriptionId == subscription.Id && paid.Status == "completed"
                    orderby paid.CreatedAt descending
                    select order)
                .FirstOrDefaultAsync(ct);
        var payment = proof?.PaymentTransactionId is { } proofTransactionId
            ? await db.PaymentTransactions.AsNoTracking()
                .FirstOrDefaultAsync(row => row.Id == proofTransactionId && row.Status == "completed", ct)
            : quote is null
                ? null
                : await db.PaymentTransactions.AsNoTracking()
                    .Where(row => row.QuoteId == quote.Id && row.Status == "completed")
                    .OrderByDescending(row => row.CreatedAt)
                    .FirstOrDefaultAsync(ct);
        return new PendingFulfilmentDto(
            subscription.Id,
            subscription.UserId,
            user?.DisplayName ?? string.Empty,
            user?.Email ?? string.Empty,
            subscription.PlanId,
            version?.Name ?? plan?.Name ?? subscription.PlanId,
            version?.DeliveryMethod ?? plan?.DeliveryMethod ?? DeliveryMethods.ManualWeb,
            version?.TelegramInviteUrl ?? plan?.TelegramInviteUrl,
            version?.DeliveryInstructions ?? plan?.DeliveryInstructions,
            subscription.Status.ToString(),
            subscription.FulfilmentStatus,
            subscription.StartedAt,
            subscription.ChangedAt,
            proof is null ? null : ManualPaymentDto.FromEntity(proof, subscription.FulfilmentStatus),
            webAccessReleased,
            version is not null
                ? ManualDeliveryPolicy.IsExternalOnly(version)
                : plan is not null && ManualDeliveryPolicy.IsExternalOnly(plan),
            proof?.AmountAmount ?? payment?.Amount ?? quote?.TotalAmount ?? subscription.PriceAmount,
            proof?.Currency ?? payment?.Currency ?? quote?.Currency ?? subscription.Currency,
            proof?.Gateway ?? payment?.Gateway,
            proof?.Reference ?? payment?.GatewayTransactionId ?? payment?.CaptureId,
            proof?.Method ?? payment?.Gateway,
            proof?.SubmittedAt ?? payment?.UpdatedAt,
            quote?.Id,
            payment?.Status ?? (proof?.Status == "paid" ? "completed" : "verified"));
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
    string? FulfilmentStatus,
    string? QuoteId)
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
        fulfilmentStatus,
        r.QuoteId);
}

public sealed record ManualPaymentListResponse(
    int Total,
    int Page,
    int PageSize,
    IReadOnlyList<ManualPaymentDto> Items);

/// <summary>A paid order awaiting an admin hand-over, with everything the admin needs to
/// complete it (the WhatsApp access link / delivery instructions) and the proof behind it.</summary>
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
    ManualPaymentDto? Proof,
    bool WebAccessReleased = false,
    bool ExternalOnly = false,
    decimal Amount = 0,
    string Currency = "AUD",
    string? Gateway = null,
    string? TransactionId = null,
    string? PaymentMethod = null,
    DateTimeOffset? PaidAt = null,
    string? OrderId = null,
    string PaymentStatus = "completed");

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
