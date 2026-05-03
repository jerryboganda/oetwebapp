using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

/// <summary>
/// Service for wallet ledger operations: credit, debit, balance queries, and top-up session creation.
/// Ensures all wallet mutations are recorded as append-only WalletTransaction entries.
/// </summary>
public class WalletService(
    LearnerDbContext db,
    PaymentGatewayService paymentGateways,
    PlatformLinkService platformLinks,
    IOptions<BillingOptions> billingOptions)
{
    public IReadOnlyList<WalletTopUpTierOption> GetConfiguredTopUpTiers()
    {
        // DB-backed admin override: when the admin "Wallet Top-Up Tiers" CMS
        // has at least one *active* row, those replace the appsettings tiers
        // entirely. The page is at /admin/billing/wallet-tiers and writes go
        // through AdminWalletTierService → WalletTopUpTierConfigs. Sync EF
        // read is intentional to preserve this method's existing signature.
        try
        {
            var dbTiers = db.WalletTopUpTierConfigs
                .AsNoTracking()
                .Where(c => c.IsActive)
                .OrderBy(c => c.DisplayOrder)
                .ThenBy(c => c.Amount)
                .ToList();

            if (dbTiers.Count > 0)
            {
                return dbTiers
                    .Where(t => t.Amount > 0 && t.Credits >= 0 && t.Bonus >= 0)
                    .Select(t => new WalletTopUpTierOption
                    {
                        Amount = t.Amount,
                        Credits = t.Credits,
                        Bonus = t.Bonus,
                        Label = t.Label,
                        IsPopular = t.IsPopular,
                    })
                    .ToList();
            }
        }
        catch
        {
            // Fall through to appsettings fallback if the table isn't reachable
            // (e.g. migration hasn't applied yet). Behaviour stays unchanged.
        }

        var tiers = billingOptions.Value?.Wallet?.TopUpTiers;
        if (tiers is null || tiers.Count == 0)
        {
            return Array.Empty<WalletTopUpTierOption>();
        }

        return tiers
            .Where(t => t.Amount > 0 && t.Credits >= 0 && t.Bonus >= 0)
            .OrderBy(t => t.Amount)
            .ToList();
    }

    public string GetWalletCurrency()
        => string.IsNullOrWhiteSpace(billingOptions.Value?.Wallet?.Currency)
            ? "AUD"
            : billingOptions.Value!.Wallet!.Currency!;

    public async Task<WalletBalance> GetBalanceAsync(string userId, CancellationToken ct)
    {
        var wallet = await db.Wallets.AsNoTracking()
            .FirstOrDefaultAsync(w => w.UserId == userId, ct);

        if (wallet is null)
            return new WalletBalance(0, DateTimeOffset.UtcNow);

        return new WalletBalance(wallet.CreditBalance, wallet.LastUpdatedAt);
    }

    public async Task<WalletTransaction> CreditAsync(
        string walletId,
        int amount,
        string transactionType,
        string? referenceType,
        string? referenceId,
        string? description,
        string? createdBy,
        CancellationToken ct)
    {
        if (amount <= 0)
            throw new ArgumentException("Credit amount must be positive.", nameof(amount));

        var wallet = await db.Wallets.FirstAsync(w => w.Id == walletId, ct);
        var now = DateTimeOffset.UtcNow;

        wallet.CreditBalance += amount;
        wallet.LastUpdatedAt = now;

        var transaction = new WalletTransaction
        {
            Id = Guid.NewGuid(),
            WalletId = walletId,
            TransactionType = transactionType,
            Amount = amount,
            BalanceAfter = wallet.CreditBalance,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Description = description ?? $"Credit: +{amount} credits",
            CreatedBy = createdBy ?? "system",
            CreatedAt = now
        };

        db.WalletTransactions.Add(transaction);
        await db.SaveChangesAsync(ct);

        return transaction;
    }

    public async Task<WalletTransaction> DebitAsync(
        string walletId,
        int amount,
        string transactionType,
        string? referenceType,
        string? referenceId,
        string? description,
        string? createdBy,
        CancellationToken ct)
    {
        if (amount <= 0)
            throw new ArgumentException("Debit amount must be positive.", nameof(amount));

        var wallet = await db.Wallets.FirstAsync(w => w.Id == walletId, ct);

        if (wallet.CreditBalance < amount)
            throw new InvalidOperationException($"Insufficient credits: balance={wallet.CreditBalance}, requested={amount}.");

        var now = DateTimeOffset.UtcNow;

        wallet.CreditBalance -= amount;
        wallet.LastUpdatedAt = now;

        var transaction = new WalletTransaction
        {
            Id = Guid.NewGuid(),
            WalletId = walletId,
            TransactionType = transactionType,
            Amount = -amount,
            BalanceAfter = wallet.CreditBalance,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Description = description ?? $"Debit: -{amount} credits",
            CreatedBy = createdBy ?? "system",
            CreatedAt = now
        };

        db.WalletTransactions.Add(transaction);
        await db.SaveChangesAsync(ct);

        return transaction;
    }

    public async Task<List<WalletTransaction>> GetTransactionHistoryAsync(
        string walletId,
        int limit,
        CancellationToken ct)
    {
        return await db.WalletTransactions.AsNoTracking()
            .Where(t => t.WalletId == walletId)
            .OrderByDescending(t => t.CreatedAt)
            .Take(Math.Min(limit, 100))
            .ToListAsync(ct);
    }

    public async Task<WalletTopUpSession> CreateTopUpSessionAsync(
        string userId,
        int amountDollars,
        string gateway,
        CancellationToken ct)
    {
        var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, ct)
            ?? throw new InvalidOperationException("Wallet not found.");

        var configuredTiers = GetConfiguredTopUpTiers();
        var tier = configuredTiers.FirstOrDefault(t => t.Amount == amountDollars);
        if (tier is null)
        {
            var validList = configuredTiers.Count == 0
                ? "(none configured)"
                : string.Join(", ", configuredTiers.Select(t => t.Amount));
            throw new ArgumentException($"Invalid top-up amount: {amountDollars}. Valid: {validList}.");
        }

        var currency = GetWalletCurrency();
        var totalCredits = tier.Credits + tier.Bonus;

        var paymentGateway = paymentGateways.GetGateway(gateway);
        var intent = await paymentGateway.CreatePaymentIntentAsync(new CreatePaymentIntentRequest(
            UserId: userId,
            Amount: amountDollars,
            Currency: currency,
            ProductType: "wallet_top_up",
            ProductId: wallet.Id,
            Description: $"Wallet top-up: {totalCredits} credits ({tier.Credits} + {tier.Bonus} bonus)",
            Metadata: new Dictionary<string, string>
            {
                ["wallet_id"] = wallet.Id,
                ["credits"] = tier.Credits.ToString(),
                ["bonus"] = tier.Bonus.ToString()
            },
            SuccessUrl: platformLinks.BuildWebUrl($"/billing?payment=success&gateway={Uri.EscapeDataString(gateway)}"),
            CancelUrl: platformLinks.BuildWebUrl($"/billing?payment=cancelled&gateway={Uri.EscapeDataString(gateway)}")), ct);

        var now = DateTimeOffset.UtcNow;

        db.PaymentTransactions.Add(new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            LearnerUserId = userId,
            Gateway = gateway,
            GatewayTransactionId = intent.GatewayTransactionId,
            TransactionType = "wallet_top_up",
            Status = "pending",
            Amount = amountDollars,
            Currency = currency,
            ProductType = "wallet_top_up",
            ProductId = wallet.Id,
            MetadataJson = JsonSupport.Serialize(new
            {
                credits = tier.Credits,
                bonus = tier.Bonus,
                totalCredits
            }),
            CreatedAt = now,
            UpdatedAt = now
        });

        await db.SaveChangesAsync(ct);

        return new WalletTopUpSession(
            SessionId: intent.GatewayTransactionId,
            Gateway: gateway,
            AmountDollars: amountDollars,
            CreditsGranted: tier.Credits,
            BonusCredits: tier.Bonus,
            TotalCredits: totalCredits,
            CheckoutUrl: intent.CheckoutUrl,
            ExpiresAt: now.AddMinutes(30));
    }
}

public record WalletBalance(int CreditBalance, DateTimeOffset LastUpdatedAt);

public record WalletTopUpSession(
    string SessionId,
    string Gateway,
    int AmountDollars,
    int CreditsGranted,
    int BonusCredits,
    int TotalCredits,
    string CheckoutUrl,
    DateTimeOffset ExpiresAt);
