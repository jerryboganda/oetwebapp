using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

/// <summary>Inbound DTO for a single wallet top-up tier.</summary>
public sealed class AdminWalletTierInput
{
    public Guid? Id { get; set; }
    public int Amount { get; set; }
    public int Credits { get; set; }
    public int Bonus { get; set; }
    public string? Label { get; set; }
    public bool IsPopular { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Currency { get; set; }
}

public sealed class AdminWalletTierReplaceRequest
{
    public List<AdminWalletTierInput> Tiers { get; set; } = new();
}

public sealed class AdminWalletTierValidationException : Exception
{
    public IReadOnlyList<string> Errors { get; }
    public AdminWalletTierValidationException(IReadOnlyList<string> errors)
        : base("Wallet top-up tier payload is invalid.")
    {
        Errors = errors;
    }
}

/// <summary>
/// Admin-facing CRUD for the DB-backed wallet top-up tier configuration.
/// Pairs with <see cref="WalletService.GetConfiguredTopUpTiers"/>, which
/// reads the active rows produced by this service and falls back to
/// <see cref="WalletBillingOptions.TopUpTiers"/> when none exist.
/// </summary>
public class AdminWalletTierService(
    LearnerDbContext db,
    IOptions<BillingOptions> billingOptions,
    TimeProvider timeProvider)
{
    public string DefaultCurrency
        => string.IsNullOrWhiteSpace(billingOptions.Value?.Wallet?.Currency)
            ? "AUD"
            : billingOptions.Value!.Wallet!.Currency!;

    /// <summary>
    /// Return every configured tier (active + inactive). When the DB is empty
    /// the appsettings fallback is projected so the admin UI shows the same
    /// values that learners currently see.
    /// </summary>
    public async Task<object> ListAsync(CancellationToken ct)
    {
        var rows = await db.WalletTopUpTierConfigs
            .AsNoTracking()
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Amount)
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            // Project the appsettings fallback so the admin UI surfaces the
            // currently-effective tier set even before any DB rows exist.
            var fallback = billingOptions.Value?.Wallet?.TopUpTiers ?? new List<WalletTopUpTierOption>();
            var currency = DefaultCurrency;
            return new
            {
                source = "appsettings",
                currency,
                tiers = fallback
                    .Select((t, i) => new
                    {
                        id = (Guid?)null,
                        amount = t.Amount,
                        credits = t.Credits,
                        bonus = t.Bonus,
                        totalCredits = t.Credits + t.Bonus,
                        label = t.Label,
                        isPopular = t.IsPopular,
                        displayOrder = i,
                        isActive = true,
                        currency,
                    })
                    .ToList<object>(),
            };
        }

        return new
        {
            source = "database",
            currency = DefaultCurrency,
            tiers = rows.Select(r => new
            {
                id = (Guid?)r.Id,
                amount = r.Amount,
                credits = r.Credits,
                bonus = r.Bonus,
                totalCredits = r.Credits + r.Bonus,
                label = r.Label,
                isPopular = r.IsPopular,
                displayOrder = r.DisplayOrder,
                isActive = r.IsActive,
                currency = r.Currency,
            }).ToList<object>(),
        };
    }

    /// <summary>
    /// Replace the entire wallet top-up tier set atomically. Existing rows
    /// not present by Id in the request are removed; matching rows are
    /// updated; new rows are inserted. Validation rejects negative or zero
    /// amounts, negative credits/bonus, and bad currency codes.
    /// </summary>
    public async Task<object> ReplaceAsync(
        string actorId,
        string actorName,
        AdminWalletTierReplaceRequest request,
        CancellationToken ct)
    {
        var defaultCurrency = DefaultCurrency;
        var errors = ValidatePayload(request, defaultCurrency);
        if (errors.Count > 0)
        {
            throw new AdminWalletTierValidationException(errors);
        }

        var now = timeProvider.GetUtcNow();
        var existing = await db.WalletTopUpTierConfigs.ToListAsync(ct);
        var keepIds = request.Tiers
            .Where(t => t.Id.HasValue)
            .Select(t => t.Id!.Value)
            .ToHashSet();

        // Remove rows that the caller dropped from the set.
        foreach (var row in existing.Where(r => !keepIds.Contains(r.Id)).ToList())
        {
            db.WalletTopUpTierConfigs.Remove(row);
        }

        var byId = existing.ToDictionary(x => x.Id);
        foreach (var input in request.Tiers)
        {
            var currency = string.IsNullOrWhiteSpace(input.Currency)
                ? defaultCurrency
                : input.Currency.Trim().ToUpperInvariant();

            if (input.Id.HasValue && byId.TryGetValue(input.Id.Value, out var row))
            {
                row.Amount = input.Amount;
                row.Credits = input.Credits;
                row.Bonus = input.Bonus;
                row.Label = string.IsNullOrWhiteSpace(input.Label) ? null : input.Label.Trim();
                row.IsPopular = input.IsPopular;
                row.DisplayOrder = input.DisplayOrder;
                row.IsActive = input.IsActive;
                row.Currency = currency;
                row.UpdatedAt = now;
                row.UpdatedBy = actorId;
            }
            else
            {
                db.WalletTopUpTierConfigs.Add(new WalletTopUpTierConfig
                {
                    Id = Guid.NewGuid(),
                    Amount = input.Amount,
                    Credits = input.Credits,
                    Bonus = input.Bonus,
                    Label = string.IsNullOrWhiteSpace(input.Label) ? null : input.Label.Trim(),
                    IsPopular = input.IsPopular,
                    DisplayOrder = input.DisplayOrder,
                    IsActive = input.IsActive,
                    Currency = currency,
                    CreatedAt = now,
                    UpdatedAt = now,
                    CreatedBy = actorId,
                    UpdatedBy = actorId,
                });
            }
        }

        await db.SaveChangesAsync(ct);

        // Audit: full collection PUT recorded as a single AuditEvent. Mirrors
        // the AdminService.LogAuditAsync convention for resourceType/Action.
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"AUD-{Guid.NewGuid():N}",
            OccurredAt = now,
            ActorId = actorId,
            ActorAuthAccountId = actorId,
            ActorName = actorName,
            Action = "wallet_tiers.replace",
            ResourceType = "WalletTopUpTierConfig",
            ResourceId = null,
            Details = $"Replaced wallet top-up tier set with {request.Tiers.Count} tier(s).",
        });
        await db.SaveChangesAsync(ct);

        return await ListAsync(ct);
    }

    private static List<string> ValidatePayload(AdminWalletTierReplaceRequest request, string defaultCurrency)
    {
        var errors = new List<string>();
        if (request.Tiers is null)
        {
            errors.Add("tiers: collection is required.");
            return errors;
        }

        for (var i = 0; i < request.Tiers.Count; i++)
        {
            var t = request.Tiers[i];
            var prefix = $"tiers[{i}]";
            if (t.Amount <= 0) errors.Add($"{prefix}.amount: must be greater than zero.");
            if (t.Credits < 0) errors.Add($"{prefix}.credits: must be zero or positive.");
            if (t.Bonus < 0) errors.Add($"{prefix}.bonus: must be zero or positive.");
            if (t.DisplayOrder < 0) errors.Add($"{prefix}.displayOrder: must be zero or positive.");
            if (t.Label is { Length: > 80 }) errors.Add($"{prefix}.label: max length is 80.");

            var currency = string.IsNullOrWhiteSpace(t.Currency) ? defaultCurrency : t.Currency.Trim();
            if (currency.Length != 3 || !currency.All(char.IsLetter))
            {
                errors.Add($"{prefix}.currency: must be a 3-letter ISO code.");
            }
        }

        var dupeAmounts = request.Tiers
            .GroupBy(t => t.Amount)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        foreach (var amount in dupeAmounts)
        {
            errors.Add($"tiers: duplicate amount {amount} is not allowed.");
        }

        return errors;
    }
}
