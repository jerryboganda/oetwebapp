using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// Owner rule: a full AI mock costs exactly 4 credits, each card held once. The live voice control plane
/// (preflight, provider session mints, a reconnect, failover to the other provider, the saved turns and the
/// transcript) must never reach the credit ledger, so none of them can charge a card a second time.
/// </summary>
public sealed class SpeakingLiveVoiceCreditSafetyTests
{
    private const string ExamId = "spx_credit_safety";

    [Fact]
    public async Task LiveVoiceMintReconnectAndFailover_LeaveEveryCreditRowUntouched()
    {
        using var rig = LiveVoiceTestKit.Create();
        var now = rig.Clock.GetUtcNow();
        var session = await LiveVoiceTestKit.SeedReadySessionAsync(
            rig.Db, now, mode: SpeakingSessionMode.AiExam, examSessionId: ExamId);
        await SeedCreditRowsAsync(rig.Db, session.UserId, now);
        var before = await CreditStateAsync(rig.Db);
        // Guard against an empty comparison: the exam's two holds are in the snapshot being compared.
        Assert.Contains($"exam:{ExamId}:cardA", before, StringComparison.Ordinal);
        Assert.Contains($"exam:{ExamId}:cardB", before, StringComparison.Ordinal);

        await rig.Service.GetPreflightAsync(session.UserId, session.SessionId, null, CancellationToken.None);
        // First provider session, a reconnect to the same provider, then failover to the other provider.
        var offer = new LiveVoiceOpenAiOfferRequest(LiveVoiceTestKit.OfferSdp);
        await rig.Service.CreateOpenAiOfferAsync(session.UserId, session.SessionId, offer, CancellationToken.None);
        await rig.Service.CreateOpenAiOfferAsync(session.UserId, session.SessionId, offer, CancellationToken.None);
        var failover = await rig.Service.CreateGeminiTokenAsync(session.UserId, session.SessionId, CancellationToken.None);
        // The browser saves each turn as it goes, then the whole transcript at the end of the card.
        await rig.Service.PersistTurnAsync(
            session.UserId,
            session.SessionId,
            new LiveVoiceTurnRequest("gemini", failover.ProviderSessionId, "Hello doctor", "Hello", "voice-turn:1", 1),
            CancellationToken.None);
        await rig.Service.PersistTranscriptAsync(
            session.UserId,
            session.SessionId,
            new LiveVoiceTranscriptRequest(
                "gemini",
                failover.ProviderSessionId,
                new[] { new LiveVoiceTranscriptSegment("candidate", 0, 1000, "Hello doctor") }),
            CancellationToken.None);

        // The control plane really ran: three provider sessions (the per-card limit) and a saved transcript.
        Assert.Equal(3, await rig.Db.SpeakingPatientTurns.CountAsync(t =>
            t.SessionId == session.SessionId && t.Role == LiveVoiceService.LiveVoiceSessionRole));
        Assert.True(await rig.Db.SpeakingTranscripts.AnyAsync(t => t.SpeakingSessionId == session.SessionId && t.IsLatest));
        // ...and every credit table is exactly as it was.
        Assert.Equal(before, await CreditStateAsync(rig.Db));
    }

    /// <summary>An account, a lot and the two card holds of one AI exam, as a funded exam leaves them.</summary>
    private static async Task SeedCreditRowsAsync(LearnerDbContext db, string userId, DateTimeOffset now)
    {
        db.AiPackageCreditAccounts.Add(new AiPackageCreditAccount
        {
            Id = "aipkg-acct-safety",
            UserId = userId,
            SpeakingOnlyCredits = 0,
            ExpiresAt = now.AddDays(30),
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.AiPackageCreditLots.Add(new AiPackageCreditLot
        {
            Id = "aipkg-lot-safety",
            UserId = userId,
            AccountId = "aipkg-acct-safety",
            PackageType = "speaking",
            SpeakingOnlyCredits = 0,
            ExpiresAt = now.AddDays(30),
            CreatedAt = now,
        });
        foreach (var slot in new[] { "cardA", "cardB" })
        {
            var reference = $"exam:{ExamId}:{slot}";
            db.AiPackageCreditTransactions.Add(new AiPackageCreditTransaction
            {
                Id = $"aipkg-tx-{slot}",
                UserId = userId,
                AccountId = "aipkg-acct-safety",
                PackageType = "speaking",
                SpeakingOnlyCreditsDelta = -2,
                Reason = AiPackageCreditReason.GradingDeduct,
                ReferenceId = reference,
                CreatedAt = now,
            });
            db.AiOperations.Add(new AiOperation
            {
                Id = $"op-{slot}",
                Module = "speaking",
                FeatureCode = AiFeatureCodes.SpeakingGrade,
                UserId = userId,
                IdempotencyKey = reference,
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.AiCreditReservations.Add(new AiCreditReservation
            {
                Id = $"res-{slot}",
                OperationId = $"op-{slot}",
                UserId = userId,
                BucketKind = "speaking",
                Units = 2,
                State = AiCreditReservationState.Reserved,
                BusinessReference = reference,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        await db.SaveChangesAsync();
    }

    /// <summary>Every persisted column of the credit ledger and the holds, as one comparable string.</summary>
    private static async Task<string> CreditStateAsync(LearnerDbContext db)
    {
        var accounts = await db.AiPackageCreditAccounts.AsNoTracking().OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.SpeakingOnlyCredits, a.SharedCredits, a.FlexibleCredits, a.MockExamsRemaining })
            .ToListAsync();
        var lots = await db.AiPackageCreditLots.AsNoTracking().OrderBy(l => l.Id)
            .Select(l => new { l.Id, l.SpeakingOnlyCredits, l.SharedCredits, l.FlexibleCredits, l.Expired })
            .ToListAsync();
        var transactions = await db.AiPackageCreditTransactions.AsNoTracking().OrderBy(t => t.Id)
            .Select(t => new { t.Id, t.Reason, t.ReferenceId, t.SpeakingOnlyCreditsDelta, t.SharedCreditsDelta, t.FlexibleCreditsDelta })
            .ToListAsync();
        var reservations = await db.AiCreditReservations.AsNoTracking().OrderBy(r => r.Id)
            .Select(r => new { r.Id, r.BusinessReference, r.State, r.Units })
            .ToListAsync();
        return JsonSerializer.Serialize(new { accounts, lots, transactions, reservations });
    }
}
