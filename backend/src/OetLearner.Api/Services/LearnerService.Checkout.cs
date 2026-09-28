using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Assessment;
using OetLearner.Api.Services.Reading;

namespace OetLearner.Api.Services;

public partial class LearnerService
{

    public async Task<object> GetBillingQuoteAsync(string userId, BillingQuoteRequest request, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        return await BuildBillingQuoteAsync(userId, request, cancellationToken, persistQuote: true);
    }

    public async Task<BillingPaymentStatusResponse> GetBillingPaymentStatusAsync(
        string userId,
        string? quoteId,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);

        BillingQuote? quote = null;
        if (!string.IsNullOrWhiteSpace(quoteId))
        {
            quote = await db.BillingQuotes.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == quoteId && x.UserId == userId, cancellationToken);
        }

        if (quote is null && !string.IsNullOrWhiteSpace(sessionId))
        {
            quote = await db.BillingQuotes.AsNoTracking()
                .FirstOrDefaultAsync(x => x.CheckoutSessionId == sessionId && x.UserId == userId, cancellationToken);
        }

        PaymentTransaction? transaction = null;
        if (quote is not null)
        {
            transaction = await db.PaymentTransactions.AsNoTracking()
                .Where(x => x.LearnerUserId == userId && (x.QuoteId == quote.Id || x.GatewayTransactionId == quote.CheckoutSessionId))
                .OrderByDescending(x => x.UpdatedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(sessionId))
        {
            transaction = await db.PaymentTransactions.AsNoTracking()
                .FirstOrDefaultAsync(x => x.LearnerUserId == userId && x.GatewayTransactionId == sessionId, cancellationToken);
            if (transaction?.QuoteId is not null)
            {
                quote = await db.BillingQuotes.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == transaction.QuoteId && x.UserId == userId, cancellationToken);
            }
        }

        if (quote is null)
        {
            throw ApiException.NotFound("billing_payment_not_found", "Payment status was not found for this checkout.");
        }

        // Gateway safety net: the verified webhook callback is the primary fulfilment
        // path, but if it was missed, delayed or rejected the learner would stay
        // "pending" (or see the quote's 15-minute window read as "expired" — see
        // NormalizeBillingPaymentStatus) despite a successful charge at the gateway.
        // Whop is explicitly in scope here: the 15 Sep 2026 P0 was a real Whop charge
        // that settled while the local quote had already timed out. Verify directly
        // with the provider (server-to-server) and complete through the same
        // idempotent fulfilment a genuine webhook uses. Best-effort — failures fall
        // through to the normal read; the frontend keeps polling either way.
        if (transaction is not null
            && !string.Equals(transaction.Status, "completed", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(transaction.GatewayTransactionId))
        {
            var recoveredLive = string.Equals(transaction.Gateway, PaymentGatewayNames.Fawaterak, StringComparison.OrdinalIgnoreCase)
                ? await TryReconcilePendingFawaterakPaymentAsync(transaction, cancellationToken)
                : await TryReconcilePendingPaymentLiveAsync(transaction, cancellationToken);

            if (recoveredLive)
            {
                transaction = await db.PaymentTransactions.AsNoTracking()
                    .Where(x => x.LearnerUserId == userId && (x.QuoteId == quote.Id || x.GatewayTransactionId == quote.CheckoutSessionId))
                    .OrderByDescending(x => x.UpdatedAt)
                    .FirstOrDefaultAsync(cancellationToken);
            }
        }

        var quoteResponse = DeserializeQuoteResponse(quote);
        var invoice = await db.Invoices.AsNoTracking()
            .Where(x => x.UserId == userId && (x.QuoteId == quote.Id || x.CheckoutSessionId == quote.CheckoutSessionId))
            .OrderByDescending(x => x.IssuedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var subscriptionItem = await db.SubscriptionItems.AsNoTracking()
            .Where(x => x.QuoteId == quote.Id || x.CheckoutSessionId == quote.CheckoutSessionId)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var status = NormalizeBillingPaymentStatus(quote, transaction, DateTimeOffset.UtcNow);
        var metadata = JsonSupport.Deserialize<Dictionary<string, object?>>(transaction?.MetadataJson ?? "{}", new Dictionary<string, object?>());
        var productType = ReadString(metadata.GetValueOrDefault("productType"))
            ?? ReadString(metadata.GetValueOrDefault("product_type"))
            ?? (quote.PlanCode is not null ? "plan_purchase" : quoteResponse.Items.FirstOrDefault()?.Kind);

        var resolvedSubscriptionId = subscriptionItem?.SubscriptionId ?? quote.SubscriptionId;
        if (string.IsNullOrWhiteSpace(resolvedSubscriptionId) && !string.IsNullOrWhiteSpace(quote.SubscriptionId))
        {
            resolvedSubscriptionId = quote.SubscriptionId;
        }

        var purchasedAddOnCodes = JsonSupport.Deserialize<List<string>>(quote.AddOnCodesJson, []);
        var manualDeliveryRequired = purchasedAddOnCodes.Contains("tutor-book-addon", StringComparer.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(quote.PlanCode)
                && DeliveryMethods.RequiresManualFulfilment(
                    await ResolvePlanDeliveryMethodAsync(quote.PlanVersionId, quote.PlanCode, cancellationToken)));

        // Master Catalogue Flow A: a completed gateway payment for a regular
        // package (Products 1-29) still needs admin verification before the
        // package unlocks. Surface that state so the learner return page shows
        // "Pending Verification" + Send-on-WhatsApp instead of "access granted".
        var verificationRequired = false;
        if (!string.IsNullOrWhiteSpace(quote.PlanCode) && status == "completed" && resolvedSubscriptionId is not null)
        {
            var purchasedSubscription = await db.Subscriptions.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == resolvedSubscriptionId, cancellationToken);
            verificationRequired = string.Equals(
                purchasedSubscription?.FulfilmentStatus,
                FulfilmentStatuses.PendingVerification,
                StringComparison.OrdinalIgnoreCase)
                && purchasedSubscription?.Status != SubscriptionStatus.Active;
        }

        // Runtime-configurable support number (Admin > Settings > Support);
        // never hard-code the wa.me link here.
        string? whatsAppLink = null;
        if (manualDeliveryRequired || verificationRequired)
        {
            const string fallbackNumber = "447961725989";
            var configuredNumber = runtimeSettings is null
                ? null
                : (await runtimeSettings.GetAsync(cancellationToken)).Support.WhatsAppNumber;
            var digits = string.Concat((configuredNumber ?? fallbackNumber).Where(char.IsDigit));
            if (digits.Length == 0) digits = fallbackNumber;
            var proofMessage = Uri.EscapeDataString(
                $"Hello OET with Dr. Hesham, I have paid for {quote.PlanCode ?? "my package"} (order {quote.Id}). Here is my payment receipt.");
            whatsAppLink = $"https://wa.me/{digits}?text={proofMessage}";
        }

        return new BillingPaymentStatusResponse(
            status,
            quote.Id,
            quote.CheckoutSessionId,
            productType,
            quote.PlanCode,
            purchasedAddOnCodes,
            quoteResponse.Items,
            quote.TotalAmount,
            quote.Currency,
            invoice?.Id,
            resolvedSubscriptionId,
            FailureReasonForBillingPaymentStatus(status),
            status == "completed" ? transaction?.UpdatedAt ?? invoice?.IssuedAt : null,
            quote.ExpiresAt,
            manualDeliveryRequired,
            manualDeliveryRequired ? whatsAppLink : null,
            verificationRequired,
            verificationRequired ? whatsAppLink : null,
            verificationRequired
                ? "Payment received — your order is Pending Verification. An admin will approve it shortly; you can also send your receipt on WhatsApp."
                : null);
    }

    public async Task<object> GetBillingExtrasAsync()
    {
        var addOns = await db.BillingAddOns.AsNoTracking()
            .Where(x => x.Status == BillingAddOnStatus.Active && (x.IsRecurring || x.GrantCredits > 0 || x.AppliesToAllPlans))
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Price)
            .ToListAsync();

        return new
        {
            items = addOns.Select(x => new
            {
                id = x.Code,
                code = x.Code,
                name = x.Name,
                productType = x.IsRecurring ? "addon_purchase" : "review_credits",
                quantity = x.GrantCredits > 0 ? x.GrantCredits : Math.Max(1, x.QuantityStep),
                price = x.Price,
                currency = x.Currency,
                interval = x.Interval,
                status = x.Status.ToString().ToLowerInvariant(),
                description = x.Description,
                grantCredits = x.GrantCredits,
                durationDays = x.DurationDays,
                isRecurring = x.IsRecurring,
                appliesToAllPlans = x.AppliesToAllPlans,
                quantityStep = x.QuantityStep,
                maxQuantity = x.MaxQuantity,
                compatiblePlanCodes = JsonSupport.Deserialize<List<string>>(x.CompatiblePlanCodesJson, [])
            })
        };
    }

    /// <summary>
    /// Returns the AI Credits storefront catalog (<c>addonKind="ai_package"</c>)
    /// grouped into Full / Separate (listening, reading, writing, speaking) / Mock
    /// families for the billing module's "AI Credits" tab. Grouping is derived from
    /// the canonical <c>pkg_*</c> code prefix so no extra catalog column is required.
    /// Purchases flow through the same quote → checkout-session → webhook pipeline
    /// as every other add-on (<see cref="CreateCheckoutSessionAsync"/>).
    /// </summary>
    /// <summary>
    /// Public learner-billing copy overrides as a flat <c>{ key: value }</c> map. The page
    /// merges these over its in-code defaults, so an empty map renders every default string.
    /// </summary>
    public async Task<object> GetBillingContentAsync(CancellationToken ct)
    {
        var rows = await db.BillingContentStrings.AsNoTracking()
            .Select(x => new { x.Key, x.Value })
            .ToListAsync(ct);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in rows) map[row.Key] = row.Value;
        return map;
    }

    public async Task<object> GetAiPackagesAsync()
    {
        string[] canonicalCodes =
        [
            "pkg_quick_check", "pkg_exam_prep_pro", "pkg_oet_mastery",
            "pkg_mock_1", "pkg_mock_3", "pkg_mock_5",
            "pkg_listening_starter", "pkg_listening_standard", "pkg_listening_pro",
            "pkg_reading_starter", "pkg_reading_standard", "pkg_reading_pro",
            "pkg_writing_starter", "pkg_writing_standard", "pkg_writing_pro",
            "pkg_speaking_starter", "pkg_speaking_standard", "pkg_speaking_pro"
        ];
        var addOns = await db.BillingAddOns.AsNoTracking()
            .Where(x => x.Status == BillingAddOnStatus.Active
                        && x.AddonKind == "ai_package"
                        && canonicalCodes.Contains(x.Code))
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Price)
            .ToListAsync();

        var views = addOns.Select(ToAiPackageView).ToList();
        var currency = views.FirstOrDefault()?.Currency ?? "GBP";

        static object Project(AiPackageView v) => new
        {
            code = v.Code,
            name = v.Name,
            description = v.Description,
            price = v.Price,
            currency = v.Currency,
            credits = v.Credits,
            sharedCredits = v.SharedCredits,
            writingCredits = v.WritingCredits,
            speakingCredits = v.SpeakingCredits,
            mocks = v.Mocks,
            validityDays = v.ValidityDays,
            priorityQueue = v.PriorityQueue,
            unlimitedGrading = v.UnlimitedGrading,
            unlimitedListening = v.UnlimitedListening,
            unlimitedReading = v.UnlimitedReading,
            group = v.Group,
            features = v.Features,
        };

        return new
        {
            currency,
            full = views.Where(v => v.Group == "full").Select(Project).ToList(),
            separate = new
            {
                listening = views.Where(v => v.Group == "listening").Select(Project).ToList(),
                reading = views.Where(v => v.Group == "reading").Select(Project).ToList(),
                writing = views.Where(v => v.Group == "writing").Select(Project).ToList(),
                speaking = views.Where(v => v.Group == "speaking").Select(Project).ToList(),
            },
            mock = views.Where(v => v.Group == "mock").Select(Project).ToList(),
        };
    }

    private static AiPackageView ToAiPackageView(BillingAddOn x)
    {
        var extras = ReadAiPackageExtras(x.GrantEntitlementsJson);
        // Prefer the admin-configured group; fall back to code-prefix derivation for legacy seeded rows.
        var group = string.IsNullOrWhiteSpace(x.AiPackageGroup)
            ? ResolveAiPackageGroup(x.Code)
            : x.AiPackageGroup.Trim().ToLowerInvariant();
        var credits = extras.SharedCredits
            ?? extras.FlexibleCredits
            ?? x.GrantCredits;
        // FINAL 2026-09-06: Writing/Speaking activities cost 2 AI credits
        // each from any pool (dedicated, Flexible W/S, or Shared).
        var writingCredits = extras.WritingItems ?? extras.WritingCredits ?? x.LettersGranted;
        var speakingCredits = extras.SpeakingItems ?? extras.SpeakingCredits ?? x.SessionsGranted;
        if (group == "writing" && writingCredits == 0) writingCredits = credits;
        if (group == "speaking" && speakingCredits == 0) speakingCredits = credits;
        // Prefer admin-authored feature bullets; fall back to auto-generated copy when none stored.
        var features = ReadAiFeatures(x.AiFeaturesJson) ?? BuildAiPackageFeatures(
            group,
            credits,
            extras.FlexibleCredits ?? 0,
            writingCredits,
            speakingCredits,
            extras.Mocks,
            x.DurationDays,
            extras.PriorityQueue,
            extras.ListeningTests,
            extras.ReadingTests);
        return new AiPackageView(
            x.Code, x.Name, x.Description ?? string.Empty,
            x.Price, x.Currency, credits, writingCredits,
            speakingCredits, extras.Mocks, x.DurationDays, extras.PriorityQueue,
            extras.UnlimitedGrading, extras.UnlimitedListening, extras.UnlimitedReading,
            group, features, extras.SharedCredits ?? 0);
    }

    private static string ResolveAiPackageGroup(string code)
    {
        if (code.StartsWith("pkg_listening", StringComparison.OrdinalIgnoreCase)) return "listening";
        if (code.StartsWith("pkg_reading", StringComparison.OrdinalIgnoreCase)) return "reading";
        if (code.StartsWith("pkg_writing", StringComparison.OrdinalIgnoreCase)) return "writing";
        if (code.StartsWith("pkg_speaking", StringComparison.OrdinalIgnoreCase)) return "speaking";
        if (code.StartsWith("pkg_mock", StringComparison.OrdinalIgnoreCase)) return "mock";
        return "full";
    }

    private static IReadOnlyList<string> BuildAiPackageFeatures(
        string group,
        int credits,
        int flexibleCredits,
        int writingCredits,
        int speakingCredits,
        int mocks,
        int validityDays,
        bool priorityQueue,
        int? listeningTests,
        int? readingTests)
    {
        var features = new List<string>();
        var validity = validityDays >= 180 ? "6-month validity" : $"{validityDays}-day validity";
        switch (group)
        {
            case "full":
                // FINAL 2026-09-06: candidates count attempts, not credits.
                // One attempt (1 letter OR 1 card) costs 2 AI credits.
                var attempts = flexibleCredits / AiGradingCreditCost.CreditsPerWritingOrSpeakingActivity;
                if (attempts > 0)
                    features.Add($"{attempts} flexible AI practice attempts for Writing or Speaking");
                else if (credits > 0)
                    features.Add($"{credits} Shared AI credits (Writing, Speaking, Listening or Reading)");
                else
                    features.Add("Unlimited AI assessment for Writing and Speaking");
                if (mocks > 0) features.Add($"{mocks} full mock exam{(mocks == 1 ? string.Empty : "s")} included");
                // Only advertise unlimited L&R when the package actually grants it (both allowances null = unlimited).
                if (listeningTests is null && readingTests is null)
                    features.Add("Unlimited Listening & Reading practice");
                if (listeningTests is not null)
                    features.Add($"{listeningTests} Listening practice exam{(listeningTests == 1 ? string.Empty : "s")}");
                if (readingTests is not null)
                    features.Add($"{readingTests} Reading practice exam{(readingTests == 1 ? string.Empty : "s")}");
                features.Add("AI feedback reports");
                if (priorityQueue) features.Add("Priority grading queue");
                features.Add(validity);
                break;
            case "writing":
                features.Add($"{writingCredits} AI-graded Writing letters");
                features.Add("Instant specialised feedback on every letter");
                features.Add("Detailed per-criterion feedback");
                features.Add(validity);
                break;
            case "speaking":
                features.Add($"{speakingCredits} AI-graded Speaking cards");
                features.Add("Instant specialised feedback on every card");
                features.Add("Detailed transcript-based feedback aligned with OET Speaking criteria");
                features.Add(validity);
                break;
            case "listening":
                features.Add(listeningTests is null ? "Unlimited Listening practice tests" : $"{listeningTests} Listening practice tests");
                features.Add("Deterministic answer-key marking");
                features.Add("Always free to grade - no AI credits used");
                features.Add(validity);
                break;
            case "reading":
                features.Add(readingTests is null ? "Unlimited Reading practice tests" : $"{readingTests} Reading practice tests");
                features.Add("Deterministic answer-key marking");
                features.Add("Always free to grade — no credits used");
                features.Add(validity);
                break;
            case "mock":
                features.Add($"{mocks} full mock exam{(mocks == 1 ? string.Empty : "s")} — all 4 subtests");
                features.Add("Writing + Speaking AI-graded");
                features.Add("Listening + Reading auto-marked");
                features.Add("Separate from AI credits");
                features.Add(validity);
                break;
        }
        return features;
    }

    /// <summary>
    /// Parses an admin-authored AI feature bullet list. Returns null (→ auto-generate fallback)
    /// when the JSON is empty, malformed, or contains no usable strings.
    /// </summary>
    private static IReadOnlyList<string>? ReadAiFeatures(string? aiFeaturesJson)
    {
        if (string.IsNullOrWhiteSpace(aiFeaturesJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(aiFeaturesJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            var features = new List<string>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String) continue;
                var text = item.GetString();
                if (!string.IsNullOrWhiteSpace(text)) features.Add(text.Trim());
            }
            return features.Count > 0 ? features : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AiPackageExtras ReadAiPackageExtras(string? grantEntitlementsJson)
    {
        if (string.IsNullOrWhiteSpace(grantEntitlementsJson))
            return new(null, null, null, null, null, null, null, null, 0, false, false, false, false);
        try
        {
            using var doc = JsonDocument.Parse(grantEntitlementsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return new(null, null, null, null, null, null, null, null, 0, false, false, false, false);
            var root = doc.RootElement;
            var mocks = ReadInt(root, "mock_exams") ?? ReadInt(root, "mockFull") ?? 0;
            var pq = root.TryGetProperty("priority_queue", out var p) && p.ValueKind == JsonValueKind.True;
            var unlimitedGrading = root.TryGetProperty("unlimited_grading", out var grading)
                                   && grading.ValueKind == JsonValueKind.True;
            var unlimitedListening = IsExplicitNull(root, "listening_tests")
                || (root.TryGetProperty("unlimited_listening", out var ul) && ul.ValueKind == JsonValueKind.True);
            var unlimitedReading = IsExplicitNull(root, "reading_tests")
                || (root.TryGetProperty("unlimited_reading", out var ur) && ur.ValueKind == JsonValueKind.True);
            return new(
                ReadInt(root, "shared_credits"),
                ReadInt(root, "flexible_credits"),
                ReadInt(root, "writing_only_credits"),
                ReadInt(root, "speaking_only_credits"),
                ReadInt(root, "writing_items"),
                ReadInt(root, "speaking_items"),
                ReadNullableAllowance(root, "listening_tests"),
                ReadNullableAllowance(root, "reading_tests"),
                mocks,
                pq,
                unlimitedGrading,
                unlimitedListening,
                unlimitedReading);
        }
        catch (JsonException)
        {
            return new(null, null, null, null, null, null, null, null, 0, false, false, false, false);
        }
    }

    private static int? ReadInt(JsonElement root, string name)
        => root.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetInt32(out var parsed)
            ? Math.Max(0, parsed)
            : null;

    private static int? ReadNullableAllowance(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Null) return null;
        return value.TryGetInt32(out var parsed) ? Math.Max(0, parsed) : null;
    }

    private static bool IsExplicitNull(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Null;

    private sealed record AiPackageExtras(
        int? SharedCredits,
        int? FlexibleCredits,
        int? WritingCredits,
        int? SpeakingCredits,
        int? WritingItems,
        int? SpeakingItems,
        int? ListeningTests,
        int? ReadingTests,
        int Mocks,
        bool PriorityQueue,
        bool UnlimitedGrading,
        bool UnlimitedListening,
        bool UnlimitedReading);

    private sealed record AiPackageView(
        string Code, string Name, string Description, decimal Price, string Currency,
        int Credits, int WritingCredits, int SpeakingCredits, int Mocks, int ValidityDays,
        bool PriorityQueue, bool UnlimitedGrading, bool UnlimitedListening, bool UnlimitedReading,
        string Group, IReadOnlyList<string> Features, int SharedCredits);

    public async Task<object> CreateCheckoutSessionAsync(string userId, CheckoutSessionCreateRequest request, CancellationToken cancellationToken)
    {
        var checkoutUser = await EnsureUserAsync(userId, cancellationToken);
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);

        var normalizedProductType = (request.ProductType ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedProductType is not ("review_credits" or "plan_purchase" or "plan_upgrade" or "plan_downgrade" or "addon_purchase"))
        {
            throw ApiException.Validation(
                "unsupported_checkout_product",
                $"Unsupported checkout product '{request.ProductType}'.",
                [new ApiFieldError("productType", "unsupported", "Only supported learner checkout products can be purchased.")]);
        }

        if (request.Quantity <= 0)
        {
            throw ApiException.Validation(
                "invalid_checkout_quantity",
                "Checkout quantity must be greater than zero.",
                [new ApiFieldError("quantity", "invalid", "Choose a checkout quantity greater than zero.")]);
        }

        if (normalizedProductType is "plan_purchase" or "plan_upgrade" or "plan_downgrade" or "addon_purchase"
            && string.IsNullOrWhiteSpace(request.PriceId))
        {
            throw ApiException.Validation(
                "target_item_required",
                "A target plan or add-on id is required for this checkout.",
                [new ApiFieldError("priceId", "required", "Choose the plan or add-on you want to purchase.")]);
        }

        var gatewayLabel = string.IsNullOrWhiteSpace(request.Gateway) ? PaymentGatewayNames.Whop : request.Gateway.Trim().ToLowerInvariant();

        var normalizedAddOnCodes = NormalizeCodes(request.AddOnCodes);
        var idempotencyKey = NormalizeIdempotencyKey(request.IdempotencyKey);
        var idempotencyRequestHash = idempotencyKey is null
            ? null
            : ComputeIdempotencyRequestHash(new
            {
                userId,
                productType = normalizedProductType,
                request.Quantity,
                priceId = request.PriceId?.Trim(),
                couponCode = request.CouponCode?.Trim().ToUpperInvariant(),
                addOnCodes = normalizedAddOnCodes.OrderBy(code => code, StringComparer.OrdinalIgnoreCase).ToArray(),
                gateway = gatewayLabel,
                quoteId = request.QuoteId?.Trim(),
                parentSubscriptionId = request.ParentSubscriptionId?.Trim()
            });
        if (idempotencyKey is not null && idempotencyRequestHash is not null)
        {
            var reservation = await ReservePaymentIdempotencyAsync(
                "checkout-session",
                idempotencyKey,
                userId,
                idempotencyRequestHash,
                cancellationToken);
            if (!reservation.ShouldProcess)
            {
                return reservation.CachedResponse!;
            }
        }

        BillingQuoteResponse quoteResponse;
        BillingQuote quoteEntity;
        var providerRequestReturned = false;
        var idempotencyCompleted = false;
        object? idempotencyResponse = null;
        try
        {
        if (!string.IsNullOrWhiteSpace(request.QuoteId))
        {
            quoteEntity = await db.BillingQuotes.FirstOrDefaultAsync(x => x.Id == request.QuoteId && x.UserId == userId, cancellationToken)
                ?? throw ApiException.NotFound("billing_quote_not_found", "The requested billing quote could not be found.");

            var now = DateTimeOffset.UtcNow;
            if (quoteEntity.ExpiresAt < now)
            {
                var releasedCouponKeys = await ReleasePreCheckoutCouponReservationsForQuoteAsync(quoteEntity, now, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await RefreshCouponRedemptionCountsAsync(releasedCouponKeys, now, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                throw ApiException.Validation("billing_quote_expired", "This billing quote has expired.");
            }
            EnsureQuoteIsFulfillable(quoteEntity, now);

            // Bind the quote snapshot to the inbound request so a stale or swapped
            // quoteId cannot be reused with a different product, plan, coupon, or add-on.
            var quoteAddOnCodes = JsonSupport.Deserialize<List<string>>(quoteEntity.AddOnCodesJson, []);
            if (!string.IsNullOrWhiteSpace(request.PriceId))
            {
                var matchesPlan = !string.IsNullOrWhiteSpace(quoteEntity.PlanCode)
                    && string.Equals(quoteEntity.PlanCode, request.PriceId, StringComparison.OrdinalIgnoreCase);
                var matchesAddOn = quoteAddOnCodes.Any(code => string.Equals(code, request.PriceId, StringComparison.OrdinalIgnoreCase));
                if (!matchesPlan && !matchesAddOn)
                {
                    throw ApiException.Validation(
                        "quote_mismatch",
                        "The supplied priceId does not match the saved quote.",
                        [new ApiFieldError("priceId", "mismatch", "Refresh your quote before checking out.")]);
                }
            }

            if (!string.IsNullOrWhiteSpace(request.CouponCode)
                && !string.Equals(request.CouponCode, quoteEntity.CouponCode, StringComparison.OrdinalIgnoreCase))
            {
                throw ApiException.Validation(
                    "quote_mismatch",
                    "The supplied couponCode does not match the saved quote.",
                    [new ApiFieldError("couponCode", "mismatch", "Refresh your quote before checking out.")]);
            }

            if (normalizedAddOnCodes.Count > 0)
            {
                var requested = normalizedAddOnCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var saved = new HashSet<string>(quoteAddOnCodes, StringComparer.OrdinalIgnoreCase);
                if (!requested.SetEquals(saved))
                {
                    throw ApiException.Validation(
                        "quote_mismatch",
                        "The supplied add-on codes do not match the saved quote.",
                        [new ApiFieldError("addOnCodes", "mismatch", "Refresh your quote before checking out.")]);
                }
            }

            // Re-run the profession / content-availability gate against the plan the
            // saved quote locked in. BuildBillingQuoteAsync already gates the fresh-quote
            // path below, but a stored quote skips it entirely — and the learner's
            // profession may have changed since the quote was built.
            if (normalizedProductType is "plan_purchase" or "plan_upgrade" or "plan_downgrade"
                && !string.IsNullOrWhiteSpace(quoteEntity.PlanCode))
            {
                var quotedPlan = await FindBillingPlanAsync(quoteEntity.PlanCode, cancellationToken);
                if (quotedPlan is not null)
                {
                    await EnsurePlanMatchesLearnerProfessionAsync(checkoutUser, quotedPlan, cancellationToken);
                }
            }

            quoteResponse = DeserializeQuoteResponse(quoteEntity);
        }
        else
        {
            quoteResponse = await BuildBillingQuoteAsync(userId, new BillingQuoteRequest(
                normalizedProductType,
                request.Quantity,
                request.PriceId,
                request.CouponCode,
                normalizedAddOnCodes,
                request.ParentSubscriptionId), cancellationToken, persistQuote: true);
            quoteEntity = await db.BillingQuotes.FirstAsync(x => x.Id == quoteResponse.QuoteId && x.UserId == userId, cancellationToken);
        }

        var purchaseTarget = quoteResponse.Items.FirstOrDefault()?.Code ?? quoteEntity.PlanCode ?? request.PriceId;
        await EnsureCheckoutGatewayAsync(gatewayLabel, cancellationToken);

        if (quoteEntity.Status == BillingQuoteStatus.Applied && !string.IsNullOrWhiteSpace(quoteEntity.CheckoutSessionId))
        {
            var reusedCheckout = await TryReuseUnpaidCheckoutSessionAsync(
                userId,
                quoteEntity,
                quoteResponse,
                normalizedProductType,
                request.Quantity,
                gatewayLabel,
                cancellationToken);
            if (reusedCheckout is not null)
            {
                if (idempotencyKey is not null && idempotencyRequestHash is not null)
                {
                    await CompletePaymentIdempotencyAsync(
                        "checkout-session",
                        idempotencyKey,
                        userId,
                        idempotencyRequestHash,
                        reusedCheckout,
                        cancellationToken);
                }

                await db.SaveChangesAsync(cancellationToken);
                idempotencyCompleted = true;
                return reusedCheckout;
            }

            await SupersedeUnpaidCheckoutSessionAsync(userId, quoteEntity, cancellationToken);
        }

        PaymentIntentResult checkoutIntent;
        try
        {
            checkoutIntent = await paymentGateways.GetGateway(gatewayLabel).CreatePaymentIntentAsync(
            new CreatePaymentIntentRequest(
                UserId: userId,
                Amount: quoteEntity.TotalAmount,
                Currency: quoteEntity.Currency,
                ProductType: normalizedProductType,
                ProductId: quoteEntity.Id,
                Description: quoteResponse.Summary,
                                Metadata: new Dictionary<string, string>
                                {
                                        ["quote_id"] = quoteEntity.Id,
                                        ["product_type"] = normalizedProductType,
                                        ["purchase_target"] = purchaseTarget ?? string.Empty,
                                        ["user_id"] = userId,
                                        ["plan_code"] = quoteEntity.PlanCode ?? string.Empty,
                                        ["coupon_code"] = quoteEntity.CouponCode ?? string.Empty,
                                        ["add_on_codes"] = string.Join(',', JsonSupport.Deserialize<List<string>>(quoteEntity.AddOnCodesJson, [])),
                                        ["parent_subscription_id"] = request.ParentSubscriptionId ?? string.Empty,
                                        ["plan_version_id"] = quoteEntity.PlanVersionId ?? string.Empty,
                                        ["add_on_version_ids"] = quoteEntity.AddOnVersionIdsJson,
                                        ["coupon_version_id"] = quoteEntity.CouponVersionId ?? string.Empty
                                },
                                SuccessUrl: platformLinks.BuildWebUrl($"/billing/payment-return?status=success&gateway={Uri.EscapeDataString(gatewayLabel)}&quote={Uri.EscapeDataString(quoteEntity.Id)}&session={{CHECKOUT_SESSION_ID}}"),
                                    CancelUrl: platformLinks.BuildWebUrl($"/billing/payment-return?status=cancelled&gateway={Uri.EscapeDataString(gatewayLabel)}&quote={Uri.EscapeDataString(quoteEntity.Id)}"),
                                    IdempotencyKey: idempotencyKey),
                        cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not configured", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation(
                "gateway_unavailable",
                "This payment method is temporarily unavailable. Please pay by card instead.",
                [new ApiFieldError("gateway", "unavailable", "Choose a different payment method.")]);
        }
        catch (PaymentGatewayApiException)
        {
            // The payment provider's API rejected the request (e.g. invalid key, declined
            // params). Surface a clean, retryable 503 instead of an opaque 500. The real
            // provider detail is already logged inside the gateway and is never shown to
            // learners.
            throw ApiException.ServiceUnavailable(
                "payment_gateway_error",
                "We couldn't start your payment right now. Please try again in a moment or choose another payment method.",
                retryable: true);
        }
        catch (HttpRequestException)
        {
            throw ApiException.ServiceUnavailable(
                "payment_gateway_error",
                "We couldn't start your payment right now. Please try again in a moment or choose another payment method.",
                retryable: true);
        }
                        providerRequestReturned = true;

        quoteEntity.CheckoutSessionId = checkoutIntent.GatewayTransactionId;
        quoteEntity.Status = BillingQuoteStatus.Applied;

        var checkoutUrl = string.IsNullOrWhiteSpace(checkoutIntent.CheckoutUrl)
            ? platformLinks.BuildCheckoutUrl(
                checkoutIntent.GatewayTransactionId,
                normalizedProductType,
                request.Quantity,
                planId: quoteEntity.PlanCode,
                couponCode: quoteEntity.CouponCode,
                addOnCodes: JsonSupport.Deserialize<List<string>>(quoteEntity.AddOnCodesJson, []),
                quoteId: quoteEntity.Id)
            : checkoutIntent.CheckoutUrl;
        var response = BuildCheckoutSessionClientResponse(
            quoteEntity,
            quoteResponse,
            normalizedProductType,
            request.Quantity,
            gatewayLabel,
            checkoutIntent.GatewayTransactionId,
            checkoutUrl,
            checkoutIntent.ClientSecret,
            checkoutIntent.Status);
        idempotencyResponse = response;

        var paymentTransaction = await db.PaymentTransactions.FirstOrDefaultAsync(
            transaction => transaction.GatewayTransactionId == checkoutIntent.GatewayTransactionId,
            cancellationToken);
        if (paymentTransaction is null)
        {
            var now = DateTimeOffset.UtcNow;
            paymentTransaction = new PaymentTransaction
            {
                Id = Guid.NewGuid(),
                LearnerUserId = userId,
                Gateway = gatewayLabel,
                GatewayTransactionId = checkoutIntent.GatewayTransactionId,
                TransactionType = normalizedProductType is "plan_purchase" or "plan_upgrade" or "plan_downgrade"
                    ? "subscription_payment"
                    : "one_time_purchase",
                Status = "pending",
                Amount = quoteEntity.TotalAmount,
                Currency = quoteEntity.Currency,
                ProductType = normalizedProductType is "plan_purchase" or "plan_upgrade" or "plan_downgrade" ? "plan" : "addon",
                ProductId = purchaseTarget ?? quoteEntity.Id,
                QuoteId = quoteEntity.Id,
                PlanVersionId = quoteEntity.PlanVersionId,
                AddOnVersionIdsJson = quoteEntity.AddOnVersionIdsJson,
                CouponVersionId = quoteEntity.CouponVersionId,
                MetadataJson = SerializeCheckoutPaymentMetadata(
                    quoteEntity,
                    normalizedProductType,
                    checkoutIntent.ClientSecret,
                    purchaseTarget,
                    checkoutUrl),
                CreatedAt = now,
                UpdatedAt = now
            };
            db.PaymentTransactions.Add(paymentTransaction);
        }

        paymentTransaction.QuoteId = quoteEntity.Id;
        paymentTransaction.PlanVersionId = quoteEntity.PlanVersionId;
        paymentTransaction.AddOnVersionIdsJson = quoteEntity.AddOnVersionIdsJson;
        paymentTransaction.CouponVersionId = quoteEntity.CouponVersionId;
        paymentTransaction.MetadataJson = SerializeCheckoutPaymentMetadata(
            quoteEntity,
            normalizedProductType,
            checkoutIntent.ClientSecret,
            purchaseTarget,
            checkoutUrl);

        var reservedRedemptions = await db.BillingCouponRedemptions
            .Where(redemption => redemption.QuoteId == quoteEntity.Id && redemption.Status == BillingRedemptionStatus.Reserved)
            .ToListAsync(cancellationToken);
        foreach (var redemption in reservedRedemptions)
        {
            redemption.CheckoutSessionId = checkoutIntent.GatewayTransactionId;
        }

        db.BillingEvents.Add(new BillingEvent
        {
            Id = $"bill-evt-{Guid.NewGuid():N}",
            UserId = userId,
            QuoteId = quoteEntity.Id,
            EventType = "checkout_session_created",
            EntityType = "CheckoutSession",
            EntityId = checkoutIntent.GatewayTransactionId,
            PayloadJson = JsonSupport.Serialize(new
            {
                productType = normalizedProductType,
                quantity = request.Quantity,
                planCode = quoteEntity.PlanCode,
                couponCode = quoteEntity.CouponCode,
                addOnCodes = JsonSupport.Deserialize<List<string>>(quoteEntity.AddOnCodesJson, []),
                totalAmount = quoteEntity.TotalAmount,
                currency = quoteEntity.Currency,
                gateway = gatewayLabel,
                status = checkoutIntent.Status
            }),
            OccurredAt = DateTimeOffset.UtcNow
        });

        if (idempotencyKey is not null && idempotencyRequestHash is not null)
        {
            await CompletePaymentIdempotencyAsync("checkout-session", idempotencyKey, userId, idempotencyRequestHash, response, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        idempotencyCompleted = true;

        await RecordEventAsync(userId, "checkout_started", new
        {
            productType = normalizedProductType,
            quantity = request.Quantity,
            targetPlanId = quoteEntity.PlanCode,
            couponCode = quoteEntity.CouponCode,
            quoteId = quoteEntity.Id,
            totalAmount = quoteEntity.TotalAmount,
            gateway = gatewayLabel
        }, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return response;
        }
        catch
        {
            if (idempotencyKey is not null && idempotencyRequestHash is not null && !idempotencyCompleted)
            {
                if (idempotencyResponse is not null)
                {
                    await TryCompletePaymentIdempotencyAsync("checkout-session", idempotencyKey, userId, idempotencyRequestHash, idempotencyResponse, cancellationToken);
                }
                else if (!providerRequestReturned)
                {
                    await RemovePaymentIdempotencyReservationAsync("checkout-session", idempotencyKey, cancellationToken);
                }
            }
            throw;
        }
    }

    private async Task<Dictionary<string, object?>?> GetIdempotentResponseAsync(string scope, string key, CancellationToken cancellationToken)
    {
        var record = await db.IdempotencyRecords.FirstOrDefaultAsync(x => x.Scope == scope && x.Key == key, cancellationToken);
        return record is null
            ? null
            : JsonSupport.Deserialize<Dictionary<string, object?>>(record.ResponseJson, new Dictionary<string, object?>());
    }

    private sealed record PaymentIdempotencyReservation(bool ShouldProcess, Dictionary<string, object?>? CachedResponse);

    private static string? NormalizeIdempotencyKey(string? key)
    {
        var normalized = key?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (normalized.Length > PaymentIdempotencyKeyMaxLength || !PaymentIdempotencyKeyRegex.IsMatch(normalized))
        {
            throw ApiException.Validation(
                "invalid_idempotency_key",
                $"Idempotency keys must be 1-{PaymentIdempotencyKeyMaxLength} ASCII token characters.",
                [new ApiFieldError("idempotencyKey", "invalid", "Use letters, numbers, dots, underscores, colons, or hyphens only.")]);
        }

        return normalized;
    }

    private static string ComputeIdempotencyRequestHash(object payload)
    {
        var json = JsonSupport.Serialize(payload);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private async Task<PaymentIdempotencyReservation> ReservePaymentIdempotencyAsync(
        string scope,
        string key,
        string userId,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var existing = await db.IdempotencyRecords.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Scope == scope && x.Key == key, cancellationToken);
        if (existing is not null)
        {
            return ReadPaymentIdempotencyRecord(existing, userId, requestHash);
        }

        var record = new IdempotencyRecord
        {
            Id = $"idem-{Guid.NewGuid():N}",
            Scope = scope,
            Key = key,
            ResponseJson = CreatePaymentIdempotencyEnvelope(userId, requestHash, "processing", response: null),
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.IdempotencyRecords.Add(record);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return new PaymentIdempotencyReservation(true, null);
        }
        catch (DbUpdateException)
        {
            db.Entry(record).State = EntityState.Detached;
            existing = await db.IdempotencyRecords.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Scope == scope && x.Key == key, cancellationToken);
            if (existing is not null)
            {
                return ReadPaymentIdempotencyRecord(existing, userId, requestHash);
            }

            throw;
        }
    }

    private PaymentIdempotencyReservation ReadPaymentIdempotencyRecord(
        IdempotencyRecord record,
        string userId,
        string requestHash)
    {
        using var document = JsonDocument.Parse(record.ResponseJson);
        var root = document.RootElement;

        if (!root.TryGetProperty("idempotency", out var idempotency))
        {
            return new PaymentIdempotencyReservation(
                false,
                JsonSupport.Deserialize<Dictionary<string, object?>>(record.ResponseJson, new Dictionary<string, object?>()));
        }

        var storedUserId = idempotency.TryGetProperty("userId", out var userIdElement) ? userIdElement.GetString() : null;
        var storedRequestHash = idempotency.TryGetProperty("requestHash", out var hashElement) ? hashElement.GetString() : null;
        if (!string.Equals(storedUserId, userId, StringComparison.Ordinal)
            || !string.Equals(storedRequestHash, requestHash, StringComparison.Ordinal))
        {
            throw ApiException.Conflict(
                "idempotency_key_reused",
                "This idempotency key was already used for a different billing request.",
                [new ApiFieldError("idempotencyKey", "reused", "Generate a new idempotency key for a changed request.")]);
        }

        var status = idempotency.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null;
        if (string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase)
            && root.TryGetProperty("response", out var response)
            && response.ValueKind != JsonValueKind.Null)
        {
            var cached = JsonSerializer.Deserialize<Dictionary<string, object?>>(response.GetRawText())
                ?? new Dictionary<string, object?>();
            return new PaymentIdempotencyReservation(false, cached);
        }

        throw ApiException.Conflict(
            "idempotency_in_progress",
            "A billing request with this idempotency key is still being prepared. Please retry shortly.",
            [new ApiFieldError("idempotencyKey", "in_progress", "Retry the same request in a few seconds.")]);
    }

    private async Task CompletePaymentIdempotencyAsync(
        string scope,
        string key,
        string userId,
        string requestHash,
        object response,
        CancellationToken cancellationToken)
    {
        var record = await db.IdempotencyRecords
            .FirstOrDefaultAsync(x => x.Scope == scope && x.Key == key, cancellationToken);
        if (record is null)
        {
            return;
        }

        ReadPaymentIdempotencyRecordForCompletion(record, userId, requestHash);
        record.ResponseJson = CreatePaymentIdempotencyEnvelope(userId, requestHash, "completed", response);
    }

    private async Task TryCompletePaymentIdempotencyAsync(
        string scope,
        string key,
        string userId,
        string requestHash,
        object response,
        CancellationToken cancellationToken)
    {
        try
        {
            db.ChangeTracker.Clear();
            await CompletePaymentIdempotencyAsync(scope, key, userId, requestHash, response, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Preserve the original billing error. A later retry with the same
            // provider idempotency key can still recover at the gateway layer.
        }
    }

    private static void ReadPaymentIdempotencyRecordForCompletion(
        IdempotencyRecord record,
        string userId,
        string requestHash)
    {
        using var document = JsonDocument.Parse(record.ResponseJson);
        if (!document.RootElement.TryGetProperty("idempotency", out var idempotency))
        {
            return;
        }

        var storedUserId = idempotency.TryGetProperty("userId", out var userIdElement) ? userIdElement.GetString() : null;
        var storedRequestHash = idempotency.TryGetProperty("requestHash", out var hashElement) ? hashElement.GetString() : null;
        if (!string.Equals(storedUserId, userId, StringComparison.Ordinal)
            || !string.Equals(storedRequestHash, requestHash, StringComparison.Ordinal))
        {
            throw ApiException.Conflict("idempotency_key_reused", "This idempotency key belongs to a different request.");
        }
    }

    private async Task RemovePaymentIdempotencyReservationAsync(string scope, string key, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var record = await db.IdempotencyRecords.FirstOrDefaultAsync(x => x.Scope == scope && x.Key == key, cancellationToken);
        if (record is null)
        {
            return;
        }

        db.IdempotencyRecords.Remove(record);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string CreatePaymentIdempotencyEnvelope(
        string userId,
        string requestHash,
        string status,
        object? response)
        => JsonSupport.Serialize(new
        {
            idempotency = new
            {
                version = "payment-v1",
                userId,
                requestHash,
                status
            },
            response
        });

    private async Task SaveIdempotentResponseAsync(string scope, string key, object response, CancellationToken cancellationToken)
    {
        var exists = await db.IdempotencyRecords.AnyAsync(x => x.Scope == scope && x.Key == key, cancellationToken);
        if (exists)
        {
            return;
        }

        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Id = $"idem-{Guid.NewGuid():N}",
            Scope = scope,
            Key = key,
            ResponseJson = JsonSupport.Serialize(response),
            CreatedAt = DateTimeOffset.UtcNow
        });
    }
}
