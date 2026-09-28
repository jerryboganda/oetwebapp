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

    // ── Wallet Transactions ──

    public object GetWalletTopUpTiers()
    {
        var tiers = walletService.GetConfiguredTopUpTiers();
        return new
        {
            currency = walletService.GetWalletCurrency(),
            tiers = tiers.Select(t => new
            {
                amount = t.Amount,
                credits = t.Credits,
                bonus = t.Bonus,
                totalCredits = t.Credits + t.Bonus,
                label = string.IsNullOrWhiteSpace(t.Label) ? $"${t.Amount}" : t.Label,
                isPopular = t.IsPopular,
            }).ToList(),
        };
    }

    public async Task<object> GetWalletTransactionsAsync(string userId, int limit, CancellationToken ct)
    {
        var wallet = await db.Wallets.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (wallet is null)
        {
            return new { balance = 0, transactions = Array.Empty<object>() };
        }

        var maxTransactions = Math.Min(limit, 100);
        var walletTransactions = await db.WalletTransactions.AsNoTracking()
            .Where(x => x.WalletId == wallet.Id)
            .ToListAsync(ct);

        var transactions = walletTransactions
            .OrderByDescending(x => x.CreatedAt)
            .Take(maxTransactions)
            .Select(x => new
            {
                id = x.Id,
                type = x.TransactionType,
                amount = x.Amount,
                balanceAfter = x.BalanceAfter,
                referenceType = x.ReferenceType,
                referenceId = x.ReferenceId,
                description = x.Description,
                createdAt = x.CreatedAt
            })
            .ToList();

        return new
        {
            balance = wallet.CreditBalance,
            lastUpdatedAt = wallet.LastUpdatedAt,
            transactions
        };
    }

    public async Task<object> CreateWalletTopUpAsync(string userId, WalletTopUpRequest request, CancellationToken ct)
    {
        await EnsureUserAsync(userId, ct);
        await EnsureLearnerMutationAllowedAsync(userId, ct);

        var gateway = string.IsNullOrWhiteSpace(request.Gateway)
            ? PaymentGatewayNames.Whop
            : request.Gateway.Trim().ToLowerInvariant();
        await EnsureCheckoutGatewayAsync(gateway, ct);

        var configuredTiers = walletService.GetConfiguredTopUpTiers();
        if (!configuredTiers.Any(t => t.Amount == request.Amount))
        {
            var validList = configuredTiers.Count == 0
                ? "currently unavailable"
                : string.Join(", ", configuredTiers.Select(t => $"${t.Amount}"));
            throw ApiException.Validation(
                "invalid_amount",
                $"Choose a valid top-up amount ({validList}).",
                [new ApiFieldError("amount", "invalid", "Select one of the available top-up amounts.")]);
        }

        var idempotencyKey = NormalizeIdempotencyKey(request.IdempotencyKey);
        var idempotencyRequestHash = idempotencyKey is null
            ? null
            : ComputeIdempotencyRequestHash(new { userId, request.Amount, gateway });
        if (idempotencyKey is not null && idempotencyRequestHash is not null)
        {
            var reservation = await ReservePaymentIdempotencyAsync("wallet-top-up", idempotencyKey, userId, idempotencyRequestHash, ct);
            if (!reservation.ShouldProcess)
            {
                return reservation.CachedResponse!;
            }
        }

        var providerRequestReturned = false;
        var idempotencyCompleted = false;
        object? idempotencyResponse = null;
        try
        {
        var session = await walletService.CreateTopUpSessionAsync(userId, request.Amount, gateway, idempotencyKey, ct);
        providerRequestReturned = true;
        var response = new
        {
            sessionId = session.SessionId,
            gateway = session.Gateway,
            amountAud = session.AmountDollars,
            creditsGranted = session.CreditsGranted,
            bonusCredits = session.BonusCredits,
            totalCredits = session.TotalCredits,
            checkoutUrl = session.CheckoutUrl,
            status = "pending_payment",
            expiresAt = session.ExpiresAt
        };

        idempotencyResponse = response;

        if (idempotencyKey is not null && idempotencyRequestHash is not null)
        {
            await CompletePaymentIdempotencyAsync("wallet-top-up", idempotencyKey, userId, idempotencyRequestHash, response, ct);
        }

        await db.SaveChangesAsync(ct);
        idempotencyCompleted = true;

        // Audit the successful top-up session creation so billing operators can
        // reconcile against payment-gateway events later.
        db.BillingEvents.Add(new BillingEvent
        {
            Id = $"bill-evt-{Guid.NewGuid():N}",
            UserId = userId,
            EventType = "wallet_top_up_session_created",
            EntityType = "WalletTopUpSession",
            EntityId = session.SessionId,
            PayloadJson = JsonSupport.Serialize(new
            {
                gateway = session.Gateway,
                amountDollars = session.AmountDollars,
                creditsGranted = session.CreditsGranted,
                bonusCredits = session.BonusCredits,
                totalCredits = session.TotalCredits
            }),
            OccurredAt = DateTimeOffset.UtcNow
        });
        await RecordEventAsync(userId, "wallet_top_up_started", new
        {
            sessionId = session.SessionId,
            gateway = session.Gateway,
            amountDollars = session.AmountDollars,
            totalCredits = session.TotalCredits
        }, ct);

        await db.SaveChangesAsync(ct);

        // Learners observe refund/payment status through billing invoices; wallet
        // top-up session creation intentionally returns only the checkout contract.
        return response;
        }
        catch
        {
            if (idempotencyKey is not null && idempotencyRequestHash is not null && !idempotencyCompleted)
            {
                if (idempotencyResponse is not null)
                {
                    await TryCompletePaymentIdempotencyAsync("wallet-top-up", idempotencyKey, userId, idempotencyRequestHash, idempotencyResponse, ct);
                }
                else if (!providerRequestReturned)
                {
                    await RemovePaymentIdempotencyReservationAsync("wallet-top-up", idempotencyKey, ct);
                }
            }

            throw;
        }
    }
}
