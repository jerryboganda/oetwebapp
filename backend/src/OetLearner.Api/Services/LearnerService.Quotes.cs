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

    private sealed record ExperimentVariant(string Code, int Weight, decimal PriceMultiplier);

    /// <summary>
    /// Looks up any running pricing experiment for the given product, deterministically
    /// assigns the user to a variant via SHA-256 hash, persists the assignment, and
    /// returns the assignment id and priceMultiplier. Returns (null, 1.0) if no
    /// experiment applies or the user falls outside the rollout bucket.
    /// </summary>
    private async Task<(string? AssignmentId, decimal Multiplier)> TryApplyPricingExperimentAsync(
        string userId, string targetType, string targetId, CancellationToken ct)
    {
        var userRegion = await db.AccountsForUser(userId)
            .Select(u => u.PreferredRegion)
            .FirstOrDefaultAsync(ct);

        var experiments = await db.PricingExperiments
            .Where(e => e.Status == "running"
                && e.TargetType == targetType
                && e.TargetId == targetId
                && (e.Region == "*" || e.Region == userRegion))
            .ToListAsync(ct);

        foreach (var experiment in experiments)
        {
            // Deterministic rollout check: SHA-256(experimentId:userId) → bucket mod 100
            var rolloutHash = SHA256.HashData(Encoding.UTF8.GetBytes($"{experiment.Id}:{userId}"));
            var rolloutBucket = (int)(BinaryPrimitives.ReadUInt64BigEndian(rolloutHash) % 100);
            if (rolloutBucket >= experiment.RolloutPercent)
                continue;

            var variants = JsonSupport.Deserialize<List<ExperimentVariant>>(experiment.VariantsJson, []);
            if (variants.Count == 0)
                continue;

            // Deterministic variant pick: SHA-256(experimentId:userId:variant) → weighted selection
            var variantHash = SHA256.HashData(Encoding.UTF8.GetBytes($"{experiment.Id}:{userId}:variant"));
            var totalWeight = variants.Sum(v => v.Weight);
            if (totalWeight <= 0)
                continue;
            var variantBucket = (int)(BinaryPrimitives.ReadUInt64BigEndian(variantHash) % (ulong)totalWeight);
            string assignedCode = variants[^1].Code;
            int cumulative = 0;
            foreach (var v in variants)
            {
                cumulative += v.Weight;
                if (variantBucket < cumulative) { assignedCode = v.Code; break; }
            }

            // Upsert assignment (unique index on ExperimentId + UserId prevents duplicates)
            var existing = await db.PricingExperimentAssignments
                .FirstOrDefaultAsync(a => a.ExperimentId == experiment.Id && a.UserId == userId, ct);

            if (existing is not null)
            {
                // Respect the pre-existing assignment — don't re-randomise on re-quote
                var existingVariant = variants.FirstOrDefault(v => v.Code == existing.VariantCode);
                return (existing.Id, existingVariant?.PriceMultiplier ?? 1m);
            }

            var assignmentId = TruncateIdentifier($"pea-{Guid.NewGuid():N}");
            db.PricingExperimentAssignments.Add(new PricingExperimentAssignment
            {
                Id = assignmentId,
                ExperimentId = experiment.Id,
                UserId = userId,
                VariantCode = assignedCode,
                AssignedAt = DateTimeOffset.UtcNow,
            });

            var multiplier = variants.FirstOrDefault(v => v.Code == assignedCode)?.PriceMultiplier ?? 1m;
            return (assignmentId, multiplier);
        }

        return (null, 1m);
    }

    private async Task<BillingPlan?> FindBillingPlanAsync(string planCode, CancellationToken cancellationToken)
    {
        var normalized = NormalizeBillingCode(planCode);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return await db.BillingPlans.AsNoTracking()
            .FirstOrDefaultAsync(plan => plan.Code.ToLower() == normalized
                || plan.Id.ToLower() == normalized, cancellationToken);
    }

    private async Task<BillingAddOn?> FindBillingAddOnAsync(string addOnCode, CancellationToken cancellationToken)
    {
        var normalized = NormalizeBillingCode(addOnCode);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return await db.BillingAddOns.AsNoTracking()
            .FirstOrDefaultAsync(addOn => addOn.Code.ToLower() == normalized
                || addOn.Id.ToLower() == normalized, cancellationToken);
    }

    private async Task<bool> IsAiPackageAddOnAsync(string addOnCode, CancellationToken cancellationToken)
    {
        var addOn = await FindBillingAddOnAsync(addOnCode, cancellationToken);
        return string.Equals(addOn?.AddonKind, "ai_package", StringComparison.OrdinalIgnoreCase);
    }

      private async Task<BillingCoupon?> FindBillingCouponAsync(string couponCode, CancellationToken cancellationToken)
      {
          var normalized = NormalizeBillingCode(couponCode);
          if (string.IsNullOrWhiteSpace(normalized))
          {
            return null;
        }

          return await db.BillingCoupons.AsNoTracking()
              .FirstOrDefaultAsync(coupon => coupon.Code.ToLower() == normalized
                  || coupon.Id.ToLower() == normalized, cancellationToken);
      }

      private async Task<BillingPlan?> FindPurchasableBillingPlanAsync(string planCode, CancellationToken cancellationToken)
      {
          var plan = await FindBillingPlanAsync(planCode, cancellationToken);
          return plan is not null && plan.Status == BillingPlanStatus.Active && plan.IsVisible && !plan.IsDraft
              ? plan
              : null;
      }

      /// <summary>
      /// Gate a plan purchase on the buyer's registered profession. A plan tagged
      /// <c>all</c> (or untagged) is sold to everyone; anything else must match the
      /// buyer's ActiveProfessionId, because the package's content is resolved on the
      /// subtest x profession axis and a mismatched buyer would pay for an empty course.
      /// Runs on every checkout entry point (fresh quote and saved quote) so a profession
      /// changed after the quote was built cannot slip through.
      /// </summary>
      private async Task EnsurePlanMatchesLearnerProfessionAsync(
          LearnerUser user,
          BillingPlan plan,
          CancellationToken cancellationToken)
      {
          var planProfession = plan.Profession?.Trim();
          if (string.IsNullOrWhiteSpace(planProfession)
              || string.Equals(planProfession, "all", StringComparison.OrdinalIgnoreCase))
          {
              return;
          }

          if (!string.Equals(planProfession, user.ActiveProfessionId, StringComparison.OrdinalIgnoreCase))
          {
              throw ApiException.Validation(
                  "profession_mismatch",
                  $"{plan.Name} is only available to learners registered under a different profession.",
                  [new ApiFieldError("priceId", "profession_mismatch", "Choose a package that matches your registered profession.")]);
          }

          // A package whose enabled content modules resolve to zero items for this
          // profession is unsellable — block it here rather than let the learner pay for
          // an empty course (e.g. Physiotherapy packages while no physiotherapy video
          // exists yet). Fails open when the service is unregistered.
          if (planContentAvailability is null)
          {
              return;
          }

          var availability = await planContentAvailability.ResolveAsync(plan, user.ActiveProfessionId, cancellationToken);
          if (availability.EmptyEnabledModules.Count > 0)
          {
              throw ApiException.Validation(
                  "content_unavailable_for_profession",
                  $"{plan.Name} has no {string.Join(", ", availability.EmptyEnabledModules)} content for your profession yet. Please contact support.",
                  [new ApiFieldError("priceId", "content_unavailable", "Choose a package that has content for your profession.")]);
          }
      }

      private async Task<bool> UserOwnsTutorBookAsync(string userId, CancellationToken cancellationToken)
          => await db.Subscriptions.AsNoTracking().AnyAsync(s =>
              s.UserId == userId
              && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial)
              && s.TutorBookUnlocked, cancellationToken);

      private async Task<BillingAddOn?> FindPurchasableBillingAddOnAsync(string addOnCode, CancellationToken cancellationToken)
      {
          var addOn = await FindBillingAddOnAsync(addOnCode, cancellationToken);
          return addOn is not null && addOn.Status == BillingAddOnStatus.Active
              ? addOn
              : null;
      }

      private static bool IsAddOnCompatibleWithPlan(BillingAddOn addOn, BillingPlan? plan)
      {
          var compatiblePlanCodes = JsonSupport.Deserialize<List<string>>(addOn.CompatiblePlanCodesJson, []);

          if (addOn.AppliesToAllPlans)
          {
              return true;
          }

          if (!addOn.RequiresEligibleParent && compatiblePlanCodes.Count == 0)
          {
              return true;
          }

          if (compatiblePlanCodes.Count == 0 || plan is null)
          {
              return false;
          }

          return compatiblePlanCodes.Any(code =>
              string.Equals(code, plan.Code, StringComparison.OrdinalIgnoreCase) ||
              string.Equals(code, plan.Id, StringComparison.OrdinalIgnoreCase));
      }

      private async Task<BillingCatalogVersionRef?> ResolvePlanVersionRefAsync(BillingPlan plan, CancellationToken cancellationToken)
      {
          BillingPlanVersion? version = null;
          if (!string.IsNullOrWhiteSpace(plan.ActiveVersionId))
          {
              version = await db.BillingPlanVersions.AsNoTracking()
                  .FirstOrDefaultAsync(item => item.PlanId == plan.Id && item.Id == plan.ActiveVersionId, cancellationToken);
          }

          if (version is null && !string.IsNullOrWhiteSpace(plan.LatestVersionId))
          {
              version = await db.BillingPlanVersions.AsNoTracking()
                  .FirstOrDefaultAsync(item => item.PlanId == plan.Id && item.Id == plan.LatestVersionId, cancellationToken);
          }

          version ??= await db.BillingPlanVersions.AsNoTracking()
              .Where(item => item.PlanId == plan.Id)
              .OrderByDescending(item => item.VersionNumber)
              .FirstOrDefaultAsync(cancellationToken);

          return version is not null && BillingPlanMatchesVersion(plan, version)
              ? new BillingCatalogVersionRef(version.Id, version.VersionNumber)
              : null;
      }

      private async Task<BillingCatalogVersionRef?> ResolveAddOnVersionRefAsync(BillingAddOn addOn, CancellationToken cancellationToken)
      {
          BillingAddOnVersion? version = null;
          if (!string.IsNullOrWhiteSpace(addOn.ActiveVersionId))
          {
              version = await db.BillingAddOnVersions.AsNoTracking()
                  .FirstOrDefaultAsync(item => item.AddOnId == addOn.Id && item.Id == addOn.ActiveVersionId, cancellationToken);
          }

          if (version is null && !string.IsNullOrWhiteSpace(addOn.LatestVersionId))
          {
              version = await db.BillingAddOnVersions.AsNoTracking()
                  .FirstOrDefaultAsync(item => item.AddOnId == addOn.Id && item.Id == addOn.LatestVersionId, cancellationToken);
          }

          version ??= await db.BillingAddOnVersions.AsNoTracking()
              .Where(item => item.AddOnId == addOn.Id)
              .OrderByDescending(item => item.VersionNumber)
              .FirstOrDefaultAsync(cancellationToken);

          return version is not null && BillingAddOnMatchesVersion(addOn, version)
              ? new BillingCatalogVersionRef(version.Id, version.VersionNumber)
              : null;
      }

      private async Task<BillingCatalogVersionRef?> ResolveCouponVersionRefAsync(BillingCoupon coupon, CancellationToken cancellationToken)
      {
          BillingCouponVersion? version = null;
          if (!string.IsNullOrWhiteSpace(coupon.ActiveVersionId))
          {
              version = await db.BillingCouponVersions.AsNoTracking()
                  .FirstOrDefaultAsync(item => item.CouponId == coupon.Id && item.Id == coupon.ActiveVersionId, cancellationToken);
          }

          if (version is null && !string.IsNullOrWhiteSpace(coupon.LatestVersionId))
          {
              version = await db.BillingCouponVersions.AsNoTracking()
                  .FirstOrDefaultAsync(item => item.CouponId == coupon.Id && item.Id == coupon.LatestVersionId, cancellationToken);
          }

          version ??= await db.BillingCouponVersions.AsNoTracking()
              .Where(item => item.CouponId == coupon.Id)
              .OrderByDescending(item => item.VersionNumber)
              .FirstOrDefaultAsync(cancellationToken);

          return version is not null && BillingCouponMatchesVersion(coupon, version)
              ? new BillingCatalogVersionRef(version.Id, version.VersionNumber)
              : null;
      }

      private static bool BillingPlanMatchesVersion(BillingPlan plan, BillingPlanVersion version)
          // Name/Description are presentation copy mirrored from Subscriptions & Packages
          // without a catalog version, so they are not part of the commercial-terms gate.
          => string.Equals(plan.Code, version.Code, StringComparison.Ordinal)
             && plan.Price == version.Price
             && string.Equals(plan.Currency, version.Currency, StringComparison.Ordinal)
             && string.Equals(plan.Interval, version.Interval, StringComparison.Ordinal)
             && plan.DurationMonths == version.DurationMonths
             && plan.IsVisible == version.IsVisible
             && plan.IsRenewable == version.IsRenewable
             && plan.TrialDays == version.TrialDays
             && plan.DisplayOrder == version.DisplayOrder
             && plan.IncludedCredits == version.IncludedCredits
             && string.Equals(plan.IncludedSubtestsJson, version.IncludedSubtestsJson, StringComparison.Ordinal)
             && string.Equals(plan.EntitlementsJson, version.EntitlementsJson, StringComparison.Ordinal)
             && plan.Status == version.Status
             && plan.ArchivedAt == version.ArchivedAt;

      private static bool BillingAddOnMatchesVersion(BillingAddOn addOn, BillingAddOnVersion version)
          // Name/Description are mirrored presentation copy (see BillingPlanMatchesVersion).
          => string.Equals(addOn.Code, version.Code, StringComparison.Ordinal)
             && addOn.Price == version.Price
             && string.Equals(addOn.Currency, version.Currency, StringComparison.Ordinal)
             && string.Equals(addOn.Interval, version.Interval, StringComparison.Ordinal)
             && addOn.Status == version.Status
             && addOn.IsRecurring == version.IsRecurring
             && addOn.DurationDays == version.DurationDays
             && addOn.GrantCredits == version.GrantCredits
             && string.Equals(addOn.GrantEntitlementsJson, version.GrantEntitlementsJson, StringComparison.Ordinal)
             && string.Equals(addOn.CompatiblePlanCodesJson, version.CompatiblePlanCodesJson, StringComparison.Ordinal)
             && addOn.AppliesToAllPlans == version.AppliesToAllPlans
             && addOn.IsStackable == version.IsStackable
             && addOn.QuantityStep == version.QuantityStep
             && addOn.MaxQuantity == version.MaxQuantity
             && addOn.DisplayOrder == version.DisplayOrder;

      private static bool BillingCouponMatchesVersion(BillingCoupon coupon, BillingCouponVersion version)
          => string.Equals(coupon.Code, version.Code, StringComparison.Ordinal)
             && string.Equals(coupon.Name, version.Name, StringComparison.Ordinal)
             && string.Equals(coupon.Description, version.Description, StringComparison.Ordinal)
             && coupon.DiscountType == version.DiscountType
             && coupon.DiscountValue == version.DiscountValue
             && string.Equals(coupon.Currency, version.Currency, StringComparison.Ordinal)
             && coupon.Status == version.Status
             && coupon.StartsAt == version.StartsAt
             && coupon.EndsAt == version.EndsAt
             && coupon.UsageLimitTotal == version.UsageLimitTotal
             && coupon.UsageLimitPerUser == version.UsageLimitPerUser
             && coupon.MinimumSubtotal == version.MinimumSubtotal
             && string.Equals(coupon.ApplicablePlanCodesJson, version.ApplicablePlanCodesJson, StringComparison.Ordinal)
             && string.Equals(coupon.ApplicableAddOnCodesJson, version.ApplicableAddOnCodesJson, StringComparison.Ordinal)
             && coupon.IsStackable == version.IsStackable
             && string.Equals(coupon.Notes, version.Notes, StringComparison.Ordinal);

      private async Task<int> CountCouponRedemptionsAsync(BillingCoupon coupon, string? userId, CancellationToken cancellationToken)
      {
          var normalizedCouponCode = NormalizeBillingCode(coupon.Code);
          var query = db.BillingCouponRedemptions.Where(redemption =>
              redemption.Status != BillingRedemptionStatus.Voided
              && (redemption.CouponId == coupon.Id
                  || (redemption.CouponId == null && redemption.CouponCode.ToLower() == normalizedCouponCode)));

          if (!string.IsNullOrWhiteSpace(userId))
          {
              query = query.Where(redemption => redemption.UserId == userId);
          }

          return await query.CountAsync(cancellationToken);
      }

      private static Dictionary<string, string> DeserializeAddOnVersionIds(BillingQuote quote)
          => new(JsonSupport.Deserialize<Dictionary<string, string>>(quote.AddOnVersionIdsJson, new Dictionary<string, string>()), StringComparer.OrdinalIgnoreCase);

      private static string NormalizeBillingCode(string? value)
          => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static List<string> NormalizeCodes(IEnumerable<string>? codes)
        => (codes ?? Array.Empty<string>()).Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static BillingQuoteResponse DeserializeQuoteResponse(BillingQuote quote)
    {
        var snapshot = JsonSupport.Deserialize<Dictionary<string, object?>>(quote.SnapshotJson, new Dictionary<string, object?>());
        var items = snapshot.TryGetValue("items", out var itemsValue)
            ? JsonSupport.Deserialize<List<BillingQuoteLineItem>>(JsonSupport.Serialize(itemsValue), [])
            : [];
        var validation = snapshot.TryGetValue("validation", out var validationValue)
            ? JsonSupport.Deserialize<Dictionary<string, object?>>(JsonSupport.Serialize(validationValue), new Dictionary<string, object?>())
            : new Dictionary<string, object?>();

        return new BillingQuoteResponse(
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
            quote.ExpiresAt,
            snapshot.TryGetValue("summary", out var summaryValue) ? summaryValue?.ToString() ?? string.Empty : string.Empty,
            validation);
    }

    private sealed record BillingCatalogVersionRef(string Id, int VersionNumber);

    private sealed class BillingQuoteCatalogSnapshot
    {
        public int SchemaVersion { get; set; } = 2;
        public DateTimeOffset CapturedAt { get; set; }
        public BillingQuotePlanSnapshot? Plan { get; set; }
        public List<BillingQuoteAddOnSnapshot> AddOns { get; set; } = [];
        public BillingQuoteCouponSnapshot? Coupon { get; set; }
    }

    private sealed class BillingQuotePlanSnapshot
    {
        public string Code { get; set; } = string.Empty;
        public string? VersionId { get; set; }
        public int? VersionNumber { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string Currency { get; set; } = "AUD";
        public string Interval { get; set; } = "month";
        public int DurationMonths { get; set; }
        public int IncludedCredits { get; set; }
        public int BundledAiCredits { get; set; }
    }

    private sealed class BillingQuoteAddOnSnapshot
    {
        public string Code { get; set; } = string.Empty;
        public string? VersionId { get; set; }
        public int? VersionNumber { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string Currency { get; set; } = "AUD";
        public string Interval { get; set; } = "one_time";
        public bool IsRecurring { get; set; }
        public int DurationDays { get; set; }
        public int GrantCredits { get; set; }
        public string GrantEntitlementsJson { get; set; } = "{}";
    }

    private sealed class BillingQuoteCouponSnapshot
    {
        public string Code { get; set; } = string.Empty;
        public string? VersionId { get; set; }
        public int? VersionNumber { get; set; }
        public string Name { get; set; } = string.Empty;
        public string DiscountType { get; set; } = string.Empty;
        public decimal DiscountValue { get; set; }
        public decimal DiscountAmount { get; set; }
        public string Currency { get; set; } = "AUD";
    }

    private static BillingQuoteCatalogSnapshot BuildQuoteCatalogSnapshot(
        DateTimeOffset capturedAt,
        BillingPlan? plan,
        BillingCatalogVersionRef? planVersion,
        IEnumerable<BillingAddOn> addOns,
        IReadOnlyDictionary<string, BillingCatalogVersionRef> addOnVersions,
        BillingCoupon? coupon,
        BillingCatalogVersionRef? couponVersion,
        decimal discountAmount)
        => new()
        {
            SchemaVersion = 2,
            CapturedAt = capturedAt,
            Plan = plan is null
                ? null
                : new BillingQuotePlanSnapshot
                {
                    Code = plan.Code,
                    VersionId = planVersion?.Id,
                    VersionNumber = planVersion?.VersionNumber,
                    Name = plan.Name,
                    Price = plan.Price,
                    Currency = plan.Currency,
                    Interval = plan.Interval,
                    DurationMonths = plan.DurationMonths,
                    IncludedCredits = plan.IncludedCredits,
                    BundledAiCredits = plan.BundledAiCredits
                },
            AddOns = addOns.Select(addOn => new BillingQuoteAddOnSnapshot
            {
                Code = addOn.Code,
                VersionId = addOnVersions.TryGetValue(addOn.Code, out var addOnVersion) ? addOnVersion.Id : null,
                VersionNumber = addOnVersions.TryGetValue(addOn.Code, out addOnVersion) ? addOnVersion.VersionNumber : null,
                Name = addOn.Name,
                Price = addOn.Price,
                Currency = addOn.Currency,
                Interval = addOn.Interval,
                IsRecurring = addOn.IsRecurring,
                DurationDays = addOn.DurationDays,
                GrantCredits = addOn.GrantCredits,
                GrantEntitlementsJson = addOn.GrantEntitlementsJson
            }).ToList(),
            Coupon = coupon is null
                ? null
                : new BillingQuoteCouponSnapshot
                {
                    Code = coupon.Code,
                    VersionId = couponVersion?.Id,
                    VersionNumber = couponVersion?.VersionNumber,
                    Name = coupon.Name,
                    DiscountType = coupon.DiscountType.ToString(),
                    DiscountValue = coupon.DiscountValue,
                    DiscountAmount = discountAmount,
                    Currency = coupon.Currency
                }
        };

    private static BillingQuoteCatalogSnapshot? DeserializeQuoteCatalogSnapshot(BillingQuote quote)
    {
        var snapshot = JsonSupport.Deserialize<Dictionary<string, object?>>(quote.SnapshotJson, new Dictionary<string, object?>());
        if (!snapshot.TryGetValue("catalog", out var catalogValue) || catalogValue is null)
        {
            return null;
        }

        var catalog = JsonSupport.Deserialize<BillingQuoteCatalogSnapshot?>(JsonSupport.Serialize(catalogValue), null);
        return catalog is null || (catalog.Plan is null && catalog.AddOns.Count == 0 && catalog.Coupon is null)
            ? null
            : catalog;
    }

    private async Task<BillingQuoteResponse> BuildBillingQuoteAsync(
        string userId,
        BillingQuoteRequest request,
        CancellationToken cancellationToken,
        bool persistQuote)
    {
        var normalizedProductType = (request.ProductType ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedProductType is not ("review_credits" or "plan_purchase" or "plan_upgrade" or "plan_downgrade" or "addon_purchase"))
        {
            throw ApiException.Validation(
                "unsupported_checkout_product",
                $"Unsupported checkout product '{request.ProductType}'.",
                [new ApiFieldError("productType", "unsupported", "Only supported learner checkout products can be quoted.")]);
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

        var now = DateTimeOffset.UtcNow;
        var parentSubscriptionId = request.ParentSubscriptionId?.Trim();
        BillingAddOn? quotedAddOn = null;
        if (normalizedProductType == "addon_purchase")
        {
            if (string.IsNullOrWhiteSpace(request.PriceId))
            {
                throw ApiException.Validation(
                    "target_addon_required",
                    "A target add-on id is required for add-on purchases.",
                    [new ApiFieldError("priceId", "required", "Choose the add-on you want to purchase.")]);
            }

            quotedAddOn = await FindPurchasableBillingAddOnAsync(request.PriceId, cancellationToken)
                ?? throw ApiException.Validation(
                    "unknown_addon",
                    $"Unknown billing add-on '{request.PriceId}'.",
                    [new ApiFieldError("priceId", "unknown", "Choose a published add-on.")]);

            if (quotedAddOn.RequiresEligibleParent)
            {
                var eligibility = addonEligibilityService ?? new AddonEligibilityService(db);
                var eligibilityResult = await eligibility.ResolveAsync(userId, quotedAddOn.Code, cancellationToken);
                if (!eligibilityResult.Eligible)
                {
                    throw ApiException.Validation(
                        eligibilityResult.Reason ?? "addon_ineligible",
                        "The selected add-on requires an eligible parent enrolment.",
                        [new ApiFieldError("priceId", "ineligible", "Choose an add-on that works with one of your active enrolments.")]);
                }

                if (string.IsNullOrWhiteSpace(parentSubscriptionId))
                {
                    if (eligibilityResult.EligibleParents.Count == 1)
                    {
                        parentSubscriptionId = eligibilityResult.EligibleParents[0].SubscriptionId;
                    }
                    else
                    {
                        throw ApiException.Validation(
                            "parent_subscription_required",
                            "Choose which eligible enrolment this add-on should apply to.",
                            [new ApiFieldError("parentSubscriptionId", "required", "Choose an eligible parent enrolment.")]);
                    }
                }
                else if (!eligibilityResult.EligibleParents.Any(parent =>
                    string.Equals(parent.SubscriptionId, parentSubscriptionId, StringComparison.OrdinalIgnoreCase)))
                {
                    throw ApiException.Validation(
                        "parent_subscription_not_eligible",
                        "The selected parent enrolment is not eligible for this add-on.",
                        [new ApiFieldError("parentSubscriptionId", "ineligible", "Choose an eligible parent enrolment.")]);
                }
            }
        }

        var subscriptionQuery = db.Subscriptions.Where(x => x.UserId == userId);
        Subscription? subscription;
        if (!string.IsNullOrWhiteSpace(parentSubscriptionId))
        {
            subscription = await subscriptionQuery.FirstOrDefaultAsync(x => x.Id == parentSubscriptionId, cancellationToken);
        }
        else if (quotedAddOn is not null && !quotedAddOn.RequiresEligibleParent)
        {
            // Quick Check / Exam Prep Pro / OET Mastery / skill packs / mocks:
            // hang off a live course if one exists, otherwise the hidden
            // standalone-addon container. Never scaffold the cheapest Full Course.
            subscription = await subscriptionQuery
                .Where(s => s.PlanId != Subscription.StandaloneAddonPlanId
                    && (s.Status == SubscriptionStatus.Active
                        || s.Status == SubscriptionStatus.Trial
                        || s.Status == SubscriptionStatus.FreezeRequested))
                .OrderByDescending(s => s.ChangedAt)
                .FirstOrDefaultAsync(cancellationToken);
            subscription ??= await StandaloneAddonSubscriptions.EnsureAsync(
                db, userId, now, cancellationToken, SubscriptionStatus.Draft);
        }
        else
        {
            subscription = await subscriptionQuery.FirstOrDefaultAsync(cancellationToken);
        }

        if (subscription is null && normalizedProductType == "addon_purchase" && !string.IsNullOrWhiteSpace(parentSubscriptionId))
        {
            throw ApiException.Validation(
                "parent_subscription_not_found",
                "The selected parent enrolment could not be found.",
                [new ApiFieldError("parentSubscriptionId", "unknown", "Choose an eligible parent enrolment.")]);
        }
        if (subscription is null)
        {
            var defaultPlan = await db.BillingPlans.AsNoTracking()
                .Where(plan => plan.Status == BillingPlanStatus.Active)
                .OrderBy(plan => plan.DisplayOrder)
                .ThenBy(plan => plan.Price)
                .FirstOrDefaultAsync(cancellationToken)
                ?? await db.BillingPlans.AsNoTracking()
                    .OrderBy(plan => plan.DisplayOrder)
                    .ThenBy(plan => plan.Price)
                    .FirstOrDefaultAsync(cancellationToken)
                ?? throw ApiException.NotFound(
                    "billing_plan_not_found",
                    "No billing plan is available for checkout.");

            // A brand-new learner with no prior subscription needs a parent row so
            // this quote (and any add-on items) have something to hang off. It is a
            // pre-payment DRAFT only — it MUST NOT become Pending, must not confer
            // entitlements, and must remain hidden from learner/admin views until a
            // successful payment completes. Before success = Draft only; after success
            // ApplyCheckoutCompletionAsync moves Draft → Pending (admin approval
            // required) or Draft → Active (automatic access). Pending is never created
            // before a successful payment.
            subscription = new Subscription
            {
                Id = TruncateIdentifier($"sub-{Guid.NewGuid():N}"),
                UserId = userId,
                PlanId = defaultPlan.Code,
                Status = SubscriptionStatus.Draft,
                NextRenewalAt = now.AddMonths(Math.Max(defaultPlan.DurationMonths, 1)),
                StartedAt = now,
                ChangedAt = now,
                PriceAmount = defaultPlan.Price,
                Currency = defaultPlan.Currency,
                Interval = defaultPlan.Interval
            };

            db.Subscriptions.Add(subscription);
            await db.SaveChangesAsync(cancellationToken);
        }
        var currentPlan = await FindBillingPlanAsync(subscription.PlanId, cancellationToken);
        var addOnCodes = NormalizeCodes(request.AddOnCodes);
        var items = new List<BillingQuoteLineItem>();
        BillingPlan? snapshotPlan = null;
        var snapshotAddOns = new List<BillingAddOn>();
        decimal subtotal;
        string? planCode = null;
        string summary;

        if (normalizedProductType is "plan_purchase" or "plan_upgrade" or "plan_downgrade")
        {
            if (string.IsNullOrWhiteSpace(request.PriceId))
            {
                throw ApiException.Validation(
                    "target_plan_required",
                    "A target plan id is required for plan changes.",
                    [new ApiFieldError("priceId", "required", "Choose the plan you want to switch to.")]);
            }

            var targetPlan = await FindPurchasableBillingPlanAsync(request.PriceId, cancellationToken)
                ?? throw ApiException.Validation(
                    "unknown_plan",
                    $"Unknown billing plan '{request.PriceId}'.",
                    [new ApiFieldError("priceId", "unknown", "Choose a published billing plan.")]);

            AdminService.EnsurePlanCanStartNewSubscription(targetPlan);
            await EnsurePlanMatchesLearnerProfessionAsync(
                await EnsureUserAsync(userId, cancellationToken),
                targetPlan,
                cancellationToken);

            // A pre-payment Draft scaffold may be REUSED for this quote unless one of
            // its earlier quotes still matters. Previously any quote at all — even a
            // long-dead one from an abandoned checkout — forced a brand-new Draft row,
            // so three failed attempts left three Draft subscriptions on the learner
            // (owner P0 report, 15 Sep 2026).
            //
            // Deliberately still minting a new row when a prior quote is Completed (the
            // Draft is being promoted) or is STILL PAYABLE (Created/Applied and not yet
            // expired). A hosted gateway checkout URL outlives our page — see
            // ResolveReusableCheckoutUrl — so mutating a Draft that a live checkout can
            // still settle against would be a far worse defect than a spare row.
            var draftIsUncommitted = subscription.Status == SubscriptionStatus.Draft
                && !await db.BillingQuotes.AsNoTracking()
                    .AnyAsync(
                        quote => quote.SubscriptionId == subscription.Id
                            && (quote.Status == BillingQuoteStatus.Completed
                                || ((quote.Status == BillingQuoteStatus.Created
                                        || quote.Status == BillingQuoteStatus.Applied)
                                    && quote.ExpiresAt > now)),
                        cancellationToken);
            if (!draftIsUncommitted)
            {
                subscription = new Subscription
                {
                    Id = TruncateIdentifier($"sub-{Guid.NewGuid():N}"),
                    UserId = userId,
                    Status = SubscriptionStatus.Draft,
                    StartedAt = now,
                    ChangedAt = now,
                };
                db.Subscriptions.Add(subscription);
            }

            subscription.PlanId = targetPlan.Code;
            subscription.PriceAmount = targetPlan.Price;
            subscription.Currency = targetPlan.Currency;
            subscription.Interval = targetPlan.Interval;
            subscription.NextRenewalAt = now.AddMonths(Math.Max(targetPlan.DurationMonths, 1));
            subscription.AccessDurationDays = Math.Max(1, targetPlan.AccessDurationDays);

            if (targetPlan.BundledTutorBook && await UserOwnsTutorBookAsync(userId, cancellationToken))
            {
                throw ApiException.Validation(
                    "tutor_book_already_owned",
                    "Your account already has Tutor Book access.",
                    [new ApiFieldError("priceId", "already_owned", "Choose the course-only option or contact support.")]);
            }

            planCode = targetPlan.Code;
            snapshotPlan = targetPlan;
            if (normalizedProductType == "plan_purchase")
            {
                subtotal = Math.Round(targetPlan.Price * Math.Max(1, request.Quantity), 2, MidpointRounding.AwayFromZero);
                summary = $"{request.Quantity} x {targetPlan.Name}.";
            }
            else
            {
                var referencePlan = currentPlan ?? targetPlan;
                var delta = targetPlan.Price - referencePlan.Price;
                subtotal = Math.Round(Math.Abs(delta) / 2m, 2, MidpointRounding.AwayFromZero);
                summary = normalizedProductType == "plan_upgrade"
                    ? $"Switching to {targetPlan.Name} increases your billing amount by {Math.Abs(delta):0.00} {targetPlan.Currency}."
                    : $"Switching to {targetPlan.Name} lowers your billing amount by {Math.Abs(delta):0.00} {targetPlan.Currency}.";
            }
            items.Add(new BillingQuoteLineItem(
                "plan",
                targetPlan.Code,
                targetPlan.Name,
                subtotal,
                targetPlan.Currency,
                Math.Max(1, request.Quantity),
                targetPlan.Description));

            // Cart (multi-item) checkout: a plan bought together with add-ons/AI packages.
            // Compose the AddOnCodes as one-each add-on line items in the same quote so the
            // whole cart is one payment, fulfilled by the ApplyCheckoutCompletionAsync loop
            // (plan block + add-on items). Only for a fresh plan purchase — upgrades and
            // downgrades price on a plan-price delta and never carry cart add-ons.
            if (normalizedProductType == "plan_purchase")
            {
                subtotal += await AppendCartAddOnItemsAsync(request.AddOnCodes, planCode, currentPlan, items, snapshotAddOns, cancellationToken);
                addOnCodes = NormalizeCodes(items.Where(line => line.Kind == "addon").Select(line => line.Code).ToList());
                if (items.Count(line => line.Kind == "addon") > 0)
                {
                    summary = $"{items.Count} items in your cart.";
                }
            }
        }
        else if (normalizedProductType == "addon_purchase")
        {
            if (string.IsNullOrWhiteSpace(request.PriceId))
            {
                throw ApiException.Validation(
                    "target_addon_required",
                    "A target add-on id is required for add-on purchases.",
                    [new ApiFieldError("priceId", "required", "Choose the add-on you want to purchase.")]);
            }

            var addOn = await FindPurchasableBillingAddOnAsync(request.PriceId, cancellationToken)
                ?? throw ApiException.Validation(
                    "unknown_addon",
                    $"Unknown billing add-on '{request.PriceId}'.",
                    [new ApiFieldError("priceId", "unknown", "Choose a published add-on.")]);

            if (!IsAddOnCompatibleWithPlan(addOn, currentPlan))
            {
                throw ApiException.Validation(
                    "addon_incompatible",
                    "The selected add-on is not available for your current plan.",
                    [new ApiFieldError("priceId", "incompatible", "Choose an add-on that works with your current plan.")]);
            }

            if (addOn.MaxQuantity is not null && request.Quantity > addOn.MaxQuantity.Value)
            {
                throw ApiException.Validation(
                    "addon_quantity_exceeded",
                    "The requested add-on quantity exceeds the allowed maximum.",
                    [new ApiFieldError("quantity", "max_exceeded", "Reduce the quantity and try again.")]);
            }

            snapshotAddOns.Add(addOn);
            planCode = currentPlan?.Code;
            var primaryLineAmount = Math.Round(addOn.Price * request.Quantity, 2, MidpointRounding.AwayFromZero);
            subtotal = primaryLineAmount;
            items.Add(new BillingQuoteLineItem(
                "addon",
                addOn.Code,
                addOn.Name,
                primaryLineAmount,
                addOn.Currency,
                request.Quantity,
                addOn.Description));

            // Cart (multi-item) checkout: any AddOnCodes beyond the primary priceId are
            // composed as additional one-each add-on line items in the SAME quote, so the
            // whole cart is charged in one payment and fulfilled by the existing
            // ApplyCheckoutCompletionAsync loop over the quote items. An empty AddOnCodes
            // list keeps the single add-on behaviour byte-for-byte.
            subtotal += await AppendCartAddOnItemsAsync(request.AddOnCodes, addOn.Code, currentPlan, items, snapshotAddOns, cancellationToken);
            addOnCodes = NormalizeCodes(items.Select(line => line.Code).ToList());
            summary = items.Count == 1
                ? $"{request.Quantity} x {addOn.Name}."
                : $"{items.Count} items in your cart.";
        }
        else
        {
            BillingAddOn? reviewPack = null;
            if (!string.IsNullOrWhiteSpace(request.PriceId))
            {
                reviewPack = await FindPurchasableBillingAddOnAsync(request.PriceId, cancellationToken)
                    ?? throw ApiException.Validation(
                        "unknown_addon",
                        $"Unknown billing add-on '{request.PriceId}'.",
                        [new ApiFieldError("priceId", "unknown", "Choose a published review credit pack.")]);

                if (!IsAddOnCompatibleWithPlan(reviewPack, currentPlan))
                {
                    throw ApiException.Validation(
                        "addon_incompatible",
                        "The selected review credit pack is not available for your current plan.",
                        [new ApiFieldError("priceId", "incompatible", "Choose a review credit pack that works with your current plan.")]);
                }
            }

            reviewPack ??= (await db.BillingAddOns.AsNoTracking()
                .Where(addOn => addOn.Status == BillingAddOnStatus.Active && addOn.GrantCredits == request.Quantity)
                .OrderBy(addOn => addOn.DisplayOrder)
                .ToListAsync(cancellationToken))
                .FirstOrDefault(addOn => IsAddOnCompatibleWithPlan(addOn, currentPlan));

            reviewPack ??= (await db.BillingAddOns.AsNoTracking()
                .Where(addOn => addOn.Status == BillingAddOnStatus.Active && addOn.GrantCredits > 0)
                .OrderBy(addOn => addOn.DisplayOrder)
                .ThenBy(addOn => addOn.Price)
                .ToListAsync(cancellationToken))
                .FirstOrDefault(addOn => IsAddOnCompatibleWithPlan(addOn, currentPlan))
                ?? throw ApiException.Validation(
                    "review_pack_unavailable",
                    "No review credit pack is available for the requested quantity.",
                    [new ApiFieldError("quantity", "unsupported", "Choose one of the available review credit packs.")]);

            addOnCodes = NormalizeCodes([reviewPack.Code]);
            snapshotAddOns.Add(reviewPack);
            planCode = currentPlan?.Code;
            subtotal = Math.Round(reviewPack.Price, 2, MidpointRounding.AwayFromZero);
            summary = $"Review credit pack: {reviewPack.GrantCredits} credits.";
            items.Add(new BillingQuoteLineItem(
                "addon",
                reviewPack.Code,
                reviewPack.Name,
                subtotal,
                reviewPack.Currency,
                1,
                reviewPack.Description));
        }

        // Apply any running pricing experiment for this product.
        var expTargetType = normalizedProductType is "plan_upgrade" or "plan_downgrade" ? "plan" : "addon";
        var expTargetId = normalizedProductType is "plan_upgrade" or "plan_downgrade"
            ? (planCode ?? string.Empty)
            : (items.Count > 0 ? items[0].Code : string.Empty);
        var (experimentAssignmentId, priceMultiplier) = string.IsNullOrEmpty(expTargetId)
            ? (null, 1m)
            : await TryApplyPricingExperimentAsync(userId, expTargetType, expTargetId, cancellationToken);
        if (priceMultiplier != 1m && items.Count > 0)
        {
            subtotal = Math.Round(subtotal * priceMultiplier, 2, MidpointRounding.AwayFromZero);
            // Reflect adjusted price in the line item so the quote response is accurate.
            var item = items[0];
            items[0] = item with { Amount = subtotal };
        }

        BillingCoupon? coupon = null;
        decimal discount = 0m;
        var validation = new Dictionary<string, object?>
        {
            ["productType"] = normalizedProductType,
            ["subtotal"] = subtotal,
            ["planCode"] = planCode,
            ["addOnCodes"] = addOnCodes
        };

        if (!string.IsNullOrWhiteSpace(request.CouponCode))
        {
            coupon = await FindBillingCouponAsync(request.CouponCode, cancellationToken);
            if (coupon is null)
            {
                throw ApiException.Validation(
                    "coupon_not_found",
                    "The coupon code could not be found.",
                    [new ApiFieldError("couponCode", "unknown", "Enter a valid coupon code.")]);
            }

            if (coupon.Status != BillingCouponStatus.Active)
            {
                throw ApiException.Validation(
                    "coupon_inactive",
                    "The coupon is not active.",
                    [new ApiFieldError("couponCode", "inactive", "Use a currently active coupon.")]);
            }

            if (coupon.StartsAt is not null && coupon.StartsAt > now)
            {
                throw ApiException.Validation(
                    "coupon_not_started",
                    "The coupon is not yet active.",
                    [new ApiFieldError("couponCode", "not_started", "Try again once the coupon start date has passed.")]);
            }

            if (coupon.EndsAt is not null && coupon.EndsAt < now)
            {
                throw ApiException.Validation(
                    "coupon_expired",
                    "The coupon has expired.",
                    [new ApiFieldError("couponCode", "expired", "Use a valid coupon code.")]);
            }

            if (coupon.MinimumSubtotal is not null && subtotal < coupon.MinimumSubtotal.Value)
            {
                throw ApiException.Validation(
                    "coupon_minimum_not_met",
                    "The coupon minimum purchase amount was not met.",
                    [new ApiFieldError("couponCode", "minimum_not_met", "Add more to your order or use another coupon.")]);
            }

            var planAllowList = JsonSupport.Deserialize<List<string>>(coupon.ApplicablePlanCodesJson, []);
            var addOnAllowList = JsonSupport.Deserialize<List<string>>(coupon.ApplicableAddOnCodesJson, []);
            if (planAllowList.Count > 0 && (planCode is null || !planAllowList.Any(code => string.Equals(code, planCode, StringComparison.OrdinalIgnoreCase))))
            {
                throw ApiException.Validation(
                    "coupon_not_applicable",
                    "The coupon does not apply to the selected plan.",
                    [new ApiFieldError("couponCode", "not_applicable", "Choose a coupon that matches the selected plan.")]);
            }

            if (addOnAllowList.Count > 0 && !addOnCodes.Any(code => addOnAllowList.Any(allowed => string.Equals(allowed, code, StringComparison.OrdinalIgnoreCase))))
            {
                throw ApiException.Validation(
                    "coupon_not_applicable",
                    "The coupon does not apply to the selected add-on.",
                    [new ApiFieldError("couponCode", "not_applicable", "Choose a coupon that matches the selected add-on.")]);
            }

            await ReleaseExpiredCouponReservationsAsync(coupon, now, cancellationToken);

            var couponRedemptionCount = await CountCouponRedemptionsAsync(coupon, userId: null, cancellationToken);
            if (coupon.UsageLimitTotal is not null && couponRedemptionCount >= coupon.UsageLimitTotal.Value)
            {
                throw ApiException.Validation(
                    "coupon_exhausted",
                    "The coupon usage limit has been reached.",
                    [new ApiFieldError("couponCode", "usage_limit", "Choose a different coupon.")]);
            }

            var perUserRedemptionCount = await CountCouponRedemptionsAsync(coupon, userId, cancellationToken);
            if (coupon.UsageLimitPerUser is not null && perUserRedemptionCount >= coupon.UsageLimitPerUser.Value)
            {
                throw ApiException.Validation(
                    "coupon_user_limit",
                    "You have already used this coupon.",
                    [new ApiFieldError("couponCode", "user_limit", "This coupon can only be used once per user.")]);
            }

            // trial_extension_days and free_months shift subscription dates post-checkout;
            // they carry no price discount at quote time.
            if (coupon.CouponVariant is "trial_extension_days" or "free_months")
            {
                discount = 0m;
            }
            else
            {
                discount = coupon.DiscountType == BillingDiscountType.Percentage
                    ? Math.Round(subtotal * Math.Min(coupon.DiscountValue, 100m) / 100m, 2, MidpointRounding.AwayFromZero)
                    : Math.Round(Math.Min(coupon.DiscountValue, subtotal), 2, MidpointRounding.AwayFromZero);

                if (discount > subtotal)
                {
                    discount = subtotal;
                }
            }

            validation["couponCode"] = coupon.Code;
            validation["couponStatus"] = coupon.Status.ToString().ToLowerInvariant();
            validation["discountType"] = coupon.DiscountType.ToString().ToLowerInvariant();
            validation["discountValue"] = coupon.DiscountValue;
            validation["discount"] = discount;
        }

        var total = Math.Max(0m, Math.Round(subtotal - discount, 2, MidpointRounding.AwayFromZero));
        var planVersion = snapshotPlan is null ? null : await ResolvePlanVersionRefAsync(snapshotPlan, cancellationToken);

        // How the plan being bought is handed over, so checkout can tell a WhatsApp/manual
        // buyer their order sits Pending Manual Fulfilment rather than implying access is
        // live the moment they pay (spec 2026-07-15 §2/§6.6). Read from the version locked
        // to this quote so a mid-flight admin edit cannot change an in-flight order. An
        // The Tutor Book add-on is also manual material: payment records the order
        // against its eligible parent course but never unlocks a platform module.
        var hasManualTutorBookAddon = snapshotAddOns.Any(addOn =>
            string.Equals(addOn.Code, "tutor-book-addon", StringComparison.OrdinalIgnoreCase));
        validation["deliveryMethod"] = snapshotPlan is null
            ? hasManualTutorBookAddon ? DeliveryMethods.ManualMaterial : DeliveryMethods.AutomaticWeb
            : await ResolvePlanDeliveryMethodAsync(planVersion?.Id, snapshotPlan.Code, cancellationToken);
        validation["manualDeliveryRequired"] = hasManualTutorBookAddon
            || string.Equals(validation["deliveryMethod"]?.ToString(), DeliveryMethods.ManualMaterial, StringComparison.OrdinalIgnoreCase);
        if ((bool)validation["manualDeliveryRequired"])
        {
            // Support number is runtime-configurable (Admin > Settings > Support).
            const string quoteFallbackNumber = "447961725989";
            var configuredSupportNumber = runtimeSettings is null
                ? null
                : (await runtimeSettings.GetAsync(cancellationToken)).Support.WhatsAppNumber;
            var supportDigits = string.Concat((configuredSupportNumber ?? quoteFallbackNumber).Where(char.IsDigit));
            if (supportDigits.Length == 0) supportDigits = quoteFallbackNumber;
            validation["whatsAppUrl"] = $"https://wa.me/{supportDigits}";
        }

        var addOnVersions = new Dictionary<string, BillingCatalogVersionRef>(StringComparer.OrdinalIgnoreCase);
        foreach (var snapshotAddOn in snapshotAddOns)
        {
            var addOnVersion = await ResolveAddOnVersionRefAsync(snapshotAddOn, cancellationToken);
            if (addOnVersion is not null)
            {
                addOnVersions[snapshotAddOn.Code] = addOnVersion;
            }
        }

        var couponVersion = coupon is null ? null : await ResolveCouponVersionRefAsync(coupon, cancellationToken);
        var addOnVersionIdsJson = JsonSupport.Serialize(addOnVersions.ToDictionary(item => item.Key, item => item.Value.Id, StringComparer.OrdinalIgnoreCase));
        var quoteIdValue = $"quote-{Guid.NewGuid():N}";
        var quoteId = quoteIdValue[..Math.Min(64, quoteIdValue.Length)];
        var quote = new BillingQuote
        {
            Id = quoteId,
            UserId = userId,
            SubscriptionId = subscription.Id,
            PlanCode = planCode,
            PlanVersionId = planVersion?.Id,
            AddOnCodesJson = JsonSupport.Serialize(addOnCodes),
            AddOnVersionIdsJson = addOnVersionIdsJson,
            CouponCode = coupon?.Code,
            CouponVersionId = couponVersion?.Id,
            Currency = items.FirstOrDefault()?.Currency ?? subscription.Currency,
            SubtotalAmount = subtotal,
            DiscountAmount = discount,
            TotalAmount = total,
            Status = BillingQuoteStatus.Created,
            CreatedAt = now,
            ExpiresAt = now.Add(BillingQuoteDefaultLifetime),
            ExperimentAssignmentId = experimentAssignmentId,
            SnapshotJson = JsonSupport.Serialize(new
            {
                items,
                catalog = BuildQuoteCatalogSnapshot(now, snapshotPlan, planVersion, snapshotAddOns, addOnVersions, coupon, couponVersion, discount),
                validation,
                summary,
                subtotal,
                discount,
                total
            })
        };

        if (persistQuote)
        {
            IDbContextTransaction? couponReservationTransaction = null;
            try
            {
                if (coupon is not null)
                {
                    couponReservationTransaction = await BeginTransactionIfNeededAsync(cancellationToken);
                    await LockBillingCouponForReservationAsync(coupon, now, cancellationToken);
                    await ReleaseExpiredCouponReservationsAsync(coupon, now, cancellationToken);

                    var lockedPerUserRedemptionCount = await CountCouponRedemptionsAsync(coupon, userId, cancellationToken);
                    if (coupon.UsageLimitPerUser is not null && lockedPerUserRedemptionCount >= coupon.UsageLimitPerUser.Value)
                    {
                        throw ApiException.Validation(
                            "coupon_user_limit",
                            "You have already used this coupon.",
                            [new ApiFieldError("couponCode", "user_limit", "This coupon can only be used once per user.")]);
                    }

                    var couponReservation = await BillingCouponRedemptionAtomic.TryReserveAsync(db, coupon.Id, now, cancellationToken);
                    if (!couponReservation.Reserved)
                    {
                        throw ApiException.Validation(
                            couponReservation.RejectionCode ?? "coupon_exhausted",
                            "The coupon could not be reserved.",
                            [new ApiFieldError("couponCode", "usage_limit", "Choose a different coupon.")]);
                    }
                }

                db.BillingQuotes.Add(quote);
                db.BillingEvents.Add(new BillingEvent
                {
                    Id = $"bill-evt-{Guid.NewGuid():N}",
                    UserId = userId,
                    SubscriptionId = subscription.Id,
                    QuoteId = quote.Id,
                    EventType = "billing_quote_created",
                    EntityType = "BillingQuote",
                    EntityId = quote.Id,
                    PayloadJson = JsonSupport.Serialize(new { planCode, addOnCodes, couponCode = coupon?.Code, subtotal, discount, total }),
                    OccurredAt = now
                });

                if (coupon is not null)
                {
                    var redemptionIdValue = $"redemption-{Guid.NewGuid():N}";
                    db.BillingCouponRedemptions.Add(new BillingCouponRedemption
                    {
                        Id = redemptionIdValue[..Math.Min(64, redemptionIdValue.Length)],
                        CouponCode = coupon.Code,
                        CouponId = coupon.Id,
                        CouponVersionId = couponVersion?.Id,
                        UserId = userId,
                        QuoteId = quote.Id,
                        DiscountAmount = discount,
                        Currency = quote.Currency,
                        Status = BillingRedemptionStatus.Reserved,
                        RedeemedAt = now
                    });

                }

                await db.SaveChangesAsync(cancellationToken);
                await CommitIfOwnedAsync(couponReservationTransaction, cancellationToken);
            }
            finally
            {
                if (couponReservationTransaction is not null)
                {
                    await couponReservationTransaction.DisposeAsync();
                }
            }
        }

        return new BillingQuoteResponse(
            quote.Id,
            quote.Status.ToString().ToLowerInvariant(),
            quote.Currency,
            quote.SubtotalAmount,
            quote.DiscountAmount,
            quote.TotalAmount,
            quote.PlanCode,
            quote.CouponCode,
            addOnCodes,
            items,
            quote.ExpiresAt,
            summary,
            validation);
    }

    /// <summary>
    /// Composes the "extra" cart add-ons (every code in <paramref name="cartAddOnCodes"/>
    /// except the primary <paramref name="primaryCode"/> and anything already staged in
    /// <paramref name="snapshotAddOns"/>) into the shared quote as one-each add-on line
    /// items, returning the summed price of the appended lines so the caller can fold it
    /// into the quote subtotal. Standalone-only: an add-on that requires an eligible parent
    /// is rejected here (must be purchased on its own) rather than silently attached to the
    /// wrong enrolment. An empty/absent list is a no-op, preserving single-item behaviour.
    /// </summary>
    private async Task<decimal> AppendCartAddOnItemsAsync(
        List<string>? cartAddOnCodes,
        string? primaryCode,
        BillingPlan? currentPlan,
        List<BillingQuoteLineItem> items,
        List<BillingAddOn> snapshotAddOns,
        CancellationToken cancellationToken)
    {
        var appendedTotal = 0m;
        foreach (var extraCode in NormalizeCodes(cartAddOnCodes))
        {
            if (!string.IsNullOrWhiteSpace(primaryCode)
                && string.Equals(extraCode, primaryCode, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (snapshotAddOns.Any(existing => string.Equals(existing.Code, extraCode, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var extraAddOn = await FindPurchasableBillingAddOnAsync(extraCode, cancellationToken)
                ?? throw ApiException.Validation(
                    "unknown_addon",
                    $"Unknown billing add-on '{extraCode}'.",
                    [new ApiFieldError("addOnCodes", "unknown", "Remove the unavailable item from your cart.")]);

            if (!IsAddOnCompatibleWithPlan(extraAddOn, currentPlan))
            {
                throw ApiException.Validation(
                    "addon_incompatible",
                    $"'{extraAddOn.Name}' is not available for your current plan.",
                    [new ApiFieldError("addOnCodes", "incompatible", "Remove the incompatible item from your cart.")]);
            }

            if (extraAddOn.RequiresEligibleParent)
            {
                throw ApiException.Validation(
                    "addon_requires_parent",
                    $"'{extraAddOn.Name}' must be purchased on its own.",
                    [new ApiFieldError("addOnCodes", "requires_parent", "Check this item out separately.")]);
            }

            var lineAmount = Math.Round(extraAddOn.Price, 2, MidpointRounding.AwayFromZero);
            appendedTotal += lineAmount;
            snapshotAddOns.Add(extraAddOn);
            items.Add(new BillingQuoteLineItem(
                "addon",
                extraAddOn.Code,
                extraAddOn.Name,
                lineAmount,
                extraAddOn.Currency,
                1,
                extraAddOn.Description));
        }

        return appendedTotal;
    }

    private async Task ReleaseExpiredCouponReservationsAsync(BillingCoupon coupon, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var normalizedCouponCode = NormalizeBillingCode(coupon.Code);
        if (string.IsNullOrWhiteSpace(normalizedCouponCode))
        {
            return;
        }

        var expiredReservations = await db.BillingCouponRedemptions
            .Where(redemption => (redemption.CouponId == coupon.Id || (redemption.CouponId == null && redemption.CouponCode.ToLower() == normalizedCouponCode))
                && redemption.Status == BillingRedemptionStatus.Reserved
                && redemption.CheckoutSessionId == null)
            .Join(db.BillingQuotes,
                redemption => redemption.QuoteId,
                quote => quote.Id,
                (redemption, quote) => new { Redemption = redemption, Quote = quote })
            .Where(row => row.Quote.ExpiresAt < now
                && row.Quote.CheckoutSessionId == null
                && (row.Quote.Status == BillingQuoteStatus.Created || row.Quote.Status == BillingQuoteStatus.Expired))
            .ToListAsync(cancellationToken);

        if (expiredReservations.Count == 0)
        {
            return;
        }

        foreach (var row in expiredReservations)
        {
            row.Redemption.Status = BillingRedemptionStatus.Voided;
            row.Quote.Status = BillingQuoteStatus.Expired;
        }

        var releasedCouponKeys = GetCouponRedemptionCountKeys(expiredReservations.Select(row => row.Redemption));
        await db.SaveChangesAsync(cancellationToken);
        await RefreshCouponRedemptionCountsAsync(releasedCouponKeys, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<(string? CouponId, string CouponCode)>> ReleasePreCheckoutCouponReservationsForQuoteAsync(BillingQuote quote, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (quote.ExpiresAt >= now
            || quote.Status is not (BillingQuoteStatus.Created or BillingQuoteStatus.Expired)
            || !string.IsNullOrWhiteSpace(quote.CheckoutSessionId))
        {
            return [];
        }

        quote.Status = BillingQuoteStatus.Expired;

        var redemptions = await db.BillingCouponRedemptions
            .Where(redemption => redemption.QuoteId == quote.Id
                && redemption.Status == BillingRedemptionStatus.Reserved
                && redemption.CheckoutSessionId == null)
            .ToListAsync(cancellationToken);

        if (redemptions.Count == 0)
        {
            return [];
        }

        var releasedCouponKeys = GetCouponRedemptionCountKeys(redemptions);
        foreach (var redemption in redemptions)
        {
            redemption.Status = BillingRedemptionStatus.Voided;
        }

        return releasedCouponKeys;
    }

    private static List<(string? CouponId, string CouponCode)> GetCouponRedemptionCountKeys(IEnumerable<BillingCouponRedemption> releasedRedemptions)
        => releasedRedemptions
            .Select(redemption => (redemption.CouponId, CouponCode: NormalizeBillingCode(redemption.CouponCode)))
            .Where(key => !string.IsNullOrWhiteSpace(key.CouponId) || !string.IsNullOrWhiteSpace(key.CouponCode))
            .Distinct()
            .ToList();

    private async Task LockBillingCouponForReservationAsync(BillingCoupon coupon, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (db.Database.IsInMemory())
        {
            return;
        }

        var lockedCount = await db.BillingCoupons
            .Where(item => item.Id == coupon.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.UpdatedAt, now), cancellationToken);

        if (lockedCount == 0)
        {
            throw ApiException.Validation(
                "coupon_not_found",
                "The coupon code could not be found.",
                [new ApiFieldError("couponCode", "unknown", "Enter a valid coupon code.")]);
        }
    }

    private async Task RefreshCouponRedemptionCountsAsync(IReadOnlyCollection<(string? CouponId, string CouponCode)> releasedCouponKeys, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (releasedCouponKeys.Count == 0)
        {
            return;
        }

        var couponIds = releasedCouponKeys
            .Where(key => !string.IsNullOrWhiteSpace(key.CouponId))
            .Select(key => key.CouponId!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var legacyCouponCodes = releasedCouponKeys
            .Where(key => string.IsNullOrWhiteSpace(key.CouponId))
            .Select(key => key.CouponCode)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (couponIds.Count == 0 && legacyCouponCodes.Count == 0)
        {
            return;
        }

        var coupons = await db.BillingCoupons
            .Where(coupon => couponIds.Contains(coupon.Id) || legacyCouponCodes.Contains(coupon.Code.ToLower()))
            .ToListAsync(cancellationToken);

        foreach (var coupon in coupons)
        {
            coupon.RedemptionCount = await CountCouponRedemptionsAsync(coupon, userId: null, cancellationToken);
            coupon.UpdatedAt = now;
        }
    }
}
