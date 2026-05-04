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
        // has created rows, those rows become authoritative. An empty active
        // set means top-ups are unavailable; appsettings are only the bootstrap
        // fallback before any DB rows exist. Sync EF read is intentional to
        // preserve this method's existing signature.
        try
        {
            var dbRows = db.WalletTopUpTierConfigs
                .AsNoTracking()
                .OrderBy(c => c.DisplayOrder)
                .ThenBy(c => c.Amount)
                .ToList();

            if (dbRows.Count > 0)
            {
                return dbRows
                    .Where(c => c.IsActive)
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
            // Money-path safety: once the endpoint cannot prove the DB tier
            // state, top-ups fail closed instead of resurrecting stale defaults.
            return Array.Empty<WalletTopUpTierOption>();
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
        => await CreditAsync(walletId, amount, transactionType, referenceType, referenceId, description, createdBy, idempotencyKey: null, ct);

    /// <summary>
    /// Atomically add <paramref name="amount"/> credits to <paramref name="walletId"/>,
    /// emitting an append-only <see cref="WalletTransaction"/> and an
    /// <see cref="AuditEvent"/>. The wallet is re-read inside the
    /// transaction so EF's concurrency token (<see cref="Wallet.LastUpdatedAt"/>)
    /// detects racing writers and forces a retry.
    /// </summary>
    /// <param name="idempotencyKey">
    /// Optional caller-supplied idempotency key. When provided, replays return the
    /// cached transaction instead of re-applying the credit.
    /// </param>
    public async Task<WalletTransaction> CreditAsync(
        string walletId,
        int amount,
        string transactionType,
        string? referenceType,
        string? referenceId,
        string? description,
        string? createdBy,
        string? idempotencyKey,
        CancellationToken ct)
    {
        ValidateMutationAmount(amount, nameof(amount));
        ValidateTransactionType(transactionType);

        var idempotencyScope = $"wallet-credit:{walletId}";
        var replay = await TryReplayIdempotentTransactionAsync(idempotencyScope, idempotencyKey, ct);
        if (replay is not null)
        {
            return replay;
        }

        var providerName = db.Database.ProviderName;
        var supportsTx = !string.Equals(providerName, "Microsoft.EntityFrameworkCore.InMemory", StringComparison.Ordinal);
        await using var tx = supportsTx ? await db.Database.BeginTransactionAsync(ct) : null;

        var wallet = await db.Wallets.FirstAsync(w => w.Id == walletId, ct);
        var now = DateTimeOffset.UtcNow;

        var newBalance = checked(wallet.CreditBalance + amount);
        wallet.CreditBalance = newBalance;
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
            IdempotencyKey = idempotencyKey,
            Description = description ?? $"Credit: +{amount} credits",
            CreatedBy = createdBy ?? "system",
            CreatedAt = now
        };

        db.WalletTransactions.Add(transaction);

        WriteAuditEvent(
            actorId: createdBy ?? "system",
            action: "wallet.credit",
            walletId: walletId,
            transaction: transaction,
            balanceAfter: wallet.CreditBalance,
            occurredAt: now);

        await PersistIdempotentReservationAsync(idempotencyScope, idempotencyKey, transaction, ct);

        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);

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
        => await DebitAsync(walletId, amount, transactionType, referenceType, referenceId, description, createdBy, idempotencyKey: null, ct);

    /// <summary>
    /// Atomically subtract <paramref name="amount"/> credits from
    /// <paramref name="walletId"/>. Refuses to drive the balance below zero
    /// even if the in-memory wallet was stale. Emits a
    /// <see cref="WalletTransaction"/> and an <see cref="AuditEvent"/>.
    /// </summary>
    public async Task<WalletTransaction> DebitAsync(
        string walletId,
        int amount,
        string transactionType,
        string? referenceType,
        string? referenceId,
        string? description,
        string? createdBy,
        string? idempotencyKey,
        CancellationToken ct)
    {
        ValidateMutationAmount(amount, nameof(amount));
        ValidateTransactionType(transactionType);

        var idempotencyScope = $"wallet-debit:{walletId}";
        var replay = await TryReplayIdempotentTransactionAsync(idempotencyScope, idempotencyKey, ct);
        if (replay is not null)
        {
            return replay;
        }

        var providerName = db.Database.ProviderName;
        var supportsTx = !string.Equals(providerName, "Microsoft.EntityFrameworkCore.InMemory", StringComparison.Ordinal);
        await using var tx = supportsTx ? await db.Database.BeginTransactionAsync(ct) : null;

        var wallet = await db.Wallets.FirstAsync(w => w.Id == walletId, ct);

        if (wallet.CreditBalance < amount)
        {
            throw new InvalidOperationException(
                $"Insufficient credits: balance={wallet.CreditBalance}, requested={amount}.");
        }

        var now = DateTimeOffset.UtcNow;
        var newBalance = wallet.CreditBalance - amount;
        // Belt-and-suspenders: never allow the persisted balance to go negative.
        if (newBalance < 0)
        {
            throw new InvalidOperationException(
                $"Wallet debit would produce a negative balance: balance={wallet.CreditBalance}, requested={amount}.");
        }
        wallet.CreditBalance = newBalance;
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
            IdempotencyKey = idempotencyKey,
            Description = description ?? $"Debit: -{amount} credits",
            CreatedBy = createdBy ?? "system",
            CreatedAt = now
        };

        db.WalletTransactions.Add(transaction);

        WriteAuditEvent(
            actorId: createdBy ?? "system",
            action: "wallet.debit",
            walletId: walletId,
            transaction: transaction,
            balanceAfter: wallet.CreditBalance,
            occurredAt: now);

        await PersistIdempotentReservationAsync(idempotencyScope, idempotencyKey, transaction, ct);

        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);

        return transaction;
    }

    // ── Hardening helpers ──────────────────────────────────────────────────

    private const int MaxMutationAmount = 1_000_000_000; // sanity bound

    private static void ValidateMutationAmount(int amount, string paramName)
    {
        if (amount <= 0)
        {
            throw new ArgumentException("Amount must be positive.", paramName);
        }
        if (amount > MaxMutationAmount)
        {
            throw new ArgumentException(
                $"Amount {amount} exceeds maximum allowed mutation ({MaxMutationAmount}).",
                paramName);
        }
    }

    private static void ValidateTransactionType(string transactionType)
    {
        if (string.IsNullOrWhiteSpace(transactionType))
        {
            throw new ArgumentException("Transaction type is required.", nameof(transactionType));
        }
        if (transactionType.Length > 32)
        {
            throw new ArgumentException("Transaction type must be 32 characters or fewer.", nameof(transactionType));
        }
    }

    private async Task<WalletTransaction?> TryReplayIdempotentTransactionAsync(
        string scope,
        string? key,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var record = await db.IdempotencyRecords.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Scope == scope && r.Key == key, ct);
        if (record is null)
        {
            return null;
        }

        // ResponseJson stores the transaction id; load and return it so the
        // caller observes the original mutation rather than applying a new one.
        if (Guid.TryParse(record.ResponseJson, out var txId))
        {
            return await db.WalletTransactions.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == txId, ct);
        }

        return null;
    }

    private async Task PersistIdempotentReservationAsync(
        string scope,
        string? key,
        WalletTransaction transaction,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        // Race-tolerant: rely on (Scope, Key) unique index — duplicate adds
        // surface as DbUpdateException and the caller should retry the read.
        var exists = await db.IdempotencyRecords
            .AnyAsync(r => r.Scope == scope && r.Key == key, ct);
        if (exists)
        {
            return;
        }

        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Id = $"wallet-idem-{Guid.NewGuid():N}",
            Scope = scope,
            Key = key,
            ResponseJson = transaction.Id.ToString(),
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    private void WriteAuditEvent(
        string actorId,
        string action,
        string walletId,
        WalletTransaction transaction,
        int balanceAfter,
        DateTimeOffset occurredAt)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"AUD-{Guid.NewGuid():N}",
            OccurredAt = occurredAt,
            ActorId = actorId,
            ActorAuthAccountId = null,
            ActorName = actorId,
            Action = action,
            ResourceType = "Wallet",
            ResourceId = walletId,
            Details = $"txId={transaction.Id} type={transaction.TransactionType} amount={transaction.Amount} balanceAfter={balanceAfter}",
        });
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
        string? idempotencyKey,
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
            throw ApiException.Validation(
                "invalid_amount",
                $"Choose a valid top-up amount ({validList}).",
                [new ApiFieldError("amount", "invalid", "Select one of the available top-up amounts.")]);
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
            CancelUrl: platformLinks.BuildWebUrl($"/billing?payment=cancelled&gateway={Uri.EscapeDataString(gateway)}"),
            IdempotencyKey: idempotencyKey), ct);

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
                providerIntentId = intent.ClientSecret,
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
