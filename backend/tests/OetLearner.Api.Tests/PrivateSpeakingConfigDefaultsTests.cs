using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

public sealed class PrivateSpeakingConfigDefaultsTests
{
    [Fact]
    public async Task GetConfigAsync_NewConfig_AppliesPdfMandatedDefaults()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        var config = await service.GetConfigAsync(CancellationToken.None);

        Assert.Equal("GBP", config.Currency);
        Assert.Equal(48, config.CancellationWindowHours);
        Assert.Equal(24, config.RescheduleFreeWindowHours);
        Assert.Equal(50, config.RescheduleSameDayPenaltyPercent);
        Assert.Equal("[1440, 60, 15]", config.ReminderOffsetsMinutesJson);

        Assert.False(string.IsNullOrWhiteSpace(config.CancellationPolicyText));
        Assert.False(string.IsNullOrWhiteSpace(config.BookingPolicyText));
        Assert.Contains("48 hours", config.CancellationPolicyText!);
        Assert.Contains("50%", config.BookingPolicyText!);
    }

    private static LearnerDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new LearnerDbContext(options);
    }

    // GetConfigAsync only touches db + timeProvider, so the remaining
    // collaborators are never invoked and can be passed as null.
    private static PrivateSpeakingService CreateService(LearnerDbContext db)
        => new(
            db,
            notificationService: null!,
            zoomService: null!,
            calendarService: null!,
            entitlementResolver: null!,
            stripeService: null!,
            timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 06, 06, 12, 0, 0, TimeSpan.Zero)),
            logger: NullLogger<PrivateSpeakingService>.Instance);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
