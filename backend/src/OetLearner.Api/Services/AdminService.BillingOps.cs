using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Endpoints;
using OetLearner.Api.Security;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Conversation;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Services;

public partial class AdminService
{

    public async Task<AdminBillingEntitlementDiagnosticsResponse> GetBillingEntitlementDiagnosticsAsync(CancellationToken ct)
    {
        var generatedAt = DateTimeOffset.UtcNow;
        var activeSubscriptions = await db.Subscriptions.AsNoTracking()
            .Where(subscription => subscription.Status == SubscriptionStatus.Active || subscription.Status == SubscriptionStatus.Trial)
            .OrderByDescending(subscription => subscription.ChangedAt)
            .ThenByDescending(subscription => subscription.StartedAt)
            .Select(subscription => new
            {
                subscription.Id,
                subscription.UserId,
                subscription.PlanId,
                subscription.Status
            })
            .ToListAsync(ct);

        var subscriptionPlanRefs = activeSubscriptions
            .Select(subscription => subscription.PlanId.Trim())
            .Where(planId => !string.IsNullOrWhiteSpace(planId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allPlans = await db.BillingPlans.AsNoTracking().ToListAsync(ct);
        var plans = allPlans
            .Where(plan => (plan.Status == BillingPlanStatus.Active && plan.IsVisible)
                || subscriptionPlanRefs.Contains(plan.Id.Trim())
                || subscriptionPlanRefs.Contains(plan.Code.Trim()))
            .OrderBy(plan => plan.DisplayOrder)
            .ThenBy(plan => plan.Code)
            .ToList();

        var activeAiQuotaPlanCodes = await db.AiQuotaPlans.AsNoTracking()
            .Where(plan => plan.IsActive)
            .Select(plan => plan.Code)
            .ToListAsync(ct);
        var activeAiQuotaPlans = new HashSet<string>(
            activeAiQuotaPlanCodes.Select(code => code.Trim().ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);

        var knownPlanKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in allPlans)
        {
            knownPlanKeys.Add(plan.Id.Trim());
            knownPlanKeys.Add(plan.Code.Trim());
        }

        var invalidAiExamples = new List<AdminBillingEntitlementDiagnosticExampleResponse>();
        var fallbackExamples = new List<AdminBillingEntitlementDiagnosticExampleResponse>();
        var legacyContentExamples = new List<AdminBillingEntitlementDiagnosticExampleResponse>();
        var missingPlanExamples = new List<AdminBillingEntitlementDiagnosticExampleResponse>();
        var invalidAiCount = 0;
        var fallbackCount = 0;
        var legacyContentCount = 0;
        var missingPlanCount = 0;

        foreach (var plan in plans)
        {
            var mapping = AiQuotaPlanMappingResolver.Resolve(plan);
            var directAiPlanActive = activeAiQuotaPlans.Contains(AiQuotaPlanMappingResolver.NormalizeCode(plan.Code) ?? string.Empty);
            if (mapping?.Source == "explicit-invalid")
            {
                invalidAiCount++;
                AddExample(invalidAiExamples, PlanExample(
                    plan,
                    "Has an invalid ai.quotaPlanCode value and will fall back to the free AI quota plan.",
                    new Dictionary<string, string> { ["mappingSource"] = mapping.Source }));
            }
            else if (mapping?.Source == "explicit" && !activeAiQuotaPlans.Contains(mapping.Code ?? string.Empty))
            {
                invalidAiCount++;
                AddExample(invalidAiExamples, PlanExample(
                    plan,
                    $"Maps to AI quota plan '{mapping.Code}', but that quota plan is missing or inactive.",
                    new Dictionary<string, string>
                    {
                        ["aiQuotaPlanCode"] = mapping.Code ?? string.Empty,
                        ["mappingSource"] = mapping.Source
                    }));
            }
            else if (directAiPlanActive)
            {
                // Runtime AI quota resolution uses a direct active billing-code match before seeded fallback mapping.
            }
            else if (mapping?.Source == "fallback" && activeAiQuotaPlans.Contains(mapping.Code ?? string.Empty))
            {
                fallbackCount++;
                AddExample(fallbackExamples, PlanExample(
                    plan,
                    $"Uses seeded fallback AI quota mapping to '{mapping.Code}'. Add ai.quotaPlanCode to make the catalog explicit.",
                    new Dictionary<string, string>
                    {
                        ["aiQuotaPlanCode"] = mapping.Code ?? string.Empty,
                        ["mappingSource"] = mapping.Source
                    }));
            }
            else if (mapping?.Source == "fallback")
            {
                invalidAiCount++;
                AddExample(invalidAiExamples, PlanExample(
                    plan,
                    $"Seeded fallback AI quota plan '{mapping.Code}' is missing or inactive.",
                    new Dictionary<string, string>
                    {
                        ["aiQuotaPlanCode"] = mapping.Code ?? string.Empty,
                        ["mappingSource"] = mapping.Source
                    }));
            }
            else if (mapping is null
                && plan.Price > 0m
                && !directAiPlanActive)
            {
                invalidAiCount++;
                AddExample(invalidAiExamples, PlanExample(
                    plan,
                    "Paid plan has no explicit AI quota mapping, seeded fallback, or direct active AI quota plan code.",
                    new Dictionary<string, string> { ["mappingSource"] = "missing" }));
            }

            var contentShape = InspectBillingContentShape(plan.EntitlementsJson);
            if (contentShape.IsLegacy)
            {
                legacyContentCount++;
                AddExample(legacyContentExamples, PlanExample(
                    plan,
                    contentShape.Reason,
                    new Dictionary<string, string> { ["contentShape"] = contentShape.Reason }));
            }
        }

        foreach (var subscription in activeSubscriptions)
        {
            if (!knownPlanKeys.Contains(subscription.PlanId.Trim()))
            {
                missingPlanCount++;
                AddExample(missingPlanExamples, new AdminBillingEntitlementDiagnosticExampleResponse(
                    "subscription",
                    subscription.Id,
                    subscription.PlanId,
                    subscription.UserId,
                    $"Active/trial subscription points to missing billing plan '{subscription.PlanId}'.",
                    new Dictionary<string, string>
                    {
                        ["userId"] = subscription.UserId,
                        ["status"] = subscription.Status.ToString().ToLowerInvariant(),
                        ["planId"] = subscription.PlanId
                    }));
            }
        }

        var checks = new[]
        {
            new AdminBillingEntitlementDiagnosticCheckResponse(
                "invalid_ai_quota_mapping",
                "Invalid AI quota mappings",
                "danger",
                invalidAiCount,
                invalidAiExamples),
            new AdminBillingEntitlementDiagnosticCheckResponse(
                "missing_plan_subscriptions",
                "Missing plan subscriptions",
                "danger",
                missingPlanCount,
                missingPlanExamples),
            new AdminBillingEntitlementDiagnosticCheckResponse(
                "fallback_ai_quota_mapping",
                "Fallback AI quota mappings",
                "warning",
                fallbackCount,
                fallbackExamples),
            new AdminBillingEntitlementDiagnosticCheckResponse(
                "legacy_content_shape",
                "Legacy content shape",
                "warning",
                legacyContentCount,
                legacyContentExamples),
        };

        return new AdminBillingEntitlementDiagnosticsResponse(
            generatedAt,
            new AdminBillingEntitlementDiagnosticsSummaryResponse(
                invalidAiCount,
                missingPlanCount,
                fallbackCount,
                legacyContentCount,
                invalidAiCount + missingPlanCount + fallbackCount + legacyContentCount),
            checks);
    }

    private static void AddExample(
        List<AdminBillingEntitlementDiagnosticExampleResponse> examples,
        AdminBillingEntitlementDiagnosticExampleResponse example)
    {
        if (examples.Count < BillingDiagnosticsExampleLimit)
        {
            examples.Add(example);
        }
    }

    private static AdminBillingEntitlementDiagnosticExampleResponse PlanExample(
        BillingPlan plan,
        string message,
        Dictionary<string, string> metadata)
    {
        metadata["status"] = plan.Status.ToString().ToLowerInvariant();
        metadata["isVisible"] = plan.IsVisible ? "true" : "false";

        return new AdminBillingEntitlementDiagnosticExampleResponse(
            "plan",
            plan.Id,
            plan.Code,
            plan.Name,
            message,
            metadata);
    }

    private static BillingEntitlementContentShape InspectBillingContentShape(string? entitlementsJson)
    {
        if (string.IsNullOrWhiteSpace(entitlementsJson))
        {
            return new BillingEntitlementContentShape(true, "Missing content entitlement node for package/media access.");
        }

        try
        {
            using var document = JsonDocument.Parse(entitlementsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new BillingEntitlementContentShape(true, "Entitlements JSON is not an object, so content access remains legacy-shaped.");
            }

            if (!document.RootElement.TryGetProperty("content", out var content))
            {
                return new BillingEntitlementContentShape(true, "Missing content entitlement node for package/media access.");
            }

            return content.ValueKind == JsonValueKind.Object
                ? new BillingEntitlementContentShape(false, string.Empty)
                : new BillingEntitlementContentShape(true, "Content entitlement node is present but not an object.");
        }
        catch (JsonException)
        {
            return new BillingEntitlementContentShape(true, "Entitlements JSON is invalid, so content access remains legacy-shaped.");
        }
    }

    public async Task<object> GetBillingCouponRedemptionsAsync(string? couponCode, string? userId, int page, int pageSize, CancellationToken ct)
    {
        var query = db.BillingCouponRedemptions.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(couponCode) && couponCode != "all")
        {
            query = query.Where(redemption => redemption.CouponCode == couponCode);
        }

        if (!string.IsNullOrWhiteSpace(userId))
        {
            query = query.Where(redemption => redemption.UserId == userId);
        }

        var total = await query.CountAsync(ct);
        var redemptions = await ToOrderedListDescendingAsync(
            query,
            redemption => redemption.RedeemedAt,
            ct,
            skip: (page - 1) * pageSize,
            take: pageSize);

        var items = redemptions.Select(redemption => new
        {
            redemption.Id,
            redemption.CouponCode,
            redemption.UserId,
            redemption.QuoteId,
            redemption.CheckoutSessionId,
            redemption.SubscriptionId,
            redemption.DiscountAmount,
            redemption.Currency,
            status = redemption.Status.ToString().ToLowerInvariant(),
            redemption.RedeemedAt
        }).ToList();

        return new { total, page, pageSize, items };
    }

    public async Task<object> GetBillingInvoicesAsync(string? status, string? search,
        int page, int pageSize, CancellationToken ct)
    {
        var query = db.Invoices.AsNoTracking().AsQueryable();

        // Case-insensitive: every Invoice row is written with Status="Paid" (capital,
        // Domain/Entities.cs default), while every caller — this admin filter and the
        // learner-facing equivalent — sends lowercase ids ("paid"/"pending"/"failed").
        // An exact `==` here made the "Paid" filter return zero rows for every invoice
        // that has ever existed.
        if (!string.IsNullOrWhiteSpace(status) && status != "all")
        {
            var normalizedStatus = status.Trim();
            query = query.Where(i => i.Status.ToLower() == normalizedStatus.ToLower());
        }
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(i => i.UserId.Contains(search) || i.Description.Contains(search));

        var total = await query.CountAsync(ct);
        var invoices = await ToOrderedListDescendingAsync(
            query,
            i => i.IssuedAt,
            ct,
            skip: (page - 1) * pageSize,
            take: pageSize);

        var userIds = invoices.Select(i => i.UserId).Distinct().ToList();
        var learnerNames = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var items = invoices.Select(i => new
        {
            i.Id,
            userId = i.UserId,
            userName = learnerNames.TryGetValue(i.UserId, out var name) ? name : i.UserId,
            amount = i.Amount,
            currency = i.Currency,
            status = i.Status,
            date = i.IssuedAt,
            plan = i.Description,
            source = i.Source
        }).ToList();

        return new { total, page, pageSize, items };
    }

    public async Task<AdminBillingPaymentTransactionListResponse> GetBillingPaymentTransactionsAsync(
        string? status,
        string? gateway,
        string? transactionType,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.PaymentTransactions.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
        {
            var normalizedStatus = status.Trim().ToLowerInvariant();
            query = query.Where(payment => payment.Status == normalizedStatus);
        }

        if (!string.IsNullOrWhiteSpace(gateway) && !string.Equals(gateway, "all", StringComparison.OrdinalIgnoreCase))
        {
            var normalizedGateway = gateway.Trim().ToLowerInvariant();
            query = query.Where(payment => payment.Gateway == normalizedGateway);
        }

        if (!string.IsNullOrWhiteSpace(transactionType) && !string.Equals(transactionType, "all", StringComparison.OrdinalIgnoreCase))
        {
            var normalizedTransactionType = transactionType.Trim().ToLowerInvariant();
            query = query.Where(payment => payment.TransactionType == normalizedTransactionType);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim();
            query = query.Where(payment =>
                payment.LearnerUserId.Contains(normalizedSearch)
                || payment.GatewayTransactionId.Contains(normalizedSearch)
                || (payment.QuoteId != null && payment.QuoteId.Contains(normalizedSearch))
                || (payment.ProductId != null && payment.ProductId.Contains(normalizedSearch))
                || db.Users.AsNoTracking().Any(user => user.Id == payment.LearnerUserId && user.DisplayName.Contains(normalizedSearch)));
        }

        var total = await query.CountAsync(ct);
        var payments = await GetOrderedPaymentTransactionsPageAsync(query, normalizedPage, normalizedPageSize, ct);

        var userIds = payments.Select(payment => payment.LearnerUserId).Distinct().ToList();
        var userNames = await db.Users.AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.DisplayName, ct);

        var items = payments.Select(payment => MapBillingPaymentTransaction(
            payment,
            userNames.TryGetValue(payment.LearnerUserId, out var userName) ? userName : payment.LearnerUserId)).ToList();

        return new AdminBillingPaymentTransactionListResponse(total, normalizedPage, normalizedPageSize, items);
    }

    private async Task<List<PaymentTransaction>> GetOrderedPaymentTransactionsPageAsync(
        IQueryable<PaymentTransaction> query,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var skip = (page - 1) * pageSize;
        if (!db.Database.IsSqlite())
        {
            return await query
                .OrderByDescending(payment => payment.CreatedAt)
                .ThenByDescending(payment => payment.Id)
                .Skip(skip)
                .Take(pageSize)
                .ToListAsync(ct);
        }

        return (await query.ToListAsync(ct))
            .OrderByDescending(payment => payment.CreatedAt)
            .ThenByDescending(payment => payment.Id)
            .Skip(skip)
            .Take(pageSize)
            .ToList();
    }

    /// <summary>
    /// Renders any learner's invoice as a branded PDF for admin download. Unlike the
    /// learner-facing <see cref="LearnerService.GetInvoiceDownloadAsync"/>, this is not
    /// scoped to the caller — it resolves the invoice's owning learner for the bill-to block.
    /// </summary>
    public async Task<GeneratedDownloadFile> GetBillingInvoiceDownloadAsync(string invoiceId, CancellationToken ct)
    {
        var normalizedInvoiceId = invoiceId.Trim();
        var invoice = await db.Invoices.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == normalizedInvoiceId, ct)
            ?? throw ApiException.NotFound("billing_invoice_not_found", "Billing invoice not found.");

        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == invoice.UserId)
            .Select(u => new { u.DisplayName, u.Email })
            .FirstOrDefaultAsync(ct);

        var pdf = new InvoicePdfService().Generate(new InvoicePdfModel(
            InvoiceId: invoice.Id,
            Number: invoice.Number,
            IssuedAt: invoice.IssuedAt,
            Amount: invoice.Amount,
            Currency: invoice.Currency,
            Status: invoice.Status,
            Description: invoice.Description,
            BillToName: user?.DisplayName ?? invoice.UserId,
            BillToEmail: user?.Email ?? string.Empty));

        return new GeneratedDownloadFile(new MemoryStream(pdf.Bytes), "application/pdf", pdf.Filename);
    }

    public async Task<AdminBillingInvoiceEvidenceResponse> GetBillingInvoiceEvidenceAsync(string invoiceId, CancellationToken ct)
    {
        var normalizedInvoiceId = invoiceId.Trim();
        var invoice = await db.Invoices.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == normalizedInvoiceId, ct)
            ?? throw ApiException.NotFound("billing_invoice_not_found", "Billing invoice not found.");

        var userName = await db.Users.AsNoTracking()
            .Where(user => user.Id == invoice.UserId)
            .Select(user => user.DisplayName)
            .FirstOrDefaultAsync(ct) ?? invoice.UserId;

        BillingQuote? quote = null;
        if (!string.IsNullOrWhiteSpace(invoice.QuoteId))
        {
            quote = await db.BillingQuotes.AsNoTracking()
                .FirstOrDefaultAsync(q => q.Id == invoice.QuoteId, ct);
        }

        if (quote is null && !string.IsNullOrWhiteSpace(invoice.CheckoutSessionId))
        {
            quote = await db.BillingQuotes.AsNoTracking()
                .FirstOrDefaultAsync(q => q.CheckoutSessionId == invoice.CheckoutSessionId, ct);
        }

        var quoteId = FirstNonEmpty(quote?.Id, invoice.QuoteId);
        var checkoutSessionId = FirstNonEmpty(invoice.CheckoutSessionId, quote?.CheckoutSessionId);
        var legacyTopUpGatewayPrefix = TryGetGatewayTransactionPrefixFromInvoiceId(invoice.Id);

        var payments = await db.PaymentTransactions.AsNoTracking()
            .Where(payment =>
                (!string.IsNullOrWhiteSpace(quoteId) && payment.QuoteId == quoteId)
                || (!string.IsNullOrWhiteSpace(checkoutSessionId) && payment.GatewayTransactionId == checkoutSessionId)
                || (!string.IsNullOrWhiteSpace(legacyTopUpGatewayPrefix)
                    && payment.LearnerUserId == invoice.UserId
                    && payment.TransactionType == "wallet_top_up"
                    && payment.Amount == invoice.Amount
                    && payment.Currency == invoice.Currency
                    && payment.GatewayTransactionId.StartsWith(legacyTopUpGatewayPrefix)))
            .OrderByDescending(payment => payment.CreatedAt)
            .ToListAsync(ct);

        // "manual_proof" invoices carry no quote/payment — their evidence is the approved
        // payment-proof row instead. Also checked defensively whenever no quote resolved and
        // the invoice has a subscription, so a mislabeled Source still surfaces real evidence.
        ManualPaymentRequest? proof = null;
        if (!string.IsNullOrWhiteSpace(invoice.SubscriptionId)
            && (invoice.Source == InvoiceSources.ManualProof || quote is null))
        {
            proof = await db.ManualPaymentRequests.AsNoTracking()
                .Where(request => request.AccessGrantedSubscriptionId == invoice.SubscriptionId
                    && (request.Status == "paid" || request.Status == "approved"))
                .OrderByDescending(request => request.SubmittedAt)
                .FirstOrDefaultAsync(ct);
        }

        var redemptions = await db.BillingCouponRedemptions.AsNoTracking()
            .Where(redemption =>
                (!string.IsNullOrWhiteSpace(quoteId) && redemption.QuoteId == quoteId)
                || (!string.IsNullOrWhiteSpace(checkoutSessionId) && redemption.CheckoutSessionId == checkoutSessionId))
            .OrderByDescending(redemption => redemption.RedeemedAt)
            .ToListAsync(ct);

        var subscriptionItems = await db.SubscriptionItems.AsNoTracking()
            .Where(item =>
                (!string.IsNullOrWhiteSpace(quoteId) && item.QuoteId == quoteId)
                || (!string.IsNullOrWhiteSpace(checkoutSessionId) && item.CheckoutSessionId == checkoutSessionId))
            .OrderByDescending(item => item.StartsAt)
            .ToListAsync(ct);

        var eventEntityIds = new[]
            {
                invoice.Id,
                checkoutSessionId
            }
            .Concat(payments.Select(payment => payment.GatewayTransactionId))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var eventSubscriptionIds = redemptions.Select(redemption => redemption.SubscriptionId)
            .Concat(subscriptionItems.Select(item => item.SubscriptionId))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var events = await db.BillingEvents.AsNoTracking()
            .Where(e =>
                (e.UserId == null || e.UserId == invoice.UserId)
                && ((!string.IsNullOrWhiteSpace(quoteId) && e.QuoteId == quoteId)
                    || eventEntityIds.Contains(e.EntityId)
                    || (e.SubscriptionId != null && eventSubscriptionIds.Contains(e.SubscriptionId))))
            .OrderByDescending(e => e.OccurredAt)
            .Take(25)
            .ToListAsync(ct);

        var catalogAnchors = BuildCatalogAnchorEvidence(invoice, quote, payments.FirstOrDefault());
        var notRecorded = BuildInvoiceEvidenceNotRecorded(invoice, quote, payments, proof, redemptions, events, catalogAnchors);
        var integrityFlags = BuildInvoiceEvidenceIntegrityFlags(invoice, quote, payments, redemptions, legacyTopUpGatewayPrefix);

        return new AdminBillingInvoiceEvidenceResponse(
            new AdminBillingInvoiceEvidenceInvoiceResponse(
                invoice.Id,
                invoice.UserId,
                userName,
                invoice.Amount,
                invoice.Currency,
                invoice.Status,
                invoice.Description,
                invoice.IssuedAt,
                invoice.PlanVersionId,
                DeserializeStringDictionary(invoice.AddOnVersionIdsJson),
                invoice.CouponVersionId,
                invoice.QuoteId,
                invoice.CheckoutSessionId,
                invoice.SubscriptionId,
                invoice.Source),
            quote is null ? null : MapInvoiceEvidenceQuote(quote),
            payments.Select(MapInvoiceEvidencePayment).ToList(),
            proof is null ? null : MapInvoiceEvidenceProof(proof),
            redemptions.Select(MapInvoiceEvidenceRedemption).ToList(),
            subscriptionItems.Select(MapInvoiceEvidenceSubscriptionItem).ToList(),
            events.Select(MapInvoiceEvidenceEvent).ToList(),
            catalogAnchors,
            notRecorded,
            integrityFlags);
    }

    private static AdminBillingInvoiceEvidenceQuoteResponse MapInvoiceEvidenceQuote(BillingQuote quote)
    {
        var snapshot = JsonSupport.Deserialize<Dictionary<string, object?>>(quote.SnapshotJson, new Dictionary<string, object?>());
        var items = JsonSupport.Deserialize<List<BillingQuoteLineItem>>(
            JsonSupport.Serialize(snapshot.GetValueOrDefault("items") ?? Array.Empty<object>()),
            []);
        var summary = snapshot.TryGetValue("summary", out var summaryValue) ? summaryValue?.ToString() ?? string.Empty : string.Empty;

        return new AdminBillingInvoiceEvidenceQuoteResponse(
            quote.Id,
            quote.Status.ToString().ToLowerInvariant(),
            quote.Currency,
            quote.SubtotalAmount,
            quote.DiscountAmount,
            quote.TotalAmount,
            quote.PlanCode,
            quote.CouponCode,
            JsonSupport.Deserialize<List<string>>(quote.AddOnCodesJson, []),
            items,
            quote.CreatedAt,
            quote.ExpiresAt,
            quote.CheckoutSessionId,
            quote.PlanVersionId,
            DeserializeStringDictionary(quote.AddOnVersionIdsJson),
            quote.CouponVersionId,
            summary);
    }

    private static AdminBillingInvoiceEvidencePaymentResponse MapInvoiceEvidencePayment(PaymentTransaction payment)
        => new(
            payment.Id.ToString("D"),
            payment.Gateway,
            payment.GatewayTransactionId,
            payment.TransactionType,
            payment.Status,
            payment.Amount,
            payment.Currency,
            payment.ProductType ?? string.Empty,
            payment.ProductId ?? string.Empty,
            payment.QuoteId,
            payment.PlanVersionId,
            DeserializeStringDictionary(payment.AddOnVersionIdsJson),
            payment.CouponVersionId,
            payment.CreatedAt,
            payment.UpdatedAt);

    private static AdminBillingPaymentTransactionResponse MapBillingPaymentTransaction(PaymentTransaction payment, string learnerName)
        => new(
            payment.Id.ToString("D"),
            payment.LearnerUserId,
            learnerName,
            payment.Gateway,
            payment.GatewayTransactionId,
            payment.TransactionType,
            payment.Status,
            payment.Amount,
            payment.Currency,
            payment.ProductType ?? string.Empty,
            payment.ProductId ?? string.Empty,
            payment.QuoteId,
            payment.PlanVersionId,
            DeserializeStringDictionary(payment.AddOnVersionIdsJson),
            payment.CouponVersionId,
            payment.CreatedAt,
            payment.UpdatedAt);

    public async Task<AdminBillingProviderLifecycleSignalsResponse> GetBillingProviderLifecycleSignalsAsync(
        string? gateway,
        string? category,
        string? processingStatus,
        string? verificationStatus,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.PaymentWebhookEvents.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(gateway) && !string.Equals(gateway, "all", StringComparison.OrdinalIgnoreCase))
        {
            var normalizedGateway = gateway.Trim().ToLowerInvariant();
            query = query.Where(signal => signal.Gateway == normalizedGateway);
        }

        if (!string.IsNullOrWhiteSpace(processingStatus) && !string.Equals(processingStatus, "all", StringComparison.OrdinalIgnoreCase))
        {
            var normalizedProcessingStatus = processingStatus.Trim().ToLowerInvariant();
            query = query.Where(signal => signal.ProcessingStatus == normalizedProcessingStatus);
        }

        if (!string.IsNullOrWhiteSpace(verificationStatus) && !string.Equals(verificationStatus, "all", StringComparison.OrdinalIgnoreCase))
        {
            var normalizedVerificationStatus = verificationStatus.Trim().ToLowerInvariant();
            query = query.Where(signal => signal.VerificationStatus == normalizedVerificationStatus);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim();
            if (Guid.TryParse(normalizedSearch, out var webhookEventId))
            {
                query = query.Where(signal =>
                    signal.Id == webhookEventId
                    || signal.EventType.Contains(normalizedSearch)
                    || signal.GatewayEventId.Contains(normalizedSearch)
                    || (signal.GatewayTransactionId != null && signal.GatewayTransactionId.Contains(normalizedSearch))
                    || (signal.NormalizedStatus != null && signal.NormalizedStatus.Contains(normalizedSearch)));
            }
            else
            {
                query = query.Where(signal =>
                    signal.EventType.Contains(normalizedSearch)
                    || signal.GatewayEventId.Contains(normalizedSearch)
                    || (signal.GatewayTransactionId != null && signal.GatewayTransactionId.Contains(normalizedSearch))
                    || (signal.NormalizedStatus != null && signal.NormalizedStatus.Contains(normalizedSearch)));
            }
        }

        query = ApplyProviderLifecycleCategoryFilter(query, category);

        var total = await query.CountAsync(ct);
        var summary = new AdminBillingProviderLifecycleSignalsSummaryResponse(
            total,
            await query.CountAsync(signal => signal.ProcessingStatus == "failed", ct),
            await query.CountAsync(signal => signal.VerificationStatus != "verified", ct),
            await query.CountAsync(signal => signal.GatewayTransactionId == null || signal.GatewayTransactionId == string.Empty
                || !db.PaymentTransactions.AsNoTracking().Any(payment =>
                    payment.Gateway == signal.Gateway
                    && payment.GatewayTransactionId == signal.GatewayTransactionId), ct),
            await ApplyProviderLifecycleCategoryFilter(query, "refund").CountAsync(ct),
            await ApplyProviderLifecycleCategoryFilter(query, "dispute").CountAsync(ct),
            await ApplyProviderLifecycleCategoryFilter(query, "cancellation").CountAsync(ct));

        var projectedSignals = await query
            .OrderByDescending(signal => signal.ReceivedAt)
            .ThenByDescending(signal => signal.GatewayEventId)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(signal => new ProviderLifecycleSignalProjection(
                signal.Id,
                signal.Gateway,
                signal.EventType,
                signal.GatewayEventId,
                signal.GatewayTransactionId,
                signal.ProcessingStatus,
                signal.VerificationStatus,
                signal.NormalizedStatus,
                signal.ReceivedAt,
                signal.ProcessedAt))
            .ToListAsync(ct);

        var correlations = await BuildProviderLifecycleCorrelationsAsync(projectedSignals, ct);
        var items = projectedSignals
            .Select(signal => MapProviderLifecycleSignal(signal, correlations.GetValueOrDefault(signal.Id) ?? ProviderLifecycleCorrelation.Empty(signal)))
            .ToList();

        return new AdminBillingProviderLifecycleSignalsResponse(total, normalizedPage, normalizedPageSize, summary, items);
    }

    private async Task<Dictionary<Guid, ProviderLifecycleCorrelation>> BuildProviderLifecycleCorrelationsAsync(
        IReadOnlyCollection<ProviderLifecycleSignalProjection> signals,
        CancellationToken ct)
    {
        var result = signals.ToDictionary(signal => signal.Id, ProviderLifecycleCorrelation.Empty);
        var gatewayTransactionIds = signals
            .Select(signal => signal.GatewayTransactionId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (gatewayTransactionIds.Count == 0)
        {
            return result;
        }

        var payments = await db.PaymentTransactions.AsNoTracking()
            .Where(payment => gatewayTransactionIds.Contains(payment.GatewayTransactionId))
            .Select(payment => new ProviderLifecyclePaymentProjection(
                payment.Id,
                payment.Gateway,
                payment.GatewayTransactionId,
                payment.LearnerUserId,
                payment.QuoteId))
            .ToListAsync(ct);

        var paymentUserIds = payments
            .Select(payment => payment.LearnerUserId)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var paymentQuoteIds = payments
            .Select(payment => payment.QuoteId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var paymentGatewayTransactionIds = payments
            .Select(payment => payment.GatewayTransactionId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var quotes = await db.BillingQuotes.AsNoTracking()
            .Where(quote => paymentQuoteIds.Contains(quote.Id) && paymentUserIds.Contains(quote.UserId))
            .Select(quote => new ProviderLifecycleQuoteProjection(
                quote.Id,
                quote.UserId,
                quote.CheckoutSessionId,
                quote.SubscriptionId))
            .ToListAsync(ct);

        var quoteIds = quotes.Select(quote => quote.Id)
            .Concat(paymentQuoteIds)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var checkoutSessionIds = quotes.Select(quote => quote.CheckoutSessionId)
            .Concat(paymentGatewayTransactionIds)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var invoices = await db.Invoices.AsNoTracking()
            .Where(invoice =>
                paymentUserIds.Contains(invoice.UserId)
                && ((invoice.QuoteId != null && quoteIds.Contains(invoice.QuoteId))
                || (invoice.CheckoutSessionId != null && checkoutSessionIds.Contains(invoice.CheckoutSessionId)))
            )
            .Select(invoice => new ProviderLifecycleInvoiceProjection(
                invoice.Id,
                invoice.UserId,
                invoice.QuoteId,
                invoice.CheckoutSessionId))
            .ToListAsync(ct);

        var subscriptionIds = quotes.Select(quote => quote.SubscriptionId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var subscriptions = await db.Subscriptions.AsNoTracking()
            .Where(subscription => subscriptionIds.Contains(subscription.Id) && paymentUserIds.Contains(subscription.UserId))
            .Select(subscription => new ProviderLifecycleSubscriptionProjection(subscription.Id, subscription.UserId))
            .ToListAsync(ct);

        var billingEventEntityIds = paymentGatewayTransactionIds
            .Concat(checkoutSessionIds)
            .Concat(quoteIds)
            .Concat(subscriptionIds)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var billingEvents = await db.BillingEvents.AsNoTracking()
            .Where(billingEvent =>
                (billingEvent.UserId == null || paymentUserIds.Contains(billingEvent.UserId))
                && ((billingEvent.EntityId != null && billingEventEntityIds.Contains(billingEvent.EntityId))
                || (billingEvent.QuoteId != null && quoteIds.Contains(billingEvent.QuoteId))
                || (billingEvent.SubscriptionId != null && subscriptionIds.Contains(billingEvent.SubscriptionId))))
            .Select(billingEvent => new ProviderLifecycleBillingEventProjection(
                billingEvent.Id,
                billingEvent.UserId,
                billingEvent.EntityId,
                billingEvent.QuoteId,
                billingEvent.SubscriptionId))
            .ToListAsync(ct);

        foreach (var signal in signals)
        {
            result[signal.Id] = BuildProviderLifecycleCorrelation(signal, payments, quotes, invoices, subscriptions, billingEvents);
        }

        return result;
    }

    private static ProviderLifecycleCorrelation BuildProviderLifecycleCorrelation(
        ProviderLifecycleSignalProjection signal,
        IReadOnlyCollection<ProviderLifecyclePaymentProjection> payments,
        IReadOnlyCollection<ProviderLifecycleQuoteProjection> quotes,
        IReadOnlyCollection<ProviderLifecycleInvoiceProjection> invoices,
        IReadOnlyCollection<ProviderLifecycleSubscriptionProjection> subscriptions,
        IReadOnlyCollection<ProviderLifecycleBillingEventProjection> billingEvents)
    {
        if (string.IsNullOrWhiteSpace(signal.GatewayTransactionId))
        {
            return ProviderLifecycleCorrelation.Empty(signal);
        }

        var gatewayTransactionId = signal.GatewayTransactionId;
        var rowPayments = payments
            .Where(payment =>
                string.Equals(payment.GatewayTransactionId, gatewayTransactionId, StringComparison.Ordinal)
                && string.Equals(payment.Gateway, signal.Gateway, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (rowPayments.Count == 0)
        {
            var emptyLinkedIds = new AdminBillingProviderLifecycleLocalIdsResponse([], [], [], [], []);
            var unmatchedFlags = BuildProviderLifecycleIntegrityFlags(signal, emptyLinkedIds);
            unmatchedFlags.Add("local_evidence_unmatched");
            return new ProviderLifecycleCorrelation("unmatched", emptyLinkedIds, 0, unmatchedFlags.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        }

        var rowPaymentUserIds = rowPayments
            .Select(payment => payment.LearnerUserId)
            .ToHashSet(StringComparer.Ordinal);
        var rowQuoteIds = rowPayments
            .Select(payment => payment.QuoteId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToHashSet(StringComparer.Ordinal);
        var rowQuotes = quotes
            .Where(quote => rowQuoteIds.Contains(quote.Id) && rowPaymentUserIds.Contains(quote.UserId))
            .ToList();
        foreach (var quoteId in rowQuotes.Select(quote => quote.Id))
        {
            rowQuoteIds.Add(quoteId);
        }

        var rowCheckoutSessionIds = rowQuotes
            .Select(quote => quote.CheckoutSessionId)
            .Append(gatewayTransactionId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToHashSet(StringComparer.Ordinal);

        var rowInvoices = invoices
            .Where(invoice =>
                rowPaymentUserIds.Contains(invoice.UserId)
                && (rowQuoteIds.Count > 0
                    ? !string.IsNullOrWhiteSpace(invoice.QuoteId) && rowQuoteIds.Contains(invoice.QuoteId)
                    : !string.IsNullOrWhiteSpace(invoice.CheckoutSessionId) && rowCheckoutSessionIds.Contains(invoice.CheckoutSessionId)))
            .ToList();

        var rowSubscriptionIds = rowQuotes
            .Select(quote => quote.SubscriptionId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToHashSet(StringComparer.Ordinal);
        var rowSubscriptions = subscriptions
            .Where(subscription => rowSubscriptionIds.Contains(subscription.Id) && rowPaymentUserIds.Contains(subscription.UserId))
            .ToList();
        foreach (var subscriptionId in rowSubscriptions.Select(subscription => subscription.Id))
        {
            rowSubscriptionIds.Add(subscriptionId);
        }

        var rowEventEntityIds = rowCheckoutSessionIds
            .Concat(rowQuoteIds)
            .Concat(rowSubscriptionIds)
            .ToHashSet(StringComparer.Ordinal);
        var rowBillingEvents = billingEvents
            .Where(billingEvent =>
                (billingEvent.UserId == null || rowPaymentUserIds.Contains(billingEvent.UserId))
                && ((!string.IsNullOrWhiteSpace(billingEvent.EntityId) && rowEventEntityIds.Contains(billingEvent.EntityId))
                || (!string.IsNullOrWhiteSpace(billingEvent.QuoteId) && rowQuoteIds.Contains(billingEvent.QuoteId))
                || (!string.IsNullOrWhiteSpace(billingEvent.SubscriptionId) && rowSubscriptionIds.Contains(billingEvent.SubscriptionId))))
            .ToList();

        var linkedIds = new AdminBillingProviderLifecycleLocalIdsResponse(
            rowPayments.Select(payment => payment.Id.ToString("D")).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            rowInvoices.Select(invoice => invoice.Id).Distinct(StringComparer.Ordinal).ToList(),
            rowQuoteIds.Distinct(StringComparer.Ordinal).ToList(),
            rowSubscriptions.Select(subscription => subscription.Id).Distinct(StringComparer.Ordinal).ToList(),
            rowBillingEvents.Select(billingEvent => billingEvent.Id).Distinct(StringComparer.Ordinal).ToList());
        var hasAnyLocalMatch = linkedIds.PaymentTransactionIds.Count > 0
            || linkedIds.InvoiceIds.Count > 0
            || linkedIds.QuoteIds.Count > 0
            || linkedIds.SubscriptionIds.Count > 0
            || linkedIds.BillingEventIds.Count > 0;
        var integrityFlags = BuildProviderLifecycleIntegrityFlags(signal, linkedIds);
        var ambiguous = linkedIds.PaymentTransactionIds.Count > 1
            || linkedIds.InvoiceIds.Count > 1
            || linkedIds.QuoteIds.Count > 1
            || linkedIds.SubscriptionIds.Count > 1;
        var correlationStatus = !hasAnyLocalMatch ? "unmatched" : ambiguous ? "ambiguous" : "linked";

        if (correlationStatus == "unmatched")
        {
            integrityFlags.Add("local_evidence_unmatched");
        }

        return new ProviderLifecycleCorrelation(correlationStatus, linkedIds, rowBillingEvents.Count, integrityFlags.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static IQueryable<PaymentWebhookEvent> ApplyProviderLifecycleCategoryFilter(
        IQueryable<PaymentWebhookEvent> query,
        string? category)
    {
        if (string.IsNullOrWhiteSpace(category) || string.Equals(category, "all", StringComparison.OrdinalIgnoreCase))
        {
            return query;
        }

        var normalizedCategory = category.Trim().ToLowerInvariant();
        return normalizedCategory switch
        {
            "refund" => query.Where(signal => signal.EventType.ToLower().Contains("refund") || signal.EventType.ToLower().Contains("refunded")),
            "dispute" => query.Where(signal => signal.EventType.ToLower().Contains("dispute") || signal.EventType.ToLower().Contains("chargeback")),
            "cancellation" => query.Where(signal => signal.EventType.ToLower().Contains("cancel") || signal.EventType.ToLower().Contains("cancelled") || signal.EventType.ToLower().Contains("canceled") || signal.EventType.ToLower().Contains("deleted") || signal.EventType.ToLower().Contains("ended")),
            "checkout" => query.Where(signal => signal.EventType.ToLower().Contains("checkout")
                && !signal.EventType.ToLower().Contains("refund")
                && !signal.EventType.ToLower().Contains("refunded")
                && !signal.EventType.ToLower().Contains("dispute")
                && !signal.EventType.ToLower().Contains("chargeback")
                && !signal.EventType.ToLower().Contains("cancel")
                && !signal.EventType.ToLower().Contains("cancelled")
                && !signal.EventType.ToLower().Contains("canceled")
                && !signal.EventType.ToLower().Contains("deleted")
                && !signal.EventType.ToLower().Contains("ended")),
            "invoice" => query.Where(signal => signal.EventType.ToLower().Contains("invoice")
                && !signal.EventType.ToLower().Contains("refund")
                && !signal.EventType.ToLower().Contains("refunded")
                && !signal.EventType.ToLower().Contains("dispute")
                && !signal.EventType.ToLower().Contains("chargeback")
                && !signal.EventType.ToLower().Contains("cancel")
                && !signal.EventType.ToLower().Contains("cancelled")
                && !signal.EventType.ToLower().Contains("canceled")
                && !signal.EventType.ToLower().Contains("deleted")
                && !signal.EventType.ToLower().Contains("ended")
                && !signal.EventType.ToLower().Contains("checkout")),
            "subscription" => query.Where(signal => signal.EventType.ToLower().Contains("subscription")
                && !signal.EventType.ToLower().Contains("refund")
                && !signal.EventType.ToLower().Contains("refunded")
                && !signal.EventType.ToLower().Contains("dispute")
                && !signal.EventType.ToLower().Contains("chargeback")
                && !signal.EventType.ToLower().Contains("cancel")
                && !signal.EventType.ToLower().Contains("cancelled")
                && !signal.EventType.ToLower().Contains("canceled")
                && !signal.EventType.ToLower().Contains("deleted")
                && !signal.EventType.ToLower().Contains("ended")
                && !signal.EventType.ToLower().Contains("checkout")
                && !signal.EventType.ToLower().Contains("invoice")),
            "payment" => query.Where(signal =>
                (signal.EventType.ToLower().Contains("payment") || signal.EventType.ToLower().Contains("charge") || signal.EventType.ToLower().Contains("capture") || signal.EventType.ToLower().Contains("paid"))
                && !signal.EventType.ToLower().Contains("refund")
                && !signal.EventType.ToLower().Contains("refunded")
                && !signal.EventType.ToLower().Contains("dispute")
                && !signal.EventType.ToLower().Contains("chargeback")
                && !signal.EventType.ToLower().Contains("cancel")
                && !signal.EventType.ToLower().Contains("cancelled")
                && !signal.EventType.ToLower().Contains("canceled")
                && !signal.EventType.ToLower().Contains("deleted")
                && !signal.EventType.ToLower().Contains("ended")
                && !signal.EventType.ToLower().Contains("checkout")
                && !signal.EventType.ToLower().Contains("invoice")
                && !signal.EventType.ToLower().Contains("subscription")),
            "unknown" => query.Where(signal =>
                !signal.EventType.ToLower().Contains("refund")
                && !signal.EventType.ToLower().Contains("refunded")
                && !signal.EventType.ToLower().Contains("dispute")
                && !signal.EventType.ToLower().Contains("chargeback")
                && !signal.EventType.ToLower().Contains("cancel")
                && !signal.EventType.ToLower().Contains("cancelled")
                && !signal.EventType.ToLower().Contains("canceled")
                && !signal.EventType.ToLower().Contains("deleted")
                && !signal.EventType.ToLower().Contains("ended")
                && !signal.EventType.ToLower().Contains("checkout")
                && !signal.EventType.ToLower().Contains("invoice")
                && !signal.EventType.ToLower().Contains("subscription")
                && !signal.EventType.ToLower().Contains("payment")
                && !signal.EventType.ToLower().Contains("charge")
                && !signal.EventType.ToLower().Contains("capture")
                && !signal.EventType.ToLower().Contains("paid")),
            _ => query
        };
    }

    private static List<string> BuildProviderLifecycleIntegrityFlags(
        ProviderLifecycleSignalProjection signal,
        AdminBillingProviderLifecycleLocalIdsResponse linkedIds)
    {
        var flags = new List<string>();
        if (IsFailedProviderLifecycleSignal(signal))
        {
            flags.Add("processing_failed");
        }

        if (IsUnverifiedProviderLifecycleSignal(signal))
        {
            flags.Add("provider_event_unverified");
        }

        if (string.IsNullOrWhiteSpace(signal.GatewayTransactionId))
        {
            flags.Add("gateway_transaction_not_recorded");
        }

        if (linkedIds.PaymentTransactionIds.Count > 1)
        {
            flags.Add("multiple_payment_transactions");
        }

        if (linkedIds.InvoiceIds.Count > 1)
        {
            flags.Add("multiple_invoices");
        }

        if (linkedIds.QuoteIds.Count > 1)
        {
            flags.Add("multiple_quotes");
        }

        if (linkedIds.SubscriptionIds.Count > 1)
        {
            flags.Add("multiple_subscriptions");
        }

        if (linkedIds.BillingEventIds.Count == 0 && linkedIds.PaymentTransactionIds.Count > 0)
        {
            flags.Add("billing_event_not_recorded");
        }

        return flags;
    }

    private static AdminBillingProviderLifecycleSignalResponse MapProviderLifecycleSignal(
        ProviderLifecycleSignalProjection signal,
        ProviderLifecycleCorrelation correlation)
    {
        var confidence = correlation.CorrelationStatus switch
        {
            "linked" => "high",
            "unmatched" => "medium",
            _ => "low"
        };

        return new AdminBillingProviderLifecycleSignalResponse(
            signal.Id.ToString("D"),
            ProviderLifecycleSource,
            ClassifyProviderLifecycleCategory(signal.EventType),
            correlation.CorrelationStatus,
            confidence,
            signal.Gateway,
            signal.EventType,
            MaskProviderId(signal.GatewayEventId) ?? "not_recorded",
            MaskProviderId(signal.GatewayTransactionId),
            signal.ProcessingStatus,
            signal.VerificationStatus,
            signal.NormalizedStatus,
            signal.ReceivedAt,
            signal.ProcessedAt,
            correlation.LinkedIds,
            correlation.BillingEventCount,
            correlation.IntegrityFlags);
    }

    private static string ClassifyProviderLifecycleCategory(string eventType)
    {
        var value = eventType.Trim().ToLowerInvariant();
        if (ContainsAny(value, "refund", "refunded")) return "refund";
        if (ContainsAny(value, "dispute", "chargeback")) return "dispute";
        if (ContainsAny(value, "cancel", "cancelled", "canceled", "deleted", "ended")) return "cancellation";
        if (value.Contains("checkout", StringComparison.Ordinal)) return "checkout";
        if (value.Contains("invoice", StringComparison.Ordinal)) return "invoice";
        if (value.Contains("subscription", StringComparison.Ordinal)) return "subscription";
        if (ContainsAny(value, "payment", "charge", "capture", "paid")) return "payment";
        return "unknown";
    }

    private static bool ContainsAny(string value, params string[] needles)
        => needles.Any(needle => value.Contains(needle, StringComparison.Ordinal));

    private static bool IsFailedProviderLifecycleSignal(ProviderLifecycleSignalProjection signal)
        => string.Equals(signal.ProcessingStatus, "failed", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnverifiedProviderLifecycleSignal(ProviderLifecycleSignalProjection signal)
        => !string.Equals(signal.VerificationStatus, "verified", StringComparison.OrdinalIgnoreCase);

    private static string? MaskProviderId(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return null;
        if (trimmed.Length <= 8) return new string('*', trimmed.Length);

        var prefixLength = trimmed.Length <= 12 ? 3 : 6;
        var suffixLength = trimmed.Length <= 12 ? 3 : 4;
        return $"{trimmed[..prefixLength]}...{trimmed[^suffixLength..]}";
    }

    private sealed record ProviderLifecycleSignalProjection(
        Guid Id,
        string Gateway,
        string EventType,
        string GatewayEventId,
        string? GatewayTransactionId,
        string ProcessingStatus,
        string VerificationStatus,
        string? NormalizedStatus,
        DateTimeOffset ReceivedAt,
        DateTimeOffset? ProcessedAt);

    private sealed record ProviderLifecyclePaymentProjection(
        Guid Id,
        string Gateway,
        string GatewayTransactionId,
        string LearnerUserId,
        string? QuoteId);

    private sealed record ProviderLifecycleQuoteProjection(
        string Id,
        string UserId,
        string? CheckoutSessionId,
        string? SubscriptionId);

    private sealed record ProviderLifecycleInvoiceProjection(
        string Id,
        string UserId,
        string? QuoteId,
        string? CheckoutSessionId);

    private sealed record ProviderLifecycleSubscriptionProjection(string Id, string UserId);

    private sealed record ProviderLifecycleBillingEventProjection(
        string Id,
        string? UserId,
        string? EntityId,
        string? QuoteId,
        string? SubscriptionId);

    private sealed record ProviderLifecycleCorrelation(
        string CorrelationStatus,
        AdminBillingProviderLifecycleLocalIdsResponse LinkedIds,
        int BillingEventCount,
        IReadOnlyList<string> IntegrityFlags)
    {
        public static ProviderLifecycleCorrelation Empty(ProviderLifecycleSignalProjection signal)
            => new(
                string.IsNullOrWhiteSpace(signal.GatewayTransactionId) ? "not_recorded" : "unmatched",
                new AdminBillingProviderLifecycleLocalIdsResponse([], [], [], [], []),
                0,
                BuildProviderLifecycleIntegrityFlags(signal, new AdminBillingProviderLifecycleLocalIdsResponse([], [], [], [], [])));
    }

    private static AdminBillingInvoiceEvidenceProofResponse MapInvoiceEvidenceProof(ManualPaymentRequest proof)
        => new(
            proof.Id,
            proof.Method,
            proof.Kind,
            proof.Gateway,
            proof.Reference,
            proof.Status,
            proof.SubmittedAt,
            proof.ReviewedAt);

    private static AdminBillingInvoiceEvidenceRedemptionResponse MapInvoiceEvidenceRedemption(BillingCouponRedemption redemption)
        => new(
            redemption.Id,
            redemption.CouponCode,
            redemption.CouponId,
            redemption.CouponVersionId,
            redemption.UserId,
            redemption.QuoteId,
            redemption.CheckoutSessionId,
            redemption.SubscriptionId,
            redemption.DiscountAmount,
            redemption.Currency,
            redemption.Status.ToString().ToLowerInvariant(),
            redemption.RedeemedAt);

    private static AdminBillingInvoiceEvidenceSubscriptionItemResponse MapInvoiceEvidenceSubscriptionItem(SubscriptionItem item)
        => new(
            item.Id,
            item.SubscriptionId,
            item.ItemType,
            item.ItemCode,
            item.AddOnVersionId,
            item.Quantity,
            item.Status.ToString().ToLowerInvariant(),
            item.QuoteId,
            item.CheckoutSessionId,
            item.StartsAt,
            item.EndsAt);

    private static AdminBillingInvoiceEvidenceEventResponse MapInvoiceEvidenceEvent(BillingEvent billingEvent)
        => new(
            billingEvent.Id,
            billingEvent.EventType,
            billingEvent.EntityType,
            billingEvent.EntityId ?? string.Empty,
            billingEvent.SubscriptionId,
            billingEvent.QuoteId,
            billingEvent.OccurredAt);

    private static AdminBillingInvoiceEvidenceCatalogAnchorResponse BuildCatalogAnchorEvidence(
        Invoice invoice,
        BillingQuote? quote,
        PaymentTransaction? payment)
    {
        var invoiceAddOnVersionIds = DeserializeStringDictionary(invoice.AddOnVersionIdsJson);
        if (!string.IsNullOrWhiteSpace(invoice.PlanVersionId) || invoiceAddOnVersionIds.Count > 0 || !string.IsNullOrWhiteSpace(invoice.CouponVersionId))
        {
            return new AdminBillingInvoiceEvidenceCatalogAnchorResponse(invoice.PlanVersionId, invoiceAddOnVersionIds, invoice.CouponVersionId, "invoice");
        }

        if (quote is not null)
        {
            var quoteAddOnVersionIds = DeserializeStringDictionary(quote.AddOnVersionIdsJson);
            if (!string.IsNullOrWhiteSpace(quote.PlanVersionId) || quoteAddOnVersionIds.Count > 0 || !string.IsNullOrWhiteSpace(quote.CouponVersionId))
            {
                return new AdminBillingInvoiceEvidenceCatalogAnchorResponse(quote.PlanVersionId, quoteAddOnVersionIds, quote.CouponVersionId, "quote");
            }
        }

        if (payment is not null)
        {
            var paymentAddOnVersionIds = DeserializeStringDictionary(payment.AddOnVersionIdsJson);
            if (!string.IsNullOrWhiteSpace(payment.PlanVersionId) || paymentAddOnVersionIds.Count > 0 || !string.IsNullOrWhiteSpace(payment.CouponVersionId))
            {
                return new AdminBillingInvoiceEvidenceCatalogAnchorResponse(payment.PlanVersionId, paymentAddOnVersionIds, payment.CouponVersionId, "payment");
            }
        }

        return new AdminBillingInvoiceEvidenceCatalogAnchorResponse(null, new Dictionary<string, string>(), null, "not_recorded");
    }

    private static IReadOnlyList<string> BuildInvoiceEvidenceNotRecorded(
        Invoice invoice,
        BillingQuote? quote,
        IReadOnlyCollection<PaymentTransaction> payments,
        ManualPaymentRequest? proof,
        IReadOnlyCollection<BillingCouponRedemption> redemptions,
        IReadOnlyCollection<BillingEvent> events,
        AdminBillingInvoiceEvidenceCatalogAnchorResponse catalogAnchors)
    {
        var notRecorded = new List<string>();

        // Gateway invoices are expected to carry a quote + completed payment — their absence
        // is a genuine gap. Manual-proof invoices are expected to carry an approved proof row
        // instead. Admin-grant invoices have none of these by design, not by omission.
        if (invoice.Source == InvoiceSources.Gateway)
        {
            if (quote is null)
            {
                notRecorded.Add("quote");
            }

            if (payments.Count == 0)
            {
                notRecorded.Add("payment");
            }
        }
        else if (invoice.Source == InvoiceSources.ManualProof && proof is null)
        {
            notRecorded.Add("proof");
        }

        if (redemptions.Count == 0 && (!string.IsNullOrWhiteSpace(invoice.CouponVersionId) || !string.IsNullOrWhiteSpace(quote?.CouponCode)))
        {
            notRecorded.Add("couponRedemption");
        }

        if (events.Count == 0)
        {
            notRecorded.Add("events");
        }

        if (catalogAnchors.Source == "not_recorded")
        {
            notRecorded.Add("catalogAnchors");
        }

        return notRecorded;
    }

    private static IReadOnlyList<string> BuildInvoiceEvidenceIntegrityFlags(
        Invoice invoice,
        BillingQuote? quote,
        IReadOnlyCollection<PaymentTransaction> payments,
        IReadOnlyCollection<BillingCouponRedemption> redemptions,
        string? legacyTopUpGatewayPrefix)
    {
        var flags = new List<string>();

        // Trustworthiness safety-net: a "Paid" invoice sourced from the gateway path with no
        // completed PaymentTransaction evidence at all is exactly the "Paid with no evidence"
        // bug this evidence view exists to catch — flag it even after reconciliation, in case
        // a future regression re-introduces an unevidenced gateway invoice.
        if (string.Equals(invoice.Status, "paid", StringComparison.OrdinalIgnoreCase)
            && invoice.Source == InvoiceSources.Gateway
            && payments.Count == 0)
        {
            flags.Add("paid_status_without_gateway_evidence");
        }

        if (quote is not null)
        {
            if (quote.UserId != invoice.UserId)
            {
                flags.Add("invoice_quote_user_mismatch");
            }

            if (invoice.Currency != quote.Currency)
            {
                flags.Add("invoice_quote_currency_mismatch");
            }

            if (invoice.Amount != quote.TotalAmount)
            {
                flags.Add("invoice_quote_total_mismatch");
            }
        }

        foreach (var payment in payments)
        {
            if (payment.LearnerUserId != invoice.UserId)
            {
                flags.Add("invoice_payment_user_mismatch");
            }

            if (payment.Currency != invoice.Currency)
            {
                flags.Add("invoice_payment_currency_mismatch");
            }

            if (payment.Amount != invoice.Amount)
            {
                flags.Add("invoice_payment_amount_mismatch");
            }
        }

        foreach (var redemption in redemptions)
        {
            if (redemption.UserId != invoice.UserId)
            {
                flags.Add("invoice_redemption_user_mismatch");
            }
        }

        if (!string.IsNullOrWhiteSpace(legacyTopUpGatewayPrefix)
            && payments.Count(payment => payment.GatewayTransactionId.StartsWith(legacyTopUpGatewayPrefix, StringComparison.OrdinalIgnoreCase)) > 1)
        {
            flags.Add("legacy_wallet_top_up_correlation_ambiguous");
        }

        return flags.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static Dictionary<string, string> DeserializeStringDictionary(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? new Dictionary<string, string>()
            : JsonSupport.Deserialize<Dictionary<string, string>>(json, new Dictionary<string, string>());

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? TryGetGatewayTransactionPrefixFromInvoiceId(string invoiceId)
        => invoiceId.StartsWith("inv-topup-", StringComparison.OrdinalIgnoreCase)
            ? invoiceId["inv-topup-".Length..]
            : null;
}
