using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Conversation;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.Pronunciation;

namespace OetLearner.Api.Tests;

public class PronunciationConversationEntitlementServiceTests
{
    [Fact]
    public async Task PronunciationCheckAsync_ActiveSponsorship_BypassesFreeAttemptLimit()
    {
        await using var db = new LearnerDbContext(NewInMemoryOptions());
        var learnerId = "learner-sponsored-pronunciation";
        var now = DateTimeOffset.UtcNow;
        db.Sponsorships.Add(ActiveSponsorship(learnerId));
        db.PronunciationAttempts.AddRange(
            PronunciationAttempt("pron-attempt-1", learnerId, now.AddDays(-2)),
            PronunciationAttempt("pron-attempt-2", learnerId, now.AddDays(-1)));
        await db.SaveChangesAsync();
        var service = new PronunciationEntitlementService(
            db,
            new LearnerEntitlementResolver(db),
            Options.Create(new PronunciationOptions { FreeTierWeeklyAttemptLimit = 1, FreeTierWindowDays = 7 }));

        var result = await service.CheckAsync(learnerId, default);

        Assert.True(result.Allowed);
        Assert.Equal("paid", result.Tier);
        Assert.Equal(int.MaxValue, result.Remaining);
        Assert.Equal(int.MaxValue, result.LimitPerWindow);
        Assert.Null(result.ResetAt);
        Assert.Equal("Sponsor seat — unlimited pronunciation practice.", result.Reason);
    }

    [Fact]
    public async Task ConversationCheckAsync_ActiveSponsorship_BypassesFreeSessionLimit()
    {
        await using var db = new LearnerDbContext(NewInMemoryOptions());
        var learnerId = "learner-sponsored-conversation";
        var now = DateTimeOffset.UtcNow;
        db.Sponsorships.Add(ActiveSponsorship(learnerId));
        db.ConversationSessions.AddRange(
            ConversationSession("conversation-session-1", learnerId, now.AddDays(-2)),
            ConversationSession("conversation-session-2", learnerId, now.AddDays(-1)));
        await db.SaveChangesAsync();
        var service = new ConversationEntitlementService(
            db,
            new LearnerEntitlementResolver(db),
            new StaticConversationOptionsProvider(new ConversationOptions
            {
                Enabled = true,
                FreeTierSessionsLimit = 1,
                FreeTierWindowDays = 7
            }));

        var result = await service.CheckAsync(learnerId, default);

        Assert.True(result.Allowed);
        Assert.Equal("paid", result.Tier);
        Assert.Equal(int.MaxValue, result.Remaining);
        Assert.Equal(int.MaxValue, result.LimitPerWindow);
        Assert.Null(result.ResetAt);
        Assert.Equal("Sponsor seat — unlimited practice.", result.Reason);
    }

    private static DbContextOptions<LearnerDbContext> NewInMemoryOptions()
        => new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static Sponsorship ActiveSponsorship(string learnerId)
        => new()
        {
            Id = Guid.NewGuid(),
            SponsorUserId = $"sponsor-{learnerId}",
            LearnerUserId = learnerId,
            LearnerEmail = $"{learnerId}@example.test",
            Status = "Active",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10)
        };

    private static PronunciationAttempt PronunciationAttempt(string id, string learnerId, DateTimeOffset createdAt)
        => new()
        {
            Id = id,
            UserId = learnerId,
            DrillId = $"drill-{id}",
            Status = "completed",
            CreatedAt = createdAt
        };

    private static ConversationSession ConversationSession(string id, string learnerId, DateTimeOffset createdAt)
        => new()
        {
            Id = id,
            UserId = learnerId,
            ExamTypeCode = "oet",
            TaskTypeCode = "oet-roleplay",
            CreatedAt = createdAt
        };

    private sealed class StaticConversationOptionsProvider(ConversationOptions options) : IConversationOptionsProvider
    {
        public Task<ConversationOptions> GetAsync(CancellationToken ct = default) => Task.FromResult(options);

        public void Invalidate()
        {
        }
    }
}