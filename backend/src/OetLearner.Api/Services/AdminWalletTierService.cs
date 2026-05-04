using System.Text.RegularExpressions;
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
    /// <summary>
    /// Stable kebab-case identifier (e.g. "starter", "best-value"). Required
    /// for new tiers; immutable once persisted.
    /// </summary>
    public string? Slug { get; set; }
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
/// <see cref="WalletBillingOptions.TopUpTiers"/> only before the DB set is created.
/// </summary>
public class AdminWalletTierService(
    LearnerDbContext db,
    IOptions<BillingOptions> billingOptions,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Strict kebab-case slug: lowercase ASCII letters / digits separated by single
    /// hyphens. No leading/trailing hyphen, no double hyphens. Length 2-64.
    /// </summary>
    private static readonly Regex SlugRegex = new(
        "^[a-z0-9]+(?:-[a-z0-9]+)*$",
        RegexOptions.Compiled);

    public string DefaultCurrency
        => string.IsNullOrWhiteSpace(billingOptions.Value?.Wallet?.Currency)
            ? "AUD"
            : billingOptions.Value!.Wallet!.Currency!.Trim().ToUpperInvariant();

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
                slug = r.Slug,
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

        var now = timeProvider.GetUtcNow();
        await using var transaction = string.Equals(db.Database.ProviderName, "Microsoft.EntityFrameworkCore.InMemory", StringComparison.Ordinal)
            ? null
            : await db.Database.BeginTransactionAsync(ct);

        var existing = await db.WalletTopUpTierConfigs.ToListAsync(ct);

        var errors = ValidatePayload(request, defaultCurrency, existing);
        if (errors.Count > 0)
        {
            throw new AdminWalletTierValidationException(errors);
        }

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
            var currency = defaultCurrency;
            var normalizedSlug = string.IsNullOrWhiteSpace(input.Slug)
                ? null
                : input.Slug.Trim().ToLowerInvariant();

            if (input.Id.HasValue && byId.TryGetValue(input.Id.Value, out var row))
            {
                // Slug is immutable once persisted: a non-null existing slug
                // wins over the inbound value, even if the caller tries to
                // change it. New slugs may be set on rows that previously
                // lacked one (legacy bootstrap data).
                if (!string.IsNullOrWhiteSpace(row.Slug))
                {
                    // Ignore inbound slug to enforce immutability.
                }
                else if (normalizedSlug is not null)
                {
                    row.Slug = normalizedSlug;
                }

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
                    Slug = normalizedSlug,
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
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
        }

        return await ListAsync(ct);
    }

    private static List<string> ValidatePayload(
        AdminWalletTierReplaceRequest request,
        string defaultCurrency,
        IReadOnlyList<WalletTopUpTierConfig> existingRows)
    {
        var errors = new List<string>();
        if (request.Tiers is null)
        {
            errors.Add("tiers: collection is required.");
            return errors;
        }

        if (request.Tiers.Count == 0)
        {
            errors.Add("tiers: at least one active tier is required.");
        }
        else if (!request.Tiers.Any(t => t.IsActive))
        {
            errors.Add("tiers: at least one tier must be active.");
        }

        var existingById = existingRows.ToDictionary(r => r.Id);

        for (var i = 0; i < request.Tiers.Count; i++)
        {
            var t = request.Tiers[i];
            var prefix = $"tiers[{i}]";
            if (t.Amount <= 0) errors.Add($"{prefix}.amount: must be greater than zero.");
            if (t.Amount > 1_000_000) errors.Add($"{prefix}.amount: exceeds maximum allowed (1,000,000).");
            if (t.Credits < 0) errors.Add($"{prefix}.credits: must be zero or positive.");
            if (t.Credits > 10_000_000) errors.Add($"{prefix}.credits: exceeds maximum allowed (10,000,000).");
            if (t.Bonus < 0) errors.Add($"{prefix}.bonus: must be zero or positive.");
            if (t.Bonus > 10_000_000) errors.Add($"{prefix}.bonus: exceeds maximum allowed (10,000,000).");
            if (t.DisplayOrder < 0) errors.Add($"{prefix}.displayOrder: must be zero or positive.");
            if (t.Label is { Length: > 80 }) errors.Add($"{prefix}.label: max length is 80.");

            var currency = string.IsNullOrWhiteSpace(t.Currency) ? defaultCurrency : t.Currency.Trim();
            if (currency.Length != 3 || !currency.All(char.IsLetter))
            {
                errors.Add($"{prefix}.currency: must be a 3-letter ISO code.");
            }
            else if (!string.Equals(currency, defaultCurrency, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{prefix}.currency: must match the platform wallet currency ({defaultCurrency}).");
            }

            // Slug rules: required for new rows; must be valid kebab-case;
            // immutable for existing rows once a non-null slug is persisted.
            var slugInput = string.IsNullOrWhiteSpace(t.Slug) ? null : t.Slug.Trim().ToLowerInvariant();
            var existingRow = t.Id.HasValue && existingById.TryGetValue(t.Id.Value, out var er) ? er : null;
            var existingSlug = existingRow?.Slug;

            if (existingRow is null)
            {
                // New row. Slug is optional but must be valid kebab-case if provided.
                if (slugInput is not null && (slugInput.Length is < 2 or > 64 || !SlugRegex.IsMatch(slugInput)))
                {
                    errors.Add($"{prefix}.slug: must be lowercase kebab-case (a-z, 0-9, single hyphens), 2-64 chars.");
                }
            }
            else if (!string.IsNullOrWhiteSpace(existingSlug))
            {
                // Existing row with a slug already set: reject any attempt to change it.
                if (slugInput is not null && !string.Equals(slugInput, existingSlug, StringComparison.Ordinal))
                {
                    errors.Add($"{prefix}.slug: is immutable; expected '{existingSlug}', got '{slugInput}'.");
                }
            }
            else if (slugInput is not null)
            {
                // Existing row without a slug being given one for the first time.
                if (slugInput.Length is < 2 or > 64 || !SlugRegex.IsMatch(slugInput))
                {
                    errors.Add($"{prefix}.slug: must be lowercase kebab-case (a-z, 0-9, single hyphens), 2-64 chars.");
                }
            }
        }

        // Duplicate amount within payload.
        var dupeAmounts = request.Tiers
            .GroupBy(t => t.Amount)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        foreach (var amount in dupeAmounts)
        {
            errors.Add($"tiers: duplicate amount {amount} is not allowed.");
        }

        // Duplicate slug within payload (compare against effective slug:
        // payload value if provided, otherwise the persisted value).
        var effectiveSlugs = request.Tiers
            .Select(t =>
            {
                var s = string.IsNullOrWhiteSpace(t.Slug) ? null : t.Slug.Trim().ToLowerInvariant();
                if (s is not null) return s;
                return t.Id.HasValue && existingById.TryGetValue(t.Id.Value, out var er) ? er.Slug : null;
            })
            .Where(s => s is not null)
            .ToList();
        var dupeSlugs = effectiveSlugs
            .GroupBy(s => s!)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        foreach (var slug in dupeSlugs)
        {
            errors.Add($"tiers: duplicate slug '{slug}' is not allowed.");
        }

        // Strictly ascending DisplayOrder among active tiers (sorted by amount).
        // Catches ranges that would render in a confusing/overlapping way.
        var activeOrdered = request.Tiers
            .Where(t => t.IsActive)
            .OrderBy(t => t.Amount)
            .Select(t => t.DisplayOrder)
            .ToList();
        for (var i = 1; i < activeOrdered.Count; i++)
        {
            if (activeOrdered[i] <= activeOrdered[i - 1])
            {
                errors.Add("tiers: active tiers must have strictly ascending displayOrder when sorted by amount (no overlapping ranges).");
                break;
            }
        }

        return errors;
    }
}
