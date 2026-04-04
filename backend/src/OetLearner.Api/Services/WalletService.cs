using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

/// <summary>
/// Service for wallet ledger operations: credit, debit, balance queries, and top-up session creation.
/// Ensures all wallet mutations are recorded as append-only WalletTransaction entries.
/// </summary>
public class WalletService(LearnerDbContext db, PaymentGatewayService paymentGateways, PlatformLinkService platformLinks)
{
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

        var topUpTiers = new Dictionary<int, (int credits, int bonus)>
        {
            [10] = (10, 0),
            [25] = (28, 3),
            [50] = (60, 10),
            [100] = (130, 30)
        };

        if (!topUpTiers.TryGetValue(amountDollars, out var tier))
            throw new ArgumentException($"Invalid top-up amount: {amountDollars}. Valid: 10, 25, 50, 100.");

        var paymentGateway = paymentGateways.GetGateway(gateway);
        var intent = await paymentGateway.CreatePaymentIntentAsync(new CreatePaymentIntentRequest(
            UserId: userId,
            Amount: amountDollars,
            Currency: "AUD",
            ProductType: "wallet_top_up",
            ProductId: wallet.Id,
            Description: $"Wallet top-up: {tier.credits + tier.bonus} credits ({tier.credits} + {tier.bonus} bonus)",
            Metadata: new Dictionary<string, string>
            {
                ["wallet_id"] = wallet.Id,
                ["credits"] = tier.credits.ToString(),
                ["bonus"] = tier.bonus.ToString()
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
            Currency = "AUD",
            ProductType = "wallet_top_up",
            ProductId = wallet.Id,
            MetadataJson = JsonSupport.Serialize(new
            {
                credits = tier.credits,
                bonus = tier.bonus,
                totalCredits = tier.credits + tier.bonus
            }),
            CreatedAt = now,
            UpdatedAt = now
        });

        await db.SaveChangesAsync(ct);

        return new WalletTopUpSession(
            SessionId: intent.GatewayTransactionId,
            Gateway: gateway,
            AmountDollars: amountDollars,
            CreditsGranted: tier.credits,
            BonusCredits: tier.bonus,
            TotalCredits: tier.credits + tier.bonus,
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
